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
public sealed class ArtifactApplierTests
{
    private string _tempRoot = null!;
    private string _outputBasePath = null!;
    private string _projectRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _outputBasePath = Path.Combine(_tempRoot, "output");
        _projectRoot = Path.Combine(_tempRoot, "project");
        Directory.CreateDirectory(_outputBasePath);
        Directory.CreateDirectory(_projectRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private void CreateArtifact(string stepFolder, string relativePath, string content)
    {
        var fullPath = Path.Combine(_outputBasePath, stepFolder, "artifacts", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    [TestMethod]
    public void Apply_DryRun_WritesNothingAndReportsWouldCopy()
    {
        CreateArtifact("coding_1", "Foo.cs", "content");

        var result = ArtifactApplier.Apply(
            _outputBasePath, ["coding_1"], _projectRoot, dryRun: true);

        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreEqual(ArtifactApplyStatus.WouldCopy, result.Entries[0].Status);
        Assert.AreEqual(0, result.CopiedCount);
        Assert.IsFalse(File.Exists(Path.Combine(_projectRoot, "Foo.cs")));
    }

    [TestMethod]
    public void Apply_ExistingTargetFile_IsOverwritten()
    {
        CreateArtifact("coding_1", "Foo.cs", "new content");
        File.WriteAllText(Path.Combine(_projectRoot, "Foo.cs"), "old content");

        var result = ArtifactApplier.Apply(
            _outputBasePath, ["coding_1"], _projectRoot, dryRun: false);

        Assert.AreEqual(1, result.CopiedCount);
        Assert.AreEqual("new content", File.ReadAllText(Path.Combine(_projectRoot, "Foo.cs")));
    }

    [TestMethod]
    public void Apply_FolderWithoutArtifacts_IsReportedAsSkipped()
    {
        Directory.CreateDirectory(Path.Combine(_outputBasePath, "coding_2"));

        var result = ArtifactApplier.Apply(
            _outputBasePath, ["coding_2"], _projectRoot, dryRun: false);

        Assert.AreEqual(0, result.Entries.Count);
        CollectionAssert.Contains(result.SkippedFoldersWithoutArtifacts.ToList(), "coding_2");
    }

    [TestMethod]
    public void Apply_MultipleFolders_ProcessesAll()
    {
        CreateArtifact("coding_1", "A.cs", "a");
        CreateArtifact("coding_2", "B.cs", "b");

        var result = ArtifactApplier.Apply(
            _outputBasePath, ["coding_1", "coding_2"], _projectRoot, dryRun: false);

        Assert.AreEqual(2, result.CopiedCount);
        Assert.IsTrue(File.Exists(Path.Combine(_projectRoot, "A.cs")));
        Assert.IsTrue(File.Exists(Path.Combine(_projectRoot, "B.cs")));
    }

    [TestMethod]
    public void Apply_NestedArtifact_PreservesRelativeStructure()
    {
        CreateArtifact("coding_1", Path.Combine("src", "Services", "Foo.cs"), "content");

        var result = ArtifactApplier.Apply(
            _outputBasePath, ["coding_1"], _projectRoot, dryRun: false);

        Assert.AreEqual(1, result.CopiedCount);
        Assert.IsTrue(File.Exists(Path.Combine(_projectRoot, "src", "Services", "Foo.cs")));
    }
}
