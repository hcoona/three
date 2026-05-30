using System.Text.Json;
using Hcoona.QidianNovelDownloader.Cache;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class CacheStoreTests
{
    [Fact]
    public void ClearCatalogOnlyRemovesCatalogAndKeepsChapterCache()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
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
    public async Task TrySaveChapterIfAbsentRechecksAbsenceBeforeCommit()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        ChapterCacheEntry staleEntry = new(
            "1",
            ["stale"],
            IsPreview: false,
            100);
        ChapterCacheEntry newerEntry = new(
            "1",
            ["newer"],
            IsPreview: false,
            100);

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (cachePath, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllText(
                    cachePath,
                    """
                    {
                        "chapterId": "1",
                        "paragraphs": ["newer"],
                        "isPreview": false,
                        "catalogWordCount": 100
                    }
                    """);
                return Task.CompletedTask;
            };

            bool saved = await CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                root,
                bookId,
                staleEntry,
                ChapterCacheExpectedState.Absent,
                CancellationToken.None);

            Assert.False(saved);
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
        }

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            bookId,
            "1",
            CancellationToken.None);
        Assert.NotNull(chapter);
        Assert.Equal(["newer"], chapter.Paragraphs);

        string chapterDirectory = Path.GetDirectoryName(
            AppPaths.GetChapterCachePath(root, bookId, "1"))!;
        Assert.DoesNotContain(
            Directory.EnumerateFiles(chapterDirectory),
            path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("请登录")]
    [InlineData("需要登录")]
    [InlineData("您还未登录")]
    [InlineData("未登录")]
    [InlineData("登录后阅读")]
    [InlineData("请登录后继续阅读")]
    public async Task GetChapterAsyncRejectsCachedChapterContainingInterstitialMarkerText(
        string markerText)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        const string BookId = "1045928363";
        const string ChapterId = "1";
        await CacheStore.SaveChapterAsync(
            root,
            BookId,
            new ChapterCacheEntry(
                ChapterId,
                ["legit paragraph", markerText],
                IsPreview: false,
                100),
            CancellationToken.None);

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            BookId,
            ChapterId,
            CancellationToken.None);

        Assert.Null(chapter);
    }

    [Fact]
    public async Task TrySaveChapterRejectsTraversalChapterIdsThatTargetAnotherBookCache()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string sourceBookId = "1045928363";
        string targetBookId = "1045928364";
        string targetChapterId = "1";
        string targetChapterPath = AppPaths.GetChapterCachePath(
            root,
            targetBookId,
            targetChapterId);
        const string TargetContent = "target chapter content";
        Directory.CreateDirectory(Path.GetDirectoryName(targetChapterPath)!);
        File.WriteAllText(targetChapterPath, TargetContent);

        foreach (char separator in new[]
        {
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
        }.Distinct())
        {
            string maliciousChapterId = string.Join(
                separator.ToString(),
                new[]
                {
                    "..",
                    "..",
                    targetBookId,
                    AppConstants.ChaptersDirectoryName,
                    targetChapterId,
                });

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    sourceBookId,
                    new ChapterCacheEntry(maliciousChapterId, ["poison"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Equal(TargetContent, File.ReadAllText(targetChapterPath));
        }
    }

    [Fact]
    public async Task TrySaveChapterObservesDurableClearGenerationChange()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        long generation = CacheStore.GetClearGeneration(root);
        WriteClearGeneration(root, (generation + 1).ToString());

        bool saved = await CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
            root,
            "1045928363",
            new ChapterCacheEntry("1", ["stale"], IsPreview: false, 100),
            ChapterCacheExpectedState.PresentOrAbsent,
            generation,
            CancellationToken.None);

        Assert.False(saved);
        Assert.False(File.Exists(AppPaths.GetChapterCachePath(root, "1045928363", "1")));
    }

    [Fact]
    public async Task TrySaveChapterObservesCancellationBeforeGenerationMismatchReturn()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        long generation = CacheStore.GetClearGeneration(root);
        WriteClearGeneration(root, (generation + 1).ToString());
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                root,
                "1045928363",
                new ChapterCacheEntry("1", ["stale"], IsPreview: false, 100),
                ChapterCacheExpectedState.PresentOrAbsent,
                generation,
                cancellationTokenSource.Token));
    }

    [Fact]
    public async Task TrySaveCatalogObservesDurableClearGenerationChange()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        long generation = CacheStore.GetClearGeneration(root);
        WriteClearGeneration(root, (generation + 1).ToString());

        bool saved = await CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
            root,
            new CatalogSnapshot(
                "1045928363",
                new BookMetadata("1045928363", "Title", "Author", null),
                [],
                DateTimeOffset.UtcNow),
            generation,
            CancellationToken.None);

        Assert.False(saved);
        Assert.False(File.Exists(AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous)));
    }

    [Fact]
    public async Task TrySaveCatalogObservesCancellationBeforeGenerationMismatchReturn()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        long generation = CacheStore.GetClearGeneration(root);
        WriteClearGeneration(root, (generation + 1).ToString());
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                root,
                new CatalogSnapshot(
                    "1045928363",
                    new BookMetadata("1045928363", "Title", "Author", null),
                    [],
                    DateTimeOffset.UtcNow),
                generation,
                cancellationTokenSource.Token));
    }

    [Fact]
    public async Task TrySaveChapterIfAbsentObservesCancellationBeforeExistingFileReturn()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, "{}");
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                root,
                "1045928363",
                new ChapterCacheEntry("1", ["stale"], IsPreview: false, 100),
                ChapterCacheExpectedState.Absent,
                cancellationTokenSource.Token));
    }

    [Fact]
    public void ClearGenerationIncrementDoesNotLoseConcurrentUpdates()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        int clearCount = 32;
        long generation = CacheStore.GetClearGeneration(root);

        Parallel.For(
            0,
            clearCount,
            _ => CacheStore.Clear(root, bookId: null, catalogOnly: true));

        Assert.Equal(generation + clearCount, CacheStore.GetClearGeneration(root));
    }

    [Fact]
    public void ClearAdvancesFromDurableGenerationWhenMemoryIsStale()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = Path.Combine(temporaryDirectory.FullPath, "cache");
        long generation = CacheStore.GetClearGeneration(root);
        Directory.CreateDirectory(Path.GetDirectoryName(
            CacheStore.GetClearGenerationFilePath(root))!);
        WriteClearGeneration(root, (generation + 1).ToString());

        CacheStore.Clear(root, bookId: null, catalogOnly: true);

        Assert.Equal(generation + 2, CacheStore.GetClearGeneration(root));
    }

    [Fact]
    public void ClearKeepsPersistentClearGenerationLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = Path.Combine(temporaryDirectory.FullPath, "cache");
        string lockPath = CacheStore.GetClearGenerationFilePath(root) + ".lock";

        CacheStore.Clear(root, bookId: null, catalogOnly: true);

        Assert.True(File.Exists(lockPath));
    }

    [Fact]
    public void ClearGlobalRemovesCacheRootAndPreservesAppStateSiblings()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string stateRoot = Path.Combine(temporaryDirectory.FullPath, "state");
        string root = Path.Combine(stateRoot, AppConstants.CacheDirectoryName);
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        string siblingFile = Path.Combine(stateRoot, "config.json");
        string siblingDirectory = Path.Combine(stateRoot, "logs");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        Directory.CreateDirectory(siblingDirectory);
        File.WriteAllText(cachePath, "{}");
        File.WriteAllText(siblingFile, "{}");
        File.WriteAllText(Path.Combine(siblingDirectory, "app.log"), "log");

        int removed = CacheStore.Clear(root, bookId: null, catalogOnly: false);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(root));
        Assert.True(File.Exists(CacheStore.GetClearGenerationFilePath(root)));
        Assert.True(File.Exists(CacheStore.GetClearGenerationFilePath(root) + ".lock"));
        Assert.True(File.Exists(siblingFile));
        Assert.True(File.Exists(Path.Combine(siblingDirectory, "app.log")));
    }

    [Fact]
    public void ClearGlobalPreflightsFullSubtreeBeforeGenerationIncrementAndDeletion()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string protectedFile = Path.Combine(root, "protected.json");
        string nestedDirectory = Path.Combine(root, "nested");
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside.json");
        string linkPath = Path.Combine(nestedDirectory, "link.json");
        const long Generation = 7;
        WriteClearGeneration(root, Generation.ToString());
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(protectedFile, "{}");
        File.WriteAllText(outsideFile, "{}");
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        File.CreateSymbolicLink(linkPath, outsideFile);
        try
        {
            Assert.Throws<IOException>(() => CacheStore.Clear(
                root,
                bookId: null,
                catalogOnly: false));

            Assert.Equal(Generation, CacheStore.GetClearGeneration(root));
            Assert.True(File.Exists(protectedFile));
            Assert.True(File.Exists(linkPath));
        }
        finally
        {
            if (File.Exists(linkPath)
                && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
            {
                File.Delete(linkPath);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClearBookPreflightsTargetSubtreeBeforeGenerationIncrementAndDeletion(
        bool catalogOnly)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string targetDirectory = catalogOnly
            ? AppPaths.GetCatalogCacheDirectory(root, bookId)
            : AppPaths.GetBookCacheDirectory(root, bookId);
        string protectedFile = Path.Combine(targetDirectory, "protected.json");
        string outsideDirectory = Path.Combine(temporaryDirectory.FullPath, "outside");
        string linkPath = Path.Combine(targetDirectory, "link");
        const long Generation = 7;
        WriteClearGeneration(root, Generation.ToString());
        Directory.CreateDirectory(targetDirectory);
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(protectedFile, "{}");
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Directory.CreateSymbolicLink(linkPath, outsideDirectory);
        try
        {
            Assert.Throws<IOException>(() => CacheStore.Clear(root, bookId, catalogOnly));

            Assert.Equal(Generation, CacheStore.GetClearGeneration(root));
            Assert.True(File.Exists(protectedFile));
            Assert.True(Directory.Exists(linkPath));
        }
        finally
        {
            if (Directory.Exists(linkPath)
                && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClearBookRejectsTraversalTargetBeforeGenerationIncrementAndDeletion(
        bool catalogOnly)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string outsideDirectory = Path.Combine(temporaryDirectory.FullPath, "outside-traversal");
        string bookId = Path.GetRelativePath(root, outsideDirectory);
        string targetDirectory = catalogOnly
            ? AppPaths.GetCatalogCacheDirectory(root, bookId)
            : AppPaths.GetBookCacheDirectory(root, bookId);
        string protectedFile = Path.Combine(targetDirectory, "protected.json");
        const long Generation = 7;
        WriteClearGeneration(root, Generation.ToString());
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(protectedFile, "{}");

        Assert.Throws<IOException>(() => CacheStore.Clear(root, bookId, catalogOnly));

        Assert.Equal(Generation, CacheStore.GetClearGeneration(root));
        Assert.True(File.Exists(protectedFile));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClearBookRejectsRootedTargetBeforeGenerationIncrementAndDeletion(
        bool catalogOnly)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = Path.Combine(temporaryDirectory.FullPath, "outside-rooted");
        string targetDirectory = catalogOnly
            ? AppPaths.GetCatalogCacheDirectory(root, bookId)
            : AppPaths.GetBookCacheDirectory(root, bookId);
        string protectedFile = Path.Combine(targetDirectory, "protected.json");
        const long Generation = 7;
        WriteClearGeneration(root, Generation.ToString());
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(protectedFile, "{}");

        Assert.Throws<IOException>(() => CacheStore.Clear(root, bookId, catalogOnly));

        Assert.Equal(Generation, CacheStore.GetClearGeneration(root));
        Assert.True(File.Exists(protectedFile));
    }

    [Fact]
    public void ClearAllCatalogsRejectsReparseDirectoryOutsideCacheRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string catalogPath = AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous);
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside");
        string outsideCatalogs = Path.Combine(outsideRoot, AppConstants.CatalogsDirectoryName);
        string outsideCatalog = Path.Combine(outsideCatalogs, "catalog.json");
        string linkPath = Path.Combine(root, "linked-book");
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        Directory.CreateDirectory(outsideCatalogs);
        File.WriteAllText(catalogPath, CreateValidCatalogJson("1045928363"));
        File.WriteAllText(outsideCatalog, CreateValidCatalogJson("1045928363"));
        Directory.CreateDirectory(root);
        try
        {
            Directory.CreateSymbolicLink(linkPath, outsideRoot);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Assert.Throws<IOException>(() => CacheStore.Clear(
            root,
            bookId: null,
            catalogOnly: true));

        Assert.True(File.Exists(catalogPath));
        Assert.True(File.Exists(outsideCatalog));
        Assert.True(Directory.Exists(linkPath));
    }

    [Fact]
    public void ClearAllCatalogsPreflightsCatalogSubtreeBeforeDeletingFiles()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string catalogPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.Anonymous);
        string catalogsDirectory = Path.GetDirectoryName(catalogPath)!;
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside");
        string linkPath = Path.Combine(catalogsDirectory, "link");
        Directory.CreateDirectory(catalogsDirectory);
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(catalogPath, CreateValidCatalogJson(bookId));
        try
        {
            Directory.CreateSymbolicLink(linkPath, outsideRoot);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            Assert.Throws<IOException>(() => CacheStore.Clear(
                root,
                bookId: null,
                catalogOnly: true));

            Assert.True(File.Exists(catalogPath));
            Assert.True(Directory.Exists(linkPath));
        }
        finally
        {
            if (Directory.Exists(linkPath)
                && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [Fact]
    public void ClearAllCatalogsDoesNotAdvanceGenerationWhenPreflightRejectsChildReparse()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string catalogPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.Anonymous);
        string catalogsDirectory = Path.GetDirectoryName(catalogPath)!;
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside");
        string linkPath = Path.Combine(catalogsDirectory, "link");
        const long Generation = 7;
        WriteClearGeneration(root, Generation.ToString());
        Directory.CreateDirectory(catalogsDirectory);
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(catalogPath, CreateValidCatalogJson(bookId));
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Directory.CreateSymbolicLink(linkPath, outsideRoot);
        try
        {
            Assert.Throws<IOException>(() => CacheStore.Clear(
                root,
                bookId: null,
                catalogOnly: true));

            Assert.Equal(Generation, CacheStore.GetClearGeneration(root));
            Assert.True(File.Exists(catalogPath));
            Assert.True(Directory.Exists(linkPath));
        }
        finally
        {
            if (Directory.Exists(linkPath)
                && (File.GetAttributes(linkPath) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(linkPath);
            }
        }
    }

    [Fact]
    public void ClearAllCatalogsRejectsReparseCacheRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string stateRoot = Path.Combine(temporaryDirectory.FullPath, "state");
        string root = Path.Combine(stateRoot, AppConstants.CacheDirectoryName);
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside-cache");
        string outsideCatalog = AppPaths.GetCatalogCachePath(
            outsideRoot,
            "1045928363",
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideCatalog)!);
        Directory.CreateDirectory(stateRoot);
        File.WriteAllText(outsideCatalog, "{}");
        try
        {
            Directory.CreateSymbolicLink(root, outsideRoot);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            Assert.Throws<IOException>(() => CacheStore.Clear(
                root,
                bookId: null,
                catalogOnly: true));

            Assert.True(File.Exists(outsideCatalog));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClearBookRejectsReparseCacheRoot(bool catalogOnly)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string stateRoot = Path.Combine(temporaryDirectory.FullPath, "state");
        string root = Path.Combine(stateRoot, AppConstants.CacheDirectoryName);
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside-cache");
        string bookId = "1045928363";
        string outsideCatalog = AppPaths.GetCatalogCachePath(
            outsideRoot,
            bookId,
            CatalogCacheScope.Anonymous);
        string outsideChapter = AppPaths.GetChapterCachePath(outsideRoot, bookId, "1");
        Directory.CreateDirectory(Path.GetDirectoryName(outsideCatalog)!);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideChapter)!);
        Directory.CreateDirectory(stateRoot);
        File.WriteAllText(outsideCatalog, "{}");
        File.WriteAllText(outsideChapter, "{}");
        try
        {
            Directory.CreateSymbolicLink(root, outsideRoot);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            Assert.Throws<IOException>(() => CacheStore.Clear(root, bookId, catalogOnly));

            Assert.True(File.Exists(outsideCatalog));
            Assert.True(File.Exists(outsideChapter));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root);
            }
        }
    }

    [Fact]
    public void ClearGlobalRejectsDanglingReparseCacheRootBeforeGenerationIncrement()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string stateRoot = Path.Combine(temporaryDirectory.FullPath, "state");
        string root = Path.Combine(stateRoot, AppConstants.CacheDirectoryName);
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside-cache");
        string generationPath = CacheStore.GetClearGenerationFilePath(root);
        const long Generation = 7;
        WriteClearGeneration(root, Generation.ToString());
        Directory.CreateDirectory(outsideRoot);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Directory.CreateSymbolicLink(root, outsideRoot);
        Directory.Delete(outsideRoot);
        try
        {
            Assert.Throws<IOException>(() => CacheStore.Clear(
                root,
                bookId: null,
                catalogOnly: false));

            Assert.Equal(Generation.ToString(), File.ReadAllText(generationPath));
        }
        finally
        {
            DeleteReparseDirectoryIfExists(root);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClearBookRejectsDanglingReparseTargetBeforeGenerationIncrement(bool catalogOnly)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string targetDirectory = catalogOnly
            ? AppPaths.GetCatalogCacheDirectory(root, bookId)
            : AppPaths.GetBookCacheDirectory(root, bookId);
        string outsideTarget = Path.Combine(temporaryDirectory.FullPath, "outside-target");
        const long Generation = 7;
        WriteClearGeneration(root, Generation.ToString());
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
        Directory.CreateDirectory(outsideTarget);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Directory.CreateSymbolicLink(targetDirectory, outsideTarget);
        Directory.Delete(outsideTarget);
        try
        {
            Assert.Throws<IOException>(() => CacheStore.Clear(root, bookId, catalogOnly));

            Assert.Equal(Generation, CacheStore.GetClearGeneration(root));
        }
        finally
        {
            DeleteReparseDirectoryIfExists(targetDirectory);
        }
    }

    [Fact]
    public void ClearBookFailsClosedWhenDirectoryBecomesReparseBeforeEnumeration()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string bookDirectory = AppPaths.GetBookCacheDirectory(root, bookId);
        string chapterPath = AppPaths.GetChapterCachePath(root, bookId, "1");
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside-cache");
        string outsideChapter = AppPaths.GetChapterCachePath(outsideRoot, bookId, "1");
        Directory.CreateDirectory(Path.GetDirectoryName(chapterPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideChapter)!);
        File.WriteAllText(chapterPath, "{}");
        File.WriteAllText(outsideChapter, "{}");
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        bool swapped = false;
        try
        {
            CacheStore.BeforeDirectoryEnumerationForTests = path =>
            {
                if (swapped || !string.Equals(path, bookDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                swapped = true;
                Directory.Delete(bookDirectory, recursive: true);
                Directory.CreateSymbolicLink(bookDirectory, outsideRoot);
            };

            Assert.Throws<IOException>(() => CacheStore.Clear(root, bookId, catalogOnly: false));

            Assert.True(File.Exists(outsideChapter));
        }
        finally
        {
            CacheStore.BeforeDirectoryEnumerationForTests = null;
            if (Directory.Exists(bookDirectory)
                && (File.GetAttributes(bookDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(bookDirectory);
            }
        }
    }

    [Fact]
    public void ClearBookFailsClosedWhenCacheRootAncestorBecomesReparseBeforeEnumeration()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string bookDirectory = AppPaths.GetBookCacheDirectory(root, bookId);
        string chapterPath = AppPaths.GetChapterCachePath(root, bookId, "1");
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside-cache");
        string outsideChapter = AppPaths.GetChapterCachePath(outsideRoot, bookId, "1");
        Directory.CreateDirectory(Path.GetDirectoryName(chapterPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideChapter)!);
        File.WriteAllText(chapterPath, "{}");
        File.WriteAllText(outsideChapter, "{}");
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        bool swapped = false;
        try
        {
            CacheStore.BeforeDirectoryEnumerationForTests = path =>
            {
                if (swapped || !string.Equals(path, bookDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                swapped = true;
                Directory.Delete(root, recursive: true);
                Directory.CreateSymbolicLink(root, outsideRoot);
            };

            Assert.Throws<IOException>(() => CacheStore.Clear(root, bookId, catalogOnly: false));

            Assert.True(File.Exists(outsideChapter));
        }
        finally
        {
            CacheStore.BeforeDirectoryEnumerationForTests = null;
            if (Directory.Exists(root)
                && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(root);
            }
        }
    }

    [Fact]
    public void ClearBookFailsClosedWhenCacheRootAncestorBecomesReparseBeforeDeletion()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string bookDirectory = AppPaths.GetBookCacheDirectory(root, bookId);
        string chapterPath = AppPaths.GetChapterCachePath(root, bookId, "1");
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside-cache");
        string outsideChapter = AppPaths.GetChapterCachePath(outsideRoot, bookId, "1");
        Directory.CreateDirectory(Path.GetDirectoryName(chapterPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideChapter)!);
        File.WriteAllText(chapterPath, "{}");
        File.WriteAllText(outsideChapter, "{}");
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        bool swapped = false;
        try
        {
            CacheStore.BeforeDirectoryDeleteForTests = path =>
            {
                if (swapped || !string.Equals(path, bookDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                swapped = true;
                Directory.Delete(root, recursive: true);
                Directory.CreateSymbolicLink(root, outsideRoot);
            };

            Assert.Throws<IOException>(() => CacheStore.Clear(root, bookId, catalogOnly: false));

            Assert.True(File.Exists(outsideChapter));
        }
        finally
        {
            CacheStore.BeforeDirectoryDeleteForTests = null;
            if (Directory.Exists(root)
                && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(root);
            }
        }
    }

    [Fact]
    public void ClearCatalogOnlyFailsClosedWhenAncestorBecomesReparseBeforeLockOpen()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string stateRoot = Path.Combine(temporaryDirectory.FullPath, "state");
        string root = Path.Combine(stateRoot, AppConstants.CacheDirectoryName);
        string outsideStateRoot = Path.Combine(temporaryDirectory.FullPath, "outside-state");
        Directory.CreateDirectory(stateRoot);
        Directory.CreateDirectory(outsideStateRoot);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        bool swapped = false;
        try
        {
            CacheStore.BeforeClearGenerationLockOpenForTests = _ =>
            {
                if (swapped)
                {
                    return;
                }

                swapped = true;
                Directory.Delete(stateRoot, recursive: true);
                Directory.CreateSymbolicLink(stateRoot, outsideStateRoot);
            };

            Assert.Throws<IOException>(() => CacheStore.Clear(
                root,
                bookId: null,
                catalogOnly: true));

            Assert.False(File.Exists(Path.Combine(
                outsideStateRoot,
                AppConstants.ClearGenerationFileName)));
            Assert.False(File.Exists(Path.Combine(
                outsideStateRoot,
                AppConstants.ClearGenerationFileName + ".lock")));
        }
        finally
        {
            CacheStore.BeforeClearGenerationLockOpenForTests = null;
            if (Directory.Exists(stateRoot)
                && (File.GetAttributes(stateRoot) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(stateRoot);
            }
        }
    }

    [Fact]
    public void GetClearGenerationFailsClosedWhenAncestorBecomesReparseBeforeFileRead()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string stateRoot = Path.Combine(temporaryDirectory.FullPath, "state");
        string root = Path.Combine(stateRoot, AppConstants.CacheDirectoryName);
        string generationPath = CacheStore.GetClearGenerationFilePath(root);
        string outsideStateRoot = Path.Combine(temporaryDirectory.FullPath, "outside-state");
        Directory.CreateDirectory(Path.GetDirectoryName(generationPath)!);
        Directory.CreateDirectory(outsideStateRoot);
        File.WriteAllText(generationPath, "1");
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        bool swapped = false;
        try
        {
            CacheStore.BeforeClearGenerationFileOperationForTests = _ =>
            {
                if (swapped)
                {
                    return;
                }

                swapped = true;
                Directory.Delete(stateRoot, recursive: true);
                Directory.CreateSymbolicLink(stateRoot, outsideStateRoot);
            };

            Assert.Throws<IOException>(() => CacheStore.GetClearGeneration(root));
        }
        finally
        {
            CacheStore.BeforeClearGenerationFileOperationForTests = null;
            if (Directory.Exists(stateRoot)
                && (File.GetAttributes(stateRoot) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(stateRoot);
            }
        }
    }

    [Fact]
    public void DurableClearGenerationIncrementDoesNotLoseIndependentActorUpdates()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        int clearCount = 32;
        long generation = CacheStore.GetClearGeneration(root);

        Parallel.For(
            0,
            clearCount,
            _ => CacheStore.IncrementClearGenerationBypassingMemoryForTests(root));

        Assert.Equal(generation + clearCount, CacheStore.GetClearGeneration(root));
    }

    [Fact]
    public void ClearFailsWhenClearGenerationLockPathIsDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = Path.Combine(temporaryDirectory.FullPath, "cache");
        string lockPath = CacheStore.GetClearGenerationFilePath(root) + ".lock";
        Directory.CreateDirectory(lockPath);

        Exception exception = Assert.ThrowsAny<Exception>(
            () => CacheStore.Clear(root, bookId: null, catalogOnly: true));

        Assert.True(exception is IOException or UnauthorizedAccessException);
    }

    [Fact]
    public async Task TrySaveChapterFailsWhenClearGenerationLockPathIsDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = Path.Combine(temporaryDirectory.FullPath, "cache");
        string lockPath = CacheStore.GetClearGenerationFilePath(root) + ".lock";
        Directory.CreateDirectory(lockPath);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                root,
                "1045928363",
                new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                ChapterCacheExpectedState.PresentOrAbsent,
                CacheStore.GetClearGeneration(root),
                CancellationToken.None));

        Assert.True(exception is IOException or UnauthorizedAccessException);
    }

    [Fact]
    public async Task TrySaveCatalogFailsWhenClearGenerationLockPathIsDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = Path.Combine(temporaryDirectory.FullPath, "cache");
        string lockPath = CacheStore.GetClearGenerationFilePath(root) + ".lock";
        Directory.CreateDirectory(lockPath);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                root,
                new CatalogSnapshot(
                    "1045928363",
                    new BookMetadata("1045928363", "Title", "Author", null),
                    [],
                    DateTimeOffset.UtcNow),
                CacheStore.GetClearGeneration(root),
                CancellationToken.None));

        Assert.True(exception is IOException or UnauthorizedAccessException);
    }

    [Fact]
    public async Task TrySaveCatalogRejectsReparseBookDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string bookDirectory = AppPaths.GetBookCacheDirectory(root, bookId);
        string outsideBookDirectory = Path.Combine(temporaryDirectory.FullPath, "outside-book");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideBookDirectory);
        try
        {
            Directory.CreateSymbolicLink(bookDirectory, outsideBookDirectory);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                    root,
                    new CatalogSnapshot(
                        bookId,
                        new BookMetadata(bookId, "Title", "Author", null),
                        [],
                        DateTimeOffset.UtcNow),
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.False(File.Exists(AppPaths.GetCatalogCachePath(
                outsideBookDirectory,
                bookId,
                CatalogCacheScope.Anonymous)));
            Assert.Empty(Directory.EnumerateFiles(
                outsideBookDirectory,
                "*",
                SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(bookDirectory)
                && (File.GetAttributes(bookDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(bookDirectory);
            }
        }
    }

    [Fact]
    public async Task TrySaveChapterRejectsReparseBookDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string bookDirectory = AppPaths.GetBookCacheDirectory(root, bookId);
        string outsideBookDirectory = Path.Combine(temporaryDirectory.FullPath, "outside-book");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideBookDirectory);
        try
        {
            Directory.CreateSymbolicLink(bookDirectory, outsideBookDirectory);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.False(File.Exists(AppPaths.GetChapterCachePath(
                outsideBookDirectory,
                bookId,
                "1")));
            Assert.Empty(Directory.EnumerateFiles(
                outsideBookDirectory,
                "*",
                SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(bookDirectory)
                && (File.GetAttributes(bookDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(bookDirectory);
            }
        }
    }

    [Fact]
    public async Task TrySaveCatalogDoesNotWriteTemporaryJsonThroughSwappedDestinationDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string bookDirectory = AppPaths.GetBookCacheDirectory(root, bookId);
        string outsideBookDirectory = Path.Combine(temporaryDirectory.FullPath, "outside-book");
        Directory.CreateDirectory(outsideBookDirectory);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = _ =>
            {
                Directory.Delete(bookDirectory, recursive: true);
                Directory.CreateSymbolicLink(bookDirectory, outsideBookDirectory);
            };

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                    root,
                    new CatalogSnapshot(
                        bookId,
                        new BookMetadata(bookId, "Title", "Author", null),
                        [],
                        DateTimeOffset.UtcNow),
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Empty(Directory.EnumerateFiles(
                outsideBookDirectory,
                "*",
                SearchOption.AllDirectories));
        }
        finally
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = null;
            if (Directory.Exists(bookDirectory)
                && (File.GetAttributes(bookDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(bookDirectory);
            }
        }
    }

    [Fact]
    public async Task TrySaveChapterDoesNotWriteTemporaryJsonThroughSwappedDestinationDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string bookDirectory = AppPaths.GetBookCacheDirectory(root, bookId);
        string outsideBookDirectory = Path.Combine(temporaryDirectory.FullPath, "outside-book");
        Directory.CreateDirectory(outsideBookDirectory);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = _ =>
            {
                Directory.Delete(bookDirectory, recursive: true);
                Directory.CreateSymbolicLink(bookDirectory, outsideBookDirectory);
            };

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Empty(Directory.EnumerateFiles(
                outsideBookDirectory,
                "*",
                SearchOption.AllDirectories));
        }
        finally
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = null;
            if (Directory.Exists(bookDirectory)
                && (File.GetAttributes(bookDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(bookDirectory);
            }
        }
    }

    [Fact]
    public async Task TrySaveCatalogDoesNotWriteTemporaryJsonThroughSwappedStagingDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string stagingDirectory = Path.Combine(root, ".staging");
        string outsideStagingDirectory = Path.Combine(
            temporaryDirectory.FullPath,
            "outside-staging");
        Directory.CreateDirectory(outsideStagingDirectory);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = _ =>
            {
                Directory.Delete(stagingDirectory, recursive: true);
                Directory.CreateSymbolicLink(stagingDirectory, outsideStagingDirectory);
            };

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                    root,
                    new CatalogSnapshot(
                        bookId,
                        new BookMetadata(bookId, "Title", "Author", null),
                        [],
                        DateTimeOffset.UtcNow),
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Empty(Directory.EnumerateFiles(
                outsideStagingDirectory,
                "*",
                SearchOption.AllDirectories));
        }
        finally
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = null;
            if (Directory.Exists(stagingDirectory)
                && (File.GetAttributes(stagingDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(stagingDirectory);
            }
        }
    }

    [Fact]
    public async Task TrySaveChapterDoesNotWriteTemporaryJsonThroughSwappedStagingDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string stagingDirectory = Path.Combine(root, ".staging");
        string outsideStagingDirectory = Path.Combine(
            temporaryDirectory.FullPath,
            "outside-staging");
        Directory.CreateDirectory(outsideStagingDirectory);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = _ =>
            {
                Directory.Delete(stagingDirectory, recursive: true);
                Directory.CreateSymbolicLink(stagingDirectory, outsideStagingDirectory);
            };

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Empty(Directory.EnumerateFiles(
                outsideStagingDirectory,
                "*",
                SearchOption.AllDirectories));
        }
        finally
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = null;
            if (Directory.Exists(stagingDirectory)
                && (File.GetAttributes(stagingDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(stagingDirectory);
            }
        }
    }

    [Fact]
    public async Task TrySaveCatalogDoesNotWriteTemporaryJsonThroughTemporaryFileSymlink()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-catalog.json");
        const string OutsideContent = "outside catalog content";
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, OutsideContent);
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = temporaryPath =>
            {
                File.CreateSymbolicLink(temporaryPath, outsideFile);
            };

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                    root,
                    new CatalogSnapshot(
                        bookId,
                        new BookMetadata(bookId, "Title", "Author", null),
                        [],
                        DateTimeOffset.UtcNow),
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Equal(OutsideContent, File.ReadAllText(outsideFile));
        }
        finally
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = null;
        }
    }

    [Fact]
    public async Task TrySaveChapterDoesNotWriteTemporaryJsonThroughTemporaryFileSymlink()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-chapter.json");
        const string OutsideContent = "outside chapter content";
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, OutsideContent);
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = temporaryPath =>
            {
                File.CreateSymbolicLink(temporaryPath, outsideFile);
            };

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Equal(OutsideContent, File.ReadAllText(outsideFile));
        }
        finally
        {
            CacheStore.BeforeCacheTemporaryWriteForTests = null;
        }
    }

    [Fact]
    public async Task TrySaveCatalogRejectsFinalCatalogFileSymlink()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-final-catalog.json");
        const string OutsideContent = "outside catalog content";
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, OutsideContent);
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCatalogCacheCommitForTests = (cachePath, _) =>
            {
                File.CreateSymbolicLink(cachePath, outsideFile);
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                    root,
                    new CatalogSnapshot(
                        bookId,
                        new BookMetadata(bookId, "Title", "Author", null),
                        [],
                        DateTimeOffset.UtcNow),
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Equal(OutsideContent, File.ReadAllText(outsideFile));
        }
        finally
        {
            CacheStore.BeforeCatalogCacheCommitForTests = null;
            string cachePath = AppPaths.GetCatalogCachePath(
                root,
                bookId,
                CatalogCacheScope.Anonymous);
            if (File.Exists(cachePath)
                && (File.GetAttributes(cachePath) & FileAttributes.ReparsePoint) != 0)
            {
                File.Delete(cachePath);
            }
        }
    }

    [Fact]
    public async Task TrySaveChapterRejectsFinalChapterFileSymlink()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-final-chapter.json");
        const string OutsideContent = "outside chapter content";
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, OutsideContent);
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (cachePath, _) =>
            {
                File.CreateSymbolicLink(cachePath, outsideFile);
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<IOException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));

            Assert.Equal(OutsideContent, File.ReadAllText(outsideFile));
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
            string cachePath = AppPaths.GetChapterCachePath(root, bookId, "1");
            if (File.Exists(cachePath)
                && (File.GetAttributes(cachePath) & FileAttributes.ReparsePoint) != 0)
            {
                File.Delete(cachePath);
            }
        }
    }

    [Fact]
    public async Task TrySaveChapterPropagatesPermanentDestinationPathFailure()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(cachePath);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                root,
                "1045928363",
                new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                ChapterCacheExpectedState.PresentOrAbsent,
                CacheStore.GetClearGeneration(root),
                CancellationToken.None));

        Assert.True(exception is IOException or UnauthorizedAccessException);
    }

    [Fact]
    public async Task TrySaveCatalogPropagatesPermanentDestinationPathFailure()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(cachePath);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                root,
                new CatalogSnapshot(
                    "1045928363",
                    new BookMetadata("1045928363", "Title", "Author", null),
                    [],
                    DateTimeOffset.UtcNow),
                CacheStore.GetClearGeneration(root),
                CancellationToken.None));

        Assert.True(exception is IOException or UnauthorizedAccessException);
    }

    [Fact]
    public async Task TrySaveChapterRejectsDurableClearGenerationChangeBeforeCommit()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        long generation = CacheStore.GetClearGeneration(root);

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (_, _) =>
            {
                WriteClearGeneration(root, (generation + 1).ToString());
                return Task.CompletedTask;
            };

            bool saved = await CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                root,
                bookId,
                new ChapterCacheEntry("1", ["stale"], IsPreview: false, 100),
                ChapterCacheExpectedState.PresentOrAbsent,
                generation,
                CancellationToken.None);

            Assert.False(saved);
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
        }

        Assert.Null(await CacheStore.GetChapterAsync(root, bookId, "1", CancellationToken.None));
    }

    [Fact]
    public async Task TrySaveCatalogRejectsDurableClearGenerationChangeBeforeCommitAndCleansTemp()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        long generation = CacheStore.GetClearGeneration(root);

        try
        {
            CacheStore.BeforeCatalogCacheCommitForTests = (_, _) =>
            {
                WriteClearGeneration(root, (generation + 1).ToString());
                return Task.CompletedTask;
            };

            bool saved = await CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                root,
                new CatalogSnapshot(
                    bookId,
                    new BookMetadata(bookId, "Title", "Author", null),
                    [],
                    DateTimeOffset.UtcNow),
                generation,
                CancellationToken.None);

            Assert.False(saved);
        }
        finally
        {
            CacheStore.BeforeCatalogCacheCommitForTests = null;
        }

        string catalogPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.Anonymous);
        Assert.False(File.Exists(catalogPath));
        AssertNoTemporaryFiles(root);
    }

    [Fact]
    public async Task TrySaveChapterPropagatesCancellationBeforeCommitAndCleansTemp()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        using CancellationTokenSource cancellationTokenSource = new();

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (_, _) =>
            {
                cancellationTokenSource.Cancel();
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    cancellationTokenSource.Token));
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
        }

        Assert.Null(await CacheStore.GetChapterAsync(root, bookId, "1", CancellationToken.None));
        AssertNoTemporaryFiles(root);
    }

    [Fact]
    public async Task TrySaveCatalogPropagatesCancellationBeforeCommitAndCleansTemp()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        using CancellationTokenSource cancellationTokenSource = new();

        try
        {
            CacheStore.BeforeCatalogCacheCommitForTests = (_, _) =>
            {
                cancellationTokenSource.Cancel();
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                    root,
                    new CatalogSnapshot(
                        bookId,
                        new BookMetadata(bookId, "Title", "Author", null),
                        [],
                        DateTimeOffset.UtcNow),
                    CacheStore.GetClearGeneration(root),
                    cancellationTokenSource.Token));
        }
        finally
        {
            CacheStore.BeforeCatalogCacheCommitForTests = null;
        }

        string catalogPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.Anonymous);
        Assert.False(File.Exists(catalogPath));
        AssertNoTemporaryFiles(root);
    }

    [Fact]
    public async Task TrySaveChapterPreservesCancellationWhenTemporaryCleanupFails()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-cleanup.json");
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, "outside");
        using CancellationTokenSource cancellationTokenSource = new();
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (_, _) =>
            {
                string stagingDirectory = Path.Combine(root, ".staging");
                string temporaryPath = Assert.Single(Directory.EnumerateFiles(
                    stagingDirectory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly));
                File.Delete(temporaryPath);
                File.CreateSymbolicLink(temporaryPath, outsideFile);
                cancellationTokenSource.Cancel();
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    cancellationTokenSource.Token));
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
            string stagingDirectory = Path.Combine(root, ".staging");
            if (Directory.Exists(stagingDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(stagingDirectory))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    {
                        File.Delete(file);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task TrySaveChapterPreservesThrownCancellationWhenTemporaryCleanupFails()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-cleanup.json");
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, "outside");
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (_, _) =>
            {
                string stagingDirectory = Path.Combine(root, ".staging");
                string temporaryPath = Assert.Single(Directory.EnumerateFiles(
                    stagingDirectory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly));
                File.Delete(temporaryPath);
                File.CreateSymbolicLink(temporaryPath, outsideFile);
                throw new OperationCanceledException();
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.PresentOrAbsent,
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
            string stagingDirectory = Path.Combine(root, ".staging");
            if (Directory.Exists(stagingDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(stagingDirectory))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    {
                        File.Delete(file);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task TrySaveCatalogPreservesThrownCancellationWhenTemporaryCleanupFails()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-cleanup.json");
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, "outside");
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCatalogCacheCommitForTests = (_, _) =>
            {
                string stagingDirectory = Path.Combine(root, ".staging");
                string temporaryPath = Assert.Single(Directory.EnumerateFiles(
                    stagingDirectory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly));
                File.Delete(temporaryPath);
                File.CreateSymbolicLink(temporaryPath, outsideFile);
                throw new OperationCanceledException();
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                    root,
                    new CatalogSnapshot(
                        bookId,
                        new BookMetadata(bookId, "Title", "Author", null),
                        [],
                        DateTimeOffset.UtcNow),
                    CacheStore.GetClearGeneration(root),
                    CancellationToken.None));
        }
        finally
        {
            CacheStore.BeforeCatalogCacheCommitForTests = null;
            string stagingDirectory = Path.Combine(root, ".staging");
            if (Directory.Exists(stagingDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(stagingDirectory))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    {
                        File.Delete(file);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task TrySaveChapterPreservesCancellationAfterBenignCommitRaceWhenTemporaryCleanupFails()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-cleanup.json");
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, "outside");
        using CancellationTokenSource cancellationTokenSource = new();
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (cachePath, _) =>
            {
                string stagingDirectory = Path.Combine(root, ".staging");
                string temporaryPath = Assert.Single(Directory.EnumerateFiles(
                    stagingDirectory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly));
                File.Delete(temporaryPath);
                File.CreateSymbolicLink(temporaryPath, outsideFile);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllText(cachePath, "{}");
                cancellationTokenSource.Cancel();
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    ChapterCacheExpectedState.Absent,
                    CacheStore.GetClearGeneration(root),
                    cancellationTokenSource.Token));
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
            DeleteReparseTemporaryFiles(root);
        }
    }

    [Fact]
    public async Task TrySaveCatalogPreservesCancellationAfterBenignCommitRaceWhenTemporaryCleanupFails()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string outsideFile = Path.Combine(temporaryDirectory.FullPath, "outside-cleanup.json");
        Directory.CreateDirectory(temporaryDirectory.FullPath);
        File.WriteAllText(outsideFile, "outside");
        using CancellationTokenSource cancellationTokenSource = new();
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            CacheStore.BeforeCatalogCacheCommitForTests = (cachePath, _) =>
            {
                string stagingDirectory = Path.Combine(root, ".staging");
                string temporaryPath = Assert.Single(Directory.EnumerateFiles(
                    stagingDirectory,
                    "*.tmp",
                    SearchOption.TopDirectoryOnly));
                File.Delete(temporaryPath);
                File.CreateSymbolicLink(temporaryPath, outsideFile);
                Directory.Delete(Path.GetDirectoryName(cachePath)!);
                cancellationTokenSource.Cancel();
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                    root,
                    new CatalogSnapshot(
                        bookId,
                        new BookMetadata(bookId, "Title", "Author", null),
                        [],
                        DateTimeOffset.UtcNow),
                    CacheStore.GetClearGeneration(root),
                    cancellationTokenSource.Token));
        }
        finally
        {
            CacheStore.BeforeCatalogCacheCommitForTests = null;
            DeleteReparseTemporaryFiles(root);
        }
    }

    [Fact]
    public async Task TrySaveChapterReturnsFalseWhenClearRemovesTempDirectoryBeforeCommit()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        long generation = CacheStore.GetClearGeneration(root);

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (_, _) =>
            {
                Directory.Delete(AppPaths.GetBookCacheDirectory(root, "1045928363"), recursive: true);
                return Task.CompletedTask;
            };

            bool saved = await CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                root,
                "1045928363",
                new ChapterCacheEntry("1", ["stale"], IsPreview: false, 100),
                ChapterCacheExpectedState.PresentOrAbsent,
                generation,
                CancellationToken.None);

            Assert.False(saved);
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
        }
    }

    [Fact]
    public async Task SaveChapterAsyncRejectsClearGenerationChangeBeforeCommitAndCleansTemp()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        long generation = CacheStore.GetClearGeneration(root);

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (_, _) =>
            {
                WriteClearGeneration(root, (generation + 1).ToString());
                return Task.CompletedTask;
            };

            await CacheStore.SaveChapterAsync(
                root,
                bookId,
                new ChapterCacheEntry("1", ["stale"], IsPreview: false, 100),
                CancellationToken.None);
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
        }

        Assert.Null(await CacheStore.GetChapterAsync(root, bookId, "1", CancellationToken.None));
        AssertNoTemporaryFiles(root);
    }

    [Fact]
    public async Task SaveChapterAsyncPropagatesCancellationBeforeCommitAndCleansTemp()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        using CancellationTokenSource cancellationTokenSource = new();

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (_, _) =>
            {
                cancellationTokenSource.Cancel();
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => CacheStore.SaveChapterAsync(
                    root,
                    bookId,
                    new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                    cancellationTokenSource.Token));
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
        }

        Assert.Null(await CacheStore.GetChapterAsync(root, bookId, "1", CancellationToken.None));
        AssertNoTemporaryFiles(root);
    }

    [Fact]
    public void GetClearGenerationReturnsZeroWhenDurableFileIsAbsent()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);

        Assert.Equal(0, CacheStore.GetClearGeneration(root));
    }

    [Fact]
    public void GetClearGenerationRejectsReparseGenerationFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string generationPath = CacheStore.GetClearGenerationFilePath(root);
        string outsideGenerationPath = Path.Combine(
            temporaryDirectory.FullPath,
            "outside-generation");
        Directory.CreateDirectory(Path.GetDirectoryName(generationPath)!);
        File.WriteAllText(outsideGenerationPath, "1");
        try
        {
            File.CreateSymbolicLink(generationPath, outsideGenerationPath);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Assert.Throws<IOException>(() => CacheStore.GetClearGeneration(root));
        Assert.Equal("1", File.ReadAllText(outsideGenerationPath));
    }

    [Fact]
    public void ClearRejectsReparseClearGenerationLockFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string lockPath = CacheStore.GetClearGenerationFilePath(root) + ".lock";
        string outsideLockPath = Path.Combine(temporaryDirectory.FullPath, "outside-lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        File.WriteAllText(outsideLockPath, "outside");
        try
        {
            File.CreateSymbolicLink(lockPath, outsideLockPath);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Assert.Throws<IOException>(() => CacheStore.Clear(
            root,
            bookId: null,
            catalogOnly: true));
        Assert.Equal("outside", File.ReadAllText(outsideLockPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-1")]
    public void GetClearGenerationThrowsForInvalidDurableFile(string generation)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string generationPath = CacheStore.GetClearGenerationFilePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(generationPath)!);
        File.WriteAllText(generationPath, generation);

        Assert.Throws<InvalidDataException>(() => CacheStore.GetClearGeneration(root));
    }

    [Fact]
    public async Task TrySaveChapterDoesNotCommitWhenDurableClearGenerationIsInvalid()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string generationPath = CacheStore.GetClearGenerationFilePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(generationPath)!);
        File.WriteAllText(generationPath, "not-a-number");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                root,
                "1045928363",
                new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                ChapterCacheExpectedState.PresentOrAbsent,
                expectedClearGeneration: 0,
                CancellationToken.None));

        Assert.False(File.Exists(AppPaths.GetChapterCachePath(root, "1045928363", "1")));
    }

    [Fact]
    public async Task SaveChapterAsyncDoesNotCommitWhenDurableClearGenerationIsUnreadable()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string generationPath = CacheStore.GetClearGenerationFilePath(root);
        Directory.CreateDirectory(generationPath);

        await Assert.ThrowsAnyAsync<Exception>(
            () => CacheStore.SaveChapterAsync(
                root,
                "1045928363",
                new ChapterCacheEntry("1", ["content"], IsPreview: false, 100),
                CancellationToken.None));

        Assert.False(File.Exists(AppPaths.GetChapterCachePath(root, "1045928363", "1")));
    }

    [Fact]
    public async Task SaveCatalogAsyncRejectsDurableClearGenerationChangeBeforeCommitAndCleansTemp()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        long generation = CacheStore.GetClearGeneration(root);

        try
        {
            CacheStore.BeforeCatalogCacheCommitForTests = (_, _) =>
            {
                WriteClearGeneration(root, (generation + 1).ToString());
                return Task.CompletedTask;
            };

            await CacheStore.SaveCatalogAsync(
                root,
                new CatalogSnapshot(
                    bookId,
                    new BookMetadata(bookId, "Title", "Author", null),
                    [],
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }
        finally
        {
            CacheStore.BeforeCatalogCacheCommitForTests = null;
        }

        string catalogPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.Anonymous);
        Assert.False(File.Exists(catalogPath));
        AssertNoTemporaryFiles(Path.GetDirectoryName(catalogPath)!);
    }

    [Fact]
    public async Task SaveCatalogAsyncDoesNotCommitWhenDurableClearGenerationIsInvalid()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string generationPath = CacheStore.GetClearGenerationFilePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(generationPath)!);
        File.WriteAllText(generationPath, "not-a-number");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => CacheStore.SaveCatalogAsync(
                root,
                new CatalogSnapshot(
                    "1045928363",
                    new BookMetadata("1045928363", "Title", "Author", null),
                    [],
                    DateTimeOffset.UtcNow),
                CancellationToken.None));

        Assert.False(File.Exists(AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous)));
        AssertNoTemporaryFiles(Path.GetDirectoryName(AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous))!);
    }

    [Fact]
    public async Task SaveCatalogAsyncDoesNotCommitWhenDurableClearGenerationIsUnreadable()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string generationPath = CacheStore.GetClearGenerationFilePath(root);
        Directory.CreateDirectory(generationPath);

        await Assert.ThrowsAnyAsync<Exception>(
            () => CacheStore.SaveCatalogAsync(
                root,
                new CatalogSnapshot(
                    "1045928363",
                    new BookMetadata("1045928363", "Title", "Author", null),
                    [],
                    DateTimeOffset.UtcNow),
                CancellationToken.None));

        Assert.False(File.Exists(AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous)));
    }

    [Fact]
    public void ClearGlobalReturnsZeroForEmptyDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        Directory.CreateDirectory(root);

        int removed = CacheStore.Clear(root, bookId: null, catalogOnly: false);

        Assert.Equal(0, removed);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForInvalidJson()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
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

    [Fact]
    public async Task GetChapterAsyncReturnsNullForEmptyParagraphs()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
                "chapterId": "1",
                "paragraphs": [],
                "isPreview": false,
                "catalogWordCount": 100
            }
            """);

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);
        ChapterCacheProbe? probe = await CacheStore.GetChapterProbeAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);

        Assert.Null(chapter);
        Assert.Null(probe);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[\"\"]")]
    [InlineData("[\"   \"]")]
    [InlineData("[\"Visible paragraph\", null]")]
    [InlineData("[\"Visible paragraph\", \"\"]")]
    [InlineData("[\"Visible paragraph\", \"   \"]")]
    public async Task GetChapterAsyncAndProbeRejectInvalidParagraphValues(string paragraphsJson)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string cachePath = AppPaths.GetChapterCachePath(root, "1045928363", "1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            $$"""
            {
                "chapterId": "1",
                "paragraphs": {{paragraphsJson}},
                "isPreview": false,
                "catalogWordCount": 100
            }
            """);

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);
        ChapterCacheProbe? probe = await CacheStore.GetChapterProbeAsync(
            root,
            "1045928363",
            "1",
            CancellationToken.None);

        Assert.Null(chapter);
        Assert.Null(probe);
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
        string root = GetCacheRoot(temporaryDirectory);
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

    [Theory]
    [InlineData(false, "[]")]
    [InlineData(true, "[]")]
    [InlineData(false, "[{\"title\":\"Empty Volume\",\"isVip\":false,\"chapters\":[]}]")]
    [InlineData(true, "[{\"title\":\"Empty Volume\",\"isVip\":false,\"chapters\":[]}]")]
    public async Task GetCatalogAsyncRejectsEmptyCatalogStructures(
        bool outputPrediction,
        string volumesJson)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string requestedBookId = "1045928363";
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            requestedBookId,
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            $$"""
            {
                "bookId": "{{requestedBookId}}",
                "metadata": {
                    "bookId": "{{requestedBookId}}",
                    "title": "Title",
                    "author": "Author",
                    "estimatedWordCount": 123456
                },
                "volumes": {{volumesJson}},
                "fetchedAtUtc": "2024-01-01T00:00:00+00:00",
                "cacheScope": {
                    "kind": "Anonymous"
                },
                "isKnownAnonymous": true
            }
            """);

        CatalogSnapshot? catalog = outputPrediction
            ? await CacheStore.GetCatalogForOutputPredictionAsync(
                root,
                requestedBookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None)
            : await CacheStore.GetCatalogAsync(
                root,
                requestedBookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None);

        Assert.Null(catalog);
    }

    [Theory]
    [InlineData(false, "", "Author", "Volume", "Chapter One")]
    [InlineData(false, " \t ", "Author", "Volume", "Chapter One")]
    [InlineData(false, "Title", "", "Volume", "Chapter One")]
    [InlineData(false, "Title", " \t ", "Volume", "Chapter One")]
    [InlineData(false, "Title", "Author", "", "Chapter One")]
    [InlineData(false, "Title", "Author", " \t ", "Chapter One")]
    [InlineData(false, "Title", "Author", "Volume", "")]
    [InlineData(false, "Title", "Author", "Volume", " \t ")]
    [InlineData(true, "", "Author", "Volume", "Chapter One")]
    [InlineData(true, " \t ", "Author", "Volume", "Chapter One")]
    [InlineData(true, "Title", "", "Volume", "Chapter One")]
    [InlineData(true, "Title", " \t ", "Volume", "Chapter One")]
    [InlineData(true, "Title", "Author", "", "Chapter One")]
    [InlineData(true, "Title", "Author", " \t ", "Chapter One")]
    [InlineData(true, "Title", "Author", "Volume", "")]
    [InlineData(true, "Title", "Author", "Volume", " \t ")]
    public async Task GetCatalogAsyncRejectsBlankCatalogTextFields(
        bool outputPrediction,
        string metadataTitle,
        string metadataAuthor,
        string volumeTitle,
        string chapterTitle)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string requestedBookId = "1045928363";
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            requestedBookId,
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            $$"""
            {
                "bookId": {{JsonSerializer.Serialize(requestedBookId)}},
                "metadata": {
                    "bookId": {{JsonSerializer.Serialize(requestedBookId)}},
                    "title": {{JsonSerializer.Serialize(metadataTitle)}},
                    "author": {{JsonSerializer.Serialize(metadataAuthor)}},
                    "estimatedWordCount": 123456
                },
                "volumes": [
                    {
                        "title": {{JsonSerializer.Serialize(volumeTitle)}},
                        "isVip": false,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": {{JsonSerializer.Serialize(chapterTitle)}},
                                "url": "https://www.qidian.com/chapter/1045928363/1/",
                                "isVip": false,
                                "catalogWordCount": 100,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ],
                "fetchedAtUtc": "2024-01-01T00:00:00+00:00",
                "cacheScope": {
                    "kind": "Anonymous"
                },
                "isKnownAnonymous": true
            }
            """);

        CatalogSnapshot? catalog = outputPrediction
            ? await CacheStore.GetCatalogForOutputPredictionAsync(
                root,
                requestedBookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None)
            : await CacheStore.GetCatalogAsync(
                root,
                requestedBookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None);

        Assert.Null(catalog);
    }

    [Theory]
    [InlineData(false, "1045928364", "1045928363")]
    [InlineData(false, "1045928363", "1045928364")]
    [InlineData(false, "1045928364", "1045928364")]
    [InlineData(true, "1045928364", "1045928363")]
    [InlineData(true, "1045928363", "1045928364")]
    [InlineData(true, "1045928364", "1045928364")]
    public async Task GetCatalogAsyncRejectsCatalogWhenCachedBookIdsDoNotMatchRequestedBookId(
        bool outputPrediction,
        string catalogBookId,
        string metadataBookId)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string requestedBookId = "1045928363";
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            requestedBookId,
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            CreateValidCatalogJson(
                catalogBookId,
                metadataBookId,
                requestedBookId,
                """
                "cacheScope": {
                    "kind": "Anonymous"
                },
                "isKnownAnonymous": true
                """));

        CatalogSnapshot? catalog = outputPrediction
            ? await CacheStore.GetCatalogForOutputPredictionAsync(
                root,
                requestedBookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None)
            : await CacheStore.GetCatalogAsync(
                root,
                requestedBookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None);

        Assert.Null(catalog);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCatalogAsyncRejectsCatalogWhenChapterUrlEmbedsDifferentBookId(
        bool outputPrediction)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string requestedBookId = "1045928363";
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            requestedBookId,
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            $$"""
            {
                "bookId": {{JsonSerializer.Serialize(requestedBookId)}},
                "metadata": {
                    "bookId": {{JsonSerializer.Serialize(requestedBookId)}},
                    "title": "Title",
                    "author": "Author",
                    "estimatedWordCount": 123456
                },
                "volumes": [
                    {
                        "title": "Volume",
                        "isVip": false,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": "Chapter One",
                                "url": "https://www.qidian.com/chapter/1045928364/1/",
                                "isVip": false,
                                "catalogWordCount": 100,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ],
                "fetchedAtUtc": "2024-01-01T00:00:00+00:00",
                "cacheScope": {
                    "kind": "Anonymous"
                },
                "isKnownAnonymous": true
            }
            """);

        CatalogSnapshot? catalog = outputPrediction
            ? await CacheStore.GetCatalogForOutputPredictionAsync(
                root,
                requestedBookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None)
            : await CacheStore.GetCatalogAsync(
                root,
                requestedBookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None);

        Assert.Null(catalog);
    }

    [Fact]
    public async Task GetCatalogAsyncReturnsNullWhenCacheFileIsLocked()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, CreateValidCatalogJson("1045928363"));

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
    public async Task GetCatalogAsyncReturnsNullWhenCatalogsDirectoryIsReparsePoint()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string catalogsDirectory = AppPaths.GetCatalogCacheDirectory(root, bookId);
        string outsideCatalogsDirectory = Path.Combine(
            temporaryDirectory.FullPath,
            "outside-catalogs");
        string outsideCatalogPath = Path.Combine(
            outsideCatalogsDirectory,
            AppConstants.CatalogCacheFileName);
        Directory.CreateDirectory(AppPaths.GetBookCacheDirectory(root, bookId));
        Directory.CreateDirectory(outsideCatalogsDirectory);
        await File.WriteAllTextAsync(outsideCatalogPath, CreateValidCatalogJson(bookId));
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Directory.CreateSymbolicLink(catalogsDirectory, outsideCatalogsDirectory);
        try
        {
            CatalogSnapshot? catalog = await CacheStore.GetCatalogAsync(
                root,
                bookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None);

            Assert.Null(catalog);
            Assert.True(File.Exists(outsideCatalogPath));
        }
        finally
        {
            if (Directory.Exists(catalogsDirectory)
                && (File.GetAttributes(catalogsDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(catalogsDirectory);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetCatalogAsyncDoesNotReadCatalogOutsideCacheRoot(bool rootedBookId)
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string outsideCatalogBookId = "1045928363";
        string outsideBookDirectory = Path.Combine(
            temporaryDirectory.FullPath,
            outsideCatalogBookId);
        string bookId = rootedBookId
            ? outsideBookDirectory
            : Path.Combine("..", "..", outsideCatalogBookId);
        string outsideCatalogPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(outsideCatalogPath)!);
        await File.WriteAllTextAsync(
            outsideCatalogPath,
            CreateValidCatalogJson(outsideCatalogBookId));

        string normalizedOutsideCatalogPath = Path.GetFullPath(outsideCatalogPath);
        bool attemptedReadThrough = false;
        try
        {
            CacheStore.BeforeCacheReadForTests = path =>
            {
                if (string.Equals(
                    Path.GetFullPath(path),
                    normalizedOutsideCatalogPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    attemptedReadThrough = true;
                }
            };

            CatalogSnapshot? catalog = await CacheStore.GetCatalogAsync(
                root,
                bookId,
                CatalogCacheScope.Anonymous,
                CancellationToken.None);

            Assert.Null(catalog);
            Assert.False(attemptedReadThrough);
            Assert.True(File.Exists(outsideCatalogPath));
        }
        finally
        {
            CacheStore.BeforeCacheReadForTests = null;
        }
    }

    [Fact]
    public void IsPathUnderRootUsesOperatingSystemPathComparison()
    {
        string root = Path.GetFullPath(Path.Combine("containment-MiXeD"));
        string sameCaseChild = Path.Combine(root, "book");
        string differentCaseChild = Path.Combine(
            Path.GetFullPath(Path.Combine("CONTAINMENT-mixed")),
            "book");
        bool expectsCaseInsensitive =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.Equal(
            expectsCaseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal,
            CacheStore.GetPathComparison());
        Assert.True(CacheStore.IsPathUnderRoot(sameCaseChild, root));
        Assert.Equal(
            expectsCaseInsensitive,
            CacheStore.IsPathUnderRoot(differentCaseChild, root));
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForNullParagraphPayload()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
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
    public async Task GetChapterAsyncAndProbeReturnNullWhenChaptersDirectoryIsReparsePoint()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string chapterId = "1";
        string chaptersDirectory = AppPaths.GetChapterCacheDirectory(root, bookId);
        string outsideChaptersDirectory = Path.Combine(
            temporaryDirectory.FullPath,
            "outside-chapters");
        string outsideChapterPath = Path.Combine(outsideChaptersDirectory, chapterId + ".json");
        Directory.CreateDirectory(AppPaths.GetBookCacheDirectory(root, bookId));
        Directory.CreateDirectory(outsideChaptersDirectory);
        await File.WriteAllTextAsync(
            outsideChapterPath,
            """
            {
                "chapterId": "1",
                "paragraphs": ["Outside paragraph"],
                "isPreview": false,
                "catalogWordCount": 100
            }
            """);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Directory.CreateSymbolicLink(chaptersDirectory, outsideChaptersDirectory);
        try
        {
            ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
                root,
                bookId,
                chapterId,
                CancellationToken.None);
            ChapterCacheProbe? probe = await CacheStore.GetChapterProbeAsync(
                root,
                bookId,
                chapterId,
                CancellationToken.None);

            Assert.Null(chapter);
            Assert.Null(probe);
            Assert.True(File.Exists(outsideChapterPath));
        }
        finally
        {
            if (Directory.Exists(chaptersDirectory)
                && (File.GetAttributes(chaptersDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(chaptersDirectory);
            }
        }
    }

    [Fact]
    public async Task GetChapterAsyncAndProbeReturnNullForTraversalChapterIdOutsideCacheRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string chapterId = Path.Combine("..", "..", "..", "outside-traversal");
        string outsideChapterPath = Path.GetFullPath(
            AppPaths.GetChapterCachePath(root, bookId, chapterId));
        Directory.CreateDirectory(Path.GetDirectoryName(outsideChapterPath)!);
        await File.WriteAllTextAsync(outsideChapterPath, CreateChapterJson(chapterId));

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            bookId,
            chapterId,
            CancellationToken.None);
        ChapterCacheProbe? probe = await CacheStore.GetChapterProbeAsync(
            root,
            bookId,
            chapterId,
            CancellationToken.None);

        Assert.Null(chapter);
        Assert.Null(probe);
        Assert.True(File.Exists(outsideChapterPath));
    }

    [Fact]
    public async Task GetChapterAsyncAndProbeReturnNullForTraversalChapterIdInsideCacheRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string sourceBookId = "1045928363";
        string targetBookId = "1045928364";
        string targetChapterId = "1";
        string targetChapterPath = AppPaths.GetChapterCachePath(
            root,
            targetBookId,
            targetChapterId);
        Directory.CreateDirectory(Path.GetDirectoryName(targetChapterPath)!);

        foreach (char separator in new[]
        {
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
        }.Distinct())
        {
            string maliciousChapterId = string.Join(
                separator.ToString(),
                new[]
                {
                    "..",
                    "..",
                    targetBookId,
                    AppConstants.ChaptersDirectoryName,
                    targetChapterId,
                });
            await File.WriteAllTextAsync(targetChapterPath, CreateChapterJson(maliciousChapterId));

            ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
                root,
                sourceBookId,
                maliciousChapterId,
                CancellationToken.None);
            ChapterCacheProbe? probe = await CacheStore.GetChapterProbeAsync(
                root,
                sourceBookId,
                maliciousChapterId,
                CancellationToken.None);

            Assert.Null(chapter);
            Assert.Null(probe);
            Assert.True(File.Exists(targetChapterPath));
        }
    }

    [Fact]
    public async Task GetChapterAsyncAndProbeReturnNullForRootedChapterIdOutsideCacheRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string chapterId = Path.Combine(temporaryDirectory.FullPath, "outside-rooted");
        string outsideChapterPath = chapterId + ".json";
        Directory.CreateDirectory(Path.GetDirectoryName(outsideChapterPath)!);
        await File.WriteAllTextAsync(outsideChapterPath, CreateChapterJson(chapterId));

        ChapterCacheEntry? chapter = await CacheStore.GetChapterAsync(
            root,
            bookId,
            chapterId,
            CancellationToken.None);
        ChapterCacheProbe? probe = await CacheStore.GetChapterProbeAsync(
            root,
            bookId,
            chapterId,
            CancellationToken.None);

        Assert.Null(chapter);
        Assert.Null(probe);
        Assert.True(File.Exists(outsideChapterPath));
    }

    [Fact]
    public void CountCachedChaptersReturnsZeroWhenChaptersDirectoryIsReparsePoint()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string chaptersDirectory = AppPaths.GetChapterCacheDirectory(root, bookId);
        string outsideChaptersDirectory = Path.Combine(
            temporaryDirectory.FullPath,
            "outside-count-chapters");
        string outsideChapterPath = Path.Combine(outsideChaptersDirectory, "1.json");
        Directory.CreateDirectory(AppPaths.GetBookCacheDirectory(root, bookId));
        Directory.CreateDirectory(outsideChaptersDirectory);
        File.WriteAllText(
            outsideChapterPath,
            """
            {
                "chapterId": "1",
                "paragraphs": ["Outside paragraph"],
                "isPreview": false,
                "catalogWordCount": 100
            }
            """);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        Directory.CreateSymbolicLink(chaptersDirectory, outsideChaptersDirectory);
        try
        {
            int cachedChapters = CacheStore.CountCachedChapters(root, bookId);

            Assert.Equal(0, cachedChapters);
            Assert.True(File.Exists(outsideChapterPath));
        }
        finally
        {
            if (Directory.Exists(chaptersDirectory)
                && (File.GetAttributes(chaptersDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(chaptersDirectory);
            }
        }
    }

    [Fact]
    public void CountCachedChaptersReturnsZeroForTraversalBookIdOutsideCacheRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = Path.Combine("..", "outside-traversal-book");
        string outsideChaptersDirectory = Path.GetFullPath(
            AppPaths.GetChapterCacheDirectory(root, bookId));
        string outsideChapterPath = Path.Combine(outsideChaptersDirectory, "1.json");
        Directory.CreateDirectory(outsideChaptersDirectory);
        File.WriteAllText(outsideChapterPath, "{}");

        int cachedChapters = CacheStore.CountCachedChapters(root, bookId);

        Assert.Equal(0, cachedChapters);
        Assert.True(File.Exists(outsideChapterPath));
    }

    [Fact]
    public void CountCachedChaptersReturnsZeroForRootedBookIdOutsideCacheRoot()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = Path.Combine(temporaryDirectory.FullPath, "outside-rooted-book");
        string outsideChaptersDirectory = Path.Combine(
            bookId,
            AppConstants.ChaptersDirectoryName);
        string outsideChapterPath = Path.Combine(outsideChaptersDirectory, "1.json");
        Directory.CreateDirectory(outsideChaptersDirectory);
        File.WriteAllText(outsideChapterPath, "{}");

        int cachedChapters = CacheStore.CountCachedChapters(root, bookId);

        Assert.Equal(0, cachedChapters);
        Assert.True(File.Exists(outsideChapterPath));
    }

    [Fact]
    public void CountCachedChaptersDoesNotCountChapterFileSymlink()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        string chaptersDirectory = AppPaths.GetChapterCacheDirectory(root, bookId);
        string outsideChapterPath = Path.Combine(
            temporaryDirectory.FullPath,
            "outside-count-chapter.json");
        string chapterPath = Path.Combine(chaptersDirectory, "1.json");
        Directory.CreateDirectory(chaptersDirectory);
        File.WriteAllText(outsideChapterPath, "{}");
        if (!CanCreateFileSymbolicLink(temporaryDirectory))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            File.CreateSymbolicLink(chapterPath, outsideChapterPath);

            int cachedChapters = CacheStore.CountCachedChapters(root, bookId);

            Assert.Equal(0, cachedChapters);
            Assert.True(File.Exists(outsideChapterPath));
        }
        finally
        {
            if (File.Exists(chapterPath)
                && (File.GetAttributes(chapterPath) & FileAttributes.ReparsePoint) != 0)
            {
                File.Delete(chapterPath);
            }
        }
    }

    [Fact]
    public async Task GetChapterAsyncReturnsNullForMissingParagraphPayload()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
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
        string root = GetCacheRoot(temporaryDirectory);
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
        string root = GetCacheRoot(temporaryDirectory);
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
            DateTimeOffset.UtcNow,
            IsKnownAnonymous: true);

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
        string root = GetCacheRoot(temporaryDirectory);
        CatalogCacheScope scope = CatalogCacheScope.ForValidatedUser("tester");
        CatalogSnapshot catalog = new(
            "1045928363",
            new BookMetadata("1045928363", "Title", "Author", 123456),
            [
                new VolumeDescriptor(
                    "Volume",
                    IsVip: false,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Chapter One",
                            "https://www.qidian.com/chapter/1045928363/1/",
                            IsVip: false,
                            CatalogWordCount: 100,
                            CatalogAccessState: CatalogChapterAccessState.Accessible),
                    ]),
            ],
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
        string root = GetCacheRoot(temporaryDirectory);
        string cachePath = AppPaths.GetCatalogCachePath(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            CreateValidCatalogJson(
                "1045928363",
                "1045928363",
                "1045928363",
                """
                "cacheScope": {
                    "kind": "ValidatedUser",
                    "userName": "tester"
                },
                "isKnownAnonymous": true
                """));

        CatalogSnapshot? catalog = await CacheStore.GetCatalogAsync(
            root,
            "1045928363",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);

        Assert.Null(catalog);
    }

    [Fact]
    public async Task GetCatalogAsyncRejectsAnonymousCatalogWhenNotKnownAnonymous()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        CatalogSnapshot catalog = new(
            bookId,
            new BookMetadata(bookId, "Title", "Author", 123456),
            [
                new VolumeDescriptor(
                    "Volume",
                    IsVip: false,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Chapter One",
                            "https://www.qidian.com/chapter/1045928363/1/",
                            IsVip: false,
                            CatalogWordCount: 100,
                            CatalogAccessState: CatalogChapterAccessState.Accessible),
                    ]),
            ],
            DateTimeOffset.UtcNow,
            CatalogCacheScope.Anonymous,
            IsKnownAnonymous: false);

        await CacheStore.SaveCatalogAsync(root, catalog, CancellationToken.None);
        CatalogSnapshot? roundTripped = await CacheStore.GetCatalogAsync(
            root,
            bookId,
            CatalogCacheScope.Anonymous,
            CancellationToken.None);

        Assert.Null(roundTripped);
    }

    [Fact]
    public async Task GetCatalogAsyncRejectsAnonymousCatalogWhenKnownAnonymousFlagIsMissing()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
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
                "volumes": [
                    {
                        "title": "Volume",
                        "isVip": false,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": "Chapter One",
                                "url": "https://www.qidian.com/chapter/1045928363/1/",
                                "isVip": false,
                                "catalogWordCount": 100,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ],
                "fetchedAtUtc": "2024-01-01T00:00:00+00:00",
                "cacheScope": {
                    "kind": "Anonymous"
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

    [Fact]
    public async Task GetCatalogAsyncKeepsAnonymousAndValidatedCatalogScopesIndependent()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string root = GetCacheRoot(temporaryDirectory);
        string bookId = "1045928363";
        CatalogCacheScope validatedScope = CatalogCacheScope.ForValidatedUser("tester");
        CatalogSnapshot anonymousCatalog = new(
            bookId,
            new BookMetadata(bookId, "Anonymous Title", "Author", 123456),
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
            DateTimeOffset.UtcNow,
            CatalogCacheScope.Anonymous,
            IsKnownAnonymous: true);
        CatalogSnapshot validatedCatalog = anonymousCatalog with
        {
            Metadata = anonymousCatalog.Metadata with { Title = "Validated Title" },
            Volumes =
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
                            CatalogChapterAccessState.Accessible),
                    ]),
            ],
            CacheScope = validatedScope,
        };

        await CacheStore.SaveCatalogAsync(root, anonymousCatalog, CancellationToken.None);
        await CacheStore.SaveCatalogAsync(root, validatedCatalog, CancellationToken.None);

        string anonymousPath = AppPaths.GetCatalogCachePath(
            root,
            bookId,
            CatalogCacheScope.Anonymous);
        string validatedPath = AppPaths.GetCatalogCachePath(root, bookId, validatedScope);
        Assert.NotEqual(anonymousPath, validatedPath);
        Assert.True(File.Exists(anonymousPath));
        Assert.True(File.Exists(validatedPath));

        CatalogSnapshot? anonymousRoundTrip = await CacheStore.GetCatalogAsync(
            root,
            bookId,
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        CatalogSnapshot? validatedRoundTrip = await CacheStore.GetCatalogAsync(
            root,
            bookId,
            validatedScope,
            CancellationToken.None);

        Assert.NotNull(anonymousRoundTrip);
        Assert.NotNull(validatedRoundTrip);
        Assert.Equal("Anonymous Title", anonymousRoundTrip.Metadata.Title);
        Assert.Equal("Validated Title", validatedRoundTrip.Metadata.Title);
        Assert.Equal(
            CatalogChapterAccessState.PurchaseRequired,
            anonymousRoundTrip.Volumes[0].Chapters[0].CatalogAccessState);
        Assert.Equal(
            CatalogChapterAccessState.Accessible,
            validatedRoundTrip.Volumes[0].Chapters[0].CatalogAccessState);
        Assert.Equal(CatalogCacheScope.Anonymous, anonymousRoundTrip.CacheScope);
        Assert.Equal(validatedScope, validatedRoundTrip.CacheScope);
    }

    private static string GetCacheRoot(TemporaryDirectory temporaryDirectory)
        => Path.Combine(
            temporaryDirectory.FullPath,
            "state",
            AppConstants.CacheDirectoryName);

    private static string CreateChapterJson(string chapterId)
        => $$"""
            {
                "chapterId": {{JsonSerializer.Serialize(chapterId)}},
                "paragraphs": ["Outside paragraph"],
                "isPreview": false,
                "catalogWordCount": 100
            }
            """;

    private static string CreateValidCatalogJson(string bookId)
        => CreateValidCatalogJson(
            bookId,
            bookId,
            bookId,
            """
            "cacheScope": {
                "kind": "Anonymous"
            },
            "isKnownAnonymous": true
            """);

    private static string CreateValidCatalogJson(
        string catalogBookId,
        string metadataBookId,
        string chapterBookId,
        string scopeAndKnownAnonymousJson)
        => $$"""
            {
                "bookId": {{JsonSerializer.Serialize(catalogBookId)}},
                "metadata": {
                    "bookId": {{JsonSerializer.Serialize(metadataBookId)}},
                    "title": "Title",
                    "author": "Author",
                    "estimatedWordCount": 123456
                },
                "volumes": [
                    {
                        "title": "Volume",
                        "isVip": false,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": "Chapter One",
                                "url": "https://www.qidian.com/chapter/{{chapterBookId}}/1/",
                                "isVip": false,
                                "catalogWordCount": 100,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ],
                "fetchedAtUtc": "2024-01-01T00:00:00+00:00",
                {{scopeAndKnownAnonymousJson}}
            }
            """;

    private static void WriteClearGeneration(string cacheRoot, string generation)
    {
        string generationPath = CacheStore.GetClearGenerationFilePath(cacheRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(generationPath)!);
        File.WriteAllText(generationPath, generation);
    }

    private static void AssertNoTemporaryFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp", SearchOption.AllDirectories));
    }

    private static void DeleteReparseTemporaryFiles(string cacheRoot)
    {
        string stagingDirectory = Path.Combine(cacheRoot, ".staging");
        if (!Directory.Exists(stagingDirectory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(stagingDirectory, "*.tmp"))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                File.Delete(file);
            }
        }
    }

    private static void DeleteReparseDirectoryIfExists(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
        }
    }

    private static bool CanCreateDirectorySymbolicLink(TemporaryDirectory temporaryDirectory)
    {
        string target = Path.Combine(temporaryDirectory.FullPath, "symlink-target");
        string link = Path.Combine(temporaryDirectory.FullPath, "symlink-link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Directory.Delete(link);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }
    }

    private static bool CanCreateFileSymbolicLink(TemporaryDirectory temporaryDirectory)
    {
        string target = Path.Combine(temporaryDirectory.FullPath, "file-symlink-target");
        string link = Path.Combine(temporaryDirectory.FullPath, "file-symlink-link");
        File.WriteAllText(target, "target");
        try
        {
            File.CreateSymbolicLink(link, target);
            File.Delete(link);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }
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
