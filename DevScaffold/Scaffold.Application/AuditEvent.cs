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

namespace Scaffold.Application;

/// <summary>
/// Audit log esemény típusok.
/// Minden típus egy fix szélességű tag-ként jelenik meg a log sorban –
/// ez teszi lehetővé a custom parser írását statisztikához.
/// </summary>
public enum AuditEvent
{
    /// <summary>A CLI session elindult – step és generáció azonosítóval.</summary>
    SessionStart,

    /// <summary>Az agent konfiguráció betöltve – modell, prompt hossz, token limit.</summary>
    Config,

    /// <summary>Az inference kérés elküldve a ServiceHost-nak.</summary>
    InferenceStart,

    /// <summary>Az inference sikeresen befejeződött – tokenek, sebesség, elapsed.</summary>
    InferenceDone,

    /// <summary>Az output fájl elérési útja rögzítve.</summary>
    Output,

    /// <summary>A human validáció döntése rögzítve – Accept/Edit/Reject + pontosítás.</summary>
    Validation,

    /// <summary>A session lezárult – teljes elapsed és végső kimenetel.</summary>
    SessionEnd,

    /// <summary>Hiba esemény – infrastruktúra vagy inference szintű hiba.</summary>
    Error,

    /// <summary> Refinement event - Refinement prompt was created. </summary>
    Refinement,
}