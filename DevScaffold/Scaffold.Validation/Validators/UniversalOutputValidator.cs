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

namespace Scaffold.Validation.Validators;

/// <summary>
/// Step-típustól független, minden kimenetere futó ellenőrzések.
///
/// Szabályok:
///   EMPTY_OUTPUT           – üres vagy whitespace-only kimenet              (Error)
///   STOP_TOKEN_LEAKED      – LLM stop token bekerült a fájlba              (Error)
///   TRUNCATED_OUTPUT       – kimenet mondat/struktúra közepén vágódik el   (Error)
///   TOKEN_LIMIT_PROXIMITY  – tokensGenerated >= maxTokens * 0.95           (Warning)
/// </summary>
/// <remarks>
/// Szándékosan nem implementál IOutputValidator-t.
/// Belső segédosztály – kizárólag a CompositeOutputValidator használja.
/// DI-ban közvetlenül NEM regisztrálandó.
/// </remarks>
internal sealed class UniversalOutputValidator
{
    private static readonly string[] KnownStopTokens =
    [
        "<|end|>",
        "<|endoftext|>",
        "<|im_end|>",
        "<|eot_id|>",
        "</s>",
        "<|-|>"
    ];

    /// <summary>
    /// Token limit proximity threshold: ha a generált tokenek száma eléri
    /// a max_tokens 95%-át, Warning kerül a reportba – még ha nem is truncált.
    /// </summary>
    private const double TokenLimitProximityThreshold = 0.95;

    public IReadOnlyList<ValidationViolation> Validate(
        string outputContent,
        int? maxTokensConfigured,
        int tokensGenerated)
    {
        var violations = new List<ValidationViolation>();

        CheckEmptyOutput(outputContent, violations);

        if (violations.Count > 0)
            return violations; // nincs értelme tovább ellenőrizni üres kimeneten

        CheckStopTokenLeaked(outputContent, violations);
        CheckTruncatedOutput(outputContent, violations);
        CheckTokenLimitProximity(maxTokensConfigured, tokensGenerated, violations);

        return violations;
    }

    private static void CheckEmptyOutput(
        string content,
        List<ValidationViolation> violations)
    {
        if (string.IsNullOrWhiteSpace(content))
            violations.Add(new ValidationViolation(
                RuleId: "EMPTY_OUTPUT",
                Layer: "Universal",
                Description: "A kimenet üres vagy csak whitespace karaktereket tartalmaz.",
                Severity: ViolationSeverity.Error,
                FixHint: "Az LLM nem generált kimenetet. Ellenőrizd a system promptot és az input összerakást."));
    }

    private static void CheckStopTokenLeaked(
        string content,
        List<ValidationViolation> violations)
    {
        foreach (var token in KnownStopTokens)
        {
            if (!content.Contains(token, StringComparison.Ordinal))
                continue;

            violations.Add(new ValidationViolation(
                RuleId: "STOP_TOKEN_LEAKED",
                Layer: "Universal",
                Description: $"Stop token szerepel a kimenetben: '{token}'.",
                Severity: ViolationSeverity.Error,
                FixHint: $"A '{token}' stop token bekerült a generált fájlba. "
                       + "Szűrd ki az InferenceWorkerben a token írás előtt."));
            return; // elég az első találat
        }
    }

    private static void CheckTruncatedOutput(
        string content,
        List<ValidationViolation> violations)
    {
        var lastLine = content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();

        if (lastLine is null)
            return;

        // Truncation jelei: nem teljes mondat (nincs lezáró írásjel vagy markdown elem)
        var endsCorrectly =
            lastLine.EndsWith('.') ||
            lastLine.EndsWith(':') ||
            lastLine.EndsWith('*') ||
            lastLine.EndsWith('-') ||
            lastLine.EndsWith('`') ||
            lastLine.EndsWith(')') ||
            lastLine.EndsWith('>') ||
            lastLine.StartsWith('#');  // heading = teljes sor

        if (!endsCorrectly && lastLine.Length > 20)
        {
            violations.Add(new ValidationViolation(
                RuleId: "TRUNCATED_OUTPUT",
                Layer: "Universal",
                Description: $"A kimenet csonkítva látszik. Utolsó sor: \"{lastLine[..Math.Min(60, lastLine.Length)]}...\"",
                Severity: ViolationSeverity.Error,
                FixHint: "A max_tokens limit elérése miatt a kimenet nem fejeződött be. "
                       + "Növeld a max_tokens értéket a step agent configban."));
        }
    }

    private static void CheckTokenLimitProximity(
        int? maxTokensConfigured,
        int tokensGenerated,
        List<ValidationViolation> violations)
    {
        if (maxTokensConfigured is null or 0)
            return;

        var threshold = (int)(maxTokensConfigured.Value * TokenLimitProximityThreshold);

        if (tokensGenerated >= threshold)
            violations.Add(new ValidationViolation(
                RuleId: "TOKEN_LIMIT_PROXIMITY",
                Layer: "Universal",
                Description: $"Generált tokenek ({tokensGenerated}) elérte a max_tokens "
                           + $"({maxTokensConfigured}) {TokenLimitProximityThreshold:P0}-át. "
                           + "Truncation kockázat a következő futásokon.",
                Severity: ViolationSeverity.Warning,
                FixHint: $"Fontold meg a max_tokens növelését (jelenlegi: {maxTokensConfigured})."));
    }
}