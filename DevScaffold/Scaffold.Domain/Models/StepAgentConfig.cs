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

namespace Scaffold.Domain.Models;

/// <summary>
/// Egy AI agent step konfigurációja.
/// A pipeline steps[].config mezője által hivatkozott yaml fájl tartalma.
/// Meghatározza az agent rendszer promptját és az elvárt kimenet formátumát.
/// </summary>
public class StepAgentConfig
{
    public string OutputFormat { get; init; } = "markdown";

    /// <summary>
    /// A lépés azonosítója. Az output mappa nevét és az eseményeket is ez határozza meg.
    /// </summary>
    public required string Step { get; init; }

    /// <summary>
    /// Az AI rendszer promptja erre a lépésre.
    /// Meghatározza az AI szerepét és viselkedési szabályait.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// Maximálisan generálható tokenek száma.
    /// Védelmet nyújt a repetition loop ellen.
    ///
    /// Ajánlott értékek:
    ///   task_breakdown:  800–1200
    ///   code_generation: 2000–4000
    ///   code_review:     1000–2000
    ///   documentation:   1500–2500
    ///
    /// Ha nincs megadva (null), a backend alapértelmezése érvényes.
    /// </summary>
    public int? MaxTokens { get; init; }
}
