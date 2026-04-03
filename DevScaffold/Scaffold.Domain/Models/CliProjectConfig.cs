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

namespace Scaffold.Domain.Models;

/// <summary>
/// A Scaffold.CLI.yaml teljes tartalmát reprezentálja.
/// Az exe melletti yaml fájlból töltődik be, projekt-szintű konfiguráció.
/// </summary>
public sealed class CliProjectConfig
{
    public string HostBinaryPath { get; init; } = "";
    public string Models { get; init; } = "";
    public string PipeName { get; init; } = "";
    public string Output { get; init; } = "./output";
    
    /// <summary>
    /// A projekt fő kontextusa – minden stepnek ez az alap bemenete.
    /// Egyszer definiálva, globálisan érvényes az összes stepre.
    /// </summary>
    public string ProjectContext { get; init; } = "";
    public Dictionary<string, StepCliConfig> Steps { get; init; } = new();

    /// <summary>
    /// A valódi projekt gyökérmappája — az --apply parancs ide másolja
    /// az artifacts/ tartalmát. Csak --apply használatakor kötelező.
    /// </summary>
    public string? ProjectRoot { get; init; }
}

/// <summary>
/// Egy step konfigurációs bejegyzése a Scaffold.CLI.yaml steps: szekciójában.
/// </summary>
public sealed class StepCliConfig
{
    /// <summary>A step agent YAML config fájl útvonala (pl. task_breakdown_agent.yaml).</summary>
    public string InputConfig { get; init; } = "";

    /// <summary>A validator YAML config fájl útvonala. Elhagyható ha a stephez nincs validator.</summary>
    public string? ValidatorConfig { get; init; }

    /// <summary>A models.yaml-ben definiált model alias (pl. qwen2.5-coder-7b-instruct).</summary>
    public string ModelAlias { get; init; } = "";

    /// <summary>A step bemeneti YAML fájl útvonala (pl. ./input.yaml vagy előző step outputja).</summary>
    public string Input { get; init; } = "";
}