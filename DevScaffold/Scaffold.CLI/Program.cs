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
    services.AddSingleton<IScaffoldConsole, ConsoleScaffoldConsole>();
    services.AddSingleton<IStepAgentConfigReader, YamlStepAgentConfigReader>();
    services.AddSingleton<IInputAssembler, InputAssembler>();
    services.AddSingleton<IHumanValidationService, ConsoleHumanValidationService>();
    var provider = services.BuildServiceProvider();

    var scaffoldConsole = provider.GetRequiredService<IScaffoldConsole>();

    // ─────────────────────────────────────────────
    // Step azonosítás – generáció számításhoz szükséges a stepId
    // ─────────────────────────────────────────────

    string stepId;
    try
    {
        var configReader = provider.GetRequiredService<IStepAgentConfigReader>();
        stepId = configReader.Load(stepConfigPath).Step;
    }
    catch (Exception ex)
    {
        scaffoldConsole.WriteError($"[SCAFFOLD ERROR] Step konfiguráció betöltési hiba: {ex.Message}");
        return 1;
    }

    // ─────────────────────────────────────────────
    // Generáció számítás + step output folder létrehozása
    // ─────────────────────────────────────────────

    int generation;
    string stepOutputFolder;
    try
    {
        generation = ComputeNextGeneration(outputBasePath, stepId);
        stepOutputFolder = Path.Combine(outputBasePath, $"{stepId}_{generation}");
        Directory.CreateDirectory(stepOutputFolder);
        scaffoldConsole.WriteCli(
            $"[SCAFFOLD] Step output folder: {stepOutputFolder} (generáció: {generation})");
    }
    catch (Exception ex)
    {
        scaffoldConsole.WriteError(
            $"[SCAFFOLD ERROR] Output folder létrehozása sikertelen: {ex.Message}");
        return 1;
    }

    // ─────────────────────────────────────────────
    // Audit logger – step output folderbe kerül
    // ─────────────────────────────────────────────

    var auditLogPath = Path.Combine(stepOutputFolder, "audit.log");
    await using var auditLogger = new FileAuditLogger(auditLogPath);

    scaffoldConsole.WriteCli($"[SCAFFOLD] Audit log: {auditLogPath}");

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
        scaffoldConsole.WriteError(ex.Message);
        auditLogger.Log(Scaffold.Application.AuditEvent.Error,
            $"reason=servicehost_start_failed message=\"{ex.Message}\"");
        return 1;
    }
    catch (OperationCanceledException)
    {
        scaffoldConsole.WriteCli("[SCAFFOLD] Megszakítva.");
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
            auditLogger,
            scaffoldConsole,
            stepConfigPath: stepConfigPath,
            inputYamlPath: inputYamlPath,
            modelAlias: modelAlias,
            stepOutputFolder: stepOutputFolder,
            generation: generation);

        try
        {
            var decision = await session.RunAsync(cancellationToken);

            return decision.Outcome switch
            {
                ValidationOutcome.Accept => 0,
                ValidationOutcome.Edit => 0,
                ValidationOutcome.Reject => HandleReject(decision, scaffoldConsole),
                _ => 0
            };
        }
        catch (Scaffold.Application.ScaffoldInputValidationException ex)
        {
            scaffoldConsole.WriteError(ex.Message);
            auditLogger.Log(Scaffold.Application.AuditEvent.Error,
                $"reason=input_validation message=\"{ex.Message.Replace("\"", "'")}\"");
            return 1;
        }
        catch (OperationCanceledException)
        {
            scaffoldConsole.WriteCli("[SCAFFOLD] Futás megszakítva.");
            return 1;
        }
        catch (Exception ex)
        {
            scaffoldConsole.WriteError($"[SCAFFOLD ERROR] Váratlan hiba: {ex.Message}");
            scaffoldConsole.WriteError(ex.StackTrace ?? string.Empty);
            auditLogger.Log(Scaffold.Application.AuditEvent.Error,
                $"reason=unexpected message=\"{ex.Message.Replace("\"", "'")}\"");
            return 1;
        }
    }
}

static int HandleReject(ValidationDecision decision, IScaffoldConsole console)
{
    console.WriteCli("[SCAFFOLD] Lépés visszaküldve.");

    if (!string.IsNullOrWhiteSpace(decision.RejectionClarification))
        console.WriteCli($"[SCAFFOLD] Pontosítás rögzítve az audit logban.");

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
    var console = new ConsoleScaffoldConsole();

    if (!File.Exists($@"\\.\pipe\{pipeName}-events"))
    {
        console.WriteCli("[SCAFFOLD] ServiceHost nem fut.");
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
            console.WriteError($"[SCAFFOLD ERROR] Pipe csatlakozás sikertelen: {ex.Message}");
            return 1;
        }

        var isReady = await pipeClient.WaitForReadyAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);

        if (!isReady)
        {
            console.WriteError("[SCAFFOLD ERROR] ServiceHost nem válaszolt.");
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

        console.WriteCli("[SCAFFOLD] Leállítás elküldve. Várakozás visszaigazolásra...");

        await Task.WhenAny(
            shuttingDownTcs.Task,
            Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));

        console.WriteCli(shuttingDownTcs.Task.IsCompleted
            ? "[SCAFFOLD] ServiceHost leállítva."
            : "[SCAFFOLD] Leállítás elküldve (visszaigazolás nem érkezett).");

        return 0;
    }
}

// ─────────────────────────────────────────────
// Generáció számítás
// ─────────────────────────────────────────────

/// <summary>
/// Meghatározza a következő generáció sorszámát.
/// Filesystem alapú – CLI process újraindulás esetén is helyes marad.
///
/// Pl. ha létezik task_breakdown_1 és task_breakdown_2, a következő 3.
/// </summary>
static int ComputeNextGeneration(string outputBasePath, string stepId)
{
    if (!Directory.Exists(outputBasePath))
        return 1;

    // Megnézi hány {stepId}_* nevű folder létezik már
    var existing = Directory.GetDirectories(
        outputBasePath,
        $"{stepId}_*",
        SearchOption.TopDirectoryOnly);

    // A sorszámokat kinyeri és a maximumot veszi – lyukas sorozat esetén is helyes
    // Pl. ha csak _1 és _3 van (a _2 törölve lett), akkor a következő _4
    var maxGeneration = existing
        .Select(dir => Path.GetFileName(dir))
        .Select(name => TryParseGeneration(name, stepId))
        .Where(n => n.HasValue)
        .Select(n => n!.Value)
        .DefaultIfEmpty(0)
        .Max();

    return maxGeneration + 1;
}

static int? TryParseGeneration(string folderName, string stepId)
{
    var prefix = $"{stepId}_";
    if (!folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        return null;

    var suffix = folderName[prefix.Length..];
    return int.TryParse(suffix, out var n) ? n : null;
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