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
/// Markdown code blockokat kinyerő implementáció.
///
/// Feldolgozási szabályok:
/// 1. Keres minden ``` ... ``` blokkot (fenced code block)
/// 2. Ha a blokk első sora a filepathHintPrefix-szel kezdődik,
///    az utána következő szöveg a RelativeFilePath (trimmelve)
///    és ez a sor NEM kerül bele a Content-be
/// 3. Ha nincs filepath hint: fallback "artifact_{N:D2}.{ext}" ahol
///    N a blokk sorszáma (1-től), ext a language-ből képzett kiterjesztés
/// 4. Ismeretlen language esetén az ext "txt"
///
/// Language → extension leképezés:
///   csharp, cs → cs
///   xml        → xml
///   json       → json
///   yaml, yml  → yaml
///   sql        → sql
///   bash, sh   → sh
///   egyéb      → txt
/// </summary>
public sealed class DefaultMarkdownArtifactExtractor : IMarkdownArtifactExtractor
{
    // Fenced code block: ```language\ncontent\n```
    // A language sor opcionális. Multiline, non-greedy content match.
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

        // Fallback névgenerálás
        var ext = LanguageExtensions.GetValueOrDefault(language, "txt");
        return ($"artifact_{index:D2}.{ext}", rawContent);
    }
}
