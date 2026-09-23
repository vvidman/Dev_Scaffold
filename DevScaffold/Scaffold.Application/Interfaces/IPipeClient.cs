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

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Abstraction for bidirectional communication with the ServiceHost.
///
/// Implemented by PipeClient on the CLI side – sends commands and
/// receives events over a Named Pipe.
///
/// The interface lives in the Scaffold.Application layer, so
/// ScaffoldSession does not depend directly on CLI infrastructure.
/// </summary>
public interface IPipeClient
{
    /// <summary>
    /// Event – raised for every incoming EventEnvelope.
    /// IInferenceResultHandler subscribes to this while an inference is running.
    /// </summary>
    event Func<EventEnvelope, Task>? EventReceived;

    /// <summary>
    /// Sends a CommandEnvelope to the ServiceHost.
    /// </summary>
    Task SendAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default);
}
