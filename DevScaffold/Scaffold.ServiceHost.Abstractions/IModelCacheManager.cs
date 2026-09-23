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

namespace Scaffold.ServiceHost.Abstractions;

/// <summary>
/// Manages the backend cache's lifecycle.
/// Received by CommandDispatcher and the composition root (Program.cs) –
/// explicit loading, unloading and listing belong here.
///
/// The composition root subscribes to ModelStatusChanged, and forwards
/// it to the CLI through IServiceEventPublisher.
/// </summary>
public interface IModelCacheManager
{
    /// <summary>
    /// Event – raised when a backend's status changes.
    /// The composition root wires this up to IServiceEventPublisher.
    /// </summary>
    event Func<string, ModelStatus, string, Task>? ModelStatusChanged;

    /// <summary>
    /// Explicit backend load – triggered by LoadModelRequest.
    /// If already loaded, no-op.
    /// </summary>
    Task LoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads a backend from memory.
    /// </summary>
    Task UnloadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the list of aliases for currently loaded backends.
    /// </summary>
    Task<IReadOnlyList<string>> GetLoadedAliasesAsync(
        CancellationToken cancellationToken = default);
}