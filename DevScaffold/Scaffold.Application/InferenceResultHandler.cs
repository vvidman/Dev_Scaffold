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
/// Processes the result of a single inference attempt.
///
/// Subscribes to IPipeClient events, waits for the InferenceCompleted
/// (or Failed/Cancelled) event, runs automatic validation, then hands
/// the decision over to IHumanValidationService if needed.
///
/// Logs every relevant event through IAuditLogger and IScaffoldConsole.
/// </summary>
internal sealed class InferenceResultHandler : IInferenceResultHandler
{
    private readonly IOutputValidator _outputValidator;
    private readonly IHumanValidationService _humanValidation;
    private readonly IRefinementStrategy _refinementStrategy;
    private readonly IScaffoldConsole _console;
    private readonly InferenceResultHandlerOptions _options;

    public InferenceResultHandler(
        IOutputValidator outputValidator,
        IHumanValidationService humanValidation,
        IRefinementStrategy refinementStrategy,
        IScaffoldConsole console,
        InferenceResultHandlerOptions options)
    {
        _outputValidator = outputValidator;
        _humanValidation = humanValidation;
        _refinementStrategy = refinementStrategy;
        _console = console;
        _options = options;
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

        // Liveness timeout: reset on every event for this request_id. Stopped
        // (infinite) while the human validation prompt is active, because the
        // human's thinking time is not a ServiceHost failure.
        var livenessTimeout = _options.InferenceLivenessTimeout;
        using var livenessCts = new CancellationTokenSource();
        livenessCts.CancelAfter(livenessTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, livenessCts.Token);

        Func<EventEnvelope, Task> handler = async evt =>
        {
            switch (evt.EventCase)
            {
                case EventEnvelope.EventOneofCase.InferenceStarted:
                    if (evt.InferenceStarted.RequestId == request.RequestId)
                    {
                        livenessCts.CancelAfter(livenessTimeout);
                        _console.WriteSession(
                            $"[SESSION] Generation started | step: {evt.InferenceStarted.StepId}" +
                            $" | model: {evt.InferenceStarted.ModelAlias}");
                    }
                    break;

                case EventEnvelope.EventOneofCase.InferenceProgress:
                    if (evt.InferenceProgress.RequestId == request.RequestId)
                    {
                        livenessCts.CancelAfter(livenessTimeout);
                        _console.WriteSession($"[SESSION] {evt.InferenceProgress.StatusMessage}");
                    }
                    break;

                case EventEnvelope.EventOneofCase.InferenceCompleted:
                    if (evt.InferenceCompleted.RequestId == request.RequestId)
                    {
                        // Human review starts inside HandleCompletedAsync – suspend the timer.
                        livenessCts.CancelAfter(Timeout.InfiniteTimeSpan);
                        await HandleCompletedAsync(
                            evt.InferenceCompleted, auditLogger, request, agentConfig, ruleSet, completionTcs);
                    }
                    break;

                case EventEnvelope.EventOneofCase.InferenceCancelled:
                    if (evt.InferenceCancelled.RequestId == request.RequestId)
                    {
                        livenessCts.CancelAfter(Timeout.InfiniteTimeSpan);
                        auditLogger.Log(AuditEvent.Error, "reason=inference_cancelled");
                        completionTcs.TrySetResult(
                            new ValidationDecision(ValidationOutcome.Reject,
                                "Inference cancelled."));
                    }
                    break;

                case EventEnvelope.EventOneofCase.InferenceFailed:
                    if (evt.InferenceFailed.RequestId == request.RequestId)
                    {
                        livenessCts.CancelAfter(Timeout.InfiniteTimeSpan);
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
            return await completionTcs.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var timeoutSeconds = (int)livenessTimeout.TotalSeconds;
            var cancelSent = await TrySendCancelAsync(pipeClient, request.RequestId);
            auditLogger.Log(AuditEvent.Error,
                $"reason=inference_liveness_timeout timeout={timeoutSeconds}s " +
                $"cancel_sent={(cancelSent ? "true" : "false")}");
            throw new TimeoutException(
                $"No event received from ServiceHost for request {request.RequestId} within {timeoutSeconds}s.");
        }
        finally
        {
            pipeClient.EventReceived -= handler;
        }
    }

    // ─────────────────────────────────────────────
    // Private implementation
    // ─────────────────────────────────────────────

    private static readonly TimeSpan CancelSendTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Best effort: asks the ServiceHost to stop working on an abandoned request,
    /// so the next --step does not fail with "already running". Never throws.
    /// </summary>
    private static async Task<bool> TrySendCancelAsync(IPipeClient pipeClient, string requestId)
    {
        try
        {
            using var cts = new CancellationTokenSource(CancelSendTimeout);
            var envelope = new CommandEnvelope
            {
                Cancel = new CancelInferRequest { RequestId = requestId }
            };
            await pipeClient.SendAsync(envelope, cts.Token).WaitAsync(cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

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
            $"[SESSION] Generation done | " +
            $"{completed.TokensGenerated:N0} tokens | " +
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
        _console.WriteValidation("[VALIDATE] Automatic validation failed – auto-reject.");

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