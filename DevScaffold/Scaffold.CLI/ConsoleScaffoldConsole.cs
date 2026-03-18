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

namespace Scaffold.CLI;

/// <summary>
/// Konzol alapú kimenet implementáció színkódolással.
///
/// Szint → szín mapping:
///   CLI        → Cyan   (infrastruktúra, pipe, ServiceHost indítás)
///   Session    → Gray   (inference progress, ServiceHost események)
///   Validation → Yellow (human validációs interakció)
///   Error      → Red    (hibák, stderr)
/// </summary>
public sealed class ConsoleScaffoldConsole : IScaffoldConsole
{
    public void WriteCli(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteSession(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteValidation(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }
}