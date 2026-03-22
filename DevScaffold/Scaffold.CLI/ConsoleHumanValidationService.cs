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
/// Konzolos human validációs service implementáció.
/// Megjeleníti a step kimenetét, bekéri a döntést, és
/// Reject esetén a pontosítást is bekéri.
///
/// Konzol kimenet Yellow színnel az IScaffoldConsole-on keresztül –
/// elkülönítve a CLI (Cyan) és Session (Gray) szintű üzenetektől.
/// Az audit logolás a ScaffoldStepOrchestrator felelőssége –
/// a döntés ott kerül rögzítésre.
///
/// A fájl megnyitása az IFileEditorLauncher-re van delegálva –
/// a konkrét editor platform-specifikus részlet, nem fér ide.
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
        _console.WriteValidation($"[VALIDATE] Validáció szükséges: {stepId}");
        _console.WriteValidation($"[VALIDATE] Kimenet fájl: {outputFilePath}");
        _console.WriteValidation(string.Empty);

        _console.WriteValidation("[VALIDATE] Megnyitom a kimenetet a szerkesztőben...");

        if (!_editorLauncher.TryOpen(outputFilePath))
        {
            _console.WriteValidation(
                "[VALIDATE] Figyelmeztetés: nem sikerült megnyitni a szerkesztőt.");
            _console.WriteValidation(
                $"[VALIDATE] Nyisd meg manuálisan: {outputFilePath}");
        }

        _console.WriteValidation(string.Empty);
        _console.WriteValidation("Döntés:");
        _console.WriteValidation("  [1] Accept  – Elfogadom, következő lépés");
        _console.WriteValidation("  [2] Edit    – Szerkesztettem, elfogadom a módosított verziót");
        _console.WriteValidation("  [3] Reject  – Visszaküldöm, pontosítással újragenerálás");
        _console.WriteValidation(string.Empty);

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("Választás (1/2/3): ");
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
                    _console.WriteValidation("Pontosítás (mit kell másképp csinálni?):");

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
                    Console.Write("Érvénytelen választás. Kérlek 1, 2 vagy 3: ");
                    Console.ResetColor();
                    break;
            }
        }
    }
}