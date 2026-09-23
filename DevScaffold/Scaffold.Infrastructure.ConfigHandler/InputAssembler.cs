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

using Scaffold.Application;
using Scaffold.Application.Interfaces;
using YamlDotNet.RepresentationModel;

namespace Scaffold.Infrastructure.ConfigHandler;

/// <summary>
/// Implementation responsible for assembling the input YAML file.
///
/// Reads the input yaml, finds all path references, checks that they
/// exist (fail fast), then assembles a full context string for the AI.
///
/// A path reference is any field whose name ends in "path"
/// within the YAML structure.
/// </summary>
public class InputAssembler : IInputAssembler
{
    public string Assemble(string inputYamlPath, string stepId, string? secondaryInputYamlPath = null)
    {
        if (!File.Exists(inputYamlPath))
            throw new FileNotFoundException(
                $"Input file not found: {inputYamlPath}");

        var yaml = File.ReadAllText(inputYamlPath);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(inputYamlPath)) ?? ".";

        ValidatePathReferences(yaml, baseDir, stepId);  // stepId instead of inputYamlPath
        var primaryContext = BuildContext(yaml, baseDir);

        if (string.IsNullOrEmpty(secondaryInputYamlPath))
            return primaryContext;

        if (!File.Exists(secondaryInputYamlPath))
            throw new FileNotFoundException(
                $"Secondary input file not found: {secondaryInputYamlPath}");

        var secondaryYaml = File.ReadAllText(secondaryInputYamlPath);
        var secondaryBaseDir = Path.GetDirectoryName(Path.GetFullPath(secondaryInputYamlPath)) ?? ".";

        ValidatePathReferences(secondaryYaml, secondaryBaseDir, stepId);
        var secondaryContext = BuildContext(secondaryYaml, secondaryBaseDir);

        return primaryContext + "\n\n---\n\n" + secondaryContext;
    }

    /// <summary>
    /// Reads through the YAML, finds all path references,
    /// and checks that the files exist.
    /// </summary>
    private static void ValidatePathReferences(string yaml, string baseDir, string stepId)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        if (stream.Documents.Count == 0) return;

        var root = stream.Documents[0].RootNode;
        var pathFields = CollectPathFields(root).ToList();

        foreach (var (fieldPath, pathValue) in pathFields)
        {
            var fullPath = ResolveFullPath(pathValue, baseDir);

            if (!File.Exists(fullPath))
                throw new ScaffoldInputValidationException(
                    stepId: stepId,
                    fieldName: fieldPath,
                    invalidPath: fullPath);
        }
    }

    /// <summary>
    /// Recursively collects every field whose key ends in "path" from the YAML.
    /// </summary>
    private static IEnumerable<(string FieldPath, string Value)> CollectPathFields(
        YamlNode node,
        string currentPath = "")
    {
        if (node is YamlMappingNode mapping)
        {
            foreach (var entry in mapping.Children)
            {
                var key = ((YamlScalarNode)entry.Key).Value ?? string.Empty;
                var childPath = string.IsNullOrEmpty(currentPath)
                    ? key
                    : $"{currentPath}.{key}";

                if (key.EndsWith("path", StringComparison.OrdinalIgnoreCase)
                    && entry.Value is YamlScalarNode scalar
                    && !string.IsNullOrWhiteSpace(scalar.Value)
                    && scalar.Value != "~")
                {
                    yield return (childPath, scalar.Value);
                }
                else
                {
                    foreach (var child in CollectPathFields(entry.Value, childPath))
                        yield return child;
                }
            }
        }
        else if (node is YamlSequenceNode sequence)
        {
            var index = 0;
            foreach (var item in sequence.Children)
            {
                foreach (var child in CollectPathFields(item, $"{currentPath}[{index}]"))
                    yield return child;
                index++;
            }
        }
    }

    /// <summary>
    /// Assembles the full context handed to the AI.
    /// Keeps the input YAML, inlining the content of the path references.
    /// </summary>
    private static string BuildContext(string yaml, string baseDir)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));

        if (stream.Documents.Count == 0)
            return yaml;

        var root = stream.Documents[0].RootNode;
        var fileContents = new List<string>();

        foreach (var (fieldPath, pathValue) in CollectPathFields(root))
        {
            var fullPath = ResolveFullPath(pathValue, baseDir);
            var content = File.ReadAllText(fullPath);
            var extension = Path.GetExtension(fullPath).TrimStart('.');

            fileContents.Add($"""
                ## File content: {pathValue}
                ```{extension}
                {content}
                ```
                """);
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("## Input configuration");
        builder.AppendLine("```yaml");
        builder.AppendLine(yaml);
        builder.AppendLine("```");

        if (fileContents.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Referenced files");
            foreach (var fc in fileContents)
            {
                builder.AppendLine(fc);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string ResolveFullPath(string path, string baseDir)
    {
        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(baseDir, path));
    }
}
