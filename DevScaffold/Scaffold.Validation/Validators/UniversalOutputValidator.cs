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

namespace Scaffold.Validation.Validators;

/// <summary>
/// Checks that run on every output, independent of step type.
///
/// Rules:
///   EMPTY_OUTPUT           – empty or whitespace-only output                (Error)
///   STOP_TOKEN_LEAKED      – an LLM stop token leaked into the file         (Error)
///   TRUNCATED_OUTPUT       – output is cut off mid-sentence/mid-structure   (Error)
///   TOKEN_LIMIT_PROXIMITY  – tokensGenerated >= maxTokens * 0.95            (Warning)
/// </summary>
/// <remarks>
/// Deliberately does not implement IOutputValidator.
/// Internal helper class – used exclusively by CompositeOutputValidator.
/// Do NOT register directly in DI.
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
    /// Token limit proximity threshold: if the number of generated tokens reaches
    /// 95% of max_tokens, a Warning is added to the report – even if not truncated.
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
            return violations; // no point checking further on empty output

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
                Description: "The output is empty or contains only whitespace characters.",
                Severity: ViolationSeverity.Error,
                FixHint: "The LLM did not generate any output. Check the system prompt and the input assembly."));
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
                Description: $"Stop token present in the output: '{token}'.",
                Severity: ViolationSeverity.Error,
                FixHint: $"The '{token}' stop token leaked into the generated file. "
                       + "Filter it out in InferenceWorker before writing the token."));
            return; // one match is enough
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

        // Signs of truncation: not a complete sentence (no closing punctuation or markdown element)
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
                Description: $"The output appears truncated. Last line: \"{lastLine[..Math.Min(60, lastLine.Length)]}...\"",
                Severity: ViolationSeverity.Error,
                FixHint: "The output did not finish because the max_tokens limit was reached. "
                       + "Increase max_tokens in the step agent config."));
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
                Description: $"Generated tokens ({tokensGenerated}) reached "
                           + $"{TokenLimitProximityThreshold:P0} of max_tokens ({maxTokensConfigured}). "
                           + "Truncation risk on subsequent runs.",
                Severity: ViolationSeverity.Warning,
                FixHint: $"Consider increasing max_tokens (currently: {maxTokensConfigured})."));
    }
}