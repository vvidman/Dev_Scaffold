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

using LLama;
using LLama.Common;
using Scaffold.Agent.Protocol;
using Scaffold.Domain.Models;

namespace Scaffold.ServiceHost;

/// <summary>
/// Inference futtatásáért felelős komponens.
///
/// Egyszerre csak egy inference futhat – a CancellationToken
/// biztosítja hogy CancelInferRequest esetén megszakítható.
///
/// Felelősségei:
/// - Modell context létrehozása a ModelCache-ből
/// - Inference futtatása LLamaSharp InstructExecutor-ral
/// - Kimenet fájlba írása
/// - Periodikus InferenceProgressEvent küldése
/// - InferenceCompletedEvent / InferenceFailedEvent küldése
/// </summary>
public class InferenceWorker
{
    private readonly ModelCache _modelCache;
    private readonly EventPublisher _eventPublisher;
    private readonly string _outputBasePath;

    // Az aktív inference CancellationTokenSource-a
    // CancelInferRequest esetén ez kerül Cancel()-re
    private CancellationTokenSource? _activeInferenceCts;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    // Progress timer intervalluma
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
        // Csak egy inference futhat egyszerre
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

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task ExecuteInferenceAsync(
        InferRequest request,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var outputFilePath = BuildOutputPath(request.StepId, request.RequestId);

        // Inference elindult esemény
        await _eventPublisher.PublishInferenceStartedAsync(
            request.RequestId,
            request.StepId,
            request.ModelAlias,
            cancellationToken);

        try
        {
            // Modell betöltése (lazy – ha még nincs cache-ben)
            var weights = await _modelCache.GetOrLoadAsync(
                request.RequestId,
                request.ModelAlias,
                cancellationToken);

            var modelConfig = _modelCache.GetModelConfig(request.ModelAlias);

            // Output mappa létrehozása
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            // Progress timer indítása
            using var progressTimer = new PeriodicTimer(ProgressInterval);
            var progressTask = RunProgressTimerAsync(
                request.RequestId,
                request.StepId,
                startTime,
                progressTimer,
                cancellationToken);

            // Inference futtatása
            uint tokensGenerated = 0;

            await using (var writer = new StreamWriter(outputFilePath, append: false))
            {
                tokensGenerated = await RunLlamaInferenceAsync(
                    weights,
                    modelConfig,
                    request,
                    writer,
                    cancellationToken);
            }

            // Progress timer leállítása
            progressTimer.Dispose();
            await progressTask;

            var elapsed = (uint)(DateTime.UtcNow - startTime).TotalSeconds;

            // Inference kész esemény
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
            // Megszakítás – töröljük a részleges kimenetet ha létezik
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

    private async Task<uint> RunLlamaInferenceAsync(
        LLamaWeights weights,
        ModelConfig modelConfig,
        InferRequest request,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        var parameters = new ModelParams(modelConfig.Path)
        {
            ContextSize = (uint)modelConfig.ContextSize,
            GpuLayerCount = modelConfig.GpuLayers
        };

        using var context = weights.CreateContext(parameters);
        var executor = new InstructExecutor(context);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = request.MaxTokens > 0 ? (int)request.MaxTokens : 4096,
            AntiPrompts = ["User:", "### User:", "\nUser:"]
        };

        var fullPrompt = $"{request.SystemPrompt}\n\n{request.UserInput}";
        uint tokenCount = 0;

        await foreach (var token in executor
            .InferAsync(fullPrompt, inferenceParams, cancellationToken)
            .ConfigureAwait(false))
        {
            await writer.WriteAsync(token);
            await writer.FlushAsync(cancellationToken);
            tokenCount++;
        }

        return tokenCount;
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
        catch (OperationCanceledException)
        {
            // Megszakítás – normál eset, nem kell logolni
        }
    }

    private string BuildOutputPath(string stepId, string requestId)
    {
        // Mappa: {outputBasePath}/{stepId}/
        // Fájlnév: {stepId}_{requestId_első_8_char}.md
        var shortId = requestId.Length >= 8
            ? requestId[..8]
            : requestId;

        return Path.Combine(
            _outputBasePath,
            stepId,
            $"{stepId}_{shortId}.md");
    }
}