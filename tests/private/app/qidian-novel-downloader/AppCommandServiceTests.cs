using Hcoona.QidianNovelDownloader.Browser;
using Hcoona.QidianNovelDownloader.Commands;
using Hcoona.QidianNovelDownloader.Cache;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class AppCommandServiceTests
{
    [Fact]
    public async Task DownloadAsyncDryRunAllowsAnonymousVipPreviewPlanningWithoutLogin()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100), ("c2", "VIP Two", true, 200)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(0, headlessSession.LoginStateRequests);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication", result.StdOut);
        Assert.Contains("- VIP Two: fetch", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunTriggersManualLoginWhenVipFullCacheReuseRequiresValidation()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                "VIP One",
                ["full content"],
                IsPreview: false,
                100,
                DateTimeOffset.UtcNow,
                AppPaths.ComputeContentHash(["full content"]),
                VisibleToUserName: "tester"),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ]);
        FakeBrowserSession headedSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserManager browserManager = new(
            headlessSession,
            headedSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true, false], browserManager.OpenCalls);
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal(1, headedSession.LoginStateRequests);
        Assert.Equal(1, headedSession.WaitForManualLoginCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "open:headed",
                "wait-for-login:headed",
                "login-state:headed",
            ],
            browserManager.Events);
        Assert.Contains("Authentication is required.", result.StdOut);
        Assert.Contains("Login confirmed. Continuing with the validated session.", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task LoginAsyncPersistsSessionStateBeforeReportingSuccess()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new();
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([false], browserManager.OpenCalls);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal(1, browserSession.PersistSessionStateCalls);
        Assert.Equal(1, browserSession.DisposeCalls);
        Assert.Equal(
            [
                "open:headed",
                "wait-for-login:headed",
                "persist-session:headed",
            ],
            browserManager.Events);
        Assert.Contains("Login confirmed and session state persisted.", result.StdOut);
        Assert.Contains("Summary: completed=1, reused=0, skipped=0, failed=0.", result.StdOut);
    }

    [Fact]
    public async Task LoginAsyncFailsWhenPersistingSessionStateFails()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new(disposeException: new InvalidOperationException("close failed"));
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([false], browserManager.OpenCalls);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal(1, browserSession.PersistSessionStateCalls);
        Assert.Equal(1, browserSession.DisposeCalls);
        Assert.Equal(
            [
                "open:headed",
                "wait-for-login:headed",
                "persist-session:headed",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Login confirmed and session state persisted.", result.StdOut);
        Assert.Contains("ERROR: Failed to persist browser session state.", result.StdErr);
    }

    [Fact]
    public async Task DownloadAsyncEmitsSummaryWhenInputValidationFails()
    {
        using TestWorkspace workspace = new();
        FakeBrowserManager browserManager = new();
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.UsageFailure, result.ReturnValue);
        Assert.Contains("Summary: books completed=0, skipped=0, failed=1; chapters downloaded=0, reused=0, failed=0.", result.StdOut);
        Assert.Contains("No book targets were provided", result.StdErr);
    }

    [Fact]
    public async Task DownloadAsyncEmitsSummaryWhenTopLevelFailureOccursAfterBookProcessing()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["chapter content"], IsPreview: false),
            ],
            disposeException: new InvalidOperationException("close failed"));
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Contains("Summary: books completed=1, skipped=0, failed=0; chapters downloaded=1, reused=0, failed=0.", result.StdOut);
        Assert.Contains("ERROR: Failed to persist browser session state.", result.StdErr);
    }

    [Fact]
    public async Task LoginAsyncEmitsSummaryWhenBrowserStartupFails()
    {
        using TestWorkspace workspace = new();
        FakeBrowserManager browserManager = new(new InvalidOperationException("browser failed"));
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([false], browserManager.OpenCalls);
        Assert.Contains("Summary: completed=0, reused=0, skipped=0, failed=1.", result.StdOut);
        Assert.Contains("ERROR: browser failed", result.StdErr);
    }

    [Fact]
    public async Task InfoAsyncEmitsSummaryWhenBrowserStartupFails()
    {
        using TestWorkspace workspace = new();
        FakeBrowserManager browserManager = new(new InvalidOperationException("browser failed"));
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Contains("Summary: completed=0, reused=0, skipped=0, failed=1.", result.StdOut);
        Assert.Contains("ERROR: browser failed", result.StdErr);
    }

    [Fact]
    public async Task DownloadAsyncDryRunRefreshesCatalogAnonymouslyWithoutLoginWhenAnonymousPlanIsReusable()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                "VIP One",
                ["preview"],
                IsPreview: true,
                100,
                DateTimeOffset.UtcNow,
                AppPaths.ComputeContentHash(["preview"])),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(0, headlessSession.LoginStateRequests);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncFetchesVipPreviewWithoutManualLoginWhenAnonymousAccessSuffices()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["preview content"], IsPreview: true),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        Assert.True(File.Exists(outputPath));
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("preview content", markdown);
        Assert.Contains(AppConstants.TruncatedChapterMarker, markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.True(cacheEntry.IsPreview);
        Assert.Null(cacheEntry.VisibleToUserName);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task CacheClearAsyncGlobalNoOpDoesNotCreateStorage()
    {
        using TestWorkspace workspace = new();
        FakeBrowserManager browserManager = new();
        FakeStorageService storageService = workspace.CreateStorageService();
        AppCommandService service = CreateService(workspace, browserManager, storageService);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.CacheClearAsync(
                new CacheClearCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(0, storageService.EnsureStorageCalls);
        Assert.False(Directory.Exists(workspace.Paths.StateRoot));
        Assert.Contains("No cache data was removed.", result.StdOut);
        Assert.Contains("Summary: completed=1, reused=0, skipped=0, failed=0.", result.StdOut);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(100, null)]
    public async Task BuildChapterPlansAsyncTreatsNullWordCountMismatchAsChanged(
        int? cachedWordCount,
        int? catalogWordCount)
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            "Chapter 1",
            ["cached"],
            IsPreview: false,
            cachedWordCount,
            DateTimeOffset.UtcNow,
            AppPaths.ComputeContentHash(["cached"]));
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, catalogWordCount)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Changed, plans[0].Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(100)]
    public async Task BuildChapterPlansAsyncReusesCacheOnlyWhenWordCountsMatch(int? wordCount)
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            "Chapter 1",
            ["cached"],
            IsPreview: false,
            wordCount,
            DateTimeOffset.UtcNow,
            AppPaths.ComputeContentHash(["cached"]));
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, wordCount)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncDoesNotReuseVipFullCacheWithoutValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            "VIP Chapter",
            ["cached"],
            IsPreview: false,
            100,
            DateTimeOffset.UtcNow,
            AppPaths.ComputeContentHash(["cached"]),
            VisibleToUserName: "tester");
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP Chapter", true, 100)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncReusesVipFullCacheForMatchingValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            "VIP Chapter",
            ["cached"],
            IsPreview: false,
            100,
            DateTimeOffset.UtcNow,
            AppPaths.ComputeContentHash(["cached"]),
            VisibleToUserName: "tester");
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP Chapter", true, 100)])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncDoesNotReuseVipPreviewCacheForValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            "VIP Chapter",
            ["preview"],
            IsPreview: true,
            100,
            DateTimeOffset.UtcNow,
            AppPaths.ComputeContentHash(["preview"]));
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP Chapter", true, 100)])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
    }

    private static AppCommandService CreateService(
        TestWorkspace workspace,
        FakeBrowserManager browserManager,
        FakeStorageService? storageService = null)
        => new(
            Options.Create(new AppSettings()),
            browserManager,
            new FakeInteractiveConsole(),
            TimeProvider.System,
            storageService ?? workspace.CreateStorageService(),
            NullLogger<AppCommandService>.Instance);

    private static CatalogSnapshot CreateCatalog(
        string bookId,
        params (string VolumeTitle, bool IsVip, (string ChapterId, string Title, bool IsVip, int? WordCount)[] Chapters)[] volumes)
        => new(
            bookId,
            new BookMetadata(bookId, $"Book {bookId}", "Author", null),
            volumes.Select(volume => new VolumeDescriptor(
                volume.VolumeTitle,
                volume.IsVip,
                volume.Chapters.Select(chapter => new ChapterDescriptor(
                    chapter.ChapterId,
                    chapter.Title,
                    $"https://www.qidian.com/chapter/{bookId}/{chapter.ChapterId}/",
                    chapter.IsVip,
                    chapter.WordCount)).ToArray())).ToArray(),
            DateTimeOffset.UtcNow);

    private static readonly SemaphoreSlim ConsoleLock = new(1, 1);

    private static async Task<ConsoleCaptureResult> WithConsoleCaptureAsync(Func<Task<int>> action)
    {
        await ConsoleLock.WaitAsync();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        StringWriter output = new();
        StringWriter error = new();
        Console.SetOut(output);
        Console.SetError(error);

        try
        {
            int returnValue = await action();
            return new ConsoleCaptureResult(returnValue, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            output.Dispose();
            error.Dispose();
            ConsoleLock.Release();
        }
    }

    private sealed record ConsoleCaptureResult(int ReturnValue, string StdOut, string StdErr);

    private sealed class FakeInteractiveConsole : IInteractiveConsole
    {
        public Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken)
            => Task.FromResult(false);
    }

    private sealed class FakeStorageService(AppStoragePaths paths) : IAppStorageService
    {
        public int EnsureStorageCalls { get; private set; }

        public AppStoragePaths Resolve(AppSettings settings) => paths;

        public AppStoragePaths EnsureStorage(AppSettings settings)
        {
            EnsureStorageCalls++;
            Directory.CreateDirectory(paths.StateRoot);
            Directory.CreateDirectory(paths.CacheRoot);
            Directory.CreateDirectory(paths.LogsRoot);
            Directory.CreateDirectory(paths.OutputRoot);
            return paths;
        }
    }

    private sealed class FakeBrowserManager : IQidianBrowserManager
    {
        private readonly Queue<FakeBrowserSession> sessions;
        private readonly Exception? openException;

        public FakeBrowserManager(params FakeBrowserSession[] sessions)
        {
            this.sessions = new Queue<FakeBrowserSession>(sessions);
        }

        public FakeBrowserManager(Exception openException)
        {
            this.sessions = new Queue<FakeBrowserSession>();
            this.openException = openException;
        }

        public List<bool> OpenCalls { get; } = [];

        public List<string> Events { get; } = [];

        public Task<IQidianBrowserSession> OpenAsync(
            ResolvedAppSettings settings,
            AppStoragePaths paths,
            bool headless,
            CancellationToken cancellationToken)
        {
            OpenCalls.Add(headless);
            Events.Add(headless ? "open:headless" : "open:headed");
            if (openException is not null)
            {
                throw openException;
            }

            if (!this.sessions.TryDequeue(out FakeBrowserSession? session))
            {
                throw new InvalidOperationException("No fake browser sessions remain.");
            }

            session.Manager = this;
            session.SessionKind = headless ? "headless" : "headed";
            return Task.FromResult<IQidianBrowserSession>(session);
        }
    }

    private sealed class FakeBrowserSession(
        IEnumerable<LoginState>? loginStates = null,
        IEnumerable<CatalogSnapshot>? catalogs = null,
        IEnumerable<ChapterFetchResult>? chapterFetchResults = null,
        Exception? disposeException = null) : IQidianBrowserSession
    {
        private readonly Queue<LoginState> loginStates = new(loginStates ?? []);
        private readonly Queue<CatalogSnapshot> catalogs = new(catalogs ?? []);
        private readonly Queue<ChapterFetchResult> chapterFetchResults = new(chapterFetchResults ?? []);
        private readonly Exception? disposeException = disposeException;
        private LoginState? lastLoginState;
        private bool disposed;

        public FakeBrowserManager? Manager { get; set; }

        public string SessionKind { get; set; } = "unknown";

        public int LoginStateRequests { get; private set; }

        public int WaitForManualLoginCalls { get; private set; }

        public int PersistSessionStateCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public async Task PersistSessionStateAsync()
        {
            PersistSessionStateCalls++;
            Manager!.Events.Add($"persist-session:{SessionKind}");
            await DisposeCoreAsync();
        }

        public async ValueTask DisposeAsync()
            => await DisposeCoreAsync();

        private Task DisposeCoreAsync()
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }

            disposed = true;
            DisposeCalls++;
            if (disposeException is not null)
            {
                throw new OperationalException(
                    "Failed to persist browser session state.",
                    disposeException);
            }

            return Task.CompletedTask;
        }

        public Task<LoginState> GetLoginStateAsync(
            string? url,
            CancellationToken cancellationToken,
            bool navigate = true)
        {
            LoginStateRequests++;
            Manager!.Events.Add($"login-state:{SessionKind}");
            if (loginStates.TryDequeue(out LoginState? loginState))
            {
                lastLoginState = loginState;
                return Task.FromResult(loginState);
            }

            return Task.FromResult(lastLoginState ?? new LoginState(false, null));
        }

        public Task<CatalogSnapshot> FetchCatalogAsync(string bookId, CancellationToken cancellationToken)
        {
            Manager!.Events.Add($"fetch-catalog:{SessionKind}:{bookId}");
            if (!catalogs.TryDequeue(out CatalogSnapshot? catalog))
            {
                throw new InvalidOperationException("No fake catalog snapshots remain.");
            }

            return Task.FromResult(catalog);
        }

        public Task<ChapterFetchResult> FetchChapterAsync(
            string bookId,
            ChapterDescriptor chapter,
            CancellationToken cancellationToken)
        {
            Manager!.Events.Add($"fetch-chapter:{SessionKind}:{bookId}:{chapter.ChapterId}");
            if (!chapterFetchResults.TryDequeue(out ChapterFetchResult? result))
            {
                throw new InvalidOperationException("No fake chapter fetch results remain.");
            }

            return Task.FromResult(result);
        }

        public Task WaitForManualLoginAsync(CancellationToken cancellationToken)
        {
            WaitForManualLoginCalls++;
            Manager!.Events.Add($"wait-for-login:{SessionKind}");
            return Task.CompletedTask;
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Paths = new AppStoragePaths(
                Root,
                Path.Combine(Root, AppConstants.ConfigFileName),
                Path.Combine(Root, AppConstants.CacheDirectoryName),
                Path.Combine(Root, AppConstants.LogsDirectoryName),
                Path.Combine(Root, AppConstants.OutputDirectoryName),
                Path.Combine(Root, AppConstants.BrowserProfileDirectoryName),
                BrowserProfileDirectory: null);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public FakeStorageService CreateStorageService() => new(Paths);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
