using Hcoona.QidianNovelDownloader.Browser;
using Hcoona.QidianNovelDownloader.Commands;
using Hcoona.QidianNovelDownloader.Cache;
using Microsoft.Extensions.Logging;
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
            loginStates:
            [
                new LoginState(false, null),
            ],
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
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
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
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.PurchaseRequired,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
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
        Assert.Equal(2, headlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(0, headedSession.LoginStateRequests);
        Assert.Equal(1, headedSession.WaitForManualLoginCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
                "open:headed",
                "wait-for-login:headed",
            ],
            browserManager.Events);
        Assert.Contains("Authentication is required.", result.StdOut);
        Assert.Contains("Login confirmed. Continuing with the validated session.", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunTriggersManualLoginWhenAuthenticatedSessionIsNotValidatedForVipCacheReuse()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.PurchaseRequired,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
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
        Assert.Equal(2, headlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(0, headedSession.LoginStateRequests);
        Assert.Equal(1, headedSession.WaitForManualLoginCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
                "open:headed",
                "wait-for-login:headed",
            ],
            browserManager.Events);
        Assert.Contains("Authentication is required.", result.StdOut);
        Assert.Contains("Login confirmed. Continuing with the validated session.", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunRefetchesCatalogAfterValidatedLoginWhenCachedAccessStateChanges()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ]);
        FakeBrowserSession headedSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession, headedSession);
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
        Assert.Equal(2, headlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "login-state:headless",
                "open:headed",
                "wait-for-login:headed",
                "fetch-catalog:headed:100",
            ],
            browserManager.Events);
        Assert.Contains("Authentication is required.", result.StdOut);
        Assert.Contains("Login confirmed. Continuing with the validated session.", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunReprobesHeadlessSessionBeforeManualLoginForVipCacheReuse()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.PurchaseRequired,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
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
        Assert.Equal(2, headlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication is required.", result.StdOut);
        Assert.DoesNotContain("Login confirmed. Continuing with the validated session.", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncReusesFreshValidatedCatalogCacheForLoggedInSessionBeforeSavingChapterCache()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                ("VIP Volume", true, [("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["full content"], IsPreview: false),
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
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal(CatalogChapterAccessState.Accessible, cacheEntry.CatalogAccessState);
    }

    [Fact]
    public async Task DownloadAsyncMarksVipFullContentAccessibleWhenFetchedFromAnonymousCatalogPlan()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["full content"], IsPreview: false),
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
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal(CatalogChapterAccessState.Accessible, cacheEntry.CatalogAccessState);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                ("VIP Volume", true, [("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible)])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotRefetchCatalogForValidatedSessionWhenAnonymousPlanNeedsNoAuthenticatedRefresh()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
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
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotReuseValidatedCatalogCacheForAnonymousSession()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                ("VIP Volume", true, [("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
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
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal(CatalogChapterAccessState.PurchaseRequired, cacheEntry.CatalogAccessState);
        Assert.True(cacheEntry.IsPreview);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveAuthenticatedCatalogIntoAnonymousCache()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VipFullContentProvenance: VipFullContentCacheProvenance.Public),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible)])),
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
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
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
    public async Task LoginAsyncIgnoresInvalidDownloadOnlySettings()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new();
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                ReadingSpeed = 0,
                MinimumRequestDelaySeconds = 0,
                MaximumRequestDelaySeconds = -1,
                RetryCount = -1,
                CatalogCacheTtlHours = 0,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([false], browserManager.OpenCalls);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal(1, browserSession.PersistSessionStateCalls);
        Assert.Contains("Login confirmed and session state persisted.", result.StdOut);
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
    public async Task DownloadAsyncSucceedsWhenBrowserCloseFailsAfterBookProcessing()
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

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Contains("Summary: books completed=1, skipped=0, failed=0; chapters downloaded=1, reused=0, failed=0.", result.StdOut);
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
    public async Task InfoAsyncUsesFreshCatalogCacheWithoutStartingBrowser()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        FakeBrowserManager browserManager = new(new InvalidOperationException("browser failed"));
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Empty(browserManager.OpenCalls);
        Assert.Contains("Book ID: 100", result.StdOut);
        Assert.Contains("Cache coverage: 0/1 chapter(s)", result.StdOut);
        Assert.Contains("Summary: completed=1, reused=0, skipped=0, failed=0.", result.StdOut);
    }

    [Fact]
    public async Task InfoAsyncRefetchesCatalogWhenFreshCatalogCacheIsInvalid()
    {
        using TestWorkspace workspace = new();
        string cachePath = AppPaths.GetCatalogCachePath(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(cachePath, "{ invalid json");
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
            ],
            browserManager.Events);
        Assert.Contains("Book ID: 100", result.StdOut);
        Assert.Contains("Summary: completed=1, reused=0, skipped=0, failed=0.", result.StdOut);
    }

    [Fact]
    public async Task InfoAsyncDoesNotSaveAuthenticatedCatalogIntoAnonymousCache()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
    }

    [Fact]
    public async Task InfoAsyncIgnoresInvalidDownloadOnlySettings()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        FakeBrowserManager browserManager = new(new InvalidOperationException("browser failed"));
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                ReadingSpeed = 0,
                MinimumRequestDelaySeconds = 0,
                MaximumRequestDelaySeconds = -1,
                RetryCount = -1,
                CatalogCacheTtlHours = 24,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Empty(browserManager.OpenCalls);
        Assert.Contains("Book ID: 100", result.StdOut);
        Assert.Contains("Summary: completed=1, reused=0, skipped=0, failed=0.", result.StdOut);
    }

    [Fact]
    public async Task InfoAsyncRejectsInvalidCatalogCacheTtl()
    {
        using TestWorkspace workspace = new();
        FakeBrowserManager browserManager = new();
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                CatalogCacheTtlHours = 0,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.UsageFailure, result.ReturnValue);
        Assert.Empty(browserManager.OpenCalls);
        Assert.Contains("Catalog cache TTL must be greater than zero.", result.StdErr);
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
                ["preview"],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
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
                "login-state:headless",
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
    public async Task DownloadAsyncUsesValidatedProbeBeforeMarkingVipFullContentAsPublic()
    {
        using TestWorkspace workspace = new();
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
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["full content"], IsPreview: false),
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
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Equal(CatalogChapterAccessState.PurchaseRequired, cacheEntry.CatalogAccessState);
        Assert.Null(cacheEntry.VisibleToUserName);
        Assert.Equal(VipFullContentCacheProvenance.Public, cacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task DownloadAsyncKeepsAuthenticatedUnvalidatedSessionForVipFullContentAttribution()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["full content"], IsPreview: false),
            ]);
        FakeBrowserSession headedSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserManager browserManager = new(headlessSession, headedSession);
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
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Equal(0, headedSession.WaitForManualLoginCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication is required.", result.StdOut);
        Assert.DoesNotContain("Login confirmed. Continuing with the validated session.", result.StdOut);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Equal(CatalogChapterAccessState.PurchaseRequired, cacheEntry.CatalogAccessState);
        Assert.Null(cacheEntry.VisibleToUserName);
        Assert.Null(cacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task DownloadAsyncFallsBackToAnonymousPlanWhenAuthenticatedCacheReuseProbeFails()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.PurchaseRequired,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ],
            loginStateException: new InvalidOperationException("probe failed"));
        FakeBrowserManager browserManager = new(headlessSession);
        TestLogger<AppCommandService> logger = new();
        AppCommandService service = CreateService(workspace, browserManager, logger: logger);

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
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication is required.", result.StdOut);
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name == nameof(LogMessages.IgnoreAuthenticatedCacheReuseProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task DownloadAsyncContinuesWhenVipFullContentClassificationProbeFails()
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
                new ChapterFetchResult(["full content"], IsPreview: false),
            ],
            loginStateException: new InvalidOperationException("probe failed"));
        FakeBrowserManager browserManager = new(headlessSession);
        TestLogger<AppCommandService> logger = new();
        AppCommandService service = CreateService(workspace, browserManager, logger: logger);

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
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
                "login-state:headless",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Null(cacheEntry.VisibleToUserName);
        Assert.Null(cacheEntry.VipFullContentProvenance);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
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
            ["cached"],
            IsPreview: false,
            cachedWordCount,
            CatalogChapterAccessState.Accessible);
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
            ["cached"],
            IsPreview: false,
            wordCount,
            CatalogChapterAccessState.Accessible);
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
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.PurchaseRequired,
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
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.PurchaseRequired,
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
    public async Task BuildChapterPlansAsyncReusesVipPublicFullCacheWithoutValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.PurchaseRequired,
            VipFullContentProvenance: VipFullContentCacheProvenance.Public);
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
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncTreatsCacheEntryWithoutParagraphsAsFetchRequired()
    {
        using TestWorkspace workspace = new();
        string cachePath = AppPaths.GetChapterCachePath(workspace.Paths.CacheRoot, "100", "c1");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllTextAsync(
            cachePath,
            """
            {
              "chapterId": "c1",
              "isPreview": false,
              "catalogWordCount": 100
            }
            """);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, 100)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncUsesProbeMetadataWithoutHydratingChangedChapterBodies()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible),
            CancellationToken.None);
        string changedCachePath = AppPaths.GetChapterCachePath(workspace.Paths.CacheRoot, "100", "c2");
        Directory.CreateDirectory(Path.GetDirectoryName(changedCachePath)!);
        await File.WriteAllTextAsync(
            changedCachePath,
            """
            {
              "chapterId": "c2",
              "paragraphs": [null],
              "isPreview": false,
              "catalogWordCount": 200
            }
            """);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog(
                "100",
                ("Volume", false, [("c1", "Chapter 1", false, 100), ("c2", "Chapter 2", false, 201)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Equal(2, plans.Count);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
        Assert.NotNull(plans[0].CachedEntry);
        Assert.Equal(ChapterPlanStatus.Changed, plans[1].Status);
        Assert.Null(plans[1].CachedEntry);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncDoesNotReuseVipPreviewCacheForValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            ["preview"],
            IsPreview: true,
            100,
            CatalogChapterAccessState.PurchaseRequired);
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

    [Fact]
    public async Task BuildChapterPlansAsyncMarksChapterChangedWhenCatalogAccessStateChanges()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached"],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                ("VIP Volume", true, [("c1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Changed, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    private static AppCommandService CreateService(
        TestWorkspace workspace,
        FakeBrowserManager browserManager,
        FakeStorageService? storageService = null,
        ILogger<AppCommandService>? logger = null,
        AppSettings? appSettings = null)
        => new(
            Options.Create(appSettings ?? new AppSettings()),
            browserManager,
            new FakeInteractiveConsole(),
            TimeProvider.System,
            storageService ?? workspace.CreateStorageService(),
            logger ?? NullLogger<AppCommandService>.Instance);

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
                    chapter.WordCount,
                    chapter.IsVip
                        ? CatalogChapterAccessState.PurchaseRequired
                        : CatalogChapterAccessState.Accessible)).ToArray())).ToArray(),
            DateTimeOffset.UtcNow);

    private static CatalogSnapshot CreateCatalogWithAccessStates(
        string bookId,
        params (string VolumeTitle, bool IsVip, (string ChapterId, string Title, bool IsVip, int? WordCount, CatalogChapterAccessState AccessState)[] Chapters)[] volumes)
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
                    chapter.WordCount,
                    chapter.AccessState)).ToArray())).ToArray(),
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
        Exception? loginStateException = null,
        Exception? disposeException = null) : IQidianBrowserSession
    {
        private readonly Queue<LoginState> loginStates = new(loginStates ?? []);
        private readonly Queue<CatalogSnapshot> catalogs = new(catalogs ?? []);
        private readonly Queue<ChapterFetchResult> chapterFetchResults = new(chapterFetchResults ?? []);
        private readonly Exception? loginStateException = loginStateException;
        private readonly Exception? disposeException = disposeException;
        private LoginState? lastLoginState;
        private bool disposed;

        public FakeBrowserManager? Manager { get; set; }

        public string SessionKind { get; set; } = "unknown";

        public int LoginStateRequests { get; private set; }

        public List<LoginStateProbeMode> LoginStateProbeModes { get; } = [];

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
        {
            try
            {
                await DisposeCoreAsync();
            }
            catch (OperationalException)
            {
                // Mirror production: DisposeAsync swallows browser-close failures.
            }
        }

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
            bool navigate = true,
            LoginStateProbeMode probeMode = LoginStateProbeMode.WaitForValidatedIdentity)
        {
            LoginStateRequests++;
            LoginStateProbeModes.Add(probeMode);
            Manager!.Events.Add($"login-state:{SessionKind}");
            if (loginStateException is not null)
            {
                throw loginStateException;
            }

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

        public Task<LoginState> WaitForManualLoginAsync(
            CancellationToken cancellationToken,
            bool requireValidatedIdentity = false)
        {
            WaitForManualLoginCalls++;
            Manager!.Events.Add($"wait-for-login:{SessionKind}");
            if (loginStates.TryDequeue(out LoginState? loginState))
            {
                lastLoginState = loginState;
                return Task.FromResult(loginState);
            }

            return Task.FromResult(lastLoginState ?? new LoginState(false, null));
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    private sealed record LogEntry(
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
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
