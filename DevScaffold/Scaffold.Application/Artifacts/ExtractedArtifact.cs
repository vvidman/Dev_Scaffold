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
/// Egyetlen markdown code blockból kinyert artifact.
/// </summary>
public sealed record ExtractedArtifact(
    /// <summary>
    /// A célfájl relatív útvonala a projekt gyökeréhez képest.
    /// Pl. "src/Services/FooService.cs"
    /// A filepath hint-ből érkezik, vagy fallback névgenerálással áll elő.
    /// </summary>
    string RelativeFilePath,
    /// <summary>
    /// A code block language azonosítója (pl. "csharp", "xml", "json").
    /// Üres string ha a code block language-et nem tartalmaz.
    /// </summary>
    string Language,
    /// <summary>A code block tartalma, vezető/záró whitespace nélkül.</summary>
    string Content);
