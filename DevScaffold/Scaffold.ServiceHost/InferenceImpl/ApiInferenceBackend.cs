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

using Scaffold.Agent.Protocol;
using Scaffold.Domain.Models;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scaffold.ServiceHost.InferenceImpl;

/// <summary>
/// OpenAI-kompatibilis REST API alapú inference backend.
/// Működik OpenAI, Anthropic proxy, Ollama és egyéb
/// /v1/chat/completions végpontot kínáló szolgáltatásokkal.
///
/// Konfiguráció a models.yaml-ban:
///   path:       https://api.openai.com/v1/chat/completions
///   model_name: gpt-4o
///   api_key:    OPENAI_API_KEY   ← environment variable neve
///
/// A ModelCache azonnal létrehozza (nincs betöltési idő),
/// az api_key értékét environment variable-ból olvassa fel.
/// </summary>
internal sealed class ApiInferenceBackend : IInferenceBackend
{
    private readonly HttpClient _httpClient;
    private readonly ModelConfig _config;
    private bool _disposed;

    public ApiInferenceBackend(ModelConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;

        // API kulcs feloldása environment variable névből
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            var key = Environment.GetEnvironmentVariable(config.ApiKey) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(key))
                Console.WriteLine(
                    $"[ServiceHost] Figyelmeztetés: '{config.ApiKey}' " +
                    $"environment variable nincs beállítva.");
            else
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");
        }
    }

    /// <inheritdoc />
    public async Task<uint> RunAsync(
        InferRequest request,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var modelName = _config.ModelName
            ?? throw new InvalidOperationException(
                $"model_name nincs megadva a modell konfigurációban: {_config.Path}");

        var maxTokens = request.MaxTokens > 0 ? (int)request.MaxTokens : 4096;

        var requestBody = new
        {
            model = modelName,
            stream = true,
            max_tokens = maxTokens,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user",   content = request.UserInput }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _config.Path)
        {
            Content = JsonContent.Create(requestBody)
        };

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        uint tokenCount = 0;

        // Server-Sent Events olvasás – minden sor "data: {...}" formátumú
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var json = line["data: ".Length..];

            if (json == "[DONE]")
                break;

            var chunk = JsonSerializer.Deserialize<StreamChunk>(json);
            var token = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;

            if (string.IsNullOrEmpty(token))
                continue;

            await writer.WriteAsync(token);
            await writer.FlushAsync(cancellationToken);
            tokenCount++;
        }

        return tokenCount;
    }

    public ValueTask DisposeAsync()
    {
        // HttpClient lifecycle a ModelCache felelőssége –
        // itt nem dispose-oljuk, mert megosztott instance
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    // ─────────────────────────────────────────────
    // SSE JSON deszializáló modellek
    // ─────────────────────────────────────────────

    private record StreamChunk(
        [property: JsonPropertyName("choices")] List<StreamChoice>? Choices);

    private record StreamChoice(
        [property: JsonPropertyName("delta")] StreamDelta? Delta);

    private record StreamDelta(
        [property: JsonPropertyName("content")] string? Content);
}