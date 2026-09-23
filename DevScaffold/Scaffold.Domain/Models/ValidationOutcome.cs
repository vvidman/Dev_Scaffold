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
/// Possible outcomes of human validation after each step.
/// </summary>
public enum ValidationOutcome
{
    /// <summary>
    /// The output has not been evaluated yet.
    /// </summary>
    NotValidated,

    /// <summary>
    /// The output is acceptable, proceed to the next step.
    /// </summary>
    Accept,

    /// <summary>
    /// The human edited the output, then it is passed on.
    /// </summary>
    Edit,

    /// <summary>
    /// Sent back to the AI within the same step.
    /// A new iteration starts: original context + rejected output + clarification.
    /// </summary>
    Reject
}
