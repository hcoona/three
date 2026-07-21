using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public enum AzureAuthLaunchHostContext
{
    Unspecified = 0,
    WindowsDesktop = 1,
    WslWindowsInterop = 2,
    Unsupported = 3,
}

public sealed record AzureAuthProcessLaunchOptions
{
    private const int DefaultOutputLimit = 8192;

    public string? SystemRoot { get; init; }

    public string? Windir { get; init; }

    public string? Temp { get; init; }

    public string? Tmp { get; init; }

    public string? LocalAppData { get; init; }

    public string? UserProfile { get; init; }

    public string? WslInterop { get; init; }

    public AzureAuthLaunchHostContext HostContext { get; init; }

    public bool BrowserInteractionSupported { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxStandardOutputBytes { get; init; } = DefaultOutputLimit;

    public int MaxStandardOutputCharacters { get; init; } = DefaultOutputLimit;

    public int MaxStandardErrorBytes { get; init; } = DefaultOutputLimit;

    public int MaxStandardErrorCharacters { get; init; } = DefaultOutputLimit;

    internal void Validate()
    {
        ValidateOptionalDirectory(SystemRoot, nameof(SystemRoot));
        ValidateOptionalDirectory(Windir, nameof(Windir));
        ValidateOptionalDirectory(Temp, nameof(Temp));
        ValidateOptionalDirectory(Tmp, nameof(Tmp));
        ValidateOptionalDirectory(LocalAppData, nameof(LocalAppData));
        ValidateOptionalDirectory(UserProfile, nameof(UserProfile));
        if (WslInterop is not null && !WslInteropPathPolicy.IsValid(WslInterop))
        {
            throw new ArgumentException(
                "WSL_INTEROP must be a canonical absolute socket path below /run/WSL/.",
                nameof(WslInterop));
        }

        if (Timeout <= TimeSpan.Zero || Timeout > ProcessStartSpec.MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Timeout),
                Timeout,
                $"AzureAuth process timeout must be positive and cannot exceed {ProcessStartSpec.MaximumTimeout}."
            );
        }

        EnsurePositive(MaxStandardOutputBytes, nameof(MaxStandardOutputBytes));
        EnsurePositive(MaxStandardOutputCharacters, nameof(MaxStandardOutputCharacters));
        EnsurePositive(MaxStandardErrorBytes, nameof(MaxStandardErrorBytes));
        EnsurePositive(MaxStandardErrorCharacters, nameof(MaxStandardErrorCharacters));

    }

    internal ProcessOutputCaptureOptions ToOutputCaptureOptions() =>
        new()
        {
            StandardOutputByteLimit = MaxStandardOutputBytes,
            StandardOutputCharacterLimit = MaxStandardOutputCharacters,
            StandardErrorByteLimit = MaxStandardErrorBytes,
            StandardErrorCharacterLimit = MaxStandardErrorCharacters,
        };

    internal IReadOnlyDictionary<string, string?> CreateEnvironment(
        IReadOnlyList<string> trustedPathEntries,
        bool disableMsalCache
    )
    {
        ArgumentNullException.ThrowIfNull(trustedPathEntries);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddIfNotNull(environment, "SystemRoot", SystemRoot);
        AddIfNotNull(environment, "WINDIR", Windir);
        AddIfNotNull(environment, "TEMP", Temp);
        AddIfNotNull(environment, "TMP", Tmp);
        AddIfNotNull(environment, "LOCALAPPDATA", LocalAppData);
        AddIfNotNull(environment, "USERPROFILE", UserProfile);
        AddIfNotNull(environment, "WSL_INTEROP", WslInterop);

        if (trustedPathEntries.Count > 0)
        {
            environment["PATH"] = string.Join(';', trustedPathEntries);
        }

        if (disableMsalCache)
        {
            environment["OEAUTH_MSAL_DISABLE_CACHE"] = "1";
        }

        if (HostContext == AzureAuthLaunchHostContext.WslWindowsInterop)
        {
            environment["PATHEXT"] = ".COM;.EXE;.BAT;.CMD";
            environment["PSModulePath"] = string.Empty;
            WslWindowsEnvironmentBridge.AddSanitizedBridge(environment);
        }

        return environment;
    }

    internal bool TryValidateInteractiveContext(out string code, out string safeMessage)
    {
        try
        {
            Validate();
        }
        catch (ArgumentException)
        {
            code = "AzureAuthLaunchContextInvalid";
            safeMessage = "The configured AzureAuth Windows launch context is invalid.";
            return false;
        }

        if (HostContext is not AzureAuthLaunchHostContext.WindowsDesktop
            and not AzureAuthLaunchHostContext.WslWindowsInterop)
        {
            code = HostContext == AzureAuthLaunchHostContext.Unspecified
                ? "AzureAuthLaunchContextRequired"
                : "AzureAuthLaunchHostUnsupported";
            safeMessage = HostContext == AzureAuthLaunchHostContext.Unspecified
                ? "An explicit trusted Windows or WSL Windows-interoperability launch context is "
                    + "required for AzureAuth."
                : "The configured host context does not support trusted AzureAuth process launch.";
            return false;
        }

        if (!BrowserInteractionSupported)
        {
            code = "AzureAuthBrowserContextUnavailable";
            safeMessage = "The configured launch context does not provide interactive browser login.";
            return false;
        }

        if (SystemRoot is null || Windir is null)
        {
            code = "AzureAuthLaunchContextIncomplete";
            safeMessage = "The trusted AzureAuth Windows launch environment is incomplete.";
            return false;
        }

        if (HostContext == AzureAuthLaunchHostContext.WslWindowsInterop && WslInterop is null)
        {
            code = "AzureAuthWslInteropUnavailable";
            safeMessage = "A valid snapshotted WSL_INTEROP endpoint is required.";
            return false;
        }

        code = "AzureAuthInteractiveReady";
        safeMessage = "AzureAuth interactive browser launch context is validated.";
        return true;
    }

    private static void AddIfNotNull(Dictionary<string, string?> environment, string key, string? value)
    {
        if (value is not null)
        {
            environment[key] = value;
        }
    }

    private static void ValidateOptionalDirectory(string? path, string paramName)
    {
        if (path is not null)
        {
            AzureAuthWindowsDirectoryPathPolicy.Validate(path, paramName);
        }
    }

    private static void EnsurePositive(int value, string paramName)
    {
        if (value <= 0 || value > ProcessOutputCaptureOptions.MaximumStreamLimit)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"AzureAuth output limits must be positive and cannot exceed {ProcessOutputCaptureOptions.MaximumStreamLimit}."
            );
        }
    }
}

internal static class WslWindowsEnvironmentBridge
{
    private static readonly string[] SanitizedVariables =
    [
        "ADO_TOKEN",
        "ALL_PROXY",
        "AZURE_DEVOPS_EXT_PAT",
        "COMPlus_AltJit",
        "COMPlus_AltJitName",
        "COMPlus_ReadyToRun",
        "COMPlus_ZapDisable",
        "CORECLR_ENABLE_PROFILING",
        "CORECLR_PROFILER",
        "CORECLR_PROFILER_PATH",
        "CORECLR_PROFILER_PATH_32",
        "CORECLR_PROFILER_PATH_64",
        "COREHOST_TRACE",
        "COREHOST_TRACEFILE",
        "COREHOST_TRACE_VERBOSITY",
        "COR_ENABLE_PROFILING",
        "COR_PROFILER",
        "COR_PROFILER_PATH",
        "COR_PROFILER_PATH_32",
        "COR_PROFILER_PATH_64",
        "DOTNET_HOST_PATH",
        "DOTNET_ROOT",
        "DOTNET_ROOT_X64",
        "DOTNET_ROOT_X86",
        "DOTNET_STARTUP_HOOKS",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "NO_PROXY",
        "NODE_OPTIONS",
        "SYSTEM_ACCESSTOKEN",
        "VSS_NUGET_EXTERNAL_FEED_ENDPOINTS",
    ];

    internal static void AddSanitizedBridge(
        Dictionary<string, string?> environment,
        params string[] additionalVariables)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (string variable in SanitizedVariables)
        {
            environment.TryAdd(variable, string.Empty);
        }

        string[] bridgedVariables = environment.Keys
            .Where(static key => !string.Equals(key, "WSL_INTEROP", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "WSLENV", StringComparison.OrdinalIgnoreCase))
            .Concat(additionalVariables)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        environment["WSLENV"] = string.Join(':', bridgedVariables);
    }
}

public static class AzureAuthProcessLaunchDiscovery
{
    private const string WindowsMountRoot = "/mnt/c";

    public static AzureAuthProcessLaunchOptions? DiscoverWsl(
        AzureAuthProviderConfig config,
        AzureAuthTrustResult trust,
        string? wslInterop)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(trust);
        if (config.Selection != AzureAuthProviderSelection.AzureAuth
            || !trust.IsReady
            || trust.Evidence is null
            || !WslInteropPathPolicy.IsValid(wslInterop))
        {
            return null;
        }

        string? hostExecutablePath = MapWindowsPath(
            config.DeploymentConfig!.ExecutablePath,
            WindowsMountRoot);
        string? hostWorkingDirectory = MapWindowsPath(
            trust.Evidence.TrustedWorkingDirectory,
            WindowsMountRoot);
        if (hostExecutablePath is null || hostWorkingDirectory is null)
        {
            return null;
        }

        const string SystemRoot = @"C:\Windows";
        return new AzureAuthProcessLaunchOptions
        {
            SystemRoot = SystemRoot,
            Windir = SystemRoot,
            WslInterop = wslInterop,
            HostContext = AzureAuthLaunchHostContext.WslWindowsInterop,
            BrowserInteractionSupported = true,
        };
    }

    internal static bool TryResolveHostLaunchPaths(
        AzureAuthLaunchHostContext hostContext,
        AzureAuthDeploymentConfig deploymentConfig,
        AzureAuthArtifactEvidence evidence,
        out string executablePath,
        out string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(deploymentConfig);
        ArgumentNullException.ThrowIfNull(evidence);
        if (hostContext == AzureAuthLaunchHostContext.WindowsDesktop)
        {
            executablePath = deploymentConfig.ExecutablePath;
            workingDirectory = evidence.TrustedWorkingDirectory;
            return true;
        }

        if (hostContext == AzureAuthLaunchHostContext.WslWindowsInterop)
        {
            executablePath = MapWindowsPath(deploymentConfig.ExecutablePath, WindowsMountRoot)
                ?? string.Empty;
            workingDirectory = MapWindowsPath(evidence.TrustedWorkingDirectory, WindowsMountRoot)
                ?? string.Empty;
            return executablePath.Length > 0 && workingDirectory.Length > 0;
        }

        executablePath = string.Empty;
        workingDirectory = string.Empty;
        return false;
    }

    internal static string? MapWindowsPathForTesting(string windowsPath, string mountRoot) =>
        MapWindowsPath(windowsPath, mountRoot);

    private static string? MapWindowsPath(string windowsPath, string mountRoot)
    {
        if (!windowsPath.StartsWith(@"C:\", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(mountRoot)
            || !Path.IsPathFullyQualified(mountRoot))
        {
            return null;
        }

        try
        {
            string root = Path.GetFullPath(mountRoot);
            string relative = windowsPath[3..].Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(root, relative));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

internal static class WslInteropPathPolicy
{
    private const string ExpectedPrefix = "/run/WSL/";

    internal static bool IsValid(string? path)
    {
        if (string.IsNullOrEmpty(path)
            || path.Any(static character => char.IsControl(character))
            || !path.StartsWith(ExpectedPrefix, StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            string fileName = Path.GetFileName(path);
            const string Suffix = "_interop";
            string identifier = fileName.EndsWith(Suffix, StringComparison.Ordinal)
                ? fileName[..^Suffix.Length]
                : string.Empty;
            return string.Equals(path, Path.GetFullPath(path), StringComparison.Ordinal)
                && path.Length > ExpectedPrefix.Length
                && identifier.Length > 0
                && identifier.All(static character => character is >= '0' and <= '9');
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}

internal static class AzureAuthWindowsDirectoryPathPolicy
{
    internal static void Validate(string path, string paramName)
    {
        if (
            string.IsNullOrWhiteSpace(path)
            || !string.Equals(path, path.Trim(), StringComparison.Ordinal)
            || path.Contains(';')
        )
        {
            throw new ArgumentException(
                "Configured AzureAuth directories must use canonical absolute local Windows paths.",
                paramName
            );
        }

        try
        {
            WindowsPathPolicy.ValidateDirectoryPath(path);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "Configured AzureAuth directories must use canonical absolute local Windows paths.",
                paramName,
                exception
            );
        }
    }
}
