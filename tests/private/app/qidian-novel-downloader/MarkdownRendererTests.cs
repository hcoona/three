using Hcoona.QidianNovelDownloader.Output;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void RenderGroupsChaptersByVolumeAndSeparatesParagraphs()
    {
        CatalogSnapshot catalog = new(
            "1045928363",
            new BookMetadata("1045928363", "Title", "Author", 123456),
            [
                new VolumeDescriptor(
                    "Volume One",
                    IsVip: false,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Chapter One",
                            "https://example.com/1",
                            false,
                            1000,
                            CatalogChapterAccessState.Accessible),
                    ]),
            ],
            DateTimeOffset.UtcNow);
        Dictionary<string, RenderedChapter> renderedChapters = new(StringComparer.Ordinal)
        {
            ["1"] = new RenderedChapter(
                "Chapter One",
                ["First paragraph。31", "Second paragraph。"]),
        };

        string markdown = MarkdownRenderer.Render(catalog, renderedChapters);

        Assert.Contains("# Volume One", markdown);
        Assert.Contains("## Chapter One", markdown);
        Assert.Contains("First paragraph。31", markdown);
        Assert.Contains($"First paragraph。31{Environment.NewLine}{Environment.NewLine}Second paragraph。", markdown);
    }

    [Fact]
    public void RenderPreservesNumericOnlyParagraphs()
    {
        CatalogSnapshot catalog = new(
            "1045928363",
            new BookMetadata("1045928363", "Title", "Author", 123456),
            [
                new VolumeDescriptor(
                    "Volume One",
                    IsVip: false,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Chapter One",
                            "https://example.com/1",
                            false,
                            1000,
                            CatalogChapterAccessState.Accessible),
                    ]),
            ],
            DateTimeOffset.UtcNow);
        Dictionary<string, RenderedChapter> renderedChapters = new(StringComparer.Ordinal)
        {
            ["1"] = new RenderedChapter(
                "Chapter One",
                ["31", "Second paragraph。"]),
        };

        string markdown = MarkdownRenderer.Render(catalog, renderedChapters);

        Assert.Contains($"31{Environment.NewLine}{Environment.NewLine}Second paragraph。", markdown);
    }
}
