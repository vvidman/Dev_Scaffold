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
/// Represents the full content of Scaffold.CLI.yaml.
/// Loaded from the yaml file next to the exe – project-level configuration.
/// </summary>
public sealed class CliProjectConfig
{
    public string HostBinaryPath { get; init; } = "";
    public string Models { get; init; } = "";
    public string PipeName { get; init; } = "";
    public string Output { get; init; } = "./output";

    /// <summary>
    /// The project's main context – the base input for every step.
    /// Defined once, applies globally to all steps.
    /// </summary>
    public string ProjectContext { get; init; } = "";
    public Dictionary<string, StepCliConfig> Steps { get; init; } = new();

    /// <summary>
    /// The root folder of the actual target project — the --apply command
    /// copies the contents of artifacts/ here. Required only when using --apply.
    /// </summary>
    public string? ProjectRoot { get; init; }
}

/// <summary>
/// A single step's configuration entry in the steps: section of Scaffold.CLI.yaml.
/// </summary>
public sealed class StepCliConfig
{
    /// <summary>Path to the step agent YAML config file (e.g. task_breakdown_agent.yaml).</summary>
    public string InputConfig { get; init; } = "";

    /// <summary>Path to the validator YAML config file. Optional if the step has no validator.</summary>
    public string? ValidatorConfig { get; init; }

    /// <summary>The model alias defined in models.yaml (e.g. qwen2.5-coder-7b-instruct).</summary>
    public string ModelAlias { get; init; } = "";

    /// <summary>Path to the step's input YAML file (e.g. ./input.yaml or the previous step's output).</summary>
    public string Input { get; init; } = "";
}
