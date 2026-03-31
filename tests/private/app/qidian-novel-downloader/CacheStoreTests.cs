using Hcoona.QidianNovelDownloader.Cache;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class CacheStoreTests
{
    [Fact]
    public void ClearCatalogOnlyRemovesCatalogAndKeepsChapterCache()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string bookId = "1045928363";
        string catalogPath = AppPaths.GetCatalogCachePath(root, bookId);
        string chapterPath = AppPaths.GetChapterCachePath(root, bookId, "1");
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(chapterPath)!);
        File.WriteAllText(catalogPath, "{}");
        File.WriteAllText(chapterPath, "{}");

        int removed = CacheStore.Clear(root, bookId, catalogOnly: true);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(catalogPath));
        Assert.True(File.Exists(chapterPath));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void ClearGlobalReturnsZeroForEmptyDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        int removed = CacheStore.Clear(root, bookId: null, catalogOnly: false);

        Assert.Equal(0, removed);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForInvalidJson()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, "{ invalid json");

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);

        Assert.Null(chapter);
        Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("{\"bookId\":\"1045928363\"")]
    [InlineData("{\"bookId\":\"1045928363\",\"metadata\":null,\"volumes\":[],\"fetchedAtUtc\":\"2024-01-01T00:00:00+00:00\"}")]
    public async Task GetCatalogAsyncReturnsNullForInvalidOrUnusablePayload(string payload)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string cachePath = AppPaths.GetCatalogCachePath(root, "1045928363");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, payload);

        CatalogSnapshot? catalog = await CacheStore.GetCatalogAsync(
            root,
            "1045928363",
            CancellationToken.None);

        Assert.Null(catalog);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForNullParagraphPayload()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForMissingParagraphPayload()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
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
        Directory.Delete(root, recursive: true);
    }
}
