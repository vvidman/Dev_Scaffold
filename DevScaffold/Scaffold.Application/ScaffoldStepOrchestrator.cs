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
/// Egy scaffold lépés végrehajtásának orchestrálása retry looppal.
///
/// Felelőssége kizárólag a vezérlési folyamat:
/// - Config betöltés és input összerakás
/// - InferRequest összeállítása és elküldése
/// - Döntés alapján loop vagy kilépés
/// - Refinement kontextus átadása következő kísérletnek
///
/// Az eredmény feldolgozása (event figyelés, validáció, human döntés)
/// az IInferenceResultHandler felelőssége.
/// A refinement prompt felépítése az IRefinementStrategy felelőssége.
/// </summary>
public sealed class ScaffoldStepOrchestrator : IAsyncDisposable
{
    private readonly IPipeClient _pipeClient;
    private readonly IStepAgentConfigReader _configReader;
    private readonly IInputAssembler _inputAssembler;
    private readonly IHumanValidationService _humanValidation;
    private readonly IAuditLogger _auditLogger;
    private readonly IScaffoldConsole _console;
    private readonly IInferenceResultHandler _resultHandler;
    private readonly IRefinementStrategy _refinementStrategy;
    private readonly IValidatorRuleSetReader _ruleSetReader;

    private readonly string _stepConfigPath;
    private readonly string _inputYamlPath;
    private readonly string _modelAlias;
    private readonly string _stepOutputFolder;
    private readonly int _generation;

    private const int MaxAttempts = 5;

    public ScaffoldStepOrchestrator(
        IPipeClient pipeClient,
        IStepAgentConfigReader configReader,
        IInputAssembler inputAssembler,
        IHumanValidationService humanValidation,
        IAuditLogger auditLogger,
        IScaffoldConsole console,
        IInferenceResultHandler resultHandler,
        IRefinementStrategy refinementStrategy,
        IValidatorRuleSetReader ruleSetReader,
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
        _resultHandler = resultHandler;
        _refinementStrategy = refinementStrategy;
        _ruleSetReader = ruleSetReader;
        _stepConfigPath = stepConfigPath;
        _inputYamlPath = inputYamlPath;
        _modelAlias = modelAlias;
        _stepOutputFolder = stepOutputFolder;
        _generation = generation;
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

        var ruleSet = _ruleSetReader.TryLoad(_stepConfigPath, agentConfig.Step);
        var assembledInput = _inputAssembler.Assemble(_inputYamlPath, agentConfig.Step);

        string? refinementClarification = null;
        var attemptNumber = 0;
        ValidationDecision decision = new(ValidationOutcome.NotValidated, "");

        while (true)
        {
            if (attemptNumber >= MaxAttempts)
                return await HandleMaxAttemptsReachedAsync(agentConfig, decision, attemptNumber);

            attemptNumber++;

            var effectiveSystemPrompt = refinementClarification is not null
                ? _refinementStrategy.BuildRefinedSystemPrompt(_auditLogger,
                    agentConfig.SystemPrompt, refinementClarification)
                : agentConfig.SystemPrompt;

            var request = BuildRequest(agentConfig, effectiveSystemPrompt, assembledInput);

            if (attemptNumber > 1)
                _console.WriteSession($"[SESSION] Refinement futás #{attemptNumber}");

            _auditLogger.Log(AuditEvent.InferenceStart,
                $"request_id={request.RequestId} step={request.StepId} " +
                $"model={request.ModelAlias} attempt={attemptNumber}");

            await _pipeClient.SendAsync(
                new CommandEnvelope { Infer = request },
                cancellationToken);

            decision = await _resultHandler.HandleAsync(
                _pipeClient, _auditLogger, request, agentConfig, ruleSet, cancellationToken);

            if (decision.Outcome == ValidationOutcome.Reject)
            {
                refinementClarification = decision.RejectionClarification;
                var source = IsAutoReject(decision) ? "Auto-reject" : "Human reject";
                _console.WriteSession($"[SESSION] {source} #{attemptNumber} – refinement következik.");
                continue;
            }

            // Accept vagy Edit: kilépés a loop-ból
            var totalElapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            _auditLogger.Log(AuditEvent.SessionEnd,
                $"total_elapsed={totalElapsed}s outcome={decision.Outcome} attempts={attemptNumber}");

            return decision;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ─────────────────────────────────────────────
    // Privát segédmetódusok
    // ─────────────────────────────────────────────

    private async Task<ValidationDecision> HandleMaxAttemptsReachedAsync(
        StepAgentConfig agentConfig,
        ValidationDecision lastDecision,
        int attemptNumber)
    {
        _console.WriteSession(
            $"[SESSION] Maximum kísérletszám elérve ({MaxAttempts}). " +
            "Human beavatkozás szükséges.");
        _auditLogger.Log(AuditEvent.Error,
            $"reason=max_attempts_reached attempts={attemptNumber}");

        if (lastDecision.Outcome != ValidationOutcome.NotValidated)
            return await _humanValidation.ValidateAsync(
                agentConfig.Step, lastDecision.ValidatedOutputFilePath);

        throw new InvalidOperationException(
            $"Maximum kísérletszám elérve ({MaxAttempts}) érvényes kimenet nélkül.");
    }

    private InferRequest BuildRequest(
        StepAgentConfig agentConfig,
        string effectiveSystemPrompt,
        string assembledInput) => new()
        {
            RequestId = Guid.NewGuid().ToString("N"),
            StepId = agentConfig.Step,
            ModelAlias = _modelAlias,
            SystemPrompt = effectiveSystemPrompt,
            UserInput = assembledInput,
            MaxTokens = (uint)(agentConfig.MaxTokens ?? 0),
            OutputFolder = _stepOutputFolder
        };

    private static bool IsAutoReject(ValidationDecision decision) =>
        decision.RejectionClarification?.StartsWith("[AUTO]") == true;
}