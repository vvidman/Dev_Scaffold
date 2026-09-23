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
/// A single model's configuration in models.yaml.
/// The value of the alias → ModelConfig mapping.
///
/// Two kinds of backend can be configured:
///
/// Local GGUF model:
///   path:         /models/qwen-coder-7b.gguf
///   context_size: 8192
///   gpu_layers:   32
///
/// Online API (OpenAI-compatible):
///   path:       https://api.openai.com/v1/chat/completions
///   model_name: gpt-4o
///   api_key:    OPENAI_API_KEY   ← name of the environment variable, not the key itself
///
/// The ServiceHost decides which backend type to use based on the path:
/// - .gguf extension → LlamaInferenceBackend
/// - http:// or https:// → ApiInferenceBackend
/// </summary>
public class ModelConfig
{
    /// <summary>
    /// For local models: the path to the GGUF file.
    /// For API models: the URL of the /v1/chat/completions endpoint.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// LLamaSharp context size in tokens.
    /// Only used for local models, ignored for API models.
    /// </summary>
    public int ContextSize { get; init; } = 4096;

    /// <summary>
    /// Number of layers to offload to the GPU. 0 = CPU-only.
    /// Only used for local models, ignored for API models.
    /// </summary>
    public int GpuLayers { get; init; } = 0;

    /// <summary>
    /// The value of the "model" field in the API call. E.g. "gpt-4o", "claude-3-5-sonnet-20241022".
    /// Only needed for API models, ignored for local models.
    /// </summary>
    public string? ModelName { get; init; }

    /// <summary>
    /// The name of the environment variable holding the API key. E.g. "OPENAI_API_KEY".
    /// The key value itself is NOT stored here – only the variable name.
    /// Only needed for API models, ignored for local models.
    /// </summary>
    public string? ApiKey { get; init; }
}
