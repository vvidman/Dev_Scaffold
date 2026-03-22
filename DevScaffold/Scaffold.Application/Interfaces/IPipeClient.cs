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
/// Kétirányú kommunikáció absztrakciója a ServiceHost felé.
///
/// A CLI oldalon a PipeClient implementálja – Named Pipe-on keresztül
/// küldi a parancsokat és fogadja az eseményeket.
///
/// Az interfész a Scaffold.Application rétegben él, így a ScaffoldSession
/// nem függ közvetlenül a CLI infrastruktúrától.
/// </summary>
public interface IPipeClient
{
    /// <summary>
    /// Esemény – minden beérkező EventEnvelope-hoz meghívódik.
    /// Az IInferenceResultHandler feliratkozik erre az inference futása alatt.
    /// </summary>
    event Func<EventEnvelope, Task>? EventReceived;

    /// <summary>
    /// CommandEnvelope küldése a ServiceHost-nak.
    /// </summary>
    Task SendAsync(CommandEnvelope envelope, CancellationToken cancellationToken = default);
}