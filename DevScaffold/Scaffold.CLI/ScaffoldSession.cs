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
using Scaffold.Validation;
using Scaffold.Validation.Abstract;

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
    private readonly IOutputValidator _outputValidator;
    private readonly ValidatorYamlReader _validatorYamlReader;


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
        int generation,
        IOutputValidator outputValidator,        
        ValidatorYamlReader validatorYamlReader)
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
        _outputValidator = outputValidator;
        _validatorYamlReader = validatorYamlReader;
    }

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

        var validatorYamlPath = ValidatorYamlReader.ResolveValidatorPath(
            _stepConfigPath, agentConfig.Step);
        var ruleSet = _validatorYamlReader.TryLoad(validatorYamlPath);

        var assembledInput = _inputAssembler.Assemble(_inputYamlPath, agentConfig.Step);

        // Refinement state – null az első futáson
        string? refinementClarification = null;
        var attemptNumber = 0;

        ValidationDecision decision = new(ValidationOutcome.NotValidated, "");
        const int MaxAttempts = 5;

        while (true)
        {
            if (attemptNumber >= MaxAttempts)
            {
                _console.WriteSession(
                    $"[SESSION] Maximum kísérletszám elérve ({MaxAttempts}). " +
                    "Human beavatkozás szükséges.");
                _auditLogger.Log(AuditEvent.Error,
                    $"reason=max_attempts_reached attempts={attemptNumber}");

                if (decision.Outcome != ValidationOutcome.NotValidated)
                {
                    // Human elé kerül a döntés – nem dobjuk el a munkát
                    return await _humanValidation.ValidateAsync(agentConfig.Step, decision.ValidatedOutputFilePath);
                }

                // Első kísérlet sem futott le – nem várható érvényes kimenet
                throw new InvalidOperationException(
                    $"Maximum kísérletszám elérve ({MaxAttempts}) érvényes kimenet nélkül.");
            }

            attemptNumber++;

            // System prompt kiegészítése refinement kontextussal (2. futástól)
            var effectiveSystemPrompt = refinementClarification is not null
                ? BuildRefinedSystemPrompt(agentConfig.SystemPrompt, refinementClarification)
                : agentConfig.SystemPrompt;

            var request = new InferRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                StepId = agentConfig.Step,
                ModelAlias = _modelAlias,
                SystemPrompt = effectiveSystemPrompt,
                UserInput = assembledInput,
                MaxTokens = (uint)(agentConfig.MaxTokens ?? 0),
                OutputFolder = _stepOutputFolder
            };

            if (attemptNumber > 1)
                _console.WriteSession($"[SESSION] Refinement futás #{attemptNumber}");

            _auditLogger.Log(AuditEvent.InferenceStart,
                $"request_id={request.RequestId} step={request.StepId} " +
                $"model={request.ModelAlias} attempt={attemptNumber}");

            await _pipeClient.SendAsync(
                new CommandEnvelope { Infer = request },
                cancellationToken);

            decision = await WaitForCompletionAsync(
                request, agentConfig, ruleSet, cancellationToken);

            // Auto-reject: a WaitForCompletionAsync adja vissza a violation clarification-t
            if (decision.Outcome == ValidationOutcome.Reject
                && decision.RejectionClarification?.StartsWith("[AUTO]") == true)
            {
                refinementClarification = decision.RejectionClarification;
                _console.WriteSession(
                    $"[SESSION] Auto-reject #{attemptNumber} – refinement következik.");                
                continue;
            }

            // Human reject: a human pontosítása lesz a refinement alap
            if (decision.Outcome == ValidationOutcome.Reject)
            {
                refinementClarification = decision.RejectionClarification;
                _console.WriteSession(
                    $"[SESSION] Human reject #{attemptNumber} – refinement következik.");
                continue;
            }

            // Accept vagy Edit: kilépés a loop-ból
            var totalElapsed = (uint)(DateTime.UtcNow - startTime).TotalSeconds;
            _auditLogger.Log(AuditEvent.SessionEnd,
                $"total_elapsed={totalElapsed}s outcome={decision.Outcome} attempts={attemptNumber}");

            return decision;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task<ValidationDecision> WaitForCompletionAsync(
        InferRequest request,
        StepAgentConfig agentConfig, 
        ValidatorRuleSet? ruleSet,   
        CancellationToken cancellationToken)
    {
        var completionTcs = new TaskCompletionSource<ValidationDecision>();

        Func<EventEnvelope, Task> handler = async evt =>
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

                        // ── VALIDATION ────────────────────────────────────────────
                        var outputContent = await File.ReadAllTextAsync(completed.OutputFilePath);

                        var validationResult = _outputValidator.Validate(
                            outputContent,
                            request.StepId,
                            agentConfig.MaxTokens,
                            (int)completed.TokensGenerated,
                            ruleSet);

                        if (!validationResult.Passed)
                        {
                            _console.WriteValidation("[VALIDATE] Automatikus validáció sikertelen – auto-reject.");

                            foreach (var error in validationResult.Errors)
                            {
                                _console.WriteValidation($"[VALIDATE] [{error.RuleId}] {error.Description}");
                                _auditLogger.Log(AuditEvent.Error,
                                    $"reason=auto_validation rule={error.RuleId} " +
                                    $"description=\"{error.Description.Replace("\"", "'")}\"");
                            }

                            var autoRejectClarification = BuildAutoRejectClarification(validationResult);
                            completionTcs.TrySetResult(
                                new ValidationDecision(ValidationOutcome.Reject, completed.OutputFilePath, autoRejectClarification));
                            return;
                        }

                        // Warning-ok megjelenítése human döntése előtt
                        foreach (var warning in validationResult.Warnings)
                            _console.WriteValidation($"[VALIDATE WARNING] [{warning.RuleId}] {warning.Description}");
                        // ── END VALIDATION ────────────────────────────────────────

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

        _pipeClient.EventReceived += handler;
        try
        {
            return await completionTcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pipeClient.EventReceived -= handler;  // ← mindig leiratkozik, kivétel esetén is
        }
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

    private static string BuildAutoRejectClarification(OutputValidationResult result)
    {
        var hints = result.Errors
            .Where(e => e.FixHint is not null)
            .Select(e => $"- [{e.RuleId}] {e.FixHint}")
            .ToList();

        var body = hints.Count > 0
            ? string.Join("\n", hints)
            : "Automatic validation failed – see audit log for details.";

        return $"[AUTO]\n{body}";
    }

    private static string BuildRefinedSystemPrompt(
        string originalSystemPrompt,
        string refinementClarification)
    {
        // Az [AUTO] prefix jelzi hogy automatikus validator generálta,
        // a human pontosítás nem tartalmaz prefixet.
        var isAutoRefinement = refinementClarification.StartsWith("[AUTO]");
        var clarificationText = isAutoRefinement
            ? refinementClarification["[AUTO]".Length..].Trim()
            : refinementClarification;

        var header = isAutoRefinement
            ? "The previous attempt was automatically rejected due to rule violations."
            : "The previous attempt was rejected by the human reviewer.";

        return $"{originalSystemPrompt}\n\n" +
               $"--- REFINEMENT CONTEXT ---\n" +
               $"{header}\n" +
               $"You MUST fix the following issues in this attempt:\n" +
               $"{clarificationText}\n" +
               $"--- END REFINEMENT CONTEXT ---";
    }
}