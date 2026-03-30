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
}
