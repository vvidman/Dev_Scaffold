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
using Scaffold.Application;

namespace Scaffold.Application.Interfaces;

/// <summary>
/// Abstraction for the step-level audit log.
///
/// Each session gets its own log file (audit.log in the step output folder).
/// The log is written continuously in auto-flush mode – partial data
/// survives even on a crash.
///
/// Log line format (custom-parser-friendly):
///   2026-03-17 14:23:01.123 [INFO ] [SESSION_START   ] step=task_breakdown generation=1
///   2026-03-17 14:25:43.891 [INFO ] [INFERENCE_DONE  ] tokens=847 elapsed=162s tok_s=5.2
///
/// The tag field is a fixed 16 characters wide – for statistical processing
/// a simple string split is enough, no syntactic parsing needed.
/// </summary>
public interface IAuditLogger : IAsyncDisposable
{
    /// <summary>
    /// Writes an entry to the audit log.
    /// </summary>
    /// <param name="eventType">The event's type – determines the log line's tag.</param>
    /// <param name="message">
    /// The log line's content, as key=value pairs.
    /// E.g.: "step=task_breakdown generation=2 model=qwen-7b"
    /// </param>
    void Log(AuditEvent eventType, string message);
}