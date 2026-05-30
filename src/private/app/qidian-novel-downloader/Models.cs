using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
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

internal sealed record BookMetadata(
    string BookId,
    string Title,
    string Author,
    int? EstimatedWordCount);

[JsonConverter(typeof(JsonStringEnumConverter<CatalogChapterAccessState>))]
internal enum CatalogChapterAccessState
{
    Unknown,
    Accessible,
    PurchaseRequired,
}

internal sealed record ChapterDescriptor(
    string ChapterId,
    string Title,
    string Url,
    bool IsVip,
    int? CatalogWordCount,
    CatalogChapterAccessState CatalogAccessState);

internal sealed record VolumeDescriptor(
    string Title,
    bool IsVip,
    IReadOnlyList<ChapterDescriptor> Chapters);

[JsonConverter(typeof(JsonStringEnumConverter<CatalogCacheScopeKind>))]
internal enum CatalogCacheScopeKind
{
    Anonymous,
    ValidatedUser,
}

internal sealed record CatalogCacheScope(CatalogCacheScopeKind Kind, string? UserName = null)
{
    public static CatalogCacheScope Anonymous { get; } = new(CatalogCacheScopeKind.Anonymous);

    public static CatalogCacheScope ForValidatedUser(string userName)
    {
        string normalizedUserName = LoginState.NormalizeUserName(userName)
            ?? throw new ArgumentException(
                "Validated catalog cache scope requires a normalized user name.",
                nameof(userName));
        return new CatalogCacheScope(CatalogCacheScopeKind.ValidatedUser, normalizedUserName);
    }

    public bool IsUsable
        => Kind switch
        {
            CatalogCacheScopeKind.Anonymous => UserName is null,
            CatalogCacheScopeKind.ValidatedUser
                => LoginState.NormalizeUserName(UserName) is { } normalizedUserName
                && string.Equals(normalizedUserName, UserName, StringComparison.Ordinal),
            _ => false,
        };

    public string GetCacheFileName()
        => Kind switch
        {
            CatalogCacheScopeKind.Anonymous => AppConstants.CatalogCacheFileName,
            CatalogCacheScopeKind.ValidatedUser => "catalog.user."
                + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(UserName!)))
                    .ToLowerInvariant()
                + ".json",
            _ => throw new InvalidOperationException($"Unsupported catalog cache scope '{Kind}'."),
        };
}

internal sealed record CatalogSnapshot
{
    [JsonConstructor]
    public CatalogSnapshot(
        string BookId,
        BookMetadata Metadata,
        IReadOnlyList<VolumeDescriptor> Volumes,
        DateTimeOffset FetchedAtUtc,
        CatalogCacheScope? CacheScope = null,
        bool IsKnownAnonymous = false)
    {
        this.BookId = BookId;
        this.Metadata = Metadata;
        this.Volumes = Volumes;
        this.FetchedAtUtc = FetchedAtUtc;
        this.CacheScope = CacheScope ?? CatalogCacheScope.Anonymous;
        this.IsKnownAnonymous = IsKnownAnonymous;
    }

    public string BookId { get; init; }

    public BookMetadata Metadata { get; init; }

    public IReadOnlyList<VolumeDescriptor> Volumes { get; init; }

    public DateTimeOffset FetchedAtUtc { get; init; }

    public CatalogCacheScope CacheScope { get; init; }

    public bool IsKnownAnonymous { get; init; }
}

internal static class CatalogSnapshotValidation
{
    public static CatalogSnapshot ValidateAndNormalizeForRequestedBook(
        CatalogSnapshot catalog,
        string expectedBookId)
    {
        if (!IsSafeCanonicalBookId(expectedBookId)
            || !IsSafeCanonicalBookId(catalog.BookId)
            || !IsSafeCanonicalBookId(catalog.Metadata.BookId)
            || !string.Equals(catalog.BookId, expectedBookId, StringComparison.Ordinal)
            || !string.Equals(catalog.Metadata.BookId, expectedBookId, StringComparison.Ordinal))
        {
            throw new OperationalException(
                $"Fetched catalog book ids '{catalog.BookId}'/'{catalog.Metadata.BookId}' "
                + $"did not match requested book id '{expectedBookId}'.");
        }

        return NormalizeChapterUrlsForBook(catalog, expectedBookId);
    }

    public static CatalogSnapshot NormalizeChapterUrlsForBook(
        CatalogSnapshot catalog,
        string expectedBookId)
    {
        if (!IsSafeCanonicalBookId(expectedBookId)
            || !IsSafeCanonicalBookId(catalog.BookId)
            || !IsSafeCanonicalBookId(catalog.Metadata.BookId)
            || !string.Equals(catalog.BookId, expectedBookId, StringComparison.Ordinal)
            || !string.Equals(catalog.Metadata.BookId, expectedBookId, StringComparison.Ordinal))
        {
            throw new OperationalException(
                $"Catalog book ids '{catalog.BookId}'/'{catalog.Metadata.BookId}' "
                + $"are not safe canonical book ids for requested book id '{expectedBookId}'.");
        }

        bool changed = false;
        VolumeDescriptor[] volumes = catalog.Volumes.Select(volume =>
        {
            ChapterDescriptor[] chapters = volume.Chapters.Select(chapter =>
            {
                if (!IsSafeCanonicalChapterId(chapter.ChapterId))
                {
                    throw new OperationalException(
                        $"Catalog chapter id '{chapter.ChapterId}' is not a safe canonical chapter id.");
                }

                if (TryGetUsableChapterUrlForBook(
                    chapter.Url,
                    expectedBookId,
                    chapter.ChapterId,
                    out string usableUrl))
                {
                    if (string.Equals(chapter.Url, usableUrl, StringComparison.Ordinal))
                    {
                        return chapter;
                    }

                    changed = true;
                    return chapter with { Url = usableUrl };
                }

                changed = true;
                return chapter with
                {
                    Url = $"{AppConstants.QidianBaseUrl}/chapter/{expectedBookId}/{chapter.ChapterId}/",
                };
            }).ToArray();

            return changed ? volume with { Chapters = chapters } : volume;
        }).ToArray();

        return changed ? catalog with { Volumes = volumes } : catalog;
    }

    public static bool IsChapterUrlUsableForBook(
        string url,
        string expectedBookId,
        string expectedChapterId)
        => TryGetUsableChapterUrlForBook(url, expectedBookId, expectedChapterId, out _);

    public static bool TryGetUsableChapterUrlForBook(
        string url,
        string expectedBookId,
        string expectedChapterId,
        out string usableUrl)
    {
        usableUrl = string.Empty;
        if (!IsSafeCanonicalBookId(expectedBookId)
            || !IsSafeCanonicalChapterId(expectedChapterId))
        {
            return false;
        }

        string trimmed = url.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || (!string.Equals(uri.Host, "www.qidian.com", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Host, "qidian.com", StringComparison.OrdinalIgnoreCase))
            || HasRawUserInfoInAuthority(trimmed)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        string originalPath = GetOriginalPath(trimmed);
        string[] segments = originalPath.Split('/');
        if (segments.Length == 5
            && segments[0].Length == 0
            && string.Equals(segments[1], "chapter", StringComparison.Ordinal)
            && string.Equals(segments[2], expectedBookId, StringComparison.Ordinal)
            && string.Equals(segments[3], expectedChapterId, StringComparison.Ordinal)
            && segments[4].Length == 0)
        {
            usableUrl = $"{AppConstants.QidianBaseUrl}/chapter/{expectedBookId}/{expectedChapterId}/";
            return true;
        }

        return false;
    }

    private static bool HasRawUserInfoInAuthority(string absoluteUrl)
    {
        int authorityStart = absoluteUrl.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return false;
        }

        authorityStart += 3;
        int authorityEnd = absoluteUrl.IndexOfAny(['/', '?', '#'], authorityStart);
        ReadOnlySpan<char> authority = authorityEnd < 0
            ? absoluteUrl.AsSpan(authorityStart)
            : absoluteUrl.AsSpan(authorityStart, authorityEnd - authorityStart);
        return authority.Contains('@');
    }

    private static string GetOriginalPath(string absoluteUrl)
    {
        int authorityStart = absoluteUrl.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
        {
            return string.Empty;
        }

        authorityStart += 3;
        int authorityEnd = absoluteUrl.IndexOfAny(['?', '#'], authorityStart);
        int pathStart = absoluteUrl.IndexOf('/', authorityStart);
        if (pathStart < 0
            || (authorityEnd >= 0 && pathStart > authorityEnd))
        {
            return string.Empty;
        }

        int queryStart = absoluteUrl.IndexOfAny(['?', '#'], pathStart);
        return queryStart < 0
            ? absoluteUrl[pathStart..]
            : absoluteUrl[pathStart..queryStart];
    }

    private static bool IsSafeCanonicalChapterId(string id)
        => id.Length > 0
        && id.All(static c => c is >= '0' and <= '9');

    internal static bool IsSafeCanonicalBookId(string id)
        => id.Length > 0
        && id.All(static c => c is >= '0' and <= '9');
}

internal sealed record ChapterCacheEntry(
    string ChapterId,
    IReadOnlyList<string> Paragraphs,
    bool IsPreview,
    int? CatalogWordCount,
    CatalogChapterAccessState CatalogAccessState = CatalogChapterAccessState.Unknown,
    string? VisibleToUserName = null,
    VipFullContentCacheProvenance? VipFullContentProvenance = null,
    bool? CatalogIsVip = null,
    bool? IsAnonymousSafeFullContent = null)
{
    [JsonPropertyOrder(-1)]
    public int ParagraphCount => Paragraphs.Count;
}

internal sealed record ChapterCacheProbe(
    string ChapterId,
    ParagraphsProbe? Paragraphs,
    bool IsPreview,
    int? CatalogWordCount,
    CatalogChapterAccessState CatalogAccessState = CatalogChapterAccessState.Unknown,
    string? VisibleToUserName = null,
    VipFullContentCacheProvenance? VipFullContentProvenance = null,
    bool? CatalogIsVip = null,
    bool? IsAnonymousSafeFullContent = null);

internal enum VipFullContentCacheProvenance
{
    Public,
    ValidatedUser,
}

[JsonConverter(typeof(ParagraphsProbeJsonConverter))]
internal sealed class ParagraphsProbe;

internal sealed class ParagraphsProbeJsonConverter : JsonConverter<ParagraphsProbe?>
{
    public override ParagraphsProbe? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException();
        }

        bool hasParagraph = false;
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    return hasParagraph ? new ParagraphsProbe() : null;
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.String:
                    if (string.IsNullOrWhiteSpace(reader.GetString()))
                    {
                        return null;
                    }

                    hasParagraph = true;
                    break;
                default:
                    throw new JsonException();
            }
        }

        throw new JsonException();
    }

    public override void Write(
        Utf8JsonWriter writer,
        ParagraphsProbe? value,
        JsonSerializerOptions options)
        => throw new NotSupportedException();
}

internal sealed record ChapterFetchResult(IReadOnlyList<string> Paragraphs, bool IsPreview);

internal sealed record LoginState(
    bool IsLoggedIn,
    string? UserName,
    bool IsProbeComplete = true)
{
    public bool HasUsableUserName => NormalizeUserName(UserName) is { Length: > 0 };

    public bool IsValidated => IsProbeComplete && IsLoggedIn && HasUsableUserName;

    public LoginState WithNormalizedUserName()
        => this with
        {
            UserName = NormalizeUserName(UserName),
        };

    public static string? NormalizeUserName(string? userName)
        => string.IsNullOrWhiteSpace(userName)
            || string.Equals(userName, "用户名", StringComparison.Ordinal)
            ? null
            : userName;
}

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
    ChapterCacheProbe? CachedProbe,
    ChapterCacheEntry? CachedEntry,
    bool TreatVipFullContentAsPublic = false);

internal sealed record RenderedChapter(
    string Title,
    IReadOnlyList<string> Paragraphs);

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
        + $"chapters downloaded={DownloadedChapters}, reused={ReusedChapters}, "
        + $"failed={FailedChapters}.";
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

    public static readonly Action<ILogger, Exception?> IgnoreAuthenticatedCacheReuseProbeFailure =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1009, nameof(IgnoreAuthenticatedCacheReuseProbeFailure)),
            "Failed to probe current login state for authenticated VIP cache reuse. "
            + "Cached authenticated-sensitive VIP content will be fetched instead of reused.");

    public static readonly Action<ILogger, Exception?>
        IgnoreVipFullContentClassificationProbeFailure =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1010, nameof(IgnoreVipFullContentClassificationProbeFailure)),
            "Failed to probe current login state for VIP full-content classification. "
            + "Saving content without upgrading cache visibility metadata.");
}
