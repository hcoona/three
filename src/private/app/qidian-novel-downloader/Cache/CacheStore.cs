using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hcoona.QidianNovelDownloader.Serialization;

namespace Hcoona.QidianNovelDownloader.Cache;

internal enum ChapterCacheExpectedState
{
    PresentOrAbsent,
    Absent,
}

internal static class CacheStore
{
    private static readonly ConcurrentDictionary<string, long> ClearGenerations =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly AsyncLocal<Func<string, CancellationToken, Task>?>
        BeforeChapterCacheCommitHook = new();

    private static readonly AsyncLocal<Func<string, CancellationToken, Task>?>
        BeforeCatalogCacheCommitHook = new();

    private static readonly AsyncLocal<Action<string>?> BeforeDirectoryEnumerationHook = new();

    private static readonly AsyncLocal<Action<string>?> BeforeDirectoryDeleteHook = new();

    private static readonly AsyncLocal<Action<string>?> BeforeClearGenerationLockOpenHook = new();

    private static readonly AsyncLocal<Action<string>?> BeforeClearGenerationFileOperationHook =
        new();

    private static readonly AsyncLocal<Action<string>?> BeforeCacheTemporaryWriteHook = new();

    internal static Func<string, CancellationToken, Task>? BeforeChapterCacheCommitForTests
    {
        get => BeforeChapterCacheCommitHook.Value;
        set => BeforeChapterCacheCommitHook.Value = value;
    }

    internal static Func<string, CancellationToken, Task>? BeforeCatalogCacheCommitForTests
    {
        get => BeforeCatalogCacheCommitHook.Value;
        set => BeforeCatalogCacheCommitHook.Value = value;
    }

    internal static Action<string>? BeforeDirectoryEnumerationForTests
    {
        get => BeforeDirectoryEnumerationHook.Value;
        set => BeforeDirectoryEnumerationHook.Value = value;
    }

    internal static Action<string>? BeforeClearGenerationLockOpenForTests
    {
        get => BeforeClearGenerationLockOpenHook.Value;
        set => BeforeClearGenerationLockOpenHook.Value = value;
    }

    internal static Action<string>? BeforeDirectoryDeleteForTests
    {
        get => BeforeDirectoryDeleteHook.Value;
        set => BeforeDirectoryDeleteHook.Value = value;
    }

    internal static Action<string>? BeforeClearGenerationFileOperationForTests
    {
        get => BeforeClearGenerationFileOperationHook.Value;
        set => BeforeClearGenerationFileOperationHook.Value = value;
    }

    internal static Action<string>? BeforeCacheTemporaryWriteForTests
    {
        get => BeforeCacheTemporaryWriteHook.Value;
        set => BeforeCacheTemporaryWriteHook.Value = value;
    }

    public static async Task<CatalogSnapshot?> GetCatalogAsync(
        string cacheRoot,
        string bookId,
        CatalogCacheScope scope,
        CancellationToken cancellationToken)
    {
        string cachePath = AppPaths.GetCatalogCachePath(cacheRoot, bookId, scope);
        if (!SafeCacheFileExists(cacheRoot, cachePath))
        {
            return null;
        }

        return await ReadCacheAsync(
            cachePath,
            AppJsonSerializerContext.Default.CatalogSnapshot,
            catalog => IsUsableCatalog(catalog, scope),
            cancellationToken,
            cacheRoot);
    }

    public static async Task SaveCatalogAsync(
        string cacheRoot,
        CatalogSnapshot catalog,
        CancellationToken cancellationToken)
    {
        long clearGeneration = GetClearGeneration(cacheRoot);
        await TrySaveCatalogIfClearGenerationUnchangedAsync(
            cacheRoot,
            catalog,
            clearGeneration,
            cancellationToken);
    }

    internal static async Task<bool> TrySaveCatalogIfClearGenerationUnchangedAsync(
        string cacheRoot,
        CatalogSnapshot catalog,
        long? expectedClearGeneration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        if (expectedClearGeneration is not null
            && GetClearGeneration(normalizedCacheRoot) != expectedClearGeneration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        string cachePath = AppPaths.GetCatalogCachePath(
            normalizedCacheRoot,
            catalog.BookId,
            catalog.CacheScope);
        string cacheDirectory = Path.GetDirectoryName(cachePath)!;
        string? temporaryPath = null;
        Exception? pendingException = null;
        try
        {
            try
            {
                using (AcquireClearGenerationLock(normalizedCacheRoot, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (expectedClearGeneration is not null
                        && GetClearGeneration(normalizedCacheRoot) != expectedClearGeneration)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return false;
                    }

                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    Directory.CreateDirectory(cacheDirectory);
                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    temporaryPath = CreateCacheTemporaryPath(normalizedCacheRoot);
                    BeforeCacheTemporaryWriteForTests?.Invoke(temporaryPath);
                    EnsureSafeDestinationDirectory(
                        normalizedCacheRoot,
                        Path.GetDirectoryName(temporaryPath)!);
                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    EnsureSafeDestinationDirectory(
                        normalizedCacheRoot,
                        Path.GetDirectoryName(temporaryPath)!);
                    EnsureNotReparsePathIfExists(temporaryPath);
                    await using (FileStream stream = File.Create(temporaryPath))
                    {
                        await JsonSerializer.SerializeAsync(
                            stream,
                            catalog,
                            AppJsonSerializerContext.Default.CatalogSnapshot,
                            cancellationToken);
                    }

                    if (BeforeCatalogCacheCommitForTests is { } beforeCommit)
                    {
                        await beforeCommit(cachePath, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (expectedClearGeneration is not null
                        && GetClearGeneration(normalizedCacheRoot) != expectedClearGeneration)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return false;
                    }

                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    EnsureNotReparsePathIfExists(cachePath);
                    File.Move(temporaryPath, cachePath, overwrite: true);
                }
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                && temporaryPath is not null
                && IsBenignCommitRaceException(ex, temporaryPath, cachePath, null))
            {
                pendingException = ex;
                ThrowIfCancellationRequestedPreservingPending(
                    cancellationToken,
                    ref pendingException);
                return false;
            }
            catch (Exception ex)
            {
                pendingException = ex;
                throw;
            }
        }
        finally
        {
            if (temporaryPath is not null)
            {
                DeleteTemporaryFilePreservingCancellation(
                    temporaryPath,
                    normalizedCacheRoot,
                    cancellationToken,
                    pendingException);
            }
        }
    }

    public static async Task<ChapterCacheEntry?> GetChapterAsync(
        string cacheRoot,
        string bookId,
        string chapterId,
        CancellationToken cancellationToken)
    {
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        string cachePath = AppPaths.GetChapterCachePath(normalizedCacheRoot, bookId, chapterId);
        if (!SafeChapterCacheFileExists(normalizedCacheRoot, bookId, cachePath))
        {
            return null;
        }

        EnsureChapterCachePathUnderChapterDirectory(normalizedCacheRoot, bookId, cachePath);
        return await ReadCacheAsync(
            cachePath,
            AppJsonSerializerContext.Default.ChapterCacheEntry,
            chapter => chapter is { Paragraphs: not null }
                && string.Equals(chapter.ChapterId, chapterId, StringComparison.Ordinal),
            cancellationToken,
            normalizedCacheRoot);
    }

    public static async Task<ChapterCacheProbe?> GetChapterProbeAsync(
        string cacheRoot,
        string bookId,
        string chapterId,
        CancellationToken cancellationToken)
    {
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        string cachePath = AppPaths.GetChapterCachePath(normalizedCacheRoot, bookId, chapterId);
        if (!SafeChapterCacheFileExists(normalizedCacheRoot, bookId, cachePath))
        {
            return null;
        }

        EnsureChapterCachePathUnderChapterDirectory(normalizedCacheRoot, bookId, cachePath);
        return await ReadCacheAsync(
            cachePath,
            AppJsonSerializerContext.Default.ChapterCacheProbe,
            chapter => chapter is { Paragraphs: not null }
                && string.Equals(chapter.ChapterId, chapterId, StringComparison.Ordinal),
            cancellationToken,
            normalizedCacheRoot);
    }

    public static async Task SaveChapterAsync(
        string cacheRoot,
        string bookId,
        ChapterCacheEntry chapter,
        CancellationToken cancellationToken)
    {
        long clearGeneration = GetClearGeneration(cacheRoot);
        await TrySaveChapterIfClearGenerationUnchangedAsync(
            cacheRoot,
            bookId,
            chapter,
            ChapterCacheExpectedState.PresentOrAbsent,
            clearGeneration,
            cancellationToken);
    }

    internal static long GetClearGeneration(string cacheRoot)
    {
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        long durableGeneration = ReadDurableClearGeneration(normalizedCacheRoot);
        return ClearGenerations.AddOrUpdate(
            normalizedCacheRoot,
            durableGeneration,
            (_, generation) => Math.Max(generation, durableGeneration));
    }

    internal static string GetClearGenerationFilePath(string cacheRoot)
        => Path.Combine(
            Path.GetDirectoryName(NormalizeCacheRoot(cacheRoot))!,
            AppConstants.ClearGenerationFileName);

    internal static void IncrementClearGenerationBypassingMemoryForTests(string cacheRoot)
    {
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        using (AcquireClearGenerationLock(normalizedCacheRoot, CancellationToken.None))
        {
            long durableGeneration = ReadDurableClearGeneration(normalizedCacheRoot);
            WriteDurableClearGeneration(normalizedCacheRoot, durableGeneration + 1);
        }
    }

    internal static async Task<bool> TrySaveChapterIfClearGenerationUnchangedAsync(
        string cacheRoot,
        string bookId,
        ChapterCacheEntry chapter,
        ChapterCacheExpectedState expectedState,
        CancellationToken cancellationToken)
        => await TrySaveChapterIfClearGenerationUnchangedAsync(
            cacheRoot,
            bookId,
            chapter,
            expectedState,
            expectedClearGeneration: null,
            cancellationToken);

    internal static async Task<bool> TrySaveChapterIfClearGenerationUnchangedAsync(
        string cacheRoot,
        string bookId,
        ChapterCacheEntry chapter,
        ChapterCacheExpectedState expectedState,
        long? expectedClearGeneration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        if (expectedClearGeneration is not null
            && GetClearGeneration(normalizedCacheRoot) != expectedClearGeneration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        string cachePath = AppPaths.GetChapterCachePath(
            normalizedCacheRoot,
            bookId,
            chapter.ChapterId);
        EnsureChapterCachePathUnderChapterDirectory(normalizedCacheRoot, bookId, cachePath);
        string cacheDirectory = Path.GetDirectoryName(cachePath)!;
        EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
        if (expectedState == ChapterCacheExpectedState.Absent && File.Exists(cachePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }

        string? temporaryPath = null;
        Exception? pendingException = null;
        try
        {
            try
            {
                using (AcquireClearGenerationLock(normalizedCacheRoot, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (expectedClearGeneration is not null
                        && GetClearGeneration(normalizedCacheRoot) != expectedClearGeneration)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return false;
                    }

                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    if (expectedState == ChapterCacheExpectedState.Absent && File.Exists(cachePath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return false;
                    }

                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    Directory.CreateDirectory(cacheDirectory);
                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    temporaryPath = CreateCacheTemporaryPath(normalizedCacheRoot);
                    BeforeCacheTemporaryWriteForTests?.Invoke(temporaryPath);
                    EnsureSafeDestinationDirectory(
                        normalizedCacheRoot,
                        Path.GetDirectoryName(temporaryPath)!);
                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    EnsureSafeDestinationDirectory(
                        normalizedCacheRoot,
                        Path.GetDirectoryName(temporaryPath)!);
                    EnsureNotReparsePathIfExists(temporaryPath);
                    await using (FileStream stream = File.Create(temporaryPath))
                    {
                        await JsonSerializer.SerializeAsync(
                            stream,
                            chapter,
                            AppJsonSerializerContext.Default.ChapterCacheEntry,
                            cancellationToken);
                    }

                    if (BeforeChapterCacheCommitForTests is { } beforeCommit)
                    {
                        await beforeCommit(cachePath, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    if (expectedClearGeneration is not null
                        && GetClearGeneration(normalizedCacheRoot) != expectedClearGeneration)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return false;
                    }

                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    if (expectedState == ChapterCacheExpectedState.Absent && File.Exists(cachePath))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        return false;
                    }

                    EnsureSafeDestinationDirectory(normalizedCacheRoot, cacheDirectory);
                    EnsureNotReparsePathIfExists(cachePath);
                    File.Move(
                        temporaryPath,
                        cachePath,
                        overwrite: expectedState != ChapterCacheExpectedState.Absent);
                }
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                && temporaryPath is not null
                && IsBenignCommitRaceException(ex, temporaryPath, cachePath, expectedState))
            {
                pendingException = ex;
                ThrowIfCancellationRequestedPreservingPending(
                    cancellationToken,
                    ref pendingException);
                return false;
            }
            catch (Exception ex)
            {
                pendingException = ex;
                throw;
            }
        }
        finally
        {
            if (temporaryPath is not null)
            {
                DeleteTemporaryFilePreservingCancellation(
                    temporaryPath,
                    normalizedCacheRoot,
                    cancellationToken,
                    pendingException);
            }
        }
    }

    public static bool IsCatalogFresh(
        CatalogSnapshot catalog,
        int ttlHours,
        TimeProvider timeProvider)
        => catalog.FetchedAtUtc.AddHours(ttlHours) > timeProvider.GetUtcNow();

    public static int CountCachedChapters(string cacheRoot, string bookId)
    {
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        string chaptersDirectory = AppPaths.GetChapterCacheDirectory(
            normalizedCacheRoot,
            bookId);
        try
        {
            EnsureSafeCacheReadPath(normalizedCacheRoot, chaptersDirectory);
            return Directory.Exists(chaptersDirectory)
                ? Directory.EnumerateFiles(
                    chaptersDirectory,
                    "*.json",
                    SearchOption.TopDirectoryOnly)
                    .Count(IsSafeCacheFileForRead)
                : 0;
        }
        catch (Exception ex) when (IsCacheMissException(ex))
        {
            return 0;
        }
    }

    public static int Clear(string cacheRoot, string? bookId, bool catalogOnly)
    {
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        string? targetDirectory = bookId is null
            ? null
            : catalogOnly
                ? AppPaths.GetCatalogCacheDirectory(normalizedCacheRoot, bookId)
                : AppPaths.GetBookCacheDirectory(normalizedCacheRoot, bookId);
        if (targetDirectory is not null)
        {
            EnsureClearTargetUnderCacheRoot(normalizedCacheRoot, targetDirectory);
        }

        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        using (AcquireClearGenerationLock(normalizedCacheRoot, CancellationToken.None))
        {
            EnsureNoReparsePointInExistingPath(normalizedCacheRoot);

            if (bookId is null)
            {
                string[]? catalogDirectories = null;
                if (catalogOnly && Directory.Exists(normalizedCacheRoot))
                {
                    catalogDirectories = PreflightAllCatalogs(normalizedCacheRoot);
                }
                else if (Directory.Exists(normalizedCacheRoot))
                {
                    EnsureSafeDirectorySubtreeForDeletion(normalizedCacheRoot);
                }

                if (!Directory.Exists(normalizedCacheRoot))
                {
                    IncrementClearGenerationUnderLock(normalizedCacheRoot);
                    return 0;
                }

                IncrementClearGenerationUnderLock(normalizedCacheRoot);
                EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
                return catalogOnly
                    ? ClearCatalogDirectories(
                        catalogDirectories ?? PreflightAllCatalogs(normalizedCacheRoot))
                    : DeleteDirectory(normalizedCacheRoot);
            }

            string clearTargetDirectory = targetDirectory!;
            EnsureNoReparsePointInExistingPath(clearTargetDirectory);
            if (Directory.Exists(clearTargetDirectory))
            {
                EnsureSafeDirectorySubtreeForDeletion(clearTargetDirectory);
            }

            IncrementClearGenerationUnderLock(normalizedCacheRoot);
            EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
            return DeleteDirectory(clearTargetDirectory);
        }
    }

    internal static void EnsureNoReparsePointInExistingCachePath(string cacheRoot)
        => EnsureNoReparsePointInExistingPath(NormalizeCacheRoot(cacheRoot));

    private static string NormalizeCacheRoot(string cacheRoot)
        => Path.GetFullPath(cacheRoot).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

    private static void EnsureNoReparsePointInExistingPath(string path)
    {
        for (DirectoryInfo? directory = new(path);
            directory is not null;
            directory = directory.Parent)
        {
            try
            {
                if ((File.GetAttributes(directory.FullName) & FileAttributes.ReparsePoint) == 0)
                {
                    continue;
                }

                throw new IOException(
                    $"Refusing to access cache through reparse point directory: '{directory.FullName}'.");
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
            }
        }
    }

    private static long ReadDurableClearGeneration(string normalizedCacheRoot)
    {
        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        string generationPath = GetClearGenerationFilePath(normalizedCacheRoot);
        try
        {
            BeforeClearGenerationFileOperationForTests?.Invoke(generationPath);
            EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
            EnsureNotReparsePathIfExists(generationPath);
            string value = File.ReadAllText(generationPath);
            if (long.TryParse(value, out long generation) && generation >= 0)
            {
                return generation;
            }

            throw new InvalidDataException(
                $"The cache clear generation file is invalid: '{generationPath}'.");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return 0;
        }
    }

    private static void IncrementClearGenerationUnderLock(string normalizedCacheRoot)
    {
        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        long durableGeneration = ReadDurableClearGeneration(normalizedCacheRoot);
        long currentGeneration = ClearGenerations.AddOrUpdate(
            normalizedCacheRoot,
            durableGeneration,
            (_, generation) => Math.Max(generation, durableGeneration));
        long nextGeneration = currentGeneration + 1;
        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        WriteDurableClearGeneration(normalizedCacheRoot, nextGeneration);
        ClearGenerations.AddOrUpdate(
            normalizedCacheRoot,
            nextGeneration,
            (_, generation) => Math.Max(generation, nextGeneration));
    }

    private static ClearGenerationLock AcquireClearGenerationLock(
        string cacheRoot,
        CancellationToken cancellationToken)
    {
        string normalizedCacheRoot = NormalizeCacheRoot(cacheRoot);
        string lockPath = GetClearGenerationFilePath(normalizedCacheRoot) + ".lock";
        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        BeforeClearGenerationLockOpenForTests?.Invoke(normalizedCacheRoot);
        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                BeforeClearGenerationFileOperationForTests?.Invoke(lockPath);
                EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
                EnsureNotReparsePathIfExists(lockPath);
                return new ClearGenerationLock(
                    new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None));
            }
            catch (IOException exception) when (IsLockContentionException(exception))
            {
                Thread.Sleep(10);
            }
        }
    }

    private static bool IsLockContentionException(IOException exception)
        => (exception.HResult & 0xFFFF) is 32 or 33;

    private sealed class ClearGenerationLock(FileStream stream) : IDisposable
    {
        public void Dispose()
            => stream.Dispose();
    }

    private static void WriteDurableClearGeneration(
        string normalizedCacheRoot,
        long generation)
    {
        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        string generationPath = GetClearGenerationFilePath(normalizedCacheRoot);
        string generationDirectory = Path.GetDirectoryName(generationPath)!;
        Directory.CreateDirectory(generationDirectory);
        EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
        string temporaryPath = generationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            BeforeClearGenerationFileOperationForTests?.Invoke(temporaryPath);
            EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
            EnsureNotReparsePathIfExists(temporaryPath);
            File.WriteAllText(temporaryPath, generation.ToString());
            BeforeClearGenerationFileOperationForTests?.Invoke(generationPath);
            EnsureNoReparsePointInExistingPath(normalizedCacheRoot);
            EnsureNotReparsePathIfExists(generationPath);
            File.Move(temporaryPath, generationPath, overwrite: true);
        }
        finally
        {
            DeleteFileIfExists(temporaryPath, normalizedCacheRoot);
        }
    }

    private static string CreateCacheTemporaryPath(string normalizedCacheRoot)
    {
        string stagingDirectory = Path.Combine(normalizedCacheRoot, ".staging");
        EnsureSafeDestinationDirectory(normalizedCacheRoot, stagingDirectory);
        Directory.CreateDirectory(stagingDirectory);
        EnsureSafeDestinationDirectory(normalizedCacheRoot, stagingDirectory);
        return Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".tmp");
    }

    private static void DeleteFileIfExists(string path, string? safetyRoot = null)
    {
        try
        {
            if (safetyRoot is not null)
            {
                EnsureNoReparsePointInExistingPath(safetyRoot);
                EnsureSafeDestinationDirectoryIfUnderRoot(safetyRoot, Path.GetDirectoryName(path)!);
            }

            EnsureNotReparsePathIfExists(path);
            if (File.Exists(path))
            {
                if (safetyRoot is not null)
                {
                    EnsureNoReparsePointInExistingPath(safetyRoot);
                    EnsureSafeDestinationDirectoryIfUnderRoot(
                        safetyRoot,
                        Path.GetDirectoryName(path)!);
                }

                EnsureNotReparsePathIfExists(path);
                File.Delete(path);
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException) when (safetyRoot is null && !File.Exists(path))
        {
        }
    }

    private static void DeleteTemporaryFilePreservingCancellation(
        string path,
        string safetyRoot,
        CancellationToken cancellationToken,
        Exception? pendingException = null)
    {
        try
        {
            DeleteFileIfExists(path, safetyRoot);
        }
        catch (Exception) when (pendingException is OperationCanceledException)
        {
            ExceptionDispatchInfo.Capture(pendingException).Throw();
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested
            && pendingException is null)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private static void ThrowIfCancellationRequestedPreservingPending(
        CancellationToken cancellationToken,
        ref Exception? pendingException)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException exception)
        {
            pendingException = exception;
            throw;
        }
    }

    private static string[] PreflightAllCatalogs(string cacheRoot)
    {
        string[] catalogDirectories = EnumerateCatalogDirectoriesSafely(cacheRoot).ToArray();
        foreach (string catalogDirectory in catalogDirectories)
        {
            EnsureSafeDirectorySubtreeForDeletion(catalogDirectory);
        }

        return catalogDirectories;
    }

    private static int ClearCatalogDirectories(string[] catalogDirectories)
    {
        int removed = 0;
        foreach (string catalogDirectory in catalogDirectories)
        {
            removed += DeleteDirectory(catalogDirectory);
        }

        return removed;
    }

    private static IEnumerable<string> EnumerateCatalogDirectoriesSafely(string cacheRoot)
    {
        string normalizedRoot = NormalizeCacheRoot(cacheRoot);
        Stack<string> pending = new([normalizedRoot]);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            EnsureSafeDirectoryForTraversal(current);
            BeforeDirectoryEnumerationForTests?.Invoke(current);
            EnsureSafeDirectoryForTraversal(current);
            foreach (string directory in Directory.EnumerateDirectories(current).ToArray())
            {
                if (!IsPathUnderRoot(directory, normalizedRoot))
                {
                    throw new IOException(
                        $"Refusing to clear cache directory outside cache root: '{directory}'.");
                }

                if (IsReparseDirectory(directory))
                {
                    throw new IOException(
                        $"Refusing to clear cache through reparse point directory: '{directory}'.");
                }

                if (string.Equals(
                    Path.GetFileName(directory),
                    AppConstants.CatalogsDirectoryName,
                    StringComparison.Ordinal))
                {
                    yield return directory;
                    continue;
                }

                pending.Push(directory);
            }
        }
    }

    private static int DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        EnsureSafeDirectoryForTraversal(path);

        int removed = 0;
        BeforeDirectoryEnumerationForTests?.Invoke(path);
        EnsureSafeDirectoryForTraversal(path);
        EnsureNoReparsePointInExistingPath(path);
        foreach (string file in Directory.EnumerateFiles(path).ToArray())
        {
            EnsureNoReparsePointInExistingPath(path);
            EnsureNotReparsePath(file);
            EnsureNoReparsePointInExistingPath(path);
            File.Delete(file);
            removed++;
        }

        EnsureSafeDirectoryForTraversal(path);
        EnsureNoReparsePointInExistingPath(path);
        foreach (string directory in Directory.EnumerateDirectories(path).ToArray())
        {
            EnsureSafeDirectoryForTraversal(directory);
            removed += DeleteDirectory(directory);
        }

        EnsureSafeDirectoryForTraversal(path);
        BeforeDirectoryDeleteForTests?.Invoke(path);
        EnsureSafeDirectoryForTraversal(path);
        EnsureNoReparsePointInExistingPath(path);
        Directory.Delete(path);
        return removed;
    }

    internal static bool IsPathUnderRoot(string path, string normalizedRoot)
    {
        string normalizedPath = NormalizeCacheRoot(path);
        StringComparison comparison = GetPathComparison();
        return string.Equals(normalizedPath, normalizedRoot, comparison)
            || normalizedPath.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison);
    }

    private static void EnsureClearTargetUnderCacheRoot(
        string normalizedCacheRoot,
        string targetDirectory)
    {
        if (!IsPathUnderRoot(targetDirectory, normalizedCacheRoot))
        {
            throw new IOException(
                $"Refusing to clear cache directory outside cache root: '{targetDirectory}'.");
        }
    }

    internal static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool IsReparseDirectory(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void EnsureSafeDirectoryForTraversal(string path)
    {
        EnsureNoReparsePointInExistingPath(path);
        if (IsReparseDirectory(path))
        {
            throw new IOException(
                $"Refusing to clear cache through reparse point directory: '{path}'.");
        }
    }

    private static void EnsureSafeDirectorySubtreeForDeletion(string path)
    {
        EnsureSafeDirectoryForTraversal(path);
        foreach (string file in Directory.EnumerateFiles(path).ToArray())
        {
            EnsureNotReparsePath(file);
        }

        foreach (string directory in Directory.EnumerateDirectories(path).ToArray())
        {
            EnsureSafeDirectorySubtreeForDeletion(directory);
        }
    }

    private static void EnsureSafeDestinationDirectory(
        string cacheRoot,
        string destinationDirectory)
    {
        string normalizedRoot = NormalizeCacheRoot(cacheRoot);
        string normalizedDestination = NormalizeCacheRoot(destinationDirectory);
        if (!IsPathUnderRoot(normalizedDestination, normalizedRoot))
        {
            throw new IOException(
                $"Refusing to write cache directory outside cache root: '{destinationDirectory}'.");
        }

        EnsureNoReparsePointInExistingPath(normalizedRoot);
        string relativePath = Path.GetRelativePath(normalizedRoot, normalizedDestination);
        string current = normalizedRoot;
        if (relativePath == ".")
        {
            if (Directory.Exists(current))
            {
                EnsureSafeDirectoryForTraversal(current);
            }

            return;
        }

        foreach (string component in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (Directory.Exists(current))
            {
                EnsureSafeDirectoryForTraversal(current);
            }
        }
    }

    private static void EnsureChapterCachePathUnderChapterDirectory(
        string normalizedCacheRoot,
        string bookId,
        string cachePath)
    {
        string chaptersDirectory = AppPaths.GetChapterCacheDirectory(normalizedCacheRoot, bookId);
        string normalizedChaptersDirectory = NormalizeCacheRoot(chaptersDirectory);
        string normalizedCachePath = NormalizeCacheRoot(cachePath);
        if (!IsPathUnderRoot(normalizedCachePath, normalizedChaptersDirectory))
        {
            throw new IOException(
                "Refusing to write chapter cache file outside the book chapter cache directory: "
                + $"'{cachePath}'.");
        }
    }

    private static void EnsureSafeDestinationDirectoryIfUnderRoot(
        string cacheRoot,
        string destinationDirectory)
    {
        string normalizedRoot = NormalizeCacheRoot(cacheRoot);
        string normalizedDestination = NormalizeCacheRoot(destinationDirectory);
        if (IsPathUnderRoot(normalizedDestination, normalizedRoot))
        {
            EnsureSafeDestinationDirectory(normalizedRoot, normalizedDestination);
        }
    }

    private static void EnsureNotReparsePath(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"Refusing to clear cache reparse point path: '{path}'.");
        }
    }

    private static void EnsureNotReparsePathIfExists(string path)
    {
        try
        {
            EnsureNotReparsePath(path);
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static void EnsureSafeCacheReadPath(string path)
    {
        EnsureNoReparsePointInExistingPath(path);
        EnsureNotReparsePathIfExists(path);
    }

    private static void EnsureSafeCacheReadPath(string cacheRoot, string path)
    {
        string normalizedRoot = NormalizeCacheRoot(cacheRoot);
        string normalizedPath = Path.GetFullPath(path);
        if (!IsPathUnderRoot(normalizedPath, normalizedRoot))
        {
            throw new IOException(
                $"Refusing to read cache file outside cache root: '{path}'.");
        }

        EnsureSafeDestinationDirectory(normalizedRoot, Path.GetDirectoryName(normalizedPath)!);
        EnsureNotReparsePathIfExists(normalizedPath);
    }

    private static bool SafeCacheFileExists(string cachePath)
    {
        try
        {
            EnsureSafeCacheReadPath(cachePath);
            return File.Exists(cachePath);
        }
        catch (Exception ex) when (IsCacheMissException(ex))
        {
            return false;
        }
    }

    private static bool SafeCacheFileExists(string cacheRoot, string cachePath)
    {
        try
        {
            EnsureSafeCacheReadPath(cacheRoot, cachePath);
            return File.Exists(cachePath);
        }
        catch (Exception ex) when (IsCacheMissException(ex))
        {
            return false;
        }
    }

    private static bool SafeChapterCacheFileExists(
        string normalizedCacheRoot,
        string bookId,
        string cachePath)
    {
        try
        {
            EnsureChapterCachePathUnderChapterDirectory(
                normalizedCacheRoot,
                bookId,
                cachePath);
            EnsureSafeCacheReadPath(normalizedCacheRoot, cachePath);
            return File.Exists(cachePath);
        }
        catch (Exception ex) when (IsCacheMissException(ex))
        {
            return false;
        }
    }

    private static bool IsSafeCacheFileForRead(string cachePath)
    {
        try
        {
            EnsureSafeCacheReadPath(cachePath);
            return true;
        }
        catch (Exception ex) when (IsCacheMissException(ex))
        {
            return false;
        }
    }

    private static async Task<T?> ReadCacheAsync<T>(
        string cachePath,
        JsonTypeInfo<T> typeInfo,
        Func<T?, bool> isUsable,
        CancellationToken cancellationToken,
        string? cacheRoot = null)
        where T : class
    {
        try
        {
            if (cacheRoot is null)
            {
                EnsureSafeCacheReadPath(cachePath);
            }
            else
            {
                EnsureSafeCacheReadPath(cacheRoot, cachePath);
            }

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

    private static bool IsBenignCommitRaceException(
        Exception ex,
        string temporaryPath,
        string cachePath,
        ChapterCacheExpectedState? expectedState)
    {
        if (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return !File.Exists(temporaryPath)
                || !Directory.Exists(Path.GetDirectoryName(cachePath)!);
        }

        return ex is IOException
            && expectedState == ChapterCacheExpectedState.Absent
            && File.Exists(cachePath);
    }

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
