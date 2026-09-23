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
using Scaffold.Application;
using Scaffold.Application.Interfaces;
using Scaffold.Validation.Abstractions;

namespace Scaffold.Tests.Refinement;

[TestClass]
public sealed class RefinementStrategyTests
{
    private readonly RefinementStrategy _strategy = new();

    [TestMethod]
    public void BuildAutoRejectionClarification_ErrorsWithFixHints_PrefixedWithAutoAndListsRuleIds()
    {
        var result = new OutputValidationResult(false,
        [
            new ValidationViolation("STOP_TOKEN_LEAKED", "Universal", "desc", ViolationSeverity.Error, "Remove the stop token."),
            new ValidationViolation("EMPTY_OUTPUT", "Universal", "desc", ViolationSeverity.Error, "Regenerate the output."),
        ]);

        var clarification = _strategy.BuildAutoRejectionClarification(result);

        Assert.IsTrue(clarification.StartsWith("[AUTO]"));
        StringAssert.Contains(clarification, "[STOP_TOKEN_LEAKED] Remove the stop token.");
        StringAssert.Contains(clarification, "[EMPTY_OUTPUT] Regenerate the output.");
    }

    [TestMethod]
    public void BuildAutoRejectionClarification_NoFixHints_UsesFallbackText()
    {
        var result = new OutputValidationResult(false,
        [
            new ValidationViolation("SOME_RULE", "Universal", "desc", ViolationSeverity.Error, FixHint: null),
        ]);

        var clarification = _strategy.BuildAutoRejectionClarification(result);

        StringAssert.Contains(clarification, "Automatic validation failed");
    }

    [TestMethod]
    public void BuildRefinedSystemPrompt_AutoClarification_UsesAutomaticRejectionHeader()
    {
        var logger = Substitute.For<IAuditLogger>();

        var prompt = _strategy.BuildRefinedSystemPrompt(logger, "original prompt", "[AUTO]\n- [RULE] fix it");

        StringAssert.Contains(prompt, "automatically rejected due to rule violations");
    }

    [TestMethod]
    public void BuildRefinedSystemPrompt_HumanClarification_UsesHumanReviewerHeader()
    {
        var logger = Substitute.For<IAuditLogger>();

        var prompt = _strategy.BuildRefinedSystemPrompt(logger, "original prompt", "Please add more detail.");

        StringAssert.Contains(prompt, "rejected by the human reviewer");
    }

    [TestMethod]
    public void BuildRefinedSystemPrompt_KeepsOriginalPromptFirst()
    {
        var logger = Substitute.For<IAuditLogger>();

        var prompt = _strategy.BuildRefinedSystemPrompt(logger, "ORIGINAL_PROMPT_TEXT", "clarification");

        Assert.IsTrue(prompt.StartsWith("ORIGINAL_PROMPT_TEXT"));
    }

    [TestMethod]
    public void BuildRefinedSystemPrompt_LogsRefinementToAudit()
    {
        var logger = Substitute.For<IAuditLogger>();

        _strategy.BuildRefinedSystemPrompt(logger, "original prompt", "clarification");

        logger.Received(1).Log(AuditEvent.Refinement, Arg.Any<string>());
    }
}
