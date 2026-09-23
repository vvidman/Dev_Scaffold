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
using Scaffold.Infrastructure.ConfigHandler;
using Scaffold.ServiceHost;
using Scaffold.ServiceHost.Abstractions;

// ─────────────────────────────────────────────
// Scaffold ServiceHost
//
// Usage (started by the CLI, not by hand):
//   Scaffold.ServiceHost --models    <models.yaml>
//                        --output    <output folder>
//                        --pipe-name <pipe name>
//
// The CLI passes every parameter at startup.
// ─────────────────────────────────────────────

var inputArgs = ParseArgs(Environment.GetCommandLineArgs()[1..]);

if (!ValidateRequiredArgs(inputArgs))
    return 1;

var modelsYamlPath = inputArgs["--models"];
var outputBasePath = inputArgs["--output"];
var pipeName = inputArgs["--pipe-name"];
var version = "1.0.0";

Console.WriteLine("[ServiceHost] Starting...");
Console.WriteLine($"[ServiceHost] Pipe name: {pipeName}");
Console.WriteLine($"[ServiceHost] Models:    {modelsYamlPath}");
Console.WriteLine($"[ServiceHost] Output:    {outputBasePath}");
Console.WriteLine();

// Graceful shutdown – on Ctrl+C or SIGTERM
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("[ServiceHost] Shutdown signalled...");
    cts.Cancel();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

// ─────────────────────────────────────────────
// Assembling the components
// ─────────────────────────────────────────────

ModelRegistryConfig registry;
try
{
    var registryReader = new YamlModelRegistryReader();
    registry = registryReader.Load(modelsYamlPath);
    Console.WriteLine(
        $"[ServiceHost] Model registry loaded. " +
        $"Available aliases: {string.Join(", ", registry.Models.Keys)}");
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"[ServiceHost ERROR] Models registry load error: {ex.Message}");
    return 1;
}

// EventPublisher – writes to the event pipe
await using var eventPublisher = new EventPublisher(pipeName);

// InferenceBackendFactory – backend instantiation (LLamaSharp vs. API decision)
using var backendFactory = new DefaultInferenceBackendFactory();

// ModelCache – lazy backend loading
await using var modelCache = new ModelCache(registry, backendFactory);

// Wire the ModelCache events into the EventPublisher
modelCache.ModelStatusChanged += async (alias, status, message) =>
{
    await eventPublisher.PublishModelStatusChangedAsync(
        requestId: string.Empty,
        modelAlias: alias,
        status: status,
        message: message,
        ct: cts.Token);
};

// InferenceWorker – runs inference backend-agnostically
var inferenceWorker = new InferenceWorker(
    modelCache,
    eventPublisher,
    outputBasePath);

// CommandDispatcher – command routing
var dispatcher = new CommandDispatcher(
    inferenceWorker,
    modelCache,
    eventPublisher);

// PipeServer – pipe lifecycle + session loop
await using var pipeServer = new PipeServer(
    pipeName,
    dispatcher,
    eventPublisher,
    eventPublisher,
    version);

// ─────────────────────────────────────────────
// Startup
// ─────────────────────────────────────────────

try
{
    await pipeServer.RunAsync(cts.Token);
    Console.WriteLine("[ServiceHost] Normal shutdown.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("[ServiceHost] Cancelled.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ServiceHost ERROR] Unexpected error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

// ─────────────────────────────────────────────
// Helper functions
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
        $"[ServiceHost ERROR] Missing arguments: {string.Join(", ", missing)}");
    return false;
}