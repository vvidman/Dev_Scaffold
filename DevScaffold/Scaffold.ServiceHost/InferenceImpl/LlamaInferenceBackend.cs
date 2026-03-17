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
    private readonly LLamaContext _context;
    private readonly ModelConfig _config;
    private bool _disposed;

    private LlamaInferenceBackend(LLamaWeights weights, LLamaContext context, ModelConfig config)
    {
        _weights = weights;
        _context = context;
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

        var (weights, context) = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var w = LLamaWeights.LoadFromFile(parameters);
            var c = w.CreateContext(parameters);
            return (w, c);
        }, cancellationToken);

        return new LlamaInferenceBackend(weights, context, config);
    }

    /// <inheritdoc />
    public async Task<uint> RunAsync(
        InferRequest request,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var executor = new InteractiveExecutor(_context);

        var inferenceParams = new InferenceParams
        {
            // Ha a request tartalmaz max_tokens limitet, azt használjuk.
            // Ha 0 (nincs megadva), a LLamaSharp int.MaxValue-t kap –
            // a tényleges korlátot a context_size adja.
            MaxTokens = request.MaxTokens > 0
                ? (int)request.MaxTokens
                : int.MaxValue,

            AntiPrompts = ["\n\nUser:", "\n\nHuman:", "<|end|>", "<|eot_id|>"]
        };

        var prompt = $"<|system|>{request.SystemPrompt}<|end|>" +
                     $"<|user|>{request.UserInput}<|end|>" +
                     $"<|assistant|>";

        uint tokenCount = 0;

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(token);
            await writer.FlushAsync(cancellationToken);
            tokenCount++;
        }

        return tokenCount;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await Task.Run(() =>
        {
            _context.Dispose();
            _weights.Dispose();
        });
    }
}