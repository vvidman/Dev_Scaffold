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
/// Egy scaffold lépés teljes életciklusát kezeli.
/// Betölti a step agent configot, összerakja az InferRequest-et,
/// elküldi a ServiceHost-nak, fogadja az eseményeket,
/// és elvégzi a human validációt.
///
/// Progress és státusz üzenetek közvetlenül konzolra kerülnek –
/// az IHumanValidationService kizárólag a validációs interakcióért felelős.
/// </summary>
public class ScaffoldSession : IAsyncDisposable
{
    private readonly PipeClient _pipeClient;
    private readonly IStepAgentConfigReader _configReader;
    private readonly IInputAssembler _inputAssembler;
    private readonly IHumanValidationService _humanValidation;
    private readonly string _stepConfigPath;
    private readonly string _inputYamlPath;
    private readonly string _modelAlias;

    public ScaffoldSession(
        PipeClient pipeClient,
        IStepAgentConfigReader configReader,
        IInputAssembler inputAssembler,
        IHumanValidationService humanValidation,
        string stepConfigPath,
        string inputYamlPath,
        string modelAlias)
    {
        _pipeClient = pipeClient;
        _configReader = configReader;
        _inputAssembler = inputAssembler;
        _humanValidation = humanValidation;
        _stepConfigPath = stepConfigPath;
        _inputYamlPath = inputYamlPath;
        _modelAlias = modelAlias;
    }

    /// <summary>
    /// Lefuttatja a lépést és visszaadja a human validáció döntését.
    /// A hívónak (Program.cs) kell kezelni az Outcome-ot:
    ///   Accept / Edit → sikeres futás (exit 0)
    ///   Reject        → újragenerálás vagy kilépés (exit 2)
    /// </summary>
    public async Task<ValidationDecision> RunAsync(CancellationToken cancellationToken = default)
    {
        var agentConfig = _configReader.Load(_stepConfigPath);
        var assembledInput = _inputAssembler.Assemble(_inputYamlPath, agentConfig.Step);

        var request = new InferRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            StepId = agentConfig.Step,
            ModelAlias = _modelAlias,
            SystemPrompt = agentConfig.SystemPrompt,
            UserInput = assembledInput,
            MaxTokens = (uint)(agentConfig.MaxTokens ?? 0)
        };

        await _pipeClient.SendAsync(
            new CommandEnvelope { Infer = request },
            cancellationToken);

        return await WaitForCompletionAsync(request, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task<ValidationDecision> WaitForCompletionAsync(
        InferRequest request,
        CancellationToken cancellationToken)
    {
        var completionTcs = new TaskCompletionSource<ValidationDecision>();

        _pipeClient.EventReceived += async evt =>
        {
            switch (evt.EventCase)
            {
                case EventEnvelope.EventOneofCase.InferenceStarted:
                    Console.WriteLine(
                        $"[SCAFFOLD] Generálás elindult | step: {evt.InferenceStarted.StepId}" +
                        $" | modell: {evt.InferenceStarted.ModelAlias}");
                    break;

                case EventEnvelope.EventOneofCase.InferenceProgress:
                    if (evt.InferenceProgress.RequestId == request.RequestId)
                        Console.WriteLine($"[SCAFFOLD] {evt.InferenceProgress.StatusMessage}");
                    break;

                case EventEnvelope.EventOneofCase.InferenceCompleted:
                    if (evt.InferenceCompleted.RequestId == request.RequestId)
                    {
                        var decision = await _humanValidation.ValidateAsync(
                            request.StepId,
                            evt.InferenceCompleted.OutputFilePath);
                        completionTcs.TrySetResult(decision);
                    }
                    break;

                case EventEnvelope.EventOneofCase.InferenceCancelled:
                    if (evt.InferenceCancelled.RequestId == request.RequestId)
                        completionTcs.TrySetResult(
                            new ValidationDecision(ValidationOutcome.Reject,
                                "Inference megszakítva."));
                    break;

                case EventEnvelope.EventOneofCase.InferenceFailed:
                    if (evt.InferenceFailed.RequestId == request.RequestId)
                        completionTcs.TrySetException(
                            new InvalidOperationException(evt.InferenceFailed.ErrorMessage));
                    break;
            }
        };

        return await completionTcs.Task.WaitAsync(cancellationToken);
    }
}