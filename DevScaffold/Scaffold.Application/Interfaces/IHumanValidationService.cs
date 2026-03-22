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

using Scaffold.Domain.Models;

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Human validációs interakció absztrakciója.
/// CLI kontextusban konzolon keresztül valósul meg.
/// </summary>
public interface IHumanValidationService
{
    /// <summary>
    /// Megjeleníti a step kimenetét, majd bekéri a human döntést.
    /// Accept / Edit / Reject.
    /// </summary>
    /// <param name="stepId">A step azonosítója (pl. task_breakdown)</param>
    /// <param name="outputFilePath">A generált kimenet fájl elérési útja</param>
    /// <returns>A validáció kimenetele és opcionális pontosítás Reject esetén</returns>
    Task<ValidationDecision> ValidateAsync(string stepId, string outputFilePath);
}

/// <summary>
/// Human validáció eredménye.
/// </summary>
public record ValidationDecision(
    ValidationOutcome Outcome,
    string ValidatedOutputFilePath,
    string? RejectionClarification = null);
