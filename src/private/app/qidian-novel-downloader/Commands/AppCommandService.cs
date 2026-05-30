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

    private static readonly StringComparer OutputPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static Action<string>? BeforeOutputWriteForTests
    {
        get => BeforeOutputWriteHook.Value;
        set => BeforeOutputWriteHook.Value = value;
    }

    private static readonly AsyncLocal<Action<string>?> AfterPendingCacheCommitsHook = new();

    internal static Action<string>? AfterPendingCacheCommitsForTests
    {
        get => AfterPendingCacheCommitsHook.Value;
        set => AfterPendingCacheCommitsHook.Value = value;
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
                            && (!loginState.IsValidated
                                || loginStateProbeMode
                                    != LoginStateProbeMode.WaitForValidatedIdentity)))
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
                        || currentState is { IsLoggedIn: true }
                        || currentState is { IsProbeComplete: false }
                        || loginState is { IsProbeComplete: false });

                async Task<LoginState?> TryGetCurrentLoginStateAsync(
                    Action<ILogger, Exception?> logFailure,
                    LoginStateProbeMode probeMode = LoginStateProbeMode.WaitForValidatedIdentity)
                {
                    try
                    {
                        LoginState currentState = await GetCurrentLoginStateAsync(
                            forceRefresh: true,
                            probeMode: probeMode);
                        if (probeMode == LoginStateProbeMode.WaitForValidatedIdentity
                            && !currentState.IsProbeComplete)
                        {
                            loginStateProbeFailedInRun = true;
                            loginState = null;
                            loginStateProbeMode = null;
                            return null;
                        }

                        return currentState;
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
                    bool VipFullContentClassificationProbeCompleted,
                    bool CatalogResolvedFromProvenAnonymousOrigin,
                    bool RegularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof,
                    bool CurrentBookStrongAnonymousProof,
                    CatalogSnapshot? RawAnonymousCatalogEvidence,
                    List<ChapterPlan>? RawAnonymousPlanEvidence,
                    long? CatalogClearGeneration)>
                    ResolveCatalogAndPlansForDownloadAsync(
                        BookReference target,
                        List<PendingCatalogCacheSave> pendingCatalogCacheSaves)
                {
                    LoginState? currentLoginState = loginState;
                    LoginStateProbeMode? currentLoginStateProbeMode = loginStateProbeMode;
                    bool hasProbedCurrentLoginState = currentLoginState is not null
                        || loginStateProbeFailedInRun;
                    bool vipFullContentClassificationProbeCompleted = false;
                    bool catalogFetchedWithIsolatedAnonymousBrowser = false;
                    bool catalogResolvedFromProvenAnonymousOrigin = false;
                    bool regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof = false;
                    bool currentBookStrongAnonymousProof = false;
                    CatalogSnapshot? rawAnonymousCatalogEvidence = null;
                    List<ChapterPlan>? rawAnonymousPlanEvidence = null;
                    long? catalogClearGeneration = null;
                    CatalogSnapshot? catalog = null;
                    List<ChapterPlan>? plans = null;

                    bool IsCurrentSessionKnownAnonymous()
                        => IsCompleteLoggedOutProof(currentLoginState)
                        && !loginStateProbeFailedInRun
                        && !authenticatedUnknownIdentityDiscoveredInRun;

                    bool IsCurrentSessionProvenAnonymous()
                        => IsCurrentSessionKnownAnonymous()
                            && currentBookStrongAnonymousProof
                            && currentLoginStateProbeMode
                                == LoginStateProbeMode.WaitForValidatedIdentity;

                    static bool HasVipAccessibleCatalogEvidence(CatalogSnapshot candidate)
                        => candidate.Volumes
                            .SelectMany(volume => volume.Chapters)
                            .Any(chapter => chapter.IsVip
                                && chapter.CatalogAccessState
                                    == CatalogChapterAccessState.Accessible);

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
                            currentLoginStateProbeMode = probeMode;
                            currentBookStrongAnonymousProof =
                                probeMode == LoginStateProbeMode.WaitForValidatedIdentity
                                && IsCompleteLoggedOutProof(probedLoginState);
                        }

                        return currentLoginState;
                    }

                    CatalogSnapshot? anonymousCatalog = await TryGetFreshCatalogAsync(
                        target.BookId,
                        settings,
                        paths,
                        CatalogCacheScope.Anonymous,
                        cancellationToken);
                    bool isFreshAnonymousCatalog = anonymousCatalog is not null;
                    catalogResolvedFromProvenAnonymousOrigin =
                        anonymousCatalog is { IsKnownAnonymous: true };
                    CatalogSnapshot? trustedAnonymousCatalog = anonymousCatalog
                        ?? await CacheStore.GetCatalogAsync(
                            paths.CacheRoot,
                            target.BookId,
                            CatalogCacheScope.Anonymous,
                            cancellationToken);
                    LoginState? knownValidatedLoginState = GetValidatedLoginState(
                        currentLoginState);

                    async Task<LoginState?> RefreshKnownValidatedLoginStateForUserCacheAsync()
                    {
                        LoginState? refreshedLoginState = await TryGetCurrentLoginStateAsync(
                            LogMessages.IgnoreAuthenticatedCacheReuseProbeFailure,
                            LoginStateProbeMode.WaitForValidatedIdentity);
                        if (refreshedLoginState is null)
                        {
                            currentLoginState = null;
                            loginState = null;
                            loginStateProbeMode = null;
                            knownValidatedLoginState = null;
                            return null;
                        }

                        currentLoginState = refreshedLoginState;
                        currentLoginStateProbeMode = LoginStateProbeMode.WaitForValidatedIdentity;
                        currentBookStrongAnonymousProof =
                            IsCompleteLoggedOutProof(refreshedLoginState);
                        knownValidatedLoginState = GetValidatedLoginState(refreshedLoginState);
                        return knownValidatedLoginState;
                    }

                    if (knownValidatedLoginState is not null)
                    {
                        knownValidatedLoginState =
                            await RefreshKnownValidatedLoginStateForUserCacheAsync();
                    }

                    async Task TrustFetchedAnonymousCatalogIfKnownAnonymousAsync()
                    {
                        if (catalog is null
                            || isFreshAnonymousCatalog
                            || catalog.CacheScope != CatalogCacheScope.Anonymous
                            || catalog.IsKnownAnonymous
                            || !IsCurrentSessionProvenAnonymous())
                        {
                            return;
                        }

                        if (!regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof)
                        {
                            if (!options.DryRun || !HasVipAccessibleCatalogEvidence(catalog))
                            {
                                return;
                            }

                            catalog = await FetchCatalogAsync(
                                target.BookId,
                                paths,
                                GetBrowserAsync,
                                CatalogCacheScope.Anonymous,
                                isKnownAnonymousFetch: false,
                                cancellationToken,
                                pendingCatalogCacheSaves,
                                clearGenerationCaptured:
                                    clearGeneration => catalogClearGeneration = clearGeneration);
                            plans = await BuildChapterPlansAsync(
                                catalog,
                                paths.CacheRoot,
                                validatedLoginState: null,
                                cancellationToken);
                            await ProbeCurrentLoginStateAsync(
                                LoginStateProbeMode.WaitForValidatedIdentity);
                            regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof =
                                IsCurrentSessionProvenAnonymous();
                            if (!regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof)
                            {
                                RemovePendingAnonymousCatalogSaves(
                                    pendingCatalogCacheSaves,
                                    target.BookId);
                                return;
                            }
                        }

                        if (!catalogResolvedFromProvenAnonymousOrigin)
                        {
                            catalog = catalog with { IsKnownAnonymous = true };
                            if (CanSaveCatalogSnapshot(CatalogCacheScope.Anonymous, catalog))
                            {
                                StagePendingCatalogCacheSave(
                                    paths.CacheRoot,
                                    pendingCatalogCacheSaves,
                                    catalog,
                                    catalogClearGeneration);
                            }

                            catalogResolvedFromProvenAnonymousOrigin = true;
                            plans = await BuildChapterPlansAsync(
                                catalog,
                                paths.CacheRoot,
                                validatedLoginState: null,
                                cancellationToken);
                            rawAnonymousCatalogEvidence = catalog;
                            rawAnonymousPlanEvidence = plans;
                            return;
                        }

                        CatalogSnapshot knownAnonymousCatalog = catalog with
                        {
                            IsKnownAnonymous = true,
                        };
                        catalog = knownAnonymousCatalog;
                        rawAnonymousCatalogEvidence = catalog;
                        rawAnonymousPlanEvidence = plans;
                        if (CanSaveCatalogSnapshot(
                            CatalogCacheScope.Anonymous,
                            knownAnonymousCatalog))
                        {
                            StagePendingCatalogCacheSave(
                                paths.CacheRoot,
                                pendingCatalogCacheSaves,
                                knownAnonymousCatalog,
                                catalogClearGeneration);
                        }
                    }

                    async Task<(CatalogSnapshot Catalog, List<ChapterPlan> Plans)>
                        GetValidatedCatalogAndPlansAsync(
                            LoginState validatedLoginState,
                            bool forceRefresh)
                    {
                        async Task<bool> ConfirmValidatedCatalogScopeAsync(
                            CatalogCacheScope requestedScope)
                        {
                            LoginState? confirmedLoginState = await TryGetCurrentLoginStateAsync(
                                LogMessages.IgnoreAuthenticatedCacheReuseProbeFailure,
                                LoginStateProbeMode.WaitForValidatedIdentity);
                            if (confirmedLoginState is null)
                            {
                                currentLoginState = null;
                                knownValidatedLoginState = null;
                                return false;
                            }

                            currentLoginState = confirmedLoginState;
                            currentLoginStateProbeMode = LoginStateProbeMode.WaitForValidatedIdentity;
                            currentBookStrongAnonymousProof =
                                IsCompleteLoggedOutProof(confirmedLoginState);
                            knownValidatedLoginState = GetValidatedLoginState(confirmedLoginState);
                            return requestedScope.Kind
                                    == CatalogCacheScopeKind.ValidatedUser
                                && IsSameValidatedUser(
                                    confirmedLoginState,
                                    requestedScope.UserName);
                        }

                        bool HasValidatedCatalogScopeProof(CatalogCacheScope requestedScope)
                            => currentLoginStateProbeMode
                                == LoginStateProbeMode.WaitForValidatedIdentity
                                && IsSameValidatedUser(
                                    currentLoginState,
                                    requestedScope.UserName);

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
                            cancellationToken,
                            pendingCatalogCacheSaves: pendingCatalogCacheSaves,
                            confirmValidatedScopeAsync: ConfirmValidatedCatalogScopeAsync,
                            hasValidatedScopeProof: HasValidatedCatalogScopeProof);
                        List<ChapterPlan> validatedPlans = await BuildChapterPlansAsync(
                            validatedCatalog,
                            paths.CacheRoot,
                            validatedLoginState,
                            cancellationToken);
                        if (reusedCachedValidatedCatalog
                            && (RequiresValidatedCatalogRefreshForEntitlementMismatch(
                                    validatedPlans,
                                    validatedLoginState)
                                || RequiresValidatedCatalogRefreshForAnonymousVipConflict(
                                    plans,
                                    anonymousCatalog,
                                    validatedCatalog)))
                        {
                            (validatedCatalog, _) = await GetCatalogAsync(
                                target.BookId,
                                settings,
                                paths,
                                GetBrowserAsync,
                                CatalogCacheScope.ForValidatedUser(
                                    validatedLoginState.UserName!),
                                forceRefresh: true,
                                cancellationToken,
                                pendingCatalogCacheSaves: pendingCatalogCacheSaves,
                                confirmValidatedScopeAsync: ConfirmValidatedCatalogScopeAsync,
                                hasValidatedScopeProof: HasValidatedCatalogScopeProof);
                            validatedPlans = await BuildChapterPlansAsync(
                                validatedCatalog,
                                paths.CacheRoot,
                                validatedLoginState,
                                cancellationToken);
                        }

                        if (HasAnonymousVipValidatedFreeConflict(
                            plans,
                            anonymousCatalog,
                            validatedCatalog))
                        {
                            RemovePendingValidatedCatalogSaves(
                                pendingCatalogCacheSaves,
                                target.BookId,
                                validatedLoginState.UserName);
                            (validatedCatalog, validatedPlans) =
                                FailClosedValidatedCatalogForAnonymousVipConflicts(
                                    validatedCatalog,
                                    validatedPlans,
                                    plans!,
                                    validatedLoginState);
                        }

                        return DeduplicateCatalogAndPlans(validatedCatalog, validatedPlans);
                    }

                    catalog = anonymousCatalog;
                    if (isFreshAnonymousCatalog)
                    {
                        plans = await BuildChapterPlansAsync(
                            catalog!,
                            paths.CacheRoot,
                            knownValidatedLoginState,
                            cancellationToken);
                        if (!plans.Any(plan => plan.Chapter.IsVip)
                            && !RequiresCurrentSessionCatalogEvaluation(
                                plans,
                                isFreshAnonymousCatalog))
                        {
                            rawAnonymousCatalogEvidence = catalog;
                            rawAnonymousPlanEvidence = plans;
                            (catalog, plans) = DeduplicateCatalogAndPlans(catalog!, plans);
                            return (
                                catalog,
                                plans,
                                currentLoginState,
                                HasAuthenticatedCacheIdentityUncertainty(currentLoginState),
                                vipFullContentClassificationProbeCompleted,
                                catalogResolvedFromProvenAnonymousOrigin,
                                regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof,
                                currentBookStrongAnonymousProof,
                                rawAnonymousCatalogEvidence,
                                rawAnonymousPlanEvidence,
                                catalogClearGeneration);
                        }
                    }

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
                            vipFullContentClassificationProbeCompleted,
                            catalogResolvedFromProvenAnonymousOrigin,
                            regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof,
                            currentBookStrongAnonymousProof,
                            rawAnonymousCatalogEvidence,
                            rawAnonymousPlanEvidence,
                            catalogClearGeneration);
                    }

                    if (catalog is null)
                    {
                        bool isCurrentSessionProvenAnonymous =
                            IsCurrentSessionProvenAnonymous();
                        bool regularCatalogFetchHadPreFetchLoggedOutProof =
                            isCurrentSessionProvenAnonymous;
                        catalog = !isCurrentSessionProvenAnonymous
                            && trustedAnonymousCatalog is { IsKnownAnonymous: true }
                                ? await FetchCatalogWithIsolatedAnonymousBrowserAsync(
                                    target.BookId,
                                    settings,
                                    paths,
                                    cancellationToken,
                                    pendingCatalogCacheSaves,
                                    clearGeneration => catalogClearGeneration = clearGeneration)
                                : await FetchCatalogAsync(
                                    target.BookId,
                                    paths,
                                    GetBrowserAsync,
                                    CatalogCacheScope.Anonymous,
                                    isKnownAnonymousFetch: false,
                                    cancellationToken,
                                    pendingCatalogCacheSaves,
                                    clearGenerationCaptured:
                                        clearGeneration =>
                                            catalogClearGeneration = clearGeneration);
                        regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof = false;
                        catalogFetchedWithIsolatedAnonymousBrowser =
                            !isCurrentSessionProvenAnonymous
                            && trustedAnonymousCatalog is { IsKnownAnonymous: true };
                        catalogResolvedFromProvenAnonymousOrigin =
                            catalogFetchedWithIsolatedAnonymousBrowser
                            || catalog.IsKnownAnonymous;
                        if (regularCatalogFetchHadPreFetchLoggedOutProof)
                        {
                            await ProbeCurrentLoginStateAsync(
                                LoginStateProbeMode.WaitForValidatedIdentity);
                            if (currentLoginState is { IsLoggedIn: true })
                            {
                                RemovePendingAnonymousCatalogSaves(
                                    pendingCatalogCacheSaves,
                                    target.BookId);
                            }

                            regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof =
                                IsCurrentSessionProvenAnonymous();
                            await TrustFetchedAnonymousCatalogIfKnownAnonymousAsync();
                        }

                        if (catalogFetchedWithIsolatedAnonymousBrowser)
                        {
                            anonymousCatalog = catalog;
                        }

                        plans = null;
                    }

                    plans ??= await BuildChapterPlansAsync(
                        catalog,
                        paths.CacheRoot,
                        validatedLoginState: null,
                        cancellationToken);
                    if (catalog.CacheScope == CatalogCacheScope.Anonymous)
                    {
                        if (!catalog.IsKnownAnonymous
                            && !plans.Any(plan => plan.Chapter.IsVip)
                            && !loginStateProbeFailedInRun
                            && !authenticatedUnknownIdentityDiscoveredInRun
                            && (catalogFetchedWithIsolatedAnonymousBrowser
                                || IsCurrentSessionProvenAnonymous()))
                        {
                            catalog = catalog with { IsKnownAnonymous = true };
                            StagePendingCatalogCacheSave(
                                paths.CacheRoot,
                                pendingCatalogCacheSaves,
                                catalog,
                                catalogClearGeneration);
                        }

                        rawAnonymousCatalogEvidence = catalog;
                        rawAnonymousPlanEvidence = plans;
                    }

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
                            await TrustFetchedAnonymousCatalogIfKnownAnonymousAsync();
                        }

                        LoginState? initialValidatedLoginState = GetValidatedLoginState(
                            currentLoginState);
                        bool suppressValidatedCatalogUseForUnprovenAnonymousCatalog =
                            !isFreshAnonymousCatalog
                            && !catalogFetchedWithIsolatedAnonymousBrowser
                            && !catalogResolvedFromProvenAnonymousOrigin
                            && catalog.CacheScope == CatalogCacheScope.Anonymous;
                        if (initialValidatedLoginState is not null
                            && !suppressValidatedCatalogUseForUnprovenAnonymousCatalog)
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
                                HasAuthenticatedCacheIdentityUncertainty(currentLoginState),
                                vipFullContentClassificationProbeCompleted,
                                catalogResolvedFromProvenAnonymousOrigin,
                                regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof,
                                currentBookStrongAnonymousProof,
                                rawAnonymousCatalogEvidence,
                                rawAnonymousPlanEvidence,
                                catalogClearGeneration);
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
                                    vipFullContentClassificationProbeCompleted,
                                    catalogResolvedFromProvenAnonymousOrigin,
                                    regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof,
                                    currentBookStrongAnonymousProof,
                                    rawAnonymousCatalogEvidence,
                                    rawAnonymousPlanEvidence,
                                    catalogClearGeneration);
                            }
                        }

                        LoginState? validatedLoginState = GetValidatedLoginState(
                            currentLoginState);
                        if (validatedLoginState is not null
                            && !suppressValidatedCatalogUseForUnprovenAnonymousCatalog)
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
                    if (catalog.CacheScope == CatalogCacheScope.Anonymous)
                    {
                        rawAnonymousCatalogEvidence = catalog;
                        rawAnonymousPlanEvidence = plans;
                    }

                    (catalog, plans) = DeduplicateCatalogAndPlans(catalog, plans);

                    return (
                        catalog,
                        plans,
                        currentLoginState,
                        HasAuthenticatedCacheIdentityUncertainty(currentLoginState),
                        vipFullContentClassificationProbeCompleted,
                        catalogResolvedFromProvenAnonymousOrigin,
                        regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof,
                        currentBookStrongAnonymousProof,
                        rawAnonymousCatalogEvidence,
                        rawAnonymousPlanEvidence,
                        catalogClearGeneration);
                }

                for (int bookIndex = 0; bookIndex < targets.Count; bookIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    BookReference target = targets[bookIndex];
                    Console.WriteLine(
                        $"[{bookIndex + 1}/{targets.Count}] Processing book {target.BookId}...");

                    try
                    {
                        HashSet<string> overwriteApprovedPaths = new(OutputPathComparer);
                        List<PendingCatalogCacheSave> pendingCatalogCacheSaves = [];

                        async Task<bool> ConfirmOutputPathForWriteAsync(string path)
                        {
                            AppPaths.EnsureNotReparsePathIfExists(path);
                            if (File.Exists(path)
                                && !options.Overwrite
                                && !overwriteApprovedPaths.Contains(path))
                            {
                                bool approved = await interactiveConsole.ConfirmAsync(
                                    $"The output file '{path}' already exists. Overwrite it?",
                                    cancellationToken);
                                if (!approved)
                                {
                                    Console.WriteLine(
                                        $"Skipped '{path}' because overwrite was not approved.");
                                    return false;
                                }

                                overwriteApprovedPaths.Add(path);
                            }

                            return true;
                        }

                        LoginState? earlyOutputPredictionLoginState =
                            bookIndex > 0 && loginState is { IsValidated: true }
                                ? null
                                : loginState;
                        string? earlyOutputPath = options.DryRun
                            ? null
                            : await TryGetCachedOutputPathForOverwriteCheckAsync(
                                target,
                                settings,
                                paths,
                                earlyOutputPredictionLoginState,
                                loginStateProbeMode,
                                loginStateProbeFailedInRun
                                    || authenticatedUnknownIdentityDiscoveredInRun,
                                timeProvider,
                                cancellationToken);
                        if (earlyOutputPath is not null
                            && !await ConfirmOutputPathForWriteAsync(earlyOutputPath))
                        {
                            skippedBooks++;
                            continue;
                        }

                        (
                            CatalogSnapshot catalog,
                            List<ChapterPlan> plans,
                            LoginState? currentLoginState,
                            bool loginStateProbeFailed,
                            bool vipFullContentClassificationProbeCompleted,
                            bool catalogWasResolvedFromProvenAnonymousOrigin,
                            bool regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof,
                            bool currentBookStrongAnonymousProof,
                            CatalogSnapshot? rawAnonymousCatalogEvidence,
                            List<ChapterPlan>? rawAnonymousPlanEvidence,
                            long? catalogClearGeneration) =
                            await ResolveCatalogAndPlansForDownloadAsync(
                                target,
                                pendingCatalogCacheSaves);

                        if (options.DryRun)
                        {
                            PrintDryRun(catalog, plans);
                            await CommitPendingCatalogCacheSavesAsync(
                                paths.CacheRoot,
                                pendingCatalogCacheSaves,
                                currentLoginState,
                                cancellationToken);
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

                        string confirmedOutputPath = outputPath;
                        bool deferInitialOutputPathConfirmation =
                            catalog.CacheScope == CatalogCacheScope.Anonymous
                            && plans.Any(plan => plan.Chapter.IsVip)
                            && GetValidatedLoginState(currentLoginState) is null;
                        if (!deferInitialOutputPathConfirmation
                            && !await ConfirmOutputPathForWriteAsync(outputPath))
                        {
                            skippedBooks++;
                            continue;
                        }

                        int downloadedChaptersBeforeBook = downloadedChapters;
                        int reusedChaptersBeforeBook = reusedChapters;
                        int failedChaptersBeforeBook = failedChapters;
                        Dictionary<string, RenderedChapter> renderedChapters =
                            new(StringComparer.Ordinal);
                        List<PendingChapterCacheSave> pendingChapterCacheSaves = [];
                        List<ChapterPlan> authenticatedSensitiveRenderedCachePlans = [];
                        List<(ChapterPlan Plan, string? UserName)>
                            authenticatedSensitiveRenderedFreshPlans = [];
                        List<ChapterPlan> orderedPlans = plans;
                        bool validatedCatalogReplanRequested = false;
                        bool validatedCatalogReplanCompleted = false;
                        bool skipBookAfterOutputPathDenied = false;
                        bool validatedCatalogIdentityUncertainAfterConfirmationFailure = false;

                        async Task<(CatalogSnapshot Catalog, List<ChapterPlan> Plans)>
                            ResolveValidatedCatalogAndPlansForOutputAsync(
                                LoginState validatedLoginState,
                                CatalogSnapshot anonymousCatalogEvidence,
                                IReadOnlyList<ChapterPlan> anonymousPlanEvidence)
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
                                forceRefresh: false,
                                cancellationToken,
                                pendingCatalogCacheSaves: pendingCatalogCacheSaves,
                                confirmValidatedScopeAsync: async requestedScope =>
                                {
                                    LoginState? confirmedLoginState =
                                        await TryGetCurrentLoginStateAsync(
                                            LogMessages
                                                .IgnoreAuthenticatedCacheReuseProbeFailure,
                                            LoginStateProbeMode
                                                .WaitForValidatedIdentity);
                                    if (confirmedLoginState is null)
                                    {
                                        currentLoginState = null;
                                        return false;
                                    }

                                    currentLoginState = confirmedLoginState;
                                    loginStateProbeFailed =
                                        HasAuthenticatedCacheIdentityUncertainty(
                                            currentLoginState);
                                    currentBookStrongAnonymousProof =
                                        IsCompleteLoggedOutProof(confirmedLoginState);
                                    return requestedScope.Kind
                                            == CatalogCacheScopeKind.ValidatedUser
                                        && IsSameValidatedUser(
                                            confirmedLoginState,
                                            requestedScope.UserName);
                                },
                                hasValidatedScopeProof: requestedScope =>
                                    loginStateProbeMode
                                        == LoginStateProbeMode.WaitForValidatedIdentity
                                    && IsSameValidatedUser(
                                        currentLoginState,
                                        requestedScope.UserName));
                            List<ChapterPlan> validatedPlans = await BuildChapterPlansAsync(
                                validatedCatalog,
                                paths.CacheRoot,
                                validatedLoginState,
                                cancellationToken);
                            if (reusedCachedValidatedCatalog
                                && (RequiresValidatedCatalogRefreshForEntitlementMismatch(
                                        validatedPlans,
                                        validatedLoginState)
                                    || RequiresValidatedCatalogRefreshForAnonymousVipConflict(
                                        anonymousPlanEvidence,
                                        anonymousCatalogEvidence,
                                        validatedCatalog)))
                            {
                                (validatedCatalog, _) = await GetCatalogAsync(
                                    target.BookId,
                                    settings,
                                    paths,
                                    GetBrowserAsync,
                                    CatalogCacheScope.ForValidatedUser(
                                        validatedLoginState.UserName!),
                                    forceRefresh: true,
                                    cancellationToken,
                                    pendingCatalogCacheSaves: pendingCatalogCacheSaves,
                                    confirmValidatedScopeAsync: async requestedScope =>
                                    {
                                        LoginState? confirmedLoginState =
                                            await TryGetCurrentLoginStateAsync(
                                                LogMessages
                                                    .IgnoreAuthenticatedCacheReuseProbeFailure,
                                                LoginStateProbeMode
                                                    .WaitForValidatedIdentity);
                                        if (confirmedLoginState is null)
                                        {
                                            currentLoginState = null;
                                            return false;
                                        }

                                        currentLoginState = confirmedLoginState;
                                        loginStateProbeFailed =
                                            HasAuthenticatedCacheIdentityUncertainty(
                                                currentLoginState);
                                        currentBookStrongAnonymousProof =
                                            IsCompleteLoggedOutProof(confirmedLoginState);
                                        return requestedScope.Kind
                                                == CatalogCacheScopeKind.ValidatedUser
                                            && IsSameValidatedUser(
                                                confirmedLoginState,
                                                requestedScope.UserName);
                                    },
                                    hasValidatedScopeProof: requestedScope =>
                                        loginStateProbeMode
                                            == LoginStateProbeMode.WaitForValidatedIdentity
                                        && IsSameValidatedUser(
                                            currentLoginState,
                                            requestedScope.UserName));
                                validatedPlans = await BuildChapterPlansAsync(
                                    validatedCatalog,
                                    paths.CacheRoot,
                                    validatedLoginState,
                                    cancellationToken);
                            }

                            if (HasAnonymousVipValidatedFreeConflict(
                                anonymousPlanEvidence,
                                anonymousCatalogEvidence,
                                validatedCatalog))
                            {
                                RemovePendingValidatedCatalogSaves(
                                    pendingCatalogCacheSaves,
                                    target.BookId,
                                    validatedLoginState.UserName);
                                (validatedCatalog, validatedPlans) =
                                    FailClosedValidatedCatalogForAnonymousVipConflicts(
                                        validatedCatalog,
                                        validatedPlans,
                                        anonymousPlanEvidence,
                                        validatedLoginState);
                            }

                            return DeduplicateCatalogAndPlans(validatedCatalog, validatedPlans);
                        }

                        void TrustResolvedAnonymousCatalogIfKnownAnonymous()
                        {
                            if (catalog.CacheScope != CatalogCacheScope.Anonymous
                                || catalog.IsKnownAnonymous
                                || !regularBrowserAnonymousCatalogFetchHadStrongLoggedOutProof
                                || !IsCompleteLoggedOutProof(currentLoginState)
                                || !currentBookStrongAnonymousProof
                                || loginStateProbeFailed
                                || loginStateProbeFailedInRun
                                || authenticatedUnknownIdentityDiscoveredInRun)
                            {
                                return;
                            }

                            catalog = catalog with { IsKnownAnonymous = true };
                            if (CanSaveCatalogSnapshot(CatalogCacheScope.Anonymous, catalog))
                            {
                                StagePendingCatalogCacheSave(
                                    paths.CacheRoot,
                                    pendingCatalogCacheSaves,
                                    catalog,
                                    catalogClearGeneration);
                            }
                        }

                        async Task<bool> RefetchAuthenticatedSensitiveRenderedCacheAsync(string reason)
                        {
                            if (authenticatedSensitiveRenderedCachePlans.Count == 0)
                            {
                                return true;
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
                                if (!await FetchAndRenderChapterAsync(planToRefetch))
                                {
                                    return false;
                                }
                            }

                            return true;
                        }

                        void FailClosedAuthenticatedSensitiveRenderedCache()
                        {
                            if (authenticatedSensitiveRenderedCachePlans.Count == 0)
                            {
                                return;
                            }

                            List<ChapterPlan> plansToFailClosed =
                                [.. authenticatedSensitiveRenderedCachePlans];
                            authenticatedSensitiveRenderedCachePlans.Clear();
                            foreach (ChapterPlan planToFailClosed in plansToFailClosed)
                            {
                                if (renderedChapters.Remove(
                                    planToFailClosed.Chapter.ChapterId))
                                {
                                    reusedChapters--;
                                    failedChapters++;
                                }

                                renderedChapters[planToFailClosed.Chapter.ChapterId] =
                                    new RenderedChapter(
                                        planToFailClosed.Chapter.Title,
                                        [AppConstants.FailedChapterPlaceholder]);
                            }
                        }

                        void FailClosedAuthenticatedSensitiveRenderedFresh(string? userName)
                        {
                            if (authenticatedSensitiveRenderedFreshPlans.Count == 0)
                            {
                                return;
                            }

                            List<(ChapterPlan Plan, string? UserName)> plansToFailClosed =
                                [.. authenticatedSensitiveRenderedFreshPlans.Where(
                                    renderedPlan => IsSameNormalizedUser(
                                        renderedPlan.UserName,
                                        userName))];
                            authenticatedSensitiveRenderedFreshPlans.RemoveAll(
                                renderedPlan => IsSameNormalizedUser(
                                    renderedPlan.UserName,
                                    userName));
                            foreach ((ChapterPlan planToFailClosed, _) in plansToFailClosed)
                            {
                                if (renderedChapters.Remove(
                                    planToFailClosed.Chapter.ChapterId))
                                {
                                    downloadedChapters--;
                                    failedChapters++;
                                }

                                renderedChapters[planToFailClosed.Chapter.ChapterId] =
                                    new RenderedChapter(
                                        planToFailClosed.Chapter.Title,
                                        [AppConstants.FailedChapterPlaceholder]);
                            }
                        }

                        async Task<bool>
                            RefetchAuthenticatedSensitiveRenderedCacheAfterLoginStateUpdateIfNeededAsync()
                        {
                            if (GetValidatedLoginState(currentLoginState) is not null)
                            {
                                loginStateProbeFailed = false;
                                return await RefetchAuthenticatedSensitiveRenderedCacheAsync(
                                    "after login-state validation");
                            }
                            else
                            {
                                loginStateProbeFailed =
                                    HasAuthenticatedCacheIdentityUncertainty(currentLoginState);
                                if (loginStateProbeFailed)
                                {
                                    return await RefetchAuthenticatedSensitiveRenderedCacheAsync(
                                        loginStateProbeFailedInRun
                                            ? "after login-state probe failure"
                                            : "after login-state identity uncertainty");
                                }
                            }

                            return true;
                        }

                        async Task<bool> FetchAndRenderChapterAsync(ChapterPlan plan)
                        {
                            async Task<bool> HandlePostFetchLoginStateUpdateAsync(
                                bool hadValidatedLoginState)
                            {
                                if (hadValidatedLoginState)
                                {
                                    return true;
                                }

                                if (currentLoginState is { IsLoggedIn: true })
                                {
                                    RemovePendingAnonymousCatalogSaves(
                                        pendingCatalogCacheSaves,
                                        catalog.Metadata.BookId);
                                    if (GetValidatedLoginState(currentLoginState) is not null
                                        && catalog.CacheScope == CatalogCacheScope.Anonymous
                                        && !validatedCatalogReplanCompleted)
                                    {
                                        validatedCatalogReplanRequested = true;
                                        return false;
                                    }
                                }

                                return await RefetchAuthenticatedSensitiveRenderedCacheAfterLoginStateUpdateIfNeededAsync();
                            }

                            long clearGeneration = CacheStore.GetClearGeneration(paths.CacheRoot);
                            bool hadStrongLoggedOutProofBeforeFetch =
                                currentBookStrongAnonymousProof
                                && IsCompleteLoggedOutProof(currentLoginState)
                                && !loginStateProbeFailedInRun
                                && !authenticatedUnknownIdentityDiscoveredInRun;
                            bool hadAuthenticatedLoginStateBeforeFetch =
                                currentLoginState is { IsLoggedIn: true }
                                || validatedCatalogIdentityUncertainAfterConfirmationFailure;
                            bool hadValidatedLoginStateBeforeFetch =
                                GetValidatedLoginState(currentLoginState) is not null;
                            string? preFetchValidatedUserName =
                                GetValidatedLoginState(currentLoginState)?.UserName;
                            string? expectedValidatedScopeUserName = preFetchValidatedUserName
                                ?? (catalog.CacheScope.Kind == CatalogCacheScopeKind.ValidatedUser
                                    ? catalog.CacheScope.UserName
                                    : null);
                            bool requiresSameUserPostFetchConfirmation =
                                hadValidatedLoginStateBeforeFetch
                                || (validatedCatalogIdentityUncertainAfterConfirmationFailure
                                    && catalog.CacheScope.Kind
                                        == CatalogCacheScopeKind.ValidatedUser
                                    && LoginState.NormalizeUserName(
                                        expectedValidatedScopeUserName) is not null);
                            string? authenticatedSensitiveScopeUserName =
                                preFetchValidatedUserName ?? expectedValidatedScopeUserName;
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
                                return true;
                            }

                            IReadOnlyList<string> paragraphs =
                                NormalizeFetchedParagraphs(chapterResult);
                            bool hasFreshPostFetchLoggedOutConfirmation = false;
                            bool postFetchValidatedIdentityProbeAttempted = false;
                            bool hasFreshPostFetchValidatedIdentityConfirmation = false;
                            bool hasFreshPostFetchValidatedUserConfirmation = false;
                            bool vipFullContentIdentityClassificationFailed = false;
                            bool requiresAnonymousSafeFreeFullContentProof =
                                catalog.CacheScope == CatalogCacheScope.Anonymous
                                && catalog.IsKnownAnonymous
                                && !plan.Chapter.IsVip
                                && !chapterResult.IsPreview;
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
                                    postFetchValidatedIdentityProbeAttempted = true;
                                    if (classificationLoginState is not null)
                                    {
                                        currentLoginState = classificationLoginState;
                                        currentBookStrongAnonymousProof =
                                            IsCompleteLoggedOutProof(classificationLoginState);
                                        loginStateProbeFailed =
                                            HasAuthenticatedCacheIdentityUncertainty(
                                                currentLoginState);
                                        vipFullContentClassificationProbeCompleted = true;
                                        hasFreshPostFetchLoggedOutConfirmation =
                                            IsCompleteLoggedOutProof(classificationLoginState)
                                            && !loginStateProbeFailedInRun
                                            && !authenticatedUnknownIdentityDiscoveredInRun;
                                        hasFreshPostFetchValidatedIdentityConfirmation =
                                            classificationLoginState.IsValidated;
                                        hasFreshPostFetchValidatedUserConfirmation =
                                            IsSameValidatedUser(
                                                classificationLoginState,
                                                expectedValidatedScopeUserName);
                                        if (!await HandlePostFetchLoginStateUpdateAsync(
                                            hadValidatedLoginStateBeforeFetch))
                                        {
                                            return false;
                                        }
                                    }
                                    else
                                    {
                                        if (!await RefetchAuthenticatedSensitiveRenderedCacheAfterLoginStateUpdateIfNeededAsync())
                                        {
                                            return false;
                                        }

                                        vipFullContentIdentityClassificationFailed = true;
                                    }
                                }

                                if (hadStrongLoggedOutProofBeforeFetch
                                    && !hasFreshPostFetchLoggedOutConfirmation
                                    && !postFetchValidatedIdentityProbeAttempted)
                                {
                                    LoginState? postFetchLoginState =
                                        await TryGetCurrentLoginStateAsync(
                                            LogMessages
                                                .IgnoreVipFullContentClassificationProbeFailure,
                                            LoginStateProbeMode.WaitForValidatedIdentity);
                                    if (postFetchLoginState is not null)
                                    {
                                        currentLoginState = postFetchLoginState;
                                        currentBookStrongAnonymousProof =
                                            IsCompleteLoggedOutProof(postFetchLoginState);
                                        loginStateProbeFailed =
                                            HasAuthenticatedCacheIdentityUncertainty(
                                                currentLoginState);
                                        hasFreshPostFetchLoggedOutConfirmation =
                                            IsCompleteLoggedOutProof(postFetchLoginState)
                                            && !loginStateProbeFailedInRun
                                            && !authenticatedUnknownIdentityDiscoveredInRun;
                                        hasFreshPostFetchValidatedIdentityConfirmation =
                                            postFetchLoginState.IsValidated;
                                        hasFreshPostFetchValidatedUserConfirmation =
                                            IsSameValidatedUser(
                                                postFetchLoginState,
                                                expectedValidatedScopeUserName);
                                        if (!await HandlePostFetchLoginStateUpdateAsync(
                                            hadValidatedLoginStateBeforeFetch))
                                        {
                                            return false;
                                        }
                                    }
                                    else if (!await RefetchAuthenticatedSensitiveRenderedCacheAfterLoginStateUpdateIfNeededAsync())
                                    {
                                        currentBookStrongAnonymousProof = false;
                                        return false;
                                    }
                                    else
                                    {
                                        currentBookStrongAnonymousProof = false;
                                        vipFullContentIdentityClassificationFailed = true;
                                    }
                                }

                                if (requiresSameUserPostFetchConfirmation
                                    && !hasFreshPostFetchValidatedUserConfirmation)
                                {
                                    LoginState? postFetchLoginState =
                                        await TryGetCurrentLoginStateAsync(
                                            LogMessages
                                                .IgnoreVipFullContentClassificationProbeFailure,
                                            LoginStateProbeMode.WaitForValidatedIdentity);
                                    if (postFetchLoginState is not null)
                                    {
                                        currentLoginState = postFetchLoginState;
                                        currentBookStrongAnonymousProof =
                                            IsCompleteLoggedOutProof(postFetchLoginState);
                                        loginStateProbeFailed =
                                            HasAuthenticatedCacheIdentityUncertainty(
                                                currentLoginState);
                                        hasFreshPostFetchLoggedOutConfirmation =
                                            IsCompleteLoggedOutProof(postFetchLoginState)
                                            && !loginStateProbeFailedInRun
                                            && !authenticatedUnknownIdentityDiscoveredInRun;
                                        hasFreshPostFetchValidatedIdentityConfirmation =
                                            postFetchLoginState.IsValidated;
                                        hasFreshPostFetchValidatedUserConfirmation =
                                            IsSameValidatedUser(
                                                postFetchLoginState,
                                                expectedValidatedScopeUserName);
                                    }

                                    if (!hasFreshPostFetchValidatedUserConfirmation)
                                    {
                                        currentLoginState = null;
                                        loginState = null;
                                        loginStateProbeMode = null;
                                        loginStateProbeFailed = true;
                                        validatedCatalogIdentityUncertainAfterConfirmationFailure = true;
                                        RemovePendingValidatedCatalogSaves(
                                            pendingCatalogCacheSaves,
                                            catalog.Metadata.BookId,
                                            authenticatedSensitiveScopeUserName);
                                        RemovePendingAuthenticatedSensitiveChapterSaves(
                                            pendingChapterCacheSaves,
                                            catalog.Metadata.BookId,
                                            authenticatedSensitiveScopeUserName);
                                        FailClosedAuthenticatedSensitiveRenderedCache();
                                        FailClosedAuthenticatedSensitiveRenderedFresh(
                                            authenticatedSensitiveScopeUserName);
                                        renderedChapters[plan.Chapter.ChapterId] =
                                            new RenderedChapter(
                                                plan.Chapter.Title,
                                                [AppConstants.FailedChapterPlaceholder]);
                                        failedChapters++;
                                        return true;
                                    }
                                }
                            }

                            if (requiresAnonymousSafeFreeFullContentProof
                                && hadStrongLoggedOutProofBeforeFetch
                                && !hasFreshPostFetchLoggedOutConfirmation)
                            {
                                LoginState? postFetchLoginState =
                                    await TryGetCurrentLoginStateAsync(
                                        LogMessages
                                            .IgnoreVipFullContentClassificationProbeFailure,
                                        LoginStateProbeMode.WaitForValidatedIdentity);
                                if (postFetchLoginState is not null)
                                {
                                    currentLoginState = postFetchLoginState;
                                    currentBookStrongAnonymousProof =
                                        IsCompleteLoggedOutProof(postFetchLoginState);
                                    loginStateProbeFailed =
                                        HasAuthenticatedCacheIdentityUncertainty(
                                            currentLoginState);
                                    hasFreshPostFetchLoggedOutConfirmation =
                                        IsCompleteLoggedOutProof(postFetchLoginState)
                                        && !loginStateProbeFailedInRun
                                        && !authenticatedUnknownIdentityDiscoveredInRun;
                                    hasFreshPostFetchValidatedIdentityConfirmation =
                                        postFetchLoginState.IsValidated;
                                    if (currentLoginState is { IsLoggedIn: true })
                                    {
                                        RemovePendingAnonymousCatalogSaves(
                                            pendingCatalogCacheSaves,
                                            catalog.Metadata.BookId);
                                    }

                                    if (!hasFreshPostFetchLoggedOutConfirmation
                                        && !await HandlePostFetchLoginStateUpdateAsync(
                                            hadValidatedLoginStateBeforeFetch))
                                    {
                                        return false;
                                    }
                                }
                                else
                                {
                                    currentBookStrongAnonymousProof = false;
                                    loginStateProbeFailed =
                                        HasAuthenticatedCacheIdentityUncertainty(
                                            currentLoginState);
                                    if (!await RefetchAuthenticatedSensitiveRenderedCacheAfterLoginStateUpdateIfNeededAsync())
                                    {
                                        return false;
                                    }
                                }
                            }

                            bool canAttributeVipFullContentAsPublic =
                                hadStrongLoggedOutProofBeforeFetch
                                && hasFreshPostFetchLoggedOutConfirmation;
                            bool canAttributeFreeFullContentAsPublic =
                                !plan.Chapter.IsVip
                                && !chapterResult.IsPreview
                                && HasAnonymousSafeFreeFullContentProof(
                                    plan.Chapter,
                                    catalog,
                                    rawAnonymousCatalogEvidence,
                                    rawAnonymousPlanEvidence,
                                    hadStrongLoggedOutProofBeforeFetch
                                        && hasFreshPostFetchLoggedOutConfirmation,
                                    hadAuthenticatedLoginStateBeforeFetch);

                            if (vipFullContentIdentityClassificationFailed)
                            {
                                renderedChapters[plan.Chapter.ChapterId] =
                                    new RenderedChapter(
                                       plan.Chapter.Title,
                                       [AppConstants.FailedChapterPlaceholder]);
                                failedChapters++;
                                return true;
                            }

                            if (plan.Chapter.IsVip
                                && !chapterResult.IsPreview
                                && !hasFreshPostFetchLoggedOutConfirmation
                                && !(hasFreshPostFetchValidatedIdentityConfirmation
                                    && (!requiresSameUserPostFetchConfirmation
                                        || hasFreshPostFetchValidatedUserConfirmation)))
                            {
                                renderedChapters[plan.Chapter.ChapterId] =
                                    new RenderedChapter(
                                       plan.Chapter.Title,
                                       [AppConstants.FailedChapterPlaceholder]);
                                failedChapters++;
                                return true;
                            }

                            if (!plan.Chapter.IsVip
                                && !chapterResult.IsPreview
                                && !canAttributeFreeFullContentAsPublic
                                && requiresSameUserPostFetchConfirmation
                                && !hasFreshPostFetchValidatedUserConfirmation)
                            {
                                LoginState? postFetchLoginState =
                                    await TryGetCurrentLoginStateAsync(
                                        LogMessages
                                            .IgnoreVipFullContentClassificationProbeFailure,
                                        LoginStateProbeMode.WaitForValidatedIdentity);
                                if (postFetchLoginState is not null)
                                {
                                    currentLoginState = postFetchLoginState;
                                    currentBookStrongAnonymousProof =
                                        IsCompleteLoggedOutProof(postFetchLoginState);
                                    loginStateProbeFailed =
                                        HasAuthenticatedCacheIdentityUncertainty(
                                            currentLoginState);
                                    hasFreshPostFetchLoggedOutConfirmation =
                                        IsCompleteLoggedOutProof(postFetchLoginState)
                                        && !loginStateProbeFailedInRun
                                        && !authenticatedUnknownIdentityDiscoveredInRun;
                                    hasFreshPostFetchValidatedUserConfirmation =
                                        IsSameValidatedUser(
                                            postFetchLoginState,
                                            expectedValidatedScopeUserName);
                                }

                                if (!hasFreshPostFetchValidatedUserConfirmation)
                                {
                                    currentLoginState = null;
                                    loginState = null;
                                    loginStateProbeMode = null;
                                    loginStateProbeFailed = true;
                                    validatedCatalogIdentityUncertainAfterConfirmationFailure = true;
                                    RemovePendingValidatedCatalogSaves(
                                        pendingCatalogCacheSaves,
                                        catalog.Metadata.BookId,
                                        authenticatedSensitiveScopeUserName);
                                    RemovePendingAuthenticatedSensitiveChapterSaves(
                                        pendingChapterCacheSaves,
                                        catalog.Metadata.BookId,
                                        authenticatedSensitiveScopeUserName);
                                    FailClosedAuthenticatedSensitiveRenderedCache();
                                    FailClosedAuthenticatedSensitiveRenderedFresh(
                                        authenticatedSensitiveScopeUserName);
                                    renderedChapters[plan.Chapter.ChapterId] =
                                        new RenderedChapter(
                                            plan.Chapter.Title,
                                            [AppConstants.FailedChapterPlaceholder]);
                                    failedChapters++;
                                    return true;
                                }
                            }

                            LoginState? vipFullContentAttributionLoginState =
                                !plan.Chapter.IsVip
                                || chapterResult.IsPreview
                                || (currentLoginState is { IsValidated: true }
                                    && hasFreshPostFetchValidatedUserConfirmation)
                                || (canAttributeVipFullContentAsPublic
                                    && IsCompleteLoggedOutProof(currentLoginState))
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
                                    vipFullContentAttributionLoginState,
                                    canAttributeFreeFullContentAsPublic),
                                GetVipFullContentCacheProvenance(
                                    plan.Chapter,
                                    chapterResult,
                                    vipFullContentAttributionLoginState,
                                    canAttributeVipFullContentAsPublic),
                                plan.Chapter.IsVip,
                                canAttributeFreeFullContentAsPublic ? true : null);
                            bool hasAnonymousSafeFreeFullContentProof =
                                !requiresAnonymousSafeFreeFullContentProof
                                || (hadStrongLoggedOutProofBeforeFetch
                                    && hasFreshPostFetchLoggedOutConfirmation);
                            if (CanSaveFetchedChapterCacheEntry(
                                plan.Chapter,
                                catalog,
                                chapterResult,
                                hasAnonymousSafeFreeFullContentProof))
                            {
                                StagePendingChapterCacheSave(
                                    paths.CacheRoot,
                                    pendingChapterCacheSaves,
                                    catalog.Metadata.BookId,
                                    cacheEntry,
                                    clearGeneration);
                            }

                            renderedChapters[plan.Chapter.ChapterId] = new RenderedChapter(
                                plan.Chapter.Title,
                                paragraphs);
                            if (IsAuthenticatedSensitiveFreshRenderedChapter(
                                cacheEntry,
                                authenticatedSensitiveScopeUserName))
                            {
                                authenticatedSensitiveRenderedFreshPlans.Add(
                                    (plan, authenticatedSensitiveScopeUserName));
                            }

                            downloadedChapters++;
                            return true;
                        }

                        bool restartRendering;
                        do
                        {
                            restartRendering = false;
                            int downloadedChaptersBeforeAttempt = downloadedChapters;
                            int reusedChaptersBeforeAttempt = reusedChapters;
                            int failedChaptersBeforeAttempt = failedChapters;
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
                                    renderedChapters[plan.Chapter.ChapterId] =
                                        new RenderedChapter(
                                            plan.Chapter.Title,
                                            plan.CachedEntry!.Paragraphs);
                                    if (IsAuthenticatedSensitiveCachedPlan(plan))
                                    {
                                        authenticatedSensitiveRenderedCachePlans.Add(plan);
                                    }

                                    reusedChapters++;
                                    continue;
                                }

                                bool rendered = await FetchAndRenderChapterAsync(plan);
                                if (!rendered && validatedCatalogReplanRequested)
                                {
                                    LoginState validatedLoginState =
                                        GetValidatedLoginState(currentLoginState)
                                        ?? throw new InvalidOperationException(
                                            "Validated catalog replan was requested without a validated login state.");
                                    CatalogSnapshot anonymousCatalogEvidence = catalog;
                                    List<ChapterPlan> anonymousPlanEvidence = orderedPlans;
                                    if (rawAnonymousCatalogEvidence is not null
                                        && rawAnonymousPlanEvidence is not null)
                                    {
                                        anonymousCatalogEvidence = rawAnonymousCatalogEvidence;
                                        anonymousPlanEvidence = rawAnonymousPlanEvidence;
                                    }

                                    (catalog, orderedPlans) =
                                        await ResolveValidatedCatalogAndPlansForOutputAsync(
                                            validatedLoginState,
                                            anonymousCatalogEvidence,
                                            anonymousPlanEvidence);
                                    outputPath = AppPaths.BuildDefaultOutputPath(
                                        paths.OutputRoot,
                                        catalog.Metadata.BookId,
                                        catalog.Metadata.Title,
                                        catalog.Metadata.Author);
                                    if (!OutputPathComparer.Equals(
                                            outputPath,
                                            confirmedOutputPath)
                                        && !await ConfirmOutputPathForWriteAsync(outputPath))
                                    {
                                        downloadedChapters = downloadedChaptersBeforeBook;
                                        reusedChapters = reusedChaptersBeforeBook;
                                        failedChapters = failedChaptersBeforeBook;
                                        skippedBooks++;
                                        skipBookAfterOutputPathDenied = true;
                                        restartRendering = false;
                                        break;
                                    }

                                    confirmedOutputPath = outputPath;
                                    renderedChapters.Clear();
                                    pendingChapterCacheSaves.Clear();
                                    authenticatedSensitiveRenderedCachePlans.Clear();
                                    authenticatedSensitiveRenderedFreshPlans.Clear();
                                    downloadedChapters = downloadedChaptersBeforeAttempt;
                                    reusedChapters = reusedChaptersBeforeAttempt;
                                    failedChapters = failedChaptersBeforeAttempt;
                                    loginStateProbeFailed = false;
                                    validatedCatalogReplanCompleted = true;
                                    validatedCatalogReplanRequested = false;
                                    restartRendering = true;
                                    break;
                                }

                                if (chapterIndex < orderedPlans.Count - 1)
                                {
                                    TimeSpan delay = RequestDelayPlanner.CalculateDelay(
                                        plan.Chapter.CatalogWordCount,
                                        settings);
                                    await Task.Delay(delay, cancellationToken);
                                }
                            }
                        }
                        while (restartRendering);

                        if (skipBookAfterOutputPathDenied)
                        {
                            continue;
                        }

                        TrustResolvedAnonymousCatalogIfKnownAnonymous();

                        BeforeOutputWriteForTests?.Invoke(outputPath);
                        AppPaths.CreateDirectoryRejectingReparseAncestors(paths.OutputRoot);
                        AppPaths.CreateDirectoryRejectingReparseAncestors(
                            Path.GetDirectoryName(outputPath)!);

                        string markdown = MarkdownRenderer.Render(catalog, renderedChapters);
                        if (!await ConfirmOutputPathForWriteAsync(outputPath))
                        {
                            downloadedChapters = downloadedChaptersBeforeBook;
                            reusedChapters = reusedChaptersBeforeBook;
                            failedChapters = failedChaptersBeforeBook;
                            skippedBooks++;
                            continue;
                        }

                        await CommitPendingCatalogCacheSavesAsync(
                            paths.CacheRoot,
                            pendingCatalogCacheSaves,
                            currentLoginState,
                            cancellationToken);

                        foreach (PendingChapterCacheSave pendingSave in pendingChapterCacheSaves)
                        {
                            await CacheStore.TrySaveChapterIfClearGenerationUnchangedAsync(
                                paths.CacheRoot,
                                pendingSave.BookId,
                                pendingSave.Entry,
                                ChapterCacheExpectedState.PresentOrAbsent,
                                pendingSave.ClearGeneration,
                                cancellationToken);
                        }

                        AfterPendingCacheCommitsForTests?.Invoke(outputPath);
                        AppPaths.CreateDirectoryRejectingReparseAncestors(paths.OutputRoot);
                        AppPaths.CreateDirectoryRejectingReparseAncestors(
                            Path.GetDirectoryName(outputPath)!);
                        if (!await ConfirmOutputPathForWriteAsync(outputPath))
                        {
                            downloadedChapters = downloadedChaptersBeforeBook;
                            reusedChapters = reusedChaptersBeforeBook;
                            failedChapters = failedChaptersBeforeBook;
                            skippedBooks++;
                            continue;
                        }

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
                cancellationToken,
                fetchKnownAnonymousCatalogAsync: () => FetchCatalogWithIsolatedAnonymousBrowserAsync(
                    target.BookId,
                    settings,
                    paths,
                    cancellationToken));

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
        CancellationToken cancellationToken,
        Func<Task<CatalogSnapshot>>? fetchKnownAnonymousCatalogAsync = null,
        List<PendingCatalogCacheSave>? pendingCatalogCacheSaves = null,
        Func<CatalogCacheScope, Task<bool>>? confirmValidatedScopeAsync = null,
        Func<CatalogCacheScope, bool>? hasValidatedScopeProof = null)
    {
        CatalogSnapshot? cachedCatalog = null;
        if (!forceRefresh)
        {
            cachedCatalog = await CacheStore.GetCatalogAsync(
                paths.CacheRoot,
                bookId,
                scope,
                cancellationToken);
            if (cachedCatalog is not null && CacheStore.IsCatalogFresh(
                    cachedCatalog,
                    settings.CatalogCacheTtlHours,
                    timeProvider))
            {
                if (scope.Kind != CatalogCacheScopeKind.ValidatedUser
                    || (hasValidatedScopeProof is not null
                        && hasValidatedScopeProof(scope))
                    || confirmValidatedScopeAsync is null
                    || await confirmValidatedScopeAsync(scope))
                {
                    return (cachedCatalog, ReusedCache: true);
                }
            }
        }

        if (scope == CatalogCacheScope.Anonymous
            && fetchKnownAnonymousCatalogAsync is not null)
        {
            CatalogSnapshot knownAnonymousCatalog = await fetchKnownAnonymousCatalogAsync();
            knownAnonymousCatalog = CatalogSnapshotValidation.ValidateAndNormalizeForRequestedBook(
                knownAnonymousCatalog,
                bookId);
            return (knownAnonymousCatalog, ReusedCache: false);
        }

        long clearGeneration = pendingCatalogCacheSaves is not null
            ? CacheStore.GetClearGeneration(paths.CacheRoot)
            : 0;
        CatalogSnapshot fetchedCatalog = await FetchCatalogAsync(
            bookId,
            paths,
            getBrowserAsync,
            scope,
            isKnownAnonymousFetch: false,
            cancellationToken,
            pendingCatalogCacheSaves,
            clearGeneration,
            confirmValidatedScopeAsync: confirmValidatedScopeAsync);
        return (fetchedCatalog, ReusedCache: false);
    }

    private static async Task<CatalogSnapshot> FetchCatalogAsync(
        string bookId,
        AppStoragePaths paths,
        Func<Task<IQidianBrowserSession>> getBrowserAsync,
        CatalogCacheScope scope,
        bool isKnownAnonymousFetch,
        CancellationToken cancellationToken,
        List<PendingCatalogCacheSave>? pendingCatalogCacheSaves = null,
        long? clearGeneration = null,
        Action<long>? clearGenerationCaptured = null,
        Func<CatalogCacheScope, Task<bool>>? confirmValidatedScopeAsync = null)
    {
        clearGeneration ??= pendingCatalogCacheSaves is not null
            ? CacheStore.GetClearGeneration(paths.CacheRoot)
            : null;
        if (clearGeneration is not null)
        {
            clearGenerationCaptured?.Invoke(clearGeneration.Value);
        }

        CatalogSnapshot fetchedCatalog = await (await getBrowserAsync()).FetchCatalogAsync(
            bookId,
            cancellationToken);
        fetchedCatalog = CatalogSnapshotValidation.ValidateAndNormalizeForRequestedBook(
            fetchedCatalog,
            bookId);
        if (scope.Kind == CatalogCacheScopeKind.ValidatedUser
            && confirmValidatedScopeAsync is not null
            && !await confirmValidatedScopeAsync(scope))
        {
            throw new OperationalException(
                "Validated catalog fetch was not confirmed for the same signed-in user.");
        }

        fetchedCatalog = fetchedCatalog with
        {
            CacheScope = scope,
            IsKnownAnonymous = scope == CatalogCacheScope.Anonymous
                && isKnownAnonymousFetch,
        };
        if (CanSaveCatalogSnapshot(scope, fetchedCatalog))
        {
            if (pendingCatalogCacheSaves is null)
            {
                await CacheStore.SaveCatalogAsync(paths.CacheRoot, fetchedCatalog, cancellationToken);
            }
            else
            {
                StagePendingCatalogCacheSave(
                    paths.CacheRoot,
                    pendingCatalogCacheSaves,
                    fetchedCatalog,
                    clearGeneration);
            }
        }

        return fetchedCatalog;
    }

    private async Task<CatalogSnapshot> FetchCatalogWithIsolatedAnonymousBrowserAsync(
        string bookId,
        ResolvedAppSettings settings,
        AppStoragePaths paths,
        CancellationToken cancellationToken,
        List<PendingCatalogCacheSave>? pendingCatalogCacheSaves = null,
        Action<long>? clearGenerationCaptured = null)
    {
        IQidianBrowserSession? isolatedBrowser = null;
        try
        {
            long clearGeneration = pendingCatalogCacheSaves is not null
                ? CacheStore.GetClearGeneration(paths.CacheRoot)
                : 0;
            if (pendingCatalogCacheSaves is not null)
            {
                clearGenerationCaptured?.Invoke(clearGeneration);
            }

            isolatedBrowser = await browserManager.OpenAsync(
                settings,
                paths,
                headless: true,
                cancellationToken,
                isolatedAnonymous: true);
            CatalogSnapshot fetchedCatalog =
                await isolatedBrowser.FetchCatalogAsync(bookId, cancellationToken);
            fetchedCatalog = CatalogSnapshotValidation.ValidateAndNormalizeForRequestedBook(
                fetchedCatalog,
                bookId);
            fetchedCatalog = fetchedCatalog with
            {
                CacheScope = CatalogCacheScope.Anonymous,
                IsKnownAnonymous = true,
            };
            if (pendingCatalogCacheSaves is null)
            {
                await CacheStore.SaveCatalogAsync(
                    paths.CacheRoot,
                    fetchedCatalog,
                    cancellationToken);
            }
            else
            {
                StagePendingCatalogCacheSave(
                    paths.CacheRoot,
                    pendingCatalogCacheSaves,
                    fetchedCatalog,
                    clearGeneration);
            }
            return fetchedCatalog;
        }
        finally
        {
            if (isolatedBrowser is not null)
            {
                await isolatedBrowser.DisposeBestEffortAsync();
            }
        }
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

    internal static async Task<string?> TryGetCachedOutputPathForOverwriteCheckAsync(
        BookReference target,
        ResolvedAppSettings settings,
        AppStoragePaths paths,
        LoginState? loginState,
        LoginStateProbeMode? loginStateProbeMode,
        bool hasRunIdentityUncertainty,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        async Task<CatalogSnapshot?> GetFreshCachedCatalogForPredictionAsync(
            CatalogCacheScope scope)
        {
            CatalogSnapshot? cachedCatalog =
                await CacheStore.GetCatalogForOutputPredictionAsync(
                    paths.CacheRoot,
                    target.BookId,
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

        async Task<CatalogSnapshot?> GetFreshTrustedCatalogAsync(CatalogCacheScope scope)
        {
            CatalogSnapshot? cachedCatalog = await CacheStore.GetCatalogAsync(
                paths.CacheRoot,
                target.BookId,
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

        LoginState? validatedLoginState = GetValidatedLoginState(loginState);
        if (validatedLoginState is { IsValidated: true }
            && loginStateProbeMode == LoginStateProbeMode.WaitForValidatedIdentity
            && !hasRunIdentityUncertainty)
        {
            CatalogSnapshot? cachedAnonymousFreeCatalog =
                await GetFreshTrustedCatalogAsync(CatalogCacheScope.Anonymous);
            if (cachedAnonymousFreeCatalog is not null)
            {
                List<ChapterPlan> anonymousFreePlans = await BuildChapterPlansAsync(
                    cachedAnonymousFreeCatalog,
                    paths.CacheRoot,
                    validatedLoginState,
                    cancellationToken);
                if (!anonymousFreePlans.Any(plan => plan.Chapter.IsVip)
                    && !RequiresCurrentSessionCatalogEvaluation(
                        anonymousFreePlans,
                        isFreshAnonymousCatalog: true))
                {
                    return BuildOutputPath(cachedAnonymousFreeCatalog, paths);
                }
            }

            CatalogSnapshot? cachedValidatedCatalog =
                await GetFreshCachedCatalogForPredictionAsync(
                    CatalogCacheScope.ForValidatedUser(validatedLoginState.UserName!));
            if (cachedValidatedCatalog is not null)
            {
                List<ChapterPlan> validatedPlans = await BuildChapterPlansAsync(
                    cachedValidatedCatalog,
                    paths.CacheRoot,
                    validatedLoginState,
                    cancellationToken);
                if (RequiresValidatedCatalogRefreshForEntitlementMismatch(
                    validatedPlans,
                    validatedLoginState))
                {
                    return null;
                }

                CatalogSnapshot? cachedAnonymousConflictCatalog =
                   await GetFreshTrustedCatalogAsync(CatalogCacheScope.Anonymous);
                if (cachedAnonymousConflictCatalog is not null)
                {
                    List<ChapterPlan> anonymousPlans = await BuildChapterPlansAsync(
                        cachedAnonymousConflictCatalog,
                        paths.CacheRoot,
                        validatedLoginState: null,
                        cancellationToken);
                    if (RequiresValidatedCatalogRefreshForAnonymousVipConflict(
                        anonymousPlans,
                        cachedAnonymousConflictCatalog,
                        cachedValidatedCatalog))
                    {
                        return null;
                    }
                }

                return BuildOutputPath(cachedValidatedCatalog, paths);
            }
        }

        if (hasRunIdentityUncertainty
            || !IsCompleteLoggedOutProof(loginState)
            || loginStateProbeMode != LoginStateProbeMode.WaitForValidatedIdentity)
        {
            return null;
        }

        CatalogSnapshot? cachedAnonymousCatalog =
            await GetFreshTrustedCatalogAsync(CatalogCacheScope.Anonymous);
        if (cachedAnonymousCatalog is not null)
        {
            List<ChapterPlan> anonymousPlans = await BuildChapterPlansAsync(
                cachedAnonymousCatalog,
                paths.CacheRoot,
                validatedLoginState: null,
                cancellationToken);
            if (anonymousPlans.Any(plan => plan.Chapter.IsVip)
                || RequiresAuthenticatedPlanEvaluation(anonymousPlans))
            {
                return null;
            }

            return BuildOutputPath(cachedAnonymousCatalog, paths);
        }

        return null;

        static string BuildOutputPath(CatalogSnapshot catalog, AppStoragePaths paths)
            => AppPaths.BuildDefaultOutputPath(
                paths.OutputRoot,
                catalog.Metadata.BookId,
                catalog.Metadata.Title,
                catalog.Metadata.Author);
    }

    private static async Task CommitPendingCatalogCacheSavesAsync(
        string cacheRoot,
        List<PendingCatalogCacheSave> pendingCatalogCacheSaves,
        LoginState? currentLoginState,
        CancellationToken cancellationToken)
    {
        foreach (PendingCatalogCacheSave pendingSave in pendingCatalogCacheSaves)
        {
            if (pendingSave.Catalog.CacheScope.Kind == CatalogCacheScopeKind.ValidatedUser
                && !IsSameValidatedUser(
                    currentLoginState,
                    pendingSave.Catalog.CacheScope.UserName))
            {
                continue;
            }

            await CacheStore.TrySaveCatalogIfClearGenerationUnchangedAsync(
                cacheRoot,
                pendingSave.Catalog,
                pendingSave.ClearGeneration,
                cancellationToken);
        }

        pendingCatalogCacheSaves.Clear();
    }

    private static void StagePendingCatalogCacheSave(
        string cacheRoot,
        List<PendingCatalogCacheSave> pendingCatalogCacheSaves,
        CatalogSnapshot catalog,
        long? clearGeneration = null)
        => pendingCatalogCacheSaves.Add(new PendingCatalogCacheSave(
            catalog,
            clearGeneration ?? CacheStore.GetClearGeneration(cacheRoot)));

    private static void RemovePendingAnonymousCatalogSaves(
        List<PendingCatalogCacheSave> pendingCatalogCacheSaves,
        string bookId)
        => pendingCatalogCacheSaves.RemoveAll(
            pendingSave => pendingSave.Catalog.Metadata.BookId == bookId
                && pendingSave.Catalog.CacheScope == CatalogCacheScope.Anonymous);

    private static void RemovePendingValidatedCatalogSaves(
        List<PendingCatalogCacheSave> pendingCatalogCacheSaves,
        string bookId,
        string? userName)
        => pendingCatalogCacheSaves.RemoveAll(
            pendingSave => pendingSave.Catalog.Metadata.BookId == bookId
                && pendingSave.Catalog.CacheScope.Kind == CatalogCacheScopeKind.ValidatedUser
                && string.Equals(
                    pendingSave.Catalog.CacheScope.UserName,
                    LoginState.NormalizeUserName(userName),
                    StringComparison.Ordinal));

    private static void StagePendingChapterCacheSave(
        string cacheRoot,
        List<PendingChapterCacheSave> pendingChapterCacheSaves,
        string bookId,
        ChapterCacheEntry entry,
        long? clearGeneration = null)
        => pendingChapterCacheSaves.Add(new PendingChapterCacheSave(
            bookId,
            entry,
            clearGeneration ?? CacheStore.GetClearGeneration(cacheRoot)));

    private static void RemovePendingAuthenticatedSensitiveChapterSaves(
        List<PendingChapterCacheSave> pendingChapterCacheSaves,
        string bookId,
        string? userName)
        => pendingChapterCacheSaves.RemoveAll(
            pendingSave => pendingSave.BookId == bookId
                && IsAuthenticatedSensitivePendingChapterSave(pendingSave.Entry, userName));

    private static bool IsAuthenticatedSensitivePendingChapterSave(
        ChapterCacheEntry entry,
        string? userName)
        => (entry.CatalogIsVip == true && entry.IsPreview)
            || IsUserSensitiveFullContentCache(
                entry.IsPreview,
                entry.CatalogAccessState,
                entry.VisibleToUserName,
                entry.VipFullContentProvenance,
                entry.CatalogIsVip,
                entry.IsAnonymousSafeFullContent)
            || (entry.CatalogIsVip == true
                && !entry.IsPreview
                && entry.VipFullContentProvenance != VipFullContentCacheProvenance.Public)
            || (LoginState.NormalizeUserName(userName) is { Length: > 0 } normalizedUserName
                && string.Equals(
                    LoginState.NormalizeUserName(entry.VisibleToUserName),
                    normalizedUserName,
                    StringComparison.Ordinal)
                && entry.VipFullContentProvenance
                    != VipFullContentCacheProvenance.Public);

    private static bool IsAuthenticatedSensitiveFreshRenderedChapter(
        ChapterCacheEntry entry,
        string? userName)
        => !entry.IsPreview
            && IsAuthenticatedSensitivePendingChapterSave(entry, userName);

    private sealed record PendingChapterCacheSave(
        string BookId,
        ChapterCacheEntry Entry,
        long ClearGeneration);

    private sealed record PendingCatalogCacheSave(CatalogSnapshot Catalog, long ClearGeneration);

    private static bool CanSaveCatalogSnapshot(CatalogCacheScope scope, CatalogSnapshot catalog)
        => scope != CatalogCacheScope.Anonymous
            || catalog.IsKnownAnonymous;

    private static bool IsSameValidatedUser(LoginState? loginState, string? userName)
        => loginState is { IsValidated: true }
            && LoginState.NormalizeUserName(loginState.UserName) is { } normalizedLoginUserName
            && LoginState.NormalizeUserName(userName) is { } normalizedUserName
            && string.Equals(normalizedLoginUserName, normalizedUserName, StringComparison.Ordinal);

    internal static bool CanSaveChapterCacheEntry(
        ChapterDescriptor chapter,
        CatalogSnapshot catalog)
        => chapter.IsVip
            || catalog.CacheScope != CatalogCacheScope.Anonymous
            || catalog.IsKnownAnonymous;

    private static bool CanSaveFetchedChapterCacheEntry(
        ChapterDescriptor chapter,
        CatalogSnapshot catalog,
        ChapterFetchResult chapterResult,
        bool hasAnonymousSafeFreeFullContentProof)
        => CanSaveChapterCacheEntry(chapter, catalog)
            && (catalog.CacheScope != CatalogCacheScope.Anonymous
                || chapter.IsVip
                || chapterResult.IsPreview
                || hasAnonymousSafeFreeFullContentProof);

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
                cachedProbe.CatalogAccessState,
                cachedProbe.VisibleToUserName,
                cachedProbe.VipFullContentProvenance,
                cachedProbe.CatalogIsVip,
                cachedProbe.IsAnonymousSafeFullContent,
                validatedLoginState))
            {
                cachedEntry = await CacheStore.GetChapterAsync(
                    cacheRoot,
                    catalog.BookId,
                    chapter.ChapterId,
                    cancellationToken);
                if (cachedEntry is null
                    || !CanReuseFullChapterCacheEntry(
                        chapter,
                        cachedEntry,
                        validatedLoginState))
                {
                    status = ChapterPlanStatus.FetchRequired;
                }
                else
                {
                    cachedProbe = CreateChapterCacheProbe(cachedEntry);
                    status = ChapterPlanStatus.Cached;
                }
            }
            else
            {
                status = ChapterPlanStatus.FetchRequired;
            }

            plans.Add(new ChapterPlan(chapter, status, cachedProbe, cachedEntry));
        }

        return plans;
    }

    private static ChapterCacheProbe CreateChapterCacheProbe(ChapterCacheEntry cachedEntry)
        => new(
            cachedEntry.ChapterId,
            (ParagraphsProbe?)null,
            cachedEntry.IsPreview,
            cachedEntry.CatalogWordCount,
            cachedEntry.CatalogAccessState,
            cachedEntry.VisibleToUserName,
            cachedEntry.VipFullContentProvenance,
            cachedEntry.CatalogIsVip,
            cachedEntry.IsAnonymousSafeFullContent);

    private static bool CanReuseFullChapterCacheEntry(
        ChapterDescriptor chapter,
        ChapterCacheEntry cachedEntry,
        LoginState? validatedLoginState)
        => cachedEntry.CatalogWordCount == chapter.CatalogWordCount
            && (cachedEntry.CatalogAccessState == chapter.CatalogAccessState
                || CanIgnoreCatalogAccessStateMismatchForReusableVipFullCache(
                    chapter,
                    new ChapterCacheProbe(
                        cachedEntry.ChapterId,
                        (ParagraphsProbe?)null,
                        cachedEntry.IsPreview,
                        cachedEntry.CatalogWordCount,
                        cachedEntry.CatalogAccessState,
                        cachedEntry.VisibleToUserName,
                        cachedEntry.VipFullContentProvenance,
                        cachedEntry.CatalogIsVip,
                        cachedEntry.IsAnonymousSafeFullContent),
                    validatedLoginState))
            && CanReuseCachedChapter(
                chapter,
                cachedEntry.IsPreview,
                cachedEntry.CatalogAccessState,
                cachedEntry.VisibleToUserName,
                cachedEntry.VipFullContentProvenance,
                cachedEntry.CatalogIsVip,
                cachedEntry.IsAnonymousSafeFullContent,
                validatedLoginState);

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

    private static bool IsCompleteLoggedOutProof(LoginState? loginState)
        => loginState is { IsLoggedIn: false, IsProbeComplete: true };

    internal static LoginState SelectCachedLoginStateForProbe(
        LoginState? cachedLoginState,
        LoginState probedLoginState)
        => probedLoginState;

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

    internal static bool RequiresValidatedCatalogRefreshForAnonymousVipConflict(
        IReadOnlyList<ChapterPlan>? anonymousPlans,
        CatalogSnapshot? anonymousCatalog,
        CatalogSnapshot validatedCatalog)
    {
        if (anonymousPlans is null
            || anonymousCatalog is null
            || validatedCatalog.FetchedAtUtc >= anonymousCatalog.FetchedAtUtc)
        {
            return false;
        }

        return HasValidatedCatalogAnonymousVipConflict(
            anonymousPlans,
            anonymousCatalog,
            validatedCatalog);
    }

    internal static bool HasValidatedCatalogAnonymousVipConflict(
        IReadOnlyList<ChapterPlan>? anonymousPlans,
        CatalogSnapshot? anonymousCatalog,
        CatalogSnapshot validatedCatalog)
    {
        if (anonymousPlans is null || anonymousCatalog is null)
        {
            return false;
        }

        Dictionary<string, int> anonymousCounts = anonymousPlans
            .GroupBy(static plan => plan.Chapter.ChapterId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);
        Dictionary<string, List<ChapterDescriptor>> validatedById = validatedCatalog.Volumes
            .SelectMany(static volume => volume.Chapters)
            .GroupBy(static chapter => chapter.ChapterId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToList(),
                StringComparer.Ordinal);
        Dictionary<string, List<ChapterPlan>> anonymousByTitle = anonymousPlans
            .GroupBy(
                static plan => NormalizeChapterTitleKey(plan.Chapter.Title),
                StringComparer.Ordinal)
            .Where(static group => group.Key.Length > 0)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToList(),
                StringComparer.Ordinal);
        Dictionary<string, List<ChapterDescriptor>> validatedByTitle = validatedCatalog.Volumes
            .SelectMany(static volume => volume.Chapters)
            .GroupBy(
                static chapter => NormalizeChapterTitleKey(chapter.Title),
                StringComparer.Ordinal)
            .Where(static group => group.Key.Length > 0)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToList(),
                StringComparer.Ordinal);

        return anonymousPlans.Any(plan =>
        {
            validatedById.TryGetValue(
                plan.Chapter.ChapterId,
                out List<ChapterDescriptor>? matchingValidatedChapters);
            matchingValidatedChapters ??= [];
            if (!plan.Chapter.IsVip)
            {
                return IsAnonymousFreeValidatedVipConflict(
                    plan,
                    matchingValidatedChapters,
                    anonymousByTitle,
                    validatedByTitle);
            }

            if (matchingValidatedChapters.Count != 1)
            {
                return true;
            }

            if (anonymousCounts[plan.Chapter.ChapterId] != 1
                || IsAnonymousVipValidatedFreeConflict(matchingValidatedChapters[0]))
            {
                return true;
            }

            string titleKey = NormalizeChapterTitleKey(plan.Chapter.Title);
            return titleKey.Length > 0
                && anonymousByTitle.TryGetValue(
                    titleKey,
                    out List<ChapterPlan>? matchingAnonymousTitlePlans)
                && validatedByTitle.TryGetValue(
                    titleKey,
                    out List<ChapterDescriptor>? matchingValidatedTitleChapters)
                && IsAnonymousVipValidatedFreeTitleConflict(
                    plan,
                    matchingAnonymousTitlePlans,
                    matchingValidatedTitleChapters);
        });
    }

    private static bool HasAnonymousVipValidatedFreeConflict(
        IReadOnlyList<ChapterPlan>? anonymousPlans,
        CatalogSnapshot? anonymousCatalog,
        CatalogSnapshot validatedCatalog)
    {
        if (anonymousPlans is null || anonymousCatalog is null)
        {
            return false;
        }

        Dictionary<string, List<ChapterDescriptor>> validatedById = validatedCatalog.Volumes
            .SelectMany(static volume => volume.Chapters)
            .GroupBy(static chapter => chapter.ChapterId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToList(),
                StringComparer.Ordinal);
        Dictionary<string, List<ChapterPlan>> anonymousByTitle = anonymousPlans
            .GroupBy(
                static plan => NormalizeChapterTitleKey(plan.Chapter.Title),
                StringComparer.Ordinal)
            .Where(static group => group.Key.Length > 0)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToList(),
                StringComparer.Ordinal);
        Dictionary<string, List<ChapterDescriptor>> validatedByTitle = validatedCatalog.Volumes
            .SelectMany(static volume => volume.Chapters)
            .GroupBy(
                static chapter => NormalizeChapterTitleKey(chapter.Title),
                StringComparer.Ordinal)
            .Where(static group => group.Key.Length > 0)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToList(),
                StringComparer.Ordinal);

        return anonymousPlans.Any(plan =>
        {
            if (!plan.Chapter.IsVip)
            {
                return false;
            }

            bool hasIdConflict = validatedById.TryGetValue(
                    plan.Chapter.ChapterId,
                    out List<ChapterDescriptor>? matchingValidatedChapters)
                && matchingValidatedChapters.Any(IsAnonymousVipValidatedFreeConflict);
            if (hasIdConflict)
            {
                return true;
            }

            string titleKey = NormalizeChapterTitleKey(plan.Chapter.Title);
            return titleKey.Length > 0
                && anonymousByTitle.TryGetValue(
                    titleKey,
                    out List<ChapterPlan>? matchingAnonymousTitlePlans)
                && validatedByTitle.TryGetValue(
                    titleKey,
                    out List<ChapterDescriptor>? matchingValidatedTitleChapters)
                && IsAnonymousVipValidatedFreeTitleConflict(
                    plan,
                    matchingAnonymousTitlePlans,
                    matchingValidatedTitleChapters);
        });
    }

    private static (CatalogSnapshot Catalog, List<ChapterPlan> Plans)
        FailClosedValidatedCatalogForAnonymousVipConflicts(
            CatalogSnapshot validatedCatalog,
            List<ChapterPlan> validatedPlans,
            IReadOnlyList<ChapterPlan> anonymousPlans,
            LoginState validatedLoginState)
    {
        HashSet<string> anonymousVipChapterIds = anonymousPlans
            .Where(static plan => plan.Chapter.IsVip)
            .Select(static plan => plan.Chapter.ChapterId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> anonymousVipTitleKeys = anonymousPlans
            .Where(static plan => plan.Chapter.IsVip)
            .Select(static plan => NormalizeChapterTitleKey(plan.Chapter.Title))
            .Where(static titleKey => titleKey.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<string, ChapterDescriptor> failClosedChaptersById = [];
        Dictionary<string, ChapterPlan> failClosedPlansById = [];
        foreach (ChapterPlan plan in validatedPlans)
        {
            ChapterDescriptor chapter = plan.Chapter;
            string titleKey = NormalizeChapterTitleKey(chapter.Title);
            if (!IsAnonymousVipValidatedFreeConflict(chapter)
                || (!anonymousVipChapterIds.Contains(chapter.ChapterId)
                    && (titleKey.Length == 0
                        || !anonymousVipTitleKeys.Contains(titleKey))))
            {
                failClosedPlansById[chapter.ChapterId] = plan;
                continue;
            }

            ChapterDescriptor failClosedChapter = chapter with
            {
                IsVip = true,
                CatalogAccessState = CatalogChapterAccessState.PurchaseRequired,
            };
            ChapterPlan failClosedPlan = plan with
            {
                Chapter = failClosedChapter,
            };
            if (plan.CachedEntry is not null
                && !CanReuseCachedChapter(
                    failClosedChapter,
                    plan.CachedProbe?.IsPreview ?? plan.CachedEntry.IsPreview,
                    plan.CachedProbe?.CatalogAccessState
                        ?? plan.CachedEntry.CatalogAccessState,
                    plan.CachedProbe?.VisibleToUserName
                        ?? plan.CachedEntry.VisibleToUserName,
                    plan.CachedProbe?.VipFullContentProvenance
                        ?? plan.CachedEntry.VipFullContentProvenance,
                    plan.CachedProbe?.CatalogIsVip ?? plan.CachedEntry.CatalogIsVip,
                    plan.CachedProbe?.IsAnonymousSafeFullContent
                        ?? plan.CachedEntry.IsAnonymousSafeFullContent,
                    validatedLoginState))
            {
                failClosedPlan = failClosedPlan with
                {
                    Status = ChapterPlanStatus.FetchRequired,
                    CachedEntry = null,
                };
            }

            failClosedChaptersById[chapter.ChapterId] = failClosedChapter;
            failClosedPlansById[chapter.ChapterId] = failClosedPlan;
        }

        CatalogSnapshot failClosedCatalog = validatedCatalog with
        {
            Volumes = validatedCatalog.Volumes
                .Select(volume => volume with
                {
                    IsVip = volume.IsVip
                        || volume.Chapters.Any(chapter =>
                            failClosedChaptersById.ContainsKey(chapter.ChapterId)),
                    Chapters = volume.Chapters
                        .Select(chapter => failClosedChaptersById.TryGetValue(
                                chapter.ChapterId,
                                out ChapterDescriptor? failClosedChapter)
                            ? failClosedChapter
                            : chapter)
                        .ToArray(),
                })
                .ToArray(),
        };
        List<ChapterPlan> failClosedPlans = validatedPlans
            .Select(plan => failClosedPlansById.TryGetValue(
                    plan.Chapter.ChapterId,
                    out ChapterPlan? failClosedPlan)
                ? failClosedPlan
                : plan)
            .ToList();

        return (failClosedCatalog, failClosedPlans);
    }

    private static bool IsAnonymousFreeValidatedVipConflict(
        ChapterPlan anonymousFreePlan,
        IReadOnlyList<ChapterDescriptor> matchingValidatedChapters,
        Dictionary<string, List<ChapterPlan>> anonymousByTitle,
        Dictionary<string, List<ChapterDescriptor>> validatedByTitle)
    {
        if (anonymousFreePlan.Chapter.CatalogAccessState
            == CatalogChapterAccessState.PurchaseRequired)
        {
            return false;
        }

        if (matchingValidatedChapters.Any(static chapter => chapter.IsVip))
        {
            return true;
        }

        string titleKey = NormalizeChapterTitleKey(anonymousFreePlan.Chapter.Title);
        return titleKey.Length > 0
            && anonymousByTitle.TryGetValue(
                titleKey,
                out List<ChapterPlan>? matchingAnonymousTitlePlans)
            && matchingAnonymousTitlePlans.Any(plan => !plan.Chapter.IsVip
                && plan.Chapter.CatalogAccessState
                    != CatalogChapterAccessState.PurchaseRequired)
            && validatedByTitle.TryGetValue(
                titleKey,
                out List<ChapterDescriptor>? matchingValidatedTitleChapters)
            && matchingValidatedTitleChapters.Any(static chapter => chapter.IsVip);
    }

    private static bool IsAnonymousVipValidatedFreeConflict(ChapterDescriptor validatedChapter)
        => !validatedChapter.IsVip
            && (validatedChapter.CatalogAccessState == CatalogChapterAccessState.Accessible
                || validatedChapter.CatalogAccessState == CatalogChapterAccessState.Unknown);

    private static bool IsAnonymousVipValidatedFreeTitleConflict(
        ChapterPlan anonymousVipPlan,
        List<ChapterPlan> matchingAnonymousTitlePlans,
        IReadOnlyList<ChapterDescriptor> matchingValidatedTitleChapters)
    {
        bool hasConcreteAnonymousTitleConflict =
            matchingAnonymousTitlePlans.Count == 1
            || matchingAnonymousTitlePlans.Any(plan =>
                !string.Equals(
                    plan.Chapter.ChapterId,
                    anonymousVipPlan.Chapter.ChapterId,
                    StringComparison.Ordinal)
                && !plan.Chapter.IsVip
                && (plan.Chapter.CatalogAccessState == CatalogChapterAccessState.Accessible
                    || plan.Chapter.CatalogAccessState == CatalogChapterAccessState.Unknown))
            || (matchingAnonymousTitlePlans.Count > 1
                && matchingAnonymousTitlePlans.All(static plan => plan.Chapter.IsVip));
        return hasConcreteAnonymousTitleConflict
            && matchingValidatedTitleChapters.Any(chapter =>
                !string.Equals(
                    chapter.ChapterId,
                    anonymousVipPlan.Chapter.ChapterId,
                    StringComparison.Ordinal)
                && IsAnonymousVipValidatedFreeConflict(chapter));
    }

    private static (CatalogSnapshot Catalog, List<ChapterPlan> Plans) DeduplicateCatalogAndPlans(
        CatalogSnapshot catalog,
        List<ChapterPlan> plans)
    {
        Dictionary<string, ChapterPlan> selectedPlans = [];
        Dictionary<string, int> selectedPlanIndexes = [];
        for (int planIndex = 0; planIndex < plans.Count; planIndex++)
        {
            ChapterPlan plan = plans[planIndex];
            if (!selectedPlans.TryGetValue(
                    plan.Chapter.ChapterId,
                    out ChapterPlan? existingPlan)
                || IsMoreConservativePlan(plan, existingPlan))
            {
                selectedPlans[plan.Chapter.ChapterId] = plan;
                selectedPlanIndexes[plan.Chapter.ChapterId] = planIndex;
            }
        }

        Dictionary<string, string> safeTitleKeysByChapterId = selectedPlans.Values
            .GroupBy(
                static plan => NormalizeChapterTitleKey(plan.Chapter.Title),
                StringComparer.Ordinal)
            .Where(static group => IsConcreteSameTitleDuplicateGroup(group.ToArray()))
            .SelectMany(static group => group.Select(plan => new
            {
                plan.Chapter.ChapterId,
                TitleKey = group.Key,
            }))
            .ToDictionary(
                static entry => entry.ChapterId,
                static entry => entry.TitleKey,
                StringComparer.Ordinal);

        Dictionary<string, ChapterPlan> finalSelectedPlans = [];
        Dictionary<string, int> finalSelectedPlanIndexes = [];
        foreach ((string chapterId, ChapterPlan plan) in selectedPlans)
        {
            string dedupeKey = GetChapterDedupeKey(chapterId, safeTitleKeysByChapterId);
            int planIndex = selectedPlanIndexes[chapterId];
            if (!finalSelectedPlans.TryGetValue(dedupeKey, out ChapterPlan? existingPlan)
                || IsMoreConservativePlan(plan, existingPlan))
            {
                finalSelectedPlans[dedupeKey] = plan;
                finalSelectedPlanIndexes[dedupeKey] = planIndex;
            }
        }

        List<VolumeDescriptor> volumes = [];
        int chapterIndex = 0;
        foreach (VolumeDescriptor volume in catalog.Volumes)
        {
            List<ChapterDescriptor> chapters = [];
            foreach (ChapterDescriptor chapter in volume.Chapters)
            {
                int currentChapterIndex = chapterIndex++;
                string dedupeKey = GetChapterDedupeKey(
                    chapter.ChapterId,
                    safeTitleKeysByChapterId);
                if (finalSelectedPlans.TryGetValue(
                        dedupeKey,
                        out ChapterPlan? selectedPlan)
                    && finalSelectedPlanIndexes.TryGetValue(
                        dedupeKey,
                        out int selectedPlanIndex)
                    && currentChapterIndex == selectedPlanIndex)
                {
                    chapters.Add(selectedPlan.Chapter);
                }
            }

            if (chapters.Count > 0)
            {
                volumes.Add(volume with { Chapters = chapters.ToArray() });
            }
        }

        CatalogSnapshot deduplicatedCatalog = catalog with { Volumes = volumes };

        List<ChapterPlan> orderedPlans = [];
        HashSet<string> emittedPlanKeys = new(StringComparer.Ordinal);
        foreach (ChapterDescriptor chapter in deduplicatedCatalog.Volumes.SelectMany(
            static volume => volume.Chapters))
        {
            string dedupeKey = GetChapterDedupeKey(
                chapter.ChapterId,
                safeTitleKeysByChapterId);
            if (emittedPlanKeys.Add(dedupeKey)
                && finalSelectedPlans.TryGetValue(dedupeKey, out ChapterPlan? selectedPlan))
            {
                orderedPlans.Add(selectedPlan);
            }
        }

        return (deduplicatedCatalog, orderedPlans);
    }

    private static string GetChapterDedupeKey(
        string chapterId,
        Dictionary<string, string> safeTitleKeysByChapterId)
        => safeTitleKeysByChapterId.TryGetValue(chapterId, out string? titleKey)
            ? "title:" + titleKey
            : "id:" + chapterId;

    private static bool IsConcreteSameTitleDuplicateGroup(ChapterPlan[] group)
    {
        if (group.Length != 2
            || NormalizeChapterTitleKey(group[0].Chapter.Title).Length == 0)
        {
            return false;
        }

        string firstUrlKey = NormalizeChapterUrlKey(group[0].Chapter.Url);
        return firstUrlKey.Length > 0
            && string.Equals(
                firstUrlKey,
                NormalizeChapterUrlKey(group[1].Chapter.Url),
                StringComparison.Ordinal);
    }

    private static string NormalizeChapterTitleKey(string title)
        => title.Trim();

    private static string NormalizeChapterUrlKey(string url)
        => url.Trim();

    private static bool IsMoreConservativePlan(ChapterPlan plan, ChapterPlan existingPlan)
    {
        int planRank = GetConservativeChapterRank(plan.Chapter);
        int existingPlanRank = GetConservativeChapterRank(existingPlan.Chapter);
        if (planRank != existingPlanRank)
        {
            return planRank > existingPlanRank;
        }

        return GetConservativeStatusRank(plan.Status)
            > GetConservativeStatusRank(existingPlan.Status);
    }

    private static int GetConservativeChapterRank(ChapterDescriptor chapter)
    {
        int rank = 0;
        if (chapter.IsVip)
        {
            rank += 2;
        }

        if (chapter.CatalogAccessState != CatalogChapterAccessState.Accessible)
        {
            rank++;
        }

        return rank;
    }

    private static int GetConservativeStatusRank(ChapterPlanStatus status)
        => status switch
        {
            ChapterPlanStatus.FetchRequired => 2,
            ChapterPlanStatus.Changed => 1,
            _ => 0,
        };

    private static bool CanReuseCachedChapter(
        ChapterDescriptor chapter,
        bool isPreview,
        CatalogChapterAccessState cachedCatalogAccessState,
        string? visibleToUserName,
        VipFullContentCacheProvenance? vipFullContentProvenance,
        bool? cachedCatalogIsVip,
        bool? isAnonymousSafeFullContent,
        LoginState? validatedLoginState)
    {
        if (!chapter.IsVip)
        {
            if (isPreview)
            {
                return false;
            }

            if (!IsUserSensitiveFullContentCache(
                isPreview,
                cachedCatalogAccessState,
                visibleToUserName,
                vipFullContentProvenance,
                cachedCatalogIsVip,
                isAnonymousSafeFullContent))
            {
                return true;
            }

            return validatedLoginState is
            {
                IsValidated: true,
                UserName: { Length: > 0 } freeCatalogUserName,
            }
                && IsSameNormalizedUser(visibleToUserName, freeCatalogUserName);
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
            cachedProbe?.CatalogAccessState ?? plan.CachedEntry.CatalogAccessState,
            cachedProbe?.VisibleToUserName ?? plan.CachedEntry.VisibleToUserName,
            cachedProbe?.VipFullContentProvenance ?? plan.CachedEntry.VipFullContentProvenance,
            cachedProbe?.CatalogIsVip ?? plan.CachedEntry.CatalogIsVip,
            cachedProbe?.IsAnonymousSafeFullContent
                ?? plan.CachedEntry.IsAnonymousSafeFullContent,
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
        if (plan is not { Status: ChapterPlanStatus.Cached })
        {
            return false;
        }

        bool isPreview = plan.CachedProbe?.IsPreview
            ?? plan.CachedEntry?.IsPreview
            ?? false;
        CatalogChapterAccessState cachedCatalogAccessState =
            plan.CachedProbe?.CatalogAccessState
            ?? plan.CachedEntry?.CatalogAccessState
            ?? CatalogChapterAccessState.Unknown;
        bool? cachedCatalogIsVip =
            plan.CachedProbe?.CatalogIsVip
            ?? plan.CachedEntry?.CatalogIsVip;
        VipFullContentCacheProvenance? provenance =
            plan.CachedProbe?.VipFullContentProvenance
            ?? plan.CachedEntry?.VipFullContentProvenance;
        string? visibleToUserName =
            plan.CachedProbe?.VisibleToUserName
            ?? plan.CachedEntry?.VisibleToUserName;
        return (plan.Chapter.IsVip && isPreview)
            || IsUserSensitiveFullContentCache(
                isPreview,
                cachedCatalogAccessState,
                visibleToUserName,
                provenance,
                cachedCatalogIsVip,
                plan.CachedProbe?.IsAnonymousSafeFullContent
                    ?? plan.CachedEntry?.IsAnonymousSafeFullContent)
            || (plan.Chapter.IsVip
                && !isPreview
                && provenance != VipFullContentCacheProvenance.Public);
    }

    private static bool IsUserSensitiveFullContentCache(
        bool isPreview,
        CatalogChapterAccessState cachedCatalogAccessState,
        string? visibleToUserName,
        VipFullContentCacheProvenance? vipFullContentProvenance,
        bool? cachedCatalogIsVip,
        bool? isAnonymousSafeFullContent)
    {
        if (isPreview || vipFullContentProvenance == VipFullContentCacheProvenance.Public)
        {
            return false;
        }

        return cachedCatalogIsVip switch
        {
            // Saved after CatalogIsVip was introduced: true is VIP-origin full content,
            // false is known free-origin content only when the rest of the metadata is
            // internally consistent with anonymous-safe public catalog content.
            true => true,
            false => !IsAnonymousSafeFreeOriginFullContentCache(
                cachedCatalogAccessState,
                visibleToUserName,
                vipFullContentProvenance,
                isAnonymousSafeFullContent),
            // Legacy entries have unknown origin and must fail closed unless another
            // caller-visible trust signal (public provenance or same validated user) applies.
            null => true,
        };
    }

    private static bool IsAnonymousSafeFreeOriginFullContentCache(
        CatalogChapterAccessState cachedCatalogAccessState,
        string? visibleToUserName,
        VipFullContentCacheProvenance? vipFullContentProvenance,
        bool? isAnonymousSafeFullContent)
        => cachedCatalogAccessState == CatalogChapterAccessState.Accessible
            && LoginState.NormalizeUserName(visibleToUserName) is not { Length: > 0 }
            && vipFullContentProvenance is null
            && isAnonymousSafeFullContent == true;

    private static bool IsSameNormalizedUser(string? left, string? right)
        => LoginState.NormalizeUserName(left) is { } normalizedLeft
            && LoginState.NormalizeUserName(right) is { } normalizedRight
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal);

    private static bool CanIgnoreCatalogAccessStateMismatchForReusableVipFullCache(
        ChapterDescriptor chapter,
        ChapterCacheProbe cachedProbe,
        LoginState? validatedLoginState)
        => cachedProbe is
        {
            IsPreview: false,
        }
            && (chapter.IsVip
                || cachedProbe.CatalogAccessState == CatalogChapterAccessState.PurchaseRequired
                || cachedProbe.CatalogIsVip == true
                || cachedProbe.VipFullContentProvenance is not null
                || LoginState.NormalizeUserName(cachedProbe.VisibleToUserName) is { Length: > 0 })
            && CanReuseCachedChapter(
                chapter,
                cachedProbe.IsPreview,
                cachedProbe.CatalogAccessState,
                cachedProbe.VisibleToUserName,
                cachedProbe.VipFullContentProvenance,
                cachedProbe.CatalogIsVip,
                cachedProbe.IsAnonymousSafeFullContent,
                validatedLoginState);

    private static string? GetVisibleToUserName(
        ChapterDescriptor chapter,
        ChapterFetchResult chapterResult,
        LoginState? validatedLoginState,
        bool canAttributeFreeFullContentAsPublic)
        => !chapterResult.IsPreview
            && (chapter.IsVip || !canAttributeFreeFullContentAsPublic)
            && validatedLoginState is { IsValidated: true, UserName: { Length: > 0 } userName }
            ? userName
            : null;

    internal static bool HasAnonymousSafeFreeFullContentProof(
        ChapterDescriptor chapter,
        CatalogSnapshot catalog,
        CatalogSnapshot? rawAnonymousCatalogEvidence,
        IReadOnlyList<ChapterPlan>? rawAnonymousPlanEvidence,
        bool fetchedWithLoggedOutProof,
        bool fetchedFromAuthenticatedContext)
    {
        if (chapter.IsVip)
        {
            return false;
        }

        if (fetchedWithLoggedOutProof)
        {
            return true;
        }

        if (fetchedFromAuthenticatedContext)
        {
            return false;
        }

        if (catalog is
            {
                CacheScope.Kind: CatalogCacheScopeKind.Anonymous,
                IsKnownAnonymous: true,
            })
        {
            return true;
        }

        if (rawAnonymousCatalogEvidence is not
            {
                CacheScope.Kind: CatalogCacheScopeKind.Anonymous,
                IsKnownAnonymous: true,
            })
        {
            return false;
        }

        return rawAnonymousPlanEvidence?.Any(
            plan => string.Equals(
                    plan.Chapter.ChapterId,
                    chapter.ChapterId,
                    StringComparison.Ordinal)
                && !plan.Chapter.IsVip
                && plan.Chapter.CatalogAccessState == CatalogChapterAccessState.Accessible) == true;
    }

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
        LoginState? attributionLoginState,
        bool canAttributePublic)
    {
        if (!chapter.IsVip || chapterResult.IsPreview)
        {
            return null;
        }

        return attributionLoginState is { IsValidated: true, UserName: { Length: > 0 } }
            ? VipFullContentCacheProvenance.ValidatedUser
            : canAttributePublic && IsCompleteLoggedOutProof(attributionLoginState)
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
