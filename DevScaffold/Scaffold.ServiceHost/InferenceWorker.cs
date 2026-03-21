/*

   Copyright 2026 Viktor Vidman

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.

 */

using Scaffold.Agent.Protocol;
using System.Text;

namespace Scaffold.ServiceHost;

/// <summary>
/// Inference futtatásáért felelős komponens.
///
/// Backend-agnosztikus: nem tudja hogy LLamaSharp vagy API futtatja
/// az inference-t – ezt az IInferenceBackend elrejti.
///
/// Egyszerre csak egy inference futhat – a SemaphoreSlim
/// biztosítja ezt, és a WaitForCompletionAsync is ezt használja
/// a graceful shutdown implementálásához.
///
/// Output path meghatározás:
/// - Ha az InferRequest.OutputFolder meg van adva, azt használja (CLI határozza meg)
/// - Ha üres, fallback: _outputBasePath / stepId / (ServiceHost saját logikája)
///
/// Felelősségei:
/// - Backend lekérése a ModelCache-ből (lazy betöltéssel)
/// - Inference futtatása a backenden keresztül
/// - Kimenet fájlba írása
/// - Periodikus InferenceProgressEvent küldése
/// - InferenceCompletedEvent / InferenceFailedEvent / InferenceCancelledEvent küldése
/// </summary>
public class InferenceWorker
{
    private readonly ModelCache _modelCache;
    private readonly EventPublisher _eventPublisher;
    private readonly string _outputBasePath;

    // Az aktív inference CancellationTokenSource-a
    private CancellationTokenSource? _activeInferenceCts;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(30);

    public InferenceWorker(
        ModelCache modelCache,
        EventPublisher eventPublisher,
        string outputBasePath)
    {
        _modelCache = modelCache;
        _eventPublisher = eventPublisher;
        _outputBasePath = outputBasePath;
    }

    /// <summary>
    /// Elindítja az inference futást.
    /// Ha már fut egy inference, InvalidOperationException-t dob.
    /// </summary>
    public async Task RunAsync(
        InferRequest request,
        CancellationToken serviceCancellationToken = default)
    {
        if (!await _inferenceLock.WaitAsync(0))
            throw new InvalidOperationException(
                $"Már fut egy inference. Kérés: {request.RequestId}");

        _activeInferenceCts = CancellationTokenSource.CreateLinkedTokenSource(
            serviceCancellationToken);

        try
        {
            await ExecuteInferenceAsync(request, _activeInferenceCts.Token);
        }
        finally
        {
            _activeInferenceCts.Dispose();
            _activeInferenceCts = null;
            _inferenceLock.Release();
        }
    }

    /// <summary>
    /// Megszakítja az aktív inference-t.
    /// Ha nincs aktív inference, no-op.
    /// </summary>
    public void Cancel()
    {
        _activeInferenceCts?.Cancel();
    }

    /// <summary>
    /// Megvárja hogy az aktív inference befejezzen.
    /// A graceful shutdown (force = false) esetén hívja a CommandDispatcher.
    /// Ha nincs aktív inference, azonnal visszatér.
    /// </summary>
    public async Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        // A lock megszerzése jelenti hogy nincs aktív inference
        await _inferenceLock.WaitAsync(cancellationToken);
        _inferenceLock.Release();
    }

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task ExecuteInferenceAsync(
         InferRequest request,
         CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var outputFilePath = BuildOutputPath(request);

        await _eventPublisher.PublishInferenceStartedAsync(
            request.RequestId,
            request.StepId,
            request.ModelAlias,
            cancellationToken);

        try
        {
            var backend = await _modelCache.GetOrLoadAsync(
                request.RequestId,
                request.ModelAlias,
                cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            // CountingTextWriter interceptálja a token írásokat –
            // a progress timer olvassa a számlálót anélkül hogy a backend
            // implementációt módosítani kellene.
            using var progressTimer = new PeriodicTimer(ProgressInterval);

            uint tokensGenerated;
            await using (var fileWriter = new StreamWriter(outputFilePath, append: false))
            {
                var countingWriter = new CountingTextWriter(fileWriter);

                var progressTask = RunProgressTimerAsync(
                    request.RequestId,
                    request.StepId,
                    startTime,
                    countingWriter,
                    progressTimer,
                    cancellationToken);

                tokensGenerated = await backend.RunAsync(request, countingWriter, cancellationToken);

                progressTimer.Dispose();
                await progressTask;
            }

            var elapsed = (uint)(DateTime.UtcNow - startTime).TotalSeconds;

            await _eventPublisher.PublishInferenceCompletedAsync(
                request.RequestId,
                request.StepId,
                outputFilePath,
                elapsed,
                tokensGenerated,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(outputFilePath))
                File.Delete(outputFilePath);

            await _eventPublisher.PublishInferenceCancelledAsync(
                request.RequestId,
                request.StepId);
        }
        catch (Exception ex)
        {
            await _eventPublisher.PublishInferenceFailedAsync(
                request.RequestId,
                request.StepId,
                ex.Message);
        }
    }

    private async Task RunProgressTimerAsync(
        string requestId,
        string stepId,
        DateTime startTime,
        CountingTextWriter countingWriter,
        PeriodicTimer timer,
        CancellationToken cancellationToken)
    {
        try
        {
            bool startGenMessageSent = false;
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                var tokens = countingWriter.TokenCount;
                var tokensPerSec = elapsed > 0 ? tokens / elapsed : 0;

                string statusMessage = "";
                if (tokens > 0)
                    statusMessage = $"Generálás folyamatban... {(uint)elapsed}mp | {tokens:N0} token | {tokensPerSec:F1} tok/s";
                else if (!startGenMessageSent)
                {
                    startGenMessageSent = true;
                    statusMessage = $"Generálás folyamatban... {(uint)elapsed}mp | modell betöltve, generálás indul";
                }

                if (!string.IsNullOrWhiteSpace(statusMessage))
                {
                    await _eventPublisher.PublishInferenceProgressAsync(
                        requestId, stepId, (uint)elapsed, statusMessage, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Meghatározza az output fájl path-ját.
    ///
    /// Ha az InferRequest OutputFolder meg van adva (CLI adja meg, generáció alapján),
    /// azt használja – ez a normál működési mód.
    ///
    /// Ha OutputFolder üres (fallback, pl. régi kliensek kompatibilitásához),
    /// a ServiceHost saját _outputBasePath-ját használja.
    /// </summary>
    private string BuildOutputPath(InferRequest request)
    {
        var shortId = request.RequestId.Length >= 8
            ? request.RequestId[..8]
            : request.RequestId;

        var folder = !string.IsNullOrEmpty(request.OutputFolder)
            ? request.OutputFolder
            : Path.Combine(_outputBasePath, request.StepId);

        return Path.Combine(folder, $"{request.StepId}_{shortId}.md");
    }

    // ─────────────────────────────────────────────
    // CountingTextWriter
    // ─────────────────────────────────────────────

    /// <summary>
    /// TextWriter decorator ami megszámolja a backend WriteAsync hívásait.
    /// Minden nem-üres WriteAsync hívás egy tokennek számít – ez közelítő érték,
    /// de elegendő a tok/s kijelzéséhez.
    ///
    /// Thread-safe: Interlocked.Increment biztosítja a számlálót,
    /// az összes többi hívás az inner writer-re delegál.
    /// </summary>
    private sealed class CountingTextWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private int _tokenCount;

        public int TokenCount => _tokenCount;

        public CountingTextWriter(TextWriter inner) => _inner = inner;

        public override Encoding Encoding => _inner.Encoding;

        public override async Task WriteAsync(string? value)
        {
            if (!string.IsNullOrEmpty(value))
                Interlocked.Increment(ref _tokenCount);

            await _inner.WriteAsync(value);
        }

        public override async Task WriteAsync(char value)
        {
            Interlocked.Increment(ref _tokenCount);
            await _inner.WriteAsync(value);
        }

        public override Task FlushAsync() => _inner.FlushAsync();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        // Dispose nem zárja be az inner writert – az InferenceWorker kezeli
        protected override void Dispose(bool disposing) { }
    }
}