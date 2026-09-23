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

using System.Diagnostics.CodeAnalysis;

namespace Scaffold.Application.Artifacts;

/// <summary>
/// Resolves a relative path that originates from untrusted input (LLM output)
/// against a root directory, and guarantees the result stays inside that root.
///
/// Rejects:
///   - rooted/absolute paths ("C:\x", "/x", "\\server\share\x")
///   - paths that escape the root via ".." segments
///   - empty or whitespace-only paths
/// </summary>
public static class ArtifactPathGuard
{
    /// <summary>
    /// Returns true and the full target path if <paramref name="relativePath"/>
    /// resolves to a location inside <paramref name="rootDirectory"/>.
    /// </summary>
    public static bool TryResolveWithin(
        string rootDirectory,
        string relativePath,
        [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;

        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        var root = Path.GetFullPath(rootDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));

        // GetRelativePath handles case sensitivity per platform and
        // avoids the "C:\root" vs "C:\root-other" prefix-matching pitfall.
        var relativeToRoot = Path.GetRelativePath(root, candidate);

        if (relativeToRoot == ".." ||
            relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relativeToRoot.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeToRoot))
            return false;

        fullPath = candidate;
        return true;
    }
}
