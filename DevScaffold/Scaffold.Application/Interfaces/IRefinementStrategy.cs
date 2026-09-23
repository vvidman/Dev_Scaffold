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

using Scaffold.Validation.Abstractions;

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Abstraction for the refinement logic that follows a failed attempt.
///
/// Handles two triggers uniformly:
/// - Automatic reject: IOutputValidator found a rule violation
/// - Human reject: the human reviewer sent it back with a clarification
///
/// ScaffoldStepOrchestrator uses this interface to decide what to pass
/// to the next attempt as a system prompt addition.
/// </summary>
public interface IRefinementStrategy
{
    /// <summary>
    /// Builds the refinement clarification from an automatic validation failure.
    /// The result includes the fix hints of the rule violations,
    /// which the model can use on the next attempt.
    /// </summary>
    string BuildAutoRejectionClarification(OutputValidationResult validationResult);

    /// <summary>
    /// Extends the original system prompt with the refinement context.
    /// The clarification's source can be an automatic validator or a human reviewer.
    /// </summary>
    string BuildRefinedSystemPrompt(IAuditLogger logger, string originalSystemPrompt, string clarification);
}