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
using Scaffold.ServiceHost.Abstractions;

namespace Scaffold.ServiceHost;

/// <summary>
/// Beérkező CommandEnvelope üzenetek feldolgozása és routolása.
///
/// A command pipe-ról érkező minden parancsot fogad,
/// és a megfelelő komponenshez irányítja:
/// - InferRequest        → IInferenceWorker
/// - CancelInferRequest  → IInferenceWorker.Cancel()
/// - ShutdownRequest     → leállítási folyamat
/// - LoadModelRequest    → IModelCacheManager
/// - UnloadModelRequest  → IModelCacheManager
/// - ListModelsRequest   → IModelCacheManager + IEventPublisher
///
/// Shutdown szemantika (ADR Protocol #6):
/// - force = false: megvárja az aktív inference befejezését, majd leáll
/// - force = true:  azonnal megszakítja az inference-t és leáll
/// </summary>
public class CommandDispatcher
{
    private readonly IInferenceWorker _inferenceWorker;
    private readonly IModelCacheManager _modelCache;
    private readonly IEventPublisher _eventPublisher;

    // Shutdown jelzése a PipeServer felé –
    // a CommandDispatcher nem állítja le a processt,
    // csak jelzi hogy shutdown parancs érkezett
    private readonly CancellationTokenSource _shutdownCts = new();

    public CancellationToken ShutdownToken => _shutdownCts.Token;

    public CommandDispatcher(
        IInferenceWorker inferenceWorker,
        IModelCacheManager modelCache,
        IEventPublisher eventPublisher)
    {
        _inferenceWorker = inferenceWorker;
        _modelCache = modelCache;
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// Feldolgoz egy beérkező CommandEnvelope-ot.
    /// </summary>
    public async Task DispatchAsync(
        CommandEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        switch (envelope.CommandCase)
        {
            case CommandEnvelope.CommandOneofCase.Infer:
                await HandleInferAsync(envelope.Infer, cancellationToken);
                break;

            case CommandEnvelope.CommandOneofCase.Cancel:
                HandleCancel(envelope.Cancel);
                break;

            case CommandEnvelope.CommandOneofCase.Shutdown:
                await HandleShutdownAsync(envelope.Shutdown, cancellationToken);
                break;

            case CommandEnvelope.CommandOneofCase.LoadModel:
                await HandleLoadModelAsync(envelope.LoadModel, cancellationToken);
                break;

            case CommandEnvelope.CommandOneofCase.UnloadModel:
                await HandleUnloadModelAsync(envelope.UnloadModel, cancellationToken);
                break;

            case CommandEnvelope.CommandOneofCase.ListModels:
                await HandleListModelsAsync(envelope.ListModels, cancellationToken);
                break;

            case CommandEnvelope.CommandOneofCase.None:
            default:
                await _eventPublisher.PublishServiceErrorAsync(
                    errorCode: "UNKNOWN_COMMAND",
                    errorMessage: $"Ismeretlen parancs típus: {envelope.CommandCase}",
                    ct: cancellationToken);
                break;
        }
    }

    // ─────────────────────────────────────────────
    // Handler implementációk
    // ─────────────────────────────────────────────

    private async Task HandleInferAsync(
        InferRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Fire and forget – nem blokkoljuk a command pipe olvasását
            // Az InferenceWorker saját maga küldi az eseményeket
            _ = Task.Run(
                async () => await _inferenceWorker.RunAsync(request, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _eventPublisher.PublishInferenceFailedAsync(
                request.RequestId,
                request.StepId,
                ex.Message,
                cancellationToken);
        }
    }

    private void HandleCancel(CancelInferRequest request)
    {
        // TODO [SCAFFOLD]: per-request cancel ha több párhuzamos inference lesz
        _inferenceWorker.Cancel();
    }

    private async Task HandleShutdownAsync(
        ShutdownRequest request,
        CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishServiceShuttingDownAsync(
            request.Force,
            cancellationToken);

        if (request.Force)
        {
            // Azonnali leállás – aktív inference megszakítása
            _inferenceWorker.Cancel();
        }
        else
        {
            // Graceful leállás – megvárjuk az aktív inference befejezését
            await _inferenceWorker.WaitForCompletionAsync(cancellationToken);
        }

        _shutdownCts.Cancel();
    }

    private async Task HandleLoadModelAsync(
        LoadModelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _modelCache.LoadAsync(
                request.RequestId,
                request.ModelAlias,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _eventPublisher.PublishServiceErrorAsync(
                errorCode: "MODEL_LOAD_FAILED",
                errorMessage: $"Backend inicializálási hiba ({request.ModelAlias}): {ex.Message}",
                ct: cancellationToken);
        }
    }

    private async Task HandleUnloadModelAsync(
        UnloadModelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _modelCache.UnloadAsync(
                request.RequestId,
                request.ModelAlias,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _eventPublisher.PublishServiceErrorAsync(
                errorCode: "MODEL_UNLOAD_FAILED",
                errorMessage: $"Backend kiürítési hiba ({request.ModelAlias}): {ex.Message}",
                ct: cancellationToken);
        }
    }

    private async Task HandleListModelsAsync(
        ListModelsRequest request,
        CancellationToken cancellationToken)
    {
        var loadedAliases = await _modelCache.GetLoadedAliasesAsync(cancellationToken);

        await _eventPublisher.PublishLoadedModelsListAsync(
            request.RequestId,
            loadedAliases,
            cancellationToken);
    }
}