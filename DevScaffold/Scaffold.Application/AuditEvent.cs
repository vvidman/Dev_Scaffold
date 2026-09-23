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
/// Audit log event types.
/// Each type appears as a fixed-width tag in the log line –
/// this is what makes it possible to write a custom parser for statistics.
/// </summary>
public enum AuditEvent
{
    /// <summary>The CLI session started – with step and generation identifiers.</summary>
    SessionStart,

    /// <summary>The agent configuration was loaded – model, prompt length, token limit.</summary>
    Config,

    /// <summary>The inference request was sent to the ServiceHost.</summary>
    InferenceStart,

    /// <summary>Inference completed successfully – tokens, speed, elapsed time.</summary>
    InferenceDone,

    /// <summary>The output file's path was recorded.</summary>
    Output,

    /// <summary>The human validation decision was recorded – Accept/Edit/Reject + clarification.</summary>
    Validation,

    /// <summary>The session ended – total elapsed time and final outcome.</summary>
    SessionEnd,

    /// <summary>Error event – an infrastructure- or inference-level error.</summary>
    Error,

    /// <summary> Refinement event - Refinement prompt was created. </summary>
    Refinement,
}
