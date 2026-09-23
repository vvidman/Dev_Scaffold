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
/// Abstraction for an inference backend.
/// TextWriter instead of StreamWriter – the caller can pass a
/// CountingTextWriter without the backend implementations changing,
/// since StreamWriter : TextWriter.
/// </summary>
public interface IInferenceBackend : IAsyncDisposable
{
    /// <summary>
    /// Runs the inference and writes the generated tokens to the writer.
    /// </summary>
    /// <returns>The number of tokens generated.</returns>
    Task<uint> RunAsync(InferRequest request, TextWriter writer, CancellationToken cancellationToken);
}