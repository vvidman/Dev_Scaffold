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

using Scaffold.Application.Interfaces;

namespace Scaffold.Application.Artifacts;

/// <summary>
/// Extracts the code blocks from the coding step's accepted markdown output
/// and writes them into the artifacts/ subfolder.
///
/// Output structure:
///   {StepOutputFolder}/artifacts/{RelativeFilePath}
///   E.g. .../coding_1/artifacts/src/Services/FooService.cs
///
/// Logs and returns on error – a failed artifact extraction must not
/// invalidate the accepted output.
/// </summary>
public sealed class CodingOutputExtractor : IStepPostProcessor
{
    private readonly IMarkdownArtifactExtractor _extractor;
    private readonly IScaffoldConsole _console;

    public string StepId => "coding";

    public CodingOutputExtractor(
        IMarkdownArtifactExtractor extractor,
        IScaffoldConsole console)
    {
        _extractor = extractor;
        _console = console;
    }

    public async Task ProcessAsync(PostProcessorContext context)
    {
        var markdownContent = await File.ReadAllTextAsync(
            context.AcceptedFilePath, context.CancellationToken);

        var artifacts = _extractor.Extract(markdownContent, context.FilepathHintPrefix);

        if (artifacts.Count == 0)
        {
            _console.WriteSession(
                "[POST] Coding: no code block found in the output.");
            return;
        }

        var artifactsRoot = Path.Combine(context.StepOutputFolder, "artifacts");

        var written = 0;
        var skipped = 0;

        foreach (var artifact in artifacts)
        {
            if (!ArtifactPathGuard.TryResolveWithin(artifactsRoot, artifact.RelativeFilePath, out var targetPath))
            {
                _console.WriteValidation(
                    $"[POST] Artifact skipped – unsafe path outside artifacts/: \"{artifact.RelativeFilePath}\"");
                skipped++;
                continue;
            }

            var targetDir = Path.GetDirectoryName(targetPath)!;

            Directory.CreateDirectory(targetDir);
            await File.WriteAllTextAsync(targetPath, artifact.Content, context.CancellationToken);

            _console.WriteSession($"[POST] Artifact saved: {artifact.RelativeFilePath}");
            written++;
        }

        _console.WriteSession(
            $"[POST] Coding: {written} written, {skipped} skipped → {artifactsRoot}");
    }
}
