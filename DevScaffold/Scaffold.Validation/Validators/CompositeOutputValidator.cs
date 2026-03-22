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
/// Az UniversalOutputValidator és a StepValidatorRegistry eredményeit kombinálja.
/// Ez az IOutputValidator egyetlen DI-ban regisztrált implementációja.
///
/// Futási sorrend:
///   1. UniversalOutputValidator (belső, nem publikus kontraktum)
///   2. IStepOutputValidator (ha van regisztrált validator az adott step_id-hoz)
///
/// Ha az Universal Error-t talál, a per-step validator is lefut –
/// minden violation összegyűlik a teljes képhez.
/// </summary>
public sealed class CompositeOutputValidator : IOutputValidator
{
    private readonly UniversalOutputValidator _universal = new();
    private readonly StepValidatorRegistry _registry;

    public CompositeOutputValidator(StepValidatorRegistry registry)
    {
        _registry = registry;
    }

    public OutputValidationResult Validate(
        string outputContent,
        string stepId,
        int? maxTokensConfigured,
        int tokensGenerated,
        ValidatorRuleSet? ruleSet = null)
    {
        var allViolations = new List<ValidationViolation>();

        // 1. Univerzális ellenőrzések
        var universalViolations = _universal.Validate(
            outputContent,
            maxTokensConfigured,
            tokensGenerated);

        allViolations.AddRange(universalViolations);

        // 2. Per-step ellenőrzések – üres kimeneten nincs értelme futtatni
        if (!string.IsNullOrWhiteSpace(outputContent))
        {
            var stepValidator = _registry.Resolve(stepId);

            if (stepValidator is not null)
            {
                var stepViolations = stepValidator.Validate(outputContent, ruleSet);
                allViolations.AddRange(stepViolations);
            }
        }

        return OutputValidationResult.FromViolations(allViolations);
    }
}