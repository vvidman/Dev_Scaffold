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
/// Egy step kimenetének validálása.
/// Az implementáció az univerzális és a per-step ellenőrzéseket kombinálja.
/// </summary>
public interface IOutputValidator
{
    /// <summary>
    /// Validálja a step kimenetét.
    /// </summary>
    /// <param name="outputContent">A generált kimenet teljes szöveges tartalma.</param>
    /// <param name="stepId">A step azonosítója (pl. "task_breakdown").</param>
    /// <param name="maxTokensConfigured">A step agent configban megadott max_tokens érték.</param>
    /// <param name="tokensGenerated">Ténylegesen generált tokenek száma.</param>
    /// <param name="ruleSet">Opcionális yaml-ból töltött deklaratív szabálykészlet.</param>
    OutputValidationResult Validate(
        string outputContent,
        string stepId,
        int? maxTokensConfigured,
        int tokensGenerated,
        ValidatorRuleSet? ruleSet = null);
}