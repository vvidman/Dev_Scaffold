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

namespace Scaffold.Application.Output;

/// <summary>
/// Computes the next generation number for a step output folder.
/// Filesystem-based and therefore correct across CLI process restarts
/// (ADR-CLI #16). Takes the maximum existing number + 1, not the count,
/// so sparse sequences (a deleted folder) are handled correctly.
///
/// Folder naming: {stepId}_{generation}, e.g. task_breakdown_3.
/// </summary>
public static class GenerationCalculator
{
    public static int ComputeNext(string outputBasePath, string stepId)
    {
        if (!Directory.Exists(outputBasePath))
            return 1;

        var existing = Directory.GetDirectories(
            outputBasePath,
            $"{stepId}_*",
            SearchOption.TopDirectoryOnly);

        var maxGeneration = existing
            .Select(dir => Path.GetFileName(dir))
            .Select(name => TryParseGeneration(name, stepId))
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .DefaultIfEmpty(0)
            .Max();

        return maxGeneration + 1;
    }

    /// <summary>Returns the generation number, or null if the folder name does not match {stepId}_{int}.</summary>
    public static int? TryParseGeneration(string folderName, string stepId)
    {
        var prefix = $"{stepId}_";
        if (!folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var suffix = folderName[prefix.Length..];
        return int.TryParse(suffix, out var n) ? n : null;
    }
}
