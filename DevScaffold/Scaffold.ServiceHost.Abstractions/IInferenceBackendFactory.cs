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
/// Abstraction for instantiating inference backends.
///
/// ModelCache receives this via injection – it does not know whether
/// LLamaSharp, an API, or any other backend gets created.
/// Introducing a new backend type only changes the implementation,
/// not ModelCache (OCP).
/// </summary>
public interface IInferenceBackendFactory
{
    /// <summary>
    /// Creates and initializes the backend for the given configuration.
    /// </summary>
    /// <param name="config">The model's configuration (path or API endpoint).</param>
    /// <param name="cancellationToken">Cancellation token – loading a GGUF is a long-running operation.</param>
    /// <returns>The initialized, ready-to-use backend.</returns>
    Task<IInferenceBackend> CreateAsync(
        ModelConfig config,
        CancellationToken cancellationToken = default);
}
