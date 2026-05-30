using Hcoona.QidianNovelDownloader.Browser;
using Hcoona.QidianNovelDownloader.Commands;
using Hcoona.QidianNovelDownloader.Cache;
using Hcoona.QidianNovelDownloader.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
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
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [("1", "VIP One", true, 100), ("2", "VIP Two", true, 200)])),
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
    public async Task DownloadAsyncDryRunDoesNotSaveFetchedAnonymousCatalogAfterWeakLoggedOutProbe()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)]))
                with
                {
                    IsKnownAnonymous = false,
                },
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
        CatalogSnapshot? anonymousCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        string anonymousCatalogDirectory = AppPaths.GetCatalogCacheDirectory(
            workspace.Paths.CacheRoot,
            "100");
        string anonymousCatalogPath = AppPaths.GetCatalogCachePath(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous);
        Assert.Null(anonymousCatalog);
        Assert.False(File.Exists(anonymousCatalogPath));
        Assert.False(Directory.Exists(anonymousCatalogDirectory));
        Assert.Contains("- VIP One: fetch", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotSaveFetchedAnonymousCatalogAfterIncompleteCurrentStateProbe()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null, IsProbeComplete: false),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)]))
                with
                {
                    IsKnownAnonymous = false,
                },
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

        string anonymousCatalogPath = AppPaths.GetCatalogCachePath(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous);

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.False(File.Exists(anonymousCatalogPath));
        Assert.Contains("- VIP One: fetch", result.StdOut);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DownloadAsyncRejectsFetchedCatalogWhenBookIdsDoNotMatchRequestedBookId(
        bool mismatchTopLevelBookId,
        bool mismatchMetadataBookId)
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot browserCatalog = CreateCatalog(
            "100",
            ("Volume", false, [("1", "Chapter One", false, 100)]))
            with
        {
            BookId = mismatchTopLevelBookId ? "200" : "100",
            Metadata = new BookMetadata(
                    mismatchMetadataBookId ? "200" : "100",
                    "Book 100",
                    "Author",
                    null),
        };
        FakeBrowserSession headlessSession = new(
            catalogs: [browserCatalog],
            validateCatalogBookIds: false);
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Contains("did not match requested book id '100'", result.StdErr);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "100");
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "200");
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotSaveLaterAnonymousCatalogAfterWeakLoggedOutProbe()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
                CreateCatalog("200", ("Free Volume", false, [("2", "Free Two", false, 200)])),
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
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "200",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "200");
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.Contains("- Free Two: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunSavesProvenAnonymousVipAccessibleCatalogAfterValidatedLoggedOutProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.Accessible),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
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
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(savedCatalog);
        Assert.True(savedCatalog.IsKnownAnonymous);
        Assert.Equal("VIP One", savedCatalog.Volumes[0].Chapters[0].Title);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunDoesNotSaveRefetchedAnonymousVipCatalogWhenSessionLogsInDuringRefetch()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.Accessible),
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Logged In VIP Volume",
                        true,
                        [
                            (
                                "1",
                                "Logged In VIP One",
                                true,
                                100,
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
        Assert.Equal(
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "100");
    }

    [Fact]
    public async Task DownloadAsyncDryRunRefreshesStaleFreeAnonymousCatalogWithoutSessionProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                ("Stale Free Volume", false, [("1", "Stale Chapter", false, 100)]))
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
                    ("Free Volume", false, [("1", "Chapter One", false, 100)])),
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
                "fetch-catalog:anonymous-headless:100",
            ],
            browserManager.Events);
        Assert.Contains("- Chapter One: fetch", result.StdOut);
        Assert.DoesNotContain("Stale Chapter", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);

        CatalogSnapshot? refreshedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(refreshedCatalog);
        Assert.True(refreshedCatalog.IsKnownAnonymous);
        Assert.Equal("Chapter One", refreshedCatalog.Volumes[0].Chapters[0].Title);

        FakeBrowserManager reuseBrowserManager = new(new FakeBrowserSession());
        AppCommandService reuseService = CreateService(workspace, reuseBrowserManager);

        ConsoleCaptureResult reuseResult = await WithConsoleCaptureAsync(
            () => reuseService.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, reuseResult.ReturnValue);
        Assert.Empty(reuseBrowserManager.OpenCalls);
        Assert.Empty(reuseBrowserManager.Events);
        Assert.Contains("- Chapter One: fetch", reuseResult.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunSavesRegularRefreshWhenCurrentBookProvesSessionAnonymous()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "200",
                ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
                CreateCatalog(
                    "200",
                    ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)])),
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
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
                "fetch-catalog:headless:200",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("fetch-catalog:anonymous-headless:200", browserManager.Events);

        CatalogSnapshot? refreshedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "200",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(refreshedCatalog);
        Assert.True(refreshedCatalog.IsKnownAnonymous);
        Assert.Equal("Fresh Chapter", refreshedCatalog.Volumes[0].Chapters[0].Title);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotSaveRegularAnonymousCatalogWhenPostFetchLoggedOutConfirmationFails()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "200",
                ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
                CreateCatalog(
                    "200",
                    ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)])),
            ],
            loginStateExceptions:
            [
                null,
                null,
                new InvalidOperationException("post-fetch confirmation failed"),
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
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "login-state:headless",
                "fetch-catalog:headless:200",
                "login-state:headless",
            ],
            browserManager.Events);

        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "200",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(savedCatalog);
        Assert.True(savedCatalog.IsKnownAnonymous);
        Assert.Equal("Stale Chapter", savedCatalog.Volumes[0].Chapters[0].Title);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunDoesNotLaunderStaleTrustedAnonymousCatalogThroughPersistedBrowser()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                ("Stale Free Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Free Volume", false, [("1", "Chapter One", false, 100)])),
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
        Assert.Empty(headlessSession.LoginStateProbeModes);
        Assert.Contains("fetch-catalog:anonymous-headless:100", browserManager.Events);
        Assert.DoesNotContain("fetch-catalog:headless:100", browserManager.Events);

        CatalogSnapshot? refreshedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(refreshedCatalog);
        Assert.True(refreshedCatalog.IsKnownAnonymous);
        Assert.Equal("Chapter One", refreshedCatalog.Volumes[0].Chapters[0].Title);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunDoesNotPromoteIsolatedAnonymousRefreshToValidatedCatalog()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                ("Stale Anonymous Volume", true, [("1", "Stale VIP", true, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Anonymous Volume", true, [("3", "Anonymous VIP", true, 100)])),
            ]);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
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
                                "4",
                                "Validated VIP",
                                true,
                                200,
                                CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
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
        Assert.Contains("fetch-catalog:anonymous-headless:100", browserManager.Events);
        Assert.Contains("fetch-catalog:headless:100", browserManager.Events);
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
        Assert.NotNull(validatedCatalog);
        Assert.Equal("Anonymous VIP", anonymousCatalog.Volumes[0].Chapters[0].Title);
        Assert.Equal("Validated VIP", validatedCatalog.Volumes[0].Chapters[0].Title);
        Assert.Contains("- Validated VIP: fetch", result.StdOut);
        Assert.DoesNotContain("- Anonymous VIP: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunDoesNotPromoteUnprovenAnonymousFetchToValidatedCatalog()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Anonymous Volume", true, [("1", "Anonymous VIP", true, 100)])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Validated Volume",
                        true,
                        [
                            (
                                "2",
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
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "100");
        CatalogSnapshot? validatedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.ForValidatedUser("tester"),
            CancellationToken.None);
        Assert.Null(validatedCatalog);
        Assert.Contains("- Anonymous VIP: fetch", result.StdOut);
        Assert.DoesNotContain("- Validated VIP: fetch", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncRejectsIsolatedFetchedCatalogWhenBookIdsDoNotMatchRequestedBookId()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        string anonymousCatalogPath = GetAnonymousCatalogCachePath(workspace, "100");
        string staleCatalogJson = await File.ReadAllTextAsync(anonymousCatalogPath);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Volume", false, [("1", "Chapter One", false, 100)]))
                with
                {
                    Metadata = new BookMetadata("200", "Wrong Book", "Author", null),
                },
            ],
            validateCatalogBookIds: false);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession);
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
        Assert.Contains("did not match requested book id '100'", result.StdErr);
        Assert.Equal(
            ["open:headless", "fetch-catalog:anonymous-headless:100"],
            browserManager.Events);
        Assert.True(File.Exists(anonymousCatalogPath));
        Assert.Equal(staleCatalogJson, await File.ReadAllTextAsync(anonymousCatalogPath));
    }

    [Fact]
    public async Task DownloadAsyncSanitizesFetchedCatalogChapterUrlsForRequestedBookId()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalogWithUrls(
                    "100",
                    (
                        "Volume",
                        false,
                        [("1", "Chapter One", "https://www.qidian.com/chapter/200/1/", false, 100)])),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession);
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
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(savedCatalog);
        Assert.Equal(
            "https://www.qidian.com/chapter/100/1/",
            savedCatalog.Volumes[0].Chapters[0].Url);
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotSaveMissingFreeAnonymousCatalogAfterNoVipProof()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession initialHeadlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Free Volume", false, [("1", "Chapter One", false, 100)])),
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
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.Null(savedCatalog);
        Assert.False(File.Exists(GetAnonymousCatalogCachePath(workspace, "100")));
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveMissingFreeAnonymousCatalogAfterNoVipProof()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Free Volume", false, [("1", "Chapter One", false, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["chapter body"], IsPreview: false),
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
                "fetch-catalog:headless:100",
                "fetch-chapter:headless:100:1",
            ],
            browserManager.Events);
        Assert.Contains("Summary: books completed=1", result.StdOut);
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.Null(savedCatalog);
        Assert.False(File.Exists(GetAnonymousCatalogCachePath(workspace, "100")));
        Assert.Null(
            await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "1",
                CancellationToken.None));
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotSaveKnownAnonymousFreeChapterCacheWithoutLoggedOutFetchBracket()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("1", "Chapter One", false, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["entitled-looking free content"], IsPreview: false),
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
        Assert.Equal(
            [
                "open:headless",
                "fetch-chapter:headless:100:1",
            ],
            browserManager.Events);
        Assert.Null(
            await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "1",
                CancellationToken.None));
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        Assert.Contains("entitled-looking free content", await File.ReadAllTextAsync(outputPath));
    }

    [Fact]
    public async Task DownloadAsyncSavesKnownAnonymousFreeChapterCacheWithLoggedOutFetchBracket()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "200",
                (
                    "Mixed Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100),
                        ("2", "Chapter Two", false, 200),
                    ]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["anonymous vip content"], IsPreview: false),
                new ChapterFetchResult(["proven anonymous free content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["200"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [
                "open:headless",
                "fetch-chapter:headless:200:1",
                "login-state:headless",
                "fetch-chapter:headless:200:2",
                "login-state:headless",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            "2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal(["proven anonymous free content"], cacheEntry.Paragraphs);
        Assert.False(cacheEntry.CatalogIsVip);
        Assert.True(cacheEntry.IsAnonymousSafeFullContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        DownloadAsyncDoesNotKeepRenderedVipPreviewWhenFreePostFetchProofChangesOrFails(
            bool failPostFetchProof)
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "200",
                (
                    "Mixed Volume",
                    true,
                    [
                        ("1", "VIP Preview", true, 100),
                        ("2", "Free Two", false, 200),
                    ])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "1",
                ["stale cached vip preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);

        List<LoginState> loginStates = failPostFetchProof
            ? [new LoginState(false, null)]
            : [new LoginState(false, null), new LoginState(true, "tester")];
        List<Exception?> loginStateExceptions = failPostFetchProof
            ? [null, new InvalidOperationException("post-fetch proof failed")]
            : [];
        List<CatalogSnapshot> catalogs = failPostFetchProof
            ? []
            :
            [
                CreateCatalogWithAccessStates(
                    "200",
                    (
                        "Validated Volume",
                        true,
                        [
                            (
                                "1",
                                "Validated VIP Preview",
                                true,
                                100,
                                CatalogChapterAccessState.Accessible),
                            (
                                "2",
                                "Validated Free Two",
                                false,
                                200,
                                CatalogChapterAccessState.Accessible),
                        ]))
                with
                {
                    CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                },
            ];
        List<ChapterFetchResult> chapterFetchResults = failPostFetchProof
            ?
            [
                new ChapterFetchResult(["anonymous free content"], IsPreview: false),
                new ChapterFetchResult(
                    ["refetched vip preview", AppConstants.TruncatedChapterMarker],
                    IsPreview: true),
            ]
            :
            [
                new ChapterFetchResult(["anonymous free content"], IsPreview: false),
                new ChapterFetchResult(["validated vip content"], IsPreview: false),
                new ChapterFetchResult(["validated free content"], IsPreview: false),
            ];
        FakeBrowserSession headlessSession = new(
            loginStates: loginStates,
            catalogs: catalogs,
            chapterFetchResults: chapterFetchResults,
            loginStateExceptions: loginStateExceptions);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["200"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Contains("Reusing VIP Preview", result.StdOut);
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            failPostFetchProof
                ?
                [
                    "open:headless",
                    "login-state:headless",
                    "fetch-chapter:headless:200:2",
                    "login-state:headless",
                    "fetch-chapter:headless:200:1",
                ]
                :
                [
                    "open:headless",
                    "login-state:headless",
                    "fetch-chapter:headless:200:2",
                    "login-state:headless",
                    "fetch-catalog:headless:200",
                    "fetch-chapter:headless:200:1",
                    "fetch-chapter:headless:200:2",
                ]);

        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Book 200",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("stale cached vip preview", markdown);
        if (failPostFetchProof)
        {
            Assert.Contains("refetched vip preview", markdown);
            Assert.Contains("anonymous free content", markdown);
        }
        else
        {
            Assert.Contains("validated vip content", markdown);
            Assert.Contains("validated free content", markdown);
            Assert.DoesNotContain("anonymous free content", markdown);
        }
    }

    [Fact]
    public async Task DownloadAsyncSkipsStagedCatalogSaveAfterCacheClearGenerationChanges()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)])),
            ]);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        try
        {
            AppCommandService.BeforeOutputWriteForTests = _ =>
                CacheStore.Clear(workspace.Paths.CacheRoot, bookId: null, catalogOnly: false);

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                    },
                    CancellationToken.None));

            Assert.Equal(ExitCodes.Success, result.ReturnValue);
            Assert.Null(await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
        }
        finally
        {
            AppCommandService.BeforeOutputWriteForTests = null;
        }
    }

    [Fact]
    public async Task DownloadAsyncSkipsStagedCatalogSaveWhenCacheClearsDuringCatalogFetch()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)])),
            ],
            catalogFetchAction: _ =>
                CacheStore.Clear(workspace.Paths.CacheRoot, bookId: null, catalogOnly: false));
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Null(await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsyncSkipsStagedChapterSaveAfterCacheClearGenerationChanges()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("1", "Chapter One", false, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        try
        {
            AppCommandService.BeforeOutputWriteForTests = _ =>
                CacheStore.Clear(workspace.Paths.CacheRoot, bookId: null, catalogOnly: false);

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                    },
                    CancellationToken.None));

            Assert.Equal(ExitCodes.Success, result.ReturnValue);
            Assert.Null(await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "1",
                CancellationToken.None));
        }
        finally
        {
            AppCommandService.BeforeOutputWriteForTests = null;
        }
    }

    [Fact]
    public async Task DownloadAsyncSkipsStagedChapterSaveWhenCacheClearsDuringChapterFetch()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("1", "Chapter One", false, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh content"], IsPreview: false),
            ],
            chapterFetchAction: (_, _) =>
                CacheStore.Clear(workspace.Paths.CacheRoot, bookId: null, catalogOnly: false));
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
        Assert.Null(await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotReusePreviewCacheForFreeChapter()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("1", "Chapter One", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
            CreateCatalog("100", ("Free Volume", false, [("1", "Chapter One", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                "fetch-chapter:headless:100:1",
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
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.True(cacheEntry.IsPreview);
        Assert.Contains("chapters downloaded=1, reused=0", result.StdOut);
    }

    [Fact]
    public void DownloadAsyncDoesNotSaveFreeChapterCacheFromUnprovenAnonymousCatalog()
    {
        CatalogSnapshot unprovenAnonymousCatalog = CreateCatalog(
            "100",
            ("Free Volume", false, [("1", "Chapter One", false, 100)]))
            with
        {
            IsKnownAnonymous = false,
        };
        ChapterDescriptor freeChapter = unprovenAnonymousCatalog.Volumes[0].Chapters[0];
        ChapterDescriptor vipChapter = freeChapter with { IsVip = true };

        Assert.False(
            AppCommandService.CanSaveChapterCacheEntry(freeChapter, unprovenAnonymousCatalog));
        Assert.True(
            AppCommandService.CanSaveChapterCacheEntry(
                freeChapter,
                unprovenAnonymousCatalog with { IsKnownAnonymous = true }));
        Assert.True(AppCommandService.CanSaveChapterCacheEntry(vipChapter, unprovenAnonymousCatalog));
    }

    [Fact]
    public async Task
        DownloadAsyncReusesPublicVipFullCacheAfterFreshAnonymousCatalogMarksChapterFree()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("1", "Chapter One", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached public full content"],
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
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Empty(browserManager.OpenCalls);
        Assert.Empty(browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("cached public full content", markdown);
        Assert.Contains("Reusing Chapter One", result.StdOut);
        Assert.DoesNotContain("Fetching Chapter One", result.StdOut);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncReusesSameUserVipFullCacheForLaterFreshAnonymousFreeCatalog()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("Free Volume", false, [("2", "Free Two", false, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "2",
                ["cached user full content"],
                IsPreview: false,
                200,
                CatalogChapterAccessState.PurchaseRequired,
                VisibleToUserName: "tester",
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
                CreateCatalog("200", ("Free Volume", false, [("2", "Free Two", false, 200)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["fetched vip content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100", "200"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(
            [
                LoginStateProbeMode.CurrentStateOnly,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ]);
        string vipOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string freeOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Book 200",
            "Author");
        string vipMarkdown = await File.ReadAllTextAsync(vipOutputPath);
        string freeMarkdown = await File.ReadAllTextAsync(freeOutputPath);
        Assert.Contains("fetched vip content", vipMarkdown);
        Assert.Contains("cached user full content", freeMarkdown);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.Contains("Reusing Free Two", result.StdOut);
        Assert.DoesNotContain("Fetching Free Two", result.StdOut);
        Assert.Contains("Summary: books completed=2", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncSavesValidatedFreeFullFetchWithoutAnonymousEvidenceAsUserSensitive()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("99", ("VIP Volume", true, [("1", "Primer VIP", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("99", ("VIP Volume", true, [("1", "Primer VIP", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "99",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog("200", ("Free Volume", false, [("2", "Free Two", false, 200)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["fetched primer vip content"], IsPreview: false),
                new ChapterFetchResult(["validated free content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "200"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            "2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
        Assert.False(cacheEntry.CatalogIsVip);
        ChapterDescriptor freeChapter = CreateCatalog(
            "200",
            ("Free Volume", false, [("2", "Free Two", false, 200)])).Volumes[0].Chapters[0];
        ChapterPlan cachedPlan = new(freeChapter, ChapterPlanStatus.Cached, null, cacheEntry);
        Assert.False(
            AppCommandService.CanReuseCachedPlanForCurrentLoginState(
                cachedPlan,
                currentLoginState: null,
                loginStateProbeFailed: false));
        Assert.True(
            AppCommandService.CanReuseCachedPlanForCurrentLoginState(
                cachedPlan,
                new LoginState(true, "tester"),
                loginStateProbeFailed: false));
    }

    [Fact]
    public async Task
        DownloadAsyncDropsValidatedFreeFullFetchWithoutAnonymousEvidenceWhenPostFetchUserChanges()
    {
        using TestWorkspace workspace = new();
        await SaveValidatedIdentityPrimerAsync(workspace);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "other"),
            ],
            catalogs:
            [
                CreateCatalog("200", ("Free Volume", false, [("2", "Free Two", false, 200)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["fetched primer vip content"], IsPreview: false),
                new ChapterFetchResult(["validated free content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "200"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            "2",
            CancellationToken.None);
        Assert.Null(cacheEntry);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Book 200",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        Assert.DoesNotContain("validated free content", markdown);
    }

    [Fact]
    public async Task
        HasAnonymousSafeFreeFullContentProofRejectsAuthenticatedFetchDespiteRawAnonymousFreeEvidence()
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot anonymousCatalog = CreateCatalog(
            "100",
            (
                "Mixed Volume",
                true,
                [
                    ("1", "Anonymous Free", false, 100),
                    ("2", "Anonymous VIP", true, 200),
                ]));
        List<ChapterPlan> anonymousPlans = await AppCommandService.BuildChapterPlansAsync(
            anonymousCatalog,
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);
        CatalogSnapshot validatedCatalog = CreateCatalogWithAccessStates(
            "100",
            (
                "Validated Volume",
                false,
                [
                    (
                        "1",
                        "Validated Free",
                        false,
                        100,
                        CatalogChapterAccessState.Accessible),
                ]))
            with
        {
            CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
        };
        ChapterDescriptor chapter = validatedCatalog.Volumes[0].Chapters[0];

        Assert.True(AppCommandService.HasAnonymousSafeFreeFullContentProof(
            chapter,
            validatedCatalog,
            anonymousCatalog,
            anonymousPlans,
            fetchedWithLoggedOutProof: false,
            fetchedFromAuthenticatedContext: false));
        Assert.False(AppCommandService.HasAnonymousSafeFreeFullContentProof(
            chapter,
            validatedCatalog,
            anonymousCatalog,
            anonymousPlans,
            fetchedWithLoggedOutProof: false,
            fetchedFromAuthenticatedContext: true));
    }

    [Fact]
    public async Task DownloadAsyncDryRunUsesFreshAnonymousVipCatalogWithoutSessionProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
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
        Assert.True(headlessSession.LoginStateProbeModes.Count >= 3);
        Assert.All(
            headlessSession.LoginStateProbeModes,
            mode => Assert.Equal(LoginStateProbeMode.WaitForValidatedIdentity, mode));
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
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
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
    }

    [Fact]
    public async Task DownloadAsyncReplansFreshKnownAnonymousCatalogAfterLateValidatedIdentity()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous Volume", true, [("1", "Anonymous VIP", true, 100)])),
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
                            ("1", "Validated VIP", true, 100, CatalogChapterAccessState.Accessible),
                        ]))
                with
                {
                    CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                    Metadata = new BookMetadata("100", "Validated Book", "Author", null),
                },
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["anonymous full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
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
        Assert.Equal(
            [
                "open:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        string anonymousOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        Assert.False(File.Exists(anonymousOutputPath));
        string markdown = await File.ReadAllTextAsync(validatedOutputPath);
        Assert.Contains("validated full content", markdown);
        Assert.DoesNotContain("anonymous full content", markdown);
        Assert.Contains("Fetching Validated VIP", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncReplansUnprovenAnonymousCatalogAfterLateValidatedIdentity()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog("100", ("Anonymous Volume", true, [("1", "Anonymous VIP", true, 100)])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Validated Volume",
                        true,
                        [
                            ("1", "Validated VIP", true, 100, CatalogChapterAccessState.Accessible),
                        ]))
                with
                {
                    CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                    Metadata = new BookMetadata("100", "Validated Book", "Author", null),
                },
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["anonymous full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
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
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        string anonymousOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        Assert.False(File.Exists(anonymousOutputPath));
        string markdown = await File.ReadAllTextAsync(validatedOutputPath);
        Assert.Contains("validated full content", markdown);
        Assert.DoesNotContain("anonymous full content", markdown);
    }

    [Fact]
    public async Task DownloadAsyncReplansAnonymousCatalogAfterPostFetchProofFindsValidatedIdentity()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "Anonymous Volume",
                        true,
                        [
                            ("1", "Anonymous One", true, 100),
                            ("2", "Anonymous Two", true, 100),
                        ])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Validated Volume",
                        true,
                        [
                            (
                                "1",
                                "Validated One",
                                true,
                                100,
                                CatalogChapterAccessState.Accessible),
                            (
                                "2",
                                "Validated Two",
                                true,
                                100,
                                CatalogChapterAccessState.Accessible),
                        ]))
                with
                {
                    CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                    Metadata = new BookMetadata("100", "Validated Book", "Author", null),
                },
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["anonymous one"], IsPreview: false),
                new ChapterFetchResult(["anonymous two"], IsPreview: false),
                new ChapterFetchResult(["validated one"], IsPreview: false),
                new ChapterFetchResult(["validated two"], IsPreview: false),
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
        AssertContainsOrderedSubsequence(
            browserManager.Events,
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "fetch-chapter:headless:100:1",
                "fetch-chapter:headless:100:2",
            ]);
        string anonymousOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        Assert.False(File.Exists(anonymousOutputPath));
        string markdown = await File.ReadAllTextAsync(validatedOutputPath);
        Assert.Contains("validated one", markdown);
        Assert.Contains("validated two", markdown);
        Assert.DoesNotContain("anonymous one", markdown);
        Assert.DoesNotContain("anonymous two", markdown);
    }

    [Fact]
    public async Task
        DownloadAsyncDryRunReusesFreshAnonymousCachedVipPreviewForConfirmedLoggedOutSession()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("2", "VIP Two", true, 200)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "2",
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
    public async Task
        DownloadAsyncDryRunDoesNotReuseValidatedCachesAfterOnlyCurrentStateUserName()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous Volume", true, [("1", "Anonymous VIP", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Volume",
                    true,
                    [
                        ("1", "Validated VIP", true, 100, CatalogChapterAccessState.Accessible),
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
                "1",
                ["validated cached full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester",
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser,
                CatalogIsVip: true),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Fetched Validated Volume",
                        true,
                        [
                            (
                                "1",
                                "Fetched Validated VIP",
                                true,
                                100,
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal(
            [
                LoginStateProbeMode.CurrentStateOnly,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Contains("fetch-catalog:headless:100", browserManager.Events);
        Assert.DoesNotContain("- Validated VIP: cached", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotUseEarlierValidatedCachesAfterLaterLogoutProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous One", true, [("1", "Anonymous One", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Validated One", true, [("1", "Validated One", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "200",
                (
                    "Anonymous Two",
                    true,
                    [
                        ("2", "Anonymous Two", false, 200),
                        ("3", "Anonymous VIP Two", true, 200),
                    ]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "200",
                (
                    "Validated Two",
                    true,
                    [
                        ("2", "Validated Two", false, 200, CatalogChapterAccessState.Accessible),
                        (
                            "3",
                            "Validated VIP Two",
                            true,
                            200,
                            CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "2",
                ["cached user full content"],
                IsPreview: false,
                200,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester",
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser,
                CatalogIsVip: true),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "3",
                ["preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(false, null),
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
        Assert.Equal(2, headlessSession.LoginStateRequests);
        Assert.Equal(
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Contains("- Validated One: fetch", result.StdOut);
        Assert.DoesNotContain("Validated Two", result.StdOut);
        Assert.Contains("Summary: books completed=2", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDeduplicatesAnonymousOnlyCatalogPlansAndRendering()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "Volume",
                        true,
                        [
                            ("1", "Free One", false, 100),
                            ("1", "VIP One", true, 100),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["deduped content"], IsPreview: false),
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
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-chapter:headless:100:1"));
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.DoesNotContain("Fetching Free One", result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("deduped content", markdown);
        Assert.Equal(1, CountOccurrences(markdown, "VIP One"));
        Assert.DoesNotContain("Free One", markdown);
    }

    [Fact]
    public async Task DownloadAsyncDeduplicatesCatalogUsingSelectedRepresentativeVolume()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "Free Volume",
                        false,
                        [("1", "Free One", false, 100)]),
                    (
                        "VIP Volume",
                        true,
                        [("1", "VIP One", true, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["deduped content"], IsPreview: false),
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
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.DoesNotContain("Fetching Free One", result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("# VIP Volume", markdown);
        Assert.DoesNotContain("# Free Volume", markdown);
        Assert.Contains("deduped content", markdown);
        Assert.Equal(1, CountOccurrences(markdown, "VIP One"));
        Assert.DoesNotContain("Free One", markdown);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotDeduplicateSameTitleDifferentChapterIdsWithoutStrongEvidence()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "Volume",
                        true,
                        [
                            ("1", "Shared Title", false, 100),
                            ("2", "Shared Title", true, 100),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["free content"], IsPreview: false),
                new ChapterFetchResult(["vip content"], IsPreview: false),
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
        Assert.Contains("fetch-chapter:headless:100:1", browserManager.Events);
        Assert.Contains("fetch-chapter:headless:100:2", browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Equal(2, CountOccurrences(markdown, "Shared Title"));
        Assert.Contains("free content", markdown);
        Assert.Contains("vip content", markdown);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadAsyncDoesNotDeduplicateSameTitleDifferentChapterIdsWithBlankUrls(
        string blankUrl)
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalogWithUrls(
                    "100",
                    (
                        "Volume",
                        true,
                        [
                            ("1", "Shared Title", blankUrl, false, 100),
                            ("2", "Shared Title", blankUrl, true, 100),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["free content"], IsPreview: false),
                new ChapterFetchResult(["vip content"], IsPreview: false),
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
        Assert.Contains("fetch-chapter:headless:100:1", browserManager.Events);
        Assert.Contains("fetch-chapter:headless:100:2", browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Equal(2, CountOccurrences(markdown, "Shared Title"));
        Assert.Contains("free content", markdown);
        Assert.Contains("vip content", markdown);
    }

    [Theory]
    [InlineData("", "https://www.qidian.com/chapter/100/9/")]
    [InlineData("   ", "https://www.qidian.com/chapter/100/9/")]
    [InlineData("https://www.qidian.com/chapter/100/9/", "")]
    [InlineData("https://www.qidian.com/chapter/100/9/", "   ")]
    [InlineData("", "   ")]
    public async Task
        DownloadAsyncDoesNotDeduplicateSameTitleDifferentChapterIdsWithMixedBlankUrls(
            string firstUrl,
            string secondUrl)
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalogWithUrls(
                    "100",
                    (
                        "Volume",
                        true,
                        [
                            ("1", "Shared Title", firstUrl, false, 100),
                            ("2", "Shared Title", secondUrl, true, 100),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["free content"], IsPreview: false),
                new ChapterFetchResult(["vip content"], IsPreview: false),
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
        Assert.Contains("fetch-chapter:headless:100:1", browserManager.Events);
        Assert.Contains("fetch-chapter:headless:100:2", browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Equal(2, CountOccurrences(markdown, "Shared Title"));
        Assert.Contains("free content", markdown);
        Assert.Contains("vip content", markdown);
    }

    [Theory]
    [InlineData(
        "https://www.qidian.com/chapter/100/9/",
        "https://www.qidian.com/chapter/100/9/",
        "Shared Title",
        "Shared Title")]
    [InlineData(
        "  https://www.qidian.com/chapter/100/9/ ",
        "\t https://www.qidian.com/chapter/100/9/ \r\n",
        " Shared Title ",
        "\tShared Title\r\n")]
    public async Task DownloadAsyncDoesNotDeduplicateSameTitleDifferentChapterIdsWithWrongChapterUrl(
        string firstUrl,
        string secondUrl,
        string firstTitle,
        string secondTitle)
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalogWithUrls(
                    "100",
                    (
                        "Volume",
                        true,
                        [
                            (
                                "1",
                                firstTitle,
                                firstUrl,
                                false,
                                100),
                            (
                                "2",
                                secondTitle,
                                secondUrl,
                                true,
                                100),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["free content"], IsPreview: false),
                new ChapterFetchResult(["vip content"], IsPreview: false),
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
        Assert.Contains("fetch-chapter:headless:100:1", browserManager.Events);
        Assert.Contains("fetch-chapter:headless:100:2", browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Equal(2, CountOccurrences(markdown, "Shared Title"));
        Assert.Contains("free content", markdown);
        Assert.Contains("vip content", markdown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        DownloadAsyncDoesNotDeduplicateThreeSameTitleDifferentChapterIdsWithoutExplicitSafety(
            bool mixedAmbiguity)
    {
        using TestWorkspace workspace = new();
        string sharedUrl = "https://www.qidian.com/chapter/100/9/";
        string thirdUrl = mixedAmbiguity ? "   " : sharedUrl;
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalogWithUrls(
                    "100",
                    (
                        "Volume",
                        true,
                        [
                            ("1", "Shared Title", sharedUrl, false, 100),
                            ("2", "Shared Title", sharedUrl, true, 100),
                            ("3", "Shared Title", thirdUrl, true, 100),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["free content"], IsPreview: false),
                new ChapterFetchResult(["vip content"], IsPreview: false),
                new ChapterFetchResult(["ambiguous content"], IsPreview: false),
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
        Assert.Contains("fetch-chapter:headless:100:1", browserManager.Events);
        Assert.Contains("fetch-chapter:headless:100:2", browserManager.Events);
        Assert.Contains("fetch-chapter:headless:100:3", browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Equal(3, CountOccurrences(markdown, "Shared Title"));
        Assert.Contains("free content", markdown);
        Assert.Contains("vip content", markdown);
        Assert.Contains("ambiguous content", markdown);
    }

    [Fact]
    public async Task DownloadAsyncDeduplicatesValidatedCatalogPlansAndRendering()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Authentication Volume", true, [("4", "Auth VIP", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "4",
                ["auth preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Free Volume",
                    false,
                    [("1", "Free One", false, 100)]),
                (
                    "VIP Volume",
                    true,
                    [("1", "VIP One", true, 100)]))
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
                new ChapterFetchResult(["validated fetched content"], IsPreview: false),
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
        Assert.DoesNotContain("Fetching Free One", result.StdOut);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-chapter:headless:100:1"));
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("# VIP Volume", markdown);
        Assert.DoesNotContain("# Free Volume", markdown);
        Assert.Contains("validated fetched content", markdown);
        Assert.Equal(1, CountOccurrences(markdown, "VIP One"));
        Assert.DoesNotContain("Free One", markdown);
    }

    [Fact]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogForDuplicateAnonymousVipWithSingleValidatedMatch()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Anonymous VIP Volume",
                    true,
                    [
                        ("1", "Anonymous VIP One A", true, 100),
                        ("1", "Anonymous VIP One B", true, 100),
                    ]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Validated Volume", true, [("1", "Stale VIP One", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed Validated Volume", true, [("1", "Refreshed VIP One", true, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- Refreshed VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- Stale VIP One: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotRepeatedlyRefreshValidatedCatalogForDuplicateAnonymousVipAfterRefresh()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        DateTimeOffset refreshedValidatedFetchedAt = anonymousFetchedAt.AddMinutes(1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Anonymous VIP Volume",
                    true,
                    [
                        ("1", "Anonymous VIP One A", true, 100),
                        ("1", "Anonymous VIP One B", true, 100),
                    ]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Validated Volume", true, [("1", "Stale VIP One", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession firstHeadlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed Validated Volume", true, [("1", "Refreshed VIP One", true, 100)]))
                with
                {
                    FetchedAtUtc = refreshedValidatedFetchedAt,
                },
            ]);
        FakeBrowserManager firstBrowserManager = new(firstHeadlessSession);
        AppCommandService firstService = CreateService(workspace, firstBrowserManager);

        ConsoleCaptureResult firstResult = await WithConsoleCaptureAsync(
            () => firstService.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, firstResult.ReturnValue);
        Assert.Equal(
            1,
            firstBrowserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));

        FakeBrowserSession secondHeadlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserManager secondBrowserManager = new(secondHeadlessSession);
        AppCommandService secondService = CreateService(workspace, secondBrowserManager);

        ConsoleCaptureResult secondResult = await WithConsoleCaptureAsync(
            () => secondService.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, secondResult.ReturnValue);
        Assert.DoesNotContain("fetch-catalog:headless:100", secondBrowserManager.Events);
        Assert.Contains("- Refreshed VIP One: fetch", secondResult.StdOut);
        Assert.DoesNotContain("- Stale VIP One: fetch", secondResult.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogForDuplicateAnonymousVipWithAmbiguousValidatedMatches()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Anonymous VIP Volume",
                    true,
                    [
                        ("1", "Anonymous VIP One A", true, 100),
                        ("1", "Anonymous VIP One B", true, 100),
                    ]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Ambiguous Validated Volume",
                    true,
                    [
                        ("1", "Stale VIP One A", true, 100),
                        ("1", "Stale VIP One B", true, 100),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed Validated Volume", true, [("1", "Refreshed VIP One", true, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- Refreshed VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- Stale VIP One A: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogForDuplicateAnonymousVipWithMissingValidatedMatch()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Anonymous VIP Volume",
                    true,
                    [
                        ("1", "Anonymous VIP One A", true, 100),
                        ("1", "Anonymous VIP One B", true, 100),
                    ]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Validated Volume", true, [("4", "Other VIP", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed Validated Volume", true, [("1", "Refreshed VIP One", true, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- Refreshed VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- Other VIP: fetch", result.StdOut);
    }

    [Theory]
    [InlineData(CatalogChapterAccessState.Accessible)]
    [InlineData(CatalogChapterAccessState.Unknown)]
    public async Task DownloadAsyncRefreshesValidatedCatalogForAnonymousVipValidatedFreeConflict(
        object staleValidatedAccessState)
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous VIP Volume", true, [("1", "Anonymous VIP One", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale Free Volume",
                    false,
                    [
                        (
                            "1",
                            "Stale Free One",
                            false,
                            100,
                            (CatalogChapterAccessState)staleValidatedAccessState),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed VIP Volume", true, [("1", "Refreshed VIP One", true, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- Refreshed VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- Stale Free One: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotSaveFreshValidatedVipDowngradeAsPublicFreeChapterCache()
    {
        using TestWorkspace workspace = new();
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous VIP Volume", true, [("1", "VIP One", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Fresh Free Volume",
                        false,
                        [
                            (
                                "1",
                                "Fresh Free One",
                                false,
                                100,
                                CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["primer full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
        Assert.Equal(
            VipFullContentCacheProvenance.ValidatedUser,
            cacheEntry.VipFullContentProvenance);
        Assert.True(cacheEntry.CatalogIsVip);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.ForValidatedUser("tester"),
                CancellationToken.None));
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotMintPublicFreeCacheAfterValidatedVipConfirmationFailure()
    {
        using TestWorkspace workspace = new();
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Validated Free Volume",
                    false,
                    [("1", "Free Before", false, 100)]),
                (
                    "Validated Mixed Volume",
                    true,
                    [
                        ("2", "VIP One", true, 100),
                        ("3", "Free After", false, 100),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                IsKnownAnonymous = false,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            loginStateExceptions:
            [
                null,
                null,
                null,
                null,
                new InvalidOperationException("confirmation failed"),
                new InvalidOperationException("still uncertain"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["validated primer content"], IsPreview: false),
                new ChapterFetchResult(["validated free before content"], IsPreview: false),
                new ChapterFetchResult(["validated vip content"], IsPreview: false),
                new ChapterFetchResult(["validated free after content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                MinimumRequestDelaySeconds = 0.001,
                MaximumRequestDelaySeconds = 0.001,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Contains("fetch-chapter:headless:100:3", browserManager.Events);
        Assert.Null(
            await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "1",
                CancellationToken.None));
        Assert.Null(
            await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "3",
                CancellationToken.None));
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        if (File.Exists(outputPath))
        {
            string markdown = await File.ReadAllTextAsync(outputPath);
            Assert.DoesNotContain("validated free after content", markdown);
        }
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotMintPublicFreeCacheFromRawAnonymousEvidenceAfterValidatedConfirmationFailure()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Anonymous Mixed Volume",
                    true,
                    [
                        ("2", "Anonymous VIP", true, 100),
                        ("3", "Anonymous Free After", false, 100),
                    ]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "2",
                ["cached anonymous preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Free Volume",
                    false,
                    [
                        (
                            "1",
                            "Free Before",
                            false,
                            100,
                            CatalogChapterAccessState.Accessible),
                    ]),
                (
                    "Validated Mixed Volume",
                    true,
                    [
                        ("2", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        (
                            "3",
                            "Free After",
                            false,
                            100,
                            CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                IsKnownAnonymous = false,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            loginStateExceptions:
            [
                null,
                null,
                new InvalidOperationException("confirmation failed"),
                new InvalidOperationException("still uncertain"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["validated free before content"], IsPreview: false),
                new ChapterFetchResult(["validated vip content"], IsPreview: false),
                new ChapterFetchResult(["validated free after content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                MinimumRequestDelaySeconds = 0.001,
                MaximumRequestDelaySeconds = 0.001,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Contains("fetch-chapter:headless:100:3", browserManager.Events);
        Assert.Null(
            await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "3",
                CancellationToken.None));
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        Assert.DoesNotContain("validated free after content", markdown);
    }

    [Fact]
    public async Task
        DownloadAsyncFailClosesValidatedVipFullFetchAfterPriorConfirmationFailure()
    {
        using TestWorkspace workspace = new();
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                IsKnownAnonymous = false,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["validated primer content"], IsPreview: false),
                new ChapterFetchResult(["validated vip one content"], IsPreview: false),
                new ChapterFetchResult(["validated vip two content"], IsPreview: false),
            ],
            loginStateExceptions:
            [
                null,
                null,
                null,
                new InvalidOperationException("confirmation failed"),
                new InvalidOperationException("still uncertain"),
                new InvalidOperationException("still uncertain"),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                MinimumRequestDelaySeconds = 0.001,
                MaximumRequestDelaySeconds = 0.001,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Contains("fetch-chapter:headless:100:1", browserManager.Events);
        Assert.Contains("fetch-chapter:headless:100:2", browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "2",
            CancellationToken.None);
        Assert.Null(cacheEntry);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Equal(2, CountOccurrences(markdown, AppConstants.FailedChapterPlaceholder));
        Assert.DoesNotContain("validated vip one content", markdown);
        Assert.DoesNotContain("validated vip two content", markdown);
    }

    [Fact]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogForAnonymousVipValidatedFreeSameTitleConflict()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous VIP Volume", true, [("2", "Shared Title", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Stale Mixed Volume",
                    true,
                    [
                        ("1", "Shared Title", false, 100),
                        ("2", "Shared Title", true, 100),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed VIP Volume", true, [("2", "Refreshed VIP One", true, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- Refreshed VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- Shared Title: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogForAnonymousMixedSameTitleVipFreeConflict()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Anonymous Mixed Volume",
                    true,
                    [
                        ("1", "Shared Title", false, 100),
                        ("2", "Shared Title", true, 100),
                    ]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Free Volume", false, [("1", "Shared Title", false, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed VIP Volume", true, [("2", "Refreshed VIP One", true, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- Refreshed VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- Shared Title: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogForAmbiguousSameTitleValidatedFreeConflict()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous VIP Volume", true, [("2", "Shared Title", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Ambiguous Stale Volume",
                    true,
                    [
                        (
                            "1",
                            "Shared Title",
                            false,
                            100,
                            CatalogChapterAccessState.Unknown),
                        (
                            "2",
                            "Shared Title",
                            true,
                            100,
                            CatalogChapterAccessState.PurchaseRequired),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed VIP Volume", true, [("2", "Refreshed VIP One", true, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- Refreshed VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- Shared Title: fetch", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncRefreshesValidatedCatalogForAmbiguousAnonymousVipSameTitleValidatedFreeConflictOnce()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset anonymousFetchedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        DateTimeOffset staleValidatedFetchedAt = anonymousFetchedAt.AddMinutes(-1);
        DateTimeOffset refreshedValidatedFetchedAt = anonymousFetchedAt.AddMinutes(1);
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Anonymous VIP Volume",
                    true,
                    [
                        ("2", "Shared Title", true, 100),
                        ("3", "Shared Title", true, 100),
                    ]))
            with
            {
                IsKnownAnonymous = true,
                FetchedAtUtc = anonymousFetchedAt,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Ambiguous Stale Volume",
                    true,
                    [
                        (
                            "2",
                            "Shared Title",
                            true,
                            100,
                            CatalogChapterAccessState.PurchaseRequired),
                        (
                            "3",
                            "Shared Title",
                            true,
                            100,
                            CatalogChapterAccessState.PurchaseRequired),
                        (
                            "1",
                            "Shared Title",
                            false,
                            100,
                            CatalogChapterAccessState.Unknown),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                FetchedAtUtc = staleValidatedFetchedAt,
            },
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "Refreshed VIP Volume",
                        true,
                        [
                            ("2", "Refreshed VIP One", true, 100),
                            ("3", "Refreshed VIP Two", true, 100),
                        ]))
                with
                {
                    FetchedAtUtc = refreshedValidatedFetchedAt,
                },
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            1,
            browserManager.Events.Count(
                static eventName => eventName == "fetch-catalog:headless:100"));
        Assert.Contains("- Refreshed VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- Shared Title: fetch", result.StdOut);

        FakeBrowserSession reuseHeadlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserManager reuseBrowserManager = new(reuseHeadlessSession);
        AppCommandService reuseService = CreateService(workspace, reuseBrowserManager);

        ConsoleCaptureResult reuseResult = await WithConsoleCaptureAsync(
            () => reuseService.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, reuseResult.ReturnValue);
        Assert.DoesNotContain(
            "fetch-catalog:headless:100",
            reuseBrowserManager.Events);
        Assert.Contains("- Refreshed VIP One: fetch", reuseResult.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncUsesFreshAnonymousFreeCatalogAfterKnownValidatedIdentity()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "200",
                ("Free Volume", false, [("2", "Free Two", false, 200)])),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
        Assert.Equal(2, headlessSession.LoginStateRequests);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
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
            CreateCatalog("200", ("VIP Volume", true, [("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "2",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                200,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
                CreateCatalog("200", ("Free Volume", false, [("2", "Free Two", false, 200)])),
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
            CreateCatalog("200", ("VIP Volume", true, [("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "2",
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                "1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("VIP Volume", true, [("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "2",
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
            CreateCatalog("200", ("VIP Volume", true, [("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "2",
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                "fetch-chapter:headless:100:1",
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
                "1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)]))
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
                                "1",
                                "VIP One",
                                true,
                                100,
                                CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
        Assert.Equal(2, resumedHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
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
                "1",
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                        cancellation.Token)
                    .WaitAsync(TimeSpan.FromSeconds(5))));

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
                "1",
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                "1",
                ["full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester"),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)]))
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
        Assert.Equal(2, resumedHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                "1",
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
                                "1",
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
        Assert.Equal(2, resumedHeadlessSession.LoginStateRequests);
        Assert.Equal(
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
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
        await SaveValidatedIdentityPrimerAsync(workspace);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                "1",
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                new LoginState(true, "tester"),
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
        Assert.Equal(1, headlessSession.LoginStateRequests);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("Authentication is required.", result.StdOut);
        Assert.DoesNotContain(
            "Login confirmed. Continuing with the validated session.",
            result.StdOut);
        Assert.DoesNotContain("- VIP One: cached", result.StdOut);
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
                "1",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                "1",
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
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
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal(CatalogChapterAccessState.Accessible, cacheEntry.CatalogAccessState);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("full content", markdown);
        Assert.DoesNotContain("discarded anonymous full content", markdown);
    }

    [Fact]
    public async Task
        DownloadAsyncMarksVipFullContentAccessibleWhenFetchedFromValidatedCatalogPlan()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
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
        Assert.Equal(3, headlessSession.LoginStateRequests);
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
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "tester"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveValidatedCatalogWhenIdentityChangesAfterFetch()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous VIP", true, [("1", "Anonymous VIP", true, 100)])),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "other"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Validated Volume",
                        true,
                        [
                            ("1", "Validated VIP", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Contains("fetch-catalog:headless:100", browserManager.Events);
        Assert.Null(await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.ForValidatedUser("tester"),
            CancellationToken.None));
        Assert.Null(await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.ForValidatedUser("other"),
            CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsyncUsesValidatedCatalogCacheWhenChapterCacheIsReusable()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                "1",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.PurchaseRequired),
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
                "1",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
        Assert.Contains("- VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("- VIP One: cached", result.StdOut);
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.PurchaseRequired),
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
                "1",
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
                                "1",
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                "1",
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
                                "1",
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                "1",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
        Assert.DoesNotContain("- VIP One: cached", result.StdOut);
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
                "1",
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
                                "1",
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.PurchaseRequired),
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
                "1",
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
                                "1",
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
    [InlineData(false, CatalogChapterAccessState.Accessible, "")]
    [InlineData(true, CatalogChapterAccessState.PurchaseRequired, "- VIP One: changed")]
    [InlineData(true, CatalogChapterAccessState.Accessible, "")]
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
                                "1",
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
                "1",
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
                            ("1", "VIP One", true, 100, refreshedAccessState),
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
        if (expectedChapterStatus.Length > 0)
        {
            Assert.Contains(expectedChapterStatus, result.StdOut);
        }

        Assert.Equal(
            expectedChapterStatus.Contains("cached", StringComparison.Ordinal),
            result.StdOut.Contains("- VIP One: cached", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DownloadAsyncOverwriteDoesNotForceCatalogRefresh()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                CatalogIsVip: false,
                IsAnonymousSafeFullContent: true),
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
    public async Task DownloadAsyncAsksBeforeFetchingChaptersWhenInitialOutputExists()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "existing output");
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["should not be fetched"], IsPreview: false),
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
        Assert.Equal("existing output", await File.ReadAllTextAsync(outputPath));
        Assert.Empty(browserManager.Events);
        Assert.Null(
            await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "1",
                CancellationToken.None));
        Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
        Assert.Contains(
            "Summary: books completed=0, skipped=1, failed=0; "
            + "chapters downloaded=0, reused=0, failed=0.",
            result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncChecksFreshCachedOutputPathBeforeOpeningBrowser()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Fresh Volume", false, [("1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "existing output");
        FakeBrowserManager browserManager = new(new InvalidOperationException("should not open"));
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Empty(browserManager.Events);
        Assert.Equal("existing output", await File.ReadAllTextAsync(outputPath));
        Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncRejectsExistingOutputFileReparsePointBeforeOverwritePrompt()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                CatalogIsVip: false,
                IsAnonymousSafeFullContent: true),
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
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
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
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                CatalogIsVip: false,
                IsAnonymousSafeFullContent: true),
            CancellationToken.None);
        string outsideRoot = Path.Combine(workspace.Root, "outside-output");
        Directory.CreateDirectory(outsideRoot);
        if (!CanCreateDirectorySymbolicLink(workspace.Root))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
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
    public async Task DownloadAsyncRechecksOutputOverwriteImmediatelyBeforeFinalWrite()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                CatalogIsVip: false,
                IsAnonymousSafeFullContent: true),
            CancellationToken.None);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        FakeBrowserManager browserManager = new();
        AppCommandService service = CreateService(workspace, browserManager);

        try
        {
            AppCommandService.BeforeOutputWriteForTests = path =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "raced output");
            };

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                    },
                    CancellationToken.None));

            Assert.Equal(ExitCodes.Success, result.ReturnValue);
            Assert.Equal("raced output", await File.ReadAllTextAsync(outputPath));
            Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
            Assert.Contains("Summary: books completed=0, skipped=1, failed=0", result.StdOut);
        }
        finally
        {
            AppCommandService.BeforeOutputWriteForTests = null;
        }
    }

    [Fact]
    public async Task DownloadAsyncRechecksOutputOverwriteAfterPendingCacheCommits()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", true, [("1", "Chapter 1", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh fetched content"], IsPreview: true),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        try
        {
            CacheStore.BeforeChapterCacheCommitForTests = (_, _) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, "raced output");
                return Task.CompletedTask;
            };

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                    },
                    CancellationToken.None));

            Assert.Equal(ExitCodes.Success, result.ReturnValue);
            Assert.Equal("raced output", await File.ReadAllTextAsync(outputPath));
            Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
            Assert.NotNull(await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "1",
                CancellationToken.None));
        }
        finally
        {
            CacheStore.BeforeChapterCacheCommitForTests = null;
        }
    }

    [Fact]
    public async Task
        DownloadAsyncRejectsOutputDirectoryReparsePointReplacedAfterPendingCacheCommits()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", true, [("1", "Chapter 1", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string outputDirectory = Path.GetDirectoryName(outputPath)!;
        string outsideRoot = Path.Combine(workspace.Root, "outside-output");
        Directory.CreateDirectory(outsideRoot);
        if (!CanCreateDirectorySymbolicLink(workspace.Root))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh fetched content"], IsPreview: true),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        try
        {
            AppCommandService.AfterPendingCacheCommitsForTests = _ =>
            {
                Directory.Delete(outputDirectory, recursive: true);
                Directory.CreateSymbolicLink(outputDirectory, outsideRoot);
            };

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
            Assert.NotNull(await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "1",
                CancellationToken.None));
        }
        finally
        {
            AppCommandService.AfterPendingCacheCommitsForTests = null;
            DeleteReparseDirectoryIfExists(outputDirectory);
        }
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveInitiallyFetchedValidatedCatalogWhenOutputDenied()
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot fetchedCatalog =
            CreateCatalog("100", ("Fetched Volume", true, [("1", "Fetched VIP", true, 100)]))
            with
            {
                Metadata = new BookMetadata("100", "Fetched Book", "Author", null),
            };
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Fetched Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "existing output");
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                fetchedCatalog,
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing output", await File.ReadAllTextAsync(outputPath));
        Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.ForValidatedUser("tester"),
                CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveIsolatedAnonymousCatalogWhenOutputDenied()
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot staleCatalog =
            CreateCatalog("100", ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                Metadata = new BookMetadata("100", "Stale Book", "Author", null),
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            };
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            staleCatalog,
            CancellationToken.None);
        CatalogSnapshot fetchedCatalog =
            CreateCatalog("100", ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)]))
            with
            {
                Metadata = new BookMetadata("100", "Fresh Book", "Author", null),
            };
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Fresh Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "existing output");
        FakeBrowserSession isolatedAnonymousSession = new(catalogs: [fetchedCatalog]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing output", await File.ReadAllTextAsync(outputPath));
        Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(savedCatalog);
        Assert.Equal("Stale Book", savedCatalog.Metadata.Title);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveForceRefreshedValidatedCatalogWhenOutputDenied()
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot staleValidatedCatalog =
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale Validated Volume",
                    true,
                    [
                        ("1", "Stale Validated VIP", true, 100, CatalogChapterAccessState.PurchaseRequired),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Stale Validated Book", "Author", null),
            };
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            staleValidatedCatalog,
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester",
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser),
            CancellationToken.None);
        CatalogSnapshot refreshedValidatedCatalog =
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Refreshed Validated Volume",
                    true,
                    [
                        ("1", "Refreshed Validated VIP", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Refreshed Validated Book", "Author", null),
            };
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Refreshed Validated Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "existing output");
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog("99", ("Primer Volume", true, [("1", "Primer VIP", true, 100)])),
                refreshedValidatedCatalog,
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["primer full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing output", await File.ReadAllTextAsync(outputPath));
        Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.ForValidatedUser("tester"),
            CancellationToken.None);
        Assert.NotNull(savedCatalog);
        Assert.Equal("Stale Validated Book", savedCatalog.Metadata.Title);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveRefetchedValidatedCatalogWhenReplannedOutputDenied()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Anonymous", true, [("1", "Anonymous VIP", true, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        CatalogSnapshot staleValidatedCatalog =
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Stale Validated Volume",
                    true,
                    [
                        ("1", "Stale Validated VIP", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Stale Validated Book", "Author", null),
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            };
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            staleValidatedCatalog,
            CancellationToken.None);
        CatalogSnapshot refreshedValidatedCatalog =
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Refreshed Validated Volume",
                    true,
                    [
                        ("1", "Refreshed Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Refreshed Validated Book", "Author", null),
            };
        string replannedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Refreshed Validated Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(replannedOutputPath)!);
        await File.WriteAllTextAsync(replannedOutputPath, "existing replanned output");
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Fresh Anonymous", true, [("1", "Anonymous VIP", true, 100)])),
            ]);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                refreshedValidatedCatalog,
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing replanned output", await File.ReadAllTextAsync(replannedOutputPath));
        Assert.Contains(
            $"Skipped '{replannedOutputPath}' because overwrite was not approved.",
            result.StdOut);
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.ForValidatedUser("tester"),
            CancellationToken.None);
        Assert.NotNull(savedCatalog);
        Assert.Equal("Stale Validated Book", savedCatalog.Metadata.Title);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotSaveColdProvenAnonymousRegularBrowserCatalogWhenOutputDenied()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("99", ("Primer Volume", true, [("1", "Primer VIP", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "99",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        CatalogSnapshot fetchedCatalog =
            CreateCatalog("100", ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)]))
            with
            {
                Metadata = new BookMetadata("100", "Fresh Anonymous Book", "Author", null),
            };
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Fresh Anonymous Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, "existing output");
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            catalogs:
            [
                fetchedCatalog,
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing output", await File.ReadAllTextAsync(outputPath));
        Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSavePostRenderAnonymousPromotionWhenFinalOverwriteDenied()
    {
        using TestWorkspace workspace = new();
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "Unproven VIP", true, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["anonymous full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);
        bool reachedPostCommitOverwriteWindow = false;

        try
        {
            AppCommandService.AfterPendingCacheCommitsForTests = path =>
            {
                reachedPostCommitOverwriteWindow = true;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "raced output");
            };

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                    },
                    CancellationToken.None));

            Assert.Equal(ExitCodes.Success, result.ReturnValue);
            Assert.True(reachedPostCommitOverwriteWindow);
            Assert.Equal("raced output", await File.ReadAllTextAsync(outputPath));
            Assert.Contains($"Skipped '{outputPath}' because overwrite was not approved.", result.StdOut);
            Assert.Null(
                await CacheStore.GetCatalogAsync(
                    workspace.Paths.CacheRoot,
                    "100",
                    CatalogCacheScope.Anonymous,
                    CancellationToken.None));
        }
        finally
        {
            AppCommandService.AfterPendingCacheCommitsForTests = null;
        }
    }

    [Fact]
    public async Task DownloadAsyncDoesNotReuseFullChapterCacheEntryWhenProbeFieldsRace()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("1", "Chapter One", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["safe cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                CatalogIsVip: false,
                IsAnonymousSafeFullContent: true),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh fetched content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");

        try
        {
            CacheStore.AfterChapterCacheProbeReadForTests = cachePath =>
            {
                File.WriteAllText(
                    cachePath,
                    """
                    {
                        "chapterId": "1",
                        "paragraphs": ["poisoned cached content"],
                        "isPreview": false,
                        "catalogWordCount": 100,
                        "catalogAccessState": "Accessible",
                        "visibleToUserName": "attacker",
                        "vipFullContentProvenance": "ValidatedUser",
                        "catalogIsVip": true
                    }
                    """);
            };

            ConsoleCaptureResult result = await WithConsoleCaptureAsync(
                () => service.DownloadAsync(
                    new DownloadCommandOptions
                    {
                        BookReferences = ["100"],
                    },
                    CancellationToken.None));

            Assert.Equal(ExitCodes.Success, result.ReturnValue);
            Assert.Contains("fetch-chapter:headless:100:1", browserManager.Events);
            string output = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("fresh fetched content", output);
            Assert.DoesNotContain("poisoned cached content", output);
        }
        finally
        {
            CacheStore.AfterChapterCacheProbeReadForTests = null;
        }
    }

    [Fact]
    public async Task DownloadAsyncDoesNotReuseCachedChapterContainingBareLoginMarker()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Free Volume", false, [("1", "Chapter One", false, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["请登录"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                CatalogIsVip: false,
                IsAnonymousSafeFullContent: true),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh fetched content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [
                "open:headless",
                "fetch-chapter:headless:100:1",
            ],
            browserManager.Events);
        Assert.Contains("Fetching Chapter One", result.StdOut);
        Assert.DoesNotContain("- Chapter One: cached", result.StdOut);
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("fresh fetched content", markdown);
        Assert.DoesNotContain("请登录", markdown);
    }

    [Fact]
    public async Task BuildChapterPlansUsesFullEntryMetadataWhenReusableEntryReplacesProbe()
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot catalog = CreateCatalogWithAccessStates(
            "100",
            (
                "VIP Volume",
                true,
                [("1", "Chapter One", true, 100, CatalogChapterAccessState.Accessible)]));
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["safe cached content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VipFullContentProvenance: VipFullContentCacheProvenance.Public,
                CatalogIsVip: true),
            CancellationToken.None);

        try
        {
            CacheStore.AfterChapterCacheProbeReadForTests = cachePath =>
            {
                File.WriteAllText(
                    cachePath,
                    JsonSerializer.Serialize(
                        new ChapterCacheEntry(
                            "1",
                            ["sensitive cached content"],
                            IsPreview: false,
                            100,
                            CatalogChapterAccessState.Accessible,
                            VisibleToUserName: "tester",
                            VipFullContentProvenance:
                                VipFullContentCacheProvenance.ValidatedUser,
                            CatalogIsVip: true),
                        AppJsonSerializerContext.Default.ChapterCacheEntry));
            };

            List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
                catalog,
                workspace.Paths.CacheRoot,
                new LoginState(true, "tester"),
                CancellationToken.None);

            ChapterPlan plan = Assert.Single(plans);
            Assert.Equal(ChapterPlanStatus.Cached, plan.Status);
            Assert.NotNull(plan.CachedEntry);
            Assert.Equal("tester", plan.CachedProbe?.VisibleToUserName);
            Assert.Equal(VipFullContentCacheProvenance.ValidatedUser, plan.CachedProbe?.VipFullContentProvenance);
            Assert.False(AppCommandService.CanReuseCachedPlanForCurrentLoginState(
                plan,
                new LoginState(true, null),
                loginStateProbeFailed: false));
        }
        finally
        {
            CacheStore.AfterChapterCacheProbeReadForTests = null;
        }
    }

    [Fact]
    public async Task
        DownloadAsyncReusesFreshAnonymousVipPreviewPlanAfterLoggedOutSessionProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
    public async Task
        DownloadAsyncDryRunRefetchesUnprovenAnonymousCatalogBeforeTrustAfterLoggedOutProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.Accessible),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
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
                            ("1", "Unproven VIP", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("1", "Proven VIP", true, 100, CatalogChapterAccessState.Accessible),
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
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        CatalogSnapshot? savedCatalog =
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None);
        Assert.NotNull(savedCatalog);
        Assert.True(savedCatalog.IsKnownAnonymous);
        Assert.Equal("Proven VIP", savedCatalog.Volumes[0].Chapters[0].Title);
        Assert.Contains("- Proven VIP: cached", result.StdOut);
        Assert.DoesNotContain("- Unproven VIP: cached", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotTrustUnprovenAnonymousCatalogInPostRenderTrustAfterLoggedOutProbe()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "Unproven VIP", true, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["anonymous full content"], IsPreview: false),
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
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.Null(savedCatalog);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("anonymous full content", markdown);
    }

    [Fact]
    public async Task DownloadAsyncFetchesCachedVipPreviewWhenLoginProbeFails()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=0, reused=0, failed=1.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("fresh visible content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.True(cacheEntry.IsPreview);
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
                "1",
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
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                "fetch-chapter:headless:100:1",
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
                    [("1", "VIP One", true, 100), ("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
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
                "2",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                            ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["second full content"], IsPreview: false),
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
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
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
            "2",
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
                ("Anonymous Volume", true, [("3", "Anonymous VIP", true, 100)])),
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
                                "4",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
        Assert.Null(
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
    public async Task DownloadAsyncReusesFreshValidatedCatalogAfterIsolatedAnonymousDiscovery()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Anonymous", true, [("1", "Anonymous VIP", true, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Cached",
                    true,
                    [
                        ("1", "Validated VIP", true, 100, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Fresh Anonymous", true, [("1", "Anonymous VIP", true, 100)])),
            ]);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
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
                "fetch-catalog:anonymous-headless:100",
                "open:headless",
                "login-state:headless",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.DoesNotContain("fetch-catalog:headless:100", browserManager.Events);
        Assert.Contains("- Validated VIP: fetch", result.StdOut);
        Assert.DoesNotContain("Anonymous VIP", result.StdOut);
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                "fetch-chapter:headless:100:1",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
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
                "1",
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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
    public async Task DownloadAsyncDoesNotTrustUnprovenCurrentSessionVipCatalogAsAnonymous()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "Chapter One", true, 100)])),
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
                CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
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
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
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
    public async Task InfoAsyncUsesIsolatedAnonymousBrowserForColdCatalogAndReusesSavedCache()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:anonymous-headless:100",
            ],
            browserManager.Events);
        CatalogSnapshot? savedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(savedCatalog);
        Assert.True(savedCatalog.IsKnownAnonymous);

        FakeBrowserManager reuseBrowserManager = new(new InvalidOperationException("browser failed"));
        AppCommandService reuseService = CreateService(workspace, reuseBrowserManager);

        ConsoleCaptureResult reuseResult = await WithConsoleCaptureAsync(
            () => reuseService.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, reuseResult.ReturnValue);
        Assert.Empty(reuseBrowserManager.OpenCalls);
        Assert.Contains("Volume", reuseResult.StdOut);
    }

    [Fact]
    public async Task InfoAsyncRefreshesStaleTrustedAnonymousCatalogWithIsolatedAnonymousBrowser()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Refreshed Volume", false, [("1", "Refreshed Chapter", false, 100)])),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:anonymous-headless:100",
            ],
            browserManager.Events);
        Assert.Contains("Refreshed Volume", result.StdOut);
        CatalogSnapshot? refreshedCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(refreshedCatalog);
        Assert.True(refreshedCatalog.IsKnownAnonymous);
        Assert.Equal("Refreshed Chapter", refreshedCatalog.Volumes[0].Chapters[0].Title);

        FakeBrowserManager reuseBrowserManager = new(new InvalidOperationException("browser failed"));
        AppCommandService reuseService = CreateService(workspace, reuseBrowserManager);

        ConsoleCaptureResult reuseResult = await WithConsoleCaptureAsync(
            () => reuseService.InfoAsync(
                new InfoCommandOptions
                {
                    BookReference = "100",
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, reuseResult.ReturnValue);
        Assert.Empty(reuseBrowserManager.OpenCalls);
        Assert.Contains("Refreshed Volume", reuseResult.StdOut);
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
                CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
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
                "fetch-catalog:anonymous-headless:100",
            ],
            browserManager.Events);
        Assert.Contains("Book ID: 100", result.StdOut);
        Assert.Contains("Summary: completed=1, reused=0, skipped=0, failed=0.", result.StdOut);
    }

    [Fact]
    public async Task InfoAsyncSavesIsolatedAnonymousCatalogIntoAnonymousCache()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession isolatedAnonymousSession = new(
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession);
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
        CatalogSnapshot? anonymousCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.NotNull(anonymousCatalog);
        Assert.True(anonymousCatalog.IsKnownAnonymous);
    }

    [Fact]
    public async Task DownloadAsyncDryRunDoesNotSaveColdRegularBrowserFreeCatalogWithoutProof()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Free Volume", false, [("1", "Free One", false, 100)])),
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
        CatalogSnapshot? anonymousCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.Null(anonymousCatalog);
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "100");
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveColdRegularBrowserCatalogWithoutBoundLoggedOutProof()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
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
                            ("1", "Public VIP", true, 100, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["public full content"], IsPreview: false),
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
        CatalogSnapshot? anonymousCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.Null(anonymousCatalog);
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "100");
    }

    [Fact]
    public async Task InfoAsyncIgnoresInvalidDownloadOnlySettings()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
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
                "1",
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
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                "fetch-chapter:headless:100:1",
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
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.True(cacheEntry.IsPreview);
        Assert.Null(cacheEntry.VisibleToUserName);
        CatalogSnapshot? anonymousCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.Null(anonymousCatalog);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotMarkVipFullContentPublicFromPostFetchProbeOnly()
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
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Equal(CatalogChapterAccessState.PurchaseRequired, cacheEntry.CatalogAccessState);
        Assert.Null(cacheEntry.VisibleToUserName);
        Assert.Null(cacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task
        DownloadAsyncFailsClosedWhenUnvalidatedSessionLogsOutAfterVipFullContentFetch()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.Null(cacheEntry);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("full content", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotTrustAnonymousCatalogFromWeakLoggedOutProbeBeforeValidatedProbe()
    {
        using TestWorkspace workspace = new();
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
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Validated VIP Volume",
                        true,
                        [
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ]))
                with
                {
                    CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                },
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["validated full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
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
        Assert.Equal(
            [
                LoginStateProbeMode.CurrentStateOnly,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        CatalogSnapshot? anonymousCatalog = await CacheStore.GetCatalogAsync(
            workspace.Paths.CacheRoot,
            "100",
            CatalogCacheScope.Anonymous,
            CancellationToken.None);
        Assert.Null(anonymousCatalog);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotSaveAnonymousCatalogFromStaleCrossBookLoggedOutProof()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
                CreateCatalog("200", ("VIP Volume", true, [("2", "VIP Two", true, 200)])),
                CreateCatalogWithAccessStates(
                    "200",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["validated full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100", "200"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "200",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "200");
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            "2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
        Assert.Equal(
            VipFullContentCacheProvenance.ValidatedUser,
            cacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotMarkNextBookRegularCatalogKnownAnonymousFromPreviousBookProof()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
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
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
                CreateCatalog("200", ("Free Volume", false, [("2", "Free Two", false, 200)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["first book full content"], IsPreview: false),
                new ChapterFetchResult(["free content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100", "200"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "200",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotSaveRegularAnonymousCatalogWhenLoggedOutProofIsOnlyPostFetch()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("VIP Volume", true, [("1", "VIP One", true, 100)])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["public full content"], IsPreview: false),
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
        Assert.Equal(
            [LoginStateProbeMode.CurrentStateOnly, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Null(
            await CacheStore.GetCatalogAsync(
                workspace.Paths.CacheRoot,
                "100",
                CatalogCacheScope.Anonymous,
                CancellationToken.None));
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "100");
    }

    [Fact]
    public async Task
        DownloadAsyncRevokesPendingAnonymousCatalogSaveWhenPostChapterProbeFindsAuthenticatedState()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                new ChapterCacheEntry(
                    "1",
                    ["cached preview", AppConstants.TruncatedChapterMarker],
                    IsPreview: true,
                    100,
                    CatalogChapterAccessState.PurchaseRequired),
                CancellationToken.None);
        FakeBrowserSession headlessSession = new(
                loginStates:
                [
                    new LoginState(false, null),
                    new LoginState(true, null),
                ],
                catalogs:
                [
                    CreateCatalog(
                        "100",
                        (
                            "VIP Volume",
                            true,
                            [
                                ("2", "VIP Two", true, 200),
                                ("1", "VIP One", true, 100),
                            ])),
                ],
                chapterFetchResults:
                [
                    new ChapterFetchResult(["full content 2"], IsPreview: false),
                    new ChapterFetchResult(["full content 1"], IsPreview: false),
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal(
                [
                    "open:headless",
                    "fetch-catalog:headless:100",
                    "login-state:headless",
                    "fetch-chapter:headless:100:2",
                    "login-state:headless",
                    "fetch-chapter:headless:100:1",
                ],
                browserManager.Events);
        Assert.Null(
                await CacheStore.GetCatalogAsync(
                    workspace.Paths.CacheRoot,
                    "100",
                    CatalogCacheScope.Anonymous,
                    CancellationToken.None));
        AssertAnonymousCatalogCacheFileDoesNotExist(workspace, "100");
    }

    [Fact]
    public async Task DownloadAsyncSavesNewVipFullContentAsPublicAfterStrongLoggedOutProof()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
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
                    (
                        "VIP Volume",
                        true,
                        [
                            ("1", "VIP One", true, 100),
                            ("2", "VIP Two", true, 200),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["public full content"], IsPreview: false),
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
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.False(cacheEntry.IsPreview);
        Assert.Null(cacheEntry.VisibleToUserName);
        Assert.Equal(
            VipFullContentCacheProvenance.Public,
            cacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task DownloadAsyncFailsClosedAfterIncompleteLoggedOutVipFullContentProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null, IsProbeComplete: false),
                new LoginState(false, null, IsProbeComplete: false),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("1", "VIP One", true, 100),
                            ("2", "VIP Two", true, 200),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["unproven first full content"], IsPreview: false),
                new ChapterFetchResult(["unproven full content"], IsPreview: false),
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.True(headlessSession.LoginStateProbeModes.Count >= 2);
        Assert.All(
            headlessSession.LoginStateProbeModes,
            probeMode => Assert.Equal(LoginStateProbeMode.WaitForValidatedIdentity, probeMode));
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "2",
            CancellationToken.None);
        Assert.Null(cacheEntry);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("unproven first full content", markdown);
        Assert.DoesNotContain("unproven full content", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
    }

    [Fact]
    public async Task
        DownloadAsyncRequiresPerFetchLoggedOutConfirmationForMultiplePublicVipFullChapters()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("1", "VIP One", true, 100),
                            ("2", "VIP Two", true, 200),
                            ("3", "VIP Three", true, 300),
                        ])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Validated VIP Volume",
                        true,
                        [
                            ("1", "Validated VIP One", true, 100, CatalogChapterAccessState.Accessible),
                            ("2", "Validated VIP Two", true, 200, CatalogChapterAccessState.Accessible),
                            ("3", "Validated VIP Three", true, 300, CatalogChapterAccessState.Accessible),
                        ]))
                with
                {
                    CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                },
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["public full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
                new ChapterFetchResult(["validated preview refetch"], IsPreview: false),
                new ChapterFetchResult(["validated two refetch"], IsPreview: false),
                new ChapterFetchResult(["validated three refetch"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                ReadingSpeed = 1_000_000,
                MinimumRequestDelaySeconds = 0.001,
                MaximumRequestDelaySeconds = 0.001,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.True(headlessSession.LoginStateProbeModes.Count >= 3);
        Assert.All(
            headlessSession.LoginStateProbeModes,
            mode => Assert.Equal(LoginStateProbeMode.WaitForValidatedIdentity, mode));
        ChapterCacheEntry? validatedTwoCacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "2",
            CancellationToken.None);
        Assert.NotNull(validatedTwoCacheEntry);
        Assert.Equal("tester", validatedTwoCacheEntry.VisibleToUserName);
        Assert.Equal(
            VipFullContentCacheProvenance.ValidatedUser,
            validatedTwoCacheEntry.VipFullContentProvenance);
        ChapterCacheEntry? validatedCacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "3",
            CancellationToken.None);
        Assert.NotNull(validatedCacheEntry);
        Assert.Equal("tester", validatedCacheEntry.VisibleToUserName);
        Assert.Equal(
            VipFullContentCacheProvenance.ValidatedUser,
            validatedCacheEntry.VipFullContentProvenance);
    }

    [Fact]
    public async Task
        DownloadAsyncFailsClosedWhenValidatedIdentityChangesAfterVipFullContentFetch()
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
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
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
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.Null(cacheEntry);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("full content", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=0, reused=0, failed=1.",
            result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotReuseSameUserVipCacheAfterPostFetchConfirmationProbeFails()
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
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
                "2",
                ["cached same-user full content"],
                IsPreview: false,
                200,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester",
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser,
                CatalogIsVip: true),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "Anonymous VIP Volume",
                        true,
                        [
                            ("1", "Anonymous VIP One", true, 100),
                            ("2", "Anonymous VIP Two", true, 200),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh one"], IsPreview: false),
                new ChapterFetchResult(["fresh two"], IsPreview: false),
            ],
            loginStateExceptions: [null, null, new InvalidOperationException("probe failed")]);
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
                "login-state:headless",
            ],
            browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("fresh one", markdown);
        Assert.DoesNotContain("fresh two", markdown);
        Assert.DoesNotContain("cached same-user full content", markdown);
        Assert.Equal(2, CountOccurrences(markdown, AppConstants.FailedChapterPlaceholder));
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal(["cached same-user full content"], cacheEntry.Paragraphs);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=0, reused=0, failed=2.",
            result.StdOut);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotReuseValidatedCatalogWhenCurrentStateProbeIsIncomplete()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated VIP Volume",
                    true,
                    [
                        ("2", "Validated VIP Two", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                IsKnownAnonymous = false,
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "2",
                ["cached same-user full content"],
                IsPreview: false,
                200,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester",
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser,
                CatalogIsVip: true),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester", IsProbeComplete: false),
            ],
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("Anonymous VIP Volume", true, [("1", "Anonymous VIP One", true, 100)])),
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
        Assert.Equal([LoginStateProbeMode.CurrentStateOnly], headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:headless:100",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("- Anonymous VIP One: fetch", result.StdOut);
        Assert.DoesNotContain("Validated VIP Two", result.StdOut);
        Assert.Contains("Dry-run summary: cached=0, changed=0, fetch-required=1.", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncFailClosesRenderedValidatedFreeChapterWhenLaterVipConfirmationFails()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "99",
                    (
                        "Primer Volume",
                        true,
                        [
                            (
                                "1",
                                "Primer VIP",
                                true,
                                100,
                                CatalogChapterAccessState.Accessible),
                        ])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "Validated Mixed Volume",
                        false,
                        [
                            ("1", "Validated Free", false, 100, CatalogChapterAccessState.Accessible),
                            ("2", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["primer full content"], IsPreview: false),
                new ChapterFetchResult(["validated free content"], IsPreview: false),
                new ChapterFetchResult(["validated vip content"], IsPreview: false),
            ],
            loginStateExceptions:
            [
                null,
                null,
                null,
                null,
                null,
                new InvalidOperationException("post-fetch confirmation failed"),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                MinimumRequestDelaySeconds = 0.001,
                MaximumRequestDelaySeconds = 0.001,
                RetryCount = 0,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("validated free content", markdown);
        Assert.DoesNotContain("validated vip content", markdown);
        Assert.Equal(2, CountOccurrences(markdown, AppConstants.FailedChapterPlaceholder));
        ChapterCacheEntry? freeCacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.Null(freeCacheEntry);
    }

    [Fact]
    public async Task
        DownloadAsyncFailClosesRenderedSameUserVipCacheAfterPostFetchConfirmationProbeFails()
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
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
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
                "1",
                ["cached same-user full content"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester",
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser,
                CatalogIsVip: true),
            CancellationToken.None);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
                new LoginState(true, "tester"),
            ],
            catalogs:
            [
                CreateCatalogWithAccessStates(
                    "99",
                    (
                        "Primer Volume",
                        true,
                        [
                            (
                                "1",
                                "Primer VIP",
                                true,
                                100,
                                CatalogChapterAccessState.Accessible),
                        ])),
                CreateCatalogWithAccessStates(
                    "100",
                    (
                        "VIP Volume",
                        true,
                        [
                            ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                            ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
                        ])),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["primer full content"], IsPreview: false),
                new ChapterFetchResult(["fresh two"], IsPreview: false),
                new ChapterFetchResult(["unexpected one"], IsPreview: false),
            ],
            loginStateExceptions:
            [
                null,
                null,
                null,
                new InvalidOperationException("probe failed"),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        TestLogger<AppCommandService> logger = new();
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            logger: logger,
            appSettings: new AppSettings
            {
                MinimumRequestDelaySeconds = 0.001,
                MaximumRequestDelaySeconds = 0.001,
                RetryCount = 0,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                },
                CancellationToken.None));
        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.DoesNotContain("fetch-chapter:headless:100:1", browserManager.Events);
        Assert.Contains("fetch-chapter:headless:100:2", browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("VIP One", markdown);
        Assert.DoesNotContain("cached same-user full content", markdown);
        Assert.DoesNotContain("fresh two", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        ChapterCacheEntry? cachedOne = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cachedOne);
        Assert.Equal(["cached same-user full content"], cachedOne.Paragraphs);
        ChapterCacheEntry? cachedTwo = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "2",
            CancellationToken.None);
        Assert.Null(cachedTwo);
        Assert.Contains(
            "Summary: books completed=2, skipped=0, failed=0; "
            + "chapters downloaded=1, reused=0, failed=2.",
            result.StdOut);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncFailsClosedForCompletedUnknownLoggedInVipFullContentClassification()
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
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
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
                "fetch-chapter:headless:100:1",
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
            "1",
            CancellationToken.None);
        Assert.Null(cacheEntry);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("full content", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotPromptForLoginWhenLoginProbeFailsAndAnonymousPlanIsUsable()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
    public async Task DownloadAsyncFailsClosedWhenVipFullContentClassificationProbeFails()
    {
        using TestWorkspace workspace = new();
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    ("VIP Volume", true, [("1", "VIP One", true, 100)])),
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
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.Null(cacheEntry);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("full content", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncReplansWithValidatedCatalogWhenVipFullClassificationDiscoversIdentity()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Anonymous", true, [("1", "Anonymous VIP", true, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Volume",
                    true,
                    [
                        ("1", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Validated Book", "Author", null),
            },
            CancellationToken.None);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Fresh Anonymous", true, [("1", "Anonymous VIP", true, 100)])),
            ]);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:anonymous-headless:100",
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Validated VIP", markdown);
        Assert.Contains("validated full content", markdown);
        Assert.DoesNotContain("Anonymous VIP", markdown);
        Assert.DoesNotContain("discarded anonymous full content", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotReplanWhenVipFullClassificationProvesAnonymous()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Anonymous", true, [("1", "Anonymous VIP", true, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Fresh Anonymous", true, [("1", "Anonymous VIP", true, 100)])),
            ]);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["anonymous full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:anonymous-headless:100",
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
            ],
            browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Anonymous VIP", markdown);
        Assert.Contains("anonymous full content", markdown);
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncPropagatesValidatedReplanRequestedWhileRefetchingRenderedSensitiveCache()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Anonymous Volume",
                    true,
                    [("1", "Anonymous One", true, 100), ("2", "Anonymous Two", true, 200)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Volume",
                    true,
                    [
                        ("1", "Validated One", true, 100, CatalogChapterAccessState.Accessible),
                        ("2", "Validated Two", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Validated Book", "Author", null),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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
                new ChapterFetchResult(["discarded anonymous c2"], IsPreview: false),
                new ChapterFetchResult(["discarded refetch c1"], IsPreview: false),
                new ChapterFetchResult(["validated c1"], IsPreview: false),
                new ChapterFetchResult(["validated c2"], IsPreview: false),
            ],
            loginStateExceptions: [null, new InvalidOperationException("probe failed"), null]);
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
        Assert.Equal(
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
                "login-state:headless",
            ],
            browserManager.Events);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Validated One", markdown);
        Assert.Contains("validated c1", markdown);
        Assert.Contains("Validated Two", markdown);
        Assert.Contains("validated c2", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        Assert.DoesNotContain("discarded anonymous c2", markdown);
        Assert.DoesNotContain("discarded refetch c1", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.Equal("tester", cacheEntry.VisibleToUserName);
        Assert.Equal(
            VipFullContentCacheProvenance.ValidatedUser,
            cacheEntry.VipFullContentProvenance);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task DownloadAsyncChecksReplannedOutputPathBeforeWrite()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Anonymous", true, [("1", "Anonymous VIP", true, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Volume",
                    true,
                    [
                        ("1", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Validated Book", "Author", null),
            },
            CancellationToken.None);
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(validatedOutputPath)!);
        await File.WriteAllTextAsync(validatedOutputPath, "existing validated output");
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Fresh Anonymous", true, [("1", "Anonymous VIP", true, 100)])),
            ]);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        AppCommandService service = CreateService(workspace, browserManager);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing validated output", await File.ReadAllTextAsync(validatedOutputPath));
        Assert.Contains($"Skipped '{validatedOutputPath}' because overwrite was not approved.", result.StdOut);
        Assert.Contains(
            "Summary: books completed=0, skipped=1, failed=0; "
            + "chapters downloaded=0, reused=0, failed=0.",
            result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotEarlySkipAnonymousCachedOutputBeforePersistedLoginIsDiscovered()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous Volume", true, [("1", "Anonymous VIP", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
                Metadata = new BookMetadata("100", "Anonymous Book", "Author", null),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
                ["cached anonymous preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Volume",
                    true,
                    [
                        ("1", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Validated Book", "Author", null),
            },
            CancellationToken.None);
        string anonymousOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Anonymous Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(anonymousOutputPath)!);
        await File.WriteAllTextAsync(anonymousOutputPath, "existing anonymous output");
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["validated full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing anonymous output", await File.ReadAllTextAsync(anonymousOutputPath));
        string markdown = await File.ReadAllTextAsync(validatedOutputPath);
        Assert.Contains("Validated VIP", markdown);
        Assert.Contains("validated full content", markdown);
        Assert.DoesNotContain("Skipped", result.StdOut);
        Assert.Contains("Summary: books completed=1, skipped=0, failed=0", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotLetDeniedProvisionalAnonymousOutputSuppressValidatedReplan()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Anonymous Volume", true, [("1", "Anonymous VIP", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
                Metadata = new BookMetadata("100", "Anonymous Book", "Author", null),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Volume",
                    true,
                    [
                        ("1", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Validated Book", "Author", null),
            },
            CancellationToken.None);
        string anonymousOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Anonymous Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(anonymousOutputPath)!);
        await File.WriteAllTextAsync(anonymousOutputPath, "existing anonymous output");
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing anonymous output", await File.ReadAllTextAsync(anonymousOutputPath));
        string markdown = await File.ReadAllTextAsync(validatedOutputPath);
        Assert.Contains("Validated VIP", markdown);
        Assert.Contains("validated full content", markdown);
        Assert.DoesNotContain("discarded anonymous full content", markdown);
        Assert.DoesNotContain("Skipped", result.StdOut);
        Assert.Contains("Summary: books completed=1, skipped=0, failed=0", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDryRunIgnoresCachedOutputOverwrite()
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot cachedCatalog = CreateCatalog(
            "100",
            ("Cached Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
        {
            FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
        };
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            cachedCatalog,
            CancellationToken.None);
        string cachedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(cachedOutputPath)!);
        await File.WriteAllTextAsync(cachedOutputPath, "existing output");
        FakeBrowserSession headlessSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)])),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                    DryRun = true,
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Empty(console.Prompts);
        Assert.Equal(
            [
                "open:headless",
                "fetch-catalog:anonymous-headless:100",
            ],
            browserManager.Events);
        Assert.Contains("- Fresh Chapter: fetch", result.StdOut);
        Assert.DoesNotContain("Skipped", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotDenyOverwriteFromStaleAnonymousCatalog()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Volume", false, [("1", "Stale Chapter", false, 100)]))
            with
            {
                Metadata = new BookMetadata("100", "Stale Book", "Author", null),
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        string staleOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Stale Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(staleOutputPath)!);
        await File.WriteAllTextAsync(staleOutputPath, "existing stale output");
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)]))
                with
                {
                    Metadata = new BookMetadata("100", "Fresh Book", "Author", null),
                },
            ]);
        FakeBrowserSession headlessSession = new(
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh content"], IsPreview: false),
            ],
            loginStates:
            [
                new LoginState(false, null),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Empty(console.Prompts);
        Assert.Equal("existing stale output", await File.ReadAllTextAsync(staleOutputPath));
        string freshOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Fresh Book",
            "Author");
        Assert.Contains("fresh content", await File.ReadAllTextAsync(freshOutputPath));
        Assert.Contains("Summary: books completed=1", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotSkipEarlyOverwriteFromFreshUnprovenAnonymousCatalog()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("99", ("Primer Volume", true, [("1", "Primer VIP", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "99",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Legacy Volume", false, [("1", "Legacy Chapter", false, 100)]))
            with
            {
                IsKnownAnonymous = false,
                Metadata = new BookMetadata("100", "Legacy Book", "Author", null),
            },
            CancellationToken.None);
        string legacyOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Legacy Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyOutputPath)!);
        await File.WriteAllTextAsync(legacyOutputPath, "existing legacy output");
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
            ],
            catalogs:
            [
                CreateCatalog("100", ("Fresh Volume", false, [("1", "Fresh Chapter", false, 100)]))
                with
                {
                    Metadata = new BookMetadata("100", "Fresh Book", "Author", null),
                },
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["fresh content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(headlessSession);
        RecordingInteractiveConsole console = new(response: false);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["99", "100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Empty(console.Prompts);
        Assert.Equal("existing legacy output", await File.ReadAllTextAsync(legacyOutputPath));
        string freshOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Fresh Book",
            "Author");
        Assert.Contains("fresh content", await File.ReadAllTextAsync(freshOutputPath));
        Assert.Contains("Summary: books completed=2", result.StdOut);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotUseAnonymousCachedOutputForEarlyOverwritePredictionWhenLoginIsUnknown()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Legacy Volume", false, [("1", "Legacy Chapter", false, 100)]))
            with
            {
                IsKnownAnonymous = false,
            },
            CancellationToken.None);

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("100"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                loginState: null,
                loginStateProbeMode: null,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Null(predictedOutputPath);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotUseAnonymousCachedOutputForEarlyOverwriteWhenLoggedOutIsWeaklyKnown()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Cached Volume", false, [("1", "Cached Chapter", false, 100)]))
            with
            {
                IsKnownAnonymous = false,
            },
            CancellationToken.None);
        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("100"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(false, null),
                LoginStateProbeMode.CurrentStateOnly,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Null(predictedOutputPath);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotUseUnprovenAnonymousCachedOutputForEarlyOverwriteWhenLoggedOutIsValidated()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Cached Volume", false, [("1", "Cached Chapter", false, 100)]))
            with
            {
                IsKnownAnonymous = false,
            },
            CancellationToken.None);
        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("100"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(false, null),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Null(predictedOutputPath);
    }

    [Fact]
    public async Task
        DownloadAsyncUsesKnownAnonymousCachedOutputForEarlyOverwriteWhenLoggedOutIsValidated()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Cached Volume", false, [("1", "Cached Chapter", false, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        string expectedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("100"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(false, null),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Equal(expectedOutputPath, predictedOutputPath);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotUseAnonymousCachedOutputForEarlyOverwriteWhenRunIdentityIsUncertain()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Cached Volume", false, [("1", "Cached Chapter", false, 100)]))
            with
            {
                IsKnownAnonymous = false,
            },
            CancellationToken.None);

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("100"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(false, null),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: true,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Null(predictedOutputPath);
    }

    [Fact]
    public async Task
        TryGetCachedOutputPathForOverwriteCheckAsyncUsesFreshValidatedUserCachedOutputWhenIdentityIsKnown()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "200",
                (
                    "Second Volume",
                    true,
                    [
                        ("2", "Second VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("200", "Validated Second Book", "Author", null),
            },
            CancellationToken.None);
        string expectedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Validated Second Book",
            "Author");

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("200"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(true, "tester"),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Equal(expectedOutputPath, predictedOutputPath);
    }

    [Fact]
    public async Task
        DownloadAsyncUsesFreshAnonymousCachedOutputInsteadOfStaleValidatedUserCatalogForEarlyOverwritePrediction()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("Anonymous Volume", false, [("1", "Anonymous", false, 100)]))
            with
            {
                Metadata = new BookMetadata("200", "Anonymous Book", "Author", null),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "200",
                (
                    "Stale Validated Volume",
                    true,
                    [
                        ("2", "Stale VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("200", "Stale Validated Book", "Author", null),
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);

        string expectedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Anonymous Book",
            "Author");

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("200"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(true, "tester"),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Equal(expectedOutputPath, predictedOutputPath);
    }

    [Fact]
    public async Task DownloadAsyncMirrorsFreshAnonymousCatalogBeforeValidatedOutputWhenIdentityIsKnown()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("Anonymous Volume", false, [("1", "Anonymous", false, 100)]))
            with
            {
                Metadata = new BookMetadata("200", "Anonymous Book", "Author", null),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "200",
                (
                    "Validated Volume",
                    true,
                    [
                        ("2", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("200", "Validated Book", "Author", null),
            },
            CancellationToken.None);
        string anonymousOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Anonymous Book",
            "Author");
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Validated Book",
            "Author");

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("200"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(true, "tester"),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Equal(anonymousOutputPath, predictedOutputPath);
        Assert.NotEqual(validatedOutputPath, predictedOutputPath);
    }

    [Fact]
    public async Task
        DownloadAsyncUsesAnonymousCachedOutputForEarlyOverwriteWhenValidatedSessionWouldUseFreshAnonymousCatalog()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("200", ("Second Anonymous", false, [("2", "Anonymous Free", false, 200)]))
            with
            {
                IsKnownAnonymous = true,
                Metadata = new BookMetadata("200", "Anonymous Second Book", "Author", null),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "200",
                (
                    "Second Validated",
                    true,
                    [
                        ("2", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("200", "Validated Second Book", "Author", null),
            },
            CancellationToken.None);
        string anonymousOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Anonymous Second Book",
            "Author");
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "200",
            "Validated Second Book",
            "Author");

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("200"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(true, "tester"),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Equal(anonymousOutputPath, predictedOutputPath);
        Assert.NotEqual(validatedOutputPath, predictedOutputPath);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotUseValidatedCachedOutputForEarlyOverwriteWhenEntitlementMismatchRequiresRefresh()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "200",
                (
                    "Validated Volume",
                    true,
                    [
                        ("2", "Validated VIP", true, 200, CatalogChapterAccessState.PurchaseRequired),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("200", "Validated Book", "Author", null),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "200",
            new ChapterCacheEntry(
                "2",
                ["validated full content"],
                IsPreview: false,
                200,
                CatalogChapterAccessState.Accessible,
                VisibleToUserName: "tester",
                VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser),
            CancellationToken.None);

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("200"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(true, "tester"),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Null(predictedOutputPath);
    }

    [Fact]
    public async Task
        DownloadAsyncDoesNotUseValidatedCachedOutputForEarlyOverwriteWhenAnonymousVipConflictRequiresRefresh()
    {
        using TestWorkspace workspace = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "200",
                ("Anonymous Volume", true, [("2", "Maybe VIP", true, 200)]))
            with
            {
                FetchedAtUtc = now,
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "200",
                (
                    "Validated Volume",
                    true,
                    [
                        ("2", "Maybe VIP", false, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("200", "Validated Book", "Author", null),
                FetchedAtUtc = now.AddMinutes(-1),
            },
            CancellationToken.None);

        string? predictedOutputPath =
            await AppCommandService.TryGetCachedOutputPathForOverwriteCheckAsync(
                BookReferenceParser.Parse("200"),
                ResolvedAppSettings.Merge(new AppSettings(), new DownloadCommandOptions()),
                workspace.Paths,
                new LoginState(true, "tester"),
                LoginStateProbeMode.WaitForValidatedIdentity,
                hasRunIdentityUncertainty: false,
                timeProvider: TimeProvider.System,
                cancellationToken: CancellationToken.None);

        Assert.Null(predictedOutputPath);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotSaveChapterCacheWhenReplannedOutputPathIsDenied()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "Stale Anonymous",
                    true,
                    [
                        ("1", "Anonymous Free", false, 100),
                        ("2", "Anonymous VIP", true, 200),
                    ]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Volume",
                    true,
                    [
                        ("1", "Validated Free", false, 100, CatalogChapterAccessState.Accessible),
                        ("2", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "Validated Book", "Author", null),
            },
            CancellationToken.None);
        string validatedOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Validated Book",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(validatedOutputPath)!);
        await File.WriteAllTextAsync(validatedOutputPath, "existing validated output");
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog(
                    "100",
                    (
                        "Fresh Anonymous",
                        true,
                        [
                            ("1", "Anonymous Free", false, 100),
                            ("2", "Anonymous VIP", true, 200),
                        ])),
            ]);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["anonymous free content"], IsPreview: false),
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            appSettings: new AppSettings
            {
                MinimumRequestDelaySeconds = 0.001,
                MaximumRequestDelaySeconds = 0.001,
            });

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Equal("existing validated output", await File.ReadAllTextAsync(validatedOutputPath));
        Assert.Null(
            await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "1",
                CancellationToken.None));
        Assert.Null(
            await CacheStore.GetChapterAsync(
                workspace.Paths.CacheRoot,
                "100",
                "2",
                CancellationToken.None));
        Assert.Contains($"Skipped '{validatedOutputPath}' because overwrite was not approved.", result.StdOut);
    }

    [Fact]
    public async Task DownloadAsyncDoesNotPromptTwiceForSameWindowsOutputPathAfterReplan()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Windows path comparison coverage is only available on Windows.");
        }

        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("100", ("Stale Anonymous", true, [("1", "Anonymous VIP", true, 100)]))
            with
            {
                FetchedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Validated Volume",
                    true,
                    [
                        ("1", "Validated VIP", true, 200, CatalogChapterAccessState.Accessible),
                    ]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
                Metadata = new BookMetadata("100", "book 100", "Author", null),
            },
            CancellationToken.None);
        string anonymousOutputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        Directory.CreateDirectory(Path.GetDirectoryName(anonymousOutputPath)!);
        await File.WriteAllTextAsync(anonymousOutputPath, "existing output");
        FakeBrowserSession isolatedAnonymousSession = new(
            catalogs:
            [
                CreateCatalog("100", ("Fresh Anonymous", true, [("1", "Anonymous VIP", true, 100)])),
            ]);
        FakeBrowserSession headlessSession = new(
            loginStates:
            [
                new LoginState(false, null),
                new LoginState(true, "tester"),
            ],
            chapterFetchResults:
            [
                new ChapterFetchResult(["discarded anonymous full content"], IsPreview: false),
                new ChapterFetchResult(["validated full content"], IsPreview: false),
            ]);
        FakeBrowserManager browserManager = new(isolatedAnonymousSession, headlessSession);
        RecordingInteractiveConsole console = new(response: true);
        AppCommandService service = CreateService(
            workspace,
            browserManager,
            interactiveConsole: console);

        ConsoleCaptureResult result = await WithConsoleCaptureAsync(
            () => service.DownloadAsync(
                new DownloadCommandOptions
                {
                    BookReferences = ["100"],
                },
                CancellationToken.None));

        Assert.Equal(ExitCodes.Success, result.ReturnValue);
        Assert.Single(console.Prompts);
        Assert.Contains("validated full content", await File.ReadAllTextAsync(anonymousOutputPath));
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
                    [("1", "VIP One", true, 100), ("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
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
                "2",
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
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
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP Two", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=0, reused=0, failed=2.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("first full content", markdown);
        Assert.DoesNotContain("second full content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.True(cacheEntry.IsPreview);
        Assert.Contains("cached preview", cacheEntry.Paragraphs);
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
                    [("1", "VIP One", true, 100), ("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
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
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.DoesNotContain("cached preview", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=0, reused=0, failed=2.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        AssertContainsOrderedSubsequence(
            [.. markdown.Split(Environment.NewLine)],
            [AppConstants.FailedChapterPlaceholder, AppConstants.FailedChapterPlaceholder]);
        Assert.DoesNotContain("first full content", markdown);
        Assert.DoesNotContain("second full content", markdown);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.True(cacheEntry.IsPreview);
        Assert.Contains("cached preview", cacheEntry.Paragraphs);
        Assert.Contains(
            logger.Entries,
            entry => entry.EventId.Name
                    == nameof(LogMessages.IgnoreVipFullContentClassificationProbeFailure)
                && entry.Level == LogLevel.Warning
                && entry.Exception?.Message == "probe failed");
    }

    [Fact]
    public async Task
        DownloadAsyncFailsClosedAlreadyRenderedCachedVipPreviewAfterUnvalidatedAuthenticatedProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "VIP Volume",
                    true,
                    [("1", "VIP One", true, 100), ("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "1",
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP One", result.StdOut);
        Assert.DoesNotContain("cached preview", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=0, reused=0, failed=2.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("first full content", markdown);
        Assert.DoesNotContain("second full content", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "1",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.True(cacheEntry.IsPreview);
    }

    [Fact]
    public async Task
        DownloadAsyncFailsClosedLaterCachedVipPreviewAfterUnvalidatedAuthenticatedProbe()
    {
        using TestWorkspace workspace = new();
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog(
                "100",
                (
                    "VIP Volume",
                    true,
                    [("1", "VIP One", true, 100), ("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            new ChapterCacheEntry(
                "2",
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

        Assert.Equal(ExitCodes.OperationalFailure, result.ReturnValue);
        Assert.Equal([true], browserManager.OpenCalls);
        Assert.Equal(
            [LoginStateProbeMode.WaitForValidatedIdentity, LoginStateProbeMode.WaitForValidatedIdentity],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
            ],
            browserManager.Events);
        Assert.Contains("Fetching VIP Two", result.StdOut);
        Assert.DoesNotContain("cached preview", result.StdOut);
        Assert.Contains(
            "Summary: books completed=1, skipped=0, failed=0; "
            + "chapters downloaded=0, reused=0, failed=2.",
            result.StdOut);
        string outputPath = AppPaths.BuildDefaultOutputPath(
            workspace.Paths.OutputRoot,
            "100",
            "Book 100",
            "Author");
        string markdown = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("first full content", markdown);
        Assert.DoesNotContain("second full content", markdown);
        Assert.Contains(AppConstants.FailedChapterPlaceholder, markdown);
        Assert.DoesNotContain("cached preview", markdown);
        ChapterCacheEntry? cacheEntry = await CacheStore.GetChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            "2",
            CancellationToken.None);
        Assert.NotNull(cacheEntry);
        Assert.True(cacheEntry.IsPreview);
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
                    [("1", "VIP One", true, 100), ("2", "VIP Two", true, 200)])),
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalogWithAccessStates(
                "100",
                (
                    "VIP Volume",
                    true,
                    [
                        ("1", "VIP One", true, 100, CatalogChapterAccessState.Accessible),
                        ("2", "VIP Two", true, 200, CatalogChapterAccessState.Accessible),
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
                "1",
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
            [
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
                LoginStateProbeMode.WaitForValidatedIdentity,
            ],
            headlessSession.LoginStateProbeModes);
        Assert.Equal(
            [
                "open:headless",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
                "login-state:headless",
                "fetch-chapter:headless:100:1",
                "login-state:headless",
                "fetch-chapter:headless:100:2",
                "login-state:headless",
            ],
            browserManager.Events);
        Assert.Contains("Reusing VIP One", result.StdOut);
        Assert.Contains("Fetching VIP One", result.StdOut);
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
            "1",
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
            "1",
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
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, catalogWordCount)])),
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
            "1",
            ["cached"],
            IsPreview: false,
            wordCount,
            CatalogChapterAccessState.Accessible,
            CatalogIsVip: false,
            IsAnonymousSafeFullContent: true);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, wordCount)])),
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
            "1",
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP Chapter", true, 100)])),
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
            "1",
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
                        ("1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
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
            "1",
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
                        ("1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
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
            "1",
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
                        ("1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "other"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncDoesNotReuseUnknownFormerVipFullCacheWhenCatalogMarksChapterFree()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.PurchaseRequired);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("1", "Chapter", false, 100)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Changed, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Theory]
    [InlineData(CatalogChapterAccessState.Unknown)]
    [InlineData(CatalogChapterAccessState.PurchaseRequired)]
    public async Task
        BuildChapterPlansAsyncDoesNotReuseKnownFreeOriginFullCacheWithNonAccessibleCachedAccessState(
            object catalogAccessState)
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached suspicious content"],
            IsPreview: false,
            100,
            (CatalogChapterAccessState)catalogAccessState,
            CatalogIsVip: false,
            IsAnonymousSafeFullContent: true);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Free Volume",
                    false,
                    [
                        (
                            "1",
                            "Free Chapter",
                            false,
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
    public async Task
        BuildChapterPlansAsyncDoesNotReuseKnownFreeOriginFullCacheWithUserBoundMetadataWithoutValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached suspicious content"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "alice",
            VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser,
            CatalogIsVip: false);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Free Volume",
                    false,
                    [
                        (
                            "1",
                            "Free Chapter",
                            false,
                            100,
                            CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncReusesKnownFreeOriginFullCacheWithUserBoundMetadataForSameValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached same-user content"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "alice",
            VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser,
            CatalogIsVip: false);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Free Volume",
                    false,
                    [
                        (
                            "1",
                            "Free Chapter",
                            false,
                            100,
                            CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "alice"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
        Assert.NotNull(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncDoesNotReuseLegacyAccessibleFullCacheWhenCatalogMarksChapterFree()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached legacy content"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Free Volume",
                    false,
                    [
                        (
                            "1",
                            "Free Chapter",
                            false,
                            100,
                            CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncDoesNotReuseUnprovenFreeOriginFullCacheWhenCatalogMarksChapterFree()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached unproven free-origin content"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            CatalogIsVip: false);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Free Volume",
                    false,
                    [
                        (
                            "1",
                            "Free Chapter",
                            false,
                            100,
                            CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncReusesProvenAnonymousSafeFreeFullCacheWhenCatalogMarksChapterFree()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached proven anonymous-safe content"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            CatalogIsVip: false,
            IsAnonymousSafeFullContent: true);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalogWithAccessStates(
                "100",
                (
                    "Free Volume",
                    false,
                    [
                        (
                            "1",
                            "Free Chapter",
                            false,
                            100,
                            CatalogChapterAccessState.Accessible),
                    ])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
        Assert.NotNull(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncDoesNotReuseUnknownAccessibleFormerVipFullCacheWhenCatalogMarksChapterFree()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            CatalogIsVip: true);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("1", "Chapter", false, 100)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncDoesNotReuseValidatedUserVipFullCacheWhenCatalogMarksChapterFreeWithoutValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "alice",
            VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("1", "Chapter", false, 100)])),
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.FetchRequired, plans[0].Status);
        Assert.Null(plans[0].CachedEntry);
    }

    [Fact]
    public async Task
        BuildChapterPlansAsyncReusesValidatedUserVipFullCacheWhenCatalogMarksChapterFreeForSameValidatedSession()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
            ["cached"],
            IsPreview: false,
            100,
            CatalogChapterAccessState.Accessible,
            VisibleToUserName: "alice",
            VipFullContentProvenance: VipFullContentCacheProvenance.ValidatedUser);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "100",
            cachedEntry,
            CancellationToken.None);

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("1", "Chapter", false, 100)])),
            workspace.Paths.CacheRoot,
            new LoginState(true, "alice"),
            CancellationToken.None);

        Assert.Single(plans);
        Assert.Equal(ChapterPlanStatus.Cached, plans[0].Status);
        Assert.NotNull(plans[0].CachedEntry);
    }

    [Fact]
    public async Task BuildChapterPlansAsyncDoesNotReuseLegacyVipFullCacheForDifferentUser()
    {
        using TestWorkspace workspace = new();
        ChapterCacheEntry cachedEntry = new(
            "1",
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
                        ("1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
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
            "1",
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
                        ("1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
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
            "1",
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP Chapter", true, 100)])),
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
            "1",
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP Chapter", true, 100)])),
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
            "1",
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
                        ("1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
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
            "1");
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

        List<ChapterPlan> plans = await AppCommandService.BuildChapterPlansAsync(
            CreateCatalog("100", ("Volume", false, [("1", "Chapter 1", false, 100)])),
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
                "1",
                ["cached"],
                IsPreview: false,
                100,
                CatalogChapterAccessState.Accessible,
                CatalogIsVip: false,
                IsAnonymousSafeFullContent: true),
            CancellationToken.None);
        string changedCachePath = AppPaths.GetChapterCachePath(
            workspace.Paths.CacheRoot,
            "100",
            "2");
        Directory.CreateDirectory(Path.GetDirectoryName(changedCachePath)!);
        await File.WriteAllTextAsync(
            changedCachePath,
            """
            {
                "chapterId": "2",
                "paragraphs": ["Probe paragraph"],
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
                    [("1", "Chapter 1", false, 100), ("2", "Chapter 2", false, 201)])),
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
            "1",
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
            CreateCatalog("100", ("VIP Volume", true, [("1", "VIP Chapter", true, 100)])),
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
            "1",
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
                        ("1", "VIP Chapter", true, 100, CatalogChapterAccessState.Accessible),
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
                "1",
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
                            "1",
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
    public async Task
        RequiresValidatedCatalogRefreshForAnonymousVipConflictDetectsNewerAnonymousFreeEvidence()
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot anonymousCatalog = CreateCatalog(
            "100",
            (
                "Mixed Volume",
                true,
                [
                    ("1", "Now Free", false, 100),
                    ("2", "Still VIP", true, 200),
                ]));
        List<ChapterPlan> anonymousPlans = await AppCommandService.BuildChapterPlansAsync(
            anonymousCatalog,
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);
        CatalogSnapshot validatedCatalog = CreateCatalogWithAccessStates(
            "100",
            (
                "Old Validated Volume",
                true,
                [
                    ("1", "Now Free", true, 100, CatalogChapterAccessState.PurchaseRequired),
                    ("2", "Still VIP", true, 200, CatalogChapterAccessState.Accessible),
                ]))
            with
        {
            FetchedAtUtc = anonymousCatalog.FetchedAtUtc.AddMinutes(-1),
            CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
        };

        Assert.True(AppCommandService.RequiresValidatedCatalogRefreshForAnonymousVipConflict(
            anonymousPlans,
            anonymousCatalog,
            validatedCatalog));
    }

    [Fact]
    public async Task
        RequiresValidatedCatalogRefreshForAnonymousVipConflictUsesRawDuplicateAnonymousEvidence()
    {
        using TestWorkspace workspace = new();
        CatalogSnapshot anonymousCatalog = new(
            "100",
            new BookMetadata("100", "Book 100", "Author", null),
            [
                new VolumeDescriptor(
                    "Mixed Volume",
                    true,
                    [
                        new ChapterDescriptor(
                            "1",
                            "Duplicate",
                            "https://www.qidian.com/chapter/100/1/",
                            IsVip: false,
                            CatalogWordCount: 100,
                            CatalogChapterAccessState.Accessible),
                        new ChapterDescriptor(
                            "1",
                            "Duplicate",
                            "https://www.qidian.com/chapter/100/1/",
                            IsVip: true,
                            CatalogWordCount: 100,
                            CatalogChapterAccessState.PurchaseRequired),
                    ]),
            ],
            DateTimeOffset.UtcNow,
            IsKnownAnonymous: true);
        List<ChapterPlan> rawAnonymousPlans = await AppCommandService.BuildChapterPlansAsync(
            anonymousCatalog,
            workspace.Paths.CacheRoot,
            validatedLoginState: null,
            CancellationToken.None);
        CatalogSnapshot validatedCatalog = CreateCatalogWithAccessStates(
            "100",
            (
                "Old Validated Volume",
                true,
                [
                    ("1", "Duplicate", true, 100, CatalogChapterAccessState.PurchaseRequired),
                ]))
            with
        {
            FetchedAtUtc = anonymousCatalog.FetchedAtUtc.AddMinutes(-1),
            CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
        };

        Assert.True(AppCommandService.RequiresValidatedCatalogRefreshForAnonymousVipConflict(
            rawAnonymousPlans,
            anonymousCatalog,
            validatedCatalog));
    }

    [Fact]
    public void SelectCachedLoginStateForProbeUsesLatestProbeToAvoidStaleValidatedIdentity()
    {
        LoginState validatedLoginState = new(true, "tester");

        Assert.Equal(
            new LoginState(false, null),
            AppCommandService.SelectCachedLoginStateForProbe(
                validatedLoginState,
                new LoginState(false, null)));
        Assert.Equal(
            new LoginState(true, null),
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
            "1",
            "VIP Chapter",
            "https://www.qidian.com/chapter/100/1/",
            IsVip: true,
            CatalogWordCount: 100,
            CatalogChapterAccessState.Accessible);
        ChapterCacheEntry cacheEntry = new(
            "1",
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
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
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
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
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
        AppSettings? appSettings = null,
        IInteractiveConsole? interactiveConsole = null)
        => new(
            Options.Create(appSettings ?? new AppSettings()),
            browserManager,
            interactiveConsole ?? new FakeInteractiveConsole(),
            TimeProvider.System,
            storageService ?? workspace.CreateStorageService(),
            logger ?? NullLogger<AppCommandService>.Instance);

    private static string GetAnonymousCatalogCachePath(TestWorkspace workspace, string bookId)
        => AppPaths.GetCatalogCachePath(
            workspace.Paths.CacheRoot,
            bookId,
            CatalogCacheScope.Anonymous);

    private static void AssertAnonymousCatalogCacheFileDoesNotExist(
        TestWorkspace workspace,
        string bookId)
        => Assert.False(File.Exists(GetAnonymousCatalogCachePath(workspace, bookId)));

    private static async Task SaveValidatedIdentityPrimerAsync(TestWorkspace workspace)
    {
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("99", ("Primer Volume", true, [("1", "Primer VIP", true, 100)]))
            with
            {
                IsKnownAnonymous = true,
            },
            CancellationToken.None);
        await CacheStore.SaveCatalogAsync(
            workspace.Paths.CacheRoot,
            CreateCatalog("99", ("Primer Volume", true, [("1", "Primer VIP", true, 100)]))
            with
            {
                CacheScope = CatalogCacheScope.ForValidatedUser("tester"),
            },
            CancellationToken.None);
        await CacheStore.SaveChapterAsync(
            workspace.Paths.CacheRoot,
            "99",
            new ChapterCacheEntry(
                "1",
                ["cached preview", AppConstants.TruncatedChapterMarker],
                IsPreview: true,
                100,
                CatalogChapterAccessState.PurchaseRequired),
            CancellationToken.None);
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;
        while ((startIndex = text.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

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

    [Fact]
    public async Task FakeBrowserSessionFetchCatalogAsyncRejectsCatalogWhenTopLevelBookIdDoesNotMatchRequestedBookId()
    {
        FakeBrowserSession session = new(
            catalogs:
            [
                CreateCatalog("100") with { BookId = "200" },
            ])
        {
            Manager = new FakeBrowserManager(),
            SessionKind = "test",
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.FetchCatalogAsync("100", CancellationToken.None));

        Assert.Contains("200", exception.Message);
    }

    [Fact]
    public async Task FakeBrowserSessionFetchCatalogAsyncReturnsQueuedCatalogAsUnprovenAnonymousByDefault()
    {
        FakeBrowserSession session = new(
            catalogs:
            [
                CreateCatalog("100"),
            ])
        {
            Manager = new FakeBrowserManager(),
            SessionKind = "test",
        };

        CatalogSnapshot catalog = await session.FetchCatalogAsync("100", CancellationToken.None);

        Assert.False(catalog.IsKnownAnonymous);
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
            DateTimeOffset.UtcNow,
            IsKnownAnonymous: true);

    private static CatalogSnapshot CreateCatalogWithUrls(
        string bookId,
        params (
            string VolumeTitle,
            bool IsVip,
            (
                string ChapterId,
                string Title,
                string Url,
                bool IsVip,
                int? WordCount)[] Chapters)[] volumes)
        => new(
            bookId,
            new BookMetadata(bookId, $"Book {bookId}", "Author", null),
            volumes.Select(volume => new VolumeDescriptor(
                volume.VolumeTitle,
                volume.IsVip,
                volume.Chapters.Select(chapter => new ChapterDescriptor(
                    chapter.ChapterId,
                    chapter.Title,
                    chapter.Url,
                    chapter.IsVip,
                    chapter.WordCount,
                    chapter.IsVip
                        ? CatalogChapterAccessState.PurchaseRequired
                        : CatalogChapterAccessState.Accessible)).ToArray())).ToArray(),
            DateTimeOffset.UtcNow,
            IsKnownAnonymous: true);

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
            DateTimeOffset.UtcNow,
            IsKnownAnonymous: true);

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
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
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
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
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

    private sealed class RecordingInteractiveConsole(bool response) : IInteractiveConsole
    {
        public List<string> Prompts { get; } = [];

        public Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(response);
        }
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
        Action<string, string>? chapterFetchAction = null,
        Exception? disposeException = null,
        Exception? waitForManualLoginException = null,
        Task? disposeTask = null,
        bool emulateValidatedIdentityPolling = false,
        bool validateCatalogBookIds = true) : IQidianBrowserSession
    {
        private readonly Queue<LoginState> loginStates = new(loginStates ?? []);
        private readonly Queue<CatalogSnapshot> catalogs = new(catalogs ?? []);
        private readonly Queue<ChapterFetchResult> chapterFetchResults =
            new(chapterFetchResults ?? []);
        private readonly Exception? loginStateException = loginStateException;
        private readonly Queue<Exception?> loginStateExceptions = new(loginStateExceptions ?? []);
        private readonly Action<int>? loginStateAction = loginStateAction;
        private readonly Action<string>? catalogFetchAction = catalogFetchAction;
        private readonly Action<string, string>? chapterFetchAction = chapterFetchAction;
        private readonly Exception? disposeException = disposeException;
        private readonly Exception? waitForManualLoginException = waitForManualLoginException;
        private readonly Task? disposeTask = disposeTask;
        private readonly bool emulateValidatedIdentityPolling = emulateValidatedIdentityPolling;
        private readonly bool validateCatalogBookIds = validateCatalogBookIds;
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

            if (validateCatalogBookIds
                && (!StringComparer.Ordinal.Equals(catalog.BookId, bookId)
                    || !StringComparer.Ordinal.Equals(catalog.Metadata.BookId, bookId)))
            {
                throw new InvalidOperationException(
                    $"Queued fake catalog book ids '{catalog.BookId}'/'{catalog.Metadata.BookId}' did not match requested book id '{bookId}'.");
            }

            return Task.FromResult(catalog with { IsKnownAnonymous = false });
        }

        public Task<ChapterFetchResult> FetchChapterAsync(
            string bookId,
            ChapterDescriptor chapter,
            CancellationToken cancellationToken)
        {
            Manager!.Events.Add($"fetch-chapter:{SessionKind}:{bookId}:{chapter.ChapterId}");
            chapterFetchAction?.Invoke(bookId, chapter.ChapterId);
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
