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
/// Egy pipeline lépés referenciája a pipeline.yaml-ban.
/// Meghatározza az agent config fájl helyét és a használt modell aliasát.
/// </summary>
public class PipelineStepRef
{
    public string Id { get; init; } = string.Empty;
    public string Config { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
}
