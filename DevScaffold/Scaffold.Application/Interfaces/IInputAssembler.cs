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
/// Input fájlok összeszereléséért felelős absztrakció.
/// Beolvassa a YAML input sémát, feloldja a path referenciákat,
/// és összeállítja az AI-nak átadható prompt kontextust.
///
/// Fail fast: ha bármely path referencia nem létezik vagy nem olvasható,
/// kivételt dob – részleges kontextussal az AI nem indulhat el.
/// </summary>
public interface IInputAssembler
{
    /// <summary>
    /// Beolvassa az input yaml fájlt, feloldja az összes path referenciát,
    /// és visszaad egy teljes, AI-nak átadható kontextus stringet.
    /// </summary>
    /// <exception cref="ScaffoldInputValidationException">
    /// Ha bármely path referencia nem található.
    /// </exception>
    string Assemble(string inputYamlPath, string stepId);
}
