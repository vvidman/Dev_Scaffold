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

using Scaffold.Application.Interfaces;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using static System.Net.Mime.MediaTypeNames;

namespace Scaffold.Application;

/// <summary>
/// Az elfogadott task_breakdown markdown kimenetét egyedi task YAML fájlokra bontja.
///
/// Minden numbered task blokkból (pl. "1. Task Title") egy önálló
/// {stepOutputFolder}/tasks/task_01.yaml fájlt hoz létre, flat YAML struktúrával:
///   task_id, title, description, affected_files, dependencies
///
/// Csak az inline (vesszővel elválasztott) Affected files / Dependencies
/// formátumot kezeli. A felsorolásos bullet-list formátum nem támogatott.
///
/// Hiba esetén naplóz és visszatér – nem dobja tovább a kivételt,
/// mivel az elfogadott kimenetet nem invalidálhatja a YAML generálás sikertelensége.
/// </summary>
internal sealed class TaskBreakdownSplitter : IStepPostProcessor
{
    private readonly IScaffoldConsole _console;

    // Numbered heading: "1." vagy "1. ##" stb. – azonos a TaskBreakdownValidator regex-ével
    private static readonly Regex TaskHeadingRegex =
        new(@"^(\d+)\.\s", RegexOptions.Multiline | RegexOptions.Compiled);

    // "Affected files:" sor – azonos a TaskBreakdownValidator AffectedFilesLineRegex-ével
    private static readonly Regex AffectedFilesRegex =
        new(@"Affected files?:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DependenciesRegex =
        new(@"Dependencies?:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Labeled field kezdete (pl. "Description:", "Affected files:")
    private static readonly Regex LabeledFieldRegex =
        new(@"^\w[\w\s]*:", RegexOptions.Compiled);

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public string StepId => "task_breakdown";

    public TaskBreakdownSplitter(IScaffoldConsole console)
    {
        _console = console;
    }

    public async Task ProcessAsync(
        string acceptedFilePath,
        string stepOutputFolder,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(acceptedFilePath))
        {
            _console.WriteError(
                $"[SCAFFOLD WARNING] TaskBreakdownSplitter: fájl nem található: {acceptedFilePath}");
            return;
        }

        var content = await File.ReadAllTextAsync(acceptedFilePath, cancellationToken);
        var tasks = SplitIntoTaskBlocks(content);

        if (tasks.Count == 0)
        {
            _console.WriteError(
                "[SCAFFOLD WARNING] TaskBreakdownSplitter: nem találhatók numbered task blokkok a kimenetben.");
            return;
        }

        var tasksFolder = Path.Combine(stepOutputFolder, "tasks");
        Directory.CreateDirectory(tasksFolder);

        for (var i = 0; i < tasks.Count; i++)
        {
            var model = ParseTaskBlock(tasks[i]);
            var fileName = $"task_{(i + 1):D2}.yaml";
            var filePath = Path.Combine(tasksFolder, fileName);

            var yaml = YamlSerializer.Serialize(model);
            await File.WriteAllTextAsync(filePath, yaml, cancellationToken);
        }

        _console.WriteCli(
            $"[SCAFFOLD] {tasks.Count} task YAML fájl létrehozva: {tasksFolder}");
    }

    // ─────────────────────────────────────────────
    // Privát segédmetódusok
    // ─────────────────────────────────────────────

    private static IReadOnlyList<string> SplitIntoTaskBlocks(string content)
    {
        var matches = TaskHeadingRegex.Matches(content);

        if (matches.Count == 0)
            return [];

        var blocks = new List<string>(matches.Count);

        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            blocks.Add(content[start..end].Trim());
        }

        return blocks;
    }

    private static TaskYamlModel ParseTaskBlock(string block)
    {
        return new TaskYamlModel
        {
            Step = "coding",
            SystemPrompt = $"You are a senior .NET software developer. Your job is to implement the following \r\n" +
                           string.Join("\n", block.Split('\n').Skip(1)) + "\r\n" +
                           "You should save C# source files, as the Affected files collection declares.",
            OutputFormat = "C# source files (*.cs)",
            MaxTokens = 4000
        };
    }

    /// <summary>
    /// Eltávolítja a heading prefixet (pl. "1. " vagy "## 1. ").
    /// </summary>
    private static string StripHeadingPrefix(string line)
    {
        // Markdown heading jelölők eltávolítása
        var stripped = line.TrimStart('#', ' ');

        // Numbered prefix eltávolítása (pl. "1. ")
        var match = Regex.Match(stripped, @"^\d+\.\s*");
        return match.Success ? stripped[match.Length..].Trim() : stripped.Trim();
    }

    /// <summary>
    /// Kinyeri a labeled field értékét és comma-split listává alakítja.
    /// Ha az érték "None", "-" vagy üres, üres listát ad vissza.
    /// </summary>
    private static List<string> ExtractListField(string block, Regex fieldRegex)
    {
        var match = fieldRegex.Match(block);

        if (!match.Success)
            return [];

        var rawValue = match.Groups[1].Value.Trim();

        if (string.IsNullOrWhiteSpace(rawValue)
            || rawValue.Equals("None", StringComparison.OrdinalIgnoreCase)
            || rawValue == "-")
            return [];

        return rawValue
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

/// <summary>
/// Egy task YAML fájl tartalmát képviseli.
/// A YamlDotNet UnderscoredNamingConvention-nel szerializálja.
/// </summary>
internal sealed class TaskYamlModel
{
    public string Step { get; set; }
    public string SystemPrompt { get; set; }
    public string OutputFormat { get; set; }
    public int MaxTokens { get; init; } = 4000;
}
