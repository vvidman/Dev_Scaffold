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

using NSubstitute;
using Scaffold.Validation;
using Scaffold.Validation.Abstractions;
using Scaffold.Validation.Validators;

namespace Scaffold.Tests.Validation;

[TestClass]
public sealed class UniversalValidationTests
{
    private static CompositeOutputValidator CreateValidator(params IStepOutputValidator[] stepValidators)
    {
        var registry = new StepValidatorRegistry(stepValidators);
        return new CompositeOutputValidator(registry);
    }

    [TestMethod]
    public void Validate_EmptyOutput_ReturnsOnlyEmptyOutputError()
    {
        var validator = CreateValidator();

        var result = validator.Validate("   ", "any_step", null, 0);

        Assert.IsFalse(result.Passed);
        Assert.HasCount(1, result.Violations);
        Assert.AreEqual("EMPTY_OUTPUT", result.Violations[0].RuleId);
    }

    [TestMethod]
    [DataRow("<|end|>")]
    [DataRow("<|endoftext|>")]
    [DataRow("<|im_end|>")]
    [DataRow("<|eot_id|>")]
    [DataRow("</s>")]
    [DataRow("<|-|>")]
    public void Validate_LeakedStopToken_ReturnsStopTokenLeaked(string stopToken)
    {
        var validator = CreateValidator();
        var content = $"Some valid content ending correctly.\n{stopToken}";

        var result = validator.Validate(content, "any_step", null, 0);

        Assert.IsFalse(result.Passed);
        CollectionAssert.Contains(
            result.Errors.Select(e => e.RuleId).ToList(), "STOP_TOKEN_LEAKED");
    }

    [TestMethod]
    public void Validate_LastLineCutMidSentence_ReturnsTruncatedOutput()
    {
        var validator = CreateValidator();
        var content = "# Heading\n\nThis is a long sentence that just stops in the middle without any punctuation at all";

        var result = validator.Validate(content, "any_step", null, 0);

        Assert.IsFalse(result.Passed);
        CollectionAssert.Contains(
            result.Errors.Select(e => e.RuleId).ToList(), "TRUNCATED_OUTPUT");
    }

    [TestMethod]
    public void Validate_LastLineIsHeading_IsNotTruncated()
    {
        var validator = CreateValidator();
        var content = "Some content.\n\n# Final Heading Without Punctuation And Long Enough";

        var result = validator.Validate(content, "any_step", null, 0);

        CollectionAssert.DoesNotContain(
            result.Errors.Select(e => e.RuleId).ToList(), "TRUNCATED_OUTPUT");
    }

    [TestMethod]
    public void Validate_ShortLastLineWithoutPunctuation_IsNotTruncated()
    {
        var validator = CreateValidator();
        var content = "Some content.\n\nShort end"; // <= 20 chars, no punctuation

        var result = validator.Validate(content, "any_step", null, 0);

        CollectionAssert.DoesNotContain(
            result.Errors.Select(e => e.RuleId).ToList(), "TRUNCATED_OUTPUT");
    }

    [TestMethod]
    public void Validate_TokensAt95PercentOfMax_AddsWarningButPasses()
    {
        var validator = CreateValidator();
        var content = "A complete sentence that ends correctly.";

        var result = validator.Validate(content, "any_step", 100, 95);

        Assert.IsTrue(result.Passed);
        CollectionAssert.Contains(
            result.Warnings.Select(w => w.RuleId).ToList(), "TOKEN_LIMIT_PROXIMITY");
    }

    [TestMethod]
    public void Validate_MaxTokensNotConfigured_NoProximityCheck()
    {
        var validator = CreateValidator();
        var content = "A complete sentence that ends correctly.";

        var result = validator.Validate(content, "any_step", null, 1000);

        Assert.IsTrue(result.Passed);
        Assert.AreEqual(0, result.Warnings.Count());
    }

    [TestMethod]
    public void Validate_UniversalError_StepValidatorStillRuns()
    {
        var stepValidator = Substitute.For<IStepOutputValidator>();
        stepValidator.StepId.Returns("my_step");
        stepValidator.Validate(Arg.Any<string>(), Arg.Any<ValidatorRuleSet?>())
            .Returns([new ValidationViolation("STEP_RULE", "StepSpecific", "step violation", ViolationSeverity.Error)]);

        var validator = CreateValidator(stepValidator);
        var content = $"Contains stop token.\n<|end|>";

        var result = validator.Validate(content, "my_step", null, 0);

        var ruleIds = result.Violations.Select(v => v.RuleId).ToList();
        CollectionAssert.Contains(ruleIds, "STOP_TOKEN_LEAKED");
        CollectionAssert.Contains(ruleIds, "STEP_RULE");
    }
}
