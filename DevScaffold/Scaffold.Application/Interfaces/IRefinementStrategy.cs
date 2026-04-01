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

using Scaffold.Validation.Abstractions;

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Egy sikertelen kísérlet utáni refinement logika absztrakciója.
///
/// Két triggert kezel egységesen:
/// - Automatikus reject: az IOutputValidator talált szabálysértést
/// - Human reject: a human reviewer visszaküldte pontosítással
///
/// A ScaffoldStepOrchestrator ezzel az interfésszel dönt arról,
/// hogy mit adjon át a következő kísérletnek system prompt kiegészítésként.
/// </summary>
public interface IRefinementStrategy
{
    /// <summary>
    /// Automatikus validációs hiba alapján felépíti a refinement clarification-t.
    /// Az eredmény tartalmazza a szabálysértések fix-hint-jeit,
    /// amelyeket a modell a következő kísérletben felhasználhat.
    /// </summary>
    string BuildAutoRejectionClarification(OutputValidationResult validationResult);

    /// <summary>
    /// Az eredeti system promptot kiegészíti a refinement kontextussal.
    /// A clarification forrása lehet automatikus validator vagy human reviewer.
    /// </summary>
    string BuildRefinedSystemPrompt(IAuditLogger logger, string originalSystemPrompt, string clarification);
}