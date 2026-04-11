using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hcoona.QidianNovelDownloader.Serialization;

namespace Hcoona.QidianNovelDownloader.Cache;

internal static class CacheStore
{
    public static async Task<CatalogSnapshot?> GetCatalogAsync(
        string cacheRoot,
        string bookId,
        CatalogCacheScope scope,
        CancellationToken cancellationToken)
    {
        string cachePath = AppPaths.GetCatalogCachePath(cacheRoot, bookId, scope);
        if (!File.Exists(cachePath))
        {
            return null;
        }

        return await ReadCacheAsync(
            cachePath,
            AppJsonSerializerContext.Default.CatalogSnapshot,
            catalog => IsUsableCatalog(catalog, scope),
            cancellationToken);
    }

    public static async Task SaveCatalogAsync(
        string cacheRoot,
        CatalogSnapshot catalog,
        CancellationToken cancellationToken)
    {
        string cachePath = AppPaths.GetCatalogCachePath(
            cacheRoot,
            catalog.BookId,
            catalog.CacheScope);
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

        return await ReadCacheAsync(
            cachePath,
            AppJsonSerializerContext.Default.ChapterCacheEntry,
            chapter => chapter is { Paragraphs: not null },
            cancellationToken);
    }

    public static async Task<ChapterCacheProbe?> GetChapterProbeAsync(
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

        return await ReadCacheAsync(
            cachePath,
            AppJsonSerializerContext.Default.ChapterCacheProbe,
            chapter => chapter is { Paragraphs: not null },
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
            ? Directory.EnumerateFiles(
                chaptersDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly)
                .Count()
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
            string catalogDirectory = AppPaths.GetCatalogCacheDirectory(cacheRoot, bookId);
            return DeleteDirectory(catalogDirectory);
        }

        string bookDirectory = AppPaths.GetBookCacheDirectory(cacheRoot, bookId);
        return DeleteDirectory(bookDirectory);
    }

    private static int ClearAllCatalogs(string cacheRoot)
    {
        int removed = 0;
        foreach (string catalogDirectory in Directory
            .EnumerateDirectories(
                cacheRoot,
                AppConstants.CatalogsDirectoryName,
                SearchOption.AllDirectories)
            .ToArray())
        {
            removed += DeleteDirectory(catalogDirectory);
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

    private static async Task<T?> ReadCacheAsync<T>(
        string cachePath,
        JsonTypeInfo<T> typeInfo,
        Func<T?, bool> isUsable,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            await using FileStream stream = File.OpenRead(cachePath);
            T? value = await JsonSerializer.DeserializeAsync(
                stream,
                typeInfo,
                cancellationToken);
            return isUsable(value) ? value : null;
        }
        catch (Exception ex) when (IsCacheMissException(ex))
        {
            return null;
        }
    }

    private static bool IsCacheMissException(Exception ex)
        => ex is JsonException
            or FileNotFoundException
            or DirectoryNotFoundException
            or IOException
            or UnauthorizedAccessException;

    private static bool IsUsableCatalog(CatalogSnapshot? catalog, CatalogCacheScope expectedScope)
        => catalog is
        {
            BookId.Length: > 0,
            CacheScope: not null,
            Metadata:
            {
                BookId.Length: > 0,
                Title: not null,
                Author: not null,
            },
            Volumes: not null,
        }
        && catalog.CacheScope.IsUsable
        && catalog.CacheScope == expectedScope
        && catalog.Volumes.All(
            volume => volume is
            {
                Title: not null,
                Chapters: not null,
            }
            && volume.Chapters.All(
                chapter => chapter is
                {
                    ChapterId.Length: > 0,
                    Title: not null,
                    Url: not null,
                }));
}
