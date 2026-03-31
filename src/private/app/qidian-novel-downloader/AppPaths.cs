using System.Runtime.InteropServices;
using System.Text;

namespace Hcoona.QidianNovelDownloader;

internal static class AppConstants
{
    public const string ConfigFileName = "config.json";
    public const string BrowserProfileDirectoryName = "browser-profile";
    public const string CacheDirectoryName = "cache";
    public const string CatalogsDirectoryName = "catalogs";
    public const string LogsDirectoryName = "logs";
    public const string OutputDirectoryName = "output";
    public const string CatalogCacheFileName = "catalog.json";
    public const string ChaptersDirectoryName = "chapters";
    public const string TruncatedChapterMarker = "……（本章内容未完，需订阅后阅读全文）";
    public const string FailedChapterPlaceholder = "……（本章下载失败，未能获取正文）";
    public const string QidianSectionName = "Qidian";
    public const string QidianBaseUrl = "https://www.qidian.com";
}

internal sealed record AppStoragePaths(
    string StateRoot,
    string ConfigPath,
    string CacheRoot,
    string LogsRoot,
    string OutputRoot,
    string BrowserProfileRoot,
    string? BrowserProfileDirectory);

internal interface IAppStorageService
{
    AppStoragePaths Resolve(AppSettings settings);

    AppStoragePaths EnsureStorage(AppSettings settings);
}

internal sealed class AppStorageService : IAppStorageService
{
    public AppStoragePaths Resolve(AppSettings settings)
        => AppPaths.Resolve(settings);

    public AppStoragePaths EnsureStorage(AppSettings settings)
    {
        AppStoragePaths paths = Resolve(settings);
        Directory.CreateDirectory(paths.StateRoot);
        Directory.CreateDirectory(paths.CacheRoot);
        Directory.CreateDirectory(paths.LogsRoot);
        Directory.CreateDirectory(paths.OutputRoot);
        return paths;
    }
}

internal static class AppPaths
{
    private const string InvalidFileNameCharacters = "<>:\"/\\|?*";

    public static string GetDefaultStateRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hcoona",
                "QidianNovelDownloader");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local",
            "share",
            "hcoona",
            "qidian-novel-downloader");
    }

    public static string GetDefaultConfigPath()
        => Path.Combine(GetDefaultStateRoot(), AppConstants.ConfigFileName);

    public static AppStoragePaths Resolve(AppSettings settings)
    {
        string stateRoot = Path.GetFullPath(GetDefaultStateRoot());
        string outputRoot = settings.OutputDir is { Length: > 0 }
            ? Path.GetFullPath(settings.OutputDir)
            : Path.Combine(stateRoot, AppConstants.OutputDirectoryName);
        ChromiumProfilePaths browserProfile = ChromiumProfilePathResolver.Resolve(
            Path.Combine(stateRoot, AppConstants.BrowserProfileDirectoryName),
            settings.BrowserProfileDir);

        return new AppStoragePaths(
            stateRoot,
            Path.Combine(stateRoot, AppConstants.ConfigFileName),
            Path.Combine(stateRoot, AppConstants.CacheDirectoryName),
            Path.Combine(stateRoot, AppConstants.LogsDirectoryName),
            outputRoot,
            browserProfile.UserDataDir,
            browserProfile.ProfileDirectory);
    }

    public static string GetBookCacheDirectory(string cacheRoot, string bookId)
        => Path.Combine(cacheRoot, bookId);

    public static string GetCatalogCacheDirectory(string cacheRoot, string bookId)
        => Path.Combine(
            GetBookCacheDirectory(cacheRoot, bookId),
            AppConstants.CatalogsDirectoryName);

    public static string GetCatalogCachePath(
        string cacheRoot,
        string bookId,
        CatalogCacheScope scope)
        => Path.Combine(
            GetCatalogCacheDirectory(cacheRoot, bookId),
            scope.GetCacheFileName());

    public static string GetChapterCacheDirectory(string cacheRoot, string bookId)
        => Path.Combine(
            GetBookCacheDirectory(cacheRoot, bookId),
            AppConstants.ChaptersDirectoryName);

    public static string GetChapterCachePath(string cacheRoot, string bookId, string chapterId)
        => Path.Combine(
            GetChapterCacheDirectory(cacheRoot, bookId),
            $"{chapterId}.json");

    public static string BuildDefaultOutputPath(
        string outputRoot,
        string bookId,
        string bookTitle,
        string author)
    {
        string fileName = string.Join(
            "_",
            [
                SanitizeFileNamePart(bookId),
                SanitizeFileNamePart(bookTitle),
                SanitizeFileNamePart(author),
            ]);
        return Path.Combine(outputRoot, $"{fileName}.md");
    }

    public static string SanitizeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        StringBuilder builder = new(value.Length);
        foreach (char character in value.Trim())
        {
            builder.Append(
                InvalidFileNameCharacters.Contains(character) || char.IsControl(character)
                    ? '_'
                    : character);
        }

        string sanitized = builder.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    public static string GetExecutionEnvironmentDisplay()
    {
        string os = RuntimeInformation.OSDescription.Trim();
        string architecture = RuntimeInformation.ProcessArchitecture.ToString();
        return $"{os} | {architecture}";
    }
}
