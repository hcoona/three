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
    private static readonly AsyncLocal<Action<string>?> BeforeOutputWriteHook = new();

    internal static Action<string>? BeforeOutputWriteForTests
    {
        get => BeforeOutputWriteHook.Value;
        set => BeforeOutputWriteHook.Value = value;
    }

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
            LoginStateProbeMode? loginStateProbeMode = null;
            bool loginStateProbeFailedInRun = false;
            bool authenticatedUnknownIdentityDiscoveredInRun = false;
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
                        if (!browserHeadless && headless)
                        {
                            await browser.DisposeAsync();
                        }
                        else
                        {
                            await browser.DisposeBestEffortAsync();
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        browser = null;
                    }

                    browser = await browserManager.OpenAsync(
                        settings,
                        paths,
                        headless,
                        cancellationToken);
                    browserHeadless = headless;
                    loginState = null;
                    loginStateProbeMode = null;
                    return browser;
                }

                Task<IQidianBrowserSession> GetBrowserAsync()
                    => OpenBrowserAsync(browser is null ? true : browserHeadless);

                async Task<LoginState> ProbeLoginStateWithRunUncertaintyAsync(
                    LoginStateProbeMode probeMode)
                {
                    try
                    {
                        return await (await GetBrowserAsync()).GetLoginStateAsync(
                            AppConstants.QidianBaseUrl,
                            cancellationToken,
                            probeMode: probeMode);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        loginStateProbeFailedInRun = true;
                        throw;
                    }
                }

                async Task<LoginState> GetCurrentLoginStateAsync(
                    bool forceRefresh = false,
                    LoginStateProbeMode probeMode = LoginStateProbeMode.WaitForValidatedIdentity)
                {
                    if (forceRefresh
                        || loginState is null
                        || (probeMode == LoginStateProbeMode.WaitForValidatedIdentity
                            && !loginState.IsValidated))
                    {
                        LoginState probedLoginState =
                            await ProbeLoginStateWithRunUncertaintyAsync(probeMode);
                        loginState = SelectCachedLoginStateForProbe(
                            loginState,
                            probedLoginState);
                        loginStateProbeMode = probeMode;
                    }

                    if (loginState.IsValidated)
                    {
                        loginStateProbeFailedInRun = false;
                        authenticatedUnknownIdentityDiscoveredInRun = false;
                    }
                    else if (loginState.IsLoggedIn)
                    {
                        authenticatedUnknownIdentityDiscoveredInRun = true;
                    }

                    return loginState;
                }

                bool HasAuthenticatedCacheIdentityUncertainty(LoginState? currentState)
                    => GetValidatedLoginState(currentState) is null
                    && GetValidatedLoginState(loginState) is null
                    && (loginStateProbeFailedInRun
                        || authenticatedUnknownIdentityDiscoveredInRun
                        || currentState is { IsLoggedIn: true });

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
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        loginStateProbeFailedInRun = true;
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
                    try
                    {
                        await browser!.WaitForManualLoginAsync(
                            cancellationToken,
                            requireValidatedIdentity: true);
                        await browser.PersistSessionStateAsync();
                        browser = null;
                    }
                    catch
                    {
                        if (browser is not null)
                        {
                            await browser.DisposeBestEffortAsync();
                            browser = null;
                        }

                        throw;
                    }

                    LoginState validatedState = await GetCurrentLoginStateAsync(forceRefresh: true);
                    if (!validatedState.IsValidated)
                    {
                        throw new OperationalException(
                            "Manual login completed, but the persisted browser session "
                            + "could not be validated. Sign in again and ensure the "
                            + "account name is visible.");
                    }

                    Console.WriteLine("Login confirmed. Continuing with the validated session.");
                    return (validatedState, ManualLoginCompleted: true);
                }

                async Task<(
                    CatalogSnapshot Catalog,
                    List<ChapterPlan> Plans,
                    LoginState? LoginState,
                    bool LoginStateProbeFailed,
                    bool VipFullContentClassificationProbeCompleted)>
                    ResolveCatalogAndPlansForDownloadAsync(BookReference target)
                {
                    LoginState? currentLoginState = loginState;
                    LoginStateProbeMode? currentLoginStateProbeMode = loginStateProbeMode;
                    bool hasProbedCurrentLoginState = currentLoginState is not null
                        || loginStateProbeFailedInRun;
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
                        LoginState? probedLoginState = await TryGetCurrentLoginStateAsync(
                            LogMessages.IgnoreAuthenticatedCacheReuseProbeFailure,
                            probeMode);
                        if (probedLoginState is not null)
                        {
                            currentLoginState = probedLoginState;
                            currentLoginStateProbeMode = loginStateProbeMode;
                        }

                        return currentLoginState;
                    }

                    LoginState? knownValidatedLoginState = GetValidatedLoginState(
                        currentLoginState);
                    if (knownValidatedLoginState is not null)
                    {
                        (
                            CatalogSnapshot validatedCatalog,
                            List<ChapterPlan> validatedPlans) =
                            await GetValidatedCatalogAndPlansAsync(
                                knownValidatedLoginState,
                                forceRefresh: false);
                        return (
                            validatedCatalog,
                            validatedPlans,
                            currentLoginState,
                            HasAuthenticatedCacheIdentityUncertainty(currentLoginState),
                            vipFullContentClassificationProbeCompleted);
                    }

                    CatalogSnapshot? anonymousCatalog = await TryGetFreshCatalogAsync(
                        target.BookId,
                        settings,
                        paths,
                        CatalogCacheScope.Anonymous,
                        cancellationToken);
                    bool isFreshAnonymousCatalog = anonymousCatalog is not null;

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
                        bool requiresValidatedIdentityProbe =
                            HasCachedVipPreviewPlan(plans)
                            && (!hasProbedCurrentLoginState
                                || (!loginStateProbeFailedInRun
                                    && currentLoginState is { IsLoggedIn: false }
                                    && currentLoginStateProbeMode
                                        != LoginStateProbeMode.WaitForValidatedIdentity));
                        if (!hasProbedCurrentLoginState || requiresValidatedIdentityProbe)
                        {
                            LoginStateProbeMode probeMode = requiresValidatedIdentityProbe
                                    ? LoginStateProbeMode.WaitForValidatedIdentity
                                    : LoginStateProbeMode.CurrentStateOnly;
                            await ProbeCurrentLoginStateAsync(probeMode);
                        }

                        LoginState? initialValidatedLoginState = GetValidatedLoginState(
                            currentLoginState);
                        if (initialValidatedLoginState is not null)
                        {
                            if (!isFreshAnonymousCatalog)
                            {
                                catalog = catalog with
                                {
                                    CacheScope = CatalogCacheScope.ForValidatedUser(
                                        initialValidatedLoginState.UserName!),
                                };
                                await CacheStore.SaveCatalogAsync(
                                    paths.CacheRoot,
                                    catalog,
                                    cancellationToken);
                                plans = await BuildChapterPlansAsync(
                                    catalog,
                                    paths.CacheRoot,
                                    initialValidatedLoginState,
                                    cancellationToken);
                                return (
                                    catalog,
                                    plans,
                                    currentLoginState,
                                    HasAuthenticatedCacheIdentityUncertainty(currentLoginState),
                                    vipFullContentClassificationProbeCompleted);
                            }

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
                                HasAuthenticatedCacheIdentityUncertainty(currentLoginState),
                                vipFullContentClassificationProbeCompleted);
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
                                    HasAuthenticatedCacheIdentityUncertainty(currentLoginState),
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

                    plans = FailClosedAuthenticatedSensitiveCachedPlansForUnknownLoginState(
                        plans,
                        currentLoginState,
                        HasAuthenticatedCacheIdentityUncertainty(currentLoginState));

                    return (
                        catalog,
                        plans,
                        currentLoginState,
                        HasAuthenticatedCacheIdentityUncertainty(currentLoginState),
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
                            bool loginStateProbeFailed,
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

                        AppPaths.EnsureNotReparsePathIfExists(outputPath);
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
                        List<ChapterPlan> authenticatedSensitiveRenderedCachePlans = [];
                        List<ChapterPlan> orderedPlans = plans;

                        async Task RefetchAuthenticatedSensitiveRenderedCacheAsync(string reason)
                        {
                            if (authenticatedSensitiveRenderedCachePlans.Count == 0)
                            {
                                return;
                            }

                            List<ChapterPlan> plansToRefetch =
                                [.. authenticatedSensitiveRenderedCachePlans];
                            authenticatedSensitiveRenderedCachePlans.Clear();
                            foreach (ChapterPlan planToRefetch in plansToRefetch)
                            {
                                if (renderedChapters.Remove(planToRefetch.Chapter.ChapterId))
                                {
                                    reusedChapters--;
                                }

                                Console.WriteLine(
                                    $"  Refetching {planToRefetch.Chapter.Title} "
                                    + reason);
                                await FetchAndRenderChapterAsync(planToRefetch);
                            }
                        }

                        async Task
                            RefetchAuthenticatedSensitiveRenderedCacheAfterLoginStateUpdateIfNeededAsync()
                        {
                            if (GetValidatedLoginState(currentLoginState) is not null)
                            {
                                loginStateProbeFailed = false;
                                await RefetchAuthenticatedSensitiveRenderedCacheAsync(
                                    "after login-state validation");
                            }
                            else
                            {
                                loginStateProbeFailed =
                                    HasAuthenticatedCacheIdentityUncertainty(currentLoginState);
                                if (loginStateProbeFailed)
                                {
                                    await RefetchAuthenticatedSensitiveRenderedCacheAsync(
                                        loginStateProbeFailedInRun
                                            ? "after login-state probe failure"
                                            : "after login-state identity uncertainty");
                                }
                            }
                        }

                        async Task FetchAndRenderChapterAsync(ChapterPlan plan)
                        {
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
                                return;
                            }

                            IReadOnlyList<string> paragraphs =
                                NormalizeFetchedParagraphs(chapterResult);
                            if (plan.Chapter.IsVip
                                && !chapterResult.IsPreview)
                            {
                                bool hadValidatedLoginState =
                                    GetValidatedLoginState(currentLoginState) is not null;
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
                                        loginStateProbeFailed =
                                            HasAuthenticatedCacheIdentityUncertainty(
                                                currentLoginState);
                                        vipFullContentClassificationProbeCompleted = true;
                                        if (!hadValidatedLoginState)
                                        {
                                            await RefetchAuthenticatedSensitiveRenderedCacheAfterLoginStateUpdateIfNeededAsync();
                                        }
                                    }
                                    else
                                    {
                                        await RefetchAuthenticatedSensitiveRenderedCacheAfterLoginStateUpdateIfNeededAsync();
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

                        for (
                            int chapterIndex = 0;
                            chapterIndex < orderedPlans.Count;
                            chapterIndex++)
                        {
                            ChapterPlan plan = orderedPlans[chapterIndex];
                            bool canReuseCachedPlan = CanReuseCachedPlanForCurrentLoginState(
                                plan,
                                currentLoginState,
                                loginStateProbeFailed);
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
                                if (IsAuthenticatedSensitiveCachedPlan(plan))
                                {
                                    authenticatedSensitiveRenderedCachePlans.Add(plan);
                                }

                                reusedChapters++;
                                continue;
                            }

                            await FetchAndRenderChapterAsync(plan);

                            if (chapterIndex < orderedPlans.Count - 1)
                            {
                                TimeSpan delay = RequestDelayPlanner.CalculateDelay(
                                    plan.Chapter.CatalogWordCount,
                                    settings);
                                await Task.Delay(delay, cancellationToken);
                            }
                        }

                        BeforeOutputWriteForTests?.Invoke(outputPath);
                        AppPaths.CreateDirectoryRejectingReparseAncestors(paths.OutputRoot);
                        AppPaths.CreateDirectoryRejectingReparseAncestors(
                            Path.GetDirectoryName(outputPath)!);
                        AppPaths.EnsureNotReparsePathIfExists(outputPath);
                        string markdown = MarkdownRenderer.Render(catalog, renderedChapters);
                        await File.WriteAllTextAsync(
                            outputPath,
                            markdown,
                            Encoding.UTF8,
                            cancellationToken);
                        Console.WriteLine($"Wrote '{outputPath}'.");
                        completedBooks++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await browser.DisposeBestEffortAsync();
                    }
                    else
                    {
                        await browser.DisposeAsync();
                    }
                }
            }
        }
        catch (CliInputException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.WriteLine(BuildDownloadSummary(fallbackFailure: true));
            return ExitCodes.UsageFailure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                LoginState loginState = await browser.WaitForManualLoginAsync(
                    cancellationToken,
                    requireValidatedIdentity: true);
                if (!loginState.IsValidated)
                {
                    throw new OperationalException(
                        "Manual login completed, but a validated account identity "
                        + "was not established. Sign in again and ensure the "
                        + "account name is visible.");
                }

                await browser.PersistSessionStateAsync();
                browser = null;
            }
            finally
            {
                if (browser is not null)
                {
                    await browser.DisposeBestEffortAsync();
                }
            }

            IQidianBrowserSession? validationBrowser = await browserManager.OpenAsync(
                settings,
                paths,
                headless: true,
                cancellationToken);
            bool disposeValidationBrowserNormally = false;
            try
            {
                LoginState persistedState = await validationBrowser.GetLoginStateAsync(
                    AppConstants.QidianBaseUrl,
                    cancellationToken,
                    probeMode: LoginStateProbeMode.WaitForValidatedIdentity);
                if (!persistedState.IsValidated)
                {
                    throw new OperationalException(
                        "Login completed, but the persisted browser session "
                        + "could not be validated. Sign in again and ensure the "
                        + "account name is visible.");
                }

                disposeValidationBrowserNormally = true;
            }
            finally
            {
                if (validationBrowser is not null)
                {
                    if (disposeValidationBrowserNormally)
                    {
                        await validationBrowser.DisposeAsync();
                    }
                    else
                    {
                        await validationBrowser.DisposeBestEffortAsync();
                    }
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
            int removed = ShouldClearCacheRoot(paths.CacheRoot)
                ? CacheStore.Clear(paths.CacheRoot, bookId, options.CatalogOnly)
                : 0;

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

    private static bool ShouldClearCacheRoot(string cacheRoot)
    {
        try
        {
            FileAttributes attributes = File.GetAttributes(cacheRoot);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"Refusing to clear cache through reparse point directory: '{cacheRoot}'.");
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException(
                    $"Refusing to clear cache because the cache root is not a directory: '{cacheRoot}'.");
            }

            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException
            or DirectoryNotFoundException)
        {
            CacheStore.EnsureNoReparsePointInExistingCachePath(cacheRoot);
            return false;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                if (cancellationToken.IsCancellationRequested)
                {
                    await browser.DisposeBestEffortAsync();
                }
                else
                {
                    await browser.DisposeAsync();
                }
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

        return plans.Any(
            plan => plan.Chapter.IsVip
                && !isFreshAnonymousCatalog
                && plan.Status != ChapterPlanStatus.Cached)
            || HasCachedVipPreviewPlan(plans);
    }

    private static bool HasCachedVipPreviewPlan(IReadOnlyList<ChapterPlan> plans)
        => plans.Any(
            plan => plan is
            {
                Chapter.IsVip: true,
                Status: ChapterPlanStatus.Cached,
                CachedProbe.IsPreview: true,
            });

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

    internal static bool CanReuseCachedPlanForCurrentLoginState(
        ChapterPlan plan,
        LoginState? currentLoginState,
        bool loginStateProbeFailed)
    {
        if (plan is not
            {
                Status: ChapterPlanStatus.Cached,
                CachedEntry: not null,
            })
        {
            return false;
        }

        if (HasAuthenticatedCacheIdentityUncertainty(currentLoginState, loginStateProbeFailed)
            && IsAuthenticatedSensitiveCachedPlan(plan))
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

    private static List<ChapterPlan> FailClosedAuthenticatedSensitiveCachedPlansForUnknownLoginState(
        IReadOnlyList<ChapterPlan> plans,
        LoginState? currentLoginState,
        bool loginStateProbeFailed)
    {
        if (!HasAuthenticatedCacheIdentityUncertainty(currentLoginState, loginStateProbeFailed))
        {
            return [.. plans];
        }

        return plans
            .Select(
                plan => IsAuthenticatedSensitiveCachedPlan(plan)
                    ? plan with
                    {
                        Status = ChapterPlanStatus.FetchRequired,
                        CachedEntry = null,
                    }
                    : plan)
            .ToList();
    }

    internal static bool HasAuthenticatedCacheIdentityUncertainty(
        LoginState? currentLoginState,
        bool loginStateProbeFailed)
        => GetValidatedLoginState(currentLoginState) is null
            && (loginStateProbeFailed || currentLoginState is { IsLoggedIn: true });

    private static bool IsAuthenticatedSensitiveCachedPlan(ChapterPlan plan)
    {
        if (plan is not { Status: ChapterPlanStatus.Cached, Chapter.IsVip: true })
        {
            return false;
        }

        bool isPreview = plan.CachedProbe?.IsPreview
            ?? plan.CachedEntry?.IsPreview
            ?? false;
        VipFullContentCacheProvenance? provenance =
            plan.CachedProbe?.VipFullContentProvenance
            ?? plan.CachedEntry?.VipFullContentProvenance;
        return isPreview || provenance != VipFullContentCacheProvenance.Public;
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
