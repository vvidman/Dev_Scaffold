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
using Scaffold.Validation.Steps;

namespace Scaffold.Tests.Validation;

[TestClass]
public sealed class TaskBreakdownValidatorTests
{
    private readonly TaskBreakdownValidator _validator = new();

    private const string ValidTwoTaskOutput = """
        1. Add input validation
        Description: Validate the incoming request payload.
        Affected files: Services/RequestHandler.cs
        Dependencies: none

        2. Add logging
        Description: Log every processed request.
        Affected files: Services/Logger.cs
        Dependencies: 1
        """;

    [TestMethod]
    public void Validate_ValidOutput_NoViolations()
    {
        var violations = _validator.Validate(ValidTwoTaskOutput, ruleSet: null);

        Assert.IsEmpty(violations);
    }

    [TestMethod]
    public void Validate_TaskMissingRequiredField_ReturnsMissingRequiredField()
    {
        var output = """
            1. Add input validation
            Description: Validate the incoming request payload.
            Dependencies: none

            2. Add logging
            Description: Log every processed request.
            Affected files: Services/Logger.cs
            Dependencies: 1
            """;

        var violations = _validator.Validate(output, ruleSet: null);

        var missing = violations.Where(v => v.RuleId == "MISSING_REQUIRED_FIELD").ToList();
        Assert.HasCount(1, missing);
        StringAssert.Contains(missing[0].Description, "Task 1");
    }

    [TestMethod]
    public void Validate_TaskCountAboveMax_ReturnsTaskCountViolation()
    {
        var ruleSet = new ValidatorRuleSet
        {
            Step = "task_breakdown",
            Rules = new ValidatorRules { TaskCount = new TaskCountConstraint { Min = 1, Max = 1 } }
        };

        var violations = _validator.Validate(ValidTwoTaskOutput, ruleSet);

        CollectionAssert.Contains(violations.Select(v => v.RuleId).ToList(), "TASK_COUNT_VIOLATION");
    }

    [TestMethod]
    public void Validate_RuleSetOverridesDefaultRequiredFields()
    {
        var ruleSet = new ValidatorRuleSet
        {
            Step = "task_breakdown",
            Rules = new ValidatorRules { RequiredFields = ["Owner"] }
        };

        var violations = _validator.Validate(ValidTwoTaskOutput, ruleSet);

        // "Owner" required field missing from both tasks – "Description"/"Affected files"
        // are no longer checked because the rule set overrides the defaults.
        var missing = violations.Where(v => v.RuleId == "MISSING_REQUIRED_FIELD").ToList();
        Assert.HasCount(2, missing);
        Assert.IsTrue(missing.All(v => v.Description.Contains("Owner")));
    }

    [TestMethod]
    public void Validate_ForbiddenKeyword_ReturnsError()
    {
        var ruleSet = new ValidatorRuleSet
        {
            Step = "task_breakdown",
            Rules = new ValidatorRules { ForbiddenKeywords = ["TODO"] }
        };

        var output = ValidTwoTaskOutput + "\nTODO: fill in later";

        var violations = _validator.Validate(output, ruleSet);

        var forbidden = violations.Single(v => v.RuleId == "FORBIDDEN_KEYWORD");
        Assert.AreEqual(ViolationSeverity.Error, forbidden.Severity);
    }

    [TestMethod]
    public void Validate_WarningKeyword_ReturnsWarningNotError()
    {
        var ruleSet = new ValidatorRuleSet
        {
            Step = "task_breakdown",
            Rules = new ValidatorRules { WarningKeywords = ["maybe"] }
        };

        var output = ValidTwoTaskOutput + "\nmaybe this works";

        var violations = _validator.Validate(output, ruleSet);

        var warning = violations.Single(v => v.RuleId == "WARNING_KEYWORD");
        Assert.AreEqual(ViolationSeverity.Warning, warning.Severity);
    }

    [TestMethod]
    public void Validate_DuplicateHeadings_ReturnsWarning()
    {
        // Same heading line repeated (renumbering defect) – DUPLICATE_TASK_HEADING
        // compares the full first line of each split block, numeral included.
        var output = """
            1. Add input validation
            Description: First.
            Affected files: A.cs
            Dependencies: none

            1. Add input validation
            Description: Second.
            Affected files: B.cs
            Dependencies: none
            """;

        var violations = _validator.Validate(output, ruleSet: null);

        var duplicate = violations.Single(v => v.RuleId == "DUPLICATE_TASK_HEADING");
        Assert.AreEqual(ViolationSeverity.Warning, duplicate.Severity);
    }

    [TestMethod]
    public void Validate_ForbiddenAffectedFile_DoesNotMatchLongerFileName()
    {
        var ruleSet = new ValidatorRuleSet
        {
            Step = "task_breakdown",
            Rules = new ValidatorRules { ForbiddenAffectedFiles = ["Repository.cs"] }
        };

        var forbiddenHit = """
            1. Change repository directly
            Description: Bad change.
            Affected files: Repository.cs
            Dependencies: none
            """;

        var safeLongerNames = """
            1. Extend repository abstractions
            Description: Safe change.
            Affected files: IRepository.cs, CachingRepository.cs
            Dependencies: none
            """;

        var hitViolations = _validator.Validate(forbiddenHit, ruleSet);
        var safeViolations = _validator.Validate(safeLongerNames, ruleSet);

        CollectionAssert.Contains(hitViolations.Select(v => v.RuleId).ToList(), "FORBIDDEN_AFFECTED_FILE");
        CollectionAssert.DoesNotContain(safeViolations.Select(v => v.RuleId).ToList(), "FORBIDDEN_AFFECTED_FILE");
    }
}
