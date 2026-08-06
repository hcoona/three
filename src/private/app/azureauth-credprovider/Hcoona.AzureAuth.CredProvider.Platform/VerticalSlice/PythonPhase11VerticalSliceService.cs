using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record PythonPhase11VerticalSliceOptions
{
    public IFileSystem? FileSystem { get; init; }

    public IProcessRunner? ProcessRunner { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }

    public string? ExpectedKeyringShimPath { get; init; }

    public string? PythonExecutablePath { get; init; }

    public string? CurrentDirectoryPath { get; init; }

    public char? PathListSeparator { get; init; }

    public string? KeyringExecutableFileName { get; init; }
}

public sealed record PythonPhase11DoctorResult
{
    public required PythonPhase11KeyringShimProbe KeyringShim { get; init; }

    public required IReadOnlyList<PythonPhase11EnvironmentProbe> EnvironmentProbes { get; init; }

    public required PythonPhase11KeyringModuleProbe KeyringModuleProbe { get; init; }

    public required bool AzureArtifactsPythonEndpointCanonicalizationSuccess { get; init; }

    public bool ActiveVirtualEnvironmentDetected =>
        EnvironmentProbes.Any(static probe =>
            probe.Kind == PythonPhase11EnvironmentKind.ActiveVirtualEnvironment
        );

    public bool ToxEnvironmentDetected =>
        EnvironmentProbes.Any(static probe => probe.Kind == PythonPhase11EnvironmentKind.Tox);

    public bool NoxEnvironmentDetected =>
        EnvironmentProbes.Any(static probe => probe.Kind == PythonPhase11EnvironmentKind.Nox);

    public bool PipxTwineDetected =>
        EnvironmentProbes.Any(static probe =>
            probe.Kind == PythonPhase11EnvironmentKind.PipxTwine
        );

    public bool UvEnvironmentDetected =>
        EnvironmentProbes.Any(static probe =>
            probe.Kind
                is PythonPhase11EnvironmentKind.UvProjectEnvironment
                    or PythonPhase11EnvironmentKind.UvToolDirectory
        );
}

public sealed record PythonPhase11KeyringShimProbe
{
    public required string ExpectedShimPath { get; init; }

    public required string ExpectedShimDirectoryPath { get; init; }

    public required bool ExpectedShimExists { get; init; }

    public required string? FirstKeyringExecutablePath { get; init; }

    public required bool AnyKeyringExecutableOnPath { get; init; }

    public required bool ExpectedShimFirstOnPath { get; init; }

    public required IReadOnlyList<string> PathDirectories { get; init; }
}

public sealed record PythonPhase11EnvironmentProbe
{
    public required PythonPhase11EnvironmentKind Kind { get; init; }

    public required string Source { get; init; }

    public required string Path { get; init; }

    public required PythonPhase11EnvironmentPathKind PathKind { get; init; }

    public required bool Exists { get; init; }
}

public sealed record PythonPhase11KeyringModuleProbe
{
    public required string? PythonExecutablePath { get; init; }

    public required bool PythonExecutableExists { get; init; }

    public required bool Attempted { get; init; }

    public required bool KeyringModuleResolvable { get; init; }

    public required PythonPhase11KeyringModuleProbeStatus Status { get; init; }

    public string? FailureMessage { get; init; }
}

public enum PythonPhase11KeyringModuleProbeStatus
{
    InterpreterNotFound,
    ModuleFound,
    ModuleNotFound,
    LaunchFailure,
    TimedOut,
    UnexpectedNonZeroExit,
    OutputTooLarge,
    InvalidOutput,
}

public enum PythonPhase11EnvironmentKind
{
    ActiveVirtualEnvironment,
    Tox,
    Nox,
    PipxTwine,
    UvProjectEnvironment,
    UvToolDirectory,
}

public enum PythonPhase11EnvironmentPathKind
{
    Directory,
    File,
}

public sealed class PythonPhase11VerticalSliceService
{
    private const string VirtualEnvVariableName = "VIRTUAL_ENV";
    private const string ToxEnvDirVariableName = "TOX_ENV_DIR";
    private const string NoxEnvDirVariableName = "NOX_ENV_DIR";
    private const string PipxHomeVariableName = "PIPX_HOME";
    private const string PipxBinDirVariableName = "PIPX_BIN_DIR";
    private const string UvProjectEnvironmentVariableName = "UV_PROJECT_ENVIRONMENT";
    private const string UvToolDirVariableName = "UV_TOOL_DIR";
    private const string PathVariableName = "PATH";
    private const string TwineExecutableFileName = "twine";
    private const string PythonExecutableResolutionScript =
        "import os,sys; print(os.path.abspath(sys.executable))";
    private const string KeyringModuleProbeScript =
        "import importlib.util,sys; sys.exit(0 if importlib.util.find_spec('keyring') is not None else 1)";
    private static readonly TimeSpan PythonProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly ProcessOutputCaptureOptions PythonProbeOutputCaptureOptions =
        new()
        {
            StandardOutputByteLimit = 4096,
            StandardErrorByteLimit = 4096,
        };

    private readonly string currentDirectoryPath;
    private readonly Func<string, string?> environmentVariableReader;
    private readonly IFileSystem fileSystem;
    private readonly string keyringExecutableFileName;
    private readonly char pathListSeparator;
    private readonly IProcessRunner processRunner;
    private readonly string? pythonExecutablePath;

    public PythonPhase11VerticalSliceService(PythonPhase11VerticalSliceOptions? options = null)
    {
        options ??= new PythonPhase11VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        processRunner = options.ProcessRunner ?? new SystemProcessRunner();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        currentDirectoryPath = fileSystem.GetFullPath(
            NullIfWhiteSpace(options.CurrentDirectoryPath) ?? Environment.CurrentDirectory
        );
        pythonExecutablePath = NullIfWhiteSpace(options.PythonExecutablePath);
        pathListSeparator = options.PathListSeparator ?? Path.PathSeparator;
        ExpectedKeyringShimPath =
            NullIfWhiteSpace(options.ExpectedKeyringShimPath)
            ?? ResolveCurrentLayoutKeyringShimPath(fileSystem, environmentVariableReader);
        keyringExecutableFileName =
            NullIfWhiteSpace(options.KeyringExecutableFileName)
            ?? NullIfWhiteSpace(GetFileName(ExpectedKeyringShimPath))
            ?? (OperatingSystem.IsWindows() ? "keyring.exe" : "keyring");
    }

    public string ExpectedKeyringShimPath { get; }

    public async ValueTask<PythonPhase11DoctorResult> RunDoctorAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        PythonPhase11KeyringShimProbe keyringShim = ProbeKeyringShim();
        List<PythonPhase11EnvironmentProbe> environmentProbes = DiscoverEnvironmentProbes();
        PythonPhase11KeyringModuleProbe keyringModuleProbe = await ProbeKeyringModuleAsync(
                cancellationToken
            )
            .ConfigureAwait(false);

        return new PythonPhase11DoctorResult
        {
            KeyringShim = keyringShim,
            EnvironmentProbes = environmentProbes,
            KeyringModuleProbe = keyringModuleProbe,
            AzureArtifactsPythonEndpointCanonicalizationSuccess =
                CheckAzureArtifactsPythonEndpointCanonicalization(),
        };
    }

    private List<PythonPhase11EnvironmentProbe> DiscoverEnvironmentProbes()
    {
        var probes = new List<PythonPhase11EnvironmentProbe>();
        string? virtualEnv = NullIfWhiteSpace(environmentVariableReader(VirtualEnvVariableName));
        AddDirectoryProbe(
            probes,
            PythonPhase11EnvironmentKind.ActiveVirtualEnvironment,
            VirtualEnvVariableName,
            virtualEnv
        );

        if (PathHasSegment(virtualEnv, ".tox"))
        {
            AddDirectoryProbe(
                probes,
                PythonPhase11EnvironmentKind.Tox,
                VirtualEnvVariableName,
                virtualEnv
            );
        }

        if (PathHasSegment(virtualEnv, ".nox"))
        {
            AddDirectoryProbe(
                probes,
                PythonPhase11EnvironmentKind.Nox,
                VirtualEnvVariableName,
                virtualEnv
            );
        }

        AddDirectoryProbe(
            probes,
            PythonPhase11EnvironmentKind.Tox,
            ToxEnvDirVariableName,
            NullIfWhiteSpace(environmentVariableReader(ToxEnvDirVariableName))
        );
        AddDirectoryProbe(
            probes,
            PythonPhase11EnvironmentKind.Nox,
            NoxEnvDirVariableName,
            NullIfWhiteSpace(environmentVariableReader(NoxEnvDirVariableName))
        );

        AddPipxProbes(probes);
        AddDirectoryProbe(
            probes,
            PythonPhase11EnvironmentKind.UvProjectEnvironment,
            UvProjectEnvironmentVariableName,
            NullIfWhiteSpace(environmentVariableReader(UvProjectEnvironmentVariableName))
        );
        AddDirectoryProbe(
            probes,
            PythonPhase11EnvironmentKind.UvToolDirectory,
            UvToolDirVariableName,
            NullIfWhiteSpace(environmentVariableReader(UvToolDirVariableName))
        );

        return probes;
    }

    private void AddPipxProbes(List<PythonPhase11EnvironmentProbe> probes)
    {
        string? pipxHome = NullIfWhiteSpace(environmentVariableReader(PipxHomeVariableName));
        if (pipxHome is not null)
        {
            AddDirectoryProbe(
                probes,
                PythonPhase11EnvironmentKind.PipxTwine,
                PipxHomeVariableName + "/venvs/twine",
                fileSystem.GetFullPath($"{pipxHome}/venvs/twine")
            );
        }

        string? pipxBinDir = NullIfWhiteSpace(environmentVariableReader(PipxBinDirVariableName));
        if (pipxBinDir is not null)
        {
            AddFileProbe(
                probes,
                PythonPhase11EnvironmentKind.PipxTwine,
                PipxBinDirVariableName + "/twine",
                fileSystem.GetFullPath(
                    $"{pipxBinDir}/{GetExecutableFileName(TwineExecutableFileName)}"
                )
            );
        }
    }

    private PythonPhase11KeyringShimProbe ProbeKeyringShim()
    {
        string expectedShimPath = fileSystem.GetFullPath(ExpectedKeyringShimPath);
        string expectedShimDirectoryPath =
            GetDirectoryPath(expectedShimPath)
            ?? throw new InvalidOperationException(
                "The expected keyring shim path has no directory."
            );
        bool expectedShimExists = fileSystem.FileExists(expectedShimPath);
        string[] pathDirectories = SplitPathDirectories(
            environmentVariableReader(PathVariableName)
        );
        string? firstKeyringExecutablePath = null;
        foreach (string directory in pathDirectories)
        {
            string candidatePath = fileSystem.GetFullPath(
                $"{directory}/{keyringExecutableFileName}"
            );
            if (fileSystem.IsExecutableFile(candidatePath))
            {
                firstKeyringExecutablePath = candidatePath;
                break;
            }
        }

        return new PythonPhase11KeyringShimProbe
        {
            ExpectedShimPath = expectedShimPath,
            ExpectedShimDirectoryPath = fileSystem.GetFullPath(expectedShimDirectoryPath),
            ExpectedShimExists = expectedShimExists,
            FirstKeyringExecutablePath = firstKeyringExecutablePath,
            AnyKeyringExecutableOnPath = firstKeyringExecutablePath is not null,
            ExpectedShimFirstOnPath =
                firstKeyringExecutablePath is not null
                && SamePath(firstKeyringExecutablePath, expectedShimPath),
            PathDirectories = pathDirectories,
        };
    }

    private async ValueTask<PythonPhase11KeyringModuleProbe> ProbeKeyringModuleAsync(
        CancellationToken cancellationToken
    )
    {
        string? effectivePythonExecutablePath = pythonExecutablePath;
        if (effectivePythonExecutablePath is null)
        {
            effectivePythonExecutablePath = await ResolveCurrentTerminalPythonExecutablePathAsync(
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (effectivePythonExecutablePath is null)
        {
            return new PythonPhase11KeyringModuleProbe
            {
                PythonExecutablePath = null,
                PythonExecutableExists = false,
                Attempted = false,
                KeyringModuleResolvable = false,
                Status = PythonPhase11KeyringModuleProbeStatus.InterpreterNotFound,
                FailureMessage = "Current-terminal Python interpreter could not be resolved.",
            };
        }

        string normalizedPythonExecutablePath = fileSystem.GetFullPath(effectivePythonExecutablePath);
        bool pythonExecutableExists = fileSystem.FileExists(normalizedPythonExecutablePath);
        ProcessResult result = await processRunner
            .RunAsync(
                CreatePythonProbeStartSpec(
                    normalizedPythonExecutablePath,
                    KeyringModuleProbeScript
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return result.Status switch
        {
            ProcessExecutionStatus.Success => CreateKeyringModuleProbe(
                normalizedPythonExecutablePath,
                pythonExecutableExists,
                PythonPhase11KeyringModuleProbeStatus.ModuleFound,
                keyringModuleResolvable: true
            ),
            ProcessExecutionStatus.NonZeroExit when result.ExitCode == 1 =>
                CreateKeyringModuleProbe(
                    normalizedPythonExecutablePath,
                    pythonExecutableExists,
                    PythonPhase11KeyringModuleProbeStatus.ModuleNotFound,
                    keyringModuleResolvable: false,
                    "The selected Python interpreter cannot resolve the keyring module."
                ),
            ProcessExecutionStatus.NonZeroExit => CreateKeyringModuleProbe(
                normalizedPythonExecutablePath,
                pythonExecutableExists,
                PythonPhase11KeyringModuleProbeStatus.UnexpectedNonZeroExit,
                keyringModuleResolvable: false,
                $"The keyring module probe exited unexpectedly with code {result.ExitCode}."
            ),
            ProcessExecutionStatus.LaunchFailure => CreateKeyringModuleProbe(
                normalizedPythonExecutablePath,
                pythonExecutableExists,
                PythonPhase11KeyringModuleProbeStatus.LaunchFailure,
                keyringModuleResolvable: false,
                "The selected Python interpreter could not be launched."
            ),
            ProcessExecutionStatus.TimedOut => CreateKeyringModuleProbe(
                normalizedPythonExecutablePath,
                pythonExecutableExists,
                PythonPhase11KeyringModuleProbeStatus.TimedOut,
                keyringModuleResolvable: false,
                "The keyring module probe timed out."
            ),
            ProcessExecutionStatus.OutputTooLarge => CreateKeyringModuleProbe(
                normalizedPythonExecutablePath,
                pythonExecutableExists,
                PythonPhase11KeyringModuleProbeStatus.OutputTooLarge,
                keyringModuleResolvable: false,
                "The keyring module probe exceeded its output limit."
            ),
            ProcessExecutionStatus.InvalidOutput => CreateKeyringModuleProbe(
                normalizedPythonExecutablePath,
                pythonExecutableExists,
                PythonPhase11KeyringModuleProbeStatus.InvalidOutput,
                keyringModuleResolvable: false,
                "The keyring module probe produced invalid output."
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported Python probe process status: {result.Status}."
            ),
        };
    }

    private static PythonPhase11KeyringModuleProbe CreateKeyringModuleProbe(
        string pythonExecutablePath,
        bool pythonExecutableExists,
        PythonPhase11KeyringModuleProbeStatus status,
        bool keyringModuleResolvable,
        string? failureMessage = null
    ) =>
        new()
        {
            PythonExecutablePath = pythonExecutablePath,
            PythonExecutableExists = pythonExecutableExists,
            Attempted = true,
            KeyringModuleResolvable = keyringModuleResolvable,
            Status = status,
            FailureMessage = failureMessage,
        };

    private async ValueTask<string?> ResolveCurrentTerminalPythonExecutablePathAsync(
        CancellationToken cancellationToken
    )
    {
        string? virtualEnv = NullIfWhiteSpace(environmentVariableReader(VirtualEnvVariableName));
        string? virtualEnvPython = TryResolveVirtualEnvPythonExecutablePath(virtualEnv);
        if (virtualEnvPython is not null)
        {
            return virtualEnvPython;
        }

        string? python3Path = await TryResolvePythonCommandExecutablePathAsync(
                "python3",
                cancellationToken
            )
            .ConfigureAwait(false);
        if (python3Path is not null)
        {
            return python3Path;
        }

        return await TryResolvePythonCommandExecutablePathAsync("python", cancellationToken)
            .ConfigureAwait(false);
    }

    private string? TryResolveVirtualEnvPythonExecutablePath(string? virtualEnv)
    {
        if (virtualEnv is null)
        {
            return null;
        }

        foreach (string candidatePath in GetVirtualEnvPythonCandidatePaths(virtualEnv))
        {
            string normalizedCandidatePath = fileSystem.GetFullPath(candidatePath);
            if (fileSystem.IsExecutableFile(normalizedCandidatePath))
            {
                return normalizedCandidatePath;
            }
        }

        return null;
    }

    private IEnumerable<string> GetVirtualEnvPythonCandidatePaths(string virtualEnv)
    {
        bool preferWindowsLayout =
            keyringExecutableFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || LooksLikeWindowsPath(virtualEnv);
        if (preferWindowsLayout)
        {
            yield return $"{virtualEnv}/Scripts/python.exe";
            yield break;
        }

        yield return $"{virtualEnv}/bin/python";
    }

    private async ValueTask<string?> TryResolvePythonCommandExecutablePathAsync(
        string commandName,
        CancellationToken cancellationToken
    )
    {
        ProcessResult result = await processRunner
            .RunAsync(
                CreatePythonProbeStartSpec(
                    commandName,
                    PythonExecutableResolutionScript,
                    BuildPythonCommandResolutionEnvironment()
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return null;
        }

        string? resolvedPath = result.StandardOutput.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries
            )
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.Length > 0);
        return resolvedPath is null ? null : fileSystem.GetFullPath(resolvedPath);
    }

    private static ProcessStartSpec CreatePythonProbeStartSpec(
        string pythonExecutable,
        string script,
        IReadOnlyDictionary<string, string?>? environment = null
    ) =>
        new(
            pythonExecutable,
            ["-c", script],
            environment: environment,
            timeout: PythonProbeTimeout,
            outputCaptureOptions: PythonProbeOutputCaptureOptions
        );

    private Dictionary<string, string?>? BuildPythonCommandResolutionEnvironment()
    {
        string? path = environmentVariableReader(PathVariableName);
        return path is null ? null : new Dictionary<string, string?> { [PathVariableName] = path };
    }

    private static bool CheckAzureArtifactsPythonEndpointCanonicalization()
    {
        Uri organizationSimple = new(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
        );
        Uri organizationUpload = new(
            "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/upload/"
        );
        Uri projectSimple = new(
            "https://dev.azure.com/org/project/_packaging/feed/pypi/simple/"
        );
        Uri projectUpload = new(
            "https://dev.azure.com/org/project/_packaging/feed/pypi/upload/"
        );

        return CheckPythonEndpoint(organizationSimple, "pkgs.dev.azure.com", "org", null, "feed")
            && CheckPythonEndpoint(organizationUpload, "pkgs.dev.azure.com", "org", null, "feed")
            && CheckPythonEndpoint(projectSimple, "dev.azure.com", "org", "project", "feed")
            && CheckPythonEndpoint(projectUpload, "dev.azure.com", "org", "project", "feed");
    }

    private static bool CheckPythonEndpoint(
        Uri serviceEndpoint,
        string host,
        string organization,
        string? project,
        string feed
    )
    {
        if (
            !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                serviceEndpoint,
                CredentialEcosystem.Python
            )
        )
        {
            return false;
        }

        if (
            CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                serviceEndpoint,
                CredentialEcosystem.NuGet
            )
            || CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                serviceEndpoint,
                CredentialEcosystem.Npm
            )
        )
        {
            return false;
        }

        try
        {
            _ = CanonicalResourceIdentity.Create(
                host,
                organization,
                serviceEndpoint,
                project: project,
                feed: feed
            );
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void AddDirectoryProbe(
        List<PythonPhase11EnvironmentProbe> probes,
        PythonPhase11EnvironmentKind kind,
        string source,
        string? path
    )
    {
        if (path is null)
        {
            return;
        }

        probes.Add(
            new PythonPhase11EnvironmentProbe
            {
                Kind = kind,
                Source = source,
                Path = fileSystem.GetFullPath(path),
                PathKind = PythonPhase11EnvironmentPathKind.Directory,
                Exists = fileSystem.DirectoryExists(path),
            }
        );
    }

    private void AddFileProbe(
        List<PythonPhase11EnvironmentProbe> probes,
        PythonPhase11EnvironmentKind kind,
        string source,
        string? path
    )
    {
        if (path is null)
        {
            return;
        }

        probes.Add(
            new PythonPhase11EnvironmentProbe
            {
                Kind = kind,
                Source = source,
                Path = fileSystem.GetFullPath(path),
                PathKind = PythonPhase11EnvironmentPathKind.File,
                Exists = fileSystem.FileExists(path),
            }
        );
    }

    private string[] SplitPathDirectories(string? path)
    {
        if (path is null)
        {
            return [];
        }

        return path.Split(pathListSeparator)
            .Select(segment =>
                segment.Length == 0
                    ? currentDirectoryPath
                    : fileSystem.IsPathFullyQualified(segment)
                        ? fileSystem.GetFullPath(segment)
                        : fileSystem.GetFullPath($"{currentDirectoryPath}/{segment}")
            )
            .ToArray();
    }

    private static string ResolveCurrentLayoutKeyringShimPath(
        IFileSystem fileSystem,
        Func<string, string?> environmentVariableReader
    )
    {
        ConfigurationLayoutPlatform platform = OperatingSystem.IsWindows()
            ? ConfigurationLayoutPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? ConfigurationLayoutPlatform.MacOs
                : ConfigurationLayoutPlatform.Linux;
        var context = new ConfigurationLayoutProjectionContext
        {
            Platform = platform,
            HomeDirectory =
                GetHomeDirectory(environmentVariableReader)
                ?? throw new InvalidOperationException("User profile directory is unavailable."),
            LocalAppDataDirectory = GetLocalAppDataDirectory(environmentVariableReader),
            XdgDataHomeDirectory = environmentVariableReader("XDG_DATA_HOME"),
            XdgConfigHomeDirectory = environmentVariableReader("XDG_CONFIG_HOME"),
            FileExists = fileSystem.FileExists,
        };

        return ConfigurationLayoutProjector.ProjectKeyringShim(context).TargetPath;
    }

    private static string GetLocalAppDataDirectory(
        Func<string, string?> environmentVariableReader
    )
    {
        string? localAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.TrimEndingDirectorySeparator(localAppData);
        }

        string? windowsLocalAppData = environmentVariableReader("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(windowsLocalAppData))
        {
            return Path.TrimEndingDirectorySeparator(windowsLocalAppData);
        }

        string? homeDirectory = GetHomeDirectory(environmentVariableReader);
        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            return Path.Combine(homeDirectory, "AppData", "Local");
        }

        throw new InvalidOperationException("User profile directory is unavailable.");
    }

    private static string? GetHomeDirectory(Func<string, string?> environmentVariableReader)
    {
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.TrimEndingDirectorySeparator(userProfile);
        }

        if (OperatingSystem.IsWindows())
        {
            string? windowsUserProfile = environmentVariableReader("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(windowsUserProfile))
            {
                return Path.TrimEndingDirectorySeparator(windowsUserProfile);
            }

            string? homeDrive = environmentVariableReader("HOMEDRIVE");
            string? homePath = environmentVariableReader("HOMEPATH");
            if (!string.IsNullOrWhiteSpace(homeDrive) && !string.IsNullOrWhiteSpace(homePath))
            {
                return Path.TrimEndingDirectorySeparator(homeDrive + homePath);
            }
        }
        else
        {
            string? home = environmentVariableReader("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.TrimEndingDirectorySeparator(home);
            }
        }

        return null;
    }

    private string GetExecutableFileName(string commandName) =>
        keyringExecutableFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? commandName + ".exe"
            : commandName;

    private static string GetFileName(string path)
    {
        int separatorIndex = path.LastIndexOfAny(['/', '\\']);
        return separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
    }

    private static string? GetDirectoryPath(string path)
    {
        int endIndex = path.Length - 1;
        while (endIndex >= 0 && (path[endIndex] == '/' || path[endIndex] == '\\'))
        {
            endIndex--;
        }

        if (endIndex < 0)
        {
            return null;
        }

        int separatorIndex = path.LastIndexOfAny(['/', '\\'], endIndex);
        if (separatorIndex < 0)
        {
            return null;
        }

        if (separatorIndex == 0)
        {
            return path[..1];
        }

        if (separatorIndex == 2 && path.Length >= 3 && path[1] == ':')
        {
            return path[..3];
        }

        return path[..separatorIndex];
    }

    private static bool PathHasSegment(string? path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.Split('/', '\\')
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeWindowsPath(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private bool SamePath(string left, string right) =>
        string.Equals(
            fileSystem.GetFullPath(left),
            fileSystem.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal
        );

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
