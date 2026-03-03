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
using Scaffold.Application.Interfaces;
using Scaffold.Application.Services;
using Scaffold.CLI;
using Scaffold.Infrastructure.Inference;
using Scaffold.Infrastructure.StepConfig;

// ─────────────────────────────────────────────
// DevScaffold CLI
// Használat:
//   DevScaffold --pipeline <pipeline.yaml>
//               --models <models.yaml>
//               --input <input.yaml>
//               --output <output mappa>
//
// Opcionális:
//   --api-key <kulcs>   (API alapú engine esetén)
// ─────────────────────────────────────────────

var inputArgs = ParseArgs(args: Environment.GetCommandLineArgs()[1..]);

if (inputArgs.ContainsKey("--help") || inputArgs.Count == 0)
{
    PrintHelp();
    return 0;
}

if (!ValidateRequiredArgs(inputArgs))
    return 1;

// DI konténer összerakása
var services = new ServiceCollection();

services.AddSingleton<IPipelineConfigReader, YamlPipelineConfigReader>();
services.AddSingleton<IModelRegistryReader, YamlModelRegistryReader>();
services.AddSingleton<IStepAgentConfigReader, YamlStepAgentConfigReader>();
services.AddSingleton<IInputAssembler, InputAssembler>();
services.AddSingleton<IHumanValidationService, ConsoleHumanValidationService>();

services.AddSingleton<IInferenceEngineFactory>(_ =>
    new InferenceEngineFactory(
        apiKey: inputArgs.GetValueOrDefault("--api-key")));

services.AddSingleton<PipelineRunner>();

var provider = services.BuildServiceProvider();
var runner = provider.GetRequiredService<PipelineRunner>();

// Pipeline futtatása
try
{
    await runner.RunAsync(
        pipelineYamlPath: inputArgs["--pipeline"],
        modelsYamlPath: inputArgs["--models"],
        inputYamlPath: inputArgs["--input"],
        outputBasePath: inputArgs.GetValueOrDefault("--output") ?? "./output");

    return 0;
}
catch (FileNotFoundException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[SCAFFOLD ERROR] Fájl nem található: {ex.Message}");
    Console.ResetColor();
    return 1;
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
    Console.WriteLine();
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

// ─────────────────────────────────────────────
// Segédfüggvények
// ─────────────────────────────────────────────

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--"))
        {
            var value = (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                ? args[++i]
                : "true";
            result[args[i]] = value;
        }
    }

    return result;
}

static bool ValidateRequiredArgs(Dictionary<string, string> args)
{
    var required = new[] { "--pipeline", "--models", "--input" };
    var missing = required.Where(r => !args.ContainsKey(r)).ToList();

    if (missing.Count == 0) return true;

    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"[SCAFFOLD ERROR] Hiányzó argumentumok: {string.Join(", ", missing)}");
    Console.ResetColor();
    Console.WriteLine();
    PrintHelp();
    return false;
}

static void PrintHelp()
{
    Console.WriteLine("""
        DevScaffold – Scaffold Protocol CLI

        Használat:
          DevScaffold --pipeline <pipeline.yaml>
                      --models   <models.yaml>
                      --input    <input.yaml>
                     [--output   <output mappa>]
                     [--api-key  <kulcs>]

        Példa:
          DevScaffold --pipeline ./scaffold/pipeline.yaml
                      --models   ./scaffold/models.yaml
                      --input    ./scaffold/inputs/01_task_definition_input.yaml
                      --output   ./scaffold/output
        """);
}
