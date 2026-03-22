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
/// Named Pipe kliens a CLI oldalán.
///
/// Felelőssége:
/// - Command pipe-ra CommandEnvelope küldés (CLI → ServiceHost)
/// - Event pipe-ról EventEnvelope olvasás (ServiceHost → CLI)
///
/// Csatlakozási sorrend (ADR #8):
///   1. ConnectAsync     – event pipe csatlakozás
///   2. WaitForReadyAsync – ServiceReadyEvent közvetlen olvasás (loop nélkül)
///   3. StartAsync       – event loop indítás + command pipe csatlakozás
///
/// Ez a sorrend garantálja hogy a ServiceReadyEvent nem vész el az
/// event loop és a WaitForReadyAsync közötti versenyhelyzetben.
/// </summary>
public class PipeClient : IPipeClient, IAsyncDisposable
{
    private readonly string _pipeName;

    private NamedPipeClientStream? _commandPipe;
    private NamedPipeClientStream? _eventPipe;

    private Task? _eventLoopTask;
    private CancellationTokenSource? _eventLoopCts;

    private bool _disposed;

    // Esemény callback – minden beérkező EventEnvelope-hoz meghívódik
    public event Func<EventEnvelope, Task>? EventReceived;

    public PipeClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    /// <summary>
    /// 1. lépés: Csatlakozik az event pipe-ra.
    /// A ServiceHost ezen küldi a ServiceReadyEvent-et.
    /// Command pipe és event loop még NEM indul – azok a StartAsync feladata.
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
    /// 2. lépés: Megvárja az első ServiceReadyEvent-et az event pipe-on.
    /// Közvetlenül olvassa a pipe-ot – az event loop még nem fut.
    ///
    /// A timeout lejáratakor a pipe-ot zárja le, amivel megszakítja
    /// a blokkoló ParseDelimitedFrom hívást.
    /// </summary>
    /// <returns>true ha megérkezett a ready jel, false ha timeout vagy hiba</returns>
    public async Task<bool> WaitForReadyAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (_eventPipe is null)
            throw new InvalidOperationException(
                "Event pipe nincs csatlakoztatva. Hívj ConnectAsync-t először.");

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        // ParseDelimitedFrom szinkron blokkoló – a pipe lezárásával szakítható meg
        using var reg = linkedCts.Token.Register(() =>
        {
            try { _eventPipe.Dispose(); }
            catch { /* Dispose hiba – normál eset timeout alatt */ }
        });

        try
        {
            // Task.Run – nem blokkoljuk az async szálat
            var envelope = await Task.Run(
                () => EventEnvelope.Parser.ParseDelimitedFrom(_eventPipe));

            return envelope.EventCase == EventEnvelope.EventOneofCase.ServiceReady;
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    /// <summary>
    /// 3. lépés: Event loop indítása és command pipe csatlakozás.
    /// WaitForReadyAsync sikeres visszatérése után hívandó.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Event loop indítása – ettől kezdve az EventReceived callback-ek tüzelnek
        _eventLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _eventLoopTask = Task.Run(
            () => RunEventLoopAsync(_eventLoopCts.Token),
            _eventLoopCts.Token);

        // Command pipe csatlakozás – a ServiceHost az event loop indulása után nyitja meg
        _commandPipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: $"{_pipeName}-commands",
            direction: PipeDirection.Out,
            options: PipeOptions.Asynchronous);

        await _commandPipe.ConnectAsync(cancellationToken);
    }

    /// <summary>
    /// CommandEnvelope küldése a ServiceHost-nak.
    /// StartAsync után hívható.
    /// </summary>
    public async Task SendAsync(
        CommandEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_commandPipe is null || !_commandPipe.IsConnected)
            throw new InvalidOperationException(
                "Command pipe nincs csatlakoztatva. Hívj StartAsync-t először.");

        // WriteDelimitedTo – varint hossz prefix + protobuf bináris adat
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
                    // ParseDelimitedFrom blokkol amíg üzenet nem érkezik
                    envelope = EventEnvelope.Parser.ParseDelimitedFrom(_eventPipe);
                }
                catch (InvalidProtocolBufferException ipbe)
                {
                    Console.Error.WriteLine($"[CLI] Event parse hiba: {ipbe.Message}");
                    continue;
                }
                catch (IOException ioe)
                {
                    if (ioe is EndOfStreamException)
                        Console.WriteLine("[CLI] Event pipe: stream vége. ServiceHost leállt.");
                    else
                        Console.WriteLine("[CLI] Event pipe lezárult.");
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
                        Console.Error.WriteLine($"[CLI] Event callback hiba: {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normál leállás
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // 1. CTS cancel – jelzi az event loop-nak hogy le kell állni
        if (_eventLoopCts is not null)
        {
            await _eventLoopCts.CancelAsync();
            _eventLoopCts.Dispose();
        }

        // 2. Pipe lezárás – feloldja a blokkoló ParseDelimitedFrom hívást.
        //    FONTOS: ez az await _eventLoopTask ELŐTT kell, különben
        //    az event loop soha nem tér vissza (ParseDelimitedFrom blokkol).
        if (_eventPipe is not null)
        {
            try { await _eventPipe.DisposeAsync(); }
            catch (ObjectDisposedException) { /* WaitForReadyAsync timeout már lezárta */ }
        }

        // 3. Event loop megvárása – a pipe lezárása már feloldotta
        if (_eventLoopTask is not null)
        {
            try { await _eventLoopTask; }
            catch (OperationCanceledException) { }
        }

        // 4. Command pipe lezárás
        if (_commandPipe is not null)
            await _commandPipe.DisposeAsync();
    }
}