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
/// Egy modell konfigurációja a models.yaml-ban.
/// Az alias → ModelConfig mapping értéke.
/// </summary>
public class ModelConfig
{
    public string Path { get; init; } = string.Empty;
    public int ContextSize { get; init; } = 4096;
    public int GpuLayers { get; init; } = 0;
}
