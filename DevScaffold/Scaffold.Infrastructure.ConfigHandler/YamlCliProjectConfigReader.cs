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

using Scaffold.Domain.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Scaffold.Infrastructure.ConfigHandler;

/// <summary>
/// Loads and validates the project-level Scaffold.CLI.yaml configuration.
/// </summary>
public sealed class YamlCliProjectConfigReader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Loads the yaml file at the given path.
    /// </summary>
    /// <exception cref="FileNotFoundException">If the file does not exist.</exception>
    /// <exception cref="InvalidOperationException">If the yaml cannot be parsed.</exception>
    public CliProjectConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"CLI configuration not found: {path}\n" +
                $"Create the file next to the exe: {Path.GetFileName(path)}");

        try
        {
            var yaml = File.ReadAllText(path);
            return Deserializer.Deserialize<CliProjectConfig>(yaml);
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException(
                $"CLI configuration parse error ({Path.GetFileName(path)}): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates the loaded config's content in the context of the given step.
    /// </summary>
    /// <exception cref="InvalidOperationException">If a required field is missing or the step is unknown.</exception>
    public void Validate(CliProjectConfig config, string stepName)
    {
        var errors = new List<string>();

        // Global fields
        if (string.IsNullOrWhiteSpace(config.HostBinaryPath))
            errors.Add("host_binary_path is missing");

        if (string.IsNullOrWhiteSpace(config.Models))
            errors.Add("models is missing");

        if (string.IsNullOrWhiteSpace(config.PipeName))
            errors.Add("pipe_name is missing");

        if (string.IsNullOrWhiteSpace(config.ProjectContext))
            errors.Add("project_context is missing");
        else if (!File.Exists(config.ProjectContext))
            errors.Add($"project_context file not found: {config.ProjectContext}");

        // Step-specific validation
        if (!config.Steps.TryGetValue(stepName, out var step))
        {
            var available = config.Steps.Count > 0
                ? string.Join(", ", config.Steps.Keys)
                : "(no steps defined)";

            errors.Add($"Unknown step: '{stepName}'. Available steps: {available}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(step.InputConfig))
                errors.Add($"steps.{stepName}.input_config is missing");

            if (string.IsNullOrWhiteSpace(step.ModelAlias))
                errors.Add($"steps.{stepName}.model_alias is missing");

            if (!string.IsNullOrWhiteSpace(step.InputConfig) && !File.Exists(step.InputConfig))
                errors.Add($"steps.{stepName}.input_config file not found: {step.InputConfig}");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"CLI configuration errors:\n  - {string.Join("\n  - ", errors)}");
    }
}