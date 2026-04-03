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
using Scaffold.Domain.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Scaffold.Infrastructure.ConfigHandler;

/// <summary>
/// YAML alapú agent step konfiguráció olvasó.
/// </summary>
public class YamlStepAgentConfigReader : IStepAgentConfigReader
{
    private readonly IDeserializer _deserializer;

    public YamlStepAgentConfigReader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    // Új opcionális mezők (pl. FilepathHintPrefix) nem igényelnek módosítást itt.
    // Az UnderscoredNamingConvention automatikusan leképezi a snake_case YAML kulcsokat
    // PascalCase property-kre, az IgnoreUnmatchedProperties pedig biztosítja, hogy
    // hiányzó mezők ne okozzanak hibát. Ez tudatos, explicit tervezési döntés.
    public StepAgentConfig Load(string yamlPath)
    {
        if (!File.Exists(yamlPath))
            throw new FileNotFoundException(
                $"Agent konfiguráció nem található: {yamlPath}");

        var yaml = File.ReadAllText(yamlPath);
        var config = _deserializer.Deserialize<StepAgentConfig>(yaml);
        ValidateConfig(config, yamlPath);
        return config;
    }

    private void ValidateConfig(StepAgentConfig config, string path)
    {
        if (string.IsNullOrWhiteSpace(config.Step))
            throw new InvalidOperationException(
                $"A step agent config 'step' mezője kötelező: {path}");

        if (string.IsNullOrWhiteSpace(config.SystemPrompt))
            throw new InvalidOperationException(
                $"A step agent config 'system_prompt' mezője kötelező: {path}");

        if (config.MaxTokens.HasValue && config.MaxTokens.Value <= 0)
            throw new InvalidOperationException(
                $"A 'max_tokens' értékének pozitívnak kell lennie: {path}");
    }
}
