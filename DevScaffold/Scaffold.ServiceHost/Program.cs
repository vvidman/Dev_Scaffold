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

using Scaffold.Domain.Models;
using Scaffold.Infrastructure.StepConfig;
using Scaffold.ServiceHost;

// ─────────────────────────────────────────────
// Scaffold ServiceHost
//
// Használat (a CLI indítja, nem kézzel):
//   Scaffold.ServiceHost --models <models.yaml>
//                        --output <output mappa>
//                        --pipe-name <pipe név>
//
// Minden paramétert a CLI ad át indításkor.
// ─────────────────────────────────────────────

var inputArgs = ParseArgs(Environment.GetCommandLineArgs()[1..]);

if (!ValidateRequiredArgs(inputArgs))
    return 1;

var modelsYamlPath = inputArgs["--models"];
var outputBasePath = inputArgs["--output"];
var pipeName = inputArgs["--pipe-name"];
var version = "1.0.0";

Console.WriteLine($"[ServiceHost] Indítás...");
Console.WriteLine($"[ServiceHost] Pipe neve: {pipeName}");
Console.WriteLine($"[ServiceHost] Models: {modelsYamlPath}");
Console.WriteLine($"[ServiceHost] Output: {outputBasePath}");
Console.WriteLine();

// Graceful shutdown – Ctrl+C vagy SIGTERM esetén
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("[ServiceHost] Leállítás jelzése...");
    cts.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

// ─────────────────────────────────────────────
// Komponensek összerakása
// ─────────────────────────────────────────────

// Models registry betöltése
ModelRegistryConfig registry;
try
{
    var registryReader = new YamlModelRegistryReader();
    registry = registryReader.Load(modelsYamlPath);
    Console.WriteLine($"[ServiceHost] Modell registry betöltve. " +
                      $"Elérhető aliasok: {string.Join(", ", registry.Models.Keys)}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ServiceHost ERROR] Models registry betöltési hiba: {ex.Message}");
    return 1;
}

// EventPublisher – event pipe írás
await using var eventPublisher = new EventPublisher(pipeName);

// ModelCache – lazy modell betöltés
await using var modelCache = new ModelCache(registry);

// ModelCache eseményeket bekötjük az EventPublisher-be
modelCache.ModelStatusChanged += async (alias, status, message) =>
{
    await eventPublisher.PublishModelStatusChangedAsync(
        requestId: string.Empty,  // lifecycle eseménynél nincs request kontextus
        modelAlias: alias,
        status: status,
        message: message,
        ct: cts.Token);
};

// InferenceWorker – inference futtatás
var inferenceWorker = new InferenceWorker(
    modelCache,
    eventPublisher,
    outputBasePath);

// CommandDispatcher – parancs routing
var dispatcher = new CommandDispatcher(
    inferenceWorker,
    modelCache,
    eventPublisher);

// PipeServer – pipe lifecycle
await using var pipeServer = new PipeServer(
    pipeName,
    dispatcher,
    eventPublisher,
    version);

// ─────────────────────────────────────────────
// Indítás
// ─────────────────────────────────────────────

try
{
    await pipeServer.RunAsync(cts.Token);
    Console.WriteLine("[ServiceHost] Normál leállás.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("[ServiceHost] Megszakítva.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ServiceHost ERROR] Váratlan hiba: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
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
        if (args[i].StartsWith("--") && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
            result[args[i]] = args[++i];
    }

    return result;
}

static bool ValidateRequiredArgs(Dictionary<string, string> args)
{
    var required = new[] { "--models", "--output", "--pipe-name" };
    var missing = required.Where(r => !args.ContainsKey(r)).ToList();

    if (missing.Count == 0) return true;

    Console.Error.WriteLine(
        $"[ServiceHost ERROR] Hiányzó argumentumok: {string.Join(", ", missing)}");
    return false;
}