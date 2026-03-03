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

using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scaffold.Application.Interfaces;
using Scaffold.Domain.Models;

namespace Scaffold.Infrastructure.Inference;

/// <summary>
/// OpenAI-kompatibilis API alapú inference engine implementáció.
/// Működik OpenAI, Anthropic (via kompatibilis proxy), Ollama API-val.
/// A ModelConfig.Path mezőben az API endpoint URL-je áll.
/// </summary>
public class OpenAiCompatibleEngine : IInferenceEngine
{
    private readonly HttpClient _httpClient;
    private readonly string _endpointUrl;
    private readonly string _modelName;
    private bool _disposed;

    public OpenAiCompatibleEngine(
        ModelConfig modelConfig,
        string apiKey,
        HttpClient? httpClient = null)
    {
        // Path mezőben az endpoint URL áll pl: https://api.openai.com/v1/chat/completions
        _endpointUrl = modelConfig.Path;

        // A model neve az alias-ból jön, de megadható külön is
        _modelName = modelConfig.Path.Contains("openai")
            ? "gpt-4o"
            : "default";

        _httpClient = httpClient ?? new HttpClient();

        if (!string.IsNullOrWhiteSpace(apiKey))
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> InferAsync(
        string systemPrompt,
        string userInput,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var requestBody = new
        {
            model = _modelName,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userInput }
            }
        };

        var response = await _httpClient.PostAsJsonAsync(
            _endpointUrl, requestBody, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<OpenAiResponse>(json);

        var content = result?.Choices?.FirstOrDefault()?.Message?.Content
                      ?? string.Empty;

        yield return content;
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _httpClient.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private record OpenAiResponse(
        [property: JsonPropertyName("choices")] List<Choice>? Choices);

    private record Choice(
        [property: JsonPropertyName("message")] Message? Message);

    private record Message(
        [property: JsonPropertyName("content")] string? Content);
}
