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
using System.IO.Pipes;
using Scaffold.ServiceHost.Abstractions;

namespace Scaffold.ServiceHost;

/// <summary>
/// Named Pipe server lifecycle management.
///
/// Two unidirectional pipes:
/// - command pipe: CLI → ServiceHost (read)
/// - event pipe:   ServiceHost → CLI (write, handled by IServiceEventPublisher)
///
/// Multi-session: every CLI invocation is one session. The ServiceHost
/// waits for the next CLI connection after a session ends, until
/// ShutdownToken triggers.
///
/// Session startup order:
/// 1. Open the event pipe – wait for CLI connection (IPipeConnectionLifecycle)
/// 2. Send ServiceReadyEvent (IServiceEventPublisher)
/// 3. Open the command pipe – wait for CLI connection
/// 4. Command loop – CLI exits → pipe closes → session ends
///
/// Shutdown:
/// - ShutdownRequest → CommandDispatcher cancels ShutdownToken → loop exits
/// - Ctrl+C / SIGTERM → the service CancellationToken is cancelled → loop exits
/// </summary>
public class PipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly CommandDispatcher _dispatcher;  // TODO: ICommandDispatcher interface (next refactor)
    private readonly IServiceEventPublisher _eventPublisher;
    private readonly IPipeConnectionLifecycle _pipeLifecycle;
    private readonly string _version;

    private NamedPipeServerStream? _commandPipe;
    private bool _disposed;

    public PipeServer(
        string pipeName,
        CommandDispatcher dispatcher,
        IServiceEventPublisher eventPublisher,
        IPipeConnectionLifecycle pipeLifecycle,
        string version = "1.0.0")
    {
        _pipeName = pipeName;
        _dispatcher = dispatcher;
        _eventPublisher = eventPublisher;
        _pipeLifecycle = pipeLifecycle;
        _version = version;
    }

    /// <summary>
    /// Starts the PipeServer. After every CLI session it waits
    /// for the next one, until ShutdownToken triggers.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _dispatcher.ShutdownToken);

        var token = linkedCts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await RunSingleSessionAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (token.IsCancellationRequested) break;

            // CLI exited, ServiceHost keeps running – preparing for the next session
            Console.WriteLine(
                "[ServiceHost] Session closed. Preparing for the next CLI connection...");

            try
            {
                await _pipeLifecycle.ResetForNewConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// The full lifecycle of a single CLI session:
    /// event pipe → ready event → command pipe → command loop.
    /// </summary>
    private async Task RunSingleSessionAsync(CancellationToken token)
    {
        // 1. Event pipe – wait for CLI connection
        Console.WriteLine("[ServiceHost] Opening event pipe...");
        await _pipeLifecycle.WaitForConnectionAsync(token);
        Console.WriteLine("[ServiceHost] Event pipe: CLI connected.");

        // 2. ServiceReadyEvent – this is the CLI's ready signal
        await _eventPublisher.PublishServiceReadyAsync(_version, token);
        Console.WriteLine($"[ServiceHost] Ready. Version: {_version}");

        // 3. Opening the command pipe
        Console.WriteLine("[ServiceHost] Opening command pipe...");
        _commandPipe = new NamedPipeServerStream(
            pipeName: $"{_pipeName}-commands",
            direction: PipeDirection.In,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        await _commandPipe.WaitForConnectionAsync(token);
        Console.WriteLine("[ServiceHost] Command pipe: CLI connected.");

        // 4. Command loop
        await RunCommandLoopAsync(token);

        // Cleanup – the next session needs a new command pipe
        await _commandPipe.DisposeAsync();
        _commandPipe = null;
    }

    /// <summary>
    /// Continuously reads the command pipe and dispatches the commands.
    /// Stops when ShutdownToken triggers or the pipe closes.
    /// </summary>
    private async Task RunCommandLoopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[ServiceHost] Command loop started. Waiting for commands...");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CommandEnvelope envelope;

                try
                {
                    envelope = CommandEnvelope.Parser.ParseDelimitedFrom(_commandPipe);
                }
                catch (InvalidProtocolBufferException ex)
                {
                    await _eventPublisher.PublishServiceErrorAsync(
                        errorCode: "PARSE_ERROR",
                        errorMessage: $"Protobuf parse error: {ex.Message}",
                        ct: cancellationToken);
                    continue;
                }
                catch (IOException ioe)
                {
                    Console.WriteLine(ioe is EndOfStreamException
                        ? "[ServiceHost] Command pipe: end of stream."
                        : "[ServiceHost] Command pipe closed. CLI exited.");
                    break;
                }

                try
                {
                    await _dispatcher.DispatchAsync(envelope, cancellationToken);
                }
                catch (Exception ex)
                {
                    await _eventPublisher.PublishServiceErrorAsync(
                        errorCode: "DISPATCH_ERROR",
                        errorMessage: $"Command processing error: {ex.Message}",
                        ct: cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) { }

        Console.WriteLine("[ServiceHost] Command loop stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_commandPipe is not null)
            await _commandPipe.DisposeAsync();
    }
}