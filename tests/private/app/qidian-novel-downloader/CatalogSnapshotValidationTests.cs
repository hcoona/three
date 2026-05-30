using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class CatalogSnapshotValidationTests
{
    [Theory]
    [InlineData("https://www.qidian.com:444/chapter/100/1/")]
    [InlineData("https://qidian.com:444/chapter/100/1/")]
    [InlineData("https://www.qidian.com/Chapter/100/1/")]
    [InlineData("https://www.qidian.com?next=/chapter/100/1/")]
    [InlineData("https://www.qidian.com#/chapter/100/1/")]
    [InlineData("https://www.qidian.com/chapter/100/1/?from=app")]
    [InlineData("https://www.qidian.com/chapter/100/1/#content")]
    [InlineData("https://www.qidian.com/chapter/100/%31/")]
    [InlineData("https://www.qidian.com/chapter/%31%30%30/1/")]
    [InlineData("\u0001https://@www.qidian.com/chapter/100/1/")]
    [InlineData("\u0001https:////www.qidian.com/chapter/100/1/")]
    [InlineData("https:////www.qidian.com/chapter/100/1/")]
    [InlineData("///www.qidian.com/chapter/100/1/")]
    [InlineData("////www.qidian.com/chapter/100/1/")]
    [InlineData("https://@www.qidian.com/chapter/100/1/")]
    [InlineData("https://attacker@www.qidian.com/chapter/100/1/")]
    [InlineData("https://attacker:password@www.qidian.com/chapter/100/1/")]
    public void IsChapterUrlUsableForBookRejectsNonCanonicalChapterUrls(string url)
    {
        Assert.False(CatalogSnapshotValidation.IsChapterUrlUsableForBook(url, "100", "1"));
    }

    [Fact]
    public void IsChapterUrlUsableForBookRejectsNonAsciiExpectedBookIds()
    {
        Assert.False(CatalogSnapshotValidation.IsChapterUrlUsableForBook(
            "https://www.qidian.com/chapter/１００/1/",
            "１００",
            "1"));
    }

    [Theory]
    [InlineData("1/2")]
    [InlineData("1?from=app")]
    [InlineData("1#content")]
    [InlineData("../1")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("1.2")]
    [InlineData("1%2F2")]
    [InlineData("c1")]
    [InlineData("chapter-1")]
    [InlineData("chapter_1")]
    [InlineData("")]
    public void IsChapterUrlUsableForBookRejectsNonCanonicalExpectedChapterIds(string chapterId)
    {
        Assert.False(CatalogSnapshotValidation.IsChapterUrlUsableForBook(
            $"https://www.qidian.com/chapter/100/{Uri.EscapeDataString(chapterId)}/",
            "100",
            chapterId));
    }

    [Theory]
    [InlineData("https://www.qidian.com:443/chapter/100/1/")]
    [InlineData("https://qidian.com/chapter/100/1/")]
    [InlineData("https://www.qidian.com/Chapter/100/1/")]
    public void NormalizeChapterUrlsForBookCanonicalizesNonCanonicalChapterUrls(string url)
    {
        CatalogSnapshot catalog = new(
            "100",
            new BookMetadata("100", "Title", "Author", EstimatedWordCount: null),
            [
                new VolumeDescriptor(
                    "Volume",
                    IsVip: false,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Chapter",
                            url,
                            IsVip: false,
                            CatalogWordCount: null,
                            CatalogAccessState: CatalogChapterAccessState.Accessible),
                    ]),
            ],
            DateTimeOffset.UtcNow);

        CatalogSnapshot normalized = CatalogSnapshotValidation.NormalizeChapterUrlsForBook(
            catalog,
            "100");

        Assert.Equal(
            "https://www.qidian.com/chapter/100/1/",
            normalized.Volumes[0].Chapters[0].Url);
    }

    [Fact]
    public void NormalizeChapterUrlsForBookTrimsUsableChapterUrls()
    {
        CatalogSnapshot catalog = new(
            "100",
            new BookMetadata("100", "Title", "Author", EstimatedWordCount: null),
            [
                new VolumeDescriptor(
                    "Volume",
                    IsVip: false,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Chapter",
                            " https://www.qidian.com/chapter/100/1/ ",
                            IsVip: false,
                            CatalogWordCount: null,
                            CatalogAccessState: CatalogChapterAccessState.Accessible),
                    ]),
            ],
            DateTimeOffset.UtcNow);

        CatalogSnapshot normalized = CatalogSnapshotValidation.NormalizeChapterUrlsForBook(
            catalog,
            "100");

        Assert.Equal(
            "https://www.qidian.com/chapter/100/1/",
            normalized.Volumes[0].Chapters[0].Url);
    }

    [Theory]
    [InlineData("1/2")]
    [InlineData("1?from=app")]
    [InlineData("1#content")]
    [InlineData("../1")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("1.2")]
    [InlineData("1%2F2")]
    [InlineData("c1")]
    [InlineData("chapter-1")]
    [InlineData("chapter_1")]
    public void NormalizeChapterUrlsForBookRejectsNonCanonicalChapterIds(string chapterId)
    {
        CatalogSnapshot catalog = new(
            "100",
            new BookMetadata("100", "Title", "Author", EstimatedWordCount: null),
            [
                new VolumeDescriptor(
                    "Volume",
                    IsVip: false,
                    [
                        new ChapterDescriptor(
                            chapterId,
                            "Chapter",
                            $"https://www.qidian.com/chapter/100/{Uri.EscapeDataString(chapterId)}/",
                            IsVip: false,
                            CatalogWordCount: null,
                            CatalogAccessState: CatalogChapterAccessState.Accessible),
                    ]),
            ],
            DateTimeOffset.UtcNow);

        Assert.Throws<OperationalException>(() =>
            CatalogSnapshotValidation.NormalizeChapterUrlsForBook(catalog, "100"));
    }

    [Theory]
    [InlineData("１００", "100", "100")]
    [InlineData("100", "１００", "100")]
    [InlineData("100", "100", "１００")]
    public void NormalizeChapterUrlsForBookRejectsNonAsciiBookIds(
        string catalogBookId,
        string metadataBookId,
        string expectedBookId)
    {
        CatalogSnapshot catalog = new(
            catalogBookId,
            new BookMetadata(metadataBookId, "Title", "Author", EstimatedWordCount: null),
            [
                new VolumeDescriptor(
                    "Volume",
                    IsVip: false,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Chapter",
                            $"https://www.qidian.com/chapter/{catalogBookId}/1/",
                            IsVip: false,
                            CatalogWordCount: null,
                            CatalogAccessState: CatalogChapterAccessState.Accessible),
                    ]),
            ],
            DateTimeOffset.UtcNow);

        Assert.Throws<OperationalException>(() =>
            CatalogSnapshotValidation.NormalizeChapterUrlsForBook(catalog, expectedBookId));
    }
}
