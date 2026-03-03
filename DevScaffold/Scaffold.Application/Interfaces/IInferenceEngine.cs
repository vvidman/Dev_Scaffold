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

namespace Scaffold.Application.Interfaces;

/// <summary>
/// AI inference engine absztrakciója.
/// Implementálható LLamaSharp (offline) vagy OpenAI-kompatibilis API alapon.
/// </summary>
public interface IInferenceEngine : IAsyncDisposable
{
    /// <summary>
    /// Streamelve futtatja az inference-t a megadott system prompt és user input alapján.
    /// </summary>
    IAsyncEnumerable<string> InferAsync(
        string systemPrompt,
        string userInput,
        CancellationToken cancellationToken = default);
}
