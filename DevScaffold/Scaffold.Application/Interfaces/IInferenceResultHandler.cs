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

using Scaffold.Agent.Protocol;
using Scaffold.Domain.Models;
using Scaffold.Validation.Abstractions;

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Egy inference kísérlet eredményének feldolgozása.
///
/// Feliratkozik a pipe eseményeire, megvárja a befejezést,
/// elvégzi az automatikus validációt, majd szükség esetén
/// meghívja a human validációs szolgáltatást.
///
/// Visszaad egy ValidationDecision-t, amelyből a
/// ScaffoldStepOrchestrator eldönti hogy elfogad, szerkeszt, vagy újrafuttat.
/// </summary>
public interface IInferenceResultHandler
{
    /// <summary>
    /// Megvárja az inference eredményét és feldolgozza azt.
    /// </summary>
    /// <param name="request">Az elküldött inference kérés – request ID alapján szűri az eseményeket.</param>
    /// <param name="agentConfig">A step konfigurációja – max_tokens ellenőrzéshez szükséges.</param>
    /// <param name="ruleSet">Opcionális deklaratív validátor szabálykészlet.</param>
    /// <param name="cancellationToken">Megszakítás token.</param>
    /// <returns>A validáció eredménye és a kimenet fájl elérési útja.</returns>
    Task<ValidationDecision> HandleAsync(
        IPipeClient pipeClient,
        IAuditLogger auditLogger,
        InferRequest request,
        StepAgentConfig agentConfig,
        ValidatorRuleSet? ruleSet,
        CancellationToken cancellationToken = default);
}