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

using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Scaffold.Agent.Protocol;
using Scaffold.ServiceHost;
using Scaffold.ServiceHost.Abstractions;

namespace Scaffold.ServiceHost.Tests;

/// <summary>
/// Regression tests for the task_03 fire-and-forget failure path fix:
/// every background inference failure must reach the CLI as an
/// InferenceFailedEvent, and the command loop must never block on inference.
/// </summary>
[TestClass]
public sealed class CommandDispatcherTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(5);

    private static CommandDispatcher CreateDispatcher(
        out IInferenceWorker worker,
        out IModelCacheManager modelCache,
        out IEventPublisher eventPublisher)
    {
        worker = Substitute.For<IInferenceWorker>();
        modelCache = Substitute.For<IModelCacheManager>();
        eventPublisher = Substitute.For<IEventPublisher>();
        return new CommandDispatcher(worker, modelCache, eventPublisher);
    }

    [TestMethod]
    public async Task DispatchAsync_InferWhileBusy_PublishesInferenceFailedWithRequestId()
    {
        var dispatcher = CreateDispatcher(out var worker, out _, out var eventPublisher);
        worker.RunAsync(Arg.Any<InferRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("An inference is already running."));

        var tcs = new TaskCompletionSource<string>();
        eventPublisher.PublishInferenceFailedAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs(ci =>
            {
                tcs.TrySetResult(ci.ArgAt<string>(0));
                return Task.CompletedTask;
            });

        var envelope = new CommandEnvelope
        {
            Infer = new InferRequest { RequestId = "busy-request", StepId = "coding" }
        };

        await dispatcher.DispatchAsync(envelope);
        var publishedRequestId = await tcs.Task.WaitAsync(WaitTimeout);

        Assert.AreEqual("busy-request", publishedRequestId);
    }

    [TestMethod]
    public async Task DispatchAsync_Infer_ReturnsBeforeInferenceCompletes()
    {
        var dispatcher = CreateDispatcher(out var worker, out _, out _);
        var neverCompletes = new TaskCompletionSource();
        worker.RunAsync(Arg.Any<InferRequest>(), Arg.Any<CancellationToken>())
            .Returns(neverCompletes.Task);

        var envelope = new CommandEnvelope
        {
            Infer = new InferRequest { RequestId = "r1", StepId = "coding" }
        };

        var dispatchTask = dispatcher.DispatchAsync(envelope);
        var completed = await Task.WhenAny(dispatchTask, Task.Delay(WaitTimeout));

        Assert.AreEqual(dispatchTask, completed);
        Assert.IsTrue(dispatchTask.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task DispatchAsync_InferFailsAndPublisherThrows_DoesNotCrash()
    {
        var dispatcher = CreateDispatcher(out var worker, out _, out var eventPublisher);
        worker.RunAsync(Arg.Any<InferRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var publishAttempted = new TaskCompletionSource();
        eventPublisher.PublishInferenceFailedAsync(default!, default!, default!, default)
            .ReturnsForAnyArgs(_ =>
            {
                publishAttempted.TrySetResult();
                throw new IOException("pipe gone");
            });

        var envelope = new CommandEnvelope
        {
            Infer = new InferRequest { RequestId = "r1", StepId = "coding" }
        };

        // Must not throw – the second-layer catch in TryPublishFailureAsync swallows it.
        await dispatcher.DispatchAsync(envelope);
        await publishAttempted.Task.WaitAsync(WaitTimeout);
    }

    [TestMethod]
    public async Task DispatchAsync_ShutdownForce_CancelsWorkerAndSignalsShutdownToken()
    {
        var dispatcher = CreateDispatcher(out var worker, out _, out _);

        var envelope = new CommandEnvelope { Shutdown = new ShutdownRequest { Force = true } };
        await dispatcher.DispatchAsync(envelope);

        worker.Received(1).Cancel();
        Assert.IsTrue(dispatcher.ShutdownToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task DispatchAsync_ShutdownGraceful_WaitsForCompletionBeforeSignalling()
    {
        var dispatcher = CreateDispatcher(out var worker, out _, out _);
        var waitTcs = new TaskCompletionSource();
        worker.WaitForCompletionAsync(Arg.Any<CancellationToken>()).Returns(waitTcs.Task);

        var envelope = new CommandEnvelope { Shutdown = new ShutdownRequest { Force = false } };
        var dispatchTask = dispatcher.DispatchAsync(envelope);

        Assert.IsFalse(dispatcher.ShutdownToken.IsCancellationRequested);

        waitTcs.TrySetResult();
        await dispatchTask.WaitAsync(WaitTimeout);

        worker.DidNotReceive().Cancel();
        Assert.IsTrue(dispatcher.ShutdownToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task DispatchAsync_UnknownCommand_PublishesServiceError()
    {
        var dispatcher = CreateDispatcher(out _, out _, out var eventPublisher);

        await dispatcher.DispatchAsync(new CommandEnvelope());

        await eventPublisher.Received(1).PublishServiceErrorAsync(
            "UNKNOWN_COMMAND", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
