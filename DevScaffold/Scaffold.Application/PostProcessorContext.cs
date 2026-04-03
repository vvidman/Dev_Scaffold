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
/// Az IStepPostProcessor.ProcessAsync hívásának teljes kontextusa.
/// Bővíthető újabb mezőkkel anélkül hogy az interface aláírása változna.
/// </summary>
public sealed record PostProcessorContext(
    /// <summary>Az elfogadott (Accept/Edit) kimeneti fájl teljes elérési útja.</summary>
    string AcceptedFilePath,
    /// <summary>A step generációs mappa (pl. .../task_breakdown_1/).</summary>
    string StepOutputFolder,
    /// <summary>A projekt gyökérmappája — --apply művelethez és artifact célútvonalhoz.</summary>
    string ProjectRootPath,
    /// <summary>A step azonosítója (pl. "task_breakdown").</summary>
    string StepId,
    /// <summary>A generáció sorszáma (1-től indul).</summary>
    int Generation,
    /// <summary>
    /// Az opcionális --input override fájl elérési útja.
    /// Ha meg volt adva, ez az egyedi taszk YAML (pl. tasks/task_01.yaml).
    /// Ha null, a futás a globális project_context-tel ment.
    /// </summary>
    string? InputOverridePath,
    /// <summary>
    /// A code blockokban keresett fájlútvonal hint prefix.
    /// A step agent config filepath_hint_prefix mezőjéből érkezik.
    /// Ha null, az IMarkdownArtifactExtractor fallback névgenerálást használ.
    /// </summary>
    string? FilepathHintPrefix,
    /// <summary>Törlési token.</summary>
    CancellationToken CancellationToken);
