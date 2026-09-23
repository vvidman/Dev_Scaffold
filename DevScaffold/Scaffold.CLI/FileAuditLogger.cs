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
using Scaffold.Application.Interfaces;

namespace Scaffold.CLI;

/// <summary>
/// File-based audit logger implementation.
///
/// Writes to audit.log in AutoFlush mode – partial data survives even
/// on a crash.
///
/// Log line format:
///   {timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] [{tag,-16}] {message}
///
/// Examples:
///   2026-03-17 14:23:01.123 [INFO ] [SESSION_START   ] step=task_breakdown generation=1
///   2026-03-17 14:23:01.124 [INFO ] [CONFIG          ] model=qwen-7b system_prompt_length=342
///   2026-03-17 14:25:43.891 [INFO ] [INFERENCE_DONE  ] tokens=847 elapsed=162s tok_s=5.2
///   2026-03-17 14:25:50.012 [INFO ] [VALIDATION      ] outcome=Reject clarification="Needs a more detailed breakdown"
///
/// Custom parser: every line has the timestamp (23 chars), the level
/// (7 chars), and the tag (18 chars) at fixed positions – followed by
/// the message as key=value pairs.
/// </summary>
public sealed class FileAuditLogger : IAuditLogger
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    // Tag → fixed-width string (16 chars) for the log format
    private static readonly Dictionary<AuditEvent, string> Tags = new()
    {
        [AuditEvent.SessionStart] = "SESSION_START",
        [AuditEvent.Config] = "CONFIG",
        [AuditEvent.InferenceStart] = "INFERENCE_START",
        [AuditEvent.InferenceDone] = "INFERENCE_DONE",
        [AuditEvent.Output] = "OUTPUT",
        [AuditEvent.Validation] = "VALIDATION",
        [AuditEvent.SessionEnd] = "SESSION_END",
        [AuditEvent.Error] = "ERROR",
    };

    public FileAuditLogger(string logFilePath)
    {
        // append: true – on a rerun, earlier generations' logs are preserved
        // since it gets its own folder, this always writes to an empty file, but it's safe
        _writer = new StreamWriter(logFilePath, append: false)
        {
            AutoFlush = true
        };
    }

    /// <inheritdoc />
    public void Log(AuditEvent eventType, string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var level = eventType == AuditEvent.Error ? "ERROR" : "INFO ";
        var tag = Tags.TryGetValue(eventType, out var t)
            ? t
            : eventType.ToString().ToUpperInvariant();

        // Fixed width: [{level,-5}] [{tag,-16}]
        // The tag is padded to 16 chars wide – a custom parser can use a position-based split
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{tag,-16}] {message}";

        _writer.WriteLine(line);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _writer.DisposeAsync();
    }
}