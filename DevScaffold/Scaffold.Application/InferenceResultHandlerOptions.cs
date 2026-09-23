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

namespace Scaffold.Application;

/// <summary>
/// Options for <see cref="InferenceResultHandler"/>.
/// </summary>
/// <param name="InferenceLivenessTimeout">
/// If no event for a request_id arrives within this window, the step fails
/// instead of waiting forever. Default: 3 × the ServiceHost progress interval
/// (30s, see ADR-Protocol #13) = 90s. Suspended while the human validation
/// prompt is active.
/// </param>
public sealed record InferenceResultHandlerOptions(TimeSpan InferenceLivenessTimeout)
{
    public static readonly TimeSpan DefaultLivenessTimeout = TimeSpan.FromSeconds(90);

    public InferenceResultHandlerOptions() : this(DefaultLivenessTimeout)
    {
    }
}
