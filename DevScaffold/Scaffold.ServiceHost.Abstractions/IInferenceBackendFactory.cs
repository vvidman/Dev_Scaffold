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

namespace Scaffold.ServiceHost.Abstractions;

/// <summary>
/// Inference backend példányosítás absztrakciója.
///
/// A ModelCache ezt injektálva kapja – nem tudja hogy LLamaSharp,
/// API, vagy bármilyen más backend kerül létrehozásra.
/// Új backend típus bevezetésekor csak az implementáció változik,
/// a ModelCache nem (OCP).
/// </summary>
public interface IInferenceBackendFactory
{
    /// <summary>
    /// Létrehozza és inicializálja a megadott konfigurációhoz tartozó backendet.
    /// </summary>
    /// <param name="config">A modell konfigurációja (path vagy API endpoint).</param>
    /// <param name="cancellationToken">Megszakítás token – GGUF betöltés hosszú műveletet végez.</param>
    /// <returns>Az inicializált, használatra kész backend.</returns>
    Task<IInferenceBackend> CreateAsync(
        ModelConfig config,
        CancellationToken cancellationToken = default);
}