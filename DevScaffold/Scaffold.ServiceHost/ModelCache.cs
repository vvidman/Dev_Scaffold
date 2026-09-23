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
using Scaffold.ServiceHost.Abstractions;

namespace Scaffold.ServiceHost;

/// <summary>
/// Lazy backend loading and cache management.
///
/// Loads / initializes the backend on the first inference request,
/// then keeps it in memory for the lifetime of the ServiceHost.
/// Releases every loaded backend on shutdown.
///
/// Handles two backend types:
/// - LlamaInferenceBackend: for a .gguf path, takes time to load
/// - ApiInferenceBackend:   for an https:// URL, instant init
///
/// Thread-safe: a per-alias SemaphoreSlim lock ensures the same
/// backend is only ever initialized once, even under concurrent
/// requests.
/// </summary>
public class ModelCache : IModelCache, IAsyncDisposable
{
    private readonly ModelRegistryConfig _registry;
    private readonly IInferenceBackendFactory _backendFactory;

    // Cache of loaded backends – alias → IInferenceBackend
    private readonly Dictionary<string, IInferenceBackend> _loadedBackends = new();

    // Per-alias lock – only the affected alias's load is blocked
    private readonly Dictionary<string, SemaphoreSlim> _loadLocks = new();

    // Global lock protecting the dictionaries
    private readonly SemaphoreSlim _dictionaryLock = new(1, 1);

    private bool _disposed;

    // Event – EventPublisher subscribes to this
    // so ModelCache does not depend directly on EventPublisher
    public event Func<string, ModelStatus, string, Task>? ModelStatusChanged;

    public ModelCache(ModelRegistryConfig registry, IInferenceBackendFactory backendFactory)
    {
        _registry = registry;
        _backendFactory = backendFactory;
    }

    /// <summary>
    /// Returns the loaded backend for the alias.
    /// If not loaded/initialized yet, does it now (lazy).
    /// </summary>
    public async Task<IInferenceBackend> GetOrLoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Fast check – if already loaded, return it immediately
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

        // Per-alias lock – only this alias is blocked while loading
        var aliasLock = _loadLocks[alias];
        await aliasLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check: someone else may have loaded it while we waited
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
    /// Explicit backend load – triggered by LoadModelRequest.
    /// If already loaded, no-op.
    /// </summary>
    public async Task LoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default)
    {
        await GetOrLoadAsync(requestId, alias, cancellationToken);
    }

    /// <summary>
    /// Unloads a backend from memory.
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
    /// Returns the list of aliases for currently loaded backends.
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
    // Private implementation
    // ─────────────────────────────────────────────

    private async Task<IInferenceBackend> LoadBackendAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken)
    {
        var config = _registry.Resolve(alias);

        if (ModelStatusChanged is not null)
            await ModelStatusChanged(alias, ModelStatus.Loading,
                "Initializing backend...");

        try
        {
            var backend = await _backendFactory.CreateAsync(config, cancellationToken);

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
                    $"Backend ready: {alias}");

            return backend;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ModelStatusChanged is not null)
                await ModelStatusChanged(alias, ModelStatus.Failed,
                    $"Backend initialization error: {ex.Message}");
            throw;
        }
    }


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

    }
}