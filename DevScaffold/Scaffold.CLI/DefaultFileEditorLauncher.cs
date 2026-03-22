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
using System.Diagnostics;

namespace Scaffold.CLI;

/// <summary>
/// Az operációs rendszer alapértelmezett szövegszerkesztőjét használja.
///
/// UseShellExecute = true: az OS dönti el melyik alkalmazás nyitja meg
/// a fájlt a kiterjesztés alapján – Windows, macOS és Linux alatt egyaránt működik.
/// Nem blokkoló: az editor folyamat aszinkron indul, a CLI várakozás nélkül folytatódik.
/// </summary>
public sealed class DefaultFileEditorLauncher : IFileEditorLauncher
{
    /// <inheritdoc />
    public bool TryOpen(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath)
            {
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}