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

namespace Scaffold.Validation;

/// <summary>
/// Registry and resolution of step-specific validators by step_id.
///
/// If no validator is registered for a given step_id, returns null –
/// in that case only UniversalOutputValidator runs, the step does not fail.
/// </summary>
public sealed class StepValidatorRegistry
{
    private readonly Dictionary<string, IStepOutputValidator> _validators;

    public StepValidatorRegistry(IEnumerable<IStepOutputValidator> validators)
    {
        _validators = validators.ToDictionary(v => v.StepId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the validator that belongs to the step_id.
    /// Returns null if no validator is registered.
    /// </summary>
    public IStepOutputValidator? Resolve(string stepId) =>
        _validators.GetValueOrDefault(stepId);

    /// <summary>List of registered step_ids (for diagnostics).</summary>
    public IReadOnlyCollection<string> RegisteredStepIds => _validators.Keys;
}