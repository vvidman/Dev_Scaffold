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

using LLama;
using LLama.Common;
using Scaffold.Application.Interfaces;
using Scaffold.Domain.Models;

namespace Scaffold.Infrastructure.Inference;

/// <summary>
/// LLamaSharp alapú offline inference engine implementáció.
/// GGUF formátumú modell fájlból dolgozik, teljesen offline.
/// </summary>
public class LLamaSharpEngine : IInferenceEngine
{
    private readonly LLamaWeights _model;
    private readonly ModelParams _parameters;
    private bool _disposed;

    public LLamaSharpEngine(ModelConfig modelConfig)
    {
        if (!File.Exists(modelConfig.Path))
            throw new FileNotFoundException(
                $"Modell fájl nem található: {modelConfig.Path}");

        _parameters = new ModelParams(modelConfig.Path)
        {
            ContextSize = (uint)modelConfig.ContextSize,
            GpuLayerCount = modelConfig.GpuLayers
        };

        _model = LLamaWeights.LoadFromFile(_parameters);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> InferAsync(
        string systemPrompt,
        string userInput,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var context = _model.CreateContext(_parameters);
        var executor = new InstructExecutor(context);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 4096,
            AntiPrompts = ["User:", "### User:", "\nUser:"]
        };

        // System prompt + user input összefűzése
        var fullPrompt = $"{systemPrompt}\n\n{userInput}";

        await foreach (var token in executor
            .InferAsync(fullPrompt, inferenceParams, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return token;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _model.Dispose();
        await ValueTask.CompletedTask;
    }
}
