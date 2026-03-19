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

using Scaffold.Validation.Abstract;
using System.Text.RegularExpressions;

namespace Scaffold.Validation.Steps;

/// <summary>
/// Task breakdown step kimenetének validálása.
///
/// Alapértelmezett szabályok (yaml nélkül is érvényesek):
///   MISSING_REQUIRED_FIELD    – required_fields hiánya egy taskból       (Error)
///   TASK_COUNT_VIOLATION      – task_count min/max sértés                (Error)
///   FORBIDDEN_KEYWORD         – tiltott kulcsszó a kimenetben            (Error)
///   FORBIDDEN_AFFECTED_FILE   – tiltott fájl az Affected files sorban   (Error)
///   DUPLICATE_TASK_HEADING    – azonos heading szöveg több tasknál       (Warning)
///
/// A yaml-ból töltött ValidatorRuleSet felülírja az alapértelmezéseket
/// ahol átfedés van, és kiegészíti ahol nincs.
/// </summary>
public sealed class TaskBreakdownValidator : IStepOutputValidator
{
    public string StepId => "task_breakdown";

    // Alapértelmezett required fields – yaml nélkül is érvényes
    private static readonly string[] DefaultRequiredFields =
    [
        "Affected files",
        "Dependencies",
        "Description"
    ];

    // Alapértelmezett forbidden affected files
    private static readonly string[] DefaultForbiddenAffectedFiles =
    [
        "IRepository.cs",
        "Repository.cs"
    ];

    // Alapértelmezett task count korlátok
    private const int DefaultTaskCountMin = 1;
    private const int DefaultTaskCountMax = 15;

    // Numbered heading pattern: "1." vagy "1. ##" vagy "## 1."
    private static readonly Regex TaskHeadingRegex =
        new(@"^(\d+)\.\s", RegexOptions.Multiline | RegexOptions.Compiled);

    // "Affected files:" sor pattern
    private static readonly Regex AffectedFilesLineRegex =
        new(@"Affected files?:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<ValidationViolation> Validate(
        string outputContent,
        ValidatorRuleSet? ruleSet)
    {
        var violations = new List<ValidationViolation>();
        var rules = ruleSet?.Rules;

        var requiredFields = rules?.RequiredFields.Count > 0
            ? rules.RequiredFields
            : [.. DefaultRequiredFields];

        var forbiddenFiles = rules?.ForbiddenAffectedFiles.Count > 0
            ? rules.ForbiddenAffectedFiles
            : [.. DefaultForbiddenAffectedFiles];

        var forbiddenKeywords = rules?.ForbiddenKeywords ?? [];

        var taskCountMin = rules?.TaskCount?.Min ?? DefaultTaskCountMin;
        var taskCountMax = rules?.TaskCount?.Max ?? DefaultTaskCountMax;

        var tasks = SplitIntoTasks(outputContent);

        CheckTaskCount(tasks, taskCountMin, taskCountMax, violations);
        CheckRequiredFields(tasks, requiredFields, violations);
        CheckForbiddenAffectedFiles(outputContent, forbiddenFiles, violations);
        CheckForbiddenKeywords(outputContent, forbiddenKeywords, violations);
        CheckDuplicateHeadings(tasks, violations);

        return violations;
    }

    // ─────────────────────────────────────────────
    // Privát ellenőrzések
    // ─────────────────────────────────────────────

    private static void CheckTaskCount(
        IReadOnlyList<string> tasks,
        int min,
        int max,
        List<ValidationViolation> violations)
    {
        var count = tasks.Count;

        if (count < min || count > max)
            violations.Add(new ValidationViolation(
                RuleId: "TASK_COUNT_VIOLATION",
                Layer: "StepSpecific",
                Description: $"Task count: {count}. Elvárt: {min}–{max}.",
                Severity: ViolationSeverity.Error,
                FixHint: $"Generálj {min} és {max} közötti számú taskot. "
                       + $"Jelenlegi: {count}."));
    }

    private static void CheckRequiredFields(
        IReadOnlyList<string> tasks,
        IReadOnlyList<string> requiredFields,
        List<ValidationViolation> violations)
    {
        for (var i = 0; i < tasks.Count; i++)
        {
            var taskContent = tasks[i];
            var taskNumber = i + 1;

            foreach (var field in requiredFields)
            {
                if (!taskContent.Contains(field, StringComparison.OrdinalIgnoreCase))
                    violations.Add(new ValidationViolation(
                        RuleId: "MISSING_REQUIRED_FIELD",
                        Layer: "StepSpecific",
                        Description: $"Task {taskNumber}: hiányzó mező: \"{field}\".",
                        Severity: ViolationSeverity.Error,
                        FixHint: $"Task {taskNumber}-nek tartalmaznia kell egy \"{field}:\" sort."));
            }
        }
    }

    private static void CheckForbiddenAffectedFiles(
        string content,
        IReadOnlyList<string> forbiddenFiles,
        List<ValidationViolation> violations)
    {
        var affectedFilesMatches = AffectedFilesLineRegex.Matches(content);

        foreach (Match match in affectedFilesMatches)
        {
            var line = match.Value;

            foreach (var forbidden in forbiddenFiles)
            {
                // Substring match helyett: szóhatár alapú egyezés
                // "CachingRepository.cs" NEM egyezik "Repository.cs"-re
                // "Repository.cs" IGEN egyezik "Repository.cs"-re
                var pattern = $@"(?<![A-Za-z]){Regex.Escape(forbidden)}";
                if (Regex.IsMatch(line, pattern, RegexOptions.IgnoreCase))
                    violations.Add(new ValidationViolation(
                        RuleId: "FORBIDDEN_AFFECTED_FILE",
                        Layer: "StepSpecific",
                        Description: $"Tiltott fájl az Affected files sorban: \"{forbidden}\".",
                        Severity: ViolationSeverity.Error,
                        FixHint: $"\"{forbidden}\" módosítása tiltott (Open/Closed Principle). "
                               + "A változtatást a decorator osztályba kell irányítani."));
            }
        }
    }

    private static void CheckForbiddenKeywords(
        string content,
        IReadOnlyList<string> forbiddenKeywords,
        List<ValidationViolation> violations)
    {
        foreach (var keyword in forbiddenKeywords)
        {
            if (content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                violations.Add(new ValidationViolation(
                    RuleId: "FORBIDDEN_KEYWORD",
                    Layer: "StepSpecific",
                    Description: $"Tiltott kulcsszó a kimenetben: \"{keyword}\".",
                    Severity: ViolationSeverity.Error,
                    FixHint: $"A kimenet tartalmazza a tiltott \"{keyword}\" kifejezést. "
                           + "Távolítsd el az erre vonatkozó taskot vagy fogalmazd át."));
        }
    }

    private static void CheckDuplicateHeadings(
        IReadOnlyList<string> tasks,
        List<ValidationViolation> violations)
    {
        var headings = tasks
            .Select(t => t.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty)
            .Where(h => !string.IsNullOrEmpty(h))
            .ToList();

        var duplicates = headings
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var dup in duplicates)
            violations.Add(new ValidationViolation(
                RuleId: "DUPLICATE_TASK_HEADING",
                Layer: "StepSpecific",
                Description: $"Duplikált task heading: \"{dup}\".",
                Severity: ViolationSeverity.Warning,
                FixHint: "Összevond az ismétlődő taskokat egybe, vagy adj eltérő nevet."));
    }

    /// <summary>
    /// A kimenet szövegét szétbontja egyedi task blokkokra a numbered heading alapján.
    /// </summary>
    private static IReadOnlyList<string> SplitIntoTasks(string content)
    {
        var matches = TaskHeadingRegex.Matches(content);

        if (matches.Count == 0)
            return [];

        var tasks = new List<string>();

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            tasks.Add(content[start..end].Trim());
        }

        return tasks;
    }
}