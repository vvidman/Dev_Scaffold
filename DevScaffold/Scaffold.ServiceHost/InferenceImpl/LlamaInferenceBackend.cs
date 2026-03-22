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
using LLama.Abstractions;
using LLama.Common;
using Scaffold.Agent.Protocol;
using Scaffold.Domain.Models;
using Scaffold.ServiceHost.Abstractions;

namespace Scaffold.ServiceHost.InferenceImpl;

/// <summary>
/// Executor mód kiválasztása a LlamaInferenceBackend-hez.
/// </summary>
public enum LlamaExecutorMode
{
    /// <summary>
    /// Minden hívás önálló, nincs conversation history.
    /// Scaffold step inference-hez ez az alapértelmezett.
    /// </summary>
    Stateless,

    /// <summary>
    /// Multi-turn párbeszéd – a context megőrzi a conversation history-t.
    /// Jövőbeli multi-turn step támogatáshoz.
    /// </summary>
    Interactive
}

/// <summary>
/// LLamaSharp alapú offline inference backend.
/// GGUF formátumú modell fájlból dolgozik, nincs hálózati függőség.
///
/// Két executor mód támogatott:
/// - Stateless:   minden RunAsync hívás tiszta kontextusból indul (nincs állapot-akkumuláció)
/// - Interactive: a _context megőrzi a conversation history-t futások között
///
/// A ModelCache a LoadStatelessAsync / LoadInteractiveAsync factory metódusokkal
/// hozza létre a megfelelő módú backendet.
/// </summary>
internal sealed class LlamaInferenceBackend : IInferenceBackend
{
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly ModelParams _params;
    private readonly ModelConfig _config;
    private readonly LlamaExecutorMode _executorMode;
    private bool _disposed;

    private LlamaInferenceBackend(
        LLamaWeights weights,
        LLamaContext context,
        ModelParams @params,
        ModelConfig config,
        LlamaExecutorMode executorMode)
    {
        _weights = weights;
        _context = context;
        _params = @params;
        _config = config;
        _executorMode = executorMode;
    }

    // ─────────────────────────────────────────────
    // Factory metódusok
    // ─────────────────────────────────────────────

    /// <summary>
    /// Stateless executor – minden inference önálló, nincs állapot-akkumuláció.
    /// Scaffold step inference-hez ez az alapértelmezett.
    /// </summary>
    public static Task<LlamaInferenceBackend> LoadStatelessAsync(
        ModelConfig config,
        CancellationToken cancellationToken = default) =>
        LoadAsync(config, LlamaExecutorMode.Stateless, cancellationToken);

    /// <summary>
    /// Interactive executor – multi-turn párbeszéd, context megőrzi a history-t.
    /// </summary>
    public static Task<LlamaInferenceBackend> LoadInteractiveAsync(
        ModelConfig config,
        CancellationToken cancellationToken = default) =>
        LoadAsync(config, LlamaExecutorMode.Interactive, cancellationToken);

    // ─────────────────────────────────────────────
    // IInferenceBackend implementáció
    // ─────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<uint> RunAsync(
        InferRequest request,
        TextWriter writer,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var executor = CreateExecutor();

        var inferenceParams = new InferenceParams
        {
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

            if (inferenceParams.AntiPrompts.Any(ap => token.Contains(ap)))
                break;

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

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private ILLamaExecutor CreateExecutor() => _executorMode switch
    {
        LlamaExecutorMode.Stateless => new StatelessExecutor(_weights, _params),
        LlamaExecutorMode.Interactive => new InteractiveExecutor(_context),
        _ => throw new ArgumentOutOfRangeException(
            nameof(_executorMode), _executorMode, "Ismeretlen executor mód.")
    };

    private static async Task<LlamaInferenceBackend> LoadAsync(
        ModelConfig config,
        LlamaExecutorMode executorMode,
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

        return new LlamaInferenceBackend(weights, context, parameters, config, executorMode);
    }
}