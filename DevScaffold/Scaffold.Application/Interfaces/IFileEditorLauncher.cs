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

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Fájl megnyitása szövegszerkesztőben.
/// Az implementáció platform-specifikus – a hívó nem tudja
/// hogy notepad, vim, code, vagy bármi más nyílik meg.
/// </summary>
public interface IFileEditorLauncher
{
    /// <summary>
    /// Megnyitja a fájlt az operációs rendszer alapértelmezett szövegszerkesztőjében.
    /// Ha az editor nem indítható el, nem dob kivételt – a hívó feladata
    /// a fallback megjelenítése.
    /// </summary>
    /// <returns>true ha az editor sikeresen elindult, false ha nem.</returns>
    bool TryOpen(string filePath);
}