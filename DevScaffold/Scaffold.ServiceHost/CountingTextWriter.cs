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
/// TextWriter decorator that counts the backend's WriteAsync calls.
/// Every non-empty WriteAsync call counts as one token – this is an
/// approximation, but good enough to display tok/s.
///
/// Thread-safe: Interlocked.Increment guards the counter,
/// every other call delegates to the inner writer.
///
/// Dispose does not close the inner writer – lifecycle management
/// is the caller's responsibility.
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

    // Dispose does not close the inner writer – InferenceWorker manages it
    protected override void Dispose(bool disposing) { }
}