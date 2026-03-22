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
using Scaffold.Validation.Abstractions;

namespace Scaffold.Application;

/// <summary>
/// Egy inference kísérlet eredményének feldolgozása.
///
/// Feliratkozik az IPipeClient eseményeire, megvárja az InferenceCompleted
/// (vagy Failed/Cancelled) eseményt, elvégzi az automatikus validációt,
/// majd szükség esetén átadja a döntést az IHumanValidationService-nek.
///
/// Naplóz minden releváns eseményt az IAuditLogger-en és
/// IScaffoldConsole-on keresztül.
/// </summary>
internal sealed class InferenceResultHandler : IInferenceResultHandler
{
    private readonly IOutputValidator _outputValidator;
    private readonly IHumanValidationService _humanValidation;
    private readonly IRefinementStrategy _refinementStrategy;
    private readonly IScaffoldConsole _console;

    public InferenceResultHandler(
        IOutputValidator outputValidator,
        IHumanValidationService humanValidation,
        IRefinementStrategy refinementStrategy,
        IScaffoldConsole console)
    {
        _outputValidator = outputValidator;
        _humanValidation = humanValidation;
        _refinementStrategy = refinementStrategy;
        _console = console;
    }

    /// <inheritdoc />
    public async Task<ValidationDecision> HandleAsync(
        IPipeClient pipeClient,
        IAuditLogger auditLogger,
        InferRequest request,
        StepAgentConfig agentConfig,
        ValidatorRuleSet? ruleSet,
        CancellationToken cancellationToken = default)
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
                        await HandleCompletedAsync(
                            evt.InferenceCompleted, auditLogger, request, agentConfig, ruleSet, completionTcs);
                    break;

                case EventEnvelope.EventOneofCase.InferenceCancelled:
                    if (evt.InferenceCancelled.RequestId == request.RequestId)
                    {
                        auditLogger.Log(AuditEvent.Error, "reason=inference_cancelled");
                        completionTcs.TrySetResult(
                            new ValidationDecision(ValidationOutcome.Reject,
                                "Inference megszakítva."));
                    }
                    break;

                case EventEnvelope.EventOneofCase.InferenceFailed:
                    if (evt.InferenceFailed.RequestId == request.RequestId)
                    {
                        auditLogger.Log(AuditEvent.Error,
                            $"reason=inference_failed message=\"{evt.InferenceFailed.ErrorMessage}\"");
                        completionTcs.TrySetException(
                            new InvalidOperationException(evt.InferenceFailed.ErrorMessage));
                    }
                    break;
            }
        };

        pipeClient.EventReceived += handler;
        try
        {
            return await completionTcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            pipeClient.EventReceived -= handler;
        }
    }

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task HandleCompletedAsync(
        InferenceCompletedEvent completed,
        IAuditLogger auditLogger,
        InferRequest request,
        StepAgentConfig agentConfig,
        ValidatorRuleSet? ruleSet,
        TaskCompletionSource<ValidationDecision> completionTcs)
    {
        auditLogger.Log(AuditEvent.InferenceDone,
            $"tokens={completed.TokensGenerated} " +
            $"elapsed={completed.ElapsedSeconds}s " +
            $"tok_s={TokPerSec(completed.TokensGenerated, completed.ElapsedSeconds):F1}");

        auditLogger.Log(AuditEvent.Output,
            $"path={completed.OutputFilePath}");

        _console.WriteSession(
            $"[SESSION] Generálás kész | " +
            $"{completed.TokensGenerated:N0} token | " +
            $"{completed.ElapsedSeconds}s | " +
            $"{TokPerSec(completed.TokensGenerated, completed.ElapsedSeconds):F1} tok/s");

        var outputContent = await File.ReadAllTextAsync(completed.OutputFilePath);

        var validationResult = _outputValidator.Validate(
            outputContent,
            request.StepId,
            agentConfig.MaxTokens,
            (int)completed.TokensGenerated,
            ruleSet);

        if (!validationResult.Passed)
        {
            LogValidationErrors(validationResult, auditLogger);

            var clarification = _refinementStrategy.BuildAutoRejectionClarification(validationResult);
            completionTcs.TrySetResult(
                new ValidationDecision(ValidationOutcome.Reject, completed.OutputFilePath, clarification));
            return;
        }

        foreach (var warning in validationResult.Warnings)
            _console.WriteValidation($"[VALIDATE WARNING] [{warning.RuleId}] {warning.Description}");

        var decision = await _humanValidation.ValidateAsync(
            request.StepId,
            completed.OutputFilePath);

        auditLogger.Log(AuditEvent.Validation, FormatValidationLog(decision));

        completionTcs.TrySetResult(decision);
    }

    private void LogValidationErrors(OutputValidationResult validationResult, IAuditLogger auditLogger)
    {
        _console.WriteValidation("[VALIDATE] Automatikus validáció sikertelen – auto-reject.");

        foreach (var error in validationResult.Errors)
        {
            _console.WriteValidation($"[VALIDATE] [{error.RuleId}] {error.Description}");
            auditLogger.Log(AuditEvent.Error,
                $"reason=auto_validation rule={error.RuleId} " +
                $"description=\"{error.Description.Replace("\"", "'")}\"");
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
            var escaped = decision.RejectionClarification.Replace("\"", "'");
            return $"{base_} clarification=\"{escaped}\"";
        }

        return base_;
    }
}