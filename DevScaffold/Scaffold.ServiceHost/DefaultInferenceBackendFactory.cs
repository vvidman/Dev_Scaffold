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
using Scaffold.ServiceHost.Abstractions;
using Scaffold.ServiceHost.InferenceImpl;

namespace Scaffold.ServiceHost;

/// <summary>
/// The default backend factory implementation.
///
/// Handles two backend types based on the model config's path:
/// - http:// or https:// prefix → ApiInferenceBackend (instant init)
/// - anything else (file path) → LlamaInferenceBackend (GGUF loading, time-consuming)
///
/// The HttpClient lives here – a single shared instance for all
/// ApiInferenceBackends for the lifetime of the ServiceHost.
/// </summary>
public sealed class DefaultInferenceBackendFactory : IInferenceBackendFactory, IDisposable
{
    // Shared HttpClient for all ApiInferenceBackends.
    // Bound to the factory's lifetime – moved here from ModelCache.
    private readonly HttpClient _httpClient = new();
    private bool _disposed;

    /// <inheritdoc />
    public async Task<IInferenceBackend> CreateAsync(
        ModelConfig config,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return IsApiEndpoint(config.Path)
            ? new ApiInferenceBackend(config, _httpClient)
            : await LlamaInferenceBackend.LoadInteractiveAsync(config, cancellationToken);//await LlamaInferenceBackend.LoadStatelessAsync(config, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private static bool IsApiEndpoint(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}