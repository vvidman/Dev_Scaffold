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
using Scaffold.Agent.Protocol;
using Scaffold.Application;
using Scaffold.Application.Interfaces;
using Scaffold.Domain.Models;
using Scaffold.Validation.Abstractions;

namespace Scaffold.Tests.Application;

/// <summary>
/// Pins the CLI liveness timeout (ADR-CLI #11): the timer is reset by every event
/// for the request's own request_id, suspended during human review, and on expiry
/// a CancelInferRequest is sent before the TimeoutException is thrown.
/// </summary>
[TestClass]
public sealed class InferenceResultHandlerLivenessTests
{
    private const string RequestId = "req-1";
    private static readonly TimeSpan LivenessTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private IPipeClient _pipeClient = null!;
    private IOutputValidator _outputValidator = null!;
    private IHumanValidationService _humanValidation = null!;
    private IAuditLogger _auditLogger = null!;
    private Func<EventEnvelope, Task>? _eventHandler;
    private InferenceResultHandler _handler = null!;

    private static readonly InferRequest Request = new()
    {
        RequestId = RequestId,
        StepId = "coding",
        ModelAlias = "test-model"
    };

    private static readonly StepAgentConfig AgentConfig = new()
    {
        Step = "coding",
        SystemPrompt = "system"
    };

    [TestInitialize]
    public void Initialize()
    {
        _pipeClient = Substitute.For<IPipeClient>();
        _pipeClient.EventReceived += Arg.Do<Func<EventEnvelope, Task>>(h => _eventHandler = h);

        _outputValidator = Substitute.For<IOutputValidator>();
        _humanValidation = Substitute.For<IHumanValidationService>();
        _auditLogger = Substitute.For<IAuditLogger>();

        _handler = new InferenceResultHandler(
            _outputValidator,
            _humanValidation,
            Substitute.For<IRefinementStrategy>(),
            Substitute.For<IScaffoldConsole>(),
            new InferenceResultHandlerOptions(LivenessTimeout));
    }

    private Task<ValidationDecision> StartHandle() =>
        _handler.HandleAsync(_pipeClient, _auditLogger, Request, AgentConfig, ruleSet: null);

    private Task Raise(EventEnvelope evt)
    {
        Assert.IsNotNull(_eventHandler, "Handler did not subscribe to EventReceived.");
        return _eventHandler(evt);
    }

    private static EventEnvelope Progress(string requestId) => new()
    {
        InferenceProgress = new InferenceProgressEvent { RequestId = requestId, StatusMessage = "tick" }
    };

    [TestMethod]
    public async Task HandleAsync_NoEvents_ThrowsTimeoutAndLogsLivenessTimeout()
    {
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => StartHandle().WaitAsync(TestTimeout));

        _auditLogger.Received(1).Log(
            AuditEvent.Error,
            Arg.Is<string>(m => m.Contains("reason=inference_liveness_timeout")));
    }

    [TestMethod]
    public async Task HandleAsync_ProgressEventsKeepArriving_DoesNotTimeOut()
    {
        var handleTask = StartHandle();

        // 15 × 100 ms = 1.5 s of activity, 1.5× the liveness timeout.
        for (var i = 0; i < 15; i++)
        {
            await Task.Delay(100);
            await Raise(Progress(RequestId));
        }

        await Raise(new EventEnvelope
        {
            InferenceFailed = new InferenceFailedEvent { RequestId = RequestId, ErrorMessage = "boom" }
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handleTask.WaitAsync(TestTimeout));
    }

    [TestMethod]
    public async Task HandleAsync_EventsForOtherRequestId_DoNotResetTimer()
    {
        var handleTask = StartHandle();
        using var stop = new CancellationTokenSource();

        var noise = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await Raise(Progress("other-request"));
                await Task.Delay(50);
            }
        });

        await Assert.ThrowsExactlyAsync<TimeoutException>(() => handleTask.WaitAsync(TestTimeout));

        stop.Cancel();
        await noise;
    }

    [TestMethod]
    public async Task HandleAsync_TimeoutFires_SendsCancelInferRequest()
    {
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => StartHandle().WaitAsync(TestTimeout));

        await _pipeClient.Received(1).SendAsync(
            Arg.Is<CommandEnvelope>(e =>
                e.CommandCase == CommandEnvelope.CommandOneofCase.Cancel
                && e.Cancel.RequestId == RequestId),
            Arg.Any<CancellationToken>());

        _auditLogger.Received(1).Log(
            AuditEvent.Error,
            Arg.Is<string>(m => m.Contains("cancel_sent=true")));
    }

    [TestMethod]
    public async Task HandleAsync_HumanValidationSlowerThanTimeout_DoesNotTimeOut()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"liveness_{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(outputPath, "# output");

        try
        {
            _outputValidator
                .Validate(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int>(), Arg.Any<ValidatorRuleSet?>())
                .Returns(OutputValidationResult.Success());

            _humanValidation.ValidateAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(async _ =>
                {
                    await Task.Delay(LivenessTimeout * 2);
                    return new ValidationDecision(ValidationOutcome.Accept, outputPath);
                });

            var handleTask = StartHandle();

            // Not awaited: the raise completes only after the human decision.
            var raiseTask = Raise(new EventEnvelope
            {
                InferenceCompleted = new InferenceCompletedEvent
                {
                    RequestId = RequestId,
                    OutputFilePath = outputPath,
                    TokensGenerated = 10,
                    ElapsedSeconds = 1
                }
            });

            var decision = await handleTask.WaitAsync(TestTimeout);
            await raiseTask;

            Assert.AreEqual(ValidationOutcome.Accept, decision.Outcome);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
}
