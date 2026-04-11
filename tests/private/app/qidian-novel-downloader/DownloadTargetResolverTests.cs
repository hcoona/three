using Hcoona.QidianNovelDownloader.Commands;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class DownloadTargetResolverTests
{
    [Fact]
    public void ResolveUsesConfiguredDefaultBooksWhenCommandLineTargetsAreMissing()
    {
        List<BookReference> targets = DownloadTargetResolver.Resolve(
            [],
            ["1045928363", "https://www.qidian.com/book/1045928364/"]);

        Assert.Collection(
            targets,
            target => Assert.Equal("1045928363", target.BookId),
            target => Assert.Equal("1045928364", target.BookId));
    }

    [Fact]
    public void ResolvePrefersCommandLineTargetsOverConfiguredDefaults()
    {
        List<BookReference> targets = DownloadTargetResolver.Resolve(
            ["1045928365"],
            ["1045928363", "1045928364"]);

        BookReference target = Assert.Single(targets);
        Assert.Equal("1045928365", target.BookId);
    }

    [Fact]
    public void ResolveDeduplicatesByNormalizedBookId()
    {
        List<BookReference> targets = DownloadTargetResolver.Resolve(
            ["1045928363", "https://www.qidian.com/book/1045928363/"],
            []);

        BookReference target = Assert.Single(targets);
        Assert.Equal("1045928363", target.BookId);
    }

    [Fact]
    public void ResolveThrowsWhenNoTargetsAreAvailable()
    {
        Assert.Throws<CliInputException>(() => DownloadTargetResolver.Resolve([], []));
    }
}
