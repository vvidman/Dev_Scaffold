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
/// Looks up an inference backend by alias.
/// InferenceWorker only sees this interface – it does not know whether
/// the backend is cached, lazily loaded, or API-based.
/// </summary>
public interface IInferenceBackendProvider
{
    /// <summary>
    /// Returns the loaded backend for the alias.
    /// If not loaded yet, initializes it now (lazy).
    /// </summary>
    Task<IInferenceBackend> GetOrLoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default);
}
