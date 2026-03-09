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
/// Egy modell konfigurációja a models.yaml-ban.
/// Az alias → ModelConfig mapping értéke.
///
/// Kétféle backend konfigurálható:
///
/// Lokális GGUF modell:
///   path:         /models/qwen-coder-7b.gguf
///   context_size: 8192
///   gpu_layers:   32
///
/// Online API (OpenAI-kompatibilis):
///   path:       https://api.openai.com/v1/chat/completions
///   model_name: gpt-4o
///   api_key:    OPENAI_API_KEY   ← environment variable neve, nem maga a kulcs
///
/// A ServiceHost a path alapján dönti el melyik backend típust használja:
/// - .gguf kiterjesztés → LlamaInferenceBackend
/// - http:// vagy https:// → ApiInferenceBackend
/// </summary>
public class ModelConfig
{
    /// <summary>
    /// Lokális modellnél: GGUF fájl elérési útja.
    /// API modelleknél: a /v1/chat/completions végpont URL-je.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// LLamaSharp context mérete tokenekben.
    /// Csak lokális modelleknél használt, API modelleknél figyelmen kívül hagyva.
    /// </summary>
    public int ContextSize { get; init; } = 4096;

    /// <summary>
    /// GPU-ra töltendő rétegek száma. 0 = CPU-only.
    /// Csak lokális modelleknél használt, API modelleknél figyelmen kívül hagyva.
    /// </summary>
    public int GpuLayers { get; init; } = 0;

    /// <summary>
    /// Az API hívásban a "model" mező értéke. Pl. "gpt-4o", "claude-3-5-sonnet-20241022".
    /// Csak API modelleknél szükséges, lokális modelleknél figyelmen kívül hagyva.
    /// </summary>
    public string? ModelName { get; init; }

    /// <summary>
    /// Az API kulcsot tartalmazó environment variable neve. Pl. "OPENAI_API_KEY".
    /// Maga a kulcs értéke NEM kerül ide – csak a változó neve.
    /// Csak API modelleknél szükséges, lokális modelleknél figyelmen kívül hagyva.
    /// </summary>
    public string? ApiKey { get; init; }
}