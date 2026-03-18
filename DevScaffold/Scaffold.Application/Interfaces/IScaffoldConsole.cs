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
/// Konzol kimenet absztrakciója – szint szerinti színkódolással.
///
/// Három szint:
///   CLI        – Cyan   – infrastruktúra üzenetek (pipe, ServiceHost, indítás)
///   Session    – Gray   – inference progress, ServiceHost események
///   Validation – Yellow – human validációs interakció
///   Error      – Red    – hibák (stderr-re kerül)
///
/// Az absztrakció tesztelhetővé teszi a konzol kimenetet,
/// és egységes prefixelést biztosít minden kimeneti ponton.
/// </summary>
public interface IScaffoldConsole
{
    /// <summary>CLI szintű üzenet – infrastruktúra, indítás, kapcsolat. (Cyan)</summary>
    void WriteCli(string message);

    /// <summary>Session szintű üzenet – inference progress, ServiceHost eventi. (Gray)</summary>
    void WriteSession(string message);

    /// <summary>Validációs üzenet – human interakció, döntés bekérés. (Yellow)</summary>
    void WriteValidation(string message);

    /// <summary>Hiba üzenet – stderr-re kerül. (Red)</summary>
    void WriteError(string message);
}