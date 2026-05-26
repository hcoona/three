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
    public async Task DownloadAsyncDryRunAllowsAnonymousVipPreviewPlanningWithCurrentStateProbe()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession initialHeadlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [("c1", "VIP One", true, 100), ("c2", "VIP Two", true, 200)])),
            ]);
        FakeBrowserManager browserManager = new(initialHeadlessSession);
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
        Assert.Equal(1, initialHeadlessSession.LoginStateRequests);
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
    public async Task DownloadAsyncDryRunRefreshesStaleFreeAnonymousCatalogWithoutSessionProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                ("Stale Free Volume", false, [("stale", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        FakeBrowserSession initialHeadlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Free Volume", false, [("c1", "Chapter One", false, 100)])),
            ]);
        FakeBrowserManager browserManager = new(initialHeadlessSession);
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
        Assert.Empty(initialHeadlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
            ],
            browserManager.Events);
        Assert.Contains("- Chapter One: fetch", result.StdOut);
        Assert.DoesNotContain("Stale Chapter", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunFetchesMissingFreeAnonymousCatalogWithoutSessionProbe()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession initialHeadlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Free Volume", false, [("c1", "Chapter One", false, 100)])),
            ]);
        FakeBrowserManager browserManager = new(initialHeadlessSession);
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
        Assert.Empty(initialHeadlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
            ],
            browserManager.Events);
        Assert.Contains("- Chapter One: fetch", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncFetchesMissingFreeAnonymousCatalogWithoutSessionProbe()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession initialHeadlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Free Volume", false, [("c1", "Chapter One", false, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["chapter body"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(initialHeadlessSession);
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
        Assert.Empty(initialHeadlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotReusePreviewCacheForFreeChapter()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("c1", "Chapter One", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.Accessible),
            CancellationToken.None);
        FakeBrowserManager browserManager = new();
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
        Assert.Empty(browserManager.OpenCalls);
        Assert.Contains("- Chapter One: fetch", result.StdOut);
        Assert.DoesNotContain("- Chapter One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncFetchesFullFreeChapterOverPreviewCache()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("c1", "Chapter One", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.Accessible),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["full free content"], IsPreview: false),
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
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("full free content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        Assert.DoesNotContain(AppConstants.TruncatedChapterMarker, markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Contains("chapters downloaded=1, reused=0", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunUsesFreshAnonymousVipCatalogWithoutSessionProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            CancellationToken.None);
        FakeBrowserManager browserManager = new();
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
        Assert.Empty(browserManager.OpenCalls);
        Assert.Empty(browserManager.Events);
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotReuseFreshAnonymousCachedVipPreviewForValidatedSession()
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
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
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
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.DoesNotContain("cached preview", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("full content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunReusesFreshAnonymousCachedVipPreviewForConfirmedLoggedOutSession()
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
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
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
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotReuseFreshAnonymousCachedVipPreviewForTransientAnonymousValidatedSession()
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
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["full content"], IsPreview: false),
            ],
            emulateValidatedIdentityPolling: true);
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
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication", result.StdOut);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.DoesNotContain("cached preview", result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("full content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
    }

    [Fact]
    public async Task DownloadAsyncDryRunUsesKnownValidatedIdentityForLaterFreshAnonymousCatalog()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("c2", "VIP Two", true, 200)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "c2",
                ["preview"],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100", "200"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.Contains("- VIP Two: fetch", result.StdOut);
        Assert.DoesNotContain("- VIP Two: cached", result.StdOut);
        Assert.Contains("Summary: books completed=2", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncUsesFreshAnonymousFreeCatalogAfterKnownValidatedIdentity()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "200",
                ("Free Volume", false, [("c2", "Free Two", false, 200)])),
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
                    BookReferences = ["100", "200"],
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
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.Contains("- Free Two: fetch", result.StdOut);
        Assert.Contains("Summary: books completed=2", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunKeepsProbeFailureUncertaintyAcrossBooksForCachedVipPreview()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "c2",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ],
            loginStateException: new InvalidOperationException("probe failed"));
        FakeBrowserManager browserManager = new(headlessSession);
        TestLogger<AppCommandService> logger = new();
        AppCommandService service = CreateService(workspace, browserManager, logger: logger);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100", "200"],
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
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.Contains("- VIP Two: fetch", result.StdOut);
        Assert.DoesNotContain("- VIP Two: cached", result.StdOut);
        Assert.Contains("Summary: books completed=2", result.StdOut);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreAuthenticatedCacheReuseProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunKeepsUnvalidatedAuthenticatedUncertaintyAcrossBooksForCachedVipPreview()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "c2",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
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
                    BookReferences = ["100", "200"],
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
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.Contains("- VIP Two: fetch", result.StdOut);
        Assert.DoesNotContain("- VIP Two: cached", result.StdOut);
        Assert.Contains("Summary: books completed=2", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunKeepsDirectEnsureProbeFailureUncertaintyAcrossBooksForCachedVipPreview()
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
                VisibleToUserName: "tester"),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "c2",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        InvalidOperationException probeException = new("direct probe failed");
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ],
            loginStateExceptions: [null, probeException]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100", "200"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
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
        Assert.Contains("ERROR: Failed to process book 100: direct probe failed", result.StdErr);
        Assert.Contains("- VIP Two: fetch", result.StdOut);
        Assert.DoesNotContain("- VIP Two: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1, skipped=0, failed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncUpgradesWeakLoggedOutProbeForFreshAnonymousCachedVipPreview()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "c2",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
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
                    BookReferences = ["100", "200"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
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
        Assert.Contains("- VIP Two: cached", result.StdOut);
        Assert.Contains("Summary: books completed=2", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncUsesFreshAnonymousVipCatalogBeforePreviewFetchWithoutSessionProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
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
        Assert.Empty(headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunTriggersManualLoginWhenVipFullCacheReuseRequiresValidation()
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
        FakeBrowserSession initialHeadlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            (
                                "c1",
                                "VIP One",
                                true,
                                100,
                                CatalogChapterAccessState.Accessible),
                        ])),
            ]);
        FakeBrowserSession headedSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserSession resumedHeadlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ]);
        FakeBrowserManager browserManager = new(
            initialHeadlessSession,
            headedSession,
            resumedHeadlessSession);
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
        Assert.Equal([true, false, true], browserManager.OpenCalls);
        Assert.Equal(2, initialHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            initialHeadlessSession.LoginStateProbeModes);
        Assert.Equal(0, headedSession.LoginStateRequests);
        Assert.Equal(1, headedSession.WaitForManualLoginCalls);
        Assert.Equal(1, headedSession.PersistSessionStateCalls);
        Assert.Equal(1, resumedHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            resumedHeadlessSession.LoginStateProbeModes);
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
                "open:headed",
                "wait-for-login:headed",
                "persist-session:headed",
                "open:headless",
                "login-state:headless",
            ]);
        Assert.Contains("Authentication is required.", result.StdOut);
        Assert.Contains("Login confirmed. Continuing with the validated session.", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncCancelsManualLoginSwitchAfterBestEffortHeadlessCleanup()
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
                VisibleToUserName: "tester"),
            CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        FakeBrowserSession initialHeadlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ],
            loginStateAction: requestCount =>
            {
                if (requestCount == 2)
                {
                    cancellation.Cancel();
                }
            },
            disposeTask: new TaskCompletionSource().Task);
        FakeBrowserSession headedSession = new();
        FakeBrowserManager browserManager = new(initialHeadlessSession, headedSession);
        AppCommandService service = CreateService(workspace, browserManager);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions { BookReferences = ["100"] },
                    cancellation.Token)));

        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(0, initialHeadlessSession.DisposeCalls);
        Assert.Equal(1, initialHeadlessSession.DisposeBestEffortCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Equal(0, headedSession.WaitForManualLoginCalls);
    }

    [Fact]
    public async Task
        DownloadAsyncOpensManualLoginBrowserAfterBoundedBestEffortHeadlessCleanup()
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
                VisibleToUserName: "tester"),
            CancellationToken.None);
        FakeBrowserSession initialHeadlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ],
            disposeTask: new TaskCompletionSource().Task);
        FakeBrowserSession headedSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserSession resumedHeadlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ]);
        FakeBrowserManager browserManager = new(
            initialHeadlessSession,
            headedSession,
            resumedHeadlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                        DryRun = true,
                    },
                    CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true, false, true], browserManager.OpenCalls);
        Assert.Equal(0, initialHeadlessSession.DisposeCalls);
        Assert.Equal(1, initialHeadlessSession.DisposeBestEffortCalls);
        Assert.Equal(1, headedSession.WaitForManualLoginCalls);
        Assert.Equal(1, headedSession.PersistSessionStateCalls);
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
                "open:headed",
                "wait-for-login:headed",
                "persist-session:headed",
                "open:headless",
                "login-state:headless",
            ]);
        Assert.Contains("Login confirmed. Continuing with the validated session.", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunTriggersManualLoginForUnvalidatedVipCacheReuse(
        )
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
        FakeBrowserSession initialHeadlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ]);
        FakeBrowserSession headedSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserSession resumedHeadlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ]);
        FakeBrowserManager browserManager = new(
            initialHeadlessSession,
            headedSession,
            resumedHeadlessSession);
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
        Assert.Equal([true, false, true], browserManager.OpenCalls);
        Assert.Equal(2, initialHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            initialHeadlessSession.LoginStateProbeModes);
        Assert.Equal(0, headedSession.LoginStateRequests);
        Assert.Equal(1, headedSession.WaitForManualLoginCalls);
        Assert.Equal(1, headedSession.PersistSessionStateCalls);
        Assert.Equal(1, resumedHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            resumedHeadlessSession.LoginStateProbeModes);
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
                "open:headed",
                "wait-for-login:headed",
                "persist-session:headed",
                "open:headless",
                "login-state:headless",
            ]);
        Assert.Contains("Authentication is required.", result.StdOut);
        Assert.Contains("Login confirmed. Continuing with the validated session.", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncUsesValidatedCatalogCacheAfterManualLogin()
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
                (
                    "Stale VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
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
        FakeBrowserSession initialHeadlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ]);
        FakeBrowserSession headedSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserSession resumedHeadlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Fetched VIP Volume",
                        true,
                        [
                            (
                                "c1",
                                "VIP One",
                                true,
                                100,
                                CatalogChapterAccessState.PurchaseRequired),
                        ])),
            ]);
        FakeBrowserManager browserManager = new(
            initialHeadlessSession,
            headedSession,
            resumedHeadlessSession);
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
        Assert.Equal([true, false, true], browserManager.OpenCalls);
        Assert.Equal(2, initialHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            initialHeadlessSession.LoginStateProbeModes);
        Assert.Equal(1, headedSession.PersistSessionStateCalls);
        Assert.Equal(1, resumedHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            resumedHeadlessSession.LoginStateProbeModes);
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            [
                "open:headless",
                "login-state:headless",
                "login-state:headless",
                "open:headed",
                "wait-for-login:headed",
                "persist-session:headed",
                "open:headless",
                "login-state:headless",
                "fetch-catalog:headless:100",
            ]);
        Assert.Contains("Authentication is required.", result.StdOut);
        Assert.Contains("Login confirmed. Continuing with the validated session.", result.StdOut);
        Assert.Contains("- VIP One: changed", result.StdOut);
        Assert.DoesNotContain("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Theory]
    [InlineData(false, "tester")]
    [InlineData(true, null)]
    public async Task DownloadAsyncFailsClearlyWhenPostManualLoginSessionIsNotValidated(
        bool persistedIsLoggedIn,
        string? persistedUserName)
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                99,
                CatalogChapterAccessState.PurchaseRequired,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        FakeBrowserSession initialHeadlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ]);
        FakeBrowserSession headedSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserSession resumedHeadlessSession = new(
            loginStates:
            [
                new LoginState(persistedIsLoggedIn, persistedUserName),
            ],
            catalogFetchAction: _ =>
                throw new InvalidOperationException(
                    "Validated catalog must not be fetched without a validated session."));
        FakeBrowserManager browserManager = new(
            initialHeadlessSession,
            headedSession,
            resumedHeadlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([true, false, true], browserManager.OpenCalls);
        Assert.Equal(1, headedSession.WaitForManualLoginCalls);
        Assert.Equal(1, headedSession.PersistSessionStateCalls);
        Assert.Equal(1, resumedHeadlessSession.LoginStateRequests);
        Assert.DoesNotContain(
            "fetch-catalog:headless:100",
            browserManager.Events);
        Assert.Contains("Authentication is required.", result.StdOut);
        Assert.DoesNotContain(
            "Login confirmed. Continuing with the validated session.",
            result.StdOut);
        Assert.Contains(
            "persisted browser session could not be validated",
            result.StdErr);
        Assert.DoesNotContain(
            "Validated catalog cache scope requires a normalized user name",
            result.StdErr);
        Assert.Contains("Summary: books completed=0, skipped=0, failed=1", result.StdOut);
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
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
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
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
        Assert.DoesNotContain(
            "Login confirmed. Continuing with the validated session.",
            result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotPromptForLoginForPublicVipFullCache()
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
                VipFullContentProvenance: VipFullContentCacheProvenance.Public),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.DoesNotContain(false, browserManager.OpenCalls);
        Assert.Empty(headlessSession.LoginStateProbeModes);
        Assert.Equal(0, headedSession.WaitForManualLoginCalls);
        Assert.Contains("fetch-catalog:headless:100", browserManager.Events);
        Assert.DoesNotContain("open:headed", browserManager.Events);
        Assert.DoesNotContain("wait-for-login:headed", browserManager.Events);
        Assert.DoesNotContain("Authentication is required.", result.StdOut);
        Assert.DoesNotContain(
            "Login confirmed. Continuing with the validated session.",
            result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncReusesFreshAnonymousPublicVipFullCacheWithoutSessionProbe()
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
                CatalogChapterAccessState.PurchaseRequired,
                VipFullContentProvenance: VipFullContentCacheProvenance.Public),
            CancellationToken.None);
        FakeBrowserManager browserManager = new();
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
        Assert.Empty(browserManager.OpenCalls);
        Assert.Empty(browserManager.Events);
        Assert.Contains("- VIP One: cached", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotPromptForLoginForVipFullCacheWithoutIdentity()
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
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
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
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Equal(0, headedSession.WaitForManualLoginCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication is required.", result.StdOut);
        Assert.Contains("- VIP One: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncReusesFreshValidatedCatalogCacheForLoggedInSessionBeforeSavingChapterCache()
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
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
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
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
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
        Assert.Equal(CatalogChapterAccessState.Accessible, cacheEntry.CatalogAccessState);
    }

    [Fact]
    public async Task
        DownloadAsyncMarksVipFullContentAccessibleWhenFetchedFromValidatedCatalogPlan()
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
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
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
        Assert.Equal(CatalogChapterAccessState.Accessible, cacheEntry.CatalogAccessState);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
    }

    [Fact]
    public async Task DownloadAsyncUsesValidatedCatalogCacheWhenChapterCacheIsReusable()
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
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
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
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
        Assert.Contains("open:headless", browserManager.Events);
        Assert.Contains("login-state:headless", browserManager.Events);
        Assert.Contains("- VIP One: cached", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogWhenSameUserVipFullCacheHasEntitlementMismatch()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.PurchaseRequired),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
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
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Fresh VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            [
                "fetch-catalog:headless:100",
                "login-state:headless",
            ]);
        Assert.Contains("- VIP One: cached", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncHonorsRefreshedValidatedCatalogWhenSameUserVipFullAccessIsLost()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.PurchaseRequired),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
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
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Fresh VIP Volume",
                        true,
                        [
                            (
                                "c1",
                                "VIP One",
                                true,
                                100,
                                CatalogChapterAccessState.PurchaseRequired),
                        ])),
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
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            [
                "fetch-catalog:headless:100",
                "login-state:headless",
            ]);
        Assert.Contains("- VIP One: changed", result.StdOut);
    }

    [Theory]
    [InlineData(CatalogChapterAccessState.PurchaseRequired, "- VIP One: fetch")]
    [InlineData(CatalogChapterAccessState.Unknown, "- VIP One: changed")]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogWhenCachedAccessDeniesButReusedCatalogGrants(
            object cachedCatalogAccessState,
            string expectedChapterStatus)
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                100,
                (CatalogChapterAccessState)cachedCatalogAccessState,
                VisibleToUserName: "tester"),
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
                    (
                        "Fresh VIP Volume",
                        true,
                        [
                            (
                                "c1",
                                "VIP One",
                                true,
                                100,
                                CatalogChapterAccessState.PurchaseRequired),
                        ])),
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
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains(expectedChapterStatus, result.StdOut);
        Assert.DoesNotContain("- VIP One: cached", result.StdOut);
    }

    [Theory]
    [InlineData(CatalogChapterAccessState.PurchaseRequired)]
    [InlineData(CatalogChapterAccessState.Unknown)]
    public async Task
        DownloadAsyncReusesSameUserVipFullCacheAfterRefreshConfirmsCachedAccessWasStale(
            object cachedCatalogAccessState)
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                100,
                (CatalogChapterAccessState)cachedCatalogAccessState,
                VisibleToUserName: "tester"),
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
                    (
                        "Fresh VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- VIP One: cached", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotRefreshAgainWhenMissingValidatedCatalogFetchDeniesSameUserVipFullAccess()
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
                VisibleToUserName: "tester"),
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
                    (
                        "Fresh VIP Volume",
                        true,
                        [
                            (
                                "c1",
                                "VIP One",
                                true,
                                100,
                                CatalogChapterAccessState.PurchaseRequired),
                        ])),
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
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- VIP One: changed", result.StdOut);
        Assert.DoesNotContain("- VIP One: cached", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotRefreshAgainWhenStaleValidatedCatalogFetchDeniesSameUserVipFullAccess()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.PurchaseRequired),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
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
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Fresh VIP Volume",
                        true,
                        [
                            (
                                "c1",
                                "VIP One",
                                true,
                                100,
                                CatalogChapterAccessState.PurchaseRequired),
                        ])),
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
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- VIP One: changed", result.StdOut);
        Assert.DoesNotContain("- VIP One: cached", result.StdOut);
    }

    [Theory]
    [InlineData(false, CatalogChapterAccessState.PurchaseRequired, "- VIP One: changed")]
    [InlineData(false, CatalogChapterAccessState.Accessible, "- VIP One: cached")]
    [InlineData(true, CatalogChapterAccessState.PurchaseRequired, "- VIP One: changed")]
    [InlineData(true, CatalogChapterAccessState.Accessible, "- VIP One: cached")]
    public async Task
        DownloadAsyncDoesNotRefreshAgainWhenInitialValidatedCatalogFetchFindsUnknownCachedAccessMismatch(
            bool hasStaleValidatedCatalog,
            object refreshedCatalogAccessState,
            string expectedChapterStatus)
    {
        using TestWorkspace workspace = new();
        CatalogChapterAccessState refreshedAccessState =
            (CatalogChapterAccessState)refreshedCatalogAccessState;
        if (hasStaleValidatedCatalog)
        {
            await CacheStore.SaveCatalogAsync(
                workspace.Paths.CacheRoot,
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Stale VIP Volume",
                        true,
                        [
                            (
                                "c1",
                                "VIP One",
                                true,
                                100,
                                CatalogChapterAccessState.Accessible),
                        ]))
                with
                {
                    CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                    FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                },
                CancellationToken.None);
        }

        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Unknown,
                VisibleToUserName: "tester"),
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
                    (
                        "Fresh VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, refreshedAccessState),
                        ])),
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
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains(expectedChapterStatus, result.StdOut);
        Assert.Equal(
            refreshedAccessState == CatalogChapterAccessState.Accessible,
            result.StdOut.Contains("- VIP One: cached", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadAsyncOverwriteDoesNotForceCatalogRefresh()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible),
            CancellationToken.None);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "existing output");
        FakeBrowserManager browserManager = new();
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    Overwrite = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Empty(browserManager.OpenCalls);
        Assert.Empty(browserManager.Events);
        Assert.Contains("Summary: books completed=1", result.StdOut);
        Assert.Contains("cached content", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task DownloadAsyncRejectsExistingOutputFileReparsePointBeforeOverwritePrompt()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible),
            CancellationToken.None);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string outsidePath = Path.Combine(workspace.Root, "outside.md");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outsidePath, "outside content");
        if (!CanCreateFileSymbolicLink(workspace.Root))
        {
            return;
        }

        try
        {
            File.CreateSymbolicLink(outputPath, outsidePath);
            FakeBrowserManager browserManager = new();
            AppCommandService service = CreateService(workspace, browserManager);

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                    },
                    CancellationToken.None));

            Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
            Assert.Contains("Refusing to use reparse point", result.StdErr);
            Assert.Equal("outside content", await File.ReadAllTextAsync(outsidePath));
        }
        finally
        {
            DeleteReparseFileIfExists(outputPath);
        }
    }

    [Fact]
    public async Task DownloadAsyncRejectsOutputRootReparsePointReplacedBeforeFinalWrite()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("c1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible),
            CancellationToken.None);
        string outsideRoot = Path.Combine(workspace.Root, "outside-output");
        Directory.CreateDirectory(outsideRoot);
        if (!CanCreateDirectorySymbolicLink(workspace.Root))
        {
            return;
        }

        try
        {
            AppCommandService.BeforeOutputWriteForTests = _ =>
            {
                Directory.Delete(workspace.Paths.OutputRoot, recursive: true);
                Directory.CreateSymbolicLink(workspace.Paths.OutputRoot, outsideRoot);
            };
            FakeBrowserManager browserManager = new();
            AppCommandService service = CreateService(workspace, browserManager);

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                    },
                    CancellationToken.None));

            Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
            Assert.Contains("Refusing to create directory through reparse point", result.StdErr);
            Assert.Empty(Directory.EnumerateFiles(outsideRoot));
        }
        finally
        {
            AppCommandService.BeforeOutputWriteForTests = null;
            DeleteReparseDirectoryIfExists(workspace.Paths.OutputRoot);
        }
    }

    [Fact]
    public async Task
        DownloadAsyncReusesFreshAnonymousVipPreviewPlanAfterLoggedOutSessionProbe()
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
                ["preview content", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
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
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
            ],
            browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("preview content", markdown);
        Assert.Contains(AppConstants.TruncatedChapterMarker, markdown);
        CatalogSnapshot? anonymousCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        CatalogSnapshot? validatedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.ForValidatedUser("tester"),
            CancellationToken.None);
        Assert.NotNull(anonymousCatalog);
        Assert.Null(validatedCatalog);
        Assert.Equal(
            CatalogChapterAccessState.PurchaseRequired,
            anonymousCatalog.Volumes[0].Chapters[0].CatalogAccessState);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncFetchesCachedVipPreviewWhenLoginProbeFails()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("c1", "VIP One", true, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh visible content"], IsPreview: false),
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
            [LoginStateProbeMode.WaitForValidatedIdentity, LoginStateProbeMode.WaitForValidatedIdentity],
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
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=1, reused=0, failed=0.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("fresh visible content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreAuthenticatedCacheReuseProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncFetchesCachedVipPreviewWhenInitialLoginProbeHasUnvalidatedIdentity()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
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
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh preview content"], IsPreview: true),
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
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=1, reused=0, failed=0.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("fresh preview content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotReuseRemainingAnonymousVipPreviewPlanAfterIdentityValidation()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "VIP Volume",
                    true,
                    [("c1", "VIP One", true, 100), ("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c2",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
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
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                            ("c2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["second full content"], IsPreview: false),
                new ChapterFetchResult(["first full content"], IsPreview: false),
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
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "fetch-chapter:headless:100:c1",
                "fetch-chapter:headless:100:c2",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP Two", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=2, reused=0, failed=0.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("first full content", markdown);
        Assert.Contains("second full content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
    }

    [Fact]
    public async Task
        DownloadAsyncKeepsFreshAnonymousCatalogWithoutSessionProbeWhenSessionMayBeLoggedIn()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                ("Anonymous Volume", true, [("anon", "Anonymous VIP", true, 100)])),
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
                    (
                        "Validated Volume",
                        true,
                        [
                            (
                                "validated",
                                "Validated VIP",
                                true,
                                200,
                                CatalogChapterAccessState.Accessible),
                        ])),
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
        Assert.Empty(browserManager.OpenCalls);
        Assert.Empty(browserManager.Events);
        Assert.Contains("- Anonymous VIP: fetch", result.StdOut);
        Assert.DoesNotContain("Validated VIP", result.StdOut);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.ForValidatedUser("tester"),
                CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsyncDoesNotFetchAnonymousThenValidatedForSameBookWhenIdentityKnown()
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
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.NotNull(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.ForValidatedUser("tester"),
                CancellationToken.None));
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsyncDoesNotReuseValidatedCatalogCacheForAnonymousSession()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
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
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
        FakeBrowserSession browserSession = new([new LoginState(true, "tester")]);
        FakeBrowserSession validationSession = new([new LoginState(true, "tester")]);
        FakeBrowserManager browserManager = new(browserSession, validationSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([false, true], browserManager.OpenCalls);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal([true], browserSession.WaitForManualLoginRequireValidatedIdentity);
        Assert.Equal(1, browserSession.PersistSessionStateCalls);
        Assert.Equal(1, browserSession.DisposeCalls);
        Assert.Equal(1, validationSession.LoginStateRequests);
        Assert.Equal([LoginStateProbeMode.WaitForValidatedIdentity], validationSession.LoginStateProbeModes);
        Assert.Equal(1, validationSession.DisposeCalls);
        Assert.Equal(
            [
                "open:headed",
                "wait-for-login:headed",
                "persist-session:headed",
                "open:headless",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("Login confirmed and session state persisted.", result.StdOut);
        Assert.Contains("Summary: completed=1, reused=0, skipped=0, failed=0.", result.StdOut);
    }

    [Fact]
    public async Task LoginAsyncUsesBestEffortDisposalWhenManualLoginIsCanceled()
    {
        using TestWorkspace workspace = new();
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> disposeCanComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        OperationCanceledException expected = new(cancellation.Token);
        FakeBrowserSession browserSession = new(
            waitForManualLoginException: expected,
            disposeTask: disposeCanComplete.Task);
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);
        cancellation.Cancel();

        OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(expected, actual);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal(0, browserSession.DisposeCalls);
        Assert.Equal(1, browserSession.DisposeBestEffortCalls);
    }

    [Fact]
    public async Task LoginAsyncTreatsBrowserOriginatedOperationCanceledExceptionAsOperationalFailure()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new(
            waitForManualLoginException: new OperationCanceledException("Browser canceled login."));
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal(1, browserSession.DisposeBestEffortCalls);
        Assert.Contains("Summary: completed=0, reused=0, skipped=0, failed=1.", result.StdOut);
        Assert.Contains("ERROR: Browser canceled login.", result.StdErr);
    }

    [Fact]
    public async Task LoginAsyncIgnoresInvalidDownloadOnlySettings()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new([new LoginState(true, "tester")]);
        FakeBrowserSession validationSession = new([new LoginState(true, "tester")]);
        FakeBrowserManager browserManager = new(browserSession, validationSession);
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
        Assert.Equal([false, true], browserManager.OpenCalls);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal([true], browserSession.WaitForManualLoginRequireValidatedIdentity);
        Assert.Equal(1, browserSession.PersistSessionStateCalls);
        Assert.Equal(1, validationSession.LoginStateRequests);
        Assert.Contains("Login confirmed and session state persisted.", result.StdOut);
    }

    [Fact]
    public async Task LoginAsyncFailsWhenPersistingSessionStateFails()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new(
            [new LoginState(true, "tester")],
            disposeException: new InvalidOperationException("close failed"));
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([false], browserManager.OpenCalls);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal([true], browserSession.WaitForManualLoginRequireValidatedIdentity);
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
    public async Task LoginAsyncFailsWhenPersistedSessionCannotBeValidated()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new([new LoginState(true, "tester")]);
        FakeBrowserSession validationSession = new([new LoginState(true, null)]);
        FakeBrowserManager browserManager = new(browserSession, validationSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.LoginAsync(
                new LoginCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([false, true], browserManager.OpenCalls);
        Assert.Equal(1, browserSession.WaitForManualLoginCalls);
        Assert.Equal([true], browserSession.WaitForManualLoginRequireValidatedIdentity);
        Assert.Equal(1, browserSession.PersistSessionStateCalls);
        Assert.Equal(1, validationSession.LoginStateRequests);
        Assert.Equal(0, validationSession.DisposeCalls);
        Assert.Equal(1, validationSession.DisposeBestEffortCalls);
        Assert.DoesNotContain("Login confirmed and session state persisted.", result.StdOut);
        Assert.Contains(
            "ERROR: Login completed, but the persisted browser session could not be validated.",
            result.StdErr);
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
        Assert.Contains(
            "Summary: books completed=0, skipped=0, failed=1; "
            + "chapters downloaded=0, reused=0, failed=0.",
            result.StdOut);
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
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=1, reused=0, failed=0.",
            result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncTreatsBrowserOriginatedOperationCanceledExceptionAsBookFailure()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new(
            catalogFetchAction: _ => throw new OperationCanceledException("Browser canceled download."));
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Contains(
            "Summary: books completed=0, skipped=0, failed=1; "
            + "chapters downloaded=0, reused=0, failed=0.",
            result.StdOut);
        Assert.Contains(
            "ERROR: Failed to process book 100: Browser canceled download.",
            result.StdErr);
    }

    [Fact]
    public async Task DownloadAsyncUsesBestEffortDisposalWhenCommandCancellationOccurs()
    {
        using TestWorkspace workspace = new();
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> disposeCanComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        OperationCanceledException expected = new(cancellation.Token);
        FakeBrowserSession browserSession = new(
            catalogFetchAction: _ =>
            {
                cancellation.Cancel();
                throw expected;
            },
            disposeTask: disposeCanComplete.Task);
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);

        OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    DryRun = true,
                },
                cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(expected, actual);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(0, browserSession.DisposeCalls);
        Assert.Equal(1, browserSession.DisposeBestEffortCalls);
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
    public async Task InfoAsyncTreatsBrowserOriginatedOperationCanceledExceptionAsOperationalFailure()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession browserSession = new(
            catalogFetchAction: _ => throw new OperationCanceledException("Browser canceled info."));
        FakeBrowserManager browserManager = new(browserSession);
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
        Assert.Contains("ERROR: Browser canceled info.", result.StdErr);
    }

    [Fact]
    public async Task InfoAsyncUsesBestEffortDisposalWhenCommandCancellationOccurs()
    {
        using TestWorkspace workspace = new();
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<bool> disposeCanComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        OperationCanceledException expected = new(cancellation.Token);
        FakeBrowserSession browserSession = new(
            catalogFetchAction: _ =>
            {
                cancellation.Cancel();
                throw expected;
            },
            disposeTask: disposeCanComplete.Task);
        FakeBrowserManager browserManager = new(browserSession);
        AppCommandService service = CreateService(workspace, browserManager);

        OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(expected, actual);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(0, browserSession.DisposeCalls);
        Assert.Equal(1, browserSession.DisposeBestEffortCalls);
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
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
    public async Task
        DownloadAsyncDryRunUpgradesWeakProbeForFetchedAnonymousCachedVipPreview()
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
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication", result.StdOut);
        Assert.Contains("- VIP One: cached", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotPromptForLoginWhenVipPreviewDownloadIsSufficient()
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
        Assert.Equal(2, headlessSession.LoginStateRequests);
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
        Assert.Equal(CatalogChapterAccessState.PurchaseRequired, cacheEntry.CatalogAccessState);
        Assert.Null(cacheEntry.VisibleToUserName);
        Assert.Equal(VipFullContentCacheProvenance.Public, cacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task
        DownloadAsyncPreservesCurrentStateValidatedIdentityForVipFullContentAttribution()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("c1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
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
        Assert.Equal(CatalogChapterAccessState.Accessible, cacheEntry.CatalogAccessState);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
        Assert.Equal(
            VipFullContentCacheProvenance.ValidatedUser,
            cacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task
        DownloadAsyncKeepsAuthenticatedUnvalidatedSessionForVipFullContentAttribution()
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
        Assert.Equal(2, headlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(0, headedSession.WaitForManualLoginCalls);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication is required.", result.StdOut);
        Assert.DoesNotContain(
            "Login confirmed. Continuing with the validated session.",
            result.StdOut);
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
    public async Task DownloadAsyncDoesNotPromptForLoginWhenLoginProbeFailsAndAnonymousPlanIsUsable()
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
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreAuthenticatedCacheReuseProbeFailure)
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
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncFetchesLaterCachedVipPreviewAfterVipFullClassificationProbeFails()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "VIP Volume",
                    true,
                    [("c1", "VIP One", true, 100), ("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c2",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["first full content"], IsPreview: false),
                new ChapterFetchResult(["second full content"], IsPreview: false),
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
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
                "login-state:headless",
                "fetch-chapter:headless:100:c2",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP Two", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=2, reused=0, failed=0.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("first full content", markdown);
        Assert.Contains("second full content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Null(cacheEntry.VisibleToUserName);
        Assert.Null(cacheEntry.VipFullContentProvenance);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncRefetchesAlreadyRenderedCachedVipPreviewAfterVipFullClassificationProbeFails()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "VIP Volume",
                    true,
                    [("c1", "VIP One", true, 100), ("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["first full content"], IsPreview: false),
                new ChapterFetchResult(["second full content"], IsPreview: false),
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
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
                "login-state:headless",
                "fetch-chapter:headless:100:c2",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.DoesNotContain("cached preview", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=2, reused=0, failed=0.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        AssertContainsOrderedSubsequence(
            [.. markdown.Split(Environment.NewLine)],
            ["first full content", "second full content"]);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncRefetchesAlreadyRenderedCachedVipPreviewAfterUnvalidatedAuthenticatedProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "VIP Volume",
                    true,
                    [("c1", "VIP One", true, 100), ("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["first full content"], IsPreview: false),
                new ChapterFetchResult(["second full content"], IsPreview: false),
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
            [LoginStateProbeMode.WaitForValidatedIdentity, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
                "login-state:headless",
                "fetch-chapter:headless:100:c2",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.DoesNotContain("cached preview", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=2, reused=0, failed=0.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        AssertContainsOrderedSubsequence(
            [.. markdown.Split(Environment.NewLine)],
            ["first full content", "second full content"]);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
    }

    [Fact]
    public async Task
        DownloadAsyncFetchesLaterCachedVipPreviewAfterUnvalidatedAuthenticatedProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "VIP Volume",
                    true,
                    [("c1", "VIP One", true, 100), ("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c2",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["first full content"], IsPreview: false),
                new ChapterFetchResult(["second full content"], IsPreview: false),
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
            [LoginStateProbeMode.WaitForValidatedIdentity, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
                "login-state:headless",
                "fetch-chapter:headless:100:c2",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP Two", result.StdOut);
        Assert.DoesNotContain("cached preview", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=2, reused=0, failed=0.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("first full content", markdown);
        Assert.Contains("second full content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
    }

    [Fact]
    public async Task
        DownloadAsyncRefetchesAlreadyRenderedCachedVipPreviewAfterMidBookValidation()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "VIP Volume",
                    true,
                    [("c1", "VIP One", true, 100), ("c2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "c1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["second full content"], IsPreview: false),
                new ChapterFetchResult(["first full content"], IsPreview: false),
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
            [LoginStateProbeMode.WaitForValidatedIdentity, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:c2",
                "login-state:headless",
                "fetch-chapter:headless:100:c1",
            ],
            browserManager.Events);
        Assert.Contains("Reusing VIP One", result.StdOut);
        Assert.Contains("Refetching VIP One after login-state validation", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=2, reused=0, failed=0.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        AssertContainsOrderedSubsequence(
            [.. markdown.Split(Environment.NewLine)],
            ["first full content", "second full content"]);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "c1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
        Assert.Equal(
            VipFullContentCacheProvenance.ValidatedUser,
            cacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task CacheClearAsyncGlobalNoOpDoesNotCreateStorage()
    {
        using TestWorkspace workspace = new();
        Directory.CreateDirectory(workspace.Paths.StateRoot);
        FakeBrowserManager browserManager = new();
        FakeStorageService storageService = workspace.CreateStorageService();
        AppCommandService service = CreateService(workspace, browserManager, storageService);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.CacheClearAsync(
                new CacheClearCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(0, storageService.EnsureStorageCalls);
        Assert.True(Directory.Exists(workspace.Paths.StateRoot));
        Assert.False(Directory.Exists(workspace.Paths.CacheRoot));
        Assert.False(File.Exists(CacheStore.GetClearGenerationFilePath(workspace.Paths.CacheRoot)));
        Assert.False(File.Exists(CacheStore.GetClearGenerationFilePath(workspace.Paths.CacheRoot) + ".lock"));
        Assert.Contains("No cache data was removed.", result.StdOut);
        Assert.Contains("Summary: completed=1, reused=0, skipped=0, failed=0.", result.StdOut);
    }

    [Fact]
    public async Task CacheClearAsyncEmitsFailedSummaryWhenInputValidationFails()
    {
        using TestWorkspace workspace = new();
        AppCommandService service = CreateService(workspace, new FakeBrowserManager());

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.CacheClearAsync(
                new CacheClearCommandOptions
                {
                    BookReference = "https://book.qidian.com/info/1045928363",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.UsageFailure, result.ReturnValue);
        Assert.Contains("Summary: completed=0, reused=0, skipped=0, failed=1.", result.StdOut);
        Assert.Contains("Unsupported book reference", result.StdErr);
    }

    [Fact]
    public async Task CacheClearAsyncEmitsFailedSummaryWhenOperationFails()
    {
        using TestWorkspace workspace = new();
        AppCommandService service = CreateService(
            workspace,
            new FakeBrowserManager(),
            storageService: new ThrowingStorageService(
                new InvalidOperationException("resolve failed")));

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.CacheClearAsync(
                new CacheClearCommandOptions(),
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Contains("Summary: completed=0, reused=0, skipped=0, failed=1.", result.StdOut);
        Assert.Contains("ERROR: resolve failed", result.StdErr);
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
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "tester");
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncReusesValidatedUserVipFullCacheForSameNormalizedUser()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "tester",
            VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncDoesNotReuseValidatedUserVipFullCacheForDifferentUser()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "tester",
            VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "other"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncDoesNotReuseLegacyVipFullCacheForDifferentUser()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "c1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "tester");
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "other"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncReusesSameUserVipFullCacheWhenValidatedCatalogGrantsAccess()
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
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
        Assert.NotNull(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncDoesNotReuseVipFullCacheWhenValidatedCatalogShowsNoAccess()
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
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
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
    public async Task BuildChapterPlansAsyncReusesVipPublicFullCacheWhenCatalogAccessChanges()
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
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
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
        string cachePath = AppPaths.GetChapterCachePath(
            workspace.Paths.CacheRoot,
            "100",
            "c1");
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
        string changedCachePath = AppPaths.GetChapterCachePath(
            workspace.Paths.CacheRoot,
            "100",
            "c2");
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
                (
                    "Volume",
                    false,
                    [("c1", "Chapter 1", false, 100), ("c2", "Chapter 2", false, 201)])),
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
    public async Task BuildChapterPlansAsyncDoesNotTreatVipPreviewCacheAsFullWhenCatalogGrantsAccess()
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
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("c1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.NotEqual(ChapterPlanStatus.Cached, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
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
                (
                    "VIP Volume",
                    true,
                    [
                        (
                            "c1",
                            "VIP Chapter",
                            true,
                            100,
                            CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Changed, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Fact]
    public void SelectCachedLoginStateForProbePreservesValidatedIdentityForUnvalidatedProbe()
    {
        LoginState validatedLoginState = new(true, "tester");

        Assert.Same(
            validatedLoginState,
            AppCommandService.SelectCachedLoginStateForProbe(
                validatedLoginState,
                new LoginState(false, null)));
        Assert.Same(
            validatedLoginState,
            AppCommandService.SelectCachedLoginStateForProbe(
                validatedLoginState,
                new LoginState(true, null)));
        Assert.Equal(
            new LoginState(true, "other"),
            AppCommandService.SelectCachedLoginStateForProbe(
                validatedLoginState,
                new LoginState(true, "other")));
    }

    [Fact]
    public void
        CanReuseCachedPlanForCurrentLoginStateAllowsSameUserVipFullCacheAfterStaleProbeFailure()
    {
        ChapterDescriptor chapter = new(
            "c1",
            "VIP Chapter",
            "https://www.qidian.com/chapter/100/c1/",
            IsVip: true,
            CatalogWordCount: 100,
            CatalogChapterAccessState.Accessible);
        ChapterCacheEntry cacheEntry = new(
            "c1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "tester",
            VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser);
        ChapterPlan plan = new(chapter, ChapterPlanStatus.Cached, null, cacheEntry);

        Assert.True(
            AppCommandService.CanReuseCachedPlanForCurrentLoginState(
                plan,
                new LoginState(true, "tester"),
                loginStateProbeFailed: true));
        Assert.False(
            AppCommandService.CanReuseCachedPlanForCurrentLoginState(
                plan,
                currentLoginState: null,
                loginStateProbeFailed: true));
        Assert.False(
            AppCommandService.CanReuseCachedPlanForCurrentLoginState(
                plan,
                new LoginState(true, null),
                loginStateProbeFailed: false));
    }

    [Fact]
    public async Task CacheClearAsyncRejectsFileCacheRoot()
    {
        using TestWorkspace workspace = new();
        Directory.CreateDirectory(workspace.Root);
        await File.WriteAllTextAsync(workspace.Paths.CacheRoot, "{}");
        AppCommandService service = CreateService(workspace, new FakeBrowserManager());

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.CacheClearAsync(new CacheClearCommandOptions(), CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Contains("cache root is not a directory", result.StdErr);
        Assert.True(File.Exists(workspace.Paths.CacheRoot));
    }

    [Fact]
    public async Task CacheClearAsyncRejectsDanglingReparseCacheRoot()
    {
        using TestWorkspace workspace = new();
        string outsideRoot = Path.Combine(workspace.Root, "outside-cache");
        Directory.CreateDirectory(workspace.Root);
        Directory.CreateDirectory(outsideRoot);
        if (!CanCreateDirectorySymbolicLink(workspace.Root))
        {
            return;
        }

        try
        {
            Directory.CreateSymbolicLink(workspace.Paths.CacheRoot, outsideRoot);
            Directory.Delete(outsideRoot);
            AppCommandService service = CreateService(workspace, new FakeBrowserManager());

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.CacheClearAsync(
                    new CacheClearCommandOptions(),
                    CancellationToken.None));

            Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
            Assert.Contains("reparse point", result.StdErr);
        }
        finally
        {
            DeleteReparseDirectoryIfExists(workspace.Paths.CacheRoot);
        }
    }

    [Fact]
    public async Task CacheClearAsyncRejectsDanglingReparseAncestorWithoutCreatingGenerationStorage()
    {
        using TestWorkspace workspace = new();
        string stateRoot = Path.Combine(workspace.Root, "state");
        string cacheRoot = Path.Combine(stateRoot, AppConstants.CacheDirectoryName);
        string outsideStateRoot = Path.Combine(workspace.Root, "outside-state");
        Directory.CreateDirectory(workspace.Root);
        Directory.CreateDirectory(outsideStateRoot);
        if (!CanCreateDirectorySymbolicLink(workspace.Root))
        {
            return;
        }

        try
        {
            Directory.CreateSymbolicLink(stateRoot, outsideStateRoot);
            Directory.Delete(outsideStateRoot);
            AppStoragePaths paths = workspace.Paths with
            {
                StateRoot = stateRoot,
                CacheRoot = cacheRoot,
            };
            AppCommandService service = CreateService(
                workspace,
                new FakeBrowserManager(),
                storageService: new FakeStorageService(paths));

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.CacheClearAsync(
                    new CacheClearCommandOptions(),
                    CancellationToken.None));

            Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
            Assert.Contains("reparse point", result.StdErr);
            Assert.False(File.Exists(CacheStore.GetClearGenerationFilePath(cacheRoot)));
            Assert.False(File.Exists(CacheStore.GetClearGenerationFilePath(cacheRoot) + ".lock"));
        }
        finally
        {
            DeleteReparseDirectoryIfExists(stateRoot);
        }
    }

    private static AppCommandService CreateService(
        TestWorkspace workspace,
        FakeBrowserManager browserManager,
        IAppStorageService? storageService = null,
        ILogger<AppCommandService>? logger = null,
        AppSettings? appSettings = null)
        => new(
            Options.Create(appSettings ?? new AppSettings()),
            browserManager,
            new FakeInteractiveConsole(),
            TimeProvider.System,
            storageService ?? workspace.CreateStorageService(),
            logger ?? NullLogger<AppCommandService>.Instance);

    private static void AssertContainsOrderedSubsequence(
        List<string> actual,
        string[] expected)
    {
        int actualIndex = 0;
        for (int expectedIndex = 0; expectedIndex < expected.Length; expectedIndex++)
        {
            bool found = false;
            while (actualIndex < actual.Count)
            {
                if (string.Equals(
                    actual[actualIndex],
                    expected[expectedIndex],
                    StringComparison.Ordinal))
                {
                    actualIndex++;
                    found = true;
                    break;
                }

                actualIndex++;
            }

            Assert.True(
                found,
                $"Expected ordered event '{expected[expectedIndex]}' was not found.");
        }
    }

    private static CatalogSnapshot CreateCatalog(
        string bookId,
        params (
            string VolumeTitle,
            bool IsVip,
            (string ChapterId, string Title, bool IsVip, int? WordCount)[] Chapters)[] volumes)
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
        params (
            string VolumeTitle,
            bool IsVip,
            (
                string ChapterId,
                string Title,
                bool IsVip,
                int? WordCount,
                CatalogChapterAccessState AccessState)[] Chapters)[] volumes)
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

    private static bool CanCreateFileSymbolicLink(string root)
    {
        string target = Path.Combine(root, "symlink-target");
        string link = Path.Combine(root, "symlink-link");
        File.WriteAllText(target, string.Empty);
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
            return false;
        }
    }

    private static bool CanCreateDirectorySymbolicLink(string root)
    {
        string target = Path.Combine(root, "symlink-target-directory");
        string link = Path.Combine(root, "symlink-link-directory");
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
            return false;
        }
    }

    private static void DeleteReparseFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                File.Delete(path);
            }
        }
        catch (FileNotFoundException)
        {
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

    private sealed class ThrowingStorageService(Exception exception) : IAppStorageService
    {
        public AppStoragePaths Resolve(AppSettings settings) => throw exception;

        public AppStoragePaths EnsureStorage(AppSettings settings) => throw exception;
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
            CancellationToken cancellationToken,
            bool isolatedAnonymous = false)
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
            session.SessionKind = isolatedAnonymous
                ? "anonymous-headless"
                : headless
                    ? "headless"
                    : "headed";
            return Task.FromResult<IQidianBrowserSession>(session);
        }
    }

    private sealed class FakeBrowserSession(
        IEnumerable<LoginState>? loginStates = null,
        IEnumerable<CatalogSnapshot>? catalogs = null,
        IEnumerable<ChapterFetchResult>? chapterFetchResults = null,
        Exception? loginStateException = null,
        IEnumerable<Exception?>? loginStateExceptions = null,
        Action<int>? loginStateAction = null,
        Action<string>? catalogFetchAction = null,
        Exception? disposeException = null,
        Exception? waitForManualLoginException = null,
        Task? disposeTask = null,
        bool emulateValidatedIdentityPolling = false) : IQidianBrowserSession
    {
        private readonly Queue<LoginState> loginStates = new(loginStates ?? []);
        private readonly Queue<CatalogSnapshot> catalogs = new(catalogs ?? []);
        private readonly Queue<ChapterFetchResult> chapterFetchResults =
            new(chapterFetchResults ?? []);
        private readonly Exception? loginStateException = loginStateException;
        private readonly Queue<Exception?> loginStateExceptions = new(loginStateExceptions ?? []);
        private readonly Action<int>? loginStateAction = loginStateAction;
        private readonly Action<string>? catalogFetchAction = catalogFetchAction;
        private readonly Exception? disposeException = disposeException;
        private readonly Exception? waitForManualLoginException = waitForManualLoginException;
        private readonly Task? disposeTask = disposeTask;
        private readonly bool emulateValidatedIdentityPolling = emulateValidatedIdentityPolling;
        private LoginState? lastLoginState;
        private bool disposed;

        public FakeBrowserManager? Manager { get; set; }

        public string SessionKind { get; set; } = "unknown";

        public int LoginStateRequests { get; private set; }

        public List<LoginStateProbeMode> LoginStateProbeModes { get; } = [];

        public int WaitForManualLoginCalls { get; private set; }

        public List<bool> WaitForManualLoginRequireValidatedIdentity { get; } = [];

        public int PersistSessionStateCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public int DisposeBestEffortCalls { get; private set; }

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

        public ValueTask DisposeBestEffortAsync()
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            DisposeBestEffortCalls++;
            try
            {
                if (disposeException is not null)
                {
                    throw new OperationalException(
                        "Failed to persist browser session state.",
                        disposeException);
                }
            }
            catch
            {
                // Best-effort cleanup must not mask cancellation/failure.
            }

            return ValueTask.CompletedTask;
        }

        private async Task DisposeCoreAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DisposeCalls++;
            if (disposeTask is not null)
            {
                await disposeTask;
            }

            if (disposeException is not null)
            {
                throw new OperationalException(
                    "Failed to persist browser session state.",
                    disposeException);
            }
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
            loginStateAction?.Invoke(LoginStateRequests);
            if (loginStateExceptions.TryDequeue(out Exception? queuedLoginStateException))
            {
                if (queuedLoginStateException is not null)
                {
                    throw queuedLoginStateException;
                }
            }
            else if (loginStateException is not null)
            {
                throw loginStateException;
            }

            if (loginStates.TryDequeue(out LoginState? loginState))
            {
                if (emulateValidatedIdentityPolling
                    && probeMode == LoginStateProbeMode.WaitForValidatedIdentity)
                {
                    while (!loginState.IsValidated
                        && loginStates.TryDequeue(out LoginState? nextLoginState))
                    {
                        loginState = nextLoginState;
                    }
                }

                lastLoginState = loginState;
                return Task.FromResult(loginState);
            }

            return Task.FromResult(lastLoginState ?? new LoginState(false, null));
        }

        public Task<CatalogSnapshot> FetchCatalogAsync(
            string bookId,
            CancellationToken cancellationToken)
        {
            Manager!.Events.Add($"fetch-catalog:{SessionKind}:{bookId}");
            catalogFetchAction?.Invoke(bookId);
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
            WaitForManualLoginRequireValidatedIdentity.Add(requireValidatedIdentity);
            Manager!.Events.Add($"wait-for-login:{SessionKind}");
            if (waitForManualLoginException is not null)
            {
                throw waitForManualLoginException;
            }

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
