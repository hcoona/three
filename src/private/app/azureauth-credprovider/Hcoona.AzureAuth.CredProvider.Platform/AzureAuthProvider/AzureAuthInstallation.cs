using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public enum AzureAuthInstallationStatus
{
    Available = 1,
    Missing = 2,
    WrongVersion = 3,
    Unsupported = 4,
    Unavailable = 5,
}

public sealed record AzureAuthInstallation
{
    public required AzureAuthInstallationStatus Status { get; init; }

    public string? WindowsExecutablePath { get; init; }

    public string? HostExecutablePath { get; init; }

    public string? Version { get; init; }

    public required string Code { get; init; }

    public required string SafeMessage { get; init; }

    public bool IsAvailable =>
        Status == AzureAuthInstallationStatus.Available
        && WindowsExecutablePath is not null
        && HostExecutablePath is not null;

    public static AzureAuthInstallation Available(
        string windowsExecutablePath,
        string hostExecutablePath,
        string version
    ) =>
        new()
        {
            Status = AzureAuthInstallationStatus.Available,
            WindowsExecutablePath = windowsExecutablePath,
            HostExecutablePath = hostExecutablePath,
            Version = version,
            Code = "AzureAuthInstallationAvailable",
            SafeMessage = "The supported AzureAuth installation is available.",
        };

    public static AzureAuthInstallation Failure(
        AzureAuthInstallationStatus status,
        string code,
        string safeMessage
    ) =>
        new()
        {
            Status = status,
            Code = code,
            SafeMessage = safeMessage,
        };
}

public interface IAzureAuthInstallationDiscovery
{
    AzureAuthInstallation Discover(
        AzureAuthProviderConfig config,
        CancellationToken cancellationToken = default
    );
}

public sealed record SystemAzureAuthInstallationDiscoveryOptions
{
    public bool? IsWslEnvironment { get; init; }

    public string WindowsMountRoot { get; init; } = "/mnt/c";

    public string? LocalApplicationDataPath { get; init; }

    public string? WindowsPowerShellPath { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public int MaximumOutputBytes { get; init; } = 8 * 1024;
}

public sealed class SystemAzureAuthInstallationDiscovery : IAzureAuthInstallationDiscovery
{
    private static readonly string DiscoveryScript =
        "$ErrorActionPreference='Stop'\n"
        + "$localAppData=[Environment]::GetFolderPath("
        + "[Environment+SpecialFolder]::LocalApplicationData)\n"
        + "$path=[IO.Path]::Combine($localAppData,'Programs','AzureAuth','"
        + AzureAuthProviderConfig.SupportedAzureAuthVersion
        + "','azureauth.exe')\n"
        + "$exists=[IO.File]::Exists($path)\n"
        + "$version=if ($exists) { "
        + "[Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion "
        + "} else { $null }\n"
        + "@{localApplicationData=$localAppData;exists=$exists;fileVersion=$version} |\n"
        + "  ConvertTo-Json -Compress";

    private readonly IProcessRunner processRunner;
    private readonly SystemAzureAuthInstallationDiscoveryOptions options;

    public SystemAzureAuthInstallationDiscovery(
        IProcessRunner? processRunner = null,
        SystemAzureAuthInstallationDiscoveryOptions? options = null
    )
    {
        this.processRunner = processRunner ?? new SystemProcessRunner();
        this.options = options ?? new SystemAzureAuthInstallationDiscoveryOptions();
    }

    public AzureAuthInstallation Discover(
        AzureAuthProviderConfig config,
        CancellationToken cancellationToken = default
    )
    {
        AzureAuthProviderConfigPolicy.EnsureValid(config);
        cancellationToken.ThrowIfCancellationRequested();
        if (config.Selection != AzureAuthProviderSelection.AzureAuth)
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unsupported,
                "AzureAuthProviderSelectionMismatch",
                "AzureAuth is not the selected provider."
            );
        }

        if (OperatingSystem.IsWindows())
        {
            string localApplicationData =
                options.LocalApplicationDataPath
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return InspectNativeInstallation(localApplicationData);
        }

        bool isWsl = options.IsWslEnvironment ?? IsWslEnvironment();
        return OperatingSystem.IsLinux() && isWsl
            ? DiscoverWsl(cancellationToken)
            : AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unsupported,
                "AzureAuthLaunchHostUnsupported",
                "AzureAuth production integration requires Windows or WSL."
            );
    }

    private AzureAuthInstallation DiscoverWsl(CancellationToken cancellationToken)
    {
        string? systemDirectory = MapWindowsPath(@"C:\Windows\System32", options.WindowsMountRoot);
        string powerShellPath =
            options.WindowsPowerShellPath
            ?? (
                systemDirectory is null
                    ? string.Empty
                    : Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")
            );
        if (powerShellPath.Length == 0 || !File.Exists(powerShellPath))
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthDiscoveryUnavailable",
                "Windows PowerShell is unavailable for AzureAuth installation discovery."
            );
        }

        ProcessResult result = processRunner
            .RunAsync(
                new ProcessStartSpec(
                    powerShellPath,
                    ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", DiscoveryScript],
                    workingDirectory: systemDirectory,
                    timeout: options.Timeout,
                    outputCaptureOptions: new ProcessOutputCaptureOptions
                    {
                        StandardOutputByteLimit = options.MaximumOutputBytes,
                        StandardErrorByteLimit = options.MaximumOutputBytes,
                    }
                ),
                cancellationToken
            )
            .GetAwaiter()
            .GetResult();

        if (!result.Succeeded)
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthDiscoveryUnavailable",
                "AzureAuth installation discovery did not complete successfully."
            );
        }

        DiscoveryOutput? output;
        try
        {
            output = JsonSerializer.Deserialize(
                result.StandardOutput.Trim(),
                AzureAuthInstallationJsonContext.Default.DiscoveryOutput
            );
        }
        catch (JsonException)
        {
            output = null;
        }

        if (output is null || string.IsNullOrWhiteSpace(output.LocalApplicationData))
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthDiscoveryOutputInvalid",
                "AzureAuth installation discovery returned invalid output."
            );
        }

        string windowsPath = GetWindowsExecutablePath(output.LocalApplicationData);
        string? hostPath = MapWindowsPath(windowsPath, options.WindowsMountRoot);
        if (!output.Exists || hostPath is null || !File.Exists(hostPath))
        {
            return Missing();
        }

        return IsSupportedVersion(output.FileVersion)
            ? AzureAuthInstallation.Available(
                windowsPath,
                hostPath,
                AzureAuthProviderConfig.SupportedAzureAuthVersion
            )
            : WrongVersion(output.FileVersion);
    }

    private static AzureAuthInstallation InspectNativeInstallation(string localApplicationData)
    {
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthLocalApplicationDataUnavailable",
                "Windows LocalApplicationData is unavailable."
            );
        }

        string path = GetWindowsExecutablePath(localApplicationData);
        if (!File.Exists(path))
        {
            return Missing();
        }

        string? version;
        try
        {
            version = FileVersionInfo.GetVersionInfo(path).FileVersion;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthVersionUnavailable",
                "The AzureAuth executable version could not be read."
            );
        }

        return IsSupportedVersion(version)
            ? AzureAuthInstallation.Available(
                path,
                path,
                AzureAuthProviderConfig.SupportedAzureAuthVersion
            )
            : WrongVersion(version);
    }

    private static string GetWindowsExecutablePath(string localApplicationData)
    {
        if (
            localApplicationData.Length >= 3
            && localApplicationData[1] == ':'
            && localApplicationData[2] == '\\'
        )
        {
            return localApplicationData.TrimEnd('\\')
                + $@"\Programs\AzureAuth\{AzureAuthProviderConfig.SupportedAzureAuthVersion}"
                + @"\azureauth.exe";
        }

        return Path.Combine(
            localApplicationData,
            "Programs",
            "AzureAuth",
            AzureAuthProviderConfig.SupportedAzureAuthVersion,
            "azureauth.exe"
        );
    }

    private static bool IsSupportedVersion(string? version) =>
        Version.TryParse(version, out Version? parsed)
        && parsed.Major == 0
        && parsed.Minor == 9
        && parsed.Build == 5
        && parsed.Revision is -1 or 0;

    private static AzureAuthInstallation Missing() =>
        AzureAuthInstallation.Failure(
            AzureAuthInstallationStatus.Missing,
            "AzureAuthInstallationMissing",
            "AzureAuth 0.9.5 is not installed in the current user's LocalApplicationData."
        );

    private static AzureAuthInstallation WrongVersion(string? version) =>
        AzureAuthInstallation.Failure(
            AzureAuthInstallationStatus.WrongVersion,
            "AzureAuthVersionMismatch",
            string.IsNullOrWhiteSpace(version)
                ? "The installed AzureAuth executable has no readable version."
                : $"The installed AzureAuth version '{version}' is unsupported; "
                    + "version 0.9.5 is required."
        );

    private static string? MapWindowsPath(string windowsPath, string mountRoot)
    {
        if (
            !windowsPath.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(mountRoot)
            || !Path.IsPathFullyQualified(mountRoot)
        )
        {
            return null;
        }

        string relative = windowsPath[3..].Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(mountRoot, relative));
    }

    private static bool IsWslEnvironment() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"))
        || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WSL_INTEROP"));

    internal sealed record DiscoveryOutput
    {
        public string? LocalApplicationData { get; init; }
        public bool Exists { get; init; }
        public string? FileVersion { get; init; }
    }
}

[JsonSerializable(typeof(SystemAzureAuthInstallationDiscovery.DiscoveryOutput))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
internal sealed partial class AzureAuthInstallationJsonContext : JsonSerializerContext;
