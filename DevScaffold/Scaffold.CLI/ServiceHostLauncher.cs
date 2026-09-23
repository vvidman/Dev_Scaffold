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
/// Automatic ServiceHost process startup and waiting for pipe-ready.
///
/// Retry policy (ADR #10):
/// - Maximum 3 attempts
/// - 60-second timeout per attempt for ServiceReadyEvent
/// - No delay between attempts – starts immediately after a timeout expires
///
/// Checking whether the ServiceHost is running (ADR #9):
/// - Tests whether the Named Pipe file exists, instead of connecting to the pipe
/// - File.Exists(@"\\.\pipe\{name}") – does not consume the server's wait
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
    /// Ensures the ServiceHost is running and ready to receive commands.
    /// Returns a connected, ready PipeClient.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// If the ServiceHost still cannot be started after 3 attempts.
    /// </exception>
    public async Task<PipeClient> EnsureRunningAsync(
        CancellationToken cancellationToken = default)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            Console.WriteLine(
                $"[SCAFFOLD] Starting ServiceHost ({attempt}/{MaxAttempts})...");

            var pipeClient = await TryStartAndConnectAsync(cancellationToken);

            if (pipeClient is not null)
            {
                Console.WriteLine("[SCAFFOLD] ServiceHost ready.");
                return pipeClient;
            }

            if (attempt < MaxAttempts)
            {
                Console.WriteLine(
                    $"[SCAFFOLD] ServiceHost did not respond ({attempt}/{MaxAttempts}). " +
                    $"Retrying...");
            }
        }

        throw new InvalidOperationException(
            $"[SCAFFOLD ERROR] ServiceHost did not respond after {MaxAttempts} attempts. " +
            $"Check the following:\n" +
            $"  - ServiceHost path: {_serviceHostPath}\n" +
            $"  - Models yaml: {_modelsYamlPath}\n" +
            $"  - Output folder: {_outputBasePath}");
    }

    // ─────────────────────────────────────────────
    // Private implementation
    // ─────────────────────────────────────────────

    private async Task<PipeClient?> TryStartAndConnectAsync(
        CancellationToken cancellationToken)
    {
        if (IsServiceHostRunning())
        {
            Console.WriteLine(
                "[SCAFFOLD] ServiceHost pipe is alive. Connecting...");
        }
        else
        {
            Console.WriteLine("[SCAFFOLD] Starting ServiceHost process...");
            StartServiceHostProcess();
        }

        var pipeClient = new PipeClient(_pipeName);

        // 1. Event pipe connection
        try
        {
            await pipeClient.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await pipeClient.DisposeAsync();
            Console.Error.WriteLine(
                $"[SCAFFOLD] Event pipe connection failed: {ex.Message}");
            return null;
        }

        // 2. Waiting for ServiceReadyEvent (direct pipe read, no event loop yet)
        Console.WriteLine(
            $"[SCAFFOLD] Waiting for ServiceHost ready signal " +
            $"(max {ReadyTimeout.TotalSeconds}s)...");

        var isReady = await pipeClient.WaitForReadyAsync(ReadyTimeout, cancellationToken);

        if (!isReady)
        {
            await pipeClient.DisposeAsync();
            return null;
        }

        // 3. Starting the event loop + connecting to the command pipe
        try
        {
            await pipeClient.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await pipeClient.DisposeAsync();
            Console.Error.WriteLine(
                $"[SCAFFOLD] Command pipe connection failed: {ex.Message}");
            return null;
        }

        return pipeClient;
    }

    /// <summary>
    /// Checks whether the ServiceHost's event pipe is already alive.
    ///
    /// Uses File.Exists instead of Connect – this way it does not
    /// consume the server side's WaitForConnectionAsync wait (ADR #9).
    /// </summary>
    private bool IsServiceHostRunning() =>
        File.Exists($@"\\.\pipe\{_pipeName}-events");

    /// <summary>
    /// Starts the ServiceHost process with the required arguments.
    /// </summary>
    private void StartServiceHostProcess()
    {
        if (!File.Exists(_serviceHostPath))
            throw new FileNotFoundException(
                $"ServiceHost binary not found: {_serviceHostPath}");

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
                "Failed to start the ServiceHost process.");

        Console.WriteLine($"[SCAFFOLD] ServiceHost process started. PID: {process.Id}");
    }

    private string BuildArguments() =>
        $"--models \"{_modelsYamlPath}\" " +
        $"--output \"{_outputBasePath}\" " +
        $"--pipe-name \"{_pipeName}\"";
}
