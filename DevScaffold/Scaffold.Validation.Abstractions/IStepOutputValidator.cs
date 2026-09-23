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
/// Per-step validation logic.
/// A dedicated implementation is written for each step type.
/// If no validator is registered for a given step_id, only UniversalOutputValidator runs.
/// </summary>
public interface IStepOutputValidator
{
    /// <summary>Must match the step field of the step agent config.</summary>
    string StepId { get; }

    /// <summary>
    /// Runs step-specific checks.
    /// Returns only the violations relevant to its own step.
    /// </summary>
    IReadOnlyList<ValidationViolation> Validate(
        string outputContent,
        ValidatorRuleSet? ruleSet);
}
