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
/// Splits the accepted task_breakdown markdown output into individual task YAML files.
///
/// Each numbered task block (e.g. "1. Task Title") becomes its own
/// {stepOutputFolder}/tasks/task_01.yaml file, with a flat YAML structure:
///   task_id, title, description, affected_files, dependencies
///
/// Only handles the inline (comma-separated) Affected files / Dependencies
/// format. The bullet-list format is not supported.
///
/// Logs and returns on error – does not rethrow, since a failed YAML
/// generation must not invalidate the accepted output.
/// </summary>
internal sealed class TaskBreakdownSplitter : IStepPostProcessor
{
    private readonly IScaffoldConsole _console;

    // Numbered heading: "1." or "1. ##" etc. – same as TaskBreakdownValidator's regex
    private static readonly Regex TaskHeadingRegex =
        new(@"^(\d+)\.\s", RegexOptions.Multiline | RegexOptions.Compiled);

    // "Affected files:" line – same as TaskBreakdownValidator's AffectedFilesLineRegex
    private static readonly Regex AffectedFilesRegex =
        new(@"Affected files?:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DependenciesRegex =
        new(@"Dependencies?:\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Start of a labeled field (e.g. "Description:", "Affected files:")
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

    public async Task ProcessAsync(PostProcessorContext context)
    {
        if (!File.Exists(context.AcceptedFilePath))
        {
            _console.WriteError(
                $"[SCAFFOLD WARNING] TaskBreakdownSplitter: file not found: {context.AcceptedFilePath}");
            return;
        }

        var content = await File.ReadAllTextAsync(context.AcceptedFilePath, context.CancellationToken);
        var tasks = SplitIntoTaskBlocks(content);

        if (tasks.Count == 0)
        {
            _console.WriteError(
                "[SCAFFOLD WARNING] TaskBreakdownSplitter: no numbered task blocks found in the output.");
            return;
        }

        var tasksFolder = Path.Combine(context.StepOutputFolder, "tasks");
        Directory.CreateDirectory(tasksFolder);

        for (var i = 0; i < tasks.Count; i++)
        {
            var model = ParseTaskBlock(tasks[i]);
            var fileName = $"task_{(i + 1):D2}.yaml";
            var filePath = Path.Combine(tasksFolder, fileName);

            var yaml = YamlSerializer.Serialize(model);
            await File.WriteAllTextAsync(filePath, yaml, context.CancellationToken);
        }

        _console.WriteCli(
            $"[SCAFFOLD] {tasks.Count} task YAML file(s) created: {tasksFolder}");
    }

    // ─────────────────────────────────────────────
    // Private helper methods
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
    /// Removes the heading prefix (e.g. "1. " or "## 1. ").
    /// </summary>
    private static string StripHeadingPrefix(string line)
    {
        // Remove markdown heading markers
        var stripped = line.TrimStart('#', ' ');

        // Remove the numbered prefix (e.g. "1. ")
        var match = Regex.Match(stripped, @"^\d+\.\s*");
        return match.Success ? stripped[match.Length..].Trim() : stripped.Trim();
    }

    /// <summary>
    /// Extracts the value of a labeled field and turns it into a comma-split list.
    /// If the value is "None", "-" or empty, returns an empty list.
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
/// Represents the content of a single task YAML file.
/// Serialized with YamlDotNet's UnderscoredNamingConvention.
/// </summary>
internal sealed class TaskYamlModel
{
    public required string Step { get; set; }
    public required string SystemPrompt { get; set; }
    public required string OutputFormat { get; set; }
    public int MaxTokens { get; init; } = 4000;
}
