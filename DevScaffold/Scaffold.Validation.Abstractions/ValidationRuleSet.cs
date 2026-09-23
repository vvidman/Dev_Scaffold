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

namespace Scaffold.Validation.Abstractions;

/// <summary>
/// Declarative validation rule set, loaded from a yaml file.
/// Complements the per-step logic in code – does not replace it.
/// </summary>
public class ValidatorRuleSet
{
    /// <summary>The step's identifier – must match the step field of the step agent config.</summary>
    public string Step { get; init; } = string.Empty;

    public ValidatorRules Rules { get; init; } = new();
}

public class ValidatorRules
{
    /// <summary>Fields that must be present in every task.</summary>
    public List<string> RequiredFields { get; init; } = [];

    /// <summary>Task count constraints.</summary>
    public TaskCountConstraint? TaskCount { get; init; }

    /// <summary>Words/phrases whose presence signals a constraint violation.</summary>
    public List<string> ForbiddenKeywords { get; init; } = [];

    /// <summary>Files that may not appear under "Affected files". (optional)</summary>
    public List<string> ForbiddenAffectedFiles { get; init; } = [];

    /// <summary>Warning triggers (Warning – not auto-reject, surfaced to the human). (optional)</summary>
    public List<string> WarningKeywords { get; init; } = [];

    /// <summary>Expected field order. (optional)</summary>
    public List<string> FieldOrder { get; init; } = [];
}

public class TaskCountConstraint
{
    public int Min { get; init; } = 1;
    public int Max { get; init; } = int.MaxValue;
}
