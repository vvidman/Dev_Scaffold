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
/// Processes and routes incoming CommandEnvelope messages.
///
/// Accepts every command arriving on the command pipe,
/// and directs it to the appropriate component:
/// - InferRequest        → IInferenceWorker
/// - CancelInferRequest  → IInferenceWorker.Cancel()
/// - ShutdownRequest     → shutdown process
/// - LoadModelRequest    → IModelCacheManager
/// - UnloadModelRequest  → IModelCacheManager
/// - ListModelsRequest   → IModelCacheManager + IEventPublisher
///
/// Shutdown semantics (ADR Protocol #6):
/// - force = false: waits for the active inference to complete, then shuts down
/// - force = true:  cancels the active inference immediately and shuts down
/// </summary>
public class CommandDispatcher
{
    private readonly IInferenceWorker _inferenceWorker;
    private readonly IModelCacheManager _modelCache;
    private readonly IEventPublisher _eventPublisher;

    // Signals shutdown to PipeServer –
    // CommandDispatcher does not stop the process itself,
    // it only signals that a shutdown command arrived
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
    /// Processes an incoming CommandEnvelope.
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
                    errorMessage: $"Unknown command type: {envelope.CommandCase}",
                    ct: cancellationToken);
                break;
        }
    }

    // ─────────────────────────────────────────────
    // Handler implementations
    // ─────────────────────────────────────────────

    private Task HandleInferAsync(
        InferRequest request,
        CancellationToken cancellationToken)
    {
        // Fire-and-forget by design (ADR-ServiceHost #9): the command loop must stay
        // responsive so CancelInferRequest can arrive. The background task is still
        // *observed*: every failure is converted into an InferenceFailedEvent.
        _ = RunInferenceObservedAsync(request, cancellationToken);
        return Task.CompletedTask;
    }

    private async Task RunInferenceObservedAsync(
        InferRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() => _inferenceWorker.RunAsync(request, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Service shutdown – InferenceWorker publishes the cancellation itself.
        }
        catch (Exception ex)
        {
            await TryPublishFailureAsync(request, ex);
        }
    }

    private async Task TryPublishFailureAsync(InferRequest request, Exception ex)
    {
        try
        {
            // CancellationToken.None: the failure must be reported even if the
            // request token is already cancelled.
            await _eventPublisher.PublishInferenceFailedAsync(
                request.RequestId, request.StepId, ex.Message, CancellationToken.None);
        }
        catch (Exception publishEx)
        {
            // Event pipe is gone (CLI disconnected). Nothing else can be done here.
            Console.Error.WriteLine(
                $"[ServiceHost ERROR] Could not publish InferenceFailed for {request.RequestId}: {publishEx.Message}");
        }
    }

    private void HandleCancel(CancelInferRequest request)
    {
        // TODO [SCAFFOLD]: per-request cancel once multiple concurrent inferences are supported
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
            // Immediate shutdown – cancel the active inference
            _inferenceWorker.Cancel();
        }
        else
        {
            // Graceful shutdown – wait for the active inference to complete
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
                errorMessage: $"Backend initialization error ({request.ModelAlias}): {ex.Message}",
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
                errorMessage: $"Backend unload error ({request.ModelAlias}): {ex.Message}",
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