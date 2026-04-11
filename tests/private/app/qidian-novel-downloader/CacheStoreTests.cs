using Hcoona.QidianNovelDownloader.Cache;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class CacheStoreTests
{
    [Fact]
    public void ClearCatalogOnlyRemovesCatalogAndKeepsChapterCache()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        string bookId = "1045928363";
        string catalogPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.Anonymous);
        string validatedCatalogPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.ForValidatedUser("tester"));
        string chapterPath = AppPaths.GetChapterCachePath(root, bookId, "1");
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(validatedCatalogPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(chapterPath)!);
        File.WriteAllText(catalogPath, "{}");
        File.WriteAllText(validatedCatalogPath, "{}");
        File.WriteAllText(chapterPath, "{}");

        int removed = CacheStore.Clear(root, bookId, catalogOnly: true);

        Assert.Equal(2, removed);
        Assert.False(File.Exists(catalogPath));
        Assert.False(File.Exists(validatedCatalogPath));
        Assert.True(File.Exists(chapterPath));
    }

    [Fact]
    public void ClearGlobalReturnsZeroForEmptyDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        Directory.CreateDirectory(root);

        int removed = CacheStore.Clear(root, bookId: null, catalogOnly: false);

        Assert.Equal(0, removed);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForInvalidJson()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, "{ invalid json");

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);

        Assert.Null(chapter);
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("{\"bookId\":\"1045928363\"")]
    [InlineData(
        "{\"bookId\":\"1045928363\",\"metadata\":null,\"volumes\":[],"
        + "\"fetchedAtUtc\":\"2024-01-01T00:00:00+00:00\"}")]
    public async Task GetCatalogAsyncReturnsNullForInvalidOrUnusablePayload(string payload)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, payload);

        CatalogSnapshot? catalog = await CacheStore.GetCatalogAsync(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);

        Assert.Null(catalog);
    }

    [Fact]
    public async Task GetCatalogAsyncReturnsNullWhenCacheFileIsLocked()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
                "bookId": "1045928363",
                "metadata": {
                    "bookId": "1045928363",
                    "title": "Title",
                    "author": "Author",
                    "estimatedWordCount": 123456
                },
                "volumes": [],
                "fetchedAtUtc": "2024-01-01T00:00:00+00:00",
                "cacheScope": {
                    "kind": "Anonymous"
                }
            }
            """);

        using FileStream _ = new(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        CatalogSnapshot? catalog = await CacheStore.GetCatalogAsync(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);

        Assert.Null(catalog);
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForNullParagraphPayload()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
                "chapterId": "1",
                "paragraphs": null,
                "isPreview": false,
                "catalogWordCount": 100
            }
            """);

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);

        Assert.Null(chapter);
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForMissingParagraphPayload()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
                "chapterId": "1",
                "isPreview": false,
                "catalogWordCount": 100
            }
            """);

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);

        Assert.Null(chapter);
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullWhenCacheFileIsLocked()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
                "chapterId": "1",
                "paragraphs": ["Paragraph 1"],
                "isPreview": false,
                "catalogWordCount": 100
            }
            """);

        using FileStream _ = new(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);

        Assert.Null(chapter);
    }

    [Fact]
    public async Task SaveCatalogAsyncPersistsCatalogChapterAccessState()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        CatalogSnapshot catalog = new(
            "1045928363",
            new BookMetadata("1045928363", "Title", "Author", 123456),
            [
                new VolumeDescriptor(
                    "VIP Volume",
                    IsVip: true,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Chapter One",
                            "https://www.qidian.com/chapter/1045928363/1/",
                            true,
                            1000,
                            CatalogChapterAccessState.PurchaseRequired),
                    ]),
            ],
            DateTimeOffset.UtcNow);

        await CacheStore.SaveCatalogAsync(root, catalog, CancellationToken.None);
        CatalogSnapshot? roundTripped = await CacheStore.GetCatalogAsync(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);

        Assert.NotNull(roundTripped);
        Assert.Equal(
            CatalogChapterAccessState.PurchaseRequired,
            roundTripped.Volumes[0].Chapters[0].CatalogAccessState);
        Assert.Equal(CatalogCacheScope.Anonymous, roundTripped.CacheScope);
    }

    [Fact]
    public async Task SaveCatalogAsyncPersistsCatalogCacheScope()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        CatalogCacheScope scope = CatalogCacheScope.ForValidatedUser("tester");
        CatalogSnapshot catalog = new(
            "1045928363",
            new BookMetadata("1045928363", "Title", "Author", 123456),
            [],
            DateTimeOffset.UtcNow,
            scope);

        await CacheStore.SaveCatalogAsync(root, catalog, CancellationToken.None);
        CatalogSnapshot? roundTripped = await CacheStore.GetCatalogAsync(
            root,
            "1045928363",
            scope,
            CancellationToken.None);

        Assert.NotNull(roundTripped);
        Assert.Equal(scope, roundTripped.CacheScope);
    }

    [Fact]
    public async Task GetCatalogAsyncRejectsCatalogWhenStoredScopeDoesNotMatchRequestedScope()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = temporaryDirectory.FullPath;
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
                "bookId": "1045928363",
                "metadata": {
                    "bookId": "1045928363",
                    "title": "Title",
                    "author": "Author",
                    "estimatedWordCount": 123456
                },
                "volumes": [],
                "fetchedAtUtc": "2024-01-01T00:00:00+00:00",
                "cacheScope": {
                    "kind": "ValidatedUser",
                    "userName": "tester"
                }
            }
            """);

        CatalogSnapshot? catalog = await CacheStore.GetCatalogAsync(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);

        Assert.Null(catalog);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string FullPath { get; } = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
    }
}
