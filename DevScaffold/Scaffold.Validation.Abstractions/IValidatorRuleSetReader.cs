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
/// Loads the declarative validator rule set that belongs to a step.
///
/// The implementation is also responsible for resolving the file path –
/// the caller does not need to know the filename convention.
///
/// If no validator yaml exists for the given step, returns null.
/// This is not an error – the per-step validator then runs with its
/// built-in default rules.
/// </summary>
public interface IValidatorRuleSetReader
{
    /// <summary>
    /// Attempts to load the validator rule set that belongs to the step.
    /// </summary>
    /// <param name="stepConfigPath">Full path to the step agent config file.</param>
    /// <param name="stepId">The step's identifier (e.g. "task_breakdown").</param>
    /// <returns>The loaded rule set, or null if no validator yaml exists.</returns>
    ValidatorRuleSet? TryLoad(string stepConfigPath, string stepId);
}
