using Hcoona.QidianNovelDownloader.Browser;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class PageScriptsTests
{
    [Fact]
    public void ChapterContentJsonDoesNotFilterNumericOnlyParagraphs()
    {
        Assert.DoesNotContain("/^\\d+$/.test(text)", PageScripts.ChapterContentJson, StringComparison.Ordinal);
    }
}
