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
/// Manages the event pipe connection's lifecycle.
/// PipeServer receives this and IServiceEventPublisher –
/// pipe-handling details do not pollute the event-publishing interface.
/// </summary>
public interface IPipeConnectionLifecycle
{
    /// <summary>
    /// Waits for the CLI client to connect to the event pipe.
    /// The ServiceHost calls this before the ready event.
    /// </summary>
    Task WaitForConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the previous CLI session's pipe and opens a new one.
    /// Called by PipeServer before it waits for the next CLI connection.
    /// </summary>
    Task ResetForNewConnectionAsync(CancellationToken cancellationToken = default);
}