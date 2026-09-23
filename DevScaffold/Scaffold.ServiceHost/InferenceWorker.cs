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
/// Component responsible for running inference.
///
/// Backend-agnostic: does not know whether LLamaSharp or an API runs
/// the inference – IInferenceBackend hides that.
///
/// Only one inference can run at a time – a SemaphoreSlim enforces this,
/// and WaitForCompletionAsync uses the same lock to implement graceful
/// shutdown.
///
/// Output path resolution:
/// - If InferRequest.OutputFolder is set, uses it (determined by the CLI)
/// - If empty, falls back to: _outputBasePath / stepId / (ServiceHost's own logic)
///
/// Responsibilities:
/// - Fetching the backend from ModelCache (with lazy loading)
/// - Running the inference through the backend
/// - Writing the output to a file
/// - Sending an InferenceProgressEvent heartbeat on every tick (model load, prompt processing, generation)
/// - Sending InferenceCompletedEvent / InferenceFailedEvent / InferenceCancelledEvent
/// </summary>
public class InferenceWorker : IInferenceWorker
{
    private readonly IInferenceBackendProvider _modelCache;
    private readonly IInferenceEventPublisher _eventPublisher;
    private readonly string _outputBasePath;

    // CancellationTokenSource of the active inference
    private CancellationTokenSource? _activeInferenceCts;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    /// <summary>Default heartbeat interval – the CLI liveness timeout is 3 × this value.</summary>
    public static readonly TimeSpan DefaultProgressInterval = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _progressInterval;

    public InferenceWorker(
        IInferenceBackendProvider modelCache,
        IInferenceEventPublisher eventPublisher,
        string outputBasePath,
        TimeSpan? progressInterval = null)
    {
        _modelCache = modelCache;
        _eventPublisher = eventPublisher;
        _outputBasePath = outputBasePath;
        _progressInterval = progressInterval ?? DefaultProgressInterval;
    }

    /// <summary>
    /// Starts the inference run.
    /// Throws InvalidOperationException if an inference is already running.
    /// </summary>
    public async Task RunAsync(
        InferRequest request,
        CancellationToken serviceCancellationToken = default)
    {
        if (!await _inferenceLock.WaitAsync(0))
            throw new InvalidOperationException(
                $"An inference is already running. Request: {request.RequestId}");

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
    /// Cancels the active inference.
    /// No-op if there is no active inference.
    /// </summary>
    public void Cancel()
    {
        _activeInferenceCts?.Cancel();
    }

    /// <summary>
    /// Waits for the active inference to complete.
    /// Called by CommandDispatcher on graceful shutdown (force = false).
    /// Returns immediately if there is no active inference.
    /// </summary>
    public async Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        // Acquiring the lock means there is no active inference
        await _inferenceLock.WaitAsync(cancellationToken);
        _inferenceLock.Release();
    }

    // ─────────────────────────────────────────────
    // Private implementation
    // ─────────────────────────────────────────────

    private async Task ExecuteInferenceAsync(
         InferRequest request,
         CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var outputFilePath = BuildOutputPath(request);
        var progressState = new ProgressState();

        try
        {
            await _eventPublisher.PublishInferenceStartedAsync(
                request.RequestId,
                request.StepId,
                request.ModelAlias,
                cancellationToken);

            // Heartbeat: the progress timer covers the whole request lifetime –
            // model loading, prompt processing and generation – so the CLI's
            // liveness timeout measures ServiceHost silence, not model speed.
            using var progressTimer = new PeriodicTimer(_progressInterval);
            var progressTask = RunProgressTimerAsync(
                request,
                startTime,
                progressState,
                progressTimer,
                cancellationToken);

            uint tokensGenerated;
            try
            {
                var backend = await _modelCache.GetOrLoadAsync(
                    request.RequestId,
                    request.ModelAlias,
                    cancellationToken);

                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

                await using var fileWriter = new StreamWriter(outputFilePath, append: false);

                // CountingTextWriter intercepts the token writes –
                // the progress timer reads the counter without the backend
                // implementation needing to change.
                var countingWriter = new CountingTextWriter(fileWriter);
                progressState.Writer = countingWriter;

                tokensGenerated = await backend.RunAsync(request, countingWriter, cancellationToken);
            }
            finally
            {
                // Stop the heartbeat before any terminal event is published,
                // on success, failure and cancellation alike. RunProgressTimerAsync
                // never throws, so awaiting it cannot mask the inference outcome.
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

    /// <summary>
    /// Sends an InferenceProgressEvent on every tick until the timer is disposed.
    /// Every tick publishes – the event doubles as the liveness heartbeat.
    /// Never throws – heartbeat failures are logged and end the loop.
    /// </summary>
    private async Task RunProgressTimerAsync(
        InferRequest request,
        DateTime startTime,
        ProgressState progressState,
        PeriodicTimer timer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                var statusMessage = BuildStatusMessage(request.ModelAlias, progressState.Writer, elapsed);

                await _eventPublisher.PublishInferenceProgressAsync(
                    request.RequestId, request.StepId, (uint)elapsed, statusMessage, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // The heartbeat is a side channel: a failure here (typically a broken
            // event pipe) must never change the outcome of the inference itself.
            // Stop sending heartbeats; the inference continues and reports its own
            // terminal event (which will fail the same way if the pipe is gone).
            Console.Error.WriteLine(
                $"[ServiceHost WARN] Heartbeat stopped for {request.RequestId}: {ex.Message}");
        }
    }

    private static string BuildStatusMessage(string modelAlias, CountingTextWriter? writer, double elapsed)
    {
        if (writer is null)
            return $"Loading model '{modelAlias}'... {(uint)elapsed}s";

        var tokens = writer.TokenCount;
        if (tokens == 0)
            return $"Processing prompt... {(uint)elapsed}s";

        var tokensPerSec = elapsed > 0 ? tokens / elapsed : 0;
        return $"Generation in progress... {(uint)elapsed}s | {tokens:N0} tokens | {tokensPerSec:F1} tok/s";
    }

    /// <summary>
    /// Phase shared between the inference and the progress timer:
    /// Writer is null while the model is loading.
    /// </summary>
    private sealed class ProgressState
    {
        private volatile CountingTextWriter? _writer;

        public CountingTextWriter? Writer
        {
            get => _writer;
            set => _writer = value;
        }
    }

    /// <summary>
    /// Determines the output file's path.
    ///
    /// If InferRequest.OutputFolder is set (provided by the CLI, based on
    /// generation), uses it – this is the normal mode of operation.
    ///
    /// If OutputFolder is empty (fallback, e.g. for compatibility with
    /// older clients), uses the ServiceHost's own _outputBasePath.
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
}