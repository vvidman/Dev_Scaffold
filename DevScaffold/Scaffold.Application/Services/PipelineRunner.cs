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

using Scaffold.Application.Interfaces;
using Scaffold.Domain.Models;

namespace Scaffold.Application.Services;

/// <summary>
/// A Scaffold Protocol pipeline orchestrátora.
/// Betölti a pipeline konfigurációt, inicializálja a modelleket,
/// majd lépésenként futtatja az agenteket human validációval.
/// </summary>
public class PipelineRunner
{
    private readonly IPipelineConfigReader _pipelineConfigReader;
    private readonly IModelRegistryReader _modelRegistryReader;
    private readonly IStepAgentConfigReader _stepAgentConfigReader;
    private readonly IInferenceEngineFactory _engineFactory;
    private readonly IInputAssembler _inputAssembler;
    private readonly IHumanValidationService _humanValidation;

    public PipelineRunner(
        IPipelineConfigReader pipelineConfigReader,
        IModelRegistryReader modelRegistryReader,
        IStepAgentConfigReader stepAgentConfigReader,
        IInferenceEngineFactory engineFactory,
        IInputAssembler inputAssembler,
        IHumanValidationService humanValidation)
    {
        _pipelineConfigReader = pipelineConfigReader;
        _modelRegistryReader = modelRegistryReader;
        _stepAgentConfigReader = stepAgentConfigReader;
        _engineFactory = engineFactory;
        _inputAssembler = inputAssembler;
        _humanValidation = humanValidation;
    }

    /// <summary>
    /// Elindítja a pipeline futást a megadott pipeline.yaml és models.yaml alapján.
    /// </summary>
    /// <param name="pipelineYamlPath">A pipeline.yaml elérési útja</param>
    /// <param name="modelsYamlPath">A models.yaml elérési útja</param>
    /// <param name="inputYamlPath">Az első lépés input yaml-jának elérési útja</param>
    /// <param name="outputBasePath">A kimenetek alap mappája</param>
    public async Task RunAsync(
        string pipelineYamlPath,
        string modelsYamlPath,
        string inputYamlPath,
        string outputBasePath,
        CancellationToken cancellationToken = default)
    {
        var pipeline = _pipelineConfigReader.Load(pipelineYamlPath);
        var registry = _modelRegistryReader.Load(modelsYamlPath);

        Console.WriteLine($"[SCAFFOLD] Pipeline betöltve: {pipeline.Name} v{pipeline.Version}");
        Console.WriteLine($"[SCAFFOLD] Lépések száma: {pipeline.Steps.Count}");
        Console.WriteLine();

        // Modellek előzetes betöltése – lifecycle egyértelmű
        Console.WriteLine("[SCAFFOLD] Modellek előzetes betöltése...");
        var distinctAliases = pipeline.Steps.Select(s => s.Model).Distinct().ToList();
        var loadedEngines = new Dictionary<string, IInferenceEngine>();

        foreach (var alias in distinctAliases)
        {
            var modelConfig = registry.Resolve(alias);
            Console.WriteLine($"  → {alias}: {modelConfig.Path}");
            loadedEngines[alias] = _engineFactory.Create(modelConfig);
        }

        Console.WriteLine("[SCAFFOLD] Modellek betöltve.");
        Console.WriteLine();

        try
        {
            string currentInputPath = inputYamlPath;

            foreach (var stepRef in pipeline.Steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await RunStepAsync(
                    stepRef,
                    loadedEngines[stepRef.Model],
                    currentInputPath,
                    outputBasePath,
                    cancellationToken);
            }
        }
        finally
        {
            // Modellek felszabadítása – minden esetben
            foreach (var engine in loadedEngines.Values)
                await engine.DisposeAsync();
        }

        Console.WriteLine("[SCAFFOLD] Pipeline sikeresen befejezve.");
    }

    private async Task RunStepAsync(
        PipelineStepRef stepRef,
        IInferenceEngine engine,
        string inputPath,
        string outputBasePath,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[SCAFFOLD] ── Lépés: {stepRef.Id} ──────────────────────────");

        var agentConfig = _stepAgentConfigReader.Load(stepRef.Config);

        // Input összeszereülése – fail fast ha path nem létezik
        var assembledInput = _inputAssembler.Assemble(inputPath);

        var outputFilePath = Path.Combine(outputBasePath, $"{stepRef.Id}_output.md");
        Directory.CreateDirectory(outputBasePath);

        bool accepted = false;
        string? rejectionContext = null;

        while (!accepted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Prompt összeállítása Reject esetén kiegészítve
            var userInput = rejectionContext is null
                ? assembledInput
                : BuildRejectionPrompt(assembledInput, outputFilePath, rejectionContext);

            Console.WriteLine($"[SCAFFOLD] Generálás folyamatban...");
            Console.WriteLine();

            // Streaming inference – kimenet konzolra és fájlba
            var fullOutput = await StreamAndSaveAsync(
                engine,
                agentConfig.SystemPrompt,
                userInput,
                outputFilePath,
                cancellationToken);

            Console.WriteLine();
            Console.WriteLine($"[SCAFFOLD] Kimenet mentve: {outputFilePath}");
            Console.WriteLine();

            // Human validáció
            var decision = await _humanValidation.ValidateAsync(stepRef.Id, outputFilePath);

            switch (decision.Outcome)
            {
                case ValidationOutcome.Accept:
                    Console.WriteLine("[SCAFFOLD] ✓ Elfogadva. Következő lépés...");
                    Console.WriteLine();
                    accepted = true;
                    break;

                case ValidationOutcome.Edit:
                    Console.WriteLine("[SCAFFOLD] ✎ Szerkesztett kimenet elfogadva. Következő lépés...");
                    Console.WriteLine();
                    accepted = true;
                    break;

                case ValidationOutcome.Reject:
                    Console.WriteLine("[SCAFFOLD] ✗ Visszaküldve. Újra generálás...");
                    Console.WriteLine();
                    rejectionContext = decision.RejectionClarification;
                    break;
            }
        }
    }

    private static string BuildRejectionPrompt(
        string originalInput,
        string rejectedOutputPath,
        string? clarification)
    {
        var rejectedContent = File.Exists(rejectedOutputPath)
            ? File.ReadAllText(rejectedOutputPath)
            : "(kimenet nem olvasható)";

        return $"""
            ## Eredeti input
            {originalInput}

            ## Előző generálás (elutasítva)
            {rejectedContent}

            ## Pontosítás
            {clarification ?? "Kérlek próbáld újra, figyelj a minőségre."}
            """;
    }

    private static async Task<string> StreamAndSaveAsync(
        IInferenceEngine engine,
        string systemPrompt,
        string userInput,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var builder = new System.Text.StringBuilder();

        await using var writer = new StreamWriter(outputPath, append: false);

        await foreach (var token in engine.InferAsync(systemPrompt, userInput, cancellationToken))
        {
            Console.Write(token);
            builder.Append(token);
            await writer.WriteAsync(token);
            await writer.FlushAsync(cancellationToken);
        }

        return builder.ToString();
    }
}
