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

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(10);

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
        var outputFilePath = BuildOutputPath(request.StepId, request.RequestId);

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

            using var progressTimer = new PeriodicTimer(ProgressInterval);
            var progressTask = RunProgressTimerAsync(
                request.RequestId,
                request.StepId,
                startTime,
                progressTimer,
                cancellationToken);

            uint tokensGenerated;
            await using (var writer = new StreamWriter(outputFilePath, append: false))
            {
                tokensGenerated = await backend.RunAsync(request, writer, cancellationToken);
            }

            progressTimer.Dispose();
            await progressTask;

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
        PeriodicTimer timer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var elapsed = (uint)(DateTime.UtcNow - startTime).TotalSeconds;

                await _eventPublisher.PublishInferenceProgressAsync(
                    requestId,
                    stepId,
                    elapsed,
                    "Generálás folyamatban...",
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private string BuildOutputPath(string stepId, string requestId)
    {
        var shortId = requestId.Length >= 8 ? requestId[..8] : requestId;

        return Path.Combine(
            _outputBasePath,
            stepId,
            $"{stepId}_{shortId}.md");
    }
}