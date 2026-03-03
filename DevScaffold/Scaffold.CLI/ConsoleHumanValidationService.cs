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
/// </summary>
public class ConsoleHumanValidationService : IHumanValidationService
{
    public async Task<ValidationDecision> ValidateAsync(
        string stepId,
        string outputFilePath)
    {
        Console.WriteLine("─────────────────────────────────────────────────");
        Console.WriteLine($"[SCAFFOLD] Validáció szükséges: {stepId}");
        Console.WriteLine($"[SCAFFOLD] Kimenet fájl: {outputFilePath}");
        Console.WriteLine();

        // Fájl megnyitása szerkesztőben
        Console.WriteLine("[SCAFFOLD] Megnyitom a kimenetet a szerkesztőben...");
        OpenInEditor(outputFilePath);

        Console.WriteLine();
        Console.WriteLine("Döntés:");
        Console.WriteLine("  [1] Accept  – Elfogadom, következő lépés");
        Console.WriteLine("  [2] Edit    – Szerkesztettem, elfogadom a módosított verziót");
        Console.WriteLine("  [3] Reject  – Visszaküldöm, pontosítással újragenerálás");
        Console.WriteLine();
        Console.Write("Választás (1/2/3): ");

        while (true)
        {
            var key = Console.ReadLine()?.Trim();

            switch (key)
            {
                case "1":
                    Console.WriteLine();
                    return new ValidationDecision(ValidationOutcome.Accept);

                case "2":
                    Console.WriteLine();
                    return new ValidationDecision(ValidationOutcome.Edit);

                case "3":
                    Console.WriteLine();
                    Console.WriteLine("Pontosítás (mit kell másképp csinálni?):");
                    Console.Write("> ");
                    var clarification = Console.ReadLine() ?? string.Empty;
                    Console.WriteLine();
                    return new ValidationDecision(
                        ValidationOutcome.Reject,
                        clarification);

                default:
                    Console.Write("Érvénytelen választás. Kérlek 1, 2 vagy 3: ");
                    break;
            }
        }
    }

    /// <summary>
    /// Megnyitja a fájlt az operációs rendszer alapértelmezett szövegszerkesztőjében.
    /// </summary>
    private static void OpenInEditor(string filePath)
    {
        try
        {
            var process = new System.Diagnostics.ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(process);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SCAFFOLD] Figyelmeztetés: nem sikerült megnyitni a szerkesztőt: {ex.Message}");
            Console.WriteLine($"[SCAFFOLD] Nyisd meg manuálisan: {filePath}");
        }
    }
}
