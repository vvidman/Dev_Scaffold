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
/// Egy validációs futás összesített eredménye.
/// Passed = true csak akkor, ha nincs Error súlyosságú violation.
/// Warning-ok jelenléte nem akadályozza meg a human validációt.
/// </summary>
public record OutputValidationResult(
    bool Passed,
    IReadOnlyList<ValidationViolation> Violations)
{
    public IEnumerable<ValidationViolation> Errors =>
        Violations.Where(v => v.Severity == ViolationSeverity.Error);

    public IEnumerable<ValidationViolation> Warnings =>
        Violations.Where(v => v.Severity == ViolationSeverity.Warning);

    public static OutputValidationResult Success() =>
        new(true, []);

    public static OutputValidationResult FromViolations(IReadOnlyList<ValidationViolation> violations)
    {
        var passed = violations.All(v => v.Severity != ViolationSeverity.Error);
        return new(passed, violations);
    }
}