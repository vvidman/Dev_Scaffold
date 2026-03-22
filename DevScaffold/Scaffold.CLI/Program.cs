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
using Scaffold.Application;
using Scaffold.Application.Interfaces;
using Scaffold.CLI;
using Scaffold.Domain.Models;
using Scaffold.Infrastructure.ConfigHandler;
using Scaffold.Validation;
using Scaffold.Validation.Abstractions;
using Scaffold.Validation.Steps;
using Scaffold.Validation.Validators;

// ─────────────────────────────────────────────
// DevScaffold CLI
//
// Használat:
//   DevScaffold --step <step_name>
//
//   DevScaffold shutdown
//
// Konfiguráció: Scaffold.CLI.yaml (az exe mellett, mindig szükséges)
//   host_binary_path: ./bin/Scaffold.ServiceHost
//   models:           ./models.yaml
//   pipe_name:        MyProject
//   output:           ./output
//   project_context:  ./input.yaml
//   steps:
//     task_breakdown:
//       input_config:    ./task_breakdown_agent.yaml
//       validator_config: ./task_breakdown_validator.yaml
//       model_alias:     qwen2.5-7b-instruct
//     coding:
//       input_config:    ./coding_agent.yaml
//       model_alias:     qwen2.5-coder-7b-instruct
//
// Visszatérési kódok:
//   0 – Accept vagy Edit (sikeres lépés)
//   1 – Hiba (kapcsolódási probléma, parse hiba, váratlan kivétel)
//   2 – Reject (a human visszaküldte, újragenerálás szükséges)
// ─────────────────────────────────────────────

var (mode, stepName) = ParseArgs(Environment.GetCommandLineArgs()[1..]);

if (mode is "help")
{
    PrintHelp();
    return 0;
}

// ─────────────────────────────────────────────
// CLI konfiguráció betöltés
// ─────────────────────────────────────────────

var configPath = GetCliConfigPath();
CliProjectConfig cliConfig;

try
{
    var configReader = new YamlCliProjectConfigReader();
    cliConfig = configReader.Load(configPath);
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[SCAFFOLD ERROR] {ex.Message}");
    Console.ResetColor();
    return 1;
}

// ─────────────────────────────────────────────
// Graceful shutdown – Ctrl+C esetén
// ─────────────────────────────────────────────

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
    "run" => await RunAsync(cliConfig, stepName!, cts.Token),
    "shutdown" => await ShutdownAsync(cliConfig, cts.Token),
    _ => UnknownMode(mode)
};

// ─────────────────────────────────────────────
// run (--step <name>)
// ─────────────────────────────────────────────

async Task<int> RunAsync(
    CliProjectConfig config,
    string step,
    CancellationToken cancellationToken)
{
    // Step-szintű validáció (fájlok megléte, kötelező mezők)
    try
    {
        new YamlCliProjectConfigReader().Validate(config, step);
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[SCAFFOLD ERROR] {ex.Message}");
        Console.ResetColor();
        return 1;
    }

    var stepConfig = config.Steps[step];
    var stepConfigPath = stepConfig.InputConfig;
    var inputYamlPath = config.ProjectContext;
    var modelAlias = stepConfig.ModelAlias;

    // Output: {output_root}/{pipe_name}/{step}_{generation}/
    var projectFolder = SanitizeFolderName(config.PipeName);
    var outputBasePath = Path.Combine(config.Output, projectFolder);

    // ─────────────────────────────────────────────
    // DI konténer
    // ─────────────────────────────────────────────

    var services = new ServiceCollection();
    services.AddSingleton<IScaffoldConsole, ConsoleScaffoldConsole>();
    services.AddSingleton<IStepAgentConfigReader, YamlStepAgentConfigReader>();
    services.AddSingleton<IInputAssembler, InputAssembler>();
    services.AddSingleton<IFileEditorLauncher, DefaultFileEditorLauncher>();
    services.AddSingleton<IHumanValidationService, ConsoleHumanValidationService>();
    // Validation réteg
    // UniversalOutputValidator szándékosan NEM kerül DI-ba –
    // belső komponens, a CompositeOutputValidator példányosítja.
    services.AddSingleton<IStepOutputValidator, TaskBreakdownValidator>();
    services.AddSingleton(sp => new StepValidatorRegistry(sp.GetServices<IStepOutputValidator>()));
    services.AddSingleton<IOutputValidator, CompositeOutputValidator>();
    services.AddSingleton<IValidatorRuleSetReader, ValidatorYamlReader>();
    services.AddScaffoldApplication();
    var provider = services.BuildServiceProvider();

    var scaffoldConsole = provider.GetRequiredService<IScaffoldConsole>();

    // ─────────────────────────────────────────────
    // Step azonosítás – generáció számításhoz szükséges a stepId
    // ─────────────────────────────────────────────

    string stepId;
    try
    {
        var reader = provider.GetRequiredService<IStepAgentConfigReader>();
        stepId = reader.Load(stepConfigPath).Step;
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
        serviceHostPath: config.HostBinaryPath,
        modelsYamlPath: config.Models,
        outputBasePath: outputBasePath,
        pipeName: config.PipeName);

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
        await using var session = new ScaffoldStepOrchestrator(
            pipeClient,
            provider.GetRequiredService<IStepAgentConfigReader>(),
            provider.GetRequiredService<IInputAssembler>(),
            provider.GetRequiredService<IHumanValidationService>(),
            auditLogger,
            scaffoldConsole,
            provider.GetRequiredService<IInferenceResultHandler>(),
            provider.GetRequiredService<IRefinementStrategy>(),
            provider.GetRequiredService<IValidatorRuleSetReader>(),
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
        console.WriteCli("[SCAFFOLD] Pontosítás rögzítve az audit logban.");

    // Exit 2 jelzi a hívónak (pl. shell script) hogy reject történt,
    // nem hiba – újrafuttatás szükséges.
    return 2;
}

// ─────────────────────────────────────────────
// shutdown parancs
// ─────────────────────────────────────────────

async Task<int> ShutdownAsync(
    CliProjectConfig config,
    CancellationToken cancellationToken)
{
    var pipeName = config.PipeName;
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

    var existing = Directory.GetDirectories(
        outputBasePath,
        $"{stepId}_*",
        SearchOption.TopDirectoryOnly);

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

/// <summary>
/// Az exe neve alapján meghatározza a CLI yaml config útvonalát.
/// Pl. /app/Scaffold.CLI.exe → /app/Scaffold.CLI.yaml
/// </summary>
static string GetCliConfigPath()
{
    var exePath = Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, "Scaffold.CLI");

    return Path.ChangeExtension(exePath, ".yaml");
}

/// <summary>
/// Eltávolítja a szóközöket a mappa névből (output path sanitizálás).
/// </summary>
static string SanitizeFolderName(string name) =>
    name.Replace(" ", "_");

static (string mode, string? stepName) ParseArgs(string[] rawArgs)
{
    if (rawArgs.Length == 0)
        return ("help", null);

    // shutdown parancs
    if (rawArgs[0].Equals("shutdown", StringComparison.OrdinalIgnoreCase))
        return ("shutdown", null);

    // --step <name> → run
    for (int i = 0; i < rawArgs.Length - 1; i++)
    {
        if (rawArgs[i].Equals("--step", StringComparison.OrdinalIgnoreCase))
            return ("run", rawArgs[i + 1]);
    }

    // --help vagy ismeretlen
    if (rawArgs.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase)
                      || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        return ("help", null);

    return ("unknown", rawArgs[0]);
}

static int UnknownMode(string mode)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[SCAFFOLD ERROR] Ismeretlen parancs: '{mode}'");
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
          Scaffold.CLI --step <step_name>
          Scaffold.CLI shutdown

        Konfiguráció: Scaffold.CLI.yaml (az exe mellett)

          host_binary_path: ./bin/Scaffold.ServiceHost
          models:           ./models.yaml
          pipe_name:        MyProject
          output:           ./output
          project_context:  ./input.yaml
          steps:
            task_breakdown:
              input_config:     ./task_breakdown_agent.yaml
              validator_config: ./task_breakdown_validator.yaml
              model_alias:      qwen2.5-7b-instruct
            coding:
              input_config:     ./coding_agent.yaml
              model_alias:      qwen2.5-coder-7b-instruct

        Visszatérési kódok:
          0 – Accept vagy Edit
          1 – Hiba
          2 – Reject (újrafuttatás szükséges)

        """);
}