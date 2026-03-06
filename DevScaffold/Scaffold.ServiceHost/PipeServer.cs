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

namespace Scaffold.ServiceHost;

/// <summary>
/// Named Pipe szerver életciklus kezelés.
///
/// Két egyirányú pipe:
/// - command pipe: CLI → ServiceHost (olvasás)
/// - event pipe:   ServiceHost → CLI (írás, az EventPublisher kezeli)
///
/// Indulási sorrend:
/// 1. Event pipe nyitása – CLI csatlakozás megvárása
/// 2. ServiceReadyEvent küldése
/// 3. Command pipe nyitása – CLI csatlakozás megvárása
/// 4. Command loop indítása
///
/// Leállási sorrend:
/// 1. ShutdownToken triggerelődik (CommandDispatcher-től)
/// 2. Command loop leáll
/// 3. ServiceShuttingDownEvent már el lett küldve (CommandDispatcher küldte)
/// 4. Pipe-ok lezárása
/// </summary>
public class PipeServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly CommandDispatcher _dispatcher;
    private readonly EventPublisher _eventPublisher;
    private readonly string _version;

    private NamedPipeServerStream? _commandPipe;
    private bool _disposed;

    public PipeServer(
        string pipeName,
        CommandDispatcher dispatcher,
        EventPublisher eventPublisher,
        string version = "1.0.0")
    {
        _pipeName = pipeName;
        _dispatcher = dispatcher;
        _eventPublisher = eventPublisher;
        _version = version;
    }

    /// <summary>
    /// Elindítja a PipeServer-t. Minden CLI session után várakozik a következőre,
    /// amíg ShutdownToken nem triggerelődik.
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

            // CLI kilépett, de ServiceHost fut tovább – következő CLI várása
            Console.WriteLine("[ServiceHost] Session lezárva. Következő CLI kapcsolat előkészítése...");

            try
            {
                await _eventPublisher.ResetForNewConnectionAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Egyetlen CLI session teljes életciklusa:
    /// event pipe → ready event → command pipe → command loop.
    /// </summary>
    private async Task RunSingleSessionAsync(CancellationToken token)
    {
        Console.WriteLine("[ServiceHost] Event pipe megnyitása...");
        await _eventPublisher.WaitForConnectionAsync(token);
        Console.WriteLine("[ServiceHost] Event pipe: CLI csatlakozott.");

        await _eventPublisher.PublishServiceReadyAsync(_version, token);
        Console.WriteLine($"[ServiceHost] Ready. Verzió: {_version}");

        Console.WriteLine("[ServiceHost] Command pipe megnyitása...");
        _commandPipe = new NamedPipeServerStream(
            pipeName: $"{_pipeName}-commands",
            direction: PipeDirection.In,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        await _commandPipe.WaitForConnectionAsync(token);
        Console.WriteLine("[ServiceHost] Command pipe: CLI csatlakozott.");

        await RunCommandLoopAsync(token);

        // Command pipe cleanup – következő sessionhöz új kell
        await _commandPipe.DisposeAsync();
        _commandPipe = null;
    }

    /// <summary>
    /// Folyamatosan olvassa a command pipe-ot és dispatch-eli a parancsokat.
    /// Akkor áll le ha a ShutdownToken triggerelődik vagy a pipe lezárul.
    /// </summary>
    private async Task RunCommandLoopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[ServiceHost] Command loop indítva. Várakozás parancsokra...");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CommandEnvelope envelope;

                try
                {
                    // ParseDelimitedFrom – varint hossz prefix alapján olvassa az üzenetet
                    // Blokkol amíg nem érkezik üzenet vagy a pipe le nem zárul
                    envelope = CommandEnvelope.Parser.ParseDelimitedFrom(_commandPipe);
                }
                catch (InvalidProtocolBufferException ex)
                {
                    await _eventPublisher.PublishServiceErrorAsync(
                        errorCode: "PARSE_ERROR",
                        errorMessage: $"Protobuf parse hiba: {ex.Message}",
                        ct: cancellationToken);
                    continue;
                }
                catch (IOException ioe)
                {
                    if (ioe is EndOfStreamException)
                    {
                        // CLI lezárta a pipe-ot
                        Console.WriteLine("[ServiceHost] Command pipe: stream vége.");
                    }
                    else
                    {
                        // Pipe lezárult – CLI kilépett
                        Console.WriteLine("[ServiceHost] Command pipe lezárult. CLI kilépett.");
                    }
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
                        errorMessage: $"Parancs feldolgozási hiba: {ex.Message}",
                        ct: cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normál leállás – ShutdownToken vagy service cancel
        }

        Console.WriteLine("[ServiceHost] Command loop leállt.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_commandPipe is not null)
            await _commandPipe.DisposeAsync();
    }
}