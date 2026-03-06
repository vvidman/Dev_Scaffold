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

using System.Diagnostics;

namespace Scaffold.CLI;

/// <summary>
/// ServiceHost process automatikus indítása és pipe-ready várakozás.
///
/// Retry policy (ADR #10):
/// - Maximum 3 kísérlet
/// - Kísérletenként 60 másodperces timeout a ServiceReadyEvent-re
/// - Kísérletek között nincs várakozás – timeout lejárta után azonnal indul
///
/// ServiceHost futás ellenőrzése (ADR #9):
/// - Named Pipe fájl létezését teszteli a pipe-ra való csatlakozás helyett
/// - File.Exists(@"\\.\pipe\{name}") – nem fogyasztja el a szerver várakozását
/// </summary>
public class ServiceHostLauncher
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

    private readonly string _serviceHostPath;
    private readonly string _modelsYamlPath;
    private readonly string _outputBasePath;
    private readonly string _pipeName;

    public ServiceHostLauncher(
        string serviceHostPath,
        string modelsYamlPath,
        string outputBasePath,
        string pipeName)
    {
        _serviceHostPath = serviceHostPath;
        _modelsYamlPath = modelsYamlPath;
        _outputBasePath = outputBasePath;
        _pipeName = pipeName;
    }

    /// <summary>
    /// Biztosítja hogy a ServiceHost fut és kész fogadni parancsokat.
    /// Visszaad egy csatlakoztatott, kész PipeClient-et.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Ha 3 kísérlet után sem sikerül elindítani a ServiceHost-ot.
    /// </exception>
    public async Task<PipeClient> EnsureRunningAsync(
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Console.WriteLine(
                $"[SCAFFOLD] ServiceHost indítás ({attempt}/{MaxAttempts})...");

            var pipeClient = await TryStartAndConnectAsync(cancellationToken);

            if (pipeClient is not null)
            {
                Console.WriteLine("[SCAFFOLD] ServiceHost kész.");
                return pipeClient;
            }

            if (attempt < MaxAttempts)
            {
                Console.WriteLine(
                    $"[SCAFFOLD] ServiceHost nem válaszolt ({attempt}/{MaxAttempts}). " +
                    $"Újrapróbálkozás...");
            }
        }

        throw new InvalidOperationException(
            $"[SCAFFOLD ERROR] ServiceHost {MaxAttempts} kísérlet után sem válaszolt. " +
            $"Ellenőrizd a következőket:\n" +
            $"  - ServiceHost path: {_serviceHostPath}\n" +
            $"  - Models yaml: {_modelsYamlPath}\n" +
            $"  - Output mappa: {_outputBasePath}");
    }

    // ─────────────────────────────────────────────
    // Privát implementáció
    // ─────────────────────────────────────────────

    private async Task<PipeClient?> TryStartAndConnectAsync(
        CancellationToken cancellationToken)
    {
        if (IsServiceHostRunning())
        {
            Console.WriteLine(
                "[SCAFFOLD] ServiceHost pipe él. Csatlakozás...");
        }
        else
        {
            Console.WriteLine("[SCAFFOLD] ServiceHost process indítása...");
            StartServiceHostProcess();
        }

        var pipeClient = new PipeClient(_pipeName);

        // 1. Event pipe csatlakozás
        try
        {
            await pipeClient.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await pipeClient.DisposeAsync();
            Console.Error.WriteLine(
                $"[SCAFFOLD] Event pipe csatlakozás sikertelen: {ex.Message}");
            return null;
        }

        // 2. ServiceReadyEvent várakozás (közvetlen pipe olvasás, event loop nélkül)
        Console.WriteLine(
            $"[SCAFFOLD] Várakozás ServiceHost ready jelzésre " +
            $"(max {ReadyTimeout.TotalSeconds}s)...");

        var isReady = await pipeClient.WaitForReadyAsync(ReadyTimeout, cancellationToken);

        if (!isReady)
        {
            await pipeClient.DisposeAsync();
            return null;
        }

        // 3. Event loop indítás + command pipe csatlakozás
        try
        {
            await pipeClient.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await pipeClient.DisposeAsync();
            Console.Error.WriteLine(
                $"[SCAFFOLD] Command pipe csatlakozás sikertelen: {ex.Message}");
            return null;
        }

        return pipeClient;
    }

    /// <summary>
    /// Ellenőrzi hogy él-e már a ServiceHost event pipe-ja.
    ///
    /// File.Exists-et használ Connect helyett – így nem fogyasztja el
    /// a szerver oldal WaitForConnectionAsync várakozását (ADR #9).
    /// </summary>
    private bool IsServiceHostRunning() =>
        File.Exists($@"\\.\pipe\{_pipeName}-events");

    /// <summary>
    /// Elindítja a ServiceHost processt a szükséges argumentumokkal.
    /// </summary>
    private void StartServiceHostProcess()
    {
        if (!File.Exists(_serviceHostPath))
            throw new FileNotFoundException(
                $"ServiceHost binary nem található: {_serviceHostPath}");

        var processInfo = new ProcessStartInfo
        {
            FileName = _serviceHostPath,
            Arguments = BuildArguments(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException(
                "ServiceHost process indítása sikertelen.");

        Console.WriteLine($"[SCAFFOLD] ServiceHost process elindítva. PID: {process.Id}");
    }

    private string BuildArguments() =>
        $"--models \"{_modelsYamlPath}\" " +
        $"--output \"{_outputBasePath}\" " +
        $"--pipe-name \"{_pipeName}\"";
}
