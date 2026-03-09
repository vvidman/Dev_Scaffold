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
using Scaffold.Application.Interfaces;
using Scaffold.Domain.Models;

namespace Scaffold.CLI;

/// <summary>
/// Egyetlen pipeline lépés futtatásának koordinálása.
///
/// Felelőssége:
/// - Input összerakás (IInputAssembler)
/// - InferRequest küldése a ServiceHost-nak
/// - Esemény fogadás és konzolra írás
/// - Human validáció (Accept / Edit / Reject)
/// - Reject esetén: ugyanaz a lépés újrafut pontosítással
///
/// Egy ScaffoldSession = egy lépés futtatása.
/// A CLI minden run híváskor új session-t hoz létre,
/// majd kilép amikor a lépés elfogadásra kerül.
/// </summary>
public class ScaffoldSession : IAsyncDisposable
{
    private readonly PipeClient _pipeClient;
    private readonly IStepAgentConfigReader _stepAgentConfigReader;
    private readonly IInputAssembler _inputAssembler;
    private readonly IHumanValidationService _humanValidation;

    private readonly string _stepConfigPath;
    private readonly string _inputYamlPath;
    private readonly string _modelAlias;

    // Inference eredmény várakozáshoz
    private TaskCompletionSource<InferenceCompletedEvent>? _inferenceCompletedTcs;
    private TaskCompletionSource<InferenceCancelledEvent>? _inferenceCancelledTcs;
    private TaskCompletionSource<InferenceFailedEvent>? _inferenceFailedTcs;

    private readonly SemaphoreSlim _eventLock = new(1, 1);
    private bool _disposed;

    public ScaffoldSession(
        PipeClient pipeClient,
        IStepAgentConfigReader stepAgentConfigReader,
        IInputAssembler inputAssembler,
        IHumanValidationService humanValidation,
        string stepConfigPath,
        string inputYamlPath,
        string modelAlias)
    {
        _pipeClient = pipeClient;
        _stepAgentConfigReader = stepAgentConfigReader;
        _inputAssembler = inputAssembler;
        _humanValidation = humanValidation;
        _stepConfigPath = stepConfigPath;
        _inputYamlPath = inputYamlPath;
        _modelAlias = modelAlias;

        _pipeClient.EventReceived += HandleEventAsync;
    }

    /// <summary>
    /// Futtatja a lépést human validációval.
    /// Reject esetén ugyanaz a lépés újrafut pontosítással.
    /// Accept vagy Edit esetén visszatér.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var agentConfig = _stepAgentConfigReader.Load(_stepConfigPath);
        var assembledInput = _inputAssembler.Assemble(_inputYamlPath, agentConfig.Step);

        Console.WriteLine($"[SCAFFOLD] Lépés: {agentConfig.Step}");
        Console.WriteLine();

        bool accepted = false;
        string? rejectionContext = null;

        while (!accepted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestId = Guid.NewGuid().ToString();

            var userInput = rejectionContext is null
                ? assembledInput
                : BuildRejectionPrompt(assembledInput, rejectionContext);

            var request = new InferRequest
            {
                RequestId = requestId,
                StepId = agentConfig.Step,
                ModelAlias = _modelAlias,
                SystemPrompt = agentConfig.SystemPrompt,
                UserInput = userInput
            };

            await _pipeClient.SendAsync(
                new CommandEnvelope { Infer = request },
                cancellationToken);

            Console.WriteLine("[SCAFFOLD] Inference kérés elküldve. Várakozás...");

            var outputFilePath = await WaitForInferenceResultAsync(
                requestId,
                agentConfig.Step,
                cancellationToken);

            if (outputFilePath is null)
                return;

            Console.WriteLine($"[SCAFFOLD] Kimenet: {outputFilePath}");
            Console.WriteLine();

            var decision = await _humanValidation.ValidateAsync(
                agentConfig.Step,
                outputFilePath);

            switch (decision.Outcome)
            {
                case ValidationOutcome.Accept:
                    Console.WriteLine("[SCAFFOLD] ✓ Elfogadva.");
                    Console.WriteLine();
                    accepted = true;
                    break;

                case ValidationOutcome.Edit:
                    Console.WriteLine("[SCAFFOLD] ✎ Szerkesztett kimenet elfogadva.");
                    Console.WriteLine();
                    accepted = true;
                    break;

                case ValidationOutcome.Reject:
                    Console.WriteLine("[SCAFFOLD] ✗ Visszaküldve. Újragenerálás...");
                    Console.WriteLine();
                    rejectionContext = decision.RejectionClarification;
                    break;
            }
        }
    }

    // ─────────────────────────────────────────────
    // Inference eredmény várakozás
    // ─────────────────────────────────────────────

    private async Task<string?> WaitForInferenceResultAsync(
        string requestId,
        string stepId,
        CancellationToken cancellationToken)
    {
        await _eventLock.WaitAsync(cancellationToken);
        try
        {
            _inferenceCompletedTcs = new TaskCompletionSource<InferenceCompletedEvent>();
            _inferenceCancelledTcs = new TaskCompletionSource<InferenceCancelledEvent>();
            _inferenceFailedTcs = new TaskCompletionSource<InferenceFailedEvent>();
        }
        finally
        {
            _eventLock.Release();
        }

        using var cancelReg = cancellationToken.Register(() =>
        {
            _inferenceCompletedTcs?.TrySetCanceled();
            _inferenceCancelledTcs?.TrySetCanceled();
            _inferenceFailedTcs?.TrySetCanceled();
        });

        var completedTask = _inferenceCompletedTcs.Task;
        var cancelledTask = _inferenceCancelledTcs.Task;
        var failedTask = _inferenceFailedTcs.Task;

        var winner = await Task.WhenAny(completedTask, cancelledTask, failedTask);

        await _eventLock.WaitAsync(cancellationToken);
        try
        {
            _inferenceCompletedTcs = null;
            _inferenceCancelledTcs = null;
            _inferenceFailedTcs = null;
        }
        finally
        {
            _eventLock.Release();
        }

        if (winner == completedTask)
            return completedTask.Result.OutputFilePath;

        if (winner == cancelledTask)
        {
            Console.WriteLine($"[SCAFFOLD] Inference megszakítva: {stepId}");
            return null;
        }

        Console.Error.WriteLine(
            $"[SCAFFOLD ERROR] Inference hiba ({stepId}): {failedTask.Result.ErrorMessage}");
        return null;
    }

    // ─────────────────────────────────────────────
    // Event handler
    // ─────────────────────────────────────────────

    private async Task HandleEventAsync(EventEnvelope envelope)
    {
        switch (envelope.EventCase)
        {
            case EventEnvelope.EventOneofCase.InferenceStarted:
                Console.WriteLine(
                    $"[SCAFFOLD] Inference elindult " +
                    $"(modell: {envelope.InferenceStarted.ModelAlias})");
                break;

            case EventEnvelope.EventOneofCase.InferenceProgress:
                var progress = envelope.InferenceProgress;
                Console.WriteLine(
                    $"[SCAFFOLD] {progress.StatusMessage} ({progress.ElapsedSeconds}s)");
                break;

            case EventEnvelope.EventOneofCase.InferenceCompleted:
                await _eventLock.WaitAsync();
                try
                {
                    _inferenceCompletedTcs?.TrySetResult(envelope.InferenceCompleted);
                }
                finally
                {
                    _eventLock.Release();
                }
                break;

            case EventEnvelope.EventOneofCase.InferenceCancelled:
                await _eventLock.WaitAsync();
                try
                {
                    _inferenceCancelledTcs?.TrySetResult(envelope.InferenceCancelled);
                }
                finally
                {
                    _eventLock.Release();
                }
                break;

            case EventEnvelope.EventOneofCase.InferenceFailed:
                await _eventLock.WaitAsync();
                try
                {
                    _inferenceFailedTcs?.TrySetResult(envelope.InferenceFailed);
                }
                finally
                {
                    _eventLock.Release();
                }
                break;

            case EventEnvelope.EventOneofCase.ModelStatusChanged:
                var modelEvt = envelope.ModelStatusChanged;
                Console.WriteLine(
                    $"[SCAFFOLD] Modell: {modelEvt.ModelAlias} → {modelEvt.Status}" +
                    (string.IsNullOrEmpty(modelEvt.Message) ? "" : $" ({modelEvt.Message})"));
                break;

            case EventEnvelope.EventOneofCase.ServiceError:
                Console.Error.WriteLine(
                    $"[SCAFFOLD ERROR] {envelope.ServiceError.ErrorCode}: " +
                    $"{envelope.ServiceError.ErrorMessage}");
                break;

            case EventEnvelope.EventOneofCase.ServiceShuttingDown:
                Console.WriteLine("[SCAFFOLD] ServiceHost leállás jelzés érkezett.");
                break;
        }
    }

    // ─────────────────────────────────────────────
    // Segédmetódusok
    // ─────────────────────────────────────────────

    private static string BuildRejectionPrompt(
        string originalInput,
        string? clarification) =>
        $"""
        ## Eredeti input
        {originalInput}

        ## Pontosítás
        {clarification ?? "Kérlek próbáld újra, figyelj a minőségre."}
        """;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _pipeClient.EventReceived -= HandleEventAsync;
        _eventLock.Dispose();
    }
}