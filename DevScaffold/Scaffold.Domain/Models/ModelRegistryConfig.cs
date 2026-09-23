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
/// Root object of models.yaml.
/// Stores the alias → ModelConfig mapping.
/// </summary>
public class ModelRegistryConfig
{
    public Dictionary<string, ModelConfig> Models { get; init; } = [];

    /// <summary>
    /// Resolves a ModelConfig by alias.
    /// </summary>
    /// <exception cref="KeyNotFoundException">If the alias is not found.</exception>
    public ModelConfig Resolve(string alias)
    {
        if (!Models.TryGetValue(alias, out var config))
            throw new KeyNotFoundException(
                $"Unknown model alias: '{alias}'. " +
                $"Available aliases: {string.Join(", ", Models.Keys)}");

        return config;
    }
}
