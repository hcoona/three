using Microsoft.Extensions.Logging;

namespace Hcoona.QidianNovelDownloader;

internal sealed class AppSettings
{
    public string? BrowserPath { get; init; }

    public string? BrowserProfileDir { get; init; }

    public string? OutputDir { get; init; }

    public int ReadingSpeed { get; init; } = 5000;

    public double MinimumRequestDelaySeconds { get; init; } = 5;

    public double MaximumRequestDelaySeconds { get; init; } = 12;

    public int RetryCount { get; init; } = 3;

    public int CatalogCacheTtlHours { get; init; } = 24;

    public IReadOnlyList<string> DefaultBooks { get; init; } = [];
}

internal sealed class DownloadCommandOptions
{
    public IReadOnlyList<string> BookReferences { get; init; } = [];

    public string? BrowserPath { get; init; }

    public string? BrowserProfileDir { get; init; }

    public string? OutputDir { get; init; }

    public bool DryRun { get; init; }

    public bool Overwrite { get; init; }

    public int? ReadingSpeed { get; init; }

    public double? MinimumRequestDelaySeconds { get; init; }

    public double? MaximumRequestDelaySeconds { get; init; }

    public int? RetryCount { get; init; }

    public int? CatalogCacheTtlHours { get; init; }
}

internal sealed class LoginCommandOptions
{
    public string? BrowserPath { get; init; }

    public string? BrowserProfileDir { get; init; }
}

internal sealed class CacheClearCommandOptions
{
    public string? BookReference { get; init; }

    public bool CatalogOnly { get; init; }
}

internal sealed class InfoCommandOptions
{
    public required string BookReference { get; init; }

    public string? BrowserPath { get; init; }

    public string? BrowserProfileDir { get; init; }

    public int? CatalogCacheTtlHours { get; init; }
}

internal sealed record ResolvedAppSettings(
    string? BrowserPath,
    string? BrowserProfileDir,
    string? OutputDir,
    int ReadingSpeed,
    double MinimumRequestDelaySeconds,
    double MaximumRequestDelaySeconds,
    int RetryCount,
    int CatalogCacheTtlHours,
    IReadOnlyList<string> DefaultBooks)
{
    public static ResolvedAppSettings Merge(
        AppSettings configuration,
        DownloadCommandOptions options)
        => new(
            options.BrowserPath ?? configuration.BrowserPath,
            options.BrowserProfileDir ?? configuration.BrowserProfileDir,
            options.OutputDir ?? configuration.OutputDir,
            options.ReadingSpeed ?? configuration.ReadingSpeed,
            options.MinimumRequestDelaySeconds ?? configuration.MinimumRequestDelaySeconds,
            options.MaximumRequestDelaySeconds ?? configuration.MaximumRequestDelaySeconds,
            options.RetryCount ?? configuration.RetryCount,
            options.CatalogCacheTtlHours ?? configuration.CatalogCacheTtlHours,
            configuration.DefaultBooks);

    public static ResolvedAppSettings Merge(
        AppSettings configuration,
        LoginCommandOptions options)
        => new(
            options.BrowserPath ?? configuration.BrowserPath,
            options.BrowserProfileDir ?? configuration.BrowserProfileDir,
            configuration.OutputDir,
            configuration.ReadingSpeed,
            configuration.MinimumRequestDelaySeconds,
            configuration.MaximumRequestDelaySeconds,
            configuration.RetryCount,
            configuration.CatalogCacheTtlHours,
            configuration.DefaultBooks);

    public static ResolvedAppSettings Merge(
        AppSettings configuration,
        InfoCommandOptions options)
        => new(
            options.BrowserPath ?? configuration.BrowserPath,
            options.BrowserProfileDir ?? configuration.BrowserProfileDir,
            configuration.OutputDir,
            configuration.ReadingSpeed,
            configuration.MinimumRequestDelaySeconds,
            configuration.MaximumRequestDelaySeconds,
            configuration.RetryCount,
            options.CatalogCacheTtlHours ?? configuration.CatalogCacheTtlHours,
            configuration.DefaultBooks);

    public AppSettings ToAppSettings()
        => new()
        {
            BrowserPath = BrowserPath,
            BrowserProfileDir = BrowserProfileDir,
            OutputDir = OutputDir,
            ReadingSpeed = ReadingSpeed,
            MinimumRequestDelaySeconds = MinimumRequestDelaySeconds,
            MaximumRequestDelaySeconds = MaximumRequestDelaySeconds,
            RetryCount = RetryCount,
            CatalogCacheTtlHours = CatalogCacheTtlHours,
            DefaultBooks = DefaultBooks,
        };
}

internal sealed record BookReference(string RawValue, string BookId);

internal sealed record BookMetadata(string BookId, string Title, string Author, int? EstimatedWordCount);

internal sealed record ChapterDescriptor(
    string ChapterId,
    string Title,
    string Url,
    bool IsVip,
    int? CatalogWordCount);

internal sealed record VolumeDescriptor(
    string Title,
    bool IsVip,
    IReadOnlyList<ChapterDescriptor> Chapters);

internal sealed record CatalogSnapshot(
    string BookId,
    BookMetadata Metadata,
    IReadOnlyList<VolumeDescriptor> Volumes,
    DateTimeOffset FetchedAtUtc);

internal sealed record ChapterCacheEntry(
    string ChapterId,
    string Title,
    IReadOnlyList<string> Paragraphs,
    bool IsPreview,
    int? CatalogWordCount,
    DateTimeOffset FetchedAtUtc,
    string ContentHash,
    string? VisibleToUserName = null);

internal sealed record ChapterFetchResult(IReadOnlyList<string> Paragraphs, bool IsPreview);

internal sealed record LoginState(bool IsLoggedIn, string? UserName);

internal sealed record BrowserLaunchPlan(
    BrowserRuntimeKind RuntimeKind,
    string? Channel,
    string? ExecutablePath,
    string DisplayName);

internal enum BrowserRuntimeKind
{
    ExplicitExecutable,
    MicrosoftEdge,
    GoogleChrome,
    PlaywrightChromium,
}

internal enum ChapterPlanStatus
{
    Cached,
    Changed,
    FetchRequired,
}

internal sealed record ChapterPlan(
    ChapterDescriptor Chapter,
    ChapterPlanStatus Status,
    ChapterCacheEntry? CachedEntry);

internal sealed record RenderedChapter(
    string ChapterId,
    string Title,
    IReadOnlyList<string> Paragraphs,
    bool FromCache,
    bool Failed);

internal sealed record CommandSummary(
    int Completed,
    int Reused,
    int Skipped,
    int Failed)
{
    public override string ToString()
        => $"Summary: completed={Completed}, reused={Reused}, skipped={Skipped}, failed={Failed}.";
}

internal sealed record DownloadCommandSummary(
    int CompletedBooks,
    int SkippedBooks,
    int FailedBooks,
    int DownloadedChapters,
    int ReusedChapters,
    int FailedChapters)
{
    public bool HasFailures => FailedBooks > 0 || FailedChapters > 0;

    public override string ToString()
        => "Summary: "
        + $"books completed={CompletedBooks}, skipped={SkippedBooks}, failed={FailedBooks}; "
        + $"chapters downloaded={DownloadedChapters}, reused={ReusedChapters}, failed={FailedChapters}.";
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int UsageFailure = 1;
    public const int OperationalFailure = 2;
}

internal sealed class CliInputException(string message) : Exception(message);

internal sealed class OperationalException(string message, Exception? innerException = null)
    : Exception(message, innerException);

internal static class LogMessages
{
    public static readonly Action<ILogger, string, Exception?> SelectedBrowserRuntime =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1000, nameof(SelectedBrowserRuntime)),
            "Selected browser runtime: {BrowserRuntime}");

    public static readonly Action<ILogger, Exception?> DownloadFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1001, nameof(DownloadFailed)),
            "Download failed.");

    public static readonly Action<ILogger, Exception?> LoginFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1002, nameof(LoginFailed)),
            "Login failed.");

    public static readonly Action<ILogger, Exception?> CacheClearFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1003, nameof(CacheClearFailed)),
            "Cache clear failed.");

    public static readonly Action<ILogger, Exception?> InfoFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1004, nameof(InfoFailed)),
            "Info command failed.");

    public static readonly Action<ILogger, string, Exception?> BookProcessingFailed =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1005, nameof(BookProcessingFailed)),
            "Failed to process book {BookId}.");

    public static readonly Action<ILogger, int, int, string, Exception?> ChapterRetry =
        LoggerMessage.Define<int, int, string>(
            LogLevel.Warning,
            new EventId(1006, nameof(ChapterRetry)),
            "Attempt {Attempt}/{TotalAttempts} failed for chapter {ChapterId}.");

    public static readonly Action<ILogger, Exception?> IgnoreBrowserCloseFailure =
        LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(1007, nameof(IgnoreBrowserCloseFailure)),
            "Ignoring browser context close failure.");
}
