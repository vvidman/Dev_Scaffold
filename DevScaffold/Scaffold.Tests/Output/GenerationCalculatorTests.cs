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

using Scaffold.Application.Output;

namespace Scaffold.Tests.Output;

[TestClass]
public sealed class GenerationCalculatorTests
{
    private string _tempRoot = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [TestMethod]
    public void ComputeNext_OutputFolderMissing_ReturnsOne()
    {
        var missing = Path.Combine(_tempRoot, "does_not_exist");

        var result = GenerationCalculator.ComputeNext(missing, "task_breakdown");

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void ComputeNext_NoMatchingFolders_ReturnsOne()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "unrelated_folder"));

        var result = GenerationCalculator.ComputeNext(_tempRoot, "task_breakdown");

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void ComputeNext_SequentialFolders_ReturnsMaxPlusOne()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "task_breakdown_1"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "task_breakdown_2"));

        var result = GenerationCalculator.ComputeNext(_tempRoot, "task_breakdown");

        Assert.AreEqual(3, result);
    }

    [TestMethod]
    public void ComputeNext_SparseFolders_UsesMaxNotCount()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "task_breakdown_1"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "task_breakdown_3"));

        var result = GenerationCalculator.ComputeNext(_tempRoot, "task_breakdown");

        Assert.AreEqual(4, result);
    }

    [TestMethod]
    public void ComputeNext_OtherStepWithSharedPrefix_IsIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "task_breakdown_1"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "task_breakdown_extra_1"));

        var result = GenerationCalculator.ComputeNext(_tempRoot, "task_breakdown");

        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public void ComputeNext_NonNumericSuffix_IsIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "task_breakdown_old"));

        var result = GenerationCalculator.ComputeNext(_tempRoot, "task_breakdown");

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public void TryParseGeneration_DifferentCase_IsParsed()
    {
        var result = GenerationCalculator.TryParseGeneration("TASK_BREAKDOWN_2", "task_breakdown");

        Assert.AreEqual(2, result);
    }
}
