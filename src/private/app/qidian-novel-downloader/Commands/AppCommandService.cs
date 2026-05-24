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
            ResolvedAppSettings settings = ValidateDownload(
                ResolvedAppSettings.Merge(settingsOptions.Value, options));
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

                async Task<LoginState> GetCurrentLoginStateAsync(
                    bool forceRefresh = false,
                    LoginStateProbeMode probeMode = LoginStateProbeMode.WaitForValidatedIdentity)
                {
                    if (forceRefresh
                        || loginState is null
                        || (probeMode == LoginStateProbeMode.WaitForValidatedIdentity
                            && !loginState.IsValidated))
                    {
                        LoginState probedLoginState = await (await GetBrowserAsync()).GetLoginStateAsync(
                            AppConstants.QidianBaseUrl,
                            cancellationToken,
                            probeMode: probeMode);
                        loginState = SelectCachedLoginStateForProbe(
                            loginState,
                            probedLoginState);
                    }

                    return loginState;
                }

                async Task<LoginState?> TryGetCurrentLoginStateAsync(
                    Action<ILogger, Exception?> logFailure,
                    LoginStateProbeMode probeMode = LoginStateProbeMode.WaitForValidatedIdentity)
                {
                    try
                    {
                        return await GetCurrentLoginStateAsync(
                            forceRefresh: true,
                            probeMode: probeMode);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logFailure(logger, exception);
                        return null;
                    }
                }

                async Task<(LoginState LoginState, bool ManualLoginCompleted)>
                    EnsureValidatedLoginStateAsync(
                    LoginState? currentState = null)
                {
                    currentState = currentState switch
                    {
                        null => await GetCurrentLoginStateAsync(forceRefresh: true),
                        { IsValidated: false } => await GetCurrentLoginStateAsync(
                            forceRefresh: true,
                            probeMode: LoginStateProbeMode.WaitForValidatedIdentity),
                        _ => currentState,
                    };
                    if (currentState.IsValidated)
                    {
                        return (currentState, ManualLoginCompleted: false);
                    }

                    Console.WriteLine(
                        "Authentication is required. Opening a visible browser "
                        + "window for manual sign-in.");
                    await OpenBrowserAsync(headless: false);
                    await browser!.WaitForManualLoginAsync(
                        cancellationToken,
                        requireValidatedIdentity: true);
                    await browser.PersistSessionStateAsync();
                    browser = null;
                    LoginState validatedState = await GetCurrentLoginStateAsync(forceRefresh: true);
                    Console.WriteLine("Login confirmed. Continuing with the validated session.");
                    return (validatedState, ManualLoginCompleted: true);
                }

                async Task<(
                    CatalogSnapshot Catalog,
                    List<ChapterPlan> Plans,
                    LoginState? LoginState,
                    bool VipFullContentClassificationProbeCompleted)>
                    ResolveCatalogAndPlansForDownloadAsync(BookReference target)
                {
                    LoginState? currentLoginState = GetValidatedLoginState(loginState);
                    bool hasProbedCurrentLoginState = false;
                    bool vipFullContentClassificationProbeCompleted = false;

                    async Task<(CatalogSnapshot Catalog, List<ChapterPlan> Plans)>
                        GetValidatedCatalogAndPlansAsync(
                            LoginState validatedLoginState,
                            bool forceRefresh)
                    {
                        (
                            CatalogSnapshot validatedCatalog,
                            bool reusedCachedValidatedCatalog) = await GetCatalogAsync(
                            target.BookId,
                            settings,
                            paths,
                            GetBrowserAsync,
                            CatalogCacheScope.ForValidatedUser(
                                validatedLoginState.UserName!),
                            forceRefresh,
                            cancellationToken);
                        List<ChapterPlan> validatedPlans = await BuildChapterPlansAsync(
                            validatedCatalog,
                            paths.CacheRoot,
                            validatedLoginState,
                            cancellationToken);
                        if (reusedCachedValidatedCatalog
                            && RequiresValidatedCatalogRefreshForEntitlementMismatch(
                                validatedPlans,
                                validatedLoginState))
                        {
                            (validatedCatalog, _) = await GetCatalogAsync(
                                target.BookId,
                                settings,
                                paths,
                                GetBrowserAsync,
                                CatalogCacheScope.ForValidatedUser(
                                    validatedLoginState.UserName!),
                                forceRefresh: true,
                                cancellationToken);
                            validatedPlans = await BuildChapterPlansAsync(
                                validatedCatalog,
                                paths.CacheRoot,
                                validatedLoginState,
                                cancellationToken);
                        }

                        return (validatedCatalog, validatedPlans);
                    }

                    async Task<LoginState?> ProbeCurrentLoginStateAsync(
                        LoginStateProbeMode probeMode)
                    {
                        hasProbedCurrentLoginState = true;
                        currentLoginState = await TryGetCurrentLoginStateAsync(
                            LogMessages.IgnoreAuthenticatedCacheReuseProbeFailure,
                            probeMode);
                        return currentLoginState;
                    }

                    if (currentLoginState is not null)
                    {
                        (
                            CatalogSnapshot validatedCatalog,
                            List<ChapterPlan> validatedPlans) =
                            await GetValidatedCatalogAndPlansAsync(
                                currentLoginState,
                                forceRefresh: false);
                        return (
                            validatedCatalog,
                            validatedPlans,
                            currentLoginState,
                            vipFullContentClassificationProbeCompleted);
                    }

                    CatalogSnapshot? anonymousCatalog = await TryGetFreshCatalogAsync(
                        target.BookId,
                        settings,
                        paths,
                        CatalogCacheScope.Anonymous,
                        cancellationToken);
                    bool isFreshAnonymousCatalog = anonymousCatalog is not null;
                    if (anonymousCatalog is null)
                    {
                        LoginState? initialLoginState = await ProbeCurrentLoginStateAsync(
                            LoginStateProbeMode.CurrentStateOnly);
                        LoginState? initialValidatedLoginState = GetValidatedLoginState(
                            initialLoginState);
                        if (initialValidatedLoginState is not null)
                        {
                            (
                                CatalogSnapshot validatedCatalog,
                                List<ChapterPlan> validatedPlans) =
                                await GetValidatedCatalogAndPlansAsync(
                                    initialValidatedLoginState,
                                    forceRefresh: false);
                            return (
                                validatedCatalog,
                                validatedPlans,
                                currentLoginState,
                                vipFullContentClassificationProbeCompleted);
                        }
                    }

                    CatalogSnapshot catalog = anonymousCatalog ?? await FetchCatalogAsync(
                        target.BookId,
                        paths,
                        GetBrowserAsync,
                        CatalogCacheScope.Anonymous,
                        cancellationToken);

                    List<ChapterPlan> plans = await BuildChapterPlansAsync(
                        catalog,
                        paths.CacheRoot,
                        validatedLoginState: null,
                        cancellationToken);
                    if (RequiresCurrentSessionCatalogEvaluation(
                        plans,
                        isFreshAnonymousCatalog))
                    {
                        if (!hasProbedCurrentLoginState)
                        {
                            await ProbeCurrentLoginStateAsync(
                                LoginStateProbeMode.CurrentStateOnly);
                        }

                        if (RequiresAuthenticatedPlanEvaluation(plans)
                            && currentLoginState is not null
                            && !currentLoginState.IsValidated)
                        {
                            (
                                currentLoginState,
                                bool manualLoginCompleted) =
                                await EnsureValidatedLoginStateAsync(currentLoginState);
                            if (manualLoginCompleted)
                            {
                                (catalog, plans) = await GetValidatedCatalogAndPlansAsync(
                                    currentLoginState,
                                    forceRefresh: true);
                                return (
                                    catalog,
                                    plans,
                                    currentLoginState,
                                    vipFullContentClassificationProbeCompleted);
                            }
                        }

                        LoginState? validatedLoginState = GetValidatedLoginState(
                            currentLoginState);
                        if (validatedLoginState is not null)
                        {
                            (catalog, plans) = await GetValidatedCatalogAndPlansAsync(
                                validatedLoginState,
                                forceRefresh: false);
                        }
                    }

                    return (
                        catalog,
                        plans,
                        currentLoginState,
                        vipFullContentClassificationProbeCompleted);
                }

                for (int bookIndex = 0; bookIndex < targets.Count; bookIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BookReference target = targets[bookIndex];
                    Console.WriteLine(
                        $"[{bookIndex + 1}/{targets.Count}] Processing book {target.BookId}...");

                    try
                    {
                        (
                            CatalogSnapshot catalog,
                            List<ChapterPlan> plans,
                            LoginState? currentLoginState,
                            bool vipFullContentClassificationProbeCompleted) =
                            await ResolveCatalogAndPlansForDownloadAsync(target);

                        if (options.DryRun)
                        {
                            PrintDryRun(catalog, plans);
                            completedBooks++;
                            reusedChapters += plans.Count(
                                plan => plan.Status == ChapterPlanStatus.Cached);
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

                        Dictionary<string, RenderedChapter> renderedChapters =
                            new(StringComparer.Ordinal);
                        List<ChapterPlan> orderedPlans = plans;
                        for (
                            int chapterIndex = 0;
                            chapterIndex < orderedPlans.Count;
                            chapterIndex++)
                        {
                            ChapterPlan plan = orderedPlans[chapterIndex];
                            bool canReuseCachedPlan = CanReuseCachedPlanForCurrentLoginState(
                                plan,
                                currentLoginState);
                            string chapterAction = canReuseCachedPlan
                                ? "Reusing"
                                : "Fetching";
                            Console.WriteLine(
                                $"  [{chapterIndex + 1}/{orderedPlans.Count}] "
                                + $"{chapterAction} "
                                + $"{plan.Chapter.Title}");

                            if (canReuseCachedPlan)
                            {
                                renderedChapters[plan.Chapter.ChapterId] = new RenderedChapter(
                                    plan.Chapter.Title,
                                    plan.CachedEntry!.Paragraphs);
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
                                    plan.Chapter.Title,
                                    [AppConstants.FailedChapterPlaceholder]);
                                failedChapters++;
                            }
                            else
                            {
                                IReadOnlyList<string> paragraphs =
                                    NormalizeFetchedParagraphs(chapterResult);
                                if (plan.Chapter.IsVip
                                    && !chapterResult.IsPreview)
                                {
                                    if (currentLoginState is not { IsValidated: true }
                                        && !vipFullContentClassificationProbeCompleted)
                                    {
                                        LoginState? classificationLoginState =
                                            await TryGetCurrentLoginStateAsync(
                                                LogMessages
                                                    .IgnoreVipFullContentClassificationProbeFailure,
                                                LoginStateProbeMode.WaitForValidatedIdentity);
                                        if (classificationLoginState is not null)
                                        {
                                            currentLoginState = classificationLoginState;
                                            vipFullContentClassificationProbeCompleted = true;
                                        }
                                    }
                                }

                                LoginState? vipFullContentAttributionLoginState =
                                    currentLoginState is { IsValidated: true }
                                    || vipFullContentClassificationProbeCompleted
                                        ? currentLoginState
                                        : null;

                                ChapterCacheEntry cacheEntry = new(
                                    plan.Chapter.ChapterId,
                                    paragraphs,
                                    chapterResult.IsPreview,
                                    plan.Chapter.CatalogWordCount,
                                    GetCachedCatalogAccessState(
                                        plan.Chapter,
                                        chapterResult,
                                        vipFullContentAttributionLoginState),
                                    GetVisibleToUserName(
                                        plan.Chapter,
                                        chapterResult,
                                        vipFullContentAttributionLoginState),
                                    GetVipFullContentCacheProvenance(
                                        plan.Chapter,
                                        chapterResult,
                                        vipFullContentAttributionLoginState));
                                await CacheStore.SaveChapterAsync(
                                    paths.CacheRoot,
                                    catalog.Metadata.BookId,
                                    cacheEntry,
                                    cancellationToken);
                                renderedChapters[plan.Chapter.ChapterId] = new RenderedChapter(
                                    plan.Chapter.Title,
                                    paragraphs);
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
            ResolvedAppSettings settings = ValidateLogin(
                ResolvedAppSettings.Merge(settingsOptions.Value, options));
            AppStoragePaths paths = EnsureStorage(settings);

            IQidianBrowserSession? browser = await browserManager.OpenAsync(
                settings,
                paths,
                headless: false,
                cancellationToken);
            try
            {
                Console.WriteLine(
                    "A visible browser window has been opened. Complete sign-in manually.");
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
            Console.WriteLine(new CommandSummary(0, 0, 0, 1));
            return Task.FromResult(ExitCodes.UsageFailure);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogMessages.CacheClearFailed(logger, exception);
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            Console.WriteLine(new CommandSummary(0, 0, 0, 1));
            return Task.FromResult(ExitCodes.OperationalFailure);
        }
    }

    public async Task<int> InfoAsync(
        InfoCommandOptions options,
        CancellationToken cancellationToken)
    {
        IQidianBrowserSession? browser = null;
        try
        {
            ResolvedAppSettings settings = ValidateInfo(
                ResolvedAppSettings.Merge(settingsOptions.Value, options));
            AppStoragePaths paths = EnsureStorage(settings);
            BookReference target = BookReferenceParser.Parse(options.BookReference);

            async Task<IQidianBrowserSession> GetBrowserAsync()
                => browser ??= await browserManager.OpenAsync(
                    settings,
                    paths,
                    headless: true,
                    cancellationToken);

            (CatalogSnapshot catalog, _) = await GetCatalogAsync(
                target.BookId,
                settings,
                paths,
                GetBrowserAsync,
                CatalogCacheScope.Anonymous,
                forceRefresh: false,
                cancellationToken);

            int totalChapters = catalog.Volumes.Sum(volume => volume.Chapters.Count);
            int cachedChapters = CacheStore.CountCachedChapters(paths.CacheRoot, catalog.BookId);

            Console.WriteLine($"Book ID: {catalog.Metadata.BookId}");
            Console.WriteLine($"Title: {catalog.Metadata.Title}");
            Console.WriteLine($"Author: {catalog.Metadata.Author}");
            Console.WriteLine($"Total chapters: {totalChapters}");
            Console.WriteLine(
                "Estimated word count: "
                + (catalog.Metadata.EstimatedWordCount?.ToString() ?? "n/a"));
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
        finally
        {
            if (browser is not null)
            {
                await browser.DisposeAsync();
            }
        }
    }

    private async Task<(CatalogSnapshot Catalog, bool ReusedCache)> GetCatalogAsync(
        string bookId,
        ResolvedAppSettings settings,
        AppStoragePaths paths,
        Func<Task<IQidianBrowserSession>> getBrowserAsync,
        CatalogCacheScope scope,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!forceRefresh)
        {
            CatalogSnapshot? cachedCatalog = await CacheStore.GetCatalogAsync(
                paths.CacheRoot,
                bookId,
                scope,
                cancellationToken);
            if (cachedCatalog is not null && CacheStore.IsCatalogFresh(
                    cachedCatalog,
                    settings.CatalogCacheTtlHours,
                    timeProvider))
            {
                return (cachedCatalog, ReusedCache: true);
            }
        }

        CatalogSnapshot fetchedCatalog = await FetchCatalogAsync(
            bookId,
            paths,
            getBrowserAsync,
            scope,
            cancellationToken);
        return (fetchedCatalog, ReusedCache: false);
    }

    private static async Task<CatalogSnapshot> FetchCatalogAsync(
        string bookId,
        AppStoragePaths paths,
        Func<Task<IQidianBrowserSession>> getBrowserAsync,
        CatalogCacheScope scope,
        CancellationToken cancellationToken)
    {
        CatalogSnapshot fetchedCatalog = await (await getBrowserAsync()).FetchCatalogAsync(
            bookId,
            cancellationToken);
        fetchedCatalog = fetchedCatalog with { CacheScope = scope };
        if (CanSaveCatalogSnapshot(scope, fetchedCatalog))
        {
            await CacheStore.SaveCatalogAsync(paths.CacheRoot, fetchedCatalog, cancellationToken);
        }

        return fetchedCatalog;
    }

    private async Task<CatalogSnapshot?> TryGetFreshCatalogAsync(
        string bookId,
        ResolvedAppSettings settings,
        AppStoragePaths paths,
        CatalogCacheScope scope,
        CancellationToken cancellationToken)
    {
        CatalogSnapshot? cachedCatalog = await CacheStore.GetCatalogAsync(
            paths.CacheRoot,
            bookId,
            scope,
            cancellationToken);
        return cachedCatalog is not null
            && CacheStore.IsCatalogFresh(
                cachedCatalog,
                settings.CatalogCacheTtlHours,
                timeProvider)
            ? cachedCatalog
            : null;
    }

    private static bool CanSaveCatalogSnapshot(CatalogCacheScope scope, CatalogSnapshot catalog)
        => scope != CatalogCacheScope.Anonymous
            || !catalog.Volumes.SelectMany(volume => volume.Chapters).Any(
                chapter => chapter is
                {
                    IsVip: true,
                    CatalogAccessState: CatalogChapterAccessState.Accessible,
                });

    internal static async Task<List<ChapterPlan>> BuildChapterPlansAsync(
        CatalogSnapshot catalog,
        string cacheRoot,
        LoginState? validatedLoginState,
        CancellationToken cancellationToken)
    {
        List<ChapterPlan> plans = [];
        foreach (ChapterDescriptor chapter in catalog.Volumes.SelectMany(volume => volume.Chapters))
        {
            ChapterCacheProbe? cachedProbe = await CacheStore.GetChapterProbeAsync(
                cacheRoot,
                catalog.BookId,
                chapter.ChapterId,
                cancellationToken);
            ChapterCacheEntry? cachedEntry = null;
            ChapterPlanStatus status;
            if (cachedProbe is null)
            {
                status = ChapterPlanStatus.FetchRequired;
            }
            else if (cachedProbe.CatalogWordCount != chapter.CatalogWordCount)
            {
                status = ChapterPlanStatus.Changed;
            }
            else if (cachedProbe.CatalogAccessState != chapter.CatalogAccessState
                && !CanIgnoreCatalogAccessStateMismatchForReusableVipFullCache(
                    chapter,
                    cachedProbe,
                    validatedLoginState))
            {
                status = ChapterPlanStatus.Changed;
            }
            else if (CanReuseCachedChapter(
                chapter,
                cachedProbe.IsPreview,
                cachedProbe.VisibleToUserName,
                cachedProbe.VipFullContentProvenance,
                validatedLoginState))
            {
                cachedEntry = await CacheStore.GetChapterAsync(
                    cacheRoot,
                    catalog.BookId,
                    chapter.ChapterId,
                    cancellationToken);
                status = cachedEntry is null
                    ? ChapterPlanStatus.FetchRequired
                    : ChapterPlanStatus.Cached;
            }
            else
            {
                status = ChapterPlanStatus.FetchRequired;
            }

            plans.Add(new ChapterPlan(chapter, status, cachedProbe, cachedEntry));
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

    private static LoginState? GetValidatedLoginState(LoginState? loginState)
        => loginState is { IsValidated: true } ? loginState : null;

    internal static LoginState SelectCachedLoginStateForProbe(
        LoginState? cachedLoginState,
        LoginState probedLoginState)
        => cachedLoginState is { IsValidated: true } && !probedLoginState.IsValidated
            ? cachedLoginState
            : probedLoginState;

    private static bool RequiresCurrentSessionCatalogEvaluation(
        IReadOnlyList<ChapterPlan> plans,
        bool isFreshAnonymousCatalog)
    {
        if (RequiresAuthenticatedPlanEvaluation(plans))
        {
            return true;
        }

        return !isFreshAnonymousCatalog
            && plans.Any(
                plan => plan.Chapter.IsVip
                    && plan.Status != ChapterPlanStatus.Cached);
    }

    private static bool RequiresAuthenticatedPlanEvaluation(IReadOnlyList<ChapterPlan> plans)
        => plans.Any(
            plan => plan.Chapter.IsVip
                && plan.CachedProbe is { IsPreview: false } cachedProbe
                && cachedProbe.VipFullContentProvenance != VipFullContentCacheProvenance.Public
                && LoginState.NormalizeUserName(cachedProbe.VisibleToUserName) is { Length: > 0 }
                && (plan.Status == ChapterPlanStatus.FetchRequired
                    || (plan.Status == ChapterPlanStatus.Changed
                        && cachedProbe.CatalogAccessState
                            != plan.Chapter.CatalogAccessState)));

    private static bool RequiresValidatedCatalogRefreshForEntitlementMismatch(
        IReadOnlyList<ChapterPlan> plans,
        LoginState validatedLoginState)
        => validatedLoginState is { IsValidated: true, UserName: { Length: > 0 } userName }
            && plans.Any(
                plan => plan.Chapter.IsVip
                    && plan.CachedProbe is { IsPreview: false } cachedProbe
                    && cachedProbe.CatalogAccessState
                        != plan.Chapter.CatalogAccessState
                    && cachedProbe.VipFullContentProvenance
                        != VipFullContentCacheProvenance.Public
                    && string.Equals(
                        LoginState.NormalizeUserName(cachedProbe.VisibleToUserName),
                        userName,
                        StringComparison.Ordinal));

    private static bool CanReuseCachedChapter(
        ChapterDescriptor chapter,
        bool isPreview,
        string? visibleToUserName,
        VipFullContentCacheProvenance? vipFullContentProvenance,
        LoginState? validatedLoginState)
    {
        if (!chapter.IsVip)
        {
            return true;
        }

        if (validatedLoginState is not { IsValidated: true, UserName: { Length: > 0 } userName })
        {
            return isPreview
                || vipFullContentProvenance == VipFullContentCacheProvenance.Public;
        }

        if (isPreview)
        {
            return false;
        }

        if (vipFullContentProvenance == VipFullContentCacheProvenance.Public)
        {
            return true;
        }

        if (chapter.CatalogAccessState != CatalogChapterAccessState.Accessible)
        {
            return false;
        }

        return vipFullContentProvenance switch
        {
            null or VipFullContentCacheProvenance.ValidatedUser
                => IsSameNormalizedUser(visibleToUserName, userName),
            _ => false,
        };
    }

    private static bool CanReuseCachedPlanForCurrentLoginState(
        ChapterPlan plan,
        LoginState? currentLoginState)
    {
        if (plan is not
            {
                Status: ChapterPlanStatus.Cached,
                CachedEntry: not null,
            })
        {
            return false;
        }

        ChapterCacheProbe? cachedProbe = plan.CachedProbe;
        return CanReuseCachedChapter(
            plan.Chapter,
            cachedProbe?.IsPreview ?? plan.CachedEntry.IsPreview,
            cachedProbe?.VisibleToUserName ?? plan.CachedEntry.VisibleToUserName,
            cachedProbe?.VipFullContentProvenance ?? plan.CachedEntry.VipFullContentProvenance,
            GetValidatedLoginState(currentLoginState));
    }

    private static bool IsSameNormalizedUser(string? left, string right)
        => string.Equals(
            LoginState.NormalizeUserName(left),
            LoginState.NormalizeUserName(right),
            StringComparison.Ordinal);

    private static bool CanIgnoreCatalogAccessStateMismatchForReusableVipFullCache(
        ChapterDescriptor chapter,
        ChapterCacheProbe cachedProbe,
        LoginState? validatedLoginState)
        => chapter.IsVip
            && cachedProbe is
            {
                IsPreview: false,
            }
            && CanReuseCachedChapter(
                chapter,
                cachedProbe.IsPreview,
                cachedProbe.VisibleToUserName,
                cachedProbe.VipFullContentProvenance,
                validatedLoginState);

    private static string? GetVisibleToUserName(
        ChapterDescriptor chapter,
        ChapterFetchResult chapterResult,
        LoginState? validatedLoginState)
        => chapter.IsVip
            && !chapterResult.IsPreview
            && validatedLoginState is { IsValidated: true, UserName: { Length: > 0 } userName }
            ? userName
            : null;

    private static CatalogChapterAccessState GetCachedCatalogAccessState(
        ChapterDescriptor chapter,
        ChapterFetchResult chapterResult,
        LoginState? validatedLoginState)
        => chapter.IsVip
            && !chapterResult.IsPreview
            && validatedLoginState is { IsValidated: true, UserName: { Length: > 0 } }
            ? CatalogChapterAccessState.Accessible
            : chapter.CatalogAccessState;

    private static VipFullContentCacheProvenance? GetVipFullContentCacheProvenance(
        ChapterDescriptor chapter,
        ChapterFetchResult chapterResult,
        LoginState? validatedLoginState)
    {
        if (!chapter.IsVip || chapterResult.IsPreview)
        {
            return null;
        }

        return validatedLoginState is { IsValidated: true, UserName: { Length: > 0 } }
            ? VipFullContentCacheProvenance.ValidatedUser
            : validatedLoginState is { IsLoggedIn: false }
                ? VipFullContentCacheProvenance.Public
                : null;
    }

    private static ResolvedAppSettings ValidateDownload(ResolvedAppSettings settings)
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

        ValidateCatalogCacheTtl(settings);
        return settings;
    }

    private static ResolvedAppSettings ValidateLogin(ResolvedAppSettings settings)
        => settings;

    private static ResolvedAppSettings ValidateInfo(ResolvedAppSettings settings)
    {
        ValidateCatalogCacheTtl(settings);
        return settings;
    }

    private static void ValidateCatalogCacheTtl(ResolvedAppSettings settings)
    {
        if (settings.CatalogCacheTtlHours <= 0)
        {
            throw new CliInputException("Catalog cache TTL must be greater than zero.");
        }
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
            + "fetch-required="
            + $"{plans.Count(plan => plan.Status == ChapterPlanStatus.FetchRequired)}.");
    }
}
