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
using Scaffold.ServiceHost.InferenceImpl;

namespace Scaffold.ServiceHost;

/// <summary>
/// Lazy backend betöltés és cache kezelés.
///
/// Az első inference kéréskor tölti be / inicializálja a backendet,
/// majd a memóriában tartja a ServiceHost élettartama alatt.
/// Shutdown-kor felszabadítja az összes betöltött backendet.
///
/// Két backend típust kezel:
/// - LlamaInferenceBackend: .gguf path esetén, betöltési idő van
/// - ApiInferenceBackend:   https:// URL esetén, azonnali init
///
/// Thread-safe: SemaphoreSlim per-alias lockkal biztosítja hogy
/// ugyanazt a backendet egyszerre csak egyszer inicializálja,
/// még párhuzamos kérések esetén sem.
/// </summary>
public class ModelCache : IAsyncDisposable
{
    private readonly ModelRegistryConfig _registry;

    // Megosztott HttpClient az összes ApiInferenceBackend számára
    private readonly HttpClient _httpClient = new();

    // Betöltött backendek cache-e – alias → IInferenceBackend
    private readonly Dictionary<string, IInferenceBackend> _loadedBackends = new();

    // Per-alias lock – csak az érintett alias töltése blokkolódik
    private readonly Dictionary<string, SemaphoreSlim> _loadLocks = new();

    // Globális lock a dictionary-k védelméhez
    private readonly SemaphoreSlim _dictionaryLock = new(1, 1);

    private bool _disposed;

    // Esemény – az EventPublisher feliratkozik erre
    // így a ModelCache nem függ közvetlenül az EventPublisher-től
    public event Func<string, ModelStatus, string, Task>? ModelStatusChanged;

    public ModelCache(ModelRegistryConfig registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Visszaadja a betöltött backendet az alias alapján.
    /// Ha még nincs betöltve/inicializálva, most csinálja (lazy).
    /// </summary>
    internal async Task<IInferenceBackend> GetOrLoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Gyors ellenőrzés – ha már betöltött, azonnal visszaadjuk
        await _dictionaryLock.WaitAsync(cancellationToken);
        try
        {
            if (_loadedBackends.TryGetValue(alias, out var cached))
                return cached;

            if (!_loadLocks.ContainsKey(alias))
                _loadLocks[alias] = new SemaphoreSlim(1, 1);
        }
        finally
        {
            _dictionaryLock.Release();
        }

        // Per-alias lock – csak ez az alias blokkolódik betöltés alatt
        var aliasLock = _loadLocks[alias];
        await aliasLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check: lehet hogy amíg vártunk, már betöltötte valaki
            await _dictionaryLock.WaitAsync(cancellationToken);
            try
            {
                if (_loadedBackends.TryGetValue(alias, out var cached))
                    return cached;
            }
            finally
            {
                _dictionaryLock.Release();
            }

            return await LoadBackendAsync(requestId, alias, cancellationToken);
        }
        finally
        {
            aliasLock.Release();
        }
    }

    /// <summary>
    /// Explicit backend betöltés – LoadModelRequest hatására.
    /// Ha már betöltött, no-op.
    /// </summary>
    public async Task LoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default)
    {
        await GetOrLoadAsync(requestId, alias, cancellationToken);
    }

    /// <summary>
    /// Backend kiürítése a memóriából.
    /// </summary>
    public async Task UnloadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _dictionaryLock.WaitAsync(cancellationToken);
        try
        {
            if (!_loadedBackends.TryGetValue(alias, out var backend))
                return;

            await backend.DisposeAsync();
            _loadedBackends.Remove(alias);
        }
        finally
        {
            _dictionaryLock.Release();
        }

        if (ModelStatusChanged is not null)
            await ModelStatusChanged(alias, ModelStatus.Unloaded, string.Empty);
    }

    /// <summary>
    /// Visszaadja a betöltött backendek alias listáját.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetLoadedAliasesAsync(
        CancellationToken cancellationToken = default)
    {
        await _dictionaryLock.WaitAsync(cancellationToken);
        try
        {
            return _loadedBackends.Keys.ToList();
        }
        finally
        {
            _dictionaryLock.Release();
        }
    }

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task<IInferenceBackend> LoadBackendAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken)
    {
        var config = _registry.Resolve(alias);

        if (ModelStatusChanged is not null)
            await ModelStatusChanged(alias, ModelStatus.Loading,
                "Backend inicializálása...");

        try
        {
            IInferenceBackend backend = IsApiEndpoint(config.Path)
                ? new ApiInferenceBackend(config, _httpClient)
                : await LlamaInferenceBackend.LoadStatelessAsync(config, cancellationToken);

            await _dictionaryLock.WaitAsync(cancellationToken);
            try
            {
                _loadedBackends[alias] = backend;
            }
            finally
            {
                _dictionaryLock.Release();
            }

            if (ModelStatusChanged is not null)
                await ModelStatusChanged(alias, ModelStatus.Loaded,
                    IsApiEndpoint(config.Path)
                        ? $"API backend kész: {config.Path}"
                        : $"Modell betöltve: {alias}");

            return backend;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ModelStatusChanged is not null)
                await ModelStatusChanged(alias, ModelStatus.Failed,
                    $"Backend inicializálási hiba: {ex.Message}");
            throw;
        }
    }

    private static bool IsApiEndpoint(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _dictionaryLock.WaitAsync();
        try
        {
            foreach (var backend in _loadedBackends.Values)
                await backend.DisposeAsync();

            _loadedBackends.Clear();
        }
        finally
        {
            _dictionaryLock.Release();
        }

        _dictionaryLock.Dispose();

        foreach (var l in _loadLocks.Values)
            l.Dispose();

        _httpClient.Dispose();
    }
}