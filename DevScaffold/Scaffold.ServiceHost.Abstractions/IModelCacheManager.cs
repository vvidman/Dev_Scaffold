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
/// Backend cache életciklus kezelése.
/// A CommandDispatcher és a composition root (Program.cs) ezt kapja –
/// explicit betöltés, kiürítés és listázás tartozik ide.
///
/// A ModelStatusChanged eseményre a composition root iratkozik fel,
/// és az IServiceEventPublisher-en keresztül továbbítja a CLI-nek.
/// </summary>
public interface IModelCacheManager
{
    /// <summary>
    /// Esemény – backend státuszváltozáskor tüzel.
    /// A composition root köti össze az IServiceEventPublisher-rel.
    /// </summary>
    event Func<string, ModelStatus, string, Task>? ModelStatusChanged;

    /// <summary>
    /// Explicit backend betöltés – LoadModelRequest hatására.
    /// Ha már betöltött, no-op.
    /// </summary>
    Task LoadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Backend kiürítése a memóriából.
    /// </summary>
    Task UnloadAsync(
        string requestId,
        string alias,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Visszaadja a betöltött backendek alias listáját.
    /// </summary>
    Task<IReadOnlyList<string>> GetLoadedAliasesAsync(
        CancellationToken cancellationToken = default);
}