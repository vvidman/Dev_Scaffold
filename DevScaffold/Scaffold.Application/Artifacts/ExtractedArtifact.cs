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
/// A single artifact extracted from a markdown code block.
/// </summary>
public sealed record ExtractedArtifact(
    /// <summary>
    /// The target file's path, relative to the project root.
    /// E.g. "src/Services/FooService.cs"
    /// Comes from the filepath hint, or is produced by fallback name generation.
    /// </summary>
    string RelativeFilePath,
    /// <summary>
    /// The code block's language identifier (e.g. "csharp", "xml", "json").
    /// Empty string if the code block has no language.
    /// </summary>
    string Language,
    /// <summary>The code block's content, without leading/trailing whitespace.</summary>
    string Content);
