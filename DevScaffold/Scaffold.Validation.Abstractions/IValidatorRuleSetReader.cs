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

namespace Scaffold.Validation.Abstractions;

/// <summary>
/// Egy step-hez tartozó deklaratív validátor szabálykészlet betöltése.
///
/// Az implementáció felelős a fájl elérési út feloldásáért is –
/// a hívónak nem kell ismernie a fájlnév-konvenciót.
///
/// Ha nem létezik validator yaml a megadott step-hez, null-t ad vissza.
/// Ez nem hiba – a per-step validator ilyenkor beégetett alapértelmezett
/// szabályokkal fut.
/// </summary>
public interface IValidatorRuleSetReader
{
    /// <summary>
    /// Megkísérli betölteni a stephez tartozó validator szabálykészletet.
    /// </summary>
    /// <param name="stepConfigPath">A step agent config fájl teljes elérési útja.</param>
    /// <param name="stepId">A step azonosítója (pl. "task_breakdown").</param>
    /// <returns>A betöltött szabálykészlet, vagy null ha nem létezik validator yaml.</returns>
    ValidatorRuleSet? TryLoad(string stepConfigPath, string stepId);
}