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
using Scaffold.Agent.Protocol;
using Scaffold.Domain.Models;

namespace Scaffold.ServiceHost.InferenceImpl;

/// <summary>
/// LLamaSharp alapú offline inference backend.
/// GGUF formátumú modell fájlból dolgozik, nincs hálózati függőség.
///
/// A ModelCache lazy módon hozza létre – az első GetOrLoadAsync híváskor
/// töltődik be a LLamaWeights, és a ServiceHost élettartama alatt
/// memóriában marad.
/// </summary>
internal sealed class LlamaInferenceBackend : IInferenceBackend
{
    private readonly LLamaWeights _weights;
    private readonly ModelConfig _config;
    private bool _disposed;

    private LlamaInferenceBackend(LLamaWeights weights, ModelConfig config)
    {
        _weights = weights;
        _config = config;
    }

    /// <summary>
    /// Betölti a GGUF modellt és létrehozza a backendet.
    /// Task.Run-ban fut – a LoadFromFile szinkron és CPU-intenzív.
    /// </summary>
    public static async Task<LlamaInferenceBackend> LoadAsync(
        ModelConfig config,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(config.Path))
            throw new FileNotFoundException(
                $"Modell fájl nem található: {config.Path}");

        var parameters = new ModelParams(config.Path)
        {
            ContextSize = (uint)config.ContextSize,
            GpuLayerCount = config.GpuLayers
        };

        var weights = await Task.Run(
            () => LLamaWeights.LoadFromFile(parameters),
            cancellationToken);

        return new LlamaInferenceBackend(weights, config);
    }

    /// <inheritdoc />
    public async Task<uint> RunAsync(
        InferRequest request,
        StreamWriter writer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var parameters = new ModelParams(_config.Path)
        {
            ContextSize = (uint)_config.ContextSize,
            GpuLayerCount = _config.GpuLayers
        };

        using var context = _weights.CreateContext(parameters);
        var executor = new InstructExecutor(context);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = request.MaxTokens > 0 ? (int)request.MaxTokens : 4096,
            AntiPrompts = ["User:", "### User:", "\nUser:"]
        };

        var fullPrompt = $"{request.SystemPrompt}\n\n{request.UserInput}";
        uint tokenCount = 0;

        await foreach (var token in executor
            .InferAsync(fullPrompt, inferenceParams, cancellationToken)
            .ConfigureAwait(false))
        {
            await writer.WriteAsync(token);
            await writer.FlushAsync(cancellationToken);
            tokenCount++;
        }

        return tokenCount;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _weights.Dispose();
        return ValueTask.CompletedTask;
    }
}