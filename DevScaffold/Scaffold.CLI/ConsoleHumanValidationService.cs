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

namespace Scaffold.CLI;

/// <summary>
/// Console-based human validation service implementation.
/// Displays the step's output, requests the decision, and on
/// Reject also requests a clarification.
///
/// Console output uses Yellow via IScaffoldConsole – kept distinct
/// from CLI-level (Cyan) and Session-level (Gray) messages.
/// Audit logging is ScaffoldStepOrchestrator's responsibility –
/// the decision is recorded there.
///
/// Opening the file is delegated to IFileEditorLauncher – the concrete
/// editor is a platform-specific detail that does not belong here.
/// </summary>
public sealed class ConsoleHumanValidationService : IHumanValidationService
{
    private readonly IScaffoldConsole _console;
    private readonly IFileEditorLauncher _editorLauncher;

    public ConsoleHumanValidationService(
        IScaffoldConsole console,
        IFileEditorLauncher editorLauncher)
    {
        _console = console;
        _editorLauncher = editorLauncher;
    }

    /// <inheritdoc />
    public async Task<ValidationDecision> ValidateAsync(
        string stepId,
        string outputFilePath)
    {
        _console.WriteValidation("─────────────────────────────────────────────────");
        _console.WriteValidation($"[VALIDATE] Validation required: {stepId}");
        _console.WriteValidation($"[VALIDATE] Output file: {outputFilePath}");
        _console.WriteValidation(string.Empty);

        _console.WriteValidation("[VALIDATE] Opening the output in the editor...");

        if (!_editorLauncher.TryOpen(outputFilePath))
        {
            _console.WriteValidation(
                "[VALIDATE] Warning: could not open the editor.");
            _console.WriteValidation(
                $"[VALIDATE] Open it manually: {outputFilePath}");
        }

        _console.WriteValidation(string.Empty);
        _console.WriteValidation("Decision:");
        _console.WriteValidation("  [1] Accept  – I accept it, proceed to the next step");
        _console.WriteValidation("  [2] Edit    – I edited it, accept the modified version");
        _console.WriteValidation("  [3] Reject  – Send it back, regenerate with a clarification");
        _console.WriteValidation(string.Empty);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Choice (1/2/3): ");
        Console.ResetColor();

        while (true)
        {
            var key = (await Console.In.ReadLineAsync())?.Trim();

            switch (key)
            {
                case "1":
                    _console.WriteValidation(string.Empty);
                    return new ValidationDecision(ValidationOutcome.Accept, outputFilePath);

                case "2":
                    _console.WriteValidation(string.Empty);
                    return new ValidationDecision(ValidationOutcome.Edit, outputFilePath);

                case "3":
                    _console.WriteValidation(string.Empty);
                    _console.WriteValidation("Clarification (what should be done differently?):");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("> ");
                    Console.ResetColor();

                    var clarification = await Console.In.ReadLineAsync() ?? string.Empty;
                    _console.WriteValidation(string.Empty);
                    return new ValidationDecision(
                        ValidationOutcome.Reject,
                        outputFilePath,
                        clarification);

                default:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("Invalid choice. Please enter 1, 2, or 3: ");
                    Console.ResetColor();
                    break;
            }
        }
    }
}