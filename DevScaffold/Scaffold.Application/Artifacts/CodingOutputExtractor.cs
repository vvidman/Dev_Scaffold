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
/// A coding step elfogadott markdown kimenetéből kinyeri a code blockokat
/// és az artifacts/ almappába írja őket.
///
/// Output struktúra:
///   {StepOutputFolder}/artifacts/{RelativeFilePath}
///   Pl. .../coding_1/artifacts/src/Services/FooService.cs
///
/// Hiba esetén naplóz és visszatér – az elfogadott kimenetet
/// nem invalidálhatja az artifact kinyerés sikertelensége.
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
                "[POST] Coding: nem található code block a kimenetben.");
            return;
        }

        var artifactsRoot = Path.Combine(context.StepOutputFolder, "artifacts");

        foreach (var artifact in artifacts)
        {
            var targetPath = Path.Combine(artifactsRoot, artifact.RelativeFilePath);
            var targetDir = Path.GetDirectoryName(targetPath)!;

            Directory.CreateDirectory(targetDir);
            await File.WriteAllTextAsync(targetPath, artifact.Content, context.CancellationToken);

            _console.WriteSession($"[POST] Artifact kimentve: {artifact.RelativeFilePath}");
        }

        _console.WriteSession(
            $"[POST] Coding: {artifacts.Count} artifact kimentve → {artifactsRoot}");
    }
}
