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
using Scaffold.Infrastructure.ConfigHandler;

namespace Scaffold.Tests.Input;

[TestClass]
public sealed class InputAssemblerTests
{
    private string _tempRoot = null!;
    private readonly InputAssembler _assembler = new();

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

    private string WriteFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    [TestMethod]
    public void Assemble_PrimaryInputMissing_ThrowsFileNotFound()
    {
        var missing = Path.Combine(_tempRoot, "does_not_exist.yaml");

        Assert.ThrowsExactly<FileNotFoundException>(
            () => _assembler.Assemble(missing, "task_breakdown"));
    }

    [TestMethod]
    public void Assemble_ReferencedPathMissing_ThrowsScaffoldInputValidationException()
    {
        var yamlPath = WriteFile("input.yaml", """
            context:
              spec_path: ./missing_spec.md
            """);

        var ex = Assert.ThrowsExactly<ScaffoldInputValidationException>(
            () => _assembler.Assemble(yamlPath, "task_breakdown"));

        StringAssert.Contains(ex.Message, "context.spec_path");
    }

    [TestMethod]
    public void Assemble_NestedPathInSequence_IsResolvedAndInlined()
    {
        WriteFile("docs/spec_a.md", "Spec A content");
        WriteFile("docs/spec_b.md", "Spec B content");
        var yamlPath = WriteFile("input.yaml", """
            references:
              - path: ./docs/spec_a.md
              - path: ./docs/spec_b.md
            """);

        var context = _assembler.Assemble(yamlPath, "task_breakdown");

        StringAssert.Contains(context, "Spec A content");
        StringAssert.Contains(context, "Spec B content");
    }

    [TestMethod]
    public void Assemble_RelativePathsResolvedAgainstYamlDirectory()
    {
        var subDir = "sub";
        WriteFile(Path.Combine(subDir, "notes.md"), "Notes content");
        var yamlPath = WriteFile(Path.Combine(subDir, "input.yaml"), """
            context:
              notes_path: ./notes.md
            """);

        var context = _assembler.Assemble(yamlPath, "task_breakdown");

        StringAssert.Contains(context, "Notes content");
    }

    [TestMethod]
    public void Assemble_SecondaryInput_AppendedAfterPrimaryWithSeparator()
    {
        var primaryPath = WriteFile("primary.yaml", "context:\n  name: primary\n");
        var secondaryPath = WriteFile("secondary.yaml", "context:\n  name: secondary\n");

        var context = _assembler.Assemble(primaryPath, "task_breakdown", secondaryPath);

        StringAssert.Contains(context, "primary");
        StringAssert.Contains(context, "\n\n---\n\n");
        StringAssert.Contains(context, "secondary");
        Assert.IsTrue(context.IndexOf("primary", StringComparison.Ordinal)
                      < context.IndexOf("secondary", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Assemble_TildeOrEmptyPathValue_IsIgnored()
    {
        var yamlPath = WriteFile("input.yaml", """
            context:
              optional_path: ~
              empty_path: ""
              name: value
            """);

        var context = _assembler.Assemble(yamlPath, "task_breakdown");

        StringAssert.Contains(context, "name: value");
    }
}
