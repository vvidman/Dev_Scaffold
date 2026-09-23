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
using Scaffold.Domain.Models;
using Scaffold.Validation.Abstractions;

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Processes the result of a single inference attempt.
///
/// Subscribes to the pipe's events, waits for completion, runs
/// automatic validation, then calls the human validation service
/// if needed.
///
/// Returns a ValidationDecision, from which ScaffoldStepOrchestrator
/// decides whether to accept, edit, or rerun.
/// </summary>
public interface IInferenceResultHandler
{
    /// <summary>
    /// Waits for the inference result and processes it.
    /// </summary>
    /// <param name="request">The inference request that was sent – events are filtered by its request ID.</param>
    /// <param name="agentConfig">The step's configuration – needed for the max_tokens check.</param>
    /// <param name="ruleSet">Optional declarative validator rule set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The validation result and the output file's path.</returns>
    Task<ValidationDecision> HandleAsync(
        IPipeClient pipeClient,
        IAuditLogger auditLogger,
        InferRequest request,
        StepAgentConfig agentConfig,
        ValidatorRuleSet? ruleSet,
        CancellationToken cancellationToken = default);
}
