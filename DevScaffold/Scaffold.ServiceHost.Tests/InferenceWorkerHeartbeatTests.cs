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

using System.Collections.Concurrent;
using NSubstitute;
using Scaffold.Agent.Protocol;
using Scaffold.ServiceHost.Abstractions;

namespace Scaffold.ServiceHost.Tests;

/// <summary>
/// Pins the heartbeat (ADR-ServiceHost #9): InferenceProgressEvent is sent on every
/// tick for the whole request lifetime, including model loading and prompt
/// processing, so the CLI liveness timeout never fires on a slow but healthy host.
/// </summary>
[TestClass]
public sealed class InferenceWorkerHeartbeatTests
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private string _outputFolder = null!;

    [TestInitialize]
    public void Initialize() =>
        _outputFolder = Path.Combine(Path.GetTempPath(), $"heartbeat_{Guid.NewGuid():N}");

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_outputFolder))
            Directory.Delete(_outputFolder, recursive: true);
    }

    private InferRequest CreateRequest() => new()
    {
        RequestId = "heartbeat-request",
        StepId = "coding",
        ModelAlias = "slow-model",
        OutputFolder = _outputFolder
    };

    private static IInferenceEventPublisher CreatePublisher(ConcurrentQueue<string> progressMessages)
    {
        var publisher = Substitute.For<IInferenceEventPublisher>();
        publisher
            .PublishInferenceProgressAsync(default!, default!, default, default!, default)
            .ReturnsForAnyArgs(ci =>
            {
                progressMessages.Enqueue(ci.ArgAt<string>(3));
                return Task.CompletedTask;
            });
        return publisher;
    }

    private static async Task WaitForProgressAsync(
        ConcurrentQueue<string> progressMessages, string prefix, int count)
    {
        using var cts = new CancellationTokenSource(WaitTimeout);
        while (progressMessages.Count(m => m.StartsWith(prefix, StringComparison.Ordinal)) < count)
            await Task.Delay(ProgressInterval, cts.Token);
    }

    [TestMethod]
    public async Task RunAsync_DuringSlowModelLoad_PublishesProgress()
    {
        var progressMessages = new ConcurrentQueue<string>();
        var publisher = CreatePublisher(progressMessages);

        var backend = Substitute.For<IInferenceBackend>();
        backend.RunAsync(Arg.Any<InferRequest>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(0u);

        var modelLoaded = new TaskCompletionSource<IInferenceBackend>();
        var provider = Substitute.For<IInferenceBackendProvider>();
        provider.GetOrLoadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(modelLoaded.Task);

        var worker = new InferenceWorker(provider, publisher, _outputFolder, ProgressInterval);
        var runTask = worker.RunAsync(CreateRequest());

        await WaitForProgressAsync(progressMessages, "Loading model 'slow-model'", count: 3);

        modelLoaded.SetResult(backend);
        await runTask.WaitAsync(WaitTimeout);

        await publisher.Received(1).PublishInferenceCompletedAsync(
            Arg.Is("heartbeat-request"), Arg.Is("coding"), Arg.Any<string>(), Arg.Any<uint>(), Arg.Is(0u), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task RunAsync_BeforeFirstToken_PublishesProgressEveryTick()
    {
        var progressMessages = new ConcurrentQueue<string>();
        var publisher = CreatePublisher(progressMessages);

        var firstTokenReleased = new TaskCompletionSource();
        var backend = Substitute.For<IInferenceBackend>();
        backend.RunAsync(Arg.Any<InferRequest>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                // Prefill: no tokens written until released.
                await firstTokenReleased.Task;
                await ci.ArgAt<TextWriter>(1).WriteAsync("token");
                return 1u;
            });

        var provider = Substitute.For<IInferenceBackendProvider>();
        provider.GetOrLoadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(backend);

        var worker = new InferenceWorker(provider, publisher, _outputFolder, ProgressInterval);
        var runTask = worker.RunAsync(CreateRequest());

        await WaitForProgressAsync(progressMessages, "Processing prompt...", count: 3);

        firstTokenReleased.SetResult();
        await runTask.WaitAsync(WaitTimeout);

        await publisher.Received(1).PublishInferenceCompletedAsync(
            Arg.Is("heartbeat-request"), Arg.Is("coding"), Arg.Any<string>(), Arg.Any<uint>(), Arg.Is(1u), Arg.Any<CancellationToken>());
    }
}
