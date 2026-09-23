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

using Scaffold.Application.Artifacts;

namespace Scaffold.Tests.Artifacts;

[TestClass]
public sealed class ArtifactPathGuardTests
{
    private string _tempRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "root");
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var parent = Path.GetDirectoryName(_tempRoot)!;
        if (Directory.Exists(parent))
            Directory.Delete(parent, recursive: true);
    }

    [TestMethod]
    public void TryResolveWithin_NestedRelativePath_ReturnsPathInsideRoot()
    {
        var ok = ArtifactPathGuard.TryResolveWithin(_tempRoot, "src/Services/Foo.cs", out var fullPath);

        Assert.IsTrue(ok);
        Assert.IsNotNull(fullPath);
        Assert.IsTrue(fullPath!.StartsWith(Path.GetFullPath(_tempRoot), StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow("../x.cs")]
    [DataRow("a/../../x.cs")]
    [DataRow("")]
    [DataRow("   ")]
    public void TryResolveWithin_ParentTraversalOrEmpty_ReturnsFalse(string relativePath)
    {
        var ok = ArtifactPathGuard.TryResolveWithin(_tempRoot, relativePath, out var fullPath);

        Assert.IsFalse(ok);
        Assert.IsNull(fullPath);
    }

    [TestMethod]
    public void TryResolveWithin_RootedPath_ReturnsFalse()
    {
        var absolute = Path.GetFullPath(Path.Combine(_tempRoot, "x.cs"));

        var ok = ArtifactPathGuard.TryResolveWithin(_tempRoot, absolute, out var fullPath);

        Assert.IsFalse(ok);
        Assert.IsNull(fullPath);
    }

    [TestMethod]
    public void TryResolveWithin_SiblingWithSharedPrefix_ReturnsFalse()
    {
        var ok = ArtifactPathGuard.TryResolveWithin(_tempRoot, "../root-other/x.cs", out var fullPath);

        Assert.IsFalse(ok);
        Assert.IsNull(fullPath);
    }

    [TestMethod]
    public void TryResolveWithin_EmptyOrWhitespace_ReturnsFalse()
    {
        var ok = ArtifactPathGuard.TryResolveWithin(_tempRoot, "", out var fullPath);

        Assert.IsFalse(ok);
        Assert.IsNull(fullPath);
    }

    [TestMethod]
    public void TryResolveWithin_DotSegmentsStayingInside_ReturnsTrue()
    {
        var ok = ArtifactPathGuard.TryResolveWithin(_tempRoot, "a/./b/../c.cs", out var fullPath);

        Assert.IsTrue(ok);
        Assert.IsNotNull(fullPath);
        Assert.AreEqual(Path.GetFullPath(Path.Combine(_tempRoot, "a", "c.cs")), fullPath);
    }
}
