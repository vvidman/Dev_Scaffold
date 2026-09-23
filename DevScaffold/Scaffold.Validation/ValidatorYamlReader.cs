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

using Scaffold.Validation.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Scaffold.Validation;

/// <summary>
/// Loads the validator rule set from a yaml file.
///
/// Convention-based filename resolution:
///   step agent config: task_breakdown_agent.yaml
///   validator yaml:    task_breakdown_validator.yaml  (same folder)
///
/// The validator yaml is optional – if it does not exist, returns null,
/// and the per-step validator runs with its built-in default rules.
/// </summary>
public sealed class ValidatorYamlReader : IValidatorRuleSetReader
{
    private readonly IDeserializer _deserializer;

    public ValidatorYamlReader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <inheritdoc />
    public ValidatorRuleSet? TryLoad(string stepConfigPath, string stepId)
    {
        var validatorYamlPath = ResolveValidatorPath(stepConfigPath, stepId);

        if (!File.Exists(validatorYamlPath))
            return null;

        var yaml = File.ReadAllText(validatorYamlPath);
        return _deserializer.Deserialize<ValidatorRuleSet>(yaml);
    }

    /// <summary>
    /// Determines the validator yaml path by convention, from the step
    /// agent config path and the step_id.
    ///
    /// E.g.: /config/task_breakdown_agent.yaml + step="task_breakdown"
    ///   → /config/task_breakdown_validator.yaml
    /// </summary>
    private static string ResolveValidatorPath(string stepConfigPath, string stepId) =>
        Path.Combine(
            Path.GetDirectoryName(stepConfigPath) ?? ".",
            $"{stepId}_validator.yaml");
}