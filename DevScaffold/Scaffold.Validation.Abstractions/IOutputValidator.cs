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

namespace Scaffold.Validation.Abstractions;

/// <summary>
/// Validates a step's output.
/// The implementation combines the universal and the per-step checks.
/// </summary>
public interface IOutputValidator
{
    /// <summary>
    /// Validates the step's output.
    /// </summary>
    /// <param name="outputContent">The full text content of the generated output.</param>
    /// <param name="stepId">The step's identifier (e.g. "task_breakdown").</param>
    /// <param name="maxTokensConfigured">The max_tokens value configured in the step agent config.</param>
    /// <param name="tokensGenerated">The number of tokens actually generated.</param>
    /// <param name="ruleSet">Optional declarative rule set loaded from yaml.</param>
    OutputValidationResult Validate(
        string outputContent,
        string stepId,
        int? maxTokensConfigured,
        int tokensGenerated,
        ValidatorRuleSet? ruleSet = null);
}
