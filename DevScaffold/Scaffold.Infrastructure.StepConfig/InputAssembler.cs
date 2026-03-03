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

namespace Scaffold.Infrastructure.StepConfig;

/// <summary>
/// Input YAML fájl összeszereléséért felelős implementáció.
///
/// Beolvassa az input yaml-t, megkeresi az összes path referenciát,
/// ellenőrzi hogy léteznek-e (fail fast), majd összeállít egy
/// teljes kontextus stringet az AI számára.
///
/// Path referenciának számít minden olyan mező aminek neve
/// "path"-ra végződik a YAML struktúrában.
/// </summary>
public class InputAssembler : IInputAssembler
{
    public string Assemble(string inputYamlPath)
    {
        if (!File.Exists(inputYamlPath))
            throw new FileNotFoundException(
                $"Input fájl nem található: {inputYamlPath}");

        var yaml = File.ReadAllText(inputYamlPath);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(inputYamlPath)) ?? ".";

        // Path referenciák validálása – fail fast
        ValidatePathReferences(yaml, baseDir, inputYamlPath);

        // Teljes kontextus összeállítása
        return BuildContext(yaml, baseDir);
    }

    /// <summary>
    /// Végigolvassa a YAML-t, megkeresi az összes path referenciát,
    /// és ellenőrzi hogy a fájlok léteznek-e.
    /// </summary>
    private static void ValidatePathReferences(string yaml, string baseDir, string inputYamlPath)
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
                    stepId: Path.GetFileNameWithoutExtension(inputYamlPath),
                    fieldName: fieldPath,
                    invalidPath: fullPath);
        }
    }

    /// <summary>
    /// Összegyűjti az összes "path" kulcsú mezőt a YAML-ből rekurzívan.
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
    /// Összeállítja az AI-nak átadott teljes kontextust.
    /// Az input YAML-t megtartja, a path referenciák tartalmát inline bővíti.
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
                ## Fájl tartalma: {pathValue}
                ```{extension}
                {content}
                ```
                """);
        }

        var builder = new System.Text.StringBuilder();
        builder.AppendLine("## Input konfiguráció");
        builder.AppendLine("```yaml");
        builder.AppendLine(yaml);
        builder.AppendLine("```");

        if (fileContents.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Hivatkozott fájlok");
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
