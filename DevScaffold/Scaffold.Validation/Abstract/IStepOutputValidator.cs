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

namespace Scaffold.Validation.Abstract;

/// <summary>
/// Per-step validációs logika.
/// Minden step típushoz egy dedikált implementáció készül.
/// Ha egy step_id-hoz nincs regisztrált validator, csak az UniversalOutputValidator fut.
/// </summary>
public interface IStepOutputValidator
{
    /// <summary>Egyeznie kell a step agent config step mezőjével.</summary>
    string StepId { get; }

    /// <summary>
    /// Step-specifikus ellenőrzések futtatása.
    /// Csak a saját step-re vonatkozó violation-öket adja vissza.
    /// </summary>
    IReadOnlyList<ValidationViolation> Validate(
        string outputContent,
        ValidatorRuleSet? ruleSet);
}