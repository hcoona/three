namespace Hcoona.QidianNovelDownloader;

internal sealed record ChromiumProfilePaths(
    string UserDataDir,
    string? ProfileDirectory,
    bool IsOverride)
{
    public string EffectiveProfilePath => ProfileDirectory is null
        ? UserDataDir
        : Path.Combine(UserDataDir, ProfileDirectory);
}

internal static class ChromiumProfilePathResolver
{
    private static readonly string[] LockConflictMarkers =
    [
        "SingletonLock",
        "ProcessSingleton",
        "profile appears to be in use",
        "Opening in existing browser session",
        "user data directory is already in use",
        "exit code: 21",
    ];

    public static ChromiumProfilePaths Resolve(string defaultUserDataDir, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return new ChromiumProfilePaths(
                Path.GetFullPath(defaultUserDataDir),
                ProfileDirectory: null,
                IsOverride: false);
        }

        string fullPath = Path.GetFullPath(configuredPath);
        if (TryResolveProfileDirectory(
            fullPath,
            out string userDataDir,
            out string profileDirectory))
        {
            return new ChromiumProfilePaths(
                userDataDir,
                profileDirectory,
                IsOverride: true);
        }

        return new ChromiumProfilePaths(
            fullPath,
            ProfileDirectory: null,
            IsOverride: true);
    }

    public static string[] BuildLaunchArguments(
        IEnumerable<string> baseArguments,
        string? profileDirectory)
    {
        List<string> arguments = [.. baseArguments];
        if (!string.IsNullOrWhiteSpace(profileDirectory))
        {
            arguments.Add($"--profile-directory={profileDirectory}");
        }

        return [.. arguments];
    }

    public static bool IsLikelyLockConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            foreach (string marker in LockConflictMarkers)
            {
                if (current.Message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryResolveProfileDirectory(
        string fullPath,
        out string userDataDir,
        out string profileDirectory)
    {
        userDataDir = string.Empty;
        profileDirectory = string.Empty;

        if (!Directory.Exists(fullPath))
        {
            return false;
        }

        DirectoryInfo profileDirectoryInfo = new(fullPath);
        DirectoryInfo? userDataDirectoryInfo = profileDirectoryInfo.Parent;
        if (userDataDirectoryInfo is null)
        {
            return false;
        }

        if (!File.Exists(Path.Combine(userDataDirectoryInfo.FullName, "Local State")))
        {
            return false;
        }

        if (!File.Exists(Path.Combine(profileDirectoryInfo.FullName, "Preferences")))
        {
            return false;
        }

        userDataDir = userDataDirectoryInfo.FullName;
        profileDirectory = profileDirectoryInfo.Name;
        return true;
    }
}
