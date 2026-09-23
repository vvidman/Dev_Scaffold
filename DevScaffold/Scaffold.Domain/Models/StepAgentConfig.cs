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
/// Configuration of a single AI agent step.
/// The content of the yaml file referenced by the pipeline's steps[].config field.
/// Defines the agent's system prompt and the expected output format.
/// </summary>
public class StepAgentConfig
{
    public string OutputFormat { get; init; } = "markdown";

    /// <summary>
    /// The step's identifier. Also determines the output folder name and the event names.
    /// </summary>
    public required string Step { get; init; }

    /// <summary>
    /// The AI system prompt for this step.
    /// Defines the AI's role and behavioral rules.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// Maximum number of tokens that may be generated.
    /// Protects against repetition loops.
    ///
    /// Recommended values:
    ///   task_breakdown:  800–1200
    ///   code_generation: 2000–4000
    ///   code_review:     1000–2000
    ///   documentation:   1500–2500
    ///
    /// If not set (null), the backend's default applies.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// The filepath hint prefix used in code blocks.
    /// E.g. "// filepath:" – this is what IMarkdownArtifactExtractor looks for.
    /// If null, the extractor falls back to generated names.
    /// Only needed for artifact-generating steps (e.g. coding, test_generation).
    /// </summary>
    public string? FilepathHintPrefix { get; init; }
}
