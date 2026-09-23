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
/// Abstraction for post-processing an accepted step output.
///
/// Follows the IStepOutputValidator pattern: every implementation is
/// bound to a specific step (StepId), and runs after Accept/Edit.
///
/// A failure in the implementation does not invalidate the acceptance –
/// the caller logs it and continues (exit 0).
/// </summary>
public interface IStepPostProcessor
{
    /// <summary>The step identifier this processor applies to (e.g. "task_breakdown").</summary>
    string StepId { get; }

    /// <summary>
    /// Runs post-processing on the accepted output.
    /// </summary>
    /// <param name="context">The full processing context (see <see cref="PostProcessorContext"/>).</param>
    Task ProcessAsync(PostProcessorContext context);
}
