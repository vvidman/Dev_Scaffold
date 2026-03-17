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
/// Step-szintű audit log absztrakciója.
///
/// Minden session saját log fájlt kap (audit.log a step output folderben).
/// A log folyamatosan íródik auto-flush módban – crash esetén is megmarad
/// a részleges adat.
///
/// Log sor formátum (custom parser-barát):
///   2026-03-17 14:23:01.123 [INFO ] [SESSION_START   ] step=task_breakdown generation=1
///   2026-03-17 14:25:43.891 [INFO ] [INFERENCE_DONE  ] tokens=847 elapsed=162s tok_s=5.2
///
/// A tag mező fix 16 karakter széles – statisztikai feldolgozásnál
/// egyszerű string split elegendő a szintaktikai elemzés helyett.
/// </summary>
public interface IAuditLogger : IAsyncDisposable
{
    /// <summary>
    /// Bejegyzést ír az audit logba.
    /// </summary>
    /// <param name="eventType">Az esemény típusa – meghatározza a log sor tag-jét.</param>
    /// <param name="message">
    /// A log sor tartalma key=value párokban.
    /// Pl.: "step=task_breakdown generation=2 model=qwen-7b"
    /// </param>
    void Log(AuditEvent eventType, string message);
}