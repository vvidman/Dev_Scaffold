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

using System.Text;

namespace Scaffold.ServiceHost;

/// <summary>
/// TextWriter decorator ami megszámolja a backend WriteAsync hívásait.
/// Minden nem-üres WriteAsync hívás egy tokennek számít – ez közelítő érték,
/// de elegendő a tok/s kijelzéséhez.
///
/// Thread-safe: Interlocked.Increment biztosítja a számlálót,
/// az összes többi hívás az inner writer-re delegál.
///
/// A Dispose nem zárja be az inner writert – az életciklus kezelése
/// a hívó felelőssége.
/// </summary>
internal sealed class CountingTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private int _tokenCount;

    public int TokenCount => _tokenCount;

    public CountingTextWriter(TextWriter inner) => _inner = inner;

    public override Encoding Encoding => _inner.Encoding;

    public override async Task WriteAsync(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            Interlocked.Increment(ref _tokenCount);

        await _inner.WriteAsync(value);
    }

    public override async Task WriteAsync(char value)
    {
        Interlocked.Increment(ref _tokenCount);
        await _inner.WriteAsync(value);
    }

    public override Task FlushAsync() => _inner.FlushAsync();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _inner.FlushAsync(cancellationToken);

    // Dispose nem zárja be az inner writert – az InferenceWorker kezeli
    protected override void Dispose(bool disposing) { }
}