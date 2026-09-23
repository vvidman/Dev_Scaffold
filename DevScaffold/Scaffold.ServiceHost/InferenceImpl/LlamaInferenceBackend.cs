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
/// Executor mode selection for LlamaInferenceBackend.
/// </summary>
public enum LlamaExecutorMode
{
    /// <summary>
    /// Every call is independent, no conversation history.
    /// The default for Scaffold step inference.
    /// </summary>
    Stateless,

    /// <summary>
    /// Multi-turn conversation – the context retains conversation history.
    /// For future multi-turn step support.
    /// </summary>
    Interactive
}

/// <summary>
/// LLamaSharp-based offline inference backend.
/// Works from a GGUF-format model file, no network dependency.
///
/// Two executor modes are supported:
/// - Stateless:   every RunAsync call starts from a clean context (no state accumulation)
/// - Interactive: _context retains conversation history across runs
///
/// ModelCache creates the backend in the right mode via the
/// LoadStatelessAsync / LoadInteractiveAsync factory methods.
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
    // Factory methods
    // ─────────────────────────────────────────────

    /// <summary>
    /// Stateless executor – every inference is independent, no state accumulation.
    /// The default for Scaffold step inference.
    /// </summary>
    public static Task<LlamaInferenceBackend> LoadStatelessAsync(
        ModelConfig config,
        CancellationToken cancellationToken = default) =>
        LoadAsync(config, LlamaExecutorMode.Stateless, cancellationToken);

    /// <summary>
    /// Interactive executor – multi-turn conversation, context retains history.
    /// </summary>
    public static Task<LlamaInferenceBackend> LoadInteractiveAsync(
        ModelConfig config,
        CancellationToken cancellationToken = default) =>
        LoadAsync(config, LlamaExecutorMode.Interactive, cancellationToken);

    // ─────────────────────────────────────────────
    // IInferenceBackend implementation
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

        //uint tokenCount = 0;

        //await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        //{
        //    cancellationToken.ThrowIfCancellationRequested();

        //    if (inferenceParams.AntiPrompts.Any(ap => token.Contains(ap)))
        //        break;

        //    await writer.WriteAsync(token);
        //    await writer.FlushAsync(cancellationToken);
        //    tokenCount++;
        //}

        uint tokenCount = 0;
        // Keep the list of possible stop sequences handy
        var stopSequences = inferenceParams.AntiPrompts;

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check whether the current token contains one of the forbidden sequences
            var foundStopSequence = stopSequences.FirstOrDefault(s => token.Contains(s));

            if (foundStopSequence != null)
            {
                // If it does, only write out the part BEFORE the stop sequence
                var index = token.IndexOf(foundStopSequence);
                if (index > 0)
                {
                    var cleanPart = token.Substring(0, index);
                    await writer.WriteAsync(cleanPart);
                }

                // Stop here – the rest of the meta-token (or all of it) is not written to the output
                break;
            }

            // If there is no stop sequence, write out the full token
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
    // Private implementation
    // ─────────────────────────────────────────────

    private ILLamaExecutor CreateExecutor() => _executorMode switch
    {
        LlamaExecutorMode.Stateless => new StatelessExecutor(_weights, _params),
        LlamaExecutorMode.Interactive => new InteractiveExecutor(_context),
        _ => throw new ArgumentOutOfRangeException(
            nameof(_executorMode), _executorMode, "Unknown executor mode.")
    };

    private static async Task<LlamaInferenceBackend> LoadAsync(
        ModelConfig config,
        LlamaExecutorMode executorMode,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(config.Path))
            throw new FileNotFoundException(
                $"Model file not found: {config.Path}");

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