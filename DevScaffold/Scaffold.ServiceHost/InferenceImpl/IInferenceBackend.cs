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

namespace Scaffold.ServiceHost.InferenceImpl;

/// <summary>
/// Inference backend absztrakciója a ServiceHoston belül.
///
/// Két implementáció létezik:
/// - LlamaInferenceBackend: GGUF modell, LLamaSharp, offline
/// - ApiInferenceBackend:   OpenAI-kompatibilis REST API, online
///
/// A ModelCache tárolja a betöltött/inicializált backendeket,
/// az InferenceWorker csak ezen az interfészen keresztül fut inference-t.
/// </summary>
internal interface IInferenceBackend : IAsyncDisposable
{
    /// <summary>
    /// Futtatja az inference-t és a kimenetét a writer-be streameli.
    /// </summary>
    /// <returns>A generált tokenek száma.</returns>
    Task<uint> RunAsync(
        InferRequest request,
        StreamWriter writer,
        CancellationToken cancellationToken);
}