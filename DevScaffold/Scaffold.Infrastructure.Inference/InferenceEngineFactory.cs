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

namespace Scaffold.Infrastructure.Inference;

/// <summary>
/// IInferenceEngine példányok gyára.
/// A ModelConfig.Path alapján dönti el melyik implementációt hozza létre:
/// - Ha a Path .gguf fájlra mutat → LLamaSharpEngine (offline)
/// - Ha a Path URL → OpenAiCompatibleEngine (API)
/// </summary>
public class InferenceEngineFactory : IInferenceEngineFactory
{
    private readonly string? _apiKey;

    public InferenceEngineFactory(string? apiKey = null)
    {
        _apiKey = apiKey;
    }

    public IInferenceEngine Create(ModelConfig modelConfig)
    {
        if (IsGgufModel(modelConfig.Path))
            return new LLamaSharpEngine(modelConfig);

        if (IsApiEndpoint(modelConfig.Path))
            return new OpenAiCompatibleEngine(modelConfig, _apiKey ?? string.Empty);

        throw new InvalidOperationException(
            $"Nem meghatározható az inference típusa a path alapján: '{modelConfig.Path}'. " +
            $"Várt formátum: .gguf fájl path vagy https:// URL.");
    }

    private static bool IsGgufModel(string path) =>
        path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase);

    private static bool IsApiEndpoint(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
