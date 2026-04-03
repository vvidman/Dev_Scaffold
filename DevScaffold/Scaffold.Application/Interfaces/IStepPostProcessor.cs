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
/// Elfogadott step kimenet utófeldolgozásának absztrakciója.
///
/// Az IStepOutputValidator mintáját követi: minden implementáció
/// egy konkrét stephez kötött (StepId), és Accept/Edit után fut.
///
/// Az implementáció hibája nem invalidálja az elfogadást –
/// a hívó kód naplóz és folytatja (exit 0).
/// </summary>
public interface IStepPostProcessor
{
    /// <summary>A step azonosítója, amelyre ez a feldolgozó vonatkozik (pl. "task_breakdown").</summary>
    string StepId { get; }

    /// <summary>
    /// Végrehajtja az utófeldolgozást az elfogadott kimeneten.
    /// </summary>
    /// <param name="context">A feldolgozás teljes kontextusa (lásd <see cref="PostProcessorContext"/>).</param>
    Task ProcessAsync(PostProcessorContext context);
}
