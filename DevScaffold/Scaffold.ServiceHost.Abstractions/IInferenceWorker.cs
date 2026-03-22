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
/// Inference futtatásának absztrakciója.
/// A CommandDispatcher ezt látja – nem tudja hogy LLamaSharp,
/// API backend, vagy bármi más hajtja végre az inference-t.
///
/// Egyszerre csak egy inference futhat – az implementáció felelős
/// a konkurencia kezeléséért.
/// </summary>
public interface IInferenceWorker
{
    /// <summary>
    /// Elindítja az inference futást.
    /// Ha már fut egy inference, InvalidOperationException-t dob.
    /// </summary>
    Task RunAsync(
        InferRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Megszakítja az aktív inference-t.
    /// Ha nincs aktív inference, no-op.
    /// </summary>
    void Cancel();

    /// <summary>
    /// Megvárja hogy az aktív inference befejezzen.
    /// Graceful shutdown esetén hívja a CommandDispatcher.
    /// Ha nincs aktív inference, azonnal visszatér.
    /// </summary>
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);
}