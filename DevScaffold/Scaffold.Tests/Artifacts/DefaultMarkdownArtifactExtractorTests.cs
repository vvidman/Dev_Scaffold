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
public sealed class DefaultMarkdownArtifactExtractorTests
{
    private const string HintPrefix = "// filepath:";

    private readonly DefaultMarkdownArtifactExtractor _extractor = new();

    [TestMethod]
    public void Extract_EmptyMarkdown_ReturnsEmpty()
    {
        var result = _extractor.Extract("", HintPrefix);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Extract_SingleBlockWithHint_UsesHintPathAndStripsHintLine()
    {
        var markdown = "```csharp\n// filepath: src/Services/Foo.cs\npublic class Foo {}\n```";

        var result = _extractor.Extract(markdown, HintPrefix);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("src/Services/Foo.cs", result[0].RelativeFilePath);
        Assert.AreEqual("public class Foo {}", result[0].Content);
    }

    [TestMethod]
    public void Extract_HintPrefixNull_UsesFallbackName()
    {
        var markdown = "```csharp\npublic class Foo {}\n```";

        var result = _extractor.Extract(markdown, null);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("artifact_01.cs", result[0].RelativeFilePath);
    }

    [TestMethod]
    public void Extract_HintMissingInBlock_UsesFallbackName()
    {
        var markdown = "```csharp\npublic class Foo {}\n```";

        var result = _extractor.Extract(markdown, HintPrefix);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("artifact_01.cs", result[0].RelativeFilePath);
    }

    [TestMethod]
    public void Extract_UnknownLanguage_FallbackExtensionIsTxt()
    {
        var markdown = "```someunknownlang\ncontent\n```";

        var result = _extractor.Extract(markdown, null);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("artifact_01.txt", result[0].RelativeFilePath);
    }

    [TestMethod]
    public void Extract_MultipleBlocks_FallbackIndexFollowsBlockOrder()
    {
        var markdown = "```csharp\nfirst\n```\n\nsome text\n\n```json\n{}\n```";

        var result = _extractor.Extract(markdown, null);

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("artifact_01.cs", result[0].RelativeFilePath);
        Assert.AreEqual("artifact_02.json", result[1].RelativeFilePath);
    }

    [TestMethod]
    public void Extract_WhitespaceOnlyBlock_IsSkipped()
    {
        var markdown = "```csharp\n   \n\t\n```";

        var result = _extractor.Extract(markdown, null);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Extract_CrLfLineEndings_AreHandled()
    {
        var markdown = "```csharp\r\n// filepath: src/Foo.cs\r\npublic class Foo {}\r\n```";

        var result = _extractor.Extract(markdown, HintPrefix);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("src/Foo.cs", result[0].RelativeFilePath);
        Assert.AreEqual("public class Foo {}", result[0].Content);
    }
}
