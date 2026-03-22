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
/// Event pipe kapcsolat életciklus kezelése.
/// A PipeServer ezt és az IServiceEventPublisher-t kapja –
/// a pipe-kezelési részletek nem szennyezik az esemény-publikálási interfészt.
/// </summary>
public interface IPipeConnectionLifecycle
{
    /// <summary>
    /// Megvárja hogy a CLI kliens csatlakozzon az event pipe-ra.
    /// A ServiceHost a ready esemény előtt hívja ezt.
    /// </summary>
    Task WaitForConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Az előző CLI session pipe-ját elveti és újat nyit.
    /// A PipeServer hívja mielőtt a következő CLI kapcsolatot várja.
    /// </summary>
    Task ResetForNewConnectionAsync(CancellationToken cancellationToken = default);
}