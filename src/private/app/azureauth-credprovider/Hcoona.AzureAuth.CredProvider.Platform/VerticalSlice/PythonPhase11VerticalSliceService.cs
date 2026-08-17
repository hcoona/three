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

    public bool? EnableProductProbe { get; init; }
}

public sealed record PythonPhase11DoctorResult
{
    public required PythonPhase11KeyringShimProbe KeyringShim { get; init; }

    public required IReadOnlyList<PythonPhase11EnvironmentProbe> EnvironmentProbes { get; init; }

    public required PythonPhase11KeyringModuleProbe KeyringModuleProbe { get; init; }

    public required PythonPhase11ProductProbe ProductProbe { get; init; }

    public required PythonPhase11AzureAuthKeyringHelperProbe AzureAuthKeyringHelper { get; init; }

    public required bool AzureArtifactsPythonEndpointCanonicalizationSuccess { get; init; }

    public bool IsConfigurationPreflightReady =>
        KeyringModuleProbe.KeyringModuleResolvable
        && ProductProbe.BackendLoadable
        && (
            !AzureAuthKeyringHelper.Applicable
            || AzureAuthKeyringHelper.Status
                == PythonPhase11AzureAuthKeyringHelperProbeStatus.Found
        );

    public bool IsReady =>
        IsConfigurationPreflightReady
        && (
            !KeyringShim.Applicable
            || (
                KeyringShim.ExpectedShimExists
                && KeyringShim.ExpectedShimFirstOnPath
            )
        );

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
    public required bool Applicable { get; init; }

    public required string ExpectedShimPath { get; init; }

    public required string ExpectedShimDirectoryPath { get; init; }

    public required bool ExpectedShimExists { get; init; }

    public required string? FirstKeyringExecutablePath { get; init; }

    public required bool AnyKeyringExecutableOnPath { get; init; }

    public required bool ExpectedShimFirstOnPath { get; init; }

    public required IReadOnlyList<string> PathDirectories { get; init; }
}

public sealed record PythonPhase11ProductProbe
{
    public required string? PythonExecutablePath { get; init; }

    public bool Enabled { get; init; }

    public required bool Attempted { get; init; }

    public required bool BackendLoadable { get; init; }

    public required PythonPhase11ProductProbeStatus Status { get; init; }

    public string? FailureMessage { get; init; }
}

public enum PythonPhase11ProductProbeStatus
{
    NotAttempted,
    Healthy,
    DistributionMissing,
    EntryPointMissing,
    EntryPointMismatch,
    LoadFailure,
    LaunchFailure,
    TimedOut,
    UnexpectedNonZeroExit,
    OutputTooLarge,
    InvalidOutput,
}

public sealed record PythonPhase11AzureAuthKeyringHelperProbe
{
    public required bool Applicable { get; init; }

    public required string? ExpectedExecutablePath { get; init; }

    public required string? ResolvedExecutablePath { get; init; }

    public required PythonPhase11AzureAuthKeyringHelperProbeStatus Status { get; init; }
}

public enum PythonPhase11AzureAuthKeyringHelperProbeStatus
{
    NotApplicable,
    Found,
    Missing,
    PathMismatch,
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
    ModuleFinderError,
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
    private const string AzureAuthKeyringExecutableFileName = "azureauth-keyring";
    private const string PythonExecutableResolutionScript =
        "import os,sys; print(os.path.abspath(sys.executable))";
    private const string KeyringModuleFoundMarker =
        "ACP_KEYRING_PROBE_V1:FOUND";
    private const string KeyringModuleNotFoundMarker =
        "ACP_KEYRING_PROBE_V1:NOT_FOUND";
    private const string KeyringModuleProbeFailureMarker =
        "ACP_KEYRING_PROBE_V1:ERROR";
    private const int KeyringModuleFoundExitCode = 0;
    private const int KeyringModuleNotFoundExitCode = 20;
    private const int KeyringModuleProbeFailureExitCode = 21;
    private const string KeyringModuleProbeScript =
        "import importlib.util,sys\n"
        + "try:\n"
        + "    keyring_spec=importlib.util.find_spec('keyring')\n"
        + "except BaseException:\n"
        + "    print('ACP_KEYRING_PROBE_V1:ERROR')\n"
        + "    sys.exit(21)\n"
        + "if keyring_spec is None:\n"
        + "    print('ACP_KEYRING_PROBE_V1:NOT_FOUND')\n"
        + "    sys.exit(20)\n"
        + "print('ACP_KEYRING_PROBE_V1:FOUND')\n"
        + "sys.exit(0)";
    private const string ProductProbeHealthyMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:HEALTHY";
    private const string ProductProbeDistributionMissingMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:DISTRIBUTION_MISSING";
    private const string ProductProbeEntryPointMissingMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISSING";
    private const string ProductProbeEntryPointMismatchMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISMATCH";
    private const string ProductProbeLoadFailureMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:LOAD_FAILURE";
    private const int ProductProbeHealthyExitCode = 0;
    private const int ProductProbeDistributionMissingExitCode = 30;
    private const int ProductProbeEntryPointMissingExitCode = 31;
    private const int ProductProbeEntryPointMismatchExitCode = 32;
    private const int ProductProbeLoadFailureExitCode = 33;
    private const string ProductProbeScript =
        "import importlib.metadata,inspect,sys\n"
        + "try:\n"
        + "    distribution=importlib.metadata.distribution('azureauth-credprovider-keyring')\n"
        + "except importlib.metadata.PackageNotFoundError:\n"
        + "    print('ACP_AZUREAUTH_PRODUCT_PROBE_V1:DISTRIBUTION_MISSING')\n"
        + "    sys.exit(30)\n"
        + "except BaseException:\n"
        + "    print('ACP_AZUREAUTH_PRODUCT_PROBE_V1:LOAD_FAILURE')\n"
        + "    sys.exit(33)\n"
        + "backend_entry_points=[entry_point for entry_point in "
        + "distribution.entry_points if entry_point.group == 'keyring.backends']\n"
        + "if not backend_entry_points:\n"
        + "    print('ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISSING')\n"
        + "    sys.exit(31)\n"
        + "if len(backend_entry_points) != 1:\n"
        + "    print('ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISMATCH')\n"
        + "    sys.exit(32)\n"
        + "entry_point=backend_entry_points[0]\n"
        + "if entry_point.name != 'azureauth' or entry_point.value != "
        + "'azureauth_credprovider_keyring.backend:AzureAuthKeyringBackend':\n"
        + "    print('ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISMATCH')\n"
        + "    sys.exit(32)\n"
        + "try:\n"
        + "    from keyring.backend import KeyringBackend\n"
        + "    backend_type=entry_point.load()\n"
        + "    contract_methods=('get_password','get_credential',"
        + "'set_password','delete_password')\n"
        + "    valid=(isinstance(backend_type,type) and "
        + "issubclass(backend_type,KeyringBackend) and "
        + "not inspect.isabstract(backend_type) and "
        + "backend_type.__name__ == 'AzureAuthKeyringBackend' and "
        + "backend_type.__module__ == 'azureauth_credprovider_keyring.backend' and "
        + "all(callable(getattr(backend_type,method,None)) "
        + "for method in contract_methods))\n"
        + "except BaseException:\n"
        + "    valid=False\n"
        + "if not valid:\n"
        + "    print('ACP_AZUREAUTH_PRODUCT_PROBE_V1:LOAD_FAILURE')\n"
        + "    sys.exit(33)\n"
        + "print('ACP_AZUREAUTH_PRODUCT_PROBE_V1:HEALTHY')\n"
        + "sys.exit(0)";
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
    private readonly bool productProbeEnabled;
    private readonly string? pythonExecutablePath;
    private readonly bool usesWindowsPaths;

    public PythonPhase11VerticalSliceService(PythonPhase11VerticalSliceOptions? options = null)
    {
        bool useDefaultOptions = options is null;
        options ??= new PythonPhase11VerticalSliceOptions();
        fileSystem = options.FileSystem ?? new SystemFileSystem();
        usesWindowsPaths = FileSystemPathSemantics.UsesWindowsPaths(fileSystem);
        processRunner = options.ProcessRunner ?? new SystemProcessRunner();
        environmentVariableReader =
            options.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
        currentDirectoryPath = fileSystem.GetFullPath(
            NullIfWhiteSpace(options.CurrentDirectoryPath) ?? Environment.CurrentDirectory
        );
        pythonExecutablePath = NullIfWhiteSpace(options.PythonExecutablePath);
        productProbeEnabled = options.EnableProductProbe ?? useDefaultOptions;
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
        PythonPhase11ProductProbe productProbe =
            productProbeEnabled && keyringModuleProbe.KeyringModuleResolvable
            ? await ProbeProductAsync(keyringModuleProbe, cancellationToken).ConfigureAwait(false)
            : CreateProductProbeNotAttempted(
                keyringModuleProbe.PythonExecutablePath,
                productProbeEnabled
            );
        PythonPhase11AzureAuthKeyringHelperProbe helperProbe =
            ProbeAzureAuthKeyringHelper(keyringModuleProbe.PythonExecutablePath);

        return new PythonPhase11DoctorResult
        {
            KeyringShim = keyringShim,
            EnvironmentProbes = environmentProbes,
            KeyringModuleProbe = keyringModuleProbe,
            ProductProbe = productProbe,
            AzureAuthKeyringHelper = helperProbe,
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
            Applicable = !usesWindowsPaths,
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

    private async ValueTask<PythonPhase11ProductProbe> ProbeProductAsync(
        PythonPhase11KeyringModuleProbe keyringModuleProbe,
        CancellationToken cancellationToken
    )
    {
        string pythonExecutablePath =
            keyringModuleProbe.PythonExecutablePath
            ?? throw new InvalidOperationException(
                "A successful keyring module probe must identify its Python interpreter."
            );
        ProcessResult result = await processRunner
            .RunAsync(
                CreatePythonProbeStartSpec(pythonExecutablePath, ProductProbeScript),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (
            result.Status
            is ProcessExecutionStatus.Success or ProcessExecutionStatus.NonZeroExit
        )
        {
            if (
                result.ExitCode == ProductProbeHealthyExitCode
                && HasExactProtocolOutput(result, ProductProbeHealthyMarker)
            )
            {
                return CreateProductProbe(
                    pythonExecutablePath,
                    PythonPhase11ProductProbeStatus.Healthy,
                    backendLoadable: true
                );
            }

            if (
                result.ExitCode == ProductProbeDistributionMissingExitCode
                && HasExactProtocolOutput(result, ProductProbeDistributionMissingMarker)
            )
            {
                return CreateProductProbe(
                    pythonExecutablePath,
                    PythonPhase11ProductProbeStatus.DistributionMissing,
                    backendLoadable: false,
                    "The selected Python interpreter does not contain the "
                        + "azureauth-credprovider-keyring distribution."
                );
            }

            if (
                result.ExitCode == ProductProbeEntryPointMissingExitCode
                && HasExactProtocolOutput(result, ProductProbeEntryPointMissingMarker)
            )
            {
                return CreateProductProbe(
                    pythonExecutablePath,
                    PythonPhase11ProductProbeStatus.EntryPointMissing,
                    backendLoadable: false,
                    "The AzureAuth keyring backend entry point is missing."
                );
            }

            if (
                result.ExitCode == ProductProbeEntryPointMismatchExitCode
                && HasExactProtocolOutput(result, ProductProbeEntryPointMismatchMarker)
            )
            {
                return CreateProductProbe(
                    pythonExecutablePath,
                    PythonPhase11ProductProbeStatus.EntryPointMismatch,
                    backendLoadable: false,
                    "The AzureAuth keyring backend entry point does not match "
                        + "the required target."
                );
            }

            if (
                result.ExitCode == ProductProbeLoadFailureExitCode
                && HasExactProtocolOutput(result, ProductProbeLoadFailureMarker)
            )
            {
                return CreateProductProbe(
                    pythonExecutablePath,
                    PythonPhase11ProductProbeStatus.LoadFailure,
                    backendLoadable: false,
                    "The AzureAuth keyring backend could not be loaded or did not "
                        + "satisfy its required contract."
                );
            }

            if (
                result.Status == ProcessExecutionStatus.NonZeroExit
                && result.ExitCode
                    is not ProductProbeDistributionMissingExitCode
                        and not ProductProbeEntryPointMissingExitCode
                        and not ProductProbeEntryPointMismatchExitCode
                        and not ProductProbeLoadFailureExitCode
            )
            {
                return CreateProductProbe(
                    pythonExecutablePath,
                    PythonPhase11ProductProbeStatus.UnexpectedNonZeroExit,
                    backendLoadable: false,
                    $"The AzureAuth keyring product probe exited unexpectedly with "
                        + $"code {result.ExitCode}."
                );
            }

            return CreateProductProbe(
                pythonExecutablePath,
                PythonPhase11ProductProbeStatus.InvalidOutput,
                backendLoadable: false,
                "The AzureAuth keyring product probe did not produce a recognized "
                    + "marker and exit-code pair."
            );
        }

        return result.Status switch
        {
            ProcessExecutionStatus.LaunchFailure => CreateProductProbe(
                pythonExecutablePath,
                PythonPhase11ProductProbeStatus.LaunchFailure,
                backendLoadable: false,
                "The selected Python interpreter could not be launched for the "
                    + "AzureAuth keyring product probe."
            ),
            ProcessExecutionStatus.TimedOut => CreateProductProbe(
                pythonExecutablePath,
                PythonPhase11ProductProbeStatus.TimedOut,
                backendLoadable: false,
                "The AzureAuth keyring product probe timed out."
            ),
            ProcessExecutionStatus.OutputTooLarge => CreateProductProbe(
                pythonExecutablePath,
                PythonPhase11ProductProbeStatus.OutputTooLarge,
                backendLoadable: false,
                "The AzureAuth keyring product probe exceeded its output limit."
            ),
            ProcessExecutionStatus.InvalidOutput => CreateProductProbe(
                pythonExecutablePath,
                PythonPhase11ProductProbeStatus.InvalidOutput,
                backendLoadable: false,
                "The AzureAuth keyring product probe produced invalid output."
            ),
            _ => throw new InvalidOperationException(
                $"Unsupported Python product probe process status: {result.Status}."
            ),
        };
    }

    private static PythonPhase11ProductProbe CreateProductProbe(
        string pythonExecutablePath,
        PythonPhase11ProductProbeStatus status,
        bool backendLoadable,
        string? failureMessage = null
    ) =>
        new()
        {
            PythonExecutablePath = pythonExecutablePath,
            Enabled = true,
            Attempted = true,
            BackendLoadable = backendLoadable,
            Status = status,
            FailureMessage = failureMessage,
        };

    private static PythonPhase11ProductProbe CreateProductProbeNotAttempted(
        string? pythonExecutablePath,
        bool enabled
    ) =>
        new()
        {
            PythonExecutablePath = pythonExecutablePath,
            Enabled = enabled,
            Attempted = false,
            BackendLoadable = false,
            Status = PythonPhase11ProductProbeStatus.NotAttempted,
            FailureMessage =
                "The AzureAuth keyring product probe was not attempted because "
                + "the keyring module is unavailable.",
        };

    private PythonPhase11AzureAuthKeyringHelperProbe ProbeAzureAuthKeyringHelper(
        string? selectedPythonExecutablePath
    )
    {
        if (usesWindowsPaths)
        {
            return new PythonPhase11AzureAuthKeyringHelperProbe
            {
                Applicable = false,
                ExpectedExecutablePath = null,
                ResolvedExecutablePath = null,
                Status = PythonPhase11AzureAuthKeyringHelperProbeStatus.NotApplicable,
            };
        }

        string helperFileName = GetExecutableFileName(AzureAuthKeyringExecutableFileName);
        string? selectedPythonDirectory =
            selectedPythonExecutablePath is null
                ? null
                : GetDirectoryPath(fileSystem.GetFullPath(selectedPythonExecutablePath));
        string? expectedExecutablePath =
            selectedPythonDirectory is null
                ? null
                : fileSystem.GetFullPath($"{selectedPythonDirectory}/{helperFileName}");
        string? resolvedExecutablePath = null;
        foreach (
            string directory in SplitPathDirectories(
                environmentVariableReader(PathVariableName)
            )
        )
        {
            string candidatePath = fileSystem.GetFullPath(
                $"{directory}/{helperFileName}"
            );
            if (fileSystem.IsExecutableFile(candidatePath))
            {
                resolvedExecutablePath = candidatePath;
                break;
            }
        }

        bool expectedHelperResolved =
            expectedExecutablePath is not null
            && resolvedExecutablePath is not null
            && SameFile(expectedExecutablePath, resolvedExecutablePath);
        bool expectedHelperExists =
            expectedExecutablePath is not null
            && fileSystem.IsExecutableFile(expectedExecutablePath);
        return new PythonPhase11AzureAuthKeyringHelperProbe
        {
            Applicable = true,
            ExpectedExecutablePath = expectedExecutablePath,
            ResolvedExecutablePath = resolvedExecutablePath,
            Status =
                expectedHelperResolved
                    ? PythonPhase11AzureAuthKeyringHelperProbeStatus.Found
                    : expectedHelperExists
                        ? PythonPhase11AzureAuthKeyringHelperProbeStatus.PathMismatch
                        : PythonPhase11AzureAuthKeyringHelperProbeStatus.Missing,
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

        string normalizedPythonExecutablePath = fileSystem.GetFullPath(
            effectivePythonExecutablePath
        );
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

        if (
            result.Status
            is ProcessExecutionStatus.Success or ProcessExecutionStatus.NonZeroExit
        )
        {
            if (
                result.ExitCode == KeyringModuleFoundExitCode
                && HasExactProtocolOutput(result, KeyringModuleFoundMarker)
            )
            {
                return CreateKeyringModuleProbe(
                    normalizedPythonExecutablePath,
                    pythonExecutableExists,
                    PythonPhase11KeyringModuleProbeStatus.ModuleFound,
                    keyringModuleResolvable: true
                );
            }

            if (
                result.ExitCode == KeyringModuleNotFoundExitCode
                && HasExactProtocolOutput(result, KeyringModuleNotFoundMarker)
            )
            {
                return CreateKeyringModuleProbe(
                    normalizedPythonExecutablePath,
                    pythonExecutableExists,
                    PythonPhase11KeyringModuleProbeStatus.ModuleNotFound,
                    keyringModuleResolvable: false,
                    "The selected Python interpreter cannot resolve the keyring module."
                );
            }

            if (
                result.ExitCode == KeyringModuleProbeFailureExitCode
                && HasExactProtocolOutput(
                    result,
                    KeyringModuleProbeFailureMarker
                )
            )
            {
                return CreateKeyringModuleProbe(
                    normalizedPythonExecutablePath,
                    pythonExecutableExists,
                    PythonPhase11KeyringModuleProbeStatus.ModuleFinderError,
                    keyringModuleResolvable: false,
                    "The keyring module finder raised an exception."
                );
            }

            if (
                result.Status == ProcessExecutionStatus.NonZeroExit
                && result.ExitCode
                    is not KeyringModuleNotFoundExitCode
                        and not KeyringModuleProbeFailureExitCode
            )
            {
                return CreateKeyringModuleProbe(
                    normalizedPythonExecutablePath,
                    pythonExecutableExists,
                    PythonPhase11KeyringModuleProbeStatus.UnexpectedNonZeroExit,
                    keyringModuleResolvable: false,
                    $"The keyring module probe exited unexpectedly with code {result.ExitCode}."
                );
            }

            return CreateKeyringModuleProbe(
                normalizedPythonExecutablePath,
                pythonExecutableExists,
                PythonPhase11KeyringModuleProbeStatus.InvalidOutput,
                keyringModuleResolvable: false,
                "The keyring module probe did not produce a recognized marker and exit-code pair."
            );
        }

        return result.Status switch
        {
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

    private static bool HasExactProtocolOutput(ProcessResult result, string marker) =>
        result.StandardError.Length == 0
        && (
            string.Equals(result.StandardOutput, marker, StringComparison.Ordinal)
            || string.Equals(result.StandardOutput, marker + "\n", StringComparison.Ordinal)
            || string.Equals(result.StandardOutput, marker + "\r\n", StringComparison.Ordinal)
        );

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

        PythonCommandResolution python3Resolution =
            await TryResolvePythonCommandExecutablePathAsync(
                "python3",
                cancellationToken
            )
            .ConfigureAwait(false);
        if (python3Resolution.ExecutablePath is not null)
        {
            return python3Resolution.ExecutablePath;
        }

        if (!python3Resolution.CandidateUnavailable)
        {
            return null;
        }

        PythonCommandResolution pythonResolution =
            await TryResolvePythonCommandExecutablePathAsync("python", cancellationToken)
                .ConfigureAwait(false);
        return pythonResolution.ExecutablePath;
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
        bool preferWindowsLayout = usesWindowsPaths;
        if (preferWindowsLayout)
        {
            yield return $"{virtualEnv}/Scripts/python.exe";
            yield break;
        }

        yield return $"{virtualEnv}/bin/python";
    }

    private async ValueTask<PythonCommandResolution> TryResolvePythonCommandExecutablePathAsync(
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
            return new PythonCommandResolution(
                null,
                IsUnavailablePathResolutionCandidate(result)
            );
        }

        string? resolvedPath = TryReadSingleOutputLine(result.StandardOutput);
        if (resolvedPath is null || !fileSystem.IsPathFullyQualified(resolvedPath))
        {
            return new PythonCommandResolution(
                null,
                CandidateUnavailable: false
            );
        }

        return new PythonCommandResolution(
            fileSystem.GetFullPath(resolvedPath),
            CandidateUnavailable: false
        );
    }

    private static bool IsUnavailablePathResolutionCandidate(ProcessResult result) =>
        result.TerminationReason == ProcessTerminationReason.LaunchFailure
        || (
            !OperatingSystem.IsWindows()
            && result.TerminationReason == ProcessTerminationReason.Exited
            && result.HasExitCode
            && result.ExitCode is 126 or 127
        );

    private static string? TryReadSingleOutputLine(string output)
    {
        string content = output.EndsWith("\r\n", StringComparison.Ordinal)
            ? output[..^2]
            : output.EndsWith('\n')
                ? output[..^1]
                : output;
        return content.Length > 0
            && !string.IsNullOrWhiteSpace(content)
            && !content.Contains('\r')
            && !content.Contains('\n')
            && string.Equals(content, content.Trim(), StringComparison.Ordinal)
                ? content
                : null;
    }

    private sealed record PythonCommandResolution(
        string? ExecutablePath,
        bool CandidateUnavailable
    );

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

    private bool SamePath(string left, string right) =>
        string.Equals(
            fileSystem.GetFullPath(left),
            fileSystem.GetFullPath(right),
            FileSystemPathSemantics.GetComparison(fileSystem)
        );

    private bool SameFile(string left, string right)
    {
        if (SamePath(left, right))
        {
            return true;
        }

        return fileSystem is IFileSystemLinkResolver linkResolver
            && string.Equals(
                linkResolver.ResolveFilePathForWrite(left),
                linkResolver.ResolveFilePathForWrite(right),
                FileSystemPathSemantics.GetComparison(fileSystem)
            );
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
