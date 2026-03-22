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

using Scaffold.Application.Interfaces;
using Scaffold.Validation.Abstractions;

namespace Scaffold.Application;

/// <summary>
/// Alapértelmezett refinement stratégia implementáció.
///
/// Az [AUTO] prefix jelzi a system promptban hogy automatikus validator
/// generálta a clarification-t – ez alapján a modell tudja hogy
/// szabálysértést kell javítania, nem human preferenciát.
/// </summary>
public sealed class RefinementStrategy : IRefinementStrategy
{
    private const string AutoPrefix = "[AUTO]";

    /// <inheritdoc />
    public string BuildAutoRejectionClarification(OutputValidationResult validationResult)
    {
        var hints = validationResult.Errors
            .Where(e => e.FixHint is not null)
            .Select(e => $"- [{e.RuleId}] {e.FixHint}")
            .ToList();

        var body = hints.Count > 0
            ? string.Join("\n", hints)
            : "Automatic validation failed – see audit log for details.";

        return $"{AutoPrefix}\n{body}";
    }

    /// <inheritdoc />
    public string BuildRefinedSystemPrompt(string originalSystemPrompt, string clarification)
    {
        var isAutoRefinement = clarification.StartsWith(AutoPrefix);

        var clarificationText = isAutoRefinement
            ? clarification[AutoPrefix.Length..].Trim()
            : clarification;

        var header = isAutoRefinement
            ? "The previous attempt was automatically rejected due to rule violations."
            : "The previous attempt was rejected by the human reviewer.";

        return $"{originalSystemPrompt}\n\n" +
               $"--- REFINEMENT CONTEXT ---\n" +
               $"{header}\n" +
               $"You MUST fix the following issues in this attempt:\n" +
               $"{clarificationText}\n" +
               $"--- END REFINEMENT CONTEXT ---";
    }
}