using System.Reflection;
using System.Text.Json;
using Hcoona.QidianNovelDownloader.Browser;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class QidianBrowserSessionTests
{
    private const string ChapterJson =
        """
        {
            "pageUrl": "https://www.qidian.com/chapter/100/1/",
            "contentSelector": "span.content-text",
            "paragraphs": ["Paragraph 1"],
            "isPreview": false
        }
        """;

    [Fact]
    public async Task GetLoginStateAsyncTreatsInitialAnonymousSampleAsProvisional()
    {
        FakePage page = new(
            new LoginState(false, null),
            new LoginState(true, "tester"));
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            AppConstants.QidianBaseUrl,
            CancellationToken.None);

        Assert.True(loginState.IsValidated);
        Assert.Equal("tester", loginState.UserName);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(2, page.EvaluateLoginStateCalls);
        Assert.Equal(1, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task GetLoginStateAsyncReturnsAnonymousOnlyAfterPollingBudgetExpires()
    {
        FakePage page = new(new LoginState(false, null));
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            AppConstants.QidianBaseUrl,
            CancellationToken.None);

        Assert.False(loginState.IsLoggedIn);
        Assert.Null(loginState.UserName);
        Assert.Equal(QidianBrowserSession.LoginStateProbeAttempts, page.EvaluateLoginStateCalls);
        Assert.Equal(QidianBrowserSession.LoginStateProbeAttempts - 1, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task GetLoginStateAsyncMarksValidatedIdentityProbeIncompleteWhenPageCloses()
    {
        FakePage page = new(new LoginState(false, null));
        page.OnEvaluate = () => page.IsClosed = true;
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            null,
            CancellationToken.None,
            navigate: false);

        Assert.False(loginState.IsLoggedIn);
        Assert.False(loginState.IsProbeComplete);
        Assert.Equal(1, page.EvaluateLoginStateCalls);
        Assert.Equal(0, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task GetLoginStateAsyncReturnsIncompleteWhenPageClosesDuringNavigation()
    {
        FakePage page = new(new LoginState(false, null))
        {
            GotoTask = Task.FromException<IResponse>(
                new PlaywrightException("Target page has been closed.")),
        };
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            AppConstants.QidianBaseUrl,
            CancellationToken.None);

        Assert.False(loginState.IsLoggedIn);
        Assert.False(loginState.IsProbeComplete);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(0, page.EvaluateLoginStateCalls);
    }

    [Fact]
    public async Task GetLoginStateAsyncReturnsIncompleteWhenPageClosesDuringEvaluation()
    {
        FakePage page = new(new LoginState(false, null))
        {
            EvaluateTask = Task.FromException<string>(
                new PlaywrightException("Target page has been closed.")),
        };
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            null,
            CancellationToken.None,
            navigate: false);

        Assert.False(loginState.IsLoggedIn);
        Assert.False(loginState.IsProbeComplete);
        Assert.Equal(0, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task GetLoginStateAsyncReturnsIncompleteWhenPageClosesDuringProbeDelay()
    {
        FakePage page = new(new LoginState(false, null))
        {
            WaitForTimeoutTask = Task.FromException(
                new PlaywrightException("Target page has been closed.")),
        };
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            null,
            CancellationToken.None,
            navigate: false);

        Assert.False(loginState.IsLoggedIn);
        Assert.False(loginState.IsProbeComplete);
        Assert.Equal(1, page.EvaluateLoginStateCalls);
        Assert.Equal(1, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task GetLoginStateAsyncMarksCurrentStateProbeIncompleteForAlreadyClosedPage()
    {
        FakePage page = new(new LoginState(true, "tester"))
        {
            IsClosed = true,
        };
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            null,
            CancellationToken.None,
            navigate: false,
            probeMode: LoginStateProbeMode.CurrentStateOnly);

        Assert.False(loginState.IsLoggedIn);
        Assert.False(loginState.IsProbeComplete);
        Assert.False(loginState.IsValidated);
        Assert.Equal(0, page.EvaluateLoginStateCalls);
    }

    [Fact]
    public async Task GetLoginStateAsyncMarksCurrentStateProbeIncompleteWhenPageClosesAfterEvaluation()
    {
        FakePage page = new(new LoginState(true, "tester"));
        page.OnEvaluate = () => page.IsClosed = true;
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.GetLoginStateAsync(
            null,
            CancellationToken.None,
            navigate: false,
            probeMode: LoginStateProbeMode.CurrentStateOnly);

        Assert.True(loginState.IsLoggedIn);
        Assert.Equal("tester", loginState.UserName);
        Assert.False(loginState.IsProbeComplete);
        Assert.False(loginState.IsValidated);
        Assert.Equal(1, page.EvaluateLoginStateCalls);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncSucceedsOnceSessionIsAuthenticated()
    {
        FakePage page = new(
            new LoginState(false, null),
            new LoginState(true, null));
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.WaitForManualLoginAsync(CancellationToken.None);

        Assert.True(loginState.IsLoggedIn);
        Assert.False(loginState.IsValidated);
        Assert.Null(loginState.UserName);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(2, page.EvaluateLoginStateCalls);
        Assert.Equal(1, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncRequiresValidatedIdentityWhenRequested()
    {
        FakePage page = new(
            new LoginState(false, null),
            new LoginState(true, null),
            new LoginState(true, "tester"));
        QidianBrowserSession session = CreateSession(page.Page);

        LoginState loginState = await session.WaitForManualLoginAsync(
            CancellationToken.None,
            requireValidatedIdentity: true);

        Assert.True(loginState.IsValidated);
        Assert.Equal("tester", loginState.UserName);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(3, page.EvaluateLoginStateCalls);
        Assert.Equal(2, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncDoesNotNavigateWhenAlreadyCanceled()
    {
        FakePage page = new(new LoginState(true, "tester"))
        {
            GotoTask = Task.FromException<IResponse>(
                new PlaywrightException("Navigation should not start.")),
        };
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        OperationCanceledException exception =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => session.WaitForManualLoginAsync(cancellationTokenSource.Token)
                    .WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(cancellationTokenSource.Token, exception.CancellationToken);
        Assert.Null(page.LastNavigatedUrl);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncTranslatesClosedPageDuringInitialNavigation()
    {
        FakePage page = new(new LoginState(false, null))
        {
            GotoTask = Task.FromException<IResponse>(
                new PlaywrightException("Target page has been closed.")),
        };
        page.OnGoto = () => page.IsClosed = true;
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.WaitForManualLoginAsync(CancellationToken.None));

        Assert.IsType<PlaywrightException>(exception.InnerException);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(0, page.EvaluateLoginStateCalls);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncTranslatesClosedPageDuringLoginProbe()
    {
        FakePage page = new(new LoginState(false, null))
        {
            EvaluateTask = Task.FromException<string>(
                new PlaywrightException("Target page has been closed.")),
        };
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.WaitForManualLoginAsync(CancellationToken.None));

        Assert.IsType<PlaywrightException>(exception.InnerException);
        Assert.False(page.IsClosed);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(0, page.WaitForLoadStateCalls);
        Assert.Equal(0, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncTranslatesClosedPageDuringPollingDelay()
    {
        FakePage page = new(new LoginState(false, null))
        {
            WaitForTimeoutTask = Task.FromException(
                new PlaywrightException("Target page has been closed.")),
        };
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.WaitForManualLoginAsync(CancellationToken.None));

        Assert.IsType<PlaywrightException>(exception.InnerException);
        Assert.False(page.IsClosed);
        Assert.Equal(AppConstants.QidianBaseUrl, page.LastNavigatedUrl);
        Assert.Equal(1, page.EvaluateLoginStateCalls);
        Assert.Equal(1, page.WaitForTimeoutCalls);
    }

    [Fact]
    public async Task FetchCatalogAsyncParsesCatalogChapterAccessState()
    {
        FakePage page = new(
            catalogJson:
            """
            {
                "title": "Book Title",
                "author": "Author",
                "estimatedWordCount": 123,
                "volumes": [
                    {
                        "title": "VIP Volume",
                        "isVip": true,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": "Purchase Required Chapter",
                                "url": "https://www.qidian.com/chapter/100/1/",
                                "isVip": true,
                                "catalogWordCount": 100,
                                "catalogAccessState": "PurchaseRequired"
                            },
                            {
                                "chapterId": "2",
                                "title": "Accessible Chapter",
                                "url": "https://www.qidian.com/chapter/100/2/",
                                "isVip": true,
                                "catalogWordCount": 101,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ]
            }
            """);
        QidianBrowserSession session = CreateSession(page.Page);

        CatalogSnapshot catalog = await session.FetchCatalogAsync("100", CancellationToken.None);

        Assert.Equal("Book Title", catalog.Metadata.Title);
        Assert.Equal(
            CatalogChapterAccessState.PurchaseRequired,
            catalog.Volumes[0].Chapters[0].CatalogAccessState);
        Assert.Equal(
            CatalogChapterAccessState.Accessible,
            catalog.Volumes[0].Chapters[1].CatalogAccessState);
    }

    [Fact]
    public async Task FetchCatalogAsyncUsesFetchedPageBookIdFromCurrentUrl()
    {
        FakePage page = new(
            catalogJson:
            """
            {
                "title": "Redirected Book",
                "author": "Author",
                "volumes": [
                    {
                        "title": "Volume",
                        "isVip": false,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": "Chapter",
                                "url": "https://www.qidian.com/chapter/200/1/",
                                "isVip": false,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ]
            }
            """)
        {
            RedirectedUrl = "https://www.qidian.com/book/200/catalog/",
        };
        QidianBrowserSession session = CreateSession(page.Page);

        CatalogSnapshot catalog = await session.FetchCatalogAsync("100", CancellationToken.None);

        Assert.Equal("200", catalog.BookId);
        Assert.Equal("200", catalog.Metadata.BookId);
    }

    [Fact]
    public async Task FetchCatalogAsyncPrefersCatalogJsonBookIdOverFallbackUrl()
    {
        FakePage page = new(
            catalogJson:
            """
            {
                "bookId": "300",
                "title": "Script Book",
                "author": "Author",
                "volumes": [
                    {
                        "title": "Volume",
                        "isVip": false,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": "Chapter",
                                "url": "https://www.qidian.com/chapter/300/1/",
                                "isVip": false,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ]
            }
            """)
        {
            RedirectedUrl = "https://www.qidian.com/book/999/catalog/",
        };
        QidianBrowserSession session = CreateSession(page.Page);

        CatalogSnapshot catalog = await session.FetchCatalogAsync("100", CancellationToken.None);

        Assert.Equal("300", catalog.BookId);
        Assert.Equal("300", catalog.Metadata.BookId);
        Assert.Equal("Script Book", catalog.Metadata.Title);
    }

    [Fact]
    public async Task FetchCatalogAsyncFailsClosedWhenFetchedPageBookIdCannotBeDetermined()
    {
        FakePage page = new(
            catalogJson:
            """
            {
                "title": "Unknown Book",
                "author": "Author",
                "volumes": [
                    {
                        "title": "Volume",
                        "isVip": false,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": "Chapter",
                                "url": "https://www.qidian.com/chapter/100/1/",
                                "isVip": false,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ]
            }
            """)
        {
            RedirectedUrl = "https://www.qidian.com/",
        };
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchCatalogAsync("100", CancellationToken.None));

        Assert.Contains("book id could not be determined", exception.Message);
    }

    [Theory]
    [InlineData("https://www.qidian.com:444/book/200/catalog/")]
    [InlineData("https://qidian.com:444/book/200/catalog/")]
    [InlineData("https://www.qidian.com/Book/200/catalog/")]
    [InlineData("https://www.qidian.com?next=/book/200/catalog/")]
    [InlineData("https://www.qidian.com#/book/200/catalog/")]
    [InlineData("https://www.qidian.com/book/200/catalog/?from=app")]
    [InlineData("https://www.qidian.com/book/200/catalog/#content")]
    [InlineData("https://www.qidian.com/book/%32%30%30/catalog/")]
    [InlineData("https://www.qidian.com/book/２００/catalog/")]
    [InlineData("https://@www.qidian.com/book/200/catalog/")]
    [InlineData("https://attacker@www.qidian.com/book/200/catalog/")]
    [InlineData("https://attacker:password@www.qidian.com/book/200/catalog/")]
    public async Task FetchCatalogAsyncRejectsNonCanonicalFallbackBookUrls(string redirectedUrl)
    {
        FakePage page = new(
            catalogJson:
            """
            {
                "title": "Redirected Book",
                "author": "Author",
                "volumes": [
                    {
                        "title": "Volume",
                        "isVip": false,
                        "chapters": [
                            {
                                "chapterId": "1",
                                "title": "Chapter",
                                "url": "https://www.qidian.com/chapter/200/1/",
                                "isVip": false,
                                "catalogAccessState": "Accessible"
                            }
                        ]
                    }
                ]
            }
            """)
        {
            RedirectedUrl = redirectedUrl,
        };
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchCatalogAsync("100", CancellationToken.None));

        Assert.Contains("book id could not be determined", exception.Message);
    }

    [Fact]
    public async Task GetLoginStateAsyncHonorsCancellationDuringNavigation()
    {
        TaskCompletionSource<IResponse> navigation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakePage page = new(new LoginState(true, "tester"))
        {
            GotoTask = navigation.Task,
        };
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<LoginState> loginState = session.GetLoginStateAsync(
            AppConstants.QidianBaseUrl,
            cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loginState.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task GetLoginStateAsyncHonorsCancellationDuringProbeDelay()
    {
        TaskCompletionSource delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakePage page = new(new LoginState(false, null))
        {
            WaitForTimeoutTask = delay.Task,
        };
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<LoginState> loginState = session.GetLoginStateAsync(
            null,
            cancellationTokenSource.Token,
            navigate: false);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loginState.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task FetchCatalogAsyncHonorsCancellationDuringEvaluation()
    {
        TaskCompletionSource<string> evaluation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakePage page = new(catalogJson: null)
        {
            EvaluateTask = evaluation.Task,
        };
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<CatalogSnapshot> fetch = session.FetchCatalogAsync(
            "100",
            cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetch.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task FetchCatalogAsyncTreatsBrowserOperationCanceledExceptionAsOperationalFailure()
    {
        FakePage page = new(catalogJson: null)
        {
            EvaluateTask = Task.FromException<string>(
                new OperationCanceledException("Browser canceled evaluation.")),
        };
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchCatalogAsync("100", CancellationToken.None));

        Assert.IsType<OperationCanceledException>(exception.InnerException);
    }

    [Theory]
    [InlineData("1?x=")]
    [InlineData("1%2F2")]
    [InlineData("1/2")]
    public async Task FetchChapterAsyncRejectsUnsafeChapterIdBeforeNavigation(string chapterId)
    {
        FakePage page = new(
            chapterJson: ChapterJson,
            gotoTask: Task.FromException<IResponse>(
                new InvalidOperationException("Navigation should not start.")),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null));
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchChapterAsync("100", Chapter(chapterId), CancellationToken.None));

        Assert.Contains("safe canonical Qidian chapter URL", exception.Message);
        Assert.Null(page.LastNavigatedUrl);
        Assert.Equal(0, page.EvaluateChapterContentCalls);
    }

    [Fact]
    public async Task FetchChapterAsyncHonorsCancellationDuringNavigation()
    {
        TaskCompletionSource<IResponse> navigation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakePage page = new(
            chapterJson: ChapterJson,
            gotoTask: navigation.Task,
            waitForSelectorTask: null);
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<ChapterFetchResult> fetch = session.FetchChapterAsync(
            "100",
            Chapter("1"),
            cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetch.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task FetchChapterAsyncHonorsCancellationDuringSelectorWait()
    {
        TaskCompletionSource<IElementHandle?> selector = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        FakePage page = new(
            chapterJson: ChapterJson,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: selector.Task);
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<ChapterFetchResult> fetch = session.FetchChapterAsync(
            "100",
            Chapter("1"),
            cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fetch.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task FetchChapterAsyncIgnoresCatalogUrlWhenEmbeddedBookIdDoesNotMatch()
    {
        FakePage page = new(
            chapterJson: ChapterJson,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null));
        QidianBrowserSession session = CreateSession(page.Page);
        ChapterDescriptor chapter = Chapter("1") with
        {
            Url = "https://www.qidian.com/chapter/200/1/",
        };

        await session.FetchChapterAsync("100", chapter, CancellationToken.None);

        Assert.Equal("https://www.qidian.com/chapter/100/1/", page.LastNavigatedUrl);
    }

    [Theory]
    [InlineData("https://evil.example/chapter/100/1/")]
    [InlineData("https://www.qidian.com/book/100/catalog/")]
    [InlineData("https://www.qidian.com/chapter/100/2/")]
    [InlineData("https://www.qidian.com:444/chapter/100/1/")]
    [InlineData("https://qidian.com:444/chapter/100/1/")]
    [InlineData("https://www.qidian.com/Chapter/100/1/")]
    [InlineData("https://www.qidian.com?next=/chapter/100/1/")]
    [InlineData("https://www.qidian.com#/chapter/100/1/")]
    [InlineData("https://www.qidian.com/chapter/100/1/?from=app")]
    [InlineData("https://www.qidian.com/chapter/100/1/#content")]
    [InlineData("https://www.qidian.com/chapter/100/%31/")]
    [InlineData("https://www.qidian.com/chapter/%31%30%30/1/")]
    [InlineData("\u0001https://@www.qidian.com/chapter/100/1/")]
    [InlineData("\u0001https:////www.qidian.com/chapter/100/1/")]
    [InlineData("https:////www.qidian.com/chapter/100/1/")]
    [InlineData("///www.qidian.com/chapter/100/1/")]
    [InlineData("////www.qidian.com/chapter/100/1/")]
    [InlineData("https://attacker@www.qidian.com/chapter/100/1/")]
    [InlineData("https://attacker:password@www.qidian.com/chapter/100/1/")]
    public async Task FetchChapterAsyncIgnoresUnsafeOrWrongChapterCatalogUrls(string catalogUrl)
    {
        FakePage page = new(
            chapterJson: ChapterJson,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null));
        QidianBrowserSession session = CreateSession(page.Page);
        ChapterDescriptor chapter = Chapter("1") with
        {
            Url = catalogUrl,
        };

        await session.FetchChapterAsync("100", chapter, CancellationToken.None);

        Assert.Equal("https://www.qidian.com/chapter/100/1/", page.LastNavigatedUrl);
    }

    [Theory]
    [InlineData(" https://www.qidian.com/chapter/100/1/ ")]
    [InlineData("https://www.qidian.com:443/chapter/100/1/")]
    [InlineData("https://qidian.com/chapter/100/1/")]
    public async Task FetchChapterAsyncNavigatesCanonicalUrlForUsableCatalogUrl(string catalogUrl)
    {
        FakePage page = new(
            chapterJson: ChapterJson,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null));
        QidianBrowserSession session = CreateSession(page.Page);
        ChapterDescriptor chapter = Chapter("1") with
        {
            Url = catalogUrl,
        };

        await session.FetchChapterAsync("100", chapter, CancellationToken.None);

        Assert.Equal("https://www.qidian.com/chapter/100/1/", page.LastNavigatedUrl);
    }

    [Theory]
    [InlineData("https://www.qidian.com/chapter/100/2/")]
    [InlineData("https://www.qidian.com/chapter/200/1/")]
    public async Task FetchChapterAsyncRejectsMismatchedFinalUrlBeforeExtractingContent(
        string redirectedUrl)
    {
        FakePage page = new(
            chapterJson: ChapterJson,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null))
        {
            RedirectedUrl = redirectedUrl,
        };
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchChapterAsync("100", Chapter("1"), CancellationToken.None));

        Assert.Contains("did not match requested chapter URL", exception.Message);
        Assert.Equal("https://www.qidian.com/chapter/100/1/", page.LastNavigatedUrl);
        Assert.Equal(0, page.EvaluateChapterContentCalls);
    }

    [Fact]
    public async Task FetchChapterAsyncRejectsUrlChangedAfterInitialValidationBeforeExtraction()
    {
        FakePage page = new(
            chapterJson: ChapterJson,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null));
        page.OnWaitForSelector =
            () => page.CurrentUrl = "https://www.qidian.com/chapter/100/2/";
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchChapterAsync("100", Chapter("1"), CancellationToken.None));

        Assert.Contains("did not match requested chapter URL", exception.Message);
        Assert.Equal("https://www.qidian.com/chapter/100/1/", page.LastNavigatedUrl);
        Assert.Equal(0, page.EvaluateChapterContentCalls);
    }

    [Theory]
    [InlineData(
        """
        {
            "pageUrl": "https://www.qidian.com/chapter/100/2/",
            "contentSelector": "span.content-text",
            "paragraphs": ["Wrong chapter"],
            "isPreview": false
        }
        """)]
    [InlineData(
        """
        {
            "contentSelector": "span.content-text",
            "paragraphs": ["Missing page URL"],
            "isPreview": false
        }
        """)]
    public async Task FetchChapterAsyncRejectsScriptReturnedPageUrlMismatch(string chapterJson)
    {
        FakePage page = new(
            chapterJson,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null));
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchChapterAsync("100", Chapter("1"), CancellationToken.None));

        Assert.Contains("did not match requested chapter URL", exception.Message);
        Assert.Equal("https://www.qidian.com/chapter/100/1/", page.CurrentUrl);
        Assert.Equal(1, page.EvaluateChapterContentCalls);
    }

    [Fact]
    public async Task FetchChapterAsyncRejectsGenericSameUrlChapterContent()
    {
        FakePage page = new(
            chapterJson:
            """
            {
                "pageUrl": "https://www.qidian.com/chapter/100/1/",
                "contentSelector": "main p",
                "paragraphs": ["error/login/captcha"],
                "isPreview": false
            }
            """,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null));
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchChapterAsync("100", Chapter("1"), CancellationToken.None));

        Assert.Contains("recognized Qidian chapter content container", exception.Message);
        Assert.Equal("https://www.qidian.com/chapter/100/1/", page.CurrentUrl);
        Assert.Equal(1, page.EvaluateChapterContentCalls);
    }

    [Theory]
    [InlineData("span.content-text", "请登录")]
    [InlineData("span.content-text", "请登录后继续阅读")]
    [InlineData(".read-content p", "captcha")]
    [InlineData(".chapter-content p", "access denied")]
    [InlineData("#j_chapterContent p", "interstitial")]
    public async Task FetchChapterAsyncRejectsMarkerTextInRecognizedContentContainer(
        string contentSelector,
        string markerText)
    {
        FakePage page = new(
            chapterJson:
            $$"""
            {
                "pageUrl": "https://www.qidian.com/chapter/100/1/",
                "contentSelector": "{{contentSelector}}",
                "paragraphs": ["{{markerText}}"],
                "isPreview": false
            }
            """,
            gotoTask: Task.FromResult<IResponse>(null!),
            waitForSelectorTask: Task.FromResult<IElementHandle?>(null));
        QidianBrowserSession session = CreateSession(page.Page);

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.FetchChapterAsync("100", Chapter("1"), CancellationToken.None));

        Assert.Contains("login, captcha, error, or interstitial marker text", exception.Message);
        Assert.Equal("https://www.qidian.com/chapter/100/1/", page.CurrentUrl);
        Assert.Equal(1, page.EvaluateChapterContentCalls);
    }

    [Fact]
    public async Task WaitForManualLoginAsyncHonorsCancellationDuringPollingDelay()
    {
        TaskCompletionSource delay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakePage page = new(new LoginState(false, null))
        {
            WaitForTimeoutTask = delay.Task,
        };
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<LoginState> loginState = session.WaitForManualLoginAsync(
            cancellationTokenSource.Token);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loginState.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitForManualLoginAsyncHonorsCancellationDuringNavigationRecovery()
    {
        TaskCompletionSource loadState = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakePage page = new(new LoginState(false, null))
        {
            EvaluateTask = Task.FromException<string>(
                new PlaywrightException("Execution context was destroyed.")),
            WaitForLoadStateTask = loadState.Task,
        };
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();

        Task<LoginState> loginState = session.WaitForManualLoginAsync(
            cancellationTokenSource.Token);
        await WaitUntilAsync(() => page.WaitForLoadStateCalls > 0);
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loginState.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitForManualLoginAsyncPrefersCancellationWhenRecoveryFaultIsSwallowed()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        FakePage page = new(new LoginState(false, null))
        {
            EvaluateTask = Task.FromException<string>(
                new PlaywrightException("Execution context was destroyed.")),
            WaitForLoadStateTask = Task.FromException(
                new PlaywrightException("Navigation interrupted.")),
            OnWaitForLoadState = cancellationTokenSource.Cancel,
        };
        QidianBrowserSession session = CreateSession(page.Page);

        Task<LoginState> loginState = session.WaitForManualLoginAsync(
            cancellationTokenSource.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => loginState.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitForManualLoginAsyncPrefersCancellationBeforeClosedWindowFailure()
    {
        FakePage page = new(new LoginState(false, null))
        {
            IsClosed = true,
        };
        QidianBrowserSession session = CreateSession(page.Page);
        using CancellationTokenSource cancellationTokenSource = new();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.WaitForManualLoginAsync(cancellationTokenSource.Token)
                .WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitForManualLoginAsyncPrefersCancellationWhenPageClosesDuringLoginProbe()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        FakePage page = new(new LoginState(false, null))
        {
            EvaluateTask = Task.FromException<string>(
                new PlaywrightException("Target page has been closed.")),
        };
        page.OnEvaluate = () =>
        {
            page.IsClosed = true;
            cancellationTokenSource.Cancel();
        };
        QidianBrowserSession session = CreateSession(page.Page);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.WaitForManualLoginAsync(cancellationTokenSource.Token)
                .WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task WaitForManualLoginAsyncPrefersCancellationForAlreadyFaultedLoginProbe()
    {
        using CancellationTokenSource cancellationTokenSource = new();
        FakePage page = new(new LoginState(false, null))
        {
            EvaluateTask = Task.FromException<string>(
                new PlaywrightException("Target page has been closed.")),
        };
        QidianBrowserSession session = CreateSession(page.Page);

        try
        {
            QidianBrowserSession.BeforeCompletedTaskFaultCancellationCheckForTests =
                cancellationTokenSource.Cancel;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => session.WaitForManualLoginAsync(cancellationTokenSource.Token)
                    .WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            QidianBrowserSession.BeforeCompletedTaskFaultCancellationCheckForTests = null;
        }
    }

    [Fact]
    public async Task DisposeAsyncAttemptsAfterDisposeWhenPlaywrightDisposeThrows()
    {
        FakePage page = new(new LoginState(false, null));
        InvalidOperationException disposeException = new("dispose failed");
        bool afterDisposeCalled = false;
        QidianBrowserSession session = CreateSession(
            page.Page,
            playwrightDisposeException: disposeException,
            afterDisposeAsync: () =>
            {
                afterDisposeCalled = true;
                return Task.CompletedTask;
            });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.DisposeAsync().AsTask());

        Assert.Same(disposeException, exception);
        Assert.True(afterDisposeCalled);
    }

    [Fact]
    public async Task PersistSessionStateAsyncPreservesCloseFailureWhenCleanupAlsoThrows()
    {
        FakePage page = new(new LoginState(false, null));
        InvalidOperationException closeException = new("close failed");
        InvalidOperationException disposeException = new("dispose failed");
        bool afterDisposeCalled = false;
        QidianBrowserSession session = CreateSession(
            page.Page,
            playwrightDisposeException: disposeException,
            closeHandler: () => Task.FromException(closeException),
            afterDisposeAsync: () =>
            {
                afterDisposeCalled = true;
                throw new InvalidOperationException("after dispose failed");
            });

        OperationalException exception = await Assert.ThrowsAsync<OperationalException>(
            () => session.PersistSessionStateAsync());

        Assert.Equal("Failed to persist browser session state.", exception.Message);
        Assert.Same(closeException, exception.InnerException);
        Assert.True(afterDisposeCalled);
    }

    [Fact]
    public async Task DisposeBestEffortAsyncDisposesPlaywrightAndRunsCleanupWhenContextCloseHangs()
    {
        FakePage page = new(new LoginState(false, null));
        TaskCompletionSource closeAttempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource closeCanComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool playwrightDisposed = false;
        bool afterDisposeCalled = false;
        QidianBrowserSession session = CreateSession(
            page.Page,
            playwrightDisposeAction: () => playwrightDisposed = true,
            closeHandler: () =>
            {
                closeAttempted.SetResult();
                return closeCanComplete.Task;
            },
            afterDisposeAsync: () =>
            {
                afterDisposeCalled = true;
                return Task.CompletedTask;
            });

        await session.DisposeBestEffortAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        await closeAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(playwrightDisposed);
        Assert.True(afterDisposeCalled);
    }

    [Fact]
    public async Task DisposeBestEffortAsyncWaitsForContextCloseBeforeDisposingPlaywrightWhenCloseCompletes()
    {
        FakePage page = new(new LoginState(false, null));
        bool contextClosed = false;
        bool playwrightDisposedAfterContextClose = false;
        QidianBrowserSession session = CreateSession(
            page.Page,
            playwrightDisposeAction: () => playwrightDisposedAfterContextClose = contextClosed,
            closeHandler: () =>
            {
                contextClosed = true;
                return Task.CompletedTask;
            });

        await session.DisposeBestEffortAsync();

        Assert.True(playwrightDisposedAfterContextClose);
    }

    private static QidianBrowserSession CreateSession(
        IPage page,
        Exception? playwrightDisposeException = null,
        Action? playwrightDisposeAction = null,
        Func<Task>? closeHandler = null,
        Func<Task>? afterDisposeAsync = null)
        => new(
            NullLogger.Instance,
            CreateProxy<IPlaywright>((method, arguments) => method.Name switch
            {
                nameof(IDisposable.Dispose) => DisposePlaywright(
                    playwrightDisposeException,
                    playwrightDisposeAction),
                _ => throw new NotSupportedException($"Unexpected IPlaywright call: {method.Name}"),
            }),
            CreateProxy<IBrowserContext>((method, arguments) => method.Name switch
            {
                nameof(IBrowserContext.CloseAsync) => closeHandler is null
                    ? Task.CompletedTask
                    : closeHandler(),
                _ => throw new NotSupportedException(
                    $"Unexpected IBrowserContext call: {method.Name}"),
            }),
            page,
            new BrowserLaunchPlan(
                BrowserRuntimeKind.PlaywrightChromium,
                Channel: null,
                ExecutablePath: null,
                DisplayName: "test"),
            afterDisposeAsync);

    private static object? DisposePlaywright(
        Exception? playwrightDisposeException,
        Action? playwrightDisposeAction)
    {
        playwrightDisposeAction?.Invoke();
        if (playwrightDisposeException is not null)
        {
            throw playwrightDisposeException;
        }

        return null;
    }

    private static ChapterDescriptor Chapter(string chapterId)
        => new(
            chapterId,
            "Chapter " + chapterId,
            $"https://www.qidian.com/chapter/100/{chapterId}/",
            IsVip: false,
            CatalogWordCount: 100,
            CatalogAccessState: CatalogChapterAccessState.Accessible);

    private static T CreateProxy<T>(Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        T proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class FakePage
    {
        private readonly Queue<LoginState> loginStates;
        private readonly string? catalogJson;
        private readonly string? chapterJson;
        private LoginState latestState;

        public int EvaluateLoginStateCalls { get; private set; }

        public int EvaluateChapterContentCalls { get; private set; }

        public int WaitForTimeoutCalls { get; private set; }

        public int WaitForLoadStateCalls { get; private set; }

        public string? LastNavigatedUrl { get; private set; }

        public string? CurrentUrl { get; set; }

        public string? RedirectedUrl { get; set; }

        public Task<IResponse>? GotoTask { get; set; }

        public Task<IElementHandle?>? WaitForSelectorTask { get; set; }

        public Task? WaitForTimeoutTask { get; set; }

        public Task? WaitForLoadStateTask { get; set; }

        public Task<string>? EvaluateTask { get; set; }

        public Action? OnEvaluate { get; set; }

        public Action? OnGoto { get; set; }

        public Action? OnWaitForSelector { get; set; }

        public Action? OnWaitForLoadState { get; set; }

        public bool IsClosed { get; set; }

        public FakePage(params LoginState[] initialStates)
            : this(
                catalogJson: null,
                chapterJson: null,
                gotoTask: null,
                waitForSelectorTask: null,
                initialStates)
        {
        }

        public FakePage(string? catalogJson, params LoginState[] initialStates)
            : this(
                catalogJson,
                chapterJson: null,
                gotoTask: null,
                waitForSelectorTask: null,
                initialStates)
        {
        }

        public FakePage(
            string? chapterJson,
            Task<IResponse>? gotoTask,
            Task<IElementHandle?>? waitForSelectorTask)
            : this(
                catalogJson: null,
                chapterJson,
                gotoTask,
                waitForSelectorTask,
                [])
        {
        }

        private FakePage(
            string? catalogJson,
            string? chapterJson,
            Task<IResponse>? gotoTask,
            Task<IElementHandle?>? waitForSelectorTask,
            params LoginState[] initialStates)
        {
            loginStates = new Queue<LoginState>(initialStates);
            this.catalogJson = catalogJson;
            this.chapterJson = chapterJson;
            GotoTask = gotoTask;
            WaitForSelectorTask = waitForSelectorTask;
            latestState = initialStates.Length > 0
                ? initialStates[0].WithNormalizedUserName()
                : new LoginState(false, null);
            Page = CreateProxy<IPage>(Invoke);
        }

        public IPage Page { get; }

        private object? Invoke(MethodInfo method, object?[]? arguments)
            => method.Name switch
            {
                nameof(IPage.GotoAsync) => HandleGoto(arguments),
                nameof(IPage.WaitForSelectorAsync) => HandleWaitForSelector(),
                nameof(IPage.WaitForTimeoutAsync) => HandleWaitForTimeout(),
                nameof(IPage.WaitForLoadStateAsync) => HandleWaitForLoadState(),
                nameof(IPage.EvaluateAsync) => HandleEvaluate(method, arguments),
                "get_IsClosed" => IsClosed,
                "get_Url" => CurrentUrl ?? LastNavigatedUrl ?? string.Empty,
                _ => throw new NotSupportedException($"Unexpected IPage call: {method.Name}"),
            };

        private Task<IResponse> HandleGoto(object?[]? arguments)
        {
            LastNavigatedUrl = arguments is [string url, ..] ? url : null;
            CurrentUrl = RedirectedUrl ?? LastNavigatedUrl;
            OnGoto?.Invoke();
            return GotoTask ?? Task.FromResult<IResponse>(null!);
        }

        private Task<IElementHandle?> HandleWaitForSelector()
        {
            OnWaitForSelector?.Invoke();
            return WaitForSelectorTask ?? Task.FromResult<IElementHandle?>(null);
        }

        private Task HandleWaitForTimeout()
        {
            WaitForTimeoutCalls++;
            return WaitForTimeoutTask ?? Task.CompletedTask;
        }

        private Task HandleWaitForLoadState()
        {
            WaitForLoadStateCalls++;
            OnWaitForLoadState?.Invoke();
            return WaitForLoadStateTask ?? Task.CompletedTask;
        }

        private Task<string> HandleEvaluate(MethodInfo method, object?[]? arguments)
        {
            if (!method.IsGenericMethod || method.GetGenericArguments()[0] != typeof(string))
            {
                throw new NotSupportedException($"Unexpected EvaluateAsync signature: {method}");
            }

            OnEvaluate?.Invoke();

            string? script = arguments is [string value, ..] ? value : null;
            if (EvaluateTask is not null)
            {
                return EvaluateTask;
            }

            if (string.Equals(script, PageScripts.CatalogJson, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    catalogJson
                    ?? throw new NotSupportedException("CatalogJson was not configured."));
            }

            if (string.Equals(script, PageScripts.ChapterContentJson, StringComparison.Ordinal))
            {
                EvaluateChapterContentCalls++;
                return Task.FromResult(
                    chapterJson
                    ?? throw new NotSupportedException("ChapterContentJson was not configured."));
            }

            if (loginStates.TryDequeue(out LoginState? loginState))
            {
                latestState = loginState.WithNormalizedUserName();
            }

            EvaluateLoginStateCalls++;

            string json = JsonSerializer.Serialize(new
            {
                isLoggedIn = latestState.IsLoggedIn,
                userName = latestState.UserName,
                isProbeComplete = latestState.IsProbeComplete,
            });
            return Task.FromResult(json);
        }
    }

#pragma warning disable CA1852
    private class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler?.Invoke(
                targetMethod
                ?? throw new InvalidOperationException("Target method was not provided."),
                args);
    }
#pragma warning restore CA1852
}
