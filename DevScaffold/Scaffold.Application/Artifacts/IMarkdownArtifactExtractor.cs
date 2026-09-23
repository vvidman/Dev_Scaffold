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

namespace Scaffold.Application.Artifacts;

/// <summary>
/// Abstraction for extracting artifact files from markdown text.
/// </summary>
public interface IMarkdownArtifactExtractor
{
    /// <summary>
    /// Extracts all code blocks from the markdown text.
    /// </summary>
    /// <param name="markdownContent">The markdown text to process.</param>
    /// <param name="filepathHintPrefix">
    /// The prefix looked for on the code block's first line (e.g. "// filepath:").
    /// If null, every artifact gets a fallback generated name.
    /// </param>
    /// <returns>
    /// The list of extracted artifacts. Empty list if there is no code block.
    /// Every code block results in exactly one artifact.
    /// </returns>
    IReadOnlyList<ExtractedArtifact> Extract(string markdownContent, string? filepathHintPrefix);
}
