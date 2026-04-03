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
/// Markdown szövegből artifact fájlok kinyerésének absztrakciója.
/// </summary>
public interface IMarkdownArtifactExtractor
{
    /// <summary>
    /// Kinyeri az összes code blockot a markdown szövegből.
    /// </summary>
    /// <param name="markdownContent">A feldolgozandó markdown szöveg.</param>
    /// <param name="filepathHintPrefix">
    /// A code block első sorában keresett prefix (pl. "// filepath:").
    /// Ha null, minden artifact fallback névgenerálást kap.
    /// </param>
    /// <returns>
    /// A kinyert artifactok listája. Üres lista ha nincs code block.
    /// Minden code block pontosan egy artifactot eredményez.
    /// </returns>
    IReadOnlyList<ExtractedArtifact> Extract(string markdownContent, string? filepathHintPrefix);
}
