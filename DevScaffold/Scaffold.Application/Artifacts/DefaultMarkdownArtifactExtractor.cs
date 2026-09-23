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

using System.Text.RegularExpressions;

namespace Scaffold.Application.Artifacts;

/// <summary>
/// Extracts markdown code blocks.
///
/// Processing rules:
/// 1. Looks for every ``` ... ``` block (fenced code block)
/// 2. If the block's first line starts with filepathHintPrefix,
///    the text after it (trimmed) becomes RelativeFilePath
///    and that line is NOT included in Content
/// 3. If there is no filepath hint: falls back to "artifact_{N:D2}.{ext}" where
///    N is the block's index (starting at 1), ext is derived from the language
/// 4. For an unknown language, ext is "txt"
///
/// Language → extension mapping:
///   csharp, cs → cs
///   xml        → xml
///   json       → json
///   yaml, yml  → yaml
///   sql        → sql
///   bash, sh   → sh
///   other      → txt
/// </summary>
public sealed class DefaultMarkdownArtifactExtractor : IMarkdownArtifactExtractor
{
    // Fenced code block: ```language\ncontent\n```
    // The language line is optional. Multiline, non-greedy content match.
    private static readonly Regex CodeBlockRegex = new(
        @"^```(?<lang>[^\r\n]*)\r?\n(?<content>.*?)^```",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> LanguageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = "cs", ["cs"] = "cs",
            ["xml"]    = "xml",
            ["json"]   = "json",
            ["yaml"]   = "yaml", ["yml"] = "yaml",
            ["sql"]    = "sql",
            ["bash"]   = "sh",  ["sh"]  = "sh",
        };

    public IReadOnlyList<ExtractedArtifact> Extract(
        string markdownContent,
        string? filepathHintPrefix)
    {
        var results = new List<ExtractedArtifact>();
        var artifactIndex = 0;

        foreach (Match match in CodeBlockRegex.Matches(markdownContent))
        {
            artifactIndex++;
            var language = match.Groups["lang"].Value.Trim();
            var rawContent = match.Groups["content"].Value;

            var (filePath, content) = ExtractFilePath(
                rawContent, language, filepathHintPrefix, artifactIndex);

            if (string.IsNullOrWhiteSpace(content))
                continue;

            results.Add(new ExtractedArtifact(filePath, language, content.Trim()));
        }

        return results;
    }

    private static (string FilePath, string Content) ExtractFilePath(
        string rawContent,
        string language,
        string? hintPrefix,
        int index)
    {
        if (hintPrefix is not null)
        {
            var firstLineEnd = rawContent.IndexOf('\n');
            if (firstLineEnd > 0)
            {
                var firstLine = rawContent[..firstLineEnd].Trim();
                if (firstLine.StartsWith(hintPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var path = firstLine[hintPrefix.Length..].Trim();
                    var content = rawContent[(firstLineEnd + 1)..];
                    if (!string.IsNullOrWhiteSpace(path))
                        return (path, content);
                }
            }
        }

        // Fallback name generation
        var ext = LanguageExtensions.GetValueOrDefault(language, "txt");
        return ($"artifact_{index:D2}.{ext}", rawContent);
    }
}
