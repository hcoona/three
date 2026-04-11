using Hcoona.QidianNovelDownloader.Browser;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class PageScriptsTests
{
    [Fact]
    public void ChapterContentJsonDoesNotFilterNumericOnlyParagraphs()
    {
        Assert.DoesNotContain(
            "/^\\d+$/.test(text)",
            PageScripts.ChapterContentJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogJsonIncludesPurchaseRequiredIconParsing()
    {
        Assert.Contains("catalogAccessState", PageScripts.CatalogJson, StringComparison.Ordinal);
        Assert.Contains("em.iconfont", PageScripts.CatalogJson, StringComparison.Ordinal);
        Assert.Contains("", PageScripts.CatalogJson, StringComparison.Ordinal);
        Assert.Contains("'PurchaseRequired'", PageScripts.CatalogJson, StringComparison.Ordinal);
    }
}
