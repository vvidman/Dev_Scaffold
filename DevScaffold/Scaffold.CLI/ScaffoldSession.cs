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
using Scaffold.Application;
using Scaffold.Application.Interfaces;
using Scaffold.Domain.Models;

namespace Scaffold.CLI;

/// <summary>
/// Egy scaffold lépés teljes életciklusát kezeli.
/// Betölti a step agent configot, összerakja az InferRequest-et,
/// elküldi a ServiceHost-nak, fogadja az eseményeket,
/// elvégzi a human validációt, és audit logot ír.
///
/// Audit log és konzol üzenetek:
/// - IAuditLogger: minden releváns esemény fájlba kerül (auto-flush)
/// - IScaffoldConsole: színkódolt konzol kimenet szint szerint
/// - IHumanValidationService: kizárólag a validációs interakcióért felelős
/// </summary>
public class ScaffoldSession : IAsyncDisposable
{
    private readonly PipeClient _pipeClient;
    private readonly IStepAgentConfigReader _configReader;
    private readonly IInputAssembler _inputAssembler;
    private readonly IHumanValidationService _humanValidation;
    private readonly IAuditLogger _auditLogger;
    private readonly IScaffoldConsole _console;
    private readonly string _stepConfigPath;
    private readonly string _inputYamlPath;
    private readonly string _modelAlias;
    private readonly string _stepOutputFolder;
    private readonly int _generation;

    public ScaffoldSession(
        PipeClient pipeClient,
        IStepAgentConfigReader configReader,
        IInputAssembler inputAssembler,
        IHumanValidationService humanValidation,
        IAuditLogger auditLogger,
        IScaffoldConsole console,
        string stepConfigPath,
        string inputYamlPath,
        string modelAlias,
        string stepOutputFolder,
        int generation)
    {
        _pipeClient = pipeClient;
        _configReader = configReader;
        _inputAssembler = inputAssembler;
        _humanValidation = humanValidation;
        _auditLogger = auditLogger;
        _console = console;
        _stepConfigPath = stepConfigPath;
        _inputYamlPath = inputYamlPath;
        _modelAlias = modelAlias;
        _stepOutputFolder = stepOutputFolder;
        _generation = generation;
    }

    /// <summary>
    /// Lefuttatja a lépést és visszaadja a human validáció döntését.
    /// A hívónak (Program.cs) kell kezelni az Outcome-ot:
    ///   Accept / Edit → sikeres futás (exit 0)
    ///   Reject        → újragenerálás vagy kilépés (exit 2)
    /// </summary>
    public async Task<ValidationDecision> RunAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        var agentConfig = _configReader.Load(_stepConfigPath);

        _auditLogger.Log(AuditEvent.SessionStart,
            $"step={agentConfig.Step} generation={_generation}");

        _auditLogger.Log(AuditEvent.Config,
            $"model={_modelAlias} " +
            $"system_prompt_length={agentConfig.SystemPrompt.Length} " +
            $"max_tokens={agentConfig.MaxTokens?.ToString() ?? "default"} " +
            $"output_folder={_stepOutputFolder}");

        var assembledInput = _inputAssembler.Assemble(_inputYamlPath, agentConfig.Step);

        var request = new InferRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            StepId = agentConfig.Step,
            ModelAlias = _modelAlias,
            SystemPrompt = agentConfig.SystemPrompt,
            UserInput = assembledInput,
            MaxTokens = (uint)(agentConfig.MaxTokens ?? 0),
            OutputFolder = _stepOutputFolder
        };

        _auditLogger.Log(AuditEvent.InferenceStart,
            $"request_id={request.RequestId} step={request.StepId} model={request.ModelAlias}");

        await _pipeClient.SendAsync(
            new CommandEnvelope { Infer = request },
            cancellationToken);

        var decision = await WaitForCompletionAsync(request, startTime, cancellationToken);

        var totalElapsed = (uint)(DateTime.UtcNow - startTime).TotalSeconds;
        _auditLogger.Log(AuditEvent.SessionEnd,
            $"total_elapsed={totalElapsed}s outcome={decision.Outcome}");

        return decision;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task<ValidationDecision> WaitForCompletionAsync(
        InferRequest request,
        DateTime startTime,
        CancellationToken cancellationToken)
    {
        var completionTcs = new TaskCompletionSource<ValidationDecision>();

        _pipeClient.EventReceived += async evt =>
        {
            switch (evt.EventCase)
            {
                case EventEnvelope.EventOneofCase.InferenceStarted:
                    _console.WriteSession(
                        $"[SESSION] Generálás elindult | step: {evt.InferenceStarted.StepId}" +
                        $" | modell: {evt.InferenceStarted.ModelAlias}");
                    break;

                case EventEnvelope.EventOneofCase.InferenceProgress:
                    if (evt.InferenceProgress.RequestId == request.RequestId)
                        _console.WriteSession($"[SESSION] {evt.InferenceProgress.StatusMessage}");
                    break;

                case EventEnvelope.EventOneofCase.InferenceCompleted:
                    if (evt.InferenceCompleted.RequestId == request.RequestId)
                    {
                        var completed = evt.InferenceCompleted;

                        _auditLogger.Log(AuditEvent.InferenceDone,
                            $"tokens={completed.TokensGenerated} " +
                            $"elapsed={completed.ElapsedSeconds}s " +
                            $"tok_s={TokPerSec(completed.TokensGenerated, completed.ElapsedSeconds):F1}");

                        _auditLogger.Log(AuditEvent.Output,
                            $"path={completed.OutputFilePath}");

                        _console.WriteSession(
                            $"[SESSION] Generálás kész | " +
                            $"{completed.TokensGenerated:N0} token | " +
                            $"{completed.ElapsedSeconds}s | " +
                            $"{TokPerSec(completed.TokensGenerated, completed.ElapsedSeconds):F1} tok/s");

                        var decision = await _humanValidation.ValidateAsync(
                            request.StepId,
                            completed.OutputFilePath);

                        _auditLogger.Log(AuditEvent.Validation,
                            FormatValidationLog(decision));

                        completionTcs.TrySetResult(decision);
                    }
                    break;

                case EventEnvelope.EventOneofCase.InferenceCancelled:
                    if (evt.InferenceCancelled.RequestId == request.RequestId)
                    {
                        _auditLogger.Log(AuditEvent.Error, "reason=inference_cancelled");
                        completionTcs.TrySetResult(
                            new ValidationDecision(ValidationOutcome.Reject,
                                "Inference megszakítva."));
                    }
                    break;

                case EventEnvelope.EventOneofCase.InferenceFailed:
                    if (evt.InferenceFailed.RequestId == request.RequestId)
                    {
                        _auditLogger.Log(AuditEvent.Error,
                            $"reason=inference_failed message=\"{evt.InferenceFailed.ErrorMessage}\"");
                        completionTcs.TrySetException(
                            new InvalidOperationException(evt.InferenceFailed.ErrorMessage));
                    }
                    break;
            }
        };

        return await completionTcs.Task.WaitAsync(cancellationToken);
    }

    private static double TokPerSec(uint tokens, uint elapsedSeconds) =>
        elapsedSeconds > 0 ? (double)tokens / elapsedSeconds : 0;

    private static string FormatValidationLog(ValidationDecision decision)
    {
        var base_ = $"outcome={decision.Outcome}";

        if (decision.Outcome == ValidationOutcome.Reject
            && !string.IsNullOrWhiteSpace(decision.RejectionClarification))
        {
            // Az idézőjelek között lévő szöveg custom parser-ben egyszerűen kinyerhető
            var escaped = decision.RejectionClarification.Replace("\"", "'");
            return $"{base_} clarification=\"{escaped}\"";
        }

        return base_;
    }
}