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
using Scaffold.Application.Interfaces;
using System.IO.Pipes;

namespace Scaffold.CLI;

/// <summary>
/// Named Pipe client on the CLI side.
///
/// Responsibilities:
/// - Sending CommandEnvelopes on the command pipe (CLI → ServiceHost)
/// - Reading EventEnvelopes from the event pipe (ServiceHost → CLI)
///
/// Connection order (ADR #8):
///   1. ConnectAsync      – connect to the event pipe
///   2. WaitForReadyAsync – read ServiceReadyEvent directly (no loop yet)
///   3. StartAsync        – start the event loop + connect to the command pipe
///
/// This order guarantees that ServiceReadyEvent cannot be lost in a
/// race between the event loop and WaitForReadyAsync.
/// </summary>
public class PipeClient : IPipeClient, IAsyncDisposable
{
    private readonly string _pipeName;

    private NamedPipeClientStream? _commandPipe;
    private NamedPipeClientStream? _eventPipe;

    private Task? _eventLoopTask;
    private CancellationTokenSource? _eventLoopCts;

    private bool _disposed;

    // Event callback – invoked for every incoming EventEnvelope
    public event Func<EventEnvelope, Task>? EventReceived;

    public PipeClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    /// <summary>
    /// Step 1: Connects to the event pipe.
    /// The ServiceHost sends ServiceReadyEvent on this.
    /// The command pipe and event loop do NOT start yet – that is StartAsync's job.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _eventPipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: $"{_pipeName}-events",
            direction: PipeDirection.In,
            options: PipeOptions.Asynchronous);

        await _eventPipe.ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// Step 2: Waits for the first ServiceReadyEvent on the event pipe.
    /// Reads the pipe directly – the event loop is not running yet.
    ///
    /// When the timeout expires, closes the pipe, which interrupts
    /// the blocking ParseDelimitedFrom call.
    /// </summary>
    /// <returns>true if the ready signal arrived, false on timeout or error</returns>
    public async Task<bool> WaitForReadyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (_eventPipe is null)
            throw new InvalidOperationException(
                "Event pipe is not connected. Call ConnectAsync first.");

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        // ParseDelimitedFrom is a blocking sync call – interrupted by closing the pipe
        using var reg = linkedCts.Token.Register(() =>
        {
            try { _eventPipe.Dispose(); }
            catch { /* Dispose error – expected during a timeout */ }
        });

        try
        {
            // Task.Run – so we don't block the async thread
            var envelope = await Task.Run(
                () => EventEnvelope.Parser.ParseDelimitedFrom(_eventPipe));

            return envelope.EventCase == EventEnvelope.EventOneofCase.ServiceReady;
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    /// <summary>
    /// Step 3: Starts the event loop and connects to the command pipe.
    /// Call this after WaitForReadyAsync returns successfully.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Starting the event loop – EventReceived callbacks fire from now on
        _eventLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _eventLoopTask = Task.Run(
            () => RunEventLoopAsync(_eventLoopCts.Token),
            _eventLoopCts.Token);

        // Connecting to the command pipe – the ServiceHost opens it after the event loop starts
        _commandPipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: $"{_pipeName}-commands",
            direction: PipeDirection.Out,
            options: PipeOptions.Asynchronous);

        await _commandPipe.ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// Sends a CommandEnvelope to the ServiceHost.
    /// Can be called after StartAsync.
    /// </summary>
    public async Task SendAsync(
        CommandEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_commandPipe is null || !_commandPipe.IsConnected)
            throw new InvalidOperationException(
                "Command pipe is not connected. Call StartAsync first.");

        // WriteDelimitedTo – varint length prefix + protobuf binary data
        envelope.WriteDelimitedTo(_commandPipe);
        await _commandPipe.FlushAsync(cancellationToken);
    }

    // ─────────────────────────────────────────────
    // Event loop
    // ─────────────────────────────────────────────

    private async Task RunEventLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                EventEnvelope envelope;

                try
                {
                    // ParseDelimitedFrom blocks until a message arrives
                    envelope = EventEnvelope.Parser.ParseDelimitedFrom(_eventPipe);
                }
                catch (InvalidProtocolBufferException ipbe)
                {
                    Console.Error.WriteLine($"[CLI] Event parse error: {ipbe.Message}");
                    continue;
                }
                catch (IOException ioe)
                {
                    if (ioe is EndOfStreamException)
                        Console.WriteLine("[CLI] Event pipe: end of stream. ServiceHost stopped.");
                    else
                        Console.WriteLine("[CLI] Event pipe closed.");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (EventReceived is not null)
                {
                    try
                    {
                        await EventReceived(envelope);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[CLI] Event callback error: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // 1. Cancel the CTS – signals the event loop that it must stop
        if (_eventLoopCts is not null)
        {
            await _eventLoopCts.CancelAsync();
            _eventLoopCts.Dispose();
        }

        // 2. Close the pipe – unblocks the blocking ParseDelimitedFrom call.
        //    IMPORTANT: this must happen BEFORE await _eventLoopTask, otherwise
        //    the event loop never returns (ParseDelimitedFrom blocks).
        if (_eventPipe is not null)
        {
            try { await _eventPipe.DisposeAsync(); }
            catch (ObjectDisposedException) { /* already closed by a WaitForReadyAsync timeout */ }
        }

        // 3. Wait for the event loop – closing the pipe already unblocked it
        if (_eventLoopTask is not null)
        {
            try { await _eventLoopTask; }
            catch (OperationCanceledException) { }
        }

        // 4. Close the command pipe
        if (_commandPipe is not null)
            await _commandPipe.DisposeAsync();
    }
}