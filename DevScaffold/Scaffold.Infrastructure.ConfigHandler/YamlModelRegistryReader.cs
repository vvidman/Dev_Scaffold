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
/// YAML alapú modell registry olvasó.
/// </summary>
public class YamlModelRegistryReader : IModelRegistryReader
{
    private readonly IDeserializer _deserializer;

    public YamlModelRegistryReader()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    public ModelRegistryConfig Load(string yamlPath)
    {
        if (!File.Exists(yamlPath))
            throw new FileNotFoundException(
                $"Modell registry nem található: {yamlPath}");

        var yaml = File.ReadAllText(yamlPath);
        return _deserializer.Deserialize<ModelRegistryConfig>(yaml);
    }
}
