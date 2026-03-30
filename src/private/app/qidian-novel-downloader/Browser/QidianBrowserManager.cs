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
        CancellationToken cancellationToken);
}

internal interface IQidianBrowserSession : IAsyncDisposable
{
    Task<LoginState> GetLoginStateAsync(
        string? url,
        CancellationToken cancellationToken,
        bool navigate = true);

    Task<CatalogSnapshot> FetchCatalogAsync(string bookId, CancellationToken cancellationToken);

    Task<ChapterFetchResult> FetchChapterAsync(
        string bookId,
        ChapterDescriptor chapter,
        CancellationToken cancellationToken);

    Task WaitForManualLoginAsync(CancellationToken cancellationToken);

    Task PersistSessionStateAsync();
}

internal sealed class QidianBrowserManager(ILogger<QidianBrowserManager> logger) : IQidianBrowserManager
{
    private static readonly string[] LaunchArgs =
    [
        "--disable-blink-features=AutomationControlled",
        "--disable-features=AutomationControlled",
    ];

    public async Task<IQidianBrowserSession> OpenAsync(
        ResolvedAppSettings settings,
        AppStoragePaths paths,
        bool headless,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (settings.BrowserPath is { Length: > 0 } && !File.Exists(settings.BrowserPath))
        {
            throw new CliInputException("The specified browser executable does not exist.");
        }

        ChromiumProfilePaths browserProfile = ChromiumProfilePathResolver.Resolve(
            Path.Combine(paths.StateRoot, AppConstants.BrowserProfileDirectoryName),
            settings.BrowserProfileDir);

        Directory.CreateDirectory(browserProfile.UserDataDir);

        List<Exception> failures = [];
        foreach (BrowserLaunchPlan plan in BuildLaunchPlans(settings))
        {
            IPlaywright? playwright = null;
            try
            {
                playwright = await Playwright.CreateAsync();
                IBrowserContext context = await playwright.Chromium.LaunchPersistentContextAsync(
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
                    });

                IPage page = context.Pages.Count > 0
                    ? context.Pages[0]
                    : await context.NewPageAsync();
                LogMessages.SelectedBrowserRuntime(logger, plan.DisplayName, null);
                return new QidianBrowserSession(logger, playwright, context, page, plan);
            }
            catch (Exception exception)
            {
                if (browserProfile.IsOverride && ChromiumProfilePathResolver.IsLikelyLockConflict(exception))
                {
                    if (playwright is not null)
                    {
                        playwright.Dispose();
                    }

                    throw new OperationalException(
                        "The configured browser profile is currently locked by another Chromium browser process "
                        + $"and cannot be opened: '{browserProfile.EffectiveProfilePath}'. "
                        + "Close all Microsoft Edge/Google Chrome windows that are using this profile and try again, "
                        + "or remove the browserProfileDir override to use the downloader's dedicated profile.",
                        exception);
                }

                failures.Add(exception);
                if (playwright is not null)
                {
                    playwright.Dispose();
                }
            }
        }

        throw new OperationalException(
            "Unable to launch a supported browser. Install Microsoft Edge or Google Chrome, "
            + "or install Playwright Chromium with the generated playwright installer script.",
            failures.Count > 0 ? new AggregateException(failures) : null);
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

internal sealed class QidianBrowserSession(
    ILogger logger,
    IPlaywright playwright,
    IBrowserContext context,
    IPage primaryPage,
    BrowserLaunchPlan launchPlan) : IQidianBrowserSession
{
    private bool disposed;

    public BrowserLaunchPlan LaunchPlan => launchPlan;

    public IPage PrimaryPage => primaryPage;

    public async Task<LoginState> GetLoginStateAsync(
        string? url,
        CancellationToken cancellationToken,
        bool navigate = true)
    {
        if (navigate && url is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await primaryPage.GotoAsync(
                url,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60_000,
                });
        }

        LoginState latestState = new(false, null);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await primaryPage.WaitForTimeoutAsync(1000);

            if (primaryPage.IsClosed)
            {
                return latestState;
            }

            latestState = await EvaluateLoginStateAsync(cancellationToken);
            if (latestState.IsLoggedIn)
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
        await primaryPage.GotoAsync(
            $"{AppConstants.QidianBaseUrl}/book/{bookId}/catalog/",
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000,
            });
        await primaryPage.WaitForTimeoutAsync(2000);

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
            foreach (JsonElement chapterElement in volumeElement.GetProperty("chapters").EnumerateArray())
            {
                chapters.Add(
                    new ChapterDescriptor(
                        chapterElement.GetProperty("chapterId").GetString() ?? string.Empty,
                        chapterElement.GetProperty("title").GetString() ?? string.Empty,
                        chapterElement.GetProperty("url").GetString() ?? string.Empty,
                        chapterElement.GetProperty("isVip").GetBoolean(),
                        ReadNullableInt(chapterElement, "catalogWordCount")));
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
        await primaryPage.GotoAsync(
            url,
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000,
            });
        await primaryPage.WaitForTimeoutAsync(1500);

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

    public async Task WaitForManualLoginAsync(CancellationToken cancellationToken)
    {
        await primaryPage.GotoAsync(
            AppConstants.QidianBaseUrl,
            new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000,
            });

        while (!primaryPage.IsClosed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoginState state = await EvaluateLoginStateAsync(cancellationToken);
            if (state.IsLoggedIn)
            {
                return;
            }

            await primaryPage.WaitForTimeoutAsync(1000);
        }

        throw new OperationalException(
            "The login browser window was closed before a valid session was established.");
    }

    public Task PersistSessionStateAsync() => DisposeCoreAsync(swallowBrowserCloseFailure: false);

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
            playwright.Dispose();
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
            ReadString(root, "userName"));
    }

    private async Task<JsonDocument> EvaluateJsonDocumentAsync(
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string json = await primaryPage.EvaluateAsync<string>(script);
        return JsonDocument.Parse(json);
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
}
