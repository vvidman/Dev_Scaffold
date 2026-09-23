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

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Abstraction responsible for assembling input files.
/// Reads the YAML input schema, resolves the path references,
/// and assembles the prompt context handed to the AI.
///
/// Fail fast: if any path reference does not exist or cannot be read,
/// throws an exception – the AI must not start with a partial context.
/// </summary>
public interface IInputAssembler
{
    /// <summary>
    /// Reads the input yaml file, resolves all path references,
    /// and returns a full context string that can be passed to the AI.
    /// If secondaryInputYamlPath is given, its content is appended after
    /// the primary input with a "\n\n---\n\n" separator.
    /// </summary>
    /// <exception cref="ScaffoldInputValidationException">
    /// If any path reference is not found.
    /// </exception>
    /// <param name="secondaryInputYamlPath">
    /// Optional secondary input YAML (e.g. tasks/task_01.yaml).
    /// If null, behavior is identical to before.
    /// </param>
    string Assemble(string inputYamlPath, string stepId, string? secondaryInputYamlPath = null);
}
