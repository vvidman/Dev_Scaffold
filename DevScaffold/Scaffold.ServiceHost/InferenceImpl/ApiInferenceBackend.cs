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

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scaffold.Agent.Protocol;
using Scaffold.Domain.Models;

namespace Scaffold.ServiceHost;

/// <summary>
/// OpenAI-kompatibilis API inference backend.
/// SSE streaming-et használ (stream: true).
///
/// MaxTokens kezelés:
/// - Ha az InferRequest.MaxTokens > 0, bekerül a request body-ba (max_tokens mező).
/// - Ha 0, a mező kimarad – az API provider alapértelmezése érvényes.
/// </summary>
internal sealed class ApiInferenceBackend : IInferenceBackend
{
    private readonly ModelConfig _config;
    private readonly HttpClient _httpClient;

    public ApiInferenceBackend(ModelConfig config, HttpClient httpClient)
    {
        _config = config;
        _httpClient = httpClient;
    }

    public async Task<uint> RunAsync(
        InferRequest request,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        var apiKey = ResolveApiKey();

        var requestBody = BuildRequestBody(request);
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _config.Path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(apiKey))
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        uint tokenCount = 0;

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line["data: ".Length..];

            if (data == "[DONE]")
                break;

            var chunk = JsonSerializer.Deserialize<StreamChunk>(data, JsonOptions);
            var token = chunk?.Choices?.FirstOrDefault()?.Delta?.Content;

            if (string.IsNullOrEmpty(token))
                continue;

            await writer.WriteAsync(token);
            await writer.FlushAsync(cancellationToken);
            tokenCount++;
        }

        return tokenCount;
    }

    // ─────────────────────────────────────────────
    // Request body összerakása
    // ─────────────────────────────────────────────

    private object BuildRequestBody(InferRequest request)
    {
        var body = new RequestBody
        {
            Model = _config.ModelName ?? "gpt-4o",
            Stream = true,
            Messages =
            [
                new Message { Role = "system", Content = request.SystemPrompt },
                new Message { Role = "user",   Content = request.UserInput   }
            ]
        };

        // MaxTokens csak akkor kerül a requestbe ha explicit meg van adva.
        // 0 = nincs limit (proto default érték).
        if (request.MaxTokens > 0)
            body.MaxTokens = (int)request.MaxTokens;

        return body;
    }

    private string? ResolveApiKey()
    {
        if (string.IsNullOrEmpty(_config.ApiKey))
            return null;

        // ApiKey az env var neve, nem maga a kulcs
        return Environment.GetEnvironmentVariable(_config.ApiKey);
    }

    // ─────────────────────────────────────────────
    // JSON modellek
    // ─────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private class RequestBody
    {
        public string Model { get; set; } = string.Empty;
        public bool Stream { get; set; }
        public List<Message> Messages { get; set; } = [];

        // Null ha nincs megadva – JsonIgnoreCondition.WhenWritingNull kihagyja
        public int? MaxTokens { get; set; }
    }

    private class Message
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    private class StreamChunk
    {
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        public Delta? Delta { get; set; }
    }

    private class Delta
    {
        public string? Content { get; set; }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}