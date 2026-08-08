using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed record AzureAuthProcessLaunchOptions
{
    private const int DefaultOutputLimit = 8192;

    public required string ExecutablePath { get; init; }

    public required string WorkingDirectory { get; init; }

    public AzureAuthHostPlatform HostPlatform { get; init; } = AzureAuthHostPlatform.Windows;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxStandardOutputBytes { get; init; } = DefaultOutputLimit;

    public int MaxStandardErrorBytes { get; init; } = DefaultOutputLimit;

    internal void Validate()
    {
        if (
            string.IsNullOrWhiteSpace(ExecutablePath)
            || !IsPathFullyQualifiedForHost(ExecutablePath, HostPlatform)
        )
        {
            throw new ArgumentException(
                "AzureAuth executable path must be absolute.",
                nameof(ExecutablePath)
            );
        }

        if (
            string.IsNullOrWhiteSpace(WorkingDirectory)
            || !IsPathFullyQualifiedForHost(WorkingDirectory, HostPlatform)
        )
        {
            throw new ArgumentException(
                "AzureAuth working directory must be absolute.",
                nameof(WorkingDirectory)
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStandardOutputBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStandardErrorBytes);
        if (HostPlatform == AzureAuthHostPlatform.Unspecified)
        {
            throw new ArgumentException(
                "AzureAuth host platform must be specified.",
                nameof(HostPlatform)
            );
        }
    }

    private static bool IsPathFullyQualifiedForHost(
        string path,
        AzureAuthHostPlatform hostPlatform
    ) =>
        hostPlatform switch
        {
            AzureAuthHostPlatform.Windows => IsFullyQualifiedWindowsPath(path),
            AzureAuthHostPlatform.Wsl or AzureAuthHostPlatform.NativeLinux =>
                path.StartsWith('/'),
            _ => Path.IsPathFullyQualified(path),
        };

    private static bool IsFullyQualifiedWindowsPath(string path) =>
        (
            path.Length >= 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && IsDirectorySeparator(path[2])
        )
        || (
            path.Length >= 2
            && IsDirectorySeparator(path[0])
            && IsDirectorySeparator(path[1])
        );

    private static bool IsDirectorySeparator(char value) => value is '/' or '\\';

    internal ProcessOutputCaptureOptions ToOutputCaptureOptions() =>
        new()
        {
            StandardOutputByteLimit = MaxStandardOutputBytes,
            StandardErrorByteLimit = MaxStandardErrorBytes,
        };

    public static AzureAuthProcessLaunchOptions? FromInstallation(
        AzureAuthInstallation installation
    )
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!installation.IsAvailable)
        {
            return null;
        }

        string executablePath = installation.HostExecutablePath!;
        string? workingDirectory = GetDirectoryNameForHost(
            executablePath,
            installation.HostPlatform
        );
        return workingDirectory is null
            ? null
            : new AzureAuthProcessLaunchOptions
            {
                ExecutablePath = executablePath,
                WorkingDirectory = workingDirectory,
                HostPlatform = installation.HostPlatform,
            };
    }

    private static string? GetDirectoryNameForHost(
        string path,
        AzureAuthHostPlatform hostPlatform
    )
    {
        if (
            hostPlatform != AzureAuthHostPlatform.Windows
            && hostPlatform != AzureAuthHostPlatform.Wsl
            && hostPlatform != AzureAuthHostPlatform.NativeLinux
        )
        {
            return Path.GetDirectoryName(path);
        }

        int separatorIndex =
            hostPlatform == AzureAuthHostPlatform.Windows
                ? path.LastIndexOfAny(['/', '\\'])
                : path.LastIndexOf('/');
        if (separatorIndex < 0)
        {
            return null;
        }

        if (separatorIndex == 0)
        {
            return path[..1];
        }

        if (
            hostPlatform == AzureAuthHostPlatform.Windows
            && separatorIndex == 2
            && path[1] == ':'
        )
        {
            return path[..3];
        }

        return path[..separatorIndex];
    }
}
