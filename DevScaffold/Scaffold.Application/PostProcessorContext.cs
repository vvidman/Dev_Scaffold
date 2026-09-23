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

namespace Scaffold.Application;

/// <summary>
/// Full context of a single IStepPostProcessor.ProcessAsync call.
/// Extensible with new fields without changing the interface signature.
/// </summary>
public sealed record PostProcessorContext(
    /// <summary>Full path of the accepted (Accept/Edit) output file.</summary>
    string AcceptedFilePath,
    /// <summary>The step's generation folder (e.g. .../task_breakdown_1/).</summary>
    string StepOutputFolder,
    /// <summary>The project's root folder — for the --apply operation and artifact target paths.</summary>
    string ProjectRootPath,
    /// <summary>The step's identifier (e.g. "task_breakdown").</summary>
    string StepId,
    /// <summary>The generation number (starts at 1).</summary>
    int Generation,
    /// <summary>
    /// Path to the optional --input override file.
    /// If given, this is the per-task YAML (e.g. tasks/task_01.yaml).
    /// If null, the run went with the global project_context.
    /// </summary>
    string? InputOverridePath,
    /// <summary>
    /// The filepath hint prefix looked for in code blocks.
    /// Comes from the step agent config's filepath_hint_prefix field.
    /// If null, IMarkdownArtifactExtractor falls back to generated names.
    /// </summary>
    string? FilepathHintPrefix,
    /// <summary>Cancellation token.</summary>
    CancellationToken CancellationToken);
