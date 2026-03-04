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

using LLama;
using LLama.Common;
using Scaffold.Agent.Protocol;
using Scaffold.Domain.Models;

namespace Scaffold.ServiceHost;

/// <summary>
/// Lazy modell betöltés és cache kezelés.
///
/// Az első inference kéréskor tölti be a modellt,
/// majd a memóriában tartja a ServiceHost élettartama alatt.
/// Shutdown-kor felszabadítja az összes betöltött modellt.
///
/// Thread-safe: SemaphoreSlim per-alias lockkal biztosítja hogy
/// ugyanazt a modellt egyszerre csak egyszer töltjük be,
/// még párhuzamos kérések esetén sem.
/// </summary>
public class ModelCache : IAsyncDisposable
{
    private readonly ModelRegistryConfig _registry;

    // Betöltött modellek cache-e – alias → LLamaWeights
    private readonly Dictionary<string, LLamaWeights> _loadedModels = new();

    // Per-alias lock – csak az érintett alias töltése blokkolódik
    // nem az összes modell művelete
    private readonly Dictionary<string, SemaphoreSlim> _loadLocks = new();

    // Globális lock a _loadedModels és _loadLocks dictionary-k védelméhez
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
    /// Visszaadja a betöltött LLamaWeights-et az alias alapján.
    /// Ha még nincs betöltve, most tölti be (lazy).
    /// </summary>
    /// <param name="requestId">Korrelációs azonosító – az eseményekhez</param>
    /// <param name="alias">Modell alias a models.yaml-ból</param>
    public async Task<LLamaWeights> GetOrLoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Gyors ellenőrzés lock nélkül – ha már betöltött, azonnal visszaadjuk
        await _dictionaryLock.WaitAsync(cancellationToken);
        try
        {
            if (_loadedModels.TryGetValue(alias, out var cached))
                return cached;

            // Per-alias lock létrehozása ha még nem létezik
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
            // Double-check: lehet hogy amíg vártunk a lockra, már betöltötte valaki
            await _dictionaryLock.WaitAsync(cancellationToken);
            try
            {
                if (_loadedModels.TryGetValue(alias, out var cached))
                    return cached;
            }
            finally
            {
                _dictionaryLock.Release();
            }

            // Ténylegesen betöltjük a modellt
            return await LoadModelAsync(requestId, alias, cancellationToken);
        }
        finally
        {
            aliasLock.Release();
        }
    }

    /// <summary>
    /// Explicit modell betöltés – LoadModelRequest hatására.
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
    /// Modell kiürítése a memóriából.
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
            if (!_loadedModels.TryGetValue(alias, out var model))
                return;

            model.Dispose();
            _loadedModels.Remove(alias);
        }
        finally
        {
            _dictionaryLock.Release();
        }

        if (ModelStatusChanged is not null)
            await ModelStatusChanged(alias, ModelStatus.Unloaded, string.Empty);
    }

    /// <summary>
    /// Visszaadja a betöltött modellek alias listáját.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetLoadedAliasesAsync(
        CancellationToken cancellationToken = default)
    {
        await _dictionaryLock.WaitAsync(cancellationToken);
        try
        {
            return _loadedModels.Keys.ToList();
        }
        finally
        {
            _dictionaryLock.Release();
        }
    }

    /// <summary>
    /// Visszaadja a ModelParams-ot egy aliashoz.
    /// Az InferenceWorker használja context létrehozáshoz.
    /// </summary>
    public ModelConfig GetModelConfig(string alias) =>
        _registry.Resolve(alias);

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task<LLamaWeights> LoadModelAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken)
    {
        var modelConfig = _registry.Resolve(alias);

        if (!File.Exists(modelConfig.Path))
            throw new FileNotFoundException(
                $"Modell fájl nem található: {modelConfig.Path}");

        // Betöltés kezdete – esemény küldése
        if (ModelStatusChanged is not null)
            await ModelStatusChanged(alias, ModelStatus.Loading,
                "Modell betöltése folyamatban...");

        try
        {
            var parameters = new ModelParams(modelConfig.Path)
            {
                ContextSize = (uint)modelConfig.ContextSize,
                GpuLayerCount = modelConfig.GpuLayers
            };

            // LLamaWeights.LoadFromFile szinkron és CPU-intenzív –
            // Task.Run-ba tesszük hogy ne blokkoljuk az async szálat
            var weights = await Task.Run(
                () => LLamaWeights.LoadFromFile(parameters),
                cancellationToken);

            // Cache-be tesszük
            await _dictionaryLock.WaitAsync(cancellationToken);
            try
            {
                _loadedModels[alias] = weights;
            }
            finally
            {
                _dictionaryLock.Release();
            }

            // Betöltés kész – esemény küldése
            if (ModelStatusChanged is not null)
                await ModelStatusChanged(alias, ModelStatus.Loaded,
                    $"Modell betöltve: {alias}");

            return weights;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Betöltés hiba – esemény küldése
            if (ModelStatusChanged is not null)
                await ModelStatusChanged(alias, ModelStatus.Failed,
                    $"Modell betöltési hiba: {ex.Message}");

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
            foreach (var model in _loadedModels.Values)
                model.Dispose();

            _loadedModels.Clear();
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