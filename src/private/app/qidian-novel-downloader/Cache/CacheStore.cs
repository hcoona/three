using System.Text.Json;
using Hcoona.QidianNovelDownloader.Serialization;

namespace Hcoona.QidianNovelDownloader.Cache;

internal static class CacheStore
{
    public static async Task<CatalogSnapshot?> GetCatalogAsync(
        string cacheRoot,
        string bookId,
        CancellationToken cancellationToken)
    {
        string cachePath = AppPaths.GetCatalogCachePath(cacheRoot, bookId);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(cachePath);
        return await JsonSerializer.DeserializeAsync(
            stream,
            AppJsonSerializerContext.Default.CatalogSnapshot,
            cancellationToken);
    }

    public static async Task SaveCatalogAsync(
        string cacheRoot,
        CatalogSnapshot catalog,
        CancellationToken cancellationToken)
    {
        string cachePath = AppPaths.GetCatalogCachePath(cacheRoot, catalog.BookId);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        await using FileStream stream = File.Create(cachePath);
        await JsonSerializer.SerializeAsync(
            stream,
            catalog,
            AppJsonSerializerContext.Default.CatalogSnapshot,
            cancellationToken);
    }

    public static async Task<ChapterCacheEntry?> GetChapterAsync(
        string cacheRoot,
        string bookId,
        string chapterId,
        CancellationToken cancellationToken)
    {
        string cachePath = AppPaths.GetChapterCachePath(cacheRoot, bookId, chapterId);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(cachePath);
        return await JsonSerializer.DeserializeAsync(
            stream,
            AppJsonSerializerContext.Default.ChapterCacheEntry,
            cancellationToken);
    }

    public static async Task SaveChapterAsync(
        string cacheRoot,
        string bookId,
        ChapterCacheEntry chapter,
        CancellationToken cancellationToken)
    {
        string cachePath = AppPaths.GetChapterCachePath(cacheRoot, bookId, chapter.ChapterId);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        await using FileStream stream = File.Create(cachePath);
        await JsonSerializer.SerializeAsync(
            stream,
            chapter,
            AppJsonSerializerContext.Default.ChapterCacheEntry,
            cancellationToken);
    }

    public static bool IsCatalogFresh(
        CatalogSnapshot catalog,
        int ttlHours,
        TimeProvider timeProvider)
        => catalog.FetchedAtUtc.AddHours(ttlHours) > timeProvider.GetUtcNow();

    public static int CountCachedChapters(string cacheRoot, string bookId)
    {
        string chaptersDirectory = AppPaths.GetChapterCacheDirectory(cacheRoot, bookId);
        return Directory.Exists(chaptersDirectory)
            ? Directory.EnumerateFiles(chaptersDirectory, "*.json", SearchOption.TopDirectoryOnly).Count()
            : 0;
    }

    public static int Clear(string cacheRoot, string? bookId, bool catalogOnly)
    {
        if (bookId is null)
        {
            if (!Directory.Exists(cacheRoot))
            {
                return 0;
            }

            return catalogOnly ? ClearAllCatalogs(cacheRoot) : DeleteDirectory(cacheRoot);
        }

        if (catalogOnly)
        {
            string catalogPath = AppPaths.GetCatalogCachePath(cacheRoot, bookId);
            return DeleteFileIfExists(catalogPath);
        }

        string bookDirectory = AppPaths.GetBookCacheDirectory(cacheRoot, bookId);
        return DeleteDirectory(bookDirectory);
    }

    private static int ClearAllCatalogs(string cacheRoot)
    {
        int removed = 0;
        foreach (string catalogPath in Directory.EnumerateFiles(
                     cacheRoot,
                     AppConstants.CatalogCacheFileName,
                     SearchOption.AllDirectories))
        {
            removed += DeleteFileIfExists(catalogPath);
        }

        return removed;
    }

    private static int DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        int removed = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
        Directory.Delete(path, recursive: true);
        return removed;
    }

    private static int DeleteFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        File.Delete(path);
        return 1;
    }
}
