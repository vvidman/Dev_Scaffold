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

using Google.Protobuf;
using Scaffold.Agent.Protocol;

namespace Scaffold.Tests.Protocol;

[TestClass]
public sealed class EnvelopeFramingTests
{
    [TestMethod]
    public void WriteDelimited_MultipleEnvelopes_ParsedBackInOrder()
    {
        using var stream = new MemoryStream();

        var infer = new CommandEnvelope { Infer = new InferRequest { RequestId = "r1", StepId = "coding" } };
        var cancel = new CommandEnvelope { Cancel = new CancelInferRequest { RequestId = "r1" } };
        var shutdown = new CommandEnvelope { Shutdown = new ShutdownRequest { Force = true } };

        infer.WriteDelimitedTo(stream);
        cancel.WriteDelimitedTo(stream);
        shutdown.WriteDelimitedTo(stream);

        stream.Position = 0;

        var first = CommandEnvelope.Parser.ParseDelimitedFrom(stream);
        var second = CommandEnvelope.Parser.ParseDelimitedFrom(stream);
        var third = CommandEnvelope.Parser.ParseDelimitedFrom(stream);

        Assert.AreEqual(CommandEnvelope.CommandOneofCase.Infer, first.CommandCase);
        Assert.AreEqual(CommandEnvelope.CommandOneofCase.Cancel, second.CommandCase);
        Assert.AreEqual(CommandEnvelope.CommandOneofCase.Shutdown, third.CommandCase);
        Assert.AreEqual("r1", first.Infer.RequestId);
        Assert.IsTrue(third.Shutdown.Force);
    }

    [TestMethod]
    public void CommandEnvelope_Oneof_ExposesCorrectCommandCase()
    {
        var envelope = new CommandEnvelope { Infer = new InferRequest { RequestId = "r1", StepId = "task_breakdown" } };

        Assert.AreEqual(CommandEnvelope.CommandOneofCase.Infer, envelope.CommandCase);
        Assert.AreEqual("task_breakdown", envelope.Infer.StepId);
    }

    [TestMethod]
    public void EventEnvelope_InferenceFailed_RoundTripsRequestIdAndMessage()
    {
        using var stream = new MemoryStream();

        var envelope = new EventEnvelope
        {
            InferenceFailed = new InferenceFailedEvent
            {
                RequestId = "r42",
                StepId = "coding",
                ErrorMessage = "backend exploded"
            }
        };

        envelope.WriteDelimitedTo(stream);
        stream.Position = 0;

        var parsed = EventEnvelope.Parser.ParseDelimitedFrom(stream);

        Assert.AreEqual(EventEnvelope.EventOneofCase.InferenceFailed, parsed.EventCase);
        Assert.AreEqual("r42", parsed.InferenceFailed.RequestId);
        Assert.AreEqual("backend exploded", parsed.InferenceFailed.ErrorMessage);
    }

    [TestMethod]
    public void ParseDelimited_EmptyStream_ReturnsNullOrThrows()
    {
        // Documents the ACTUAL behaviour of Google.Protobuf's ParseDelimitedFrom on an
        // empty stream, because PipeServer's read loop (PipeServer.cs) depends on it.
        using var stream = new MemoryStream();

        CommandEnvelope? result = null;
        Exception? thrown = null;

        try
        {
            result = CommandEnvelope.Parser.ParseDelimitedFrom(stream);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.IsTrue(result is null || thrown is not null,
            "Expected ParseDelimitedFrom to either return null or throw on an empty stream.");
    }
}
