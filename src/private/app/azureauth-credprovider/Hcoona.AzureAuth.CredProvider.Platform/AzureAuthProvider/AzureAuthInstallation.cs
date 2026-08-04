using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public enum AzureAuthHostPlatform
{
    Unspecified = 0,
    Windows = 1,
    Wsl = 2,
    NativeLinux = 3,
}

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

    public string? InstalledExecutablePath { get; init; }

    public string? HostExecutablePath { get; init; }

    public string? Version { get; init; }

    public AzureAuthHostPlatform HostPlatform { get; init; }

    public required string Code { get; init; }

    public required string SafeMessage { get; init; }

    public bool IsAvailable =>
        Status == AzureAuthInstallationStatus.Available
        && InstalledExecutablePath is not null
        && HostExecutablePath is not null
        && HostPlatform != AzureAuthHostPlatform.Unspecified;

    public static AzureAuthInstallation Available(
        string installedExecutablePath,
        string hostExecutablePath,
        string version,
        AzureAuthHostPlatform hostPlatform = AzureAuthHostPlatform.Windows
    ) =>
        new()
        {
            Status = AzureAuthInstallationStatus.Available,
            InstalledExecutablePath = installedExecutablePath,
            HostExecutablePath = hostExecutablePath,
            Version = version,
            HostPlatform = hostPlatform,
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

internal enum LinuxExecuteAccessResult
{
    Allowed,
    Denied,
    Unavailable,
}

public sealed record SystemAzureAuthInstallationDiscoveryOptions
{
    private const int AtFdcwd = -100;
    private const int XOk = 1;
    private const int AtEaccess = 0x200;
    private const int OperationNotPermitted = 1;
    private const int PermissionDenied = 13;

    internal AzureAuthHostPlatform? ForcedHostPlatform { get; init; }

    public bool? IsWslEnvironment { get; init; }

    public string WindowsMountRoot { get; init; } = "/mnt/c";

    public string? LocalApplicationDataPath { get; init; }

    public string? WindowsPowerShellPath { get; init; }

    public string? NativeLinuxExecutablePath { get; init; }

    public Func<string, string?> EnvironmentVariableReader { get; init; } =
        Environment.GetEnvironmentVariable;

    public Func<string, AssemblyName> ManagedAssemblyIdentityReader { get; init; } =
        AssemblyName.GetAssemblyName;

    internal Func<string, LinuxExecuteAccessResult> LinuxExecuteAccessChecker { get; init; } =
        HasLinuxEffectiveExecuteAccess;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public int MaximumOutputBytes { get; init; } = 8 * 1024;

    private static LinuxExecuteAccessResult HasLinuxEffectiveExecuteAccess(string path) =>
        OperatingSystem.IsLinux()
            ? InvokeLinuxEffectiveExecuteAccessCheck(
                path,
                FileAccessAt,
                Marshal.GetLastPInvokeError
            )
            : LinuxExecuteAccessResult.Unavailable;

    internal static LinuxExecuteAccessResult InvokeLinuxEffectiveExecuteAccessCheck(
        string path,
        Func<int, string, int, int, int> fileAccessAt,
        Func<int> getLastError
    )
    {
        try
        {
            if (fileAccessAt(AtFdcwd, path, XOk, AtEaccess) == 0)
            {
                return LinuxExecuteAccessResult.Allowed;
            }

            return getLastError() is PermissionDenied or OperationNotPermitted
                ? LinuxExecuteAccessResult.Denied
                : LinuxExecuteAccessResult.Unavailable;
        }
        catch (Exception exception)
            when (exception
                    is DllNotFoundException
                        or EntryPointNotFoundException
                        or MarshalDirectiveException
                        or BadImageFormatException
            )
        {
            return LinuxExecuteAccessResult.Unavailable;
        }
    }

    [DllImport(
        "libc",
        EntryPoint = "faccessat",
        CharSet = CharSet.Ansi,
        ExactSpelling = true,
        SetLastError = true
    )]
    private static extern int FileAccessAt(
        int directoryFileDescriptor,
        string path,
        int mode,
        int flags
    );
}

public sealed class SystemAzureAuthInstallationDiscovery : IAzureAuthInstallationDiscovery
{
    internal const string NativeLinuxExecutablePathEnvironmentVariable =
        "AZUREAUTH_CREDPROVIDER_AZUREAUTH_PATH";
    internal const string DefaultNativeLinuxExecutablePath = "/usr/lib/azureauth/azureauth";

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

        AzureAuthHostPlatform hostPlatform = GetHostPlatform();
        if (hostPlatform == AzureAuthHostPlatform.Windows)
        {
            string localApplicationData =
                options.LocalApplicationDataPath
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return InspectNativeInstallation(localApplicationData);
        }

        return hostPlatform switch
        {
            AzureAuthHostPlatform.Wsl => DiscoverWsl(cancellationToken),
            AzureAuthHostPlatform.NativeLinux => DiscoverNativeLinux(),
            _ => AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unsupported,
                "AzureAuthLaunchHostUnsupported",
                "AzureAuth production integration requires Windows, WSL, or native Linux."
            ),
        };
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
                AzureAuthProviderConfig.SupportedAzureAuthVersion,
                AzureAuthHostPlatform.Wsl
            )
            : WrongVersion(output.FileVersion);
    }

    private AzureAuthInstallation DiscoverNativeLinux()
    {
        string path =
            options.NativeLinuxExecutablePath
            ?? options.EnvironmentVariableReader(NativeLinuxExecutablePathEnvironmentVariable)
            ?? DefaultNativeLinuxExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthLinuxExecutablePathInvalid",
                "The native Linux AzureAuth executable path must be absolute."
            );
        }

        if (!File.Exists(path))
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Missing,
                "AzureAuthInstallationMissing",
                "AzureAuth 0.9.5 is not installed at the native Linux package location."
            );
        }

        LinuxExecuteAccessResult executeAccess = options.LinuxExecuteAccessChecker(path);
        if (executeAccess == LinuxExecuteAccessResult.Unavailable)
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthLinuxExecutableAccessUnavailable",
                "The native Linux AzureAuth executable access could not be checked."
            );
        }

        if (executeAccess == LinuxExecuteAccessResult.Denied)
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthLinuxExecutableNotExecutable",
                "The native Linux AzureAuth apphost is not executable."
            );
        }

        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthVersionUnavailable",
                "The AzureAuth executable directory could not be determined."
            );
        }

        string assemblyPath = Path.Combine(directory, "azureauth.dll");
        string? version;
        try
        {
            AssemblyName identity = options.ManagedAssemblyIdentityReader(assemblyPath);
            if (!string.Equals(identity.Name, "azureauth", StringComparison.Ordinal))
            {
                return WrongVersion(identity.Version?.ToString());
            }

            version = identity.Version?.ToString();
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or BadImageFormatException
                        or ArgumentException
            )
        {
            return AzureAuthInstallation.Failure(
                AzureAuthInstallationStatus.Unavailable,
                "AzureAuthVersionUnavailable",
                "The AzureAuth managed assembly version could not be read."
            );
        }

        return IsSupportedVersion(version)
            ? AzureAuthInstallation.Available(
                path,
                path,
                AzureAuthProviderConfig.SupportedAzureAuthVersion,
                AzureAuthHostPlatform.NativeLinux
            )
            : WrongVersion(version);
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
                AzureAuthProviderConfig.SupportedAzureAuthVersion,
                AzureAuthHostPlatform.Windows
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
            windowsPath.Length < 3
            || !char.IsAsciiLetter(windowsPath[0])
            || windowsPath[1] != ':'
            || windowsPath[2] != '\\'
            || string.IsNullOrWhiteSpace(mountRoot)
            || !Path.IsPathFullyQualified(mountRoot)
        )
        {
            return null;
        }

        string normalizedMountRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(mountRoot)
        );
        string mountName = Path.GetFileName(normalizedMountRoot);
        if (mountName.Length == 1 && char.IsAsciiLetter(mountName[0]))
        {
            string? mountParent = Path.GetDirectoryName(normalizedMountRoot);
            if (mountParent is null)
            {
                return null;
            }

            normalizedMountRoot = Path.Combine(
                mountParent,
                char.ToLowerInvariant(windowsPath[0]).ToString()
            );
        }

        string relative = windowsPath[3..].Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(normalizedMountRoot, relative));
    }

    private AzureAuthHostPlatform GetHostPlatform()
    {
        if (options.ForcedHostPlatform is { } forced)
        {
            return forced;
        }

        if (OperatingSystem.IsWindows())
        {
            return AzureAuthHostPlatform.Windows;
        }

        if (!OperatingSystem.IsLinux())
        {
            return AzureAuthHostPlatform.Unspecified;
        }

        return options.IsWslEnvironment ?? IsWslEnvironment()
            ? AzureAuthHostPlatform.Wsl
            : AzureAuthHostPlatform.NativeLinux;
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
