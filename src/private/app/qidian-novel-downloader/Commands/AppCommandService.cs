using Hcoona.QidianNovelDownloader.Browser;
using Hcoona.QidianNovelDownloader.Cache;
using Hcoona.QidianNovelDownloader.Output;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace Hcoona.QidianNovelDownloader.Commands;

internal sealed class AppCommandService(
    IOptions<AppSettings> settingsOptions,
    IQidianBrowserManager browserManager,
    IInteractiveConsole interactiveConsole,
    TimeProvider timeProvider,
    IAppStorageService storageService,
    ILogger<AppCommandService> logger)
{
    public async Task<int> DownloadAsync(
        DownloadCommandOptions options,
        CancellationToken cancellationToken)
    {
        int completedBooks = 0;
        int skippedBooks = 0;
        int failedBooks = 0;
        int downloadedChapters = 0;
        int reusedChapters = 0;
        int failedChapters = 0;

        DownloadCommandSummary BuildDownloadSummary(bool fallbackFailure)
            => new(
                completedBooks,
                skippedBooks,
                fallbackFailure && failedBooks == 0 && completedBooks == 0
                    ? 1
                    : failedBooks,
                downloadedChapters,
                reusedChapters,
                failedChapters);

        try
        {
            ResolvedAppSettings settings = Validate(ResolvedAppSettings.Merge(settingsOptions.Value, options));
            AppStoragePaths paths = EnsureStorage(settings);
            List<BookReference> targets = DownloadTargetResolver.Resolve(
                options.BookReferences,
                settings.DefaultBooks);

            IQidianBrowserSession? browser = null;
            bool browserHeadless = true;
            LoginState? loginState = null;
            try
            {
                async Task<IQidianBrowserSession> OpenBrowserAsync(bool headless)
                {
                    if (browser is not null && browserHeadless == headless)
                    {
                        return browser;
                    }

                    if (browser is not null)
                    {
                        await browser.DisposeAsync();
                        browser = null;
                    }

                    browser = await browserManager.OpenAsync(
                        settings,
                        paths,
                        headless,
                        cancellationToken);
                    browserHeadless = headless;
                    loginState = null;
                    return browser;
                }

                Task<IQidianBrowserSession> GetBrowserAsync()
                    => OpenBrowserAsync(browser is null ? true : browserHeadless);

                async Task<LoginState> GetCurrentLoginStateAsync(bool forceRefresh = false)
                {
                    if (forceRefresh || loginState is null)
                    {
                        loginState = await (await GetBrowserAsync()).GetLoginStateAsync(
                            AppConstants.QidianBaseUrl,
                            cancellationToken);
                    }

                    return loginState;
                }

                async Task<LoginState> EnsureValidatedLoginStateAsync(LoginState? currentState = null)
                {
                    currentState ??= await GetCurrentLoginStateAsync(forceRefresh: true);
                    if (currentState.IsLoggedIn)
                    {
                        return currentState;
                    }

                    Console.WriteLine(
                        "Authentication is required. Opening a visible browser window for manual sign-in.");
                    await OpenBrowserAsync(headless: false);
                    await browser!.WaitForManualLoginAsync(cancellationToken);
                    LoginState validatedState = await GetCurrentLoginStateAsync(forceRefresh: true);
                    if (!validatedState.IsLoggedIn)
                    {
                        throw new OperationalException(
                            "Manual login completed, but the session could not be validated.");
                    }

                    Console.WriteLine("Login confirmed. Continuing with the validated session.");
                    return validatedState;
                }

                for (int bookIndex = 0; bookIndex < targets.Count; bookIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BookReference target = targets[bookIndex];
                    Console.WriteLine(
                        $"[{bookIndex + 1}/{targets.Count}] Processing book {target.BookId}...");

                    try
                    {
                        CatalogSnapshot? cachedCatalog = await CacheStore.GetCatalogAsync(
                            paths.CacheRoot,
                            target.BookId,
                            cancellationToken);
                        bool hasFreshCatalog = cachedCatalog is not null && CacheStore.IsCatalogFresh(
                            cachedCatalog,
                            settings.CatalogCacheTtlHours,
                            timeProvider);
                        CatalogSnapshot catalog = hasFreshCatalog
                            ? cachedCatalog!
                            : await GetCatalogAsync(
                                target.BookId,
                                settings,
                                paths,
                                GetBrowserAsync,
                                forceRefresh: true,
                                cancellationToken);
                        List<ChapterPlan> plans = await BuildChapterPlansAsync(
                            catalog,
                            paths.CacheRoot,
                            validatedLoginState: null,
                            cancellationToken);
                        LoginState? currentLoginState = null;
                        if (RequiresAuthenticatedCacheReuseEvaluation(plans))
                        {
                            currentLoginState = await GetCurrentLoginStateAsync(forceRefresh: true);
                            if (!currentLoginState.IsLoggedIn)
                            {
                                currentLoginState = await EnsureValidatedLoginStateAsync(currentLoginState);
                            }

                            plans = await BuildChapterPlansAsync(
                                catalog,
                                paths.CacheRoot,
                                currentLoginState,
                                cancellationToken);
                        }

                        if (options.DryRun)
                        {
                            PrintDryRun(catalog, plans);
                            completedBooks++;
                            reusedChapters += plans.Count(plan => plan.Status == ChapterPlanStatus.Cached);
                            continue;
                        }

                        string outputPath = AppPaths.BuildDefaultOutputPath(
                            paths.OutputRoot,
                            catalog.Metadata.BookId,
                            catalog.Metadata.Title,
                            catalog.Metadata.Author);

                        if (File.Exists(outputPath) && !options.Overwrite)
                        {
                            bool approved = await interactiveConsole.ConfirmAsync(
                                $"The output file '{outputPath}' already exists. Overwrite it?",
                                cancellationToken);
                            if (!approved)
                            {
                                Console.WriteLine(
                                    $"Skipped '{outputPath}' because overwrite was not approved.");
                                skippedBooks++;
                                continue;
                            }
                        }

                        Dictionary<string, RenderedChapter> renderedChapters = new(StringComparer.Ordinal);
                        List<ChapterPlan> orderedPlans = plans;
                        for (int chapterIndex = 0; chapterIndex < orderedPlans.Count; chapterIndex++)
                        {
                            ChapterPlan plan = orderedPlans[chapterIndex];
                            Console.WriteLine(
                                $"  [{chapterIndex + 1}/{orderedPlans.Count}] "
                                + $"{(plan.Status == ChapterPlanStatus.Cached ? "Reusing" : "Fetching")} "
                                + $"{plan.Chapter.Title}");

                            if (plan.Status == ChapterPlanStatus.Cached && plan.CachedEntry is not null)
                            {
                                renderedChapters[plan.Chapter.ChapterId] = new RenderedChapter(
                                    plan.Chapter.ChapterId,
                                    plan.Chapter.Title,
                                    plan.CachedEntry.Paragraphs,
                                    FromCache: true,
                                    Failed: false);
                                reusedChapters++;
                                continue;
                            }

                            ChapterFetchResult? chapterResult = await FetchChapterWithRetryAsync(
                                await GetBrowserAsync(),
                                catalog.Metadata.BookId,
                                plan.Chapter,
                                settings,
                                cancellationToken);
                            if (chapterResult is null)
                            {
                                renderedChapters[plan.Chapter.ChapterId] = new RenderedChapter(
                                    plan.Chapter.ChapterId,
                                    plan.Chapter.Title,
                                    [AppConstants.FailedChapterPlaceholder],
                                    FromCache: false,
                                    Failed: true);
                                failedChapters++;
                            }
                            else
                            {
                                IReadOnlyList<string> paragraphs = NormalizeFetchedParagraphs(chapterResult);
                                if (plan.Chapter.IsVip
                                    && !chapterResult.IsPreview
                                    && currentLoginState is null)
                                {
                                    currentLoginState = await GetCurrentLoginStateAsync(forceRefresh: true);
                                }

                                ChapterCacheEntry cacheEntry = new(
                                    plan.Chapter.ChapterId,
                                    plan.Chapter.Title,
                                    paragraphs,
                                    chapterResult.IsPreview,
                                    plan.Chapter.CatalogWordCount,
                                    timeProvider.GetUtcNow(),
                                    AppPaths.ComputeContentHash(paragraphs),
                                    GetVisibleToUserName(plan.Chapter, chapterResult, currentLoginState));
                                await CacheStore.SaveChapterAsync(
                                    paths.CacheRoot,
                                    catalog.Metadata.BookId,
                                    cacheEntry,
                                    cancellationToken);
                                renderedChapters[plan.Chapter.ChapterId] = new RenderedChapter(
                                    plan.Chapter.ChapterId,
                                    plan.Chapter.Title,
                                    paragraphs,
                                    FromCache: false,
                                    Failed: false);
                                downloadedChapters++;
                            }

                            if (chapterIndex < orderedPlans.Count - 1)
                            {
                                TimeSpan delay = RequestDelayPlanner.CalculateDelay(
                                    plan.Chapter.CatalogWordCount,
                                    settings);
                                await Task.Delay(delay, cancellationToken);
                            }
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                        string markdown = MarkdownRenderer.Render(catalog, renderedChapters);
                        await File.WriteAllTextAsync(
                            outputPath,
                            markdown,
                            Encoding.UTF8,
                            cancellationToken);
                        Console.WriteLine($"Wrote '{outputPath}'.");
                        completedBooks++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        LogMessages.BookProcessingFailed(logger, target.BookId, exception);
                        Console.Error.WriteLine(
                            $"ERROR: Failed to process book {target.BookId}: {exception.Message}");
                        failedBooks++;
                    }
                }

                DownloadCommandSummary summary = BuildDownloadSummary(fallbackFailure: false);
                Console.WriteLine(summary);
                return summary.HasFailures ? ExitCodes.OperationalFailure : ExitCodes.Success;
            }
            finally
            {
                if (browser is not null)
                {
                    await browser.DisposeAsync();
                }
            }
        }
        catch (CliInputException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.WriteLine(BuildDownloadSummary(fallbackFailure: true));
            return ExitCodes.UsageFailure;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogMessages.DownloadFailed(logger, exception);
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            Console.WriteLine(BuildDownloadSummary(fallbackFailure: true));
            return ExitCodes.OperationalFailure;
        }
    }

    public async Task<int> LoginAsync(
        LoginCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            ResolvedAppSettings settings = Validate(ResolvedAppSettings.Merge(settingsOptions.Value, options));
            AppStoragePaths paths = EnsureStorage(settings);

            IQidianBrowserSession? browser = await browserManager.OpenAsync(
                settings,
                paths,
                headless: false,
                cancellationToken);
            try
            {
                Console.WriteLine("A visible browser window has been opened. Complete sign-in manually.");
                await browser.WaitForManualLoginAsync(cancellationToken);
                await browser.PersistSessionStateAsync();
                browser = null;
            }
            finally
            {
                if (browser is not null)
                {
                    await browser.DisposeAsync();
                }
            }

            Console.WriteLine("Login confirmed and session state persisted.");
            Console.WriteLine(new CommandSummary(1, 0, 0, 0));
            return ExitCodes.Success;
        }
        catch (CliInputException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.WriteLine(new CommandSummary(0, 0, 0, 1));
            return ExitCodes.UsageFailure;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogMessages.LoginFailed(logger, exception);
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            Console.WriteLine(new CommandSummary(0, 0, 0, 1));
            return ExitCodes.OperationalFailure;
        }
    }

    public Task<int> CacheClearAsync(
        CacheClearCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            AppStoragePaths paths = storageService.Resolve(settingsOptions.Value);
            string? bookId = options.BookReference is null
                ? null
                : BookReferenceParser.Parse(options.BookReference).BookId;
            int removed = CacheStore.Clear(paths.CacheRoot, bookId, options.CatalogOnly);

            if (removed == 0)
            {
                Console.WriteLine("No cache data was removed.");
                Console.WriteLine(new CommandSummary(1, 0, 0, 0));
            }
            else
            {
                Console.WriteLine($"Removed {removed} cache item(s).");
                Console.WriteLine(new CommandSummary(1, 0, 0, 0));
            }

            return Task.FromResult(ExitCodes.Success);
        }
        catch (CliInputException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return Task.FromResult(ExitCodes.UsageFailure);
        }
        catch (Exception exception)
        {
            LogMessages.CacheClearFailed(logger, exception);
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return Task.FromResult(ExitCodes.OperationalFailure);
        }
    }

    public async Task<int> InfoAsync(
        InfoCommandOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            ResolvedAppSettings settings = Validate(ResolvedAppSettings.Merge(settingsOptions.Value, options));
            AppStoragePaths paths = EnsureStorage(settings);

            await using IQidianBrowserSession browser = await browserManager.OpenAsync(
                settings,
                paths,
                headless: true,
                cancellationToken);
            BookReference target = BookReferenceParser.Parse(options.BookReference);
            CatalogSnapshot catalog = await GetCatalogAsync(
                target.BookId,
                settings,
                paths,
                () => Task.FromResult(browser),
                forceRefresh: false,
                cancellationToken);

            int totalChapters = catalog.Volumes.Sum(volume => volume.Chapters.Count);
            int cachedChapters = CacheStore.CountCachedChapters(paths.CacheRoot, catalog.BookId);

            Console.WriteLine($"Book ID: {catalog.Metadata.BookId}");
            Console.WriteLine($"Title: {catalog.Metadata.Title}");
            Console.WriteLine($"Author: {catalog.Metadata.Author}");
            Console.WriteLine($"Total chapters: {totalChapters}");
            Console.WriteLine(
                $"Estimated word count: {(catalog.Metadata.EstimatedWordCount?.ToString() ?? "n/a")}");
            Console.WriteLine($"Cache coverage: {cachedChapters}/{totalChapters} chapter(s)");
            Console.WriteLine("Volumes:");
            foreach (VolumeDescriptor volume in catalog.Volumes)
            {
                Console.WriteLine(
                    $"- {volume.Title}: {volume.Chapters.Count} chapter(s) "
                    + $"({(volume.IsVip ? "VIP" : "Free")})");
            }

            Console.WriteLine(new CommandSummary(1, 0, 0, 0));
            return ExitCodes.Success;
        }
        catch (CliInputException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.WriteLine(new CommandSummary(0, 0, 0, 1));
            return ExitCodes.UsageFailure;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogMessages.InfoFailed(logger, exception);
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            Console.WriteLine(new CommandSummary(0, 0, 0, 1));
            return ExitCodes.OperationalFailure;
        }
    }

    private async Task<CatalogSnapshot> GetCatalogAsync(
        string bookId,
        ResolvedAppSettings settings,
        AppStoragePaths paths,
        Func<Task<IQidianBrowserSession>> getBrowserAsync,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh)
        {
            CatalogSnapshot? cachedCatalog = await CacheStore.GetCatalogAsync(
                paths.CacheRoot,
                bookId,
                cancellationToken);
            if (cachedCatalog is not null && CacheStore.IsCatalogFresh(
                    cachedCatalog,
                    settings.CatalogCacheTtlHours,
                    timeProvider))
            {
                return cachedCatalog;
            }
        }

        CatalogSnapshot fetchedCatalog = await (await getBrowserAsync()).FetchCatalogAsync(
            bookId,
            cancellationToken);
        await CacheStore.SaveCatalogAsync(paths.CacheRoot, fetchedCatalog, cancellationToken);
        return fetchedCatalog;
    }

    internal static async Task<List<ChapterPlan>> BuildChapterPlansAsync(
        CatalogSnapshot catalog,
        string cacheRoot,
        LoginState? validatedLoginState,
        CancellationToken cancellationToken)
    {
        List<ChapterPlan> plans = [];
        foreach (ChapterDescriptor chapter in catalog.Volumes.SelectMany(volume => volume.Chapters))
        {
            ChapterCacheEntry? cachedEntry = await CacheStore.GetChapterAsync(
                cacheRoot,
                catalog.BookId,
                chapter.ChapterId,
                cancellationToken);
            ChapterPlanStatus status;
            if (cachedEntry is null)
            {
                status = ChapterPlanStatus.FetchRequired;
            }
            else if (cachedEntry.CatalogWordCount != chapter.CatalogWordCount)
            {
                status = ChapterPlanStatus.Changed;
            }
            else if (CanReuseCachedChapter(chapter, cachedEntry, validatedLoginState))
            {
                status = ChapterPlanStatus.Cached;
            }
            else
            {
                status = ChapterPlanStatus.FetchRequired;
            }

            plans.Add(new ChapterPlan(chapter, status, cachedEntry));
        }

        return plans;
    }

    private async Task<ChapterFetchResult?> FetchChapterWithRetryAsync(
        IQidianBrowserSession browser,
        string bookId,
        ChapterDescriptor chapter,
        ResolvedAppSettings settings,
        CancellationToken cancellationToken)
    {
        int totalAttempts = settings.RetryCount + 1;
        for (int attempt = 1; attempt <= totalAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ChapterFetchResult result = await browser.FetchChapterAsync(
                    bookId,
                    chapter,
                    cancellationToken);
                if (result.Paragraphs.Count > 0)
                {
                    return result;
                }

                throw new OperationalException(
                    $"Chapter '{chapter.Title}' returned no visible content.");
            }
            catch (Exception exception) when (attempt < totalAttempts)
            {
                LogMessages.ChapterRetry(
                    logger,
                    attempt,
                    totalAttempts,
                    chapter.ChapterId,
                    exception);
                TimeSpan retryDelay = TimeSpan.FromSeconds(Math.Min(30 * attempt, 120));
                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        return null;
    }

    private static List<string> NormalizeFetchedParagraphs(ChapterFetchResult result)
    {
        List<string> paragraphs = [.. result.Paragraphs];
        if (result.IsPreview)
        {
            paragraphs.Add(AppConstants.TruncatedChapterMarker);
        }

        return paragraphs;
    }

    private static bool RequiresAuthenticatedCacheReuseEvaluation(IReadOnlyList<ChapterPlan> plans)
        => plans.Any(
            plan => plan.Chapter.IsVip
                && plan.Status == ChapterPlanStatus.FetchRequired
                && plan.CachedEntry is { IsPreview: false });

    private static bool CanReuseCachedChapter(
        ChapterDescriptor chapter,
        ChapterCacheEntry cachedEntry,
        LoginState? validatedLoginState)
    {
        if (!chapter.IsVip)
        {
            return true;
        }

        if (validatedLoginState is not { IsLoggedIn: true, UserName: { Length: > 0 } userName })
        {
            return cachedEntry.IsPreview;
        }

        if (cachedEntry.IsPreview)
        {
            return false;
        }

        return string.Equals(
            cachedEntry.VisibleToUserName,
            userName,
            StringComparison.Ordinal);
    }

    private static string? GetVisibleToUserName(
        ChapterDescriptor chapter,
        ChapterFetchResult chapterResult,
        LoginState? validatedLoginState)
        => chapter.IsVip && !chapterResult.IsPreview && validatedLoginState?.IsLoggedIn == true
            ? validatedLoginState.UserName
            : null;

    private static ResolvedAppSettings Validate(ResolvedAppSettings settings)
    {
        if (settings.ReadingSpeed <= 0)
        {
            throw new CliInputException("Reading speed must be greater than zero.");
        }

        if (settings.MinimumRequestDelaySeconds <= 0)
        {
            throw new CliInputException("Minimum request delay must be greater than zero.");
        }

        if (settings.MaximumRequestDelaySeconds < settings.MinimumRequestDelaySeconds)
        {
            throw new CliInputException(
                "Maximum request delay must be greater than or equal to the minimum delay.");
        }

        if (settings.RetryCount < 0)
        {
            throw new CliInputException("Retry count cannot be negative.");
        }

        if (settings.CatalogCacheTtlHours <= 0)
        {
            throw new CliInputException("Catalog cache TTL must be greater than zero.");
        }

        return settings;
    }

    private AppStoragePaths EnsureStorage(ResolvedAppSettings settings)
        => storageService.EnsureStorage(settings.ToAppSettings());

    private static void PrintDryRun(CatalogSnapshot catalog, IReadOnlyList<ChapterPlan> plans)
    {
        Console.WriteLine($"Dry-run plan for {catalog.Metadata.BookId} - {catalog.Metadata.Title}");
        foreach (ChapterPlan plan in plans)
        {
            Console.WriteLine(
                $"- {plan.Chapter.Title}: {plan.Status switch
                {
                    ChapterPlanStatus.Cached => "cached",
                    ChapterPlanStatus.Changed => "changed",
                    _ => "fetch",
                }}");
        }

        Console.WriteLine(
            "Dry-run summary: "
            + $"cached={plans.Count(plan => plan.Status == ChapterPlanStatus.Cached)}, "
            + $"changed={plans.Count(plan => plan.Status == ChapterPlanStatus.Changed)}, "
            + $"fetch-required={plans.Count(plan => plan.Status == ChapterPlanStatus.FetchRequired)}.");
    }
}
