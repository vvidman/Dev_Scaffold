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

namespace Scaffold.ServiceHost.Abstractions;

/// <summary>
/// Inference backend lekérése alias alapján.
/// Az InferenceWorker csak ezt az interfészt látja –
/// nem tudja hogy a backend cache-elt, lazy betöltött, vagy API alapú.
/// </summary>
public interface IInferenceBackendProvider
{
    /// <summary>
    /// Visszaadja a betöltött backendet az alias alapján.
    /// Ha még nincs betöltve, most inicializálja (lazy).
    /// </summary>
    Task<IInferenceBackend> GetOrLoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default);
}