using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Hcoona.QidianNovelDownloader.Browser;

internal interface IQidianBrowserManager
{
    Task<IQidianBrowserSession> OpenAsync(
        ResolvedAppSettings settings,
        AppStoragePaths paths,
        bool headless,
        CancellationToken cancellationToken,
        bool isolatedAnonymous = false);
}

internal interface IQidianPlaywrightFactory
{
    Task<IPlaywright> CreateAsync();
}

internal interface IQidianBrowserSession : IAsyncDisposable
{
    Task<LoginState> GetLoginStateAsync(
        string? url,
        CancellationToken cancellationToken,
        bool navigate = true,
        LoginStateProbeMode probeMode = LoginStateProbeMode.WaitForValidatedIdentity);

    Task<CatalogSnapshot> FetchCatalogAsync(string bookId, CancellationToken cancellationToken);

    Task<ChapterFetchResult> FetchChapterAsync(
        string bookId,
        ChapterDescriptor chapter,
        CancellationToken cancellationToken);

    Task<LoginState> WaitForManualLoginAsync(
        CancellationToken cancellationToken,
        bool requireValidatedIdentity = false);

    Task PersistSessionStateAsync();

    ValueTask DisposeBestEffortAsync();
}

internal enum LoginStateProbeMode
{
    CurrentStateOnly,
    WaitForValidatedIdentity,
}

internal sealed class QidianBrowserManager(
    ILogger<QidianBrowserManager> logger) : IQidianBrowserManager
{
    private static readonly string[] LaunchArgs =
    [
        "--disable-blink-features=AutomationControlled",
        "--disable-features=AutomationControlled",
    ];
    private static readonly TimeSpan StartupCleanupRetryWait = TimeSpan.FromSeconds(2);
    private readonly IQidianPlaywrightFactory playwrightFactory =
        new DefaultQidianPlaywrightFactory();

    internal QidianBrowserManager(
        ILogger<QidianBrowserManager> logger,
        IQidianPlaywrightFactory playwrightFactory)
        : this(logger)
    {
        this.playwrightFactory = playwrightFactory;
    }

    public async Task<IQidianBrowserSession> OpenAsync(
        ResolvedAppSettings settings,
        AppStoragePaths paths,
        bool headless,
        CancellationToken cancellationToken,
        bool isolatedAnonymous = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (settings.BrowserPath is { Length: > 0 } && !File.Exists(settings.BrowserPath))
        {
            throw new CliInputException(
                $"The specified browser executable does not exist: '{settings.BrowserPath}'.");
        }

        string? isolatedUserDataDir = null;
        ChromiumProfilePaths browserProfile;
        if (isolatedAnonymous)
        {
            isolatedUserDataDir = Path.Combine(
                paths.StateRoot,
                "anonymous-browser-profile-" + Guid.NewGuid().ToString("N"));
            browserProfile = new ChromiumProfilePaths(
                Path.GetFullPath(isolatedUserDataDir),
                ProfileDirectory: null,
                IsOverride: false);
        }
        else
        {
            browserProfile = ChromiumProfilePathResolver.Resolve(
                Path.Combine(paths.StateRoot, AppConstants.BrowserProfileDirectoryName),
                settings.BrowserProfileDir);
        }

        int isolatedProfileTransferred = 0;
        int isolatedProfileCleanupRequested = 0;

        Task DeleteIsolatedProfileIfOwnedAsync()
            => isolatedUserDataDir is not null
                && Volatile.Read(ref isolatedProfileTransferred) == 0
                    ? DeleteDirectoryBestEffortAsync(isolatedUserDataDir)
                    : Task.CompletedTask;

        Task DeleteIsolatedProfileIfCleanupRequestedAsync()
            => Volatile.Read(ref isolatedProfileCleanupRequested) != 0
                ? DeleteIsolatedProfileIfOwnedAsync()
                : Task.CompletedTask;

        async Task<bool> DisposePlaywrightAndCloseStartupContextThenDeleteIfRequestedAsync(
            IBrowserContext? context,
            IPlaywright? playwright)
        {
            bool cleanupSucceeded = await DisposePlaywrightAndCloseStartupContextBestEffortAsync(
                context,
                playwright).ConfigureAwait(false);
            await DeleteIsolatedProfileIfCleanupRequestedAsync().ConfigureAwait(false);
            return cleanupSucceeded;
        }

        try
        {
            AppPaths.CreateDirectoryRejectingReparseAncestors(browserProfile.UserDataDir);
            AppPaths.CreateDirectoryRejectingReparseAncestors(browserProfile.EffectiveProfilePath);

            List<Exception> failures = [];
            BrowserLaunchPlan[] launchPlans = [.. BuildLaunchPlans(settings)];
            for (int planIndex = 0; planIndex < launchPlans.Length; planIndex++)
            {
                BrowserLaunchPlan plan = launchPlans[planIndex];
                bool hasRemainingLaunchPlans = planIndex < launchPlans.Length - 1;
                IPlaywright? playwright = null;
                IBrowserContext? context = null;
                int playwrightDisposed = 0;

                void DisposeOwnedPlaywrightBestEffort()
                {
                    if (Interlocked.Exchange(ref playwrightDisposed, 1) == 0)
                    {
                        DisposePlaywrightBestEffort(playwright);
                    }
                }

                async Task DisposeOwnedPlaywrightAndDeleteIfRequestedAsync()
                {
                    DisposeOwnedPlaywrightBestEffort();
                    await DeleteIsolatedProfileIfCleanupRequestedAsync().ConfigureAwait(false);
                }

                try
                {
                    playwright = await AwaitStartupWithCancellationAsync(
                        playwrightFactory.CreateAsync(),
                        cancellationToken,
                        abandonedPlaywright =>
                        {
                            abandonedPlaywright.Dispose();
                            return Task.CompletedTask;
                        });
                    cancellationToken.ThrowIfCancellationRequested();
                    context = await AwaitStartupWithCancellationAsync(
                        playwright.Chromium.LaunchPersistentContextAsync(
                            browserProfile.UserDataDir,
                            new BrowserTypeLaunchPersistentContextOptions
                            {
                                Headless = headless,
                                Channel = plan.Channel,
                                ExecutablePath = plan.ExecutablePath,
                                Args = ChromiumProfilePathResolver.BuildLaunchArguments(
                                    LaunchArgs,
                                    browserProfile.ProfileDirectory),
                                ViewportSize = new ViewportSize
                                {
                                    Width = 1440,
                                    Height = 960,
                                },
                                Locale = "zh-CN",
                            }),
                        cancellationToken,
                        CloseStartupContextAbandonedBestEffortAsync,
                        abandonedCompletionCleanupAsync:
                            DisposeOwnedPlaywrightAndDeleteIfRequestedAsync);
                    cancellationToken.ThrowIfCancellationRequested();

                    IPage page = context.Pages.Count > 0
                        ? context.Pages[0]
                        : await AwaitStartupWithCancellationAsync(
                            context.NewPageAsync(),
                            cancellationToken,
                            CloseStartupPageAbandonedBestEffortAsync);
                    cancellationToken.ThrowIfCancellationRequested();
                    LogMessages.SelectedBrowserRuntime(logger, plan.DisplayName, null);
                    QidianBrowserSession session = new(
                        logger,
                        playwright,
                        context,
                        page,
                        plan,
                        isolatedUserDataDir is null
                            ? null
                            : () => DeleteDirectoryBestEffortAsync(isolatedUserDataDir));
                    Interlocked.Exchange(ref isolatedProfileTransferred, 1);
                    return session;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    if (context is not null)
                    {
                        _ = DisposePlaywrightAndCloseStartupContextThenDeleteIfRequestedAsync(
                            context,
                            playwright);
                    }
                    else if (playwright is not null)
                    {
                        DisposeOwnedPlaywrightBestEffort();
                    }

                    throw;
                }
                catch (Exception exception)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        if (context is not null)
                        {
                            _ = DisposePlaywrightAndCloseStartupContextThenDeleteIfRequestedAsync(
                                context,
                                playwright);
                        }
                        else if (playwright is not null)
                        {
                            DisposeOwnedPlaywrightBestEffort();
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (
                        browserProfile.IsOverride
                        && ChromiumProfilePathResolver.IsLikelyLockConflict(exception))
                    {
                        if (context is not null)
                        {
                            _ = DisposePlaywrightAndCloseStartupContextThenDeleteIfRequestedAsync(
                                context,
                                playwright);
                        }
                        else if (playwright is not null)
                        {
                            DisposePlaywrightBestEffort(playwright);
                        }

                        throw new OperationalException(
                            "The configured browser profile is currently locked by another "
                            + "Chromium browser process "
                            + $"and cannot be opened: '{browserProfile.EffectiveProfilePath}'. "
                            + "Close all Microsoft Edge/Google Chrome windows that are using this "
                            + "profile and try again, "
                            + "or remove the browserProfileDir override "
                            + "to use the downloader's dedicated profile.",
                            exception);
                    }

                    failures.Add(exception);
                    if (context is not null)
                    {
                        Task<bool> cleanupTask = DisposePlaywrightAndCloseStartupContextThenDeleteIfRequestedAsync(
                            context,
                            playwright);
                        if (hasRemainingLaunchPlans
                            && !await WaitForStartupCleanupBeforeRetryAsync(cleanupTask)
                                .ConfigureAwait(false))
                        {
                            break;
                        }
                    }
                    else if (playwright is not null)
                    {
                        DisposePlaywrightBestEffort(playwright);
                    }

                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationalException(
                "Unable to launch a supported browser. Install Microsoft Edge or Google Chrome, "
                + "or install Playwright Chromium with the generated playwright installer script.",
                failures.Count > 0 ? new AggregateException(failures) : null);
        }
        finally
        {
            Interlocked.Exchange(ref isolatedProfileCleanupRequested, 1);
            await DeleteIsolatedProfileIfOwnedAsync();
        }
    }

    private static async Task<bool> WaitForStartupCleanupBeforeRetryAsync(Task<bool> cleanupTask)
    {
        try
        {
            return await cleanupTask
                .WaitAsync(StartupCleanupRetryWait + TimeSpan.FromSeconds(1))
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<T> AwaitStartupWithCancellationAsync<T>(
        Task<T> task,
        CancellationToken cancellationToken,
        Func<T, Task>? abandonedCleanupAsync = null,
        Action? abandonedCleanupScheduled = null,
        Func<Task>? abandonedCompletionCleanupAsync = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (task.IsCompleted)
        {
            if (!task.IsCompletedSuccessfully)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await task;
        }

        TaskCompletionSource cancellationCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationCompletion);
        if (await Task.WhenAny(task, cancellationCompletion.Task) != task)
        {
            abandonedCleanupScheduled?.Invoke();
            _ = ObserveAndCleanupAbandonedStartupTaskAsync(
                task,
                abandonedCleanupAsync,
                abandonedCompletionCleanupAsync);
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (!task.IsCompletedSuccessfully)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await task;
    }

    private static async Task ObserveAndCleanupAbandonedStartupTaskAsync<T>(
        Task<T> task,
        Func<T, Task>? cleanupAsync,
        Func<Task>? completionCleanupAsync)
    {
        try
        {
            T resource = await task.ConfigureAwait(false);
            if (cleanupAsync is not null)
            {
                await cleanupAsync(resource).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            if (completionCleanupAsync is not null)
            {
                try
                {
                    await completionCleanupAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    private async Task CloseStartupContextAbandonedBestEffortAsync(IBrowserContext? context)
        => await CloseStartupContextForCleanupAsync(context).ConfigureAwait(false);

    private static void DisposePlaywrightBestEffort(IPlaywright? playwright)
    {
        if (playwright is null)
        {
            return;
        }

        try
        {
            playwright.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private static Task DeleteDirectoryBestEffortAsync(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception)
        {
        }

        return Task.CompletedTask;
    }

    private async Task<bool> DisposePlaywrightAndCloseStartupContextBestEffortAsync(
        IBrowserContext? context,
        IPlaywright? playwright)
    {
        bool closeSucceeded = await CloseStartupContextForCleanupAsync(context)
            .ConfigureAwait(false);
        DisposePlaywrightBestEffort(playwright);
        return closeSucceeded;
    }

    private async Task<bool> CloseStartupContextForCleanupAsync(IBrowserContext? context)
    {
        if (context is null)
        {
            return true;
        }

        try
        {
            await context.CloseAsync()
                .WaitAsync(StartupCleanupRetryWait)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException exception)
        {
            LogMessages.IgnoreBrowserCloseFailure(logger, exception);
            return false;
        }
        catch (Exception exception)
        {
            LogMessages.IgnoreBrowserCloseFailure(logger, exception);
            return false;
        }
    }

    private async Task CloseStartupPageAbandonedBestEffortAsync(IPage? page)
    {
        if (page is null)
        {
            return;
        }

        try
        {
            await page.CloseAsync()
                .WaitAsync(StartupCleanupRetryWait)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            LogMessages.IgnoreBrowserCloseFailure(logger, exception);
        }
        catch (Exception exception)
        {
            LogMessages.IgnoreBrowserCloseFailure(logger, exception);
        }
    }

    private static IEnumerable<BrowserLaunchPlan> BuildLaunchPlans(ResolvedAppSettings settings)
    {
        if (settings.BrowserPath is { Length: > 0 } explicitPath)
        {
            yield return new BrowserLaunchPlan(
                BrowserRuntimeKind.ExplicitExecutable,
                Channel: null,
                ExecutablePath: explicitPath,
                DisplayName: explicitPath);
            yield break;
        }

        yield return new BrowserLaunchPlan(
            BrowserRuntimeKind.MicrosoftEdge,
            Channel: "msedge",
            ExecutablePath: null,
            DisplayName: "Microsoft Edge");
        yield return new BrowserLaunchPlan(
            BrowserRuntimeKind.GoogleChrome,
            Channel: "chrome",
            ExecutablePath: null,
            DisplayName: "Google Chrome");
        yield return new BrowserLaunchPlan(
            BrowserRuntimeKind.PlaywrightChromium,
            Channel: null,
            ExecutablePath: null,
            DisplayName: "Playwright Chromium");
    }
}

internal sealed class DefaultQidianPlaywrightFactory : IQidianPlaywrightFactory
{
    public Task<IPlaywright> CreateAsync() => Playwright.CreateAsync();
}

internal sealed class QidianBrowserSession(
    ILogger logger,
    IPlaywright playwright,
    IBrowserContext context,
    IPage primaryPage,
    BrowserLaunchPlan launchPlan,
    Func<Task>? afterDisposeAsync = null) : IQidianBrowserSession
{
    internal const int LoginStateProbeAttempts = 11;
    internal const int LoginStateProbeDelayMilliseconds = 1000;
    private static readonly AsyncLocal<Action?> BeforeCompletedTaskFaultCancellationCheckHook =
        new();
    private bool disposed;

    public BrowserLaunchPlan LaunchPlan => launchPlan;

    public IPage PrimaryPage => primaryPage;

    internal static Action? BeforeCompletedTaskFaultCancellationCheckForTests
    {
        get => BeforeCompletedTaskFaultCancellationCheckHook.Value;
        set => BeforeCompletedTaskFaultCancellationCheckHook.Value = value;
    }

    public async Task<LoginState> GetLoginStateAsync(
        string? url,
        CancellationToken cancellationToken,
        bool navigate = true,
        LoginStateProbeMode probeMode = LoginStateProbeMode.WaitForValidatedIdentity)
    {
        if (navigate && url is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AwaitWithCancellationAsync(
                primaryPage.GotoAsync(
                    url,
                    new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 60_000,
                    }),
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (primaryPage.IsClosed)
        {
            return new LoginState(false, null);
        }

        LoginState latestState = await EvaluateLoginStateAsync(cancellationToken);
        if (probeMode == LoginStateProbeMode.CurrentStateOnly || latestState.IsValidated)
        {
            return latestState;
        }

        for (int attempt = 1; attempt < LoginStateProbeAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (primaryPage.IsClosed)
            {
                return latestState;
            }

            await AwaitWithCancellationAsync(
                primaryPage.WaitForTimeoutAsync(LoginStateProbeDelayMilliseconds),
                cancellationToken);
            latestState = await EvaluateLoginStateAsync(cancellationToken);
            if (latestState.IsValidated)
            {
                return latestState;
            }
        }

        return latestState;
    }

    public async Task<CatalogSnapshot> FetchCatalogAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await AwaitWithCancellationAsync(
            primaryPage.GotoAsync(
                $"{AppConstants.QidianBaseUrl}/book/{bookId}/catalog/",
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60_000,
                }),
            cancellationToken);
        await AwaitWithCancellationAsync(
            primaryPage.WaitForTimeoutAsync(2000),
            cancellationToken);

        using JsonDocument document = await EvaluateJsonDocumentAsync(
            PageScripts.CatalogJson,
            cancellationToken);
        JsonElement root = document.RootElement;

        string title = ReadString(root, "title") ?? bookId;
        string author = ReadString(root, "author") ?? "unknown";
        int? estimatedWordCount = ReadNullableInt(root, "estimatedWordCount");

        List<VolumeDescriptor> volumes = [];
        foreach (JsonElement volumeElement in root.GetProperty("volumes").EnumerateArray())
        {
            List<ChapterDescriptor> chapters = [];
            foreach (JsonElement chapterElement in volumeElement
                .GetProperty("chapters")
                .EnumerateArray())
            {
                chapters.Add(
                    new ChapterDescriptor(
                        chapterElement.GetProperty("chapterId").GetString() ?? string.Empty,
                        chapterElement.GetProperty("title").GetString() ?? string.Empty,
                        chapterElement.GetProperty("url").GetString() ?? string.Empty,
                        chapterElement.GetProperty("isVip").GetBoolean(),
                        ReadNullableInt(chapterElement, "catalogWordCount"),
                        ReadEnum(
                            chapterElement,
                            "catalogAccessState",
                            CatalogChapterAccessState.Unknown)));
            }

            volumes.Add(
                new VolumeDescriptor(
                    volumeElement.GetProperty("title").GetString() ?? string.Empty,
                    volumeElement.GetProperty("isVip").GetBoolean(),
                    chapters));
        }

        if (volumes.Count == 0)
        {
            throw new OperationalException($"No catalog volumes were found for book {bookId}.");
        }

        return new CatalogSnapshot(
            bookId,
            new BookMetadata(bookId, title, author, estimatedWordCount),
            volumes,
            DateTimeOffset.UtcNow);
    }

    public async Task<ChapterFetchResult> FetchChapterAsync(
        string bookId,
        ChapterDescriptor chapter,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string url = string.IsNullOrWhiteSpace(chapter.Url)
            ? $"{AppConstants.QidianBaseUrl}/chapter/{bookId}/{chapter.ChapterId}/"
            : chapter.Url;
        await AwaitWithCancellationAsync(
            primaryPage.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60_000,
                }),
            cancellationToken);
        try
        {
            await AwaitWithCancellationAsync(
                primaryPage.WaitForSelectorAsync(
                    "span.content-text, main p, .read-content p, "
                    + ".chapter-content p, #j_chapterContent p",
                    new PageWaitForSelectorOptions { Timeout = 10_000 }),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            // Content selectors did not appear; proceed with best-effort extraction.
        }

        using JsonDocument document = await EvaluateJsonDocumentAsync(
            PageScripts.ChapterContentJson,
            cancellationToken);
        JsonElement root = document.RootElement;
        List<string> paragraphs = [];
        foreach (JsonElement paragraph in root.GetProperty("paragraphs").EnumerateArray())
        {
            string? text = paragraph.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                paragraphs.Add(text);
            }
        }

        return new ChapterFetchResult(
            paragraphs,
            root.GetProperty("isPreview").GetBoolean());
    }

    public async Task<LoginState> WaitForManualLoginAsync(
        CancellationToken cancellationToken,
        bool requireValidatedIdentity = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await AwaitWithCancellationAsync(
            primaryPage.GotoAsync(
                AppConstants.QidianBaseUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60_000,
                }),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        while (!primaryPage.IsClosed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                LoginState state = await EvaluateLoginStateAsync(cancellationToken);
                if (requireValidatedIdentity ? state.IsValidated : state.IsLoggedIn)
                {
                    return state;
                }
            }
            catch (PlaywrightException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (primaryPage.IsClosed)
                {
                    break;
                }

                // The execution context can be destroyed when the user navigates
                // (e.g., clicking sign-in). Wait for the new page to settle before
                // retrying the login-state probe.
                try
                {
                    await AwaitWithCancellationAsync(
                        primaryPage.WaitForLoadStateAsync(
                            LoadState.DOMContentLoaded,
                            new PageWaitForLoadStateOptions { Timeout = 10_000 }),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (PlaywrightException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Ignore timeout or further navigation; the outer loop will retry.
                }
            }

            if (primaryPage.IsClosed)
            {
                break;
            }

            await AwaitWithCancellationAsync(
                primaryPage.WaitForTimeoutAsync(1000),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationalException(
            requireValidatedIdentity
                ? "The login browser window was closed before "
                    + "a validated account identity was established."
                : "The login browser window was closed before "
                    + "an authenticated session was established.");
    }

    public Task PersistSessionStateAsync() => DisposeCoreAsync(swallowBrowserCloseFailure: false);

    public async ValueTask DisposeBestEffortAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            Task closeTask = context.CloseAsync();
            Task completedTask = await Task.WhenAny(
                closeTask,
                Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
            if (completedTask == closeTask)
            {
                await closeTask.ConfigureAwait(false);
            }
            else
            {
                _ = closeTask.ContinueWith(
                    static (task, state) =>
                    {
                        _ = task.Exception;
                        LogMessages.IgnoreBrowserCloseFailure((ILogger)state!, task.Exception!);
                    },
                    logger,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (Exception exception)
        {
            LogMessages.IgnoreBrowserCloseFailure(logger, exception);
        }

        try
        {
            playwright.Dispose();
        }
        catch (Exception)
        {
        }

        if (afterDisposeAsync is not null)
        {
            try
            {
                await afterDisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
        => await DisposeCoreAsync(swallowBrowserCloseFailure: true);

    private async Task DisposeCoreAsync(bool swallowBrowserCloseFailure)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Exception? closeFailure = null;
        try
        {
            await context.CloseAsync();
        }
        catch (Exception exception)
        {
            if (swallowBrowserCloseFailure)
            {
                LogMessages.IgnoreBrowserCloseFailure(logger, exception);
            }
            else
            {
                closeFailure = exception;
            }
        }
        finally
        {
            Exception? disposeFailure = null;
            Exception? afterDisposeFailure = null;
            try
            {
                playwright.Dispose();
            }
            catch (Exception exception)
            {
                disposeFailure = exception;
            }

            if (afterDisposeAsync is not null)
            {
                try
                {
                    await afterDisposeAsync();
                }
                catch (Exception exception)
                {
                    afterDisposeFailure = exception;
                }
            }

            if (disposeFailure is not null && closeFailure is null)
            {
                ExceptionDispatchInfo.Capture(disposeFailure).Throw();
            }

            if (afterDisposeFailure is not null && closeFailure is null)
            {
                ExceptionDispatchInfo.Capture(afterDisposeFailure).Throw();
            }
        }

        if (closeFailure is not null)
        {
            throw new OperationalException(
                "Failed to persist browser session state.",
                closeFailure);
        }
    }

    private async Task<LoginState> EvaluateLoginStateAsync(CancellationToken cancellationToken)
    {
        using JsonDocument document = await EvaluateJsonDocumentAsync(
            PageScripts.LoginStateJson,
            cancellationToken);
        JsonElement root = document.RootElement;
        return new LoginState(
            root.GetProperty("isLoggedIn").GetBoolean(),
            ReadString(root, "userName")).WithNormalizedUserName();
    }

    private async Task<JsonDocument> EvaluateJsonDocumentAsync(
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string json = await AwaitWithCancellationAsync(
            primaryPage.EvaluateAsync<string>(script),
            cancellationToken);
        return JsonDocument.Parse(json);
    }

    private static async Task<T> AwaitWithCancellationAsync<T>(
        Task<T> task,
        CancellationToken cancellationToken)
    {
        await AwaitWithCancellationAsync((Task)task, cancellationToken);
        return await task;
    }

    private static async Task AwaitWithCancellationAsync(
        Task task,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (task.IsCompleted)
        {
            if (!task.IsCompletedSuccessfully)
            {
                BeforeCompletedTaskFaultCancellationCheckForTests?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
            }

            await AwaitBrowserTaskAsync(task, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        TaskCompletionSource cancellationCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationCompletion);
        if (await Task.WhenAny(task, cancellationCompletion.Task) != task)
        {
            _ = task.ContinueWith(
                static completedTask => _ = completedTask.Exception,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously);
            cancellationToken.ThrowIfCancellationRequested();
        }

        cancellationToken.ThrowIfCancellationRequested();
        await AwaitBrowserTaskAsync(task, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task AwaitBrowserTaskAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OperationalException(
                "The browser operation was canceled before completion.",
                exception);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

    private static int? ReadNullableInt(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.Number
                ? property.GetInt32()
                : null;

    private static TEnum ReadEnum<TEnum>(
        JsonElement element,
        string propertyName,
        TEnum defaultValue)
        where TEnum : struct, Enum
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return defaultValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && Enum.TryParse(property.GetString(), ignoreCase: true, out TEnum parsedFromString))
        {
            return parsedFromString;
        }

        if (property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out int parsedFromNumber)
            && Enum.IsDefined(typeof(TEnum), parsedFromNumber))
        {
            return (TEnum)Enum.ToObject(typeof(TEnum), parsedFromNumber);
        }

        return defaultValue;
    }
}
