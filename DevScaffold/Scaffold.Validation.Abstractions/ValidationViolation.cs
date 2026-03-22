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
/// Egyetlen validációs szabálysértés leírása.
/// </summary>
/// <param name="RuleId">Gépileg olvasható azonosító, pl. "STOP_TOKEN_LEAKED".</param>
/// <param name="Layer">Honnan érkezett: "Universal" vagy "StepSpecific".</param>
/// <param name="Description">Ember-olvasható leírás.</param>
/// <param name="Severity">Error = auto-reject; Warning = human elé kerül kiemelve.</param>
/// <param name="FixHint">Opcionális: error-driven refinement promptba kerül Error esetén.</param>
public record ValidationViolation(
    string RuleId,
    string Layer,
    string Description,
    ViolationSeverity Severity,
    string? FixHint = null);