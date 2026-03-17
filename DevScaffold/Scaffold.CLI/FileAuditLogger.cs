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
/// Fájl alapú audit logger implementáció.
///
/// Az audit.log fájlba ír, AutoFlush módban – crash esetén is megmarad
/// a részleges adat.
///
/// Log sor formátum:
///   {timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] [{tag,-16}] {message}
///
/// Példák:
///   2026-03-17 14:23:01.123 [INFO ] [SESSION_START   ] step=task_breakdown generation=1
///   2026-03-17 14:23:01.124 [INFO ] [CONFIG          ] model=qwen-7b system_prompt_length=342
///   2026-03-17 14:25:43.891 [INFO ] [INFERENCE_DONE  ] tokens=847 elapsed=162s tok_s=5.2
///   2026-03-17 14:25:50.012 [INFO ] [VALIDATION      ] outcome=Reject clarification="Részletesebb bontás kell"
///
/// Custom parser: minden sor fix pozíción tartalmazza a timestamp-et (23 kar),
/// a level-t (7 kar), a tag-et (18 kar) – ezután a message key=value párokban.
/// </summary>
public sealed class FileAuditLogger : IAuditLogger
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    // Tag → fix szélességű string (16 kar) a log formátumhoz
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
        // append: true – újrafuttatás esetén a korábbi generációk logjai megmaradnak
        // ha külön foldert kap, ez mindig üres fájlba ír, de biztonságos
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

        // Fix szélesség: [{level,-5}] [{tag,-16}]
        // A tag 16 kar szélesen van kitöltve – custom parser pozíció-alapú split-tel kezelheti
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