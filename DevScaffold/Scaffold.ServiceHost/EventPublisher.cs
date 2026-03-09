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

using System.IO.Pipes;
using Google.Protobuf;
using Scaffold.Agent.Protocol;

namespace Scaffold.ServiceHost;

/// <summary>
/// Az event pipe-ra ír EventEnvelope üzeneteket.
/// A ServiceHost minden kimenő eseménye ezen keresztül jut el a CLI-hez.
///
/// Thread-safe: a _lock biztosítja hogy egyszerre csak egy esemény
/// kerül a pipe-ra – párhuzamos inference progress és modell események
/// esetén sem keverednek az üzenetek.
///
/// Multi-session: a ResetForNewConnectionAsync új pipe instance-t hoz létre
/// miután a CLI kilépett, így a következő CLI session csatlakozhat.
/// </summary>
public class EventPublisher : IAsyncDisposable
{
    private readonly string _pipeName;
    private NamedPipeServerStream _pipe;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public EventPublisher(string pipeName)
    {
        _pipeName = pipeName;
        _pipe = CreatePipe();
    }

    private NamedPipeServerStream CreatePipe() => new(
        pipeName: $"{_pipeName}-events",
        direction: PipeDirection.Out,
        maxNumberOfServerInstances: 1,
        transmissionMode: PipeTransmissionMode.Byte,
        options: PipeOptions.Asynchronous);

    /// <summary>
    /// Megvárja hogy a CLI kliens csatlakozzon az event pipe-ra.
    /// A ServiceHost a ready esemény előtt hívja ezt.
    /// </summary>
    public async Task WaitForConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _pipe.WaitForConnectionAsync(cancellationToken);
    }

    /// <summary>
    /// Az előző CLI session pipe-ját elveti és újat nyit.
    /// A PipeServer hívja mielőtt a következő CLI kapcsolatot várja.
    /// </summary>
    public async Task ResetForNewConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            try { await _pipe.DisposeAsync(); }
            catch (IOException) { /* már lezárt pipe – normál eset */ }

            _pipe = CreatePipe();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Elküldi az EventEnvelope-ot a CLI-nek.
    /// WriteDelimitedTo gondoskodik a hossz prefix framing-ről.
    /// </summary>
    public async Task PublishAsync(
        EventEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // WriteDelimitedTo = varint hossz prefix + protobuf bináris adat
            envelope.WriteDelimitedTo(_pipe);
            await _pipe.FlushAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ─────────────────────────────────────────────
    // Gyártó metódusok – az összes esemény típushoz
    // ─────────────────────────────────────────────

    public Task PublishServiceReadyAsync(string version, CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            ServiceReady = new ServiceReadyEvent
            {
                Version = version,
                StartedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public Task PublishServiceShuttingDownAsync(bool forced, CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            ServiceShuttingDown = new ServiceShuttingDownEvent
            {
                Forced = forced,
                OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public Task PublishModelStatusChangedAsync(
        string requestId,
        string modelAlias,
        ModelStatus status,
        string message = "",
        CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            ModelStatusChanged = new ModelStatusChangedEvent
            {
                RequestId = requestId,
                ModelAlias = modelAlias,
                Status = status,
                Message = message,
                OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public Task PublishLoadedModelsListAsync(
        string requestId,
        IEnumerable<string> loadedAliases,
        CancellationToken ct = default)
    {
        var evt = new LoadedModelsListEvent
        {
            RequestId = requestId,
            OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
        };
        evt.LoadedAliases.AddRange(loadedAliases);
        return PublishAsync(new EventEnvelope { LoadedModelsList = evt }, ct);
    }

    public Task PublishInferenceStartedAsync(
        string requestId,
        string stepId,
        string modelAlias,
        CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            InferenceStarted = new InferenceStartedEvent
            {
                RequestId = requestId,
                StepId = stepId,
                ModelAlias = modelAlias,
                StartedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public Task PublishInferenceProgressAsync(
        string requestId,
        string stepId,
        uint elapsedSeconds,
        string statusMessage,
        CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            InferenceProgress = new InferenceProgressEvent
            {
                RequestId = requestId,
                StepId = stepId,
                ElapsedSeconds = elapsedSeconds,
                StatusMessage = statusMessage,
                OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public Task PublishInferenceCompletedAsync(
        string requestId,
        string stepId,
        string outputFilePath,
        uint elapsedSeconds,
        uint tokensGenerated,
        CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            InferenceCompleted = new InferenceCompletedEvent
            {
                RequestId = requestId,
                StepId = stepId,
                OutputFilePath = outputFilePath,
                ElapsedSeconds = elapsedSeconds,
                TokensGenerated = tokensGenerated,
                CompletedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public Task PublishInferenceCancelledAsync(
        string requestId,
        string stepId,
        CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            InferenceCancelled = new InferenceCancelledEvent
            {
                RequestId = requestId,
                StepId = stepId,
                OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public Task PublishInferenceFailedAsync(
        string requestId,
        string stepId,
        string errorMessage,
        CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            InferenceFailed = new InferenceFailedEvent
            {
                RequestId = requestId,
                StepId = stepId,
                ErrorMessage = errorMessage,
                OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public Task PublishServiceErrorAsync(
        string errorCode,
        string errorMessage,
        CancellationToken ct = default) =>
        PublishAsync(new EventEnvelope
        {
            ServiceError = new ServiceErrorEvent
            {
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                OccurredAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)
            }
        }, ct);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lock.Dispose();
        try { await _pipe.DisposeAsync(); }
        catch (IOException) { /* már bontott pipe – normál eset */ }
    }
}