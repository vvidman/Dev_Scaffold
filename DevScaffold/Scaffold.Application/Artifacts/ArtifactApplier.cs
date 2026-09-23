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

public enum ArtifactApplyStatus { Copied, WouldCopy, SkippedUnsafePath }

public sealed record ArtifactApplyEntry(
    string StepFolder,
    string RelativePath,
    string? TargetPath,
    ArtifactApplyStatus Status);

public sealed record ArtifactApplyResult(
    IReadOnlyList<ArtifactApplyEntry> Entries,
    IReadOnlyList<string> SkippedFoldersWithoutArtifacts)
{
    public int CopiedCount => Entries.Count(e => e.Status == ArtifactApplyStatus.Copied);
}

/// <summary>
/// Copies files from {stepOutputFolder}/artifacts/ into the target project root.
/// Always overwrites (deliberate decision, see README "Applying Generated Artifacts").
/// Every target path is validated by ArtifactPathGuard.
/// Performs no console output – the caller renders the result.
/// </summary>
public static class ArtifactApplier
{
    public static ArtifactApplyResult Apply(
        string outputBasePath,
        IEnumerable<string> stepFolderNames,
        string projectRoot,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<ArtifactApplyEntry>();
        var skippedFolders = new List<string>();

        foreach (var folderName in stepFolderNames)
        {
            var artifactsPath = Path.Combine(outputBasePath, folderName, "artifacts");

            if (!Directory.Exists(artifactsPath))
            {
                skippedFolders.Add(folderName);
                continue;
            }

            var files = Directory.GetFiles(artifactsPath, "*", SearchOption.AllDirectories);

            foreach (var sourceFile in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(artifactsPath, sourceFile);

                if (!ArtifactPathGuard.TryResolveWithin(projectRoot, relativePath, out var targetPath))
                {
                    entries.Add(new ArtifactApplyEntry(
                        folderName, relativePath, null, ArtifactApplyStatus.SkippedUnsafePath));
                    continue;
                }

                if (dryRun)
                {
                    entries.Add(new ArtifactApplyEntry(
                        folderName, relativePath, targetPath, ArtifactApplyStatus.WouldCopy));
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(sourceFile, targetPath, overwrite: true);
                    entries.Add(new ArtifactApplyEntry(
                        folderName, relativePath, targetPath, ArtifactApplyStatus.Copied));
                }
            }
        }

        return new ArtifactApplyResult(entries, skippedFolders);
    }
}
