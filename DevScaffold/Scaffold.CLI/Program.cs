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

using Microsoft.Extensions.DependencyInjection;
using Scaffold.Agent.Protocol;
using Scaffold.Application.Interfaces;
using Scaffold.CLI;
using Scaffold.Domain.Models;
using Scaffold.Infrastructure.StepConfig;

// ─────────────────────────────────────────────
// DevScaffold CLI
//
// Használat:
//   DevScaffold run  --config <step_agent_config.yaml>
//                    --input  <input.yaml>
//                    --model  <model_alias>
//                    --host   <servicehost_binary_path>
//                    --models <models.yaml>
//                   [--output   <output mappa>]     (alapértelmezett: ./output)
//                   [--pipe-name <név>]             (alapértelmezett: scaffold)
//
//   DevScaffold shutdown
//                   [--pipe-name <név>]             (alapértelmezett: scaffold)
//
// Visszatérési kódok:
//   0 – Accept vagy Edit (sikeres lépés)
//   1 – Hiba (kapcsolódási probléma, parse hiba, váratlan kivétel)
//   2 – Reject (a human visszaküldte, újragenerálás szükséges)
// ─────────────────────────────────────────────

var (mode, parsedArgs) = ParseArgs(Environment.GetCommandLineArgs()[1..]);

if (mode is "help" or "" || parsedArgs.ContainsKey("--help"))
{
    PrintHelp();
    return 0;
}

// Graceful shutdown – Ctrl+C esetén
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("[SCAFFOLD] Megszakítás jelzése...");
    cts.Cancel();
};

return mode switch
{
    "run" => await RunAsync(parsedArgs, cts.Token),
    "shutdown" => await ShutdownAsync(parsedArgs, cts.Token),
    _ => UnknownMode(mode)
};

// ─────────────────────────────────────────────
// run parancs
// ─────────────────────────────────────────────

async Task<int> RunAsync(
    Dictionary<string, string> args,
    CancellationToken cancellationToken)
{
    if (!ValidateRunArgs(args))
        return 1;

    var stepConfigPath = args["--config"];
    var inputYamlPath = args["--input"];
    var modelAlias = args["--model"];
    var serviceHostPath = args["--host"];
    var modelsYamlPath = args["--models"];
    var outputBasePath = args.GetValueOrDefault("--output", "./output");
    var pipeName = args.GetValueOrDefault("--pipe-name", "scaffold");

    // ─────────────────────────────────────────────
    // DI konténer
    // ─────────────────────────────────────────────

    var services = new ServiceCollection();
    services.AddSingleton<IStepAgentConfigReader, YamlStepAgentConfigReader>();
    services.AddSingleton<IInputAssembler, InputAssembler>();
    services.AddSingleton<IHumanValidationService, ConsoleHumanValidationService>();
    var provider = services.BuildServiceProvider();

    // ─────────────────────────────────────────────
    // ServiceHost indítás
    // ─────────────────────────────────────────────

    var launcher = new ServiceHostLauncher(
        serviceHostPath: serviceHostPath,
        modelsYamlPath: modelsYamlPath,
        outputBasePath: outputBasePath,
        pipeName: pipeName);

    PipeClient pipeClient;

    try
    {
        pipeClient = await launcher.EnsureRunningAsync(cancellationToken);
    }
    catch (InvalidOperationException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(ex.Message);
        Console.ResetColor();
        return 1;
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("[SCAFFOLD] Megszakítva.");
        return 1;
    }

    // ─────────────────────────────────────────────
    // Lépés futtatás
    // ─────────────────────────────────────────────

    await using (pipeClient)
    {
        await using var session = new ScaffoldSession(
            pipeClient,
            provider.GetRequiredService<IStepAgentConfigReader>(),
            provider.GetRequiredService<IInputAssembler>(),
            provider.GetRequiredService<IHumanValidationService>(),
            stepConfigPath: stepConfigPath,
            inputYamlPath: inputYamlPath,
            modelAlias: modelAlias);

        try
        {
            var decision = await session.RunAsync(cancellationToken);

            return decision.Outcome switch
            {
                ValidationOutcome.Accept => 0,
                ValidationOutcome.Edit => 0,
                ValidationOutcome.Reject => HandleReject(decision),
                _ => 0
            };
        }
        catch (Scaffold.Application.ScaffoldInputValidationException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(ex.Message);
            Console.ResetColor();
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[SCAFFOLD] Futás megszakítva.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[SCAFFOLD ERROR] Váratlan hiba: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            Console.ResetColor();
            return 1;
        }
    }
}

static int HandleReject(ValidationDecision decision)
{
    Console.WriteLine("[SCAFFOLD] Lépés visszaküldve.");

    if (!string.IsNullOrWhiteSpace(decision.RejectionClarification))
        Console.WriteLine($"[SCAFFOLD] Pontosítás: {decision.RejectionClarification}");

    // Exit 2 jelzi a hívónak (pl. shell script) hogy reject történt,
    // nem hiba – újrafuttatás szükséges.
    return 2;
}

// ─────────────────────────────────────────────
// shutdown parancs
// ─────────────────────────────────────────────

async Task<int> ShutdownAsync(
    Dictionary<string, string> args,
    CancellationToken cancellationToken)
{
    var pipeName = args.GetValueOrDefault("--pipe-name", "scaffold");

    if (!File.Exists($@"\\.\pipe\{pipeName}-events"))
    {
        Console.WriteLine("[SCAFFOLD] ServiceHost nem fut.");
        return 0;
    }

    var pipeClient = new PipeClient(pipeName);

    await using (pipeClient)
    {
        try
        {
            await pipeClient.ConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SCAFFOLD ERROR] Pipe csatlakozás sikertelen: {ex.Message}");
            return 1;
        }

        var isReady = await pipeClient.WaitForReadyAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);

        if (!isReady)
        {
            Console.Error.WriteLine("[SCAFFOLD ERROR] ServiceHost nem válaszolt.");
            return 1;
        }

        var shuttingDownTcs = new TaskCompletionSource();
        pipeClient.EventReceived += evt =>
        {
            if (evt.EventCase == EventEnvelope.EventOneofCase.ServiceShuttingDown)
                shuttingDownTcs.TrySetResult();
            return Task.CompletedTask;
        };

        await pipeClient.StartAsync(cancellationToken);

        await pipeClient.SendAsync(
            new CommandEnvelope { Shutdown = new ShutdownRequest() },
            cancellationToken);

        Console.WriteLine("[SCAFFOLD] Leállítás elküldve. Várakozás visszaigazolásra...");

        await Task.WhenAny(
            shuttingDownTcs.Task,
            Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));

        Console.WriteLine(shuttingDownTcs.Task.IsCompleted
            ? "[SCAFFOLD] ServiceHost leállítva."
            : "[SCAFFOLD] Leállítás elküldve (visszaigazolás nem érkezett).");

        return 0;
    }
}

// ─────────────────────────────────────────────
// Segédfüggvények
// ─────────────────────────────────────────────

static (string mode, Dictionary<string, string> args) ParseArgs(string[] rawArgs)
{
    if (rawArgs.Length == 0)
        return ("help", new Dictionary<string, string>());

    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var mode = rawArgs[0];

    for (int i = 1; i < rawArgs.Length; i++)
    {
        if (!rawArgs[i].StartsWith("--"))
            continue;

        var key = rawArgs[i];
        var value = (i + 1 < rawArgs.Length && !rawArgs[i + 1].StartsWith("--"))
            ? rawArgs[++i]
            : "true";

        result[key] = value;
    }

    return (mode, result);
}

static bool ValidateRunArgs(Dictionary<string, string> args)
{
    var required = new[] { "--config", "--input", "--model", "--host", "--models" };
    var missing = required.Where(r => !args.ContainsKey(r)).ToList();

    if (missing.Count == 0) return true;

    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(
        $"[SCAFFOLD ERROR] Hiányzó argumentumok: {string.Join(", ", missing)}");
    Console.ResetColor();
    Console.WriteLine();
    PrintHelp();
    return false;
}

static int UnknownMode(string mode)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[SCAFFOLD ERROR] Ismeretlen parancs: {mode}");
    Console.ResetColor();
    Console.WriteLine();
    PrintHelp();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""

        DevScaffold – Scaffold Protocol CLI

        Használat:
          DevScaffold run  --config <step_agent_config.yaml>
                           --input  <input.yaml>
                           --model  <model_alias>
                           --host   <servicehost_binary_path>
                           --models <models.yaml>
                          [--output   <output mappa>]
                          [--pipe-name <név>]

          DevScaffold shutdown
                          [--pipe-name <név>]

        Visszatérési kódok:
          0 – Accept vagy Edit
          1 – Hiba
          2 – Reject (újrafuttatás szükséges)

        """);
}