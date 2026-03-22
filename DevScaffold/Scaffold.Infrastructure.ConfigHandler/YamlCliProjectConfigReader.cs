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
/// Betölti és validálja a Scaffold.CLI.yaml projekt-szintű konfigurációt.
/// </summary>
public sealed class YamlCliProjectConfigReader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Betölti a megadott útvonalú yaml fájlt.
    /// </summary>
    /// <exception cref="FileNotFoundException">Ha a fájl nem létezik.</exception>
    /// <exception cref="InvalidOperationException">Ha a yaml nem értelmezhető.</exception>
    public CliProjectConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"CLI konfiguráció nem található: {path}\n" +
                $"Hozd létre a fájlt az exe mellett: {Path.GetFileName(path)}");

        try
        {
            var yaml = File.ReadAllText(path);
            return Deserializer.Deserialize<CliProjectConfig>(yaml);
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException(
                $"CLI konfiguráció parse hiba ({Path.GetFileName(path)}): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validálja a betöltött config tartalmát a megadott step kontextusában.
    /// </summary>
    /// <exception cref="InvalidOperationException">Ha kötelező mező hiányzik vagy a step ismeretlen.</exception>
    public void Validate(CliProjectConfig config, string stepName)
    {
        var errors = new List<string>();

        // Globális mezők
        if (string.IsNullOrWhiteSpace(config.HostBinaryPath))
            errors.Add("host_binary_path hiányzik");

        if (string.IsNullOrWhiteSpace(config.Models))
            errors.Add("models hiányzik");

        if (string.IsNullOrWhiteSpace(config.PipeName))
            errors.Add("pipe_name hiányzik");

        if (string.IsNullOrWhiteSpace(config.ProjectContext))
            errors.Add("project_context hiányzik");
        else if (!File.Exists(config.ProjectContext))
            errors.Add($"project_context fájl nem található: {config.ProjectContext}");

        // Step-specifikus validáció
        if (!config.Steps.TryGetValue(stepName, out var step))
        {
            var available = config.Steps.Count > 0
                ? string.Join(", ", config.Steps.Keys)
                : "(nincsenek steps definiálva)";

            errors.Add($"Ismeretlen step: '{stepName}'. Elérhető stepek: {available}");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(step.InputConfig))
                errors.Add($"steps.{stepName}.input_config hiányzik");

            if (string.IsNullOrWhiteSpace(step.ModelAlias))
                errors.Add($"steps.{stepName}.model_alias hiányzik");

            if (!string.IsNullOrWhiteSpace(step.InputConfig) && !File.Exists(step.InputConfig))
                errors.Add($"steps.{stepName}.input_config fájl nem található: {step.InputConfig}");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"CLI konfiguráció hibák:\n  - {string.Join("\n  - ", errors)}");
    }
}