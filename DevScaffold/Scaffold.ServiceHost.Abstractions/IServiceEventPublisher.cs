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
/// Service szintű eseményeinek publikálása.
/// A PipeServer és a CommandDispatcher service-szintű részeit ez látja.
/// </summary>
public interface IServiceEventPublisher
{
    Task PublishServiceReadyAsync(
        string version,
        CancellationToken ct = default);

    Task PublishServiceShuttingDownAsync(
        bool forced,
        CancellationToken ct = default);

    Task PublishServiceErrorAsync(
        string errorCode,
        string errorMessage,
        CancellationToken ct = default);

    Task PublishModelStatusChangedAsync(
        string requestId,
        string modelAlias,
        ModelStatus status,
        string message = "",
        CancellationToken ct = default);

    Task PublishLoadedModelsListAsync(
        string requestId,
        IEnumerable<string> loadedAliases,
        CancellationToken ct = default);
}