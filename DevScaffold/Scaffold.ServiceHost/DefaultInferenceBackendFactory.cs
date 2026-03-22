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
/// Az alapértelmezett backend factory implementáció.
///
/// Két backend típust kezel a modell config path-ja alapján:
/// - http:// vagy https:// prefix → ApiInferenceBackend (azonnali init)
/// - egyéb (fájl path) → LlamaInferenceBackend (GGUF betöltés, időigényes)
///
/// A HttpClient itt él – egyetlen megosztott példány az összes
/// ApiInferenceBackend számára a ServiceHost élettartama alatt.
/// </summary>
public sealed class DefaultInferenceBackendFactory : IInferenceBackendFactory, IDisposable
{
    // Megosztott HttpClient az összes ApiInferenceBackend számára.
    // A factory élettartamához kötött – a ModelCache-ből ide került.
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
            : await LlamaInferenceBackend.LoadStatelessAsync(config, cancellationToken);
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