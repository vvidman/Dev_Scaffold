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

using Scaffold.Validation.Abstract;

namespace Scaffold.Validation;

/// <summary>
/// Step-specifikus validátorok nyilvántartása és feloldása step_id alapján.
///
/// Ha nincs regisztrált validator egy adott step_id-hoz, null-t ad vissza –
/// ebben az esetben csak az UniversalOutputValidator fut, a step nem esik el.
/// </summary>
public sealed class StepValidatorRegistry
{
    private readonly Dictionary<string, IStepOutputValidator> _validators;

    public StepValidatorRegistry(IEnumerable<IStepOutputValidator> validators)
    {
        _validators = validators.ToDictionary(v => v.StepId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Feloldja a step_id-hoz tartozó validátort.
    /// Ha nincs regisztrált validator, null-t ad vissza.
    /// </summary>
    public IStepOutputValidator? Resolve(string stepId) =>
        _validators.GetValueOrDefault(stepId);

    /// <summary>Regisztrált step_id-ok listája (diagnosztikához).</summary>
    public IReadOnlyCollection<string> RegisteredStepIds => _validators.Keys;
}