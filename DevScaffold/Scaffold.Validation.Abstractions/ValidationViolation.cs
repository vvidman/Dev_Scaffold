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

public enum ViolationSeverity
{
    Warning,
    Error
}

/// <summary>
/// Description of a single validation rule violation.
/// </summary>
/// <param name="RuleId">Machine-readable identifier, e.g. "STOP_TOKEN_LEAKED".</param>
/// <param name="Layer">Where it came from: "Universal" or "StepSpecific".</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="Severity">Error = auto-reject; Warning = surfaced to the human.</param>
/// <param name="FixHint">Optional: included in the error-driven refinement prompt on Error.</param>
public record ValidationViolation(
    string RuleId,
    string Layer,
    string Description,
    ViolationSeverity Severity,
    string? FixHint = null);