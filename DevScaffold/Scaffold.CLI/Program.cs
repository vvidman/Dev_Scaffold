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
using Scaffold.Application.Artifacts;
using Scaffold.Application.Interfaces;
using Scaffold.Application.Output;
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
// Usage:
//   DevScaffold --step <step_name>
//
//   DevScaffold shutdown
//
// Configuration: Scaffold.CLI.yaml (next to the exe, always required)
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
// Return codes:
//   0 – Accept or Edit (successful step)
//   1 – Error (connection problem, parse error, unexpected exception)
//   2 – Reject (the human sent it back, regeneration required)
// ─────────────────────────────────────────────

var (mode, stepName, inputOverridePath, applyFolders, dryRun) = ParseArgs(Environment.GetCommandLineArgs()[1..]);

if (mode is "help")
{
    PrintHelp();
    return 0;
}

// ─────────────────────────────────────────────
// CLI configuration loading
// ─────────────────────────────────────────────

var configPath = GetCliConfigPath();

if (!File.Exists(configPath))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(
        $"[SCAFFOLD ERROR] Scaffold.CLI.yaml not found at {configPath}. " +
        "Copy Scaffold.CLI.example.yaml to Scaffold.CLI.yaml and adjust the paths.");
    Console.ResetColor();
    return 1;
}

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
// Graceful shutdown – on Ctrl+C
// ─────────────────────────────────────────────

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine();
    Console.WriteLine("[SCAFFOLD] Signalling cancellation...");
    cts.Cancel();
};

return mode switch
{
    "run"      => await RunAsync(cliConfig, stepName!, inputOverridePath, cts.Token),
    "apply"    => Apply(cliConfig, applyFolders!, dryRun, cts.Token),
    "shutdown" => await ShutdownAsync(cliConfig, cts.Token),
    _ => UnknownMode(mode)
};

// ─────────────────────────────────────────────
// run (--step <name>)
// ─────────────────────────────────────────────

async Task<int> RunAsync(
    CliProjectConfig config,
    string step,
    string? inputOverride,
    CancellationToken cancellationToken)
{
    // Step-level validation (file existence, required fields)
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
    // DI container
    // ─────────────────────────────────────────────

    var services = new ServiceCollection();
    services.AddSingleton<IScaffoldConsole, ConsoleScaffoldConsole>();
    services.AddSingleton<IStepAgentConfigReader, YamlStepAgentConfigReader>();
    services.AddSingleton<IInputAssembler, InputAssembler>();
    services.AddSingleton<IFileEditorLauncher, DefaultFileEditorLauncher>();
    services.AddSingleton<IHumanValidationService, ConsoleHumanValidationService>();
    // Validation layer
    // UniversalOutputValidator is deliberately NOT registered in DI –
    // it is an internal component, instantiated by CompositeOutputValidator.
    services.AddSingleton<IStepOutputValidator, TaskBreakdownValidator>();
    services.AddSingleton(sp => new StepValidatorRegistry(sp.GetServices<IStepOutputValidator>()));
    services.AddSingleton<IOutputValidator, CompositeOutputValidator>();
    services.AddSingleton<IValidatorRuleSetReader, ValidatorYamlReader>();
    services.AddScaffoldApplication();
    var provider = services.BuildServiceProvider();

    var scaffoldConsole = provider.GetRequiredService<IScaffoldConsole>();

    // ─────────────────────────────────────────────
    // Step identification – stepId is needed for the generation computation
    // ─────────────────────────────────────────────

    string stepId;
    try
    {
        var reader = provider.GetRequiredService<IStepAgentConfigReader>();
        stepId = reader.Load(stepConfigPath).Step;
    }
    catch (Exception ex)
    {
        scaffoldConsole.WriteError($"[SCAFFOLD ERROR] Step configuration load error: {ex.Message}");
        return 1;
    }

    // ─────────────────────────────────────────────
    // Generation computation + creating the step output folder
    // ─────────────────────────────────────────────

    int generation;
    string stepOutputFolder;
    try
    {
        generation = GenerationCalculator.ComputeNext(outputBasePath, stepId);
        stepOutputFolder = Path.Combine(outputBasePath, $"{stepId}_{generation}");
        Directory.CreateDirectory(stepOutputFolder);
        scaffoldConsole.WriteCli(
            $"[SCAFFOLD] Step output folder: {stepOutputFolder} (generation: {generation})");
    }
    catch (Exception ex)
    {
        scaffoldConsole.WriteError(
            $"[SCAFFOLD ERROR] Failed to create output folder: {ex.Message}");
        return 1;
    }

    // ─────────────────────────────────────────────
    // Audit logger – written into the step output folder
    // ─────────────────────────────────────────────

    var auditLogPath = Path.Combine(stepOutputFolder, "audit.log");
    await using var auditLogger = new FileAuditLogger(auditLogPath);

    scaffoldConsole.WriteCli($"[SCAFFOLD] Audit log: {auditLogPath}");

    // ─────────────────────────────────────────────
    // Starting the ServiceHost
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
        scaffoldConsole.WriteCli("[SCAFFOLD] Cancelled.");
        return 1;
    }

    // ─────────────────────────────────────────────
    // Running the step
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
            generation: generation,
            secondaryInputYamlPath: inputOverride);

        try
        {
            var decision = await session.RunAsync(cancellationToken);

            if (decision.Outcome is ValidationOutcome.Accept or ValidationOutcome.Edit)
            {
                var postProcessors = provider.GetServices<IStepPostProcessor>()
                    .Where(p => p.StepId.Equals(stepId, StringComparison.OrdinalIgnoreCase));

                // Loading StepAgentConfig to extract FilepathHintPrefix.
                // The config was already loaded inside ScaffoldStepOrchestrator too,
                // but Program.cs needs it to build the PostProcessorContext.
                // This is a cheap operation (file read); model loading lives in the ServiceHost.
                var agentConfigForContext = provider
                    .GetRequiredService<IStepAgentConfigReader>()
                    .Load(stepConfigPath);

                var postProcessorContext = new PostProcessorContext(
                    AcceptedFilePath:   decision.ValidatedOutputFilePath,
                    StepOutputFolder:   stepOutputFolder,
                    ProjectRootPath:    config.ProjectRoot ?? string.Empty,
                    StepId:             stepId,
                    Generation:         generation,
                    InputOverridePath:  inputOverride,
                    FilepathHintPrefix: agentConfigForContext.FilepathHintPrefix,
                    CancellationToken:  cancellationToken);

                foreach (var processor in postProcessors)
                {
                    try
                    {
                        await processor.ProcessAsync(postProcessorContext);
                    }
                    catch (Exception ex)
                    {
                        scaffoldConsole.WriteError(
                            $"[SCAFFOLD WARNING] Post-processing failed ({processor.StepId}): {ex.Message}");
                        auditLogger.Log(AuditEvent.Error,
                            $"reason=post_processor_failed step={processor.StepId} " +
                            $"message=\"{ex.Message.Replace("\"", "'")}\"");
                        // Not rethrown – a post-processing failure does not invalidate the acceptance
                    }
                }
            }

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
            scaffoldConsole.WriteCli("[SCAFFOLD] Run cancelled.");
            return 1;
        }
        catch (TimeoutException ex)
        {
            // Expected, handled state (liveness timeout) – the handler already wrote the audit log.
            scaffoldConsole.WriteError($"[SCAFFOLD ERROR] {ex.Message}");
            scaffoldConsole.WriteError("[SCAFFOLD] The ServiceHost may be stuck. Try 'DevScaffold shutdown' and run the step again.");
            return 1;
        }
        catch (Exception ex)
        {
            scaffoldConsole.WriteError($"[SCAFFOLD ERROR] Unexpected error: {ex.Message}");
            scaffoldConsole.WriteError(ex.StackTrace ?? string.Empty);
            auditLogger.Log(Scaffold.Application.AuditEvent.Error,
                $"reason=unexpected message=\"{ex.Message.Replace("\"", "'")}\"");
            return 1;
        }
    }
}

static int HandleReject(ValidationDecision decision, IScaffoldConsole console)
{
    console.WriteCli("[SCAFFOLD] Step sent back.");

    if (!string.IsNullOrWhiteSpace(decision.RejectionClarification))
        console.WriteCli("[SCAFFOLD] Clarification recorded in the audit log.");

    // Exit 2 tells the caller (e.g. a shell script) that a reject
    // happened, not an error – a rerun is required.
    return 2;
}

// ─────────────────────────────────────────────
// shutdown command
// ─────────────────────────────────────────────

async Task<int> ShutdownAsync(
    CliProjectConfig config,
    CancellationToken cancellationToken)
{
    var pipeName = config.PipeName;
    var console = new ConsoleScaffoldConsole();

    if (!File.Exists($@"\\.\pipe\{pipeName}-events"))
    {
        console.WriteCli("[SCAFFOLD] ServiceHost is not running.");
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
            console.WriteError($"[SCAFFOLD ERROR] Pipe connection failed: {ex.Message}");
            return 1;
        }

        var isReady = await pipeClient.WaitForReadyAsync(
            TimeSpan.FromSeconds(10),
            cancellationToken);

        if (!isReady)
        {
            console.WriteError("[SCAFFOLD ERROR] ServiceHost did not respond.");
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

        console.WriteCli("[SCAFFOLD] Shutdown sent. Waiting for confirmation...");

        await Task.WhenAny(
            shuttingDownTcs.Task,
            Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));

        console.WriteCli(shuttingDownTcs.Task.IsCompleted
            ? "[SCAFFOLD] ServiceHost stopped."
            : "[SCAFFOLD] Shutdown sent (no confirmation received).");

        return 0;
    }
}

// ─────────────────────────────────────────────
// apply (--apply <folder...> [--dry-run])
// ─────────────────────────────────────────────

static int Apply(
    CliProjectConfig config,
    string[] folderNames,
    bool dryRun,
    CancellationToken cancellationToken)
{
    var console = new ConsoleScaffoldConsole();

    if (string.IsNullOrWhiteSpace(config.ProjectRoot))
    {
        console.WriteError(
            "[SCAFFOLD ERROR] project_root is not configured in Scaffold.CLI.yaml. " +
            "It is required for the --apply command.");
        return 1;
    }

    if (!Directory.Exists(config.ProjectRoot))
    {
        console.WriteError($"[SCAFFOLD ERROR] project_root does not exist: {config.ProjectRoot}");
        return 1;
    }

    var projectFolder = SanitizeFolderName(config.PipeName);
    var outputBasePath = Path.Combine(config.Output, projectFolder);

    var result = ArtifactApplier.Apply(
        outputBasePath, folderNames, config.ProjectRoot, dryRun, cancellationToken);

    foreach (var folderName in result.SkippedFoldersWithoutArtifacts)
        console.WriteCli($"[SCAFFOLD APPLY] Skipped (no artifacts/): {folderName}");

    foreach (var entry in result.Entries)
    {
        switch (entry.Status)
        {
            case ArtifactApplyStatus.Copied:
                console.WriteCli($"[SCAFFOLD APPLY] {entry.RelativePath} → {entry.TargetPath}");
                break;
            case ArtifactApplyStatus.WouldCopy:
                console.WriteCli($"[DRY-RUN] {entry.RelativePath} → {entry.TargetPath}");
                break;
            case ArtifactApplyStatus.SkippedUnsafePath:
                console.WriteValidation(
                    $"[SCAFFOLD APPLY] Skipped – unsafe path outside project_root: \"{entry.RelativePath}\"");
                break;
        }
    }

    if (!dryRun)
        console.WriteCli($"[SCAFFOLD APPLY] Done. {result.CopiedCount} file(s) copied.");
    else
        console.WriteCli("[SCAFFOLD APPLY] Dry-run done. No writes occurred.");

    return 0;
}

// ─────────────────────────────────────────────
// Helper functions
// ─────────────────────────────────────────────

/// <summary>
/// The CLI yaml config's path – always "Scaffold.CLI.yaml" next to the
/// exe, regardless of the exe's (assembly's) name.
/// </summary>
static string GetCliConfigPath()
{
    const string CliConfigFileName = "Scaffold.CLI.yaml";
    return Path.Combine(AppContext.BaseDirectory, CliConfigFileName);
}

/// <summary>
/// Removes spaces from the folder name (output path sanitization).
/// </summary>
static string SanitizeFolderName(string name) =>
    name.Replace(" ", "_");

static (string mode, string? stepName, string? inputOverridePath, string[]? applyFolders, bool dryRun) ParseArgs(string[] rawArgs)
{
    if (rawArgs.Length == 0)
        return ("help", null, null, null, false);

    // shutdown command
    if (rawArgs[0].Equals("shutdown", StringComparison.OrdinalIgnoreCase))
        return ("shutdown", null, null, null, false);

    // --apply <folder...> [--dry-run]
    if (rawArgs[0].Equals("--apply", StringComparison.OrdinalIgnoreCase))
    {
        var folders = rawArgs[1..].Where(a => !a.StartsWith("--")).ToArray();
        var isDryRun = rawArgs.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        return ("apply", null, null, folders, isDryRun);
    }

    string? stepName = null;
    string? inputOverridePath = null;

    for (int i = 0; i < rawArgs.Length - 1; i++)
    {
        if (rawArgs[i].Equals("--step", StringComparison.OrdinalIgnoreCase))
            stepName = rawArgs[i + 1];
        if (rawArgs[i].Equals("--input", StringComparison.OrdinalIgnoreCase))
            inputOverridePath = rawArgs[i + 1];
    }

    if (stepName is not null)
        return ("run", stepName, inputOverridePath, null, false);

    // --help or unknown
    if (rawArgs.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase)
                      || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        return ("help", null, null, null, false);

    return ("unknown", rawArgs[0], null, null, false);
}

static int UnknownMode(string mode)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[SCAFFOLD ERROR] Unknown command: '{mode}'");
    Console.ResetColor();
    Console.WriteLine();
    PrintHelp();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""

        DevScaffold – Scaffold Protocol CLI

        Usage:
          DevScaffold --step <step_name>
          DevScaffold shutdown

        Configuration: Scaffold.CLI.yaml (next to the exe)

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

        Return codes:
          0 – Accept or Edit
          1 – Error
          2 – Reject (a rerun is required)

        """);
}