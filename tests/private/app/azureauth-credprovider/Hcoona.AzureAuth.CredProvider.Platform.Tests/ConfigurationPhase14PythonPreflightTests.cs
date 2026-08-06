using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

#pragma warning disable CA1707
[Collection("ConfigurationManagerExecution")]
public sealed class ConfigurationPhase14PythonPreflightTests
{
    private const string GenericFoundMarker = "ACP_KEYRING_PROBE_V1:FOUND";
    private const string ProductHealthyMarker = "ACP_AZUREAUTH_PRODUCT_PROBE_V1:HEALTHY";
    private const string SelectedPythonPath = "/workspace/.venv/bin/python";
    private const string HelperPath = "/workspace/.venv/bin/azureauth-keyring";
    private const string ExpectedShimPath =
        "/home/test/.local/share/azureauth-credprovider/keyring-shim/keyring";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigureEntryPoints_RunReusablePythonDoctorBeforePlanning(bool execute)
    {
        var fileSystem = CreatePosixFileSystem(helperPresent: true);
        var runner = new FakeProcessRunner();
        runner.EnqueueHandler(
            (_, _) =>
            {
                AssertPrePlanState(fileSystem);
                return Task.FromResult(GenericFoundResult());
            }
        );
        runner.EnqueueHandler(
            (_, _) =>
            {
                AssertPrePlanState(fileSystem);
                return Task.FromResult(ProductHealthyResult());
            }
        );
        ConfigurationPhase14VerticalSliceService service = CreateConfigurationService(
            fileSystem,
            CreateDoctor(fileSystem, runner)
        );

        ConfigurationPhase14PlanResult result = execute
            ? await service.ConfigureAsync(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            )
            : await service.DryRunConfigureAsync(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            );

        Assert.True(result.PythonPreflightSucceeded);
        Assert.Equal(PythonPhase11ProductProbeStatus.Healthy, result.PythonPreflight!.ProductProbe.Status);
        Assert.Equal(
            PythonPhase11AzureAuthKeyringHelperProbeStatus.Found,
            result.PythonPreflight.AzureAuthKeyringHelper.Status
        );
        Assert.Equal(2, runner.StartSpecs.Count);
        Assert.Equal(2, result.PlanResults.Count);
        Assert.Equal(execute, fileSystem.FileExists(ExpectedShimPath));
    }

    [Fact]
    public async Task ConfigureAsync_WhenPythonPreflightFails_ReturnsFailureWithoutWritesOrPlanExecution()
    {
        var fileSystem = CreatePosixFileSystem(helperPresent: true);
        var runner = HealthyGenericRunner(
            ProductResult(
                30,
                "ACP_AZUREAUTH_PRODUCT_PROBE_V1:DISTRIBUTION_MISSING"
            )
        );
        ConfigurationPhase14VerticalSliceService service = CreateConfigurationService(
            fileSystem,
            CreateDoctor(fileSystem, runner)
        );
        string manifestPath = Path.Combine(
            service.Paths.ManifestDirectoryPath,
            "python-user-ownership-manifest.json"
        );
        fileSystem.CreateDirectory(service.Paths.ManifestDirectoryPath);
        fileSystem.WriteAllText(manifestPath, "{not-valid-json");
        Dictionary<string, string> filesBefore = fileSystem.Files.ToDictionary();
        HashSet<string> directoriesBefore = [.. fileSystem.Directories];

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.PythonPreflightSucceeded);
        Assert.Equal(
            PythonPhase11ProductProbeStatus.DistributionMissing,
            result.PythonPreflight!.ProductProbe.Status
        );
        Assert.Empty(result.PlanResults);
        Assert.Null(result.PlanResult);
        Assert.Equal(0, result.ChangeCount);
        Assert.Equal(0, result.AppliedChangeCount);
        Assert.Equal(filesBefore, fileSystem.Files);
        Assert.Equal(directoriesBefore, fileSystem.Directories);
        Assert.Equal("{not-valid-json", fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task DryRunConfigureAsync_WhenPythonPreflightFails_ReturnsNoPlannedWrites()
    {
        var fileSystem = CreatePosixFileSystem(helperPresent: true);
        var runner = HealthyGenericRunner(
            ProductResult(
                33,
                "ACP_AZUREAUTH_PRODUCT_PROBE_V1:LOAD_FAILURE"
            )
        );
        ConfigurationPhase14VerticalSliceService service = CreateConfigurationService(
            fileSystem,
            CreateDoctor(fileSystem, runner)
        );
        Dictionary<string, string> filesBefore = fileSystem.Files.ToDictionary();
        HashSet<string> directoriesBefore = [.. fileSystem.Directories];

        ConfigurationPhase14PlanResult result = await service.DryRunConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.PythonPreflightSucceeded);
        Assert.Empty(result.PlanResults);
        Assert.Null(result.PlanResult);
        Assert.Equal(0, result.ChangeCount);
        Assert.Equal(filesBefore, fileSystem.Files);
        Assert.Equal(directoriesBefore, fileSystem.Directories);
        Assert.False(fileSystem.DirectoryExists(service.Paths.ManifestDirectoryPath));
    }

    [Theory]
    [MemberData(nameof(MissingDependencyCases))]
    public async Task ConfigureEntryPoints_WhenDependenciesAreMissing_DoNotInvokeInstaller(
        bool execute,
        PythonPreflightFailure failure
    )
    {
        bool helperPresent = failure != PythonPreflightFailure.HelperMissing;
        var fileSystem = CreatePosixFileSystem(helperPresent);
        var runner = HealthyGenericRunner(ProductResultFor(failure));
        ConfigurationPhase14VerticalSliceService service = CreateConfigurationService(
            fileSystem,
            CreateDoctor(fileSystem, runner)
        );

        ConfigurationPhase14PlanResult result = execute
            ? await service.ConfigureAsync(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            )
            : await service.DryRunConfigureAsync(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            );

        Assert.False(result.PythonPreflightSucceeded);
        Assert.Empty(result.PlanResults);
        Assert.Null(result.PlanResult);
        Assert.All(
            runner.StartSpecs,
            spec =>
            {
                Assert.Equal(SelectedPythonPath, spec.FileName);
                Assert.Equal("-c", spec.Arguments[0]);
                Assert.Equal(TimeSpan.FromSeconds(5), spec.Timeout);
                Assert.NotEqual(HelperPath, spec.FileName);
                Assert.DoesNotContain(
                    spec.Arguments,
                    argument =>
                        argument.Contains("pip install", StringComparison.OrdinalIgnoreCase)
                        || argument.Contains("uv tool install", StringComparison.OrdinalIgnoreCase)
                );
            }
        );
        Assert.Equal(2, runner.StartSpecs.Count);
        Assert.False(fileSystem.FileExists(ExpectedShimPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigureEntryPoints_PreflightDoesNotRequireNotYetGeneratedKeyringShim(
        bool execute
    )
    {
        var fileSystem = CreatePosixFileSystem(helperPresent: true);
        var runner = HealthyGenericRunner(ProductHealthyResult());
        PythonPhase11VerticalSliceService doctor = CreateDoctor(fileSystem, runner);
        ConfigurationPhase14VerticalSliceService service = CreateConfigurationService(
            fileSystem,
            doctor
        );
        Assert.False(fileSystem.FileExists(ExpectedShimPath));

        ConfigurationPhase14PlanResult result = execute
            ? await service.ConfigureAsync(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            )
            : await service.DryRunConfigureAsync(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            );

        Assert.True(result.PythonPreflightSucceeded);
        Assert.False(result.PythonPreflight!.KeyringShim.ExpectedShimExists);
        Assert.False(result.PythonPreflight.KeyringShim.ExpectedShimFirstOnPath);
        Assert.NotEmpty(result.PlanResults);
        Assert.Equal(execute, fileSystem.FileExists(ExpectedShimPath));
    }

    [Fact]
    public async Task ConfigureAsync_OnWindows_CreatesBackendOnlyLayoutAcceptedByDoctor()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        WriteExecutable(fileSystem, @"C:\venv\Scripts\python.exe");
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(GenericFoundResult());
        runner.EnqueueResult(ProductHealthyResult());
        runner.EnqueueResult(GenericFoundResult());
        runner.EnqueueResult(ProductHealthyResult());
        var doctor = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = runner,
                EnvironmentVariableReader = name =>
                    name switch
                    {
                        "LOCALAPPDATA" => @"C:\Users\Test\AppData\Local",
                        "USERPROFILE" => @"C:\Users\Test",
                        "PATH" => @"C:\venv\Scripts;C:\Windows\System32",
                        _ => null,
                    },
                ExpectedKeyringShimPath =
                    @"C:\Users\Test\AppData\Local\azureauth-credprovider\keyring-shim\keyring.exe",
                PythonExecutablePath = @"C:\venv\Scripts\python.exe",
                CurrentDirectoryPath = @"C:\workspace",
                PathListSeparator = ';',
                KeyringExecutableFileName = "keyring.exe",
                EnableProductProbe = true,
            }
        );
        ConfigurationPhase14VerticalSliceService service = CreateConfigurationService(
            fileSystem,
            doctor
        );

        ConfigurationPhase14PlanResult configured = await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        PythonPhase11DoctorResult checkedLayout = await doctor.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(configured.PythonPreflightSucceeded);
        Assert.Single(configured.PlanResults);
        Assert.DoesNotContain(
            configured.PlanResults.SelectMany(static result => result.Changes),
            static change => change.TargetKind == ConfigurationTargetKind.KeyringShim
        );
        Assert.False(checkedLayout.KeyringShim.Applicable);
        Assert.False(checkedLayout.AzureAuthKeyringHelper.Applicable);
        Assert.Equal(
            PythonPhase11AzureAuthKeyringHelperProbeStatus.NotApplicable,
            checkedLayout.AzureAuthKeyringHelper.Status
        );
        Assert.True(checkedLayout.ProductProbe.BackendLoadable);
        Assert.True(checkedLayout.IsReady);
        Assert.Equal(4, runner.StartSpecs.Count);
    }

    public static TheoryData<bool, PythonPreflightFailure> MissingDependencyCases =>
        new()
        {
            { false, PythonPreflightFailure.DistributionMissing },
            { true, PythonPreflightFailure.DistributionMissing },
            { false, PythonPreflightFailure.EntryPointMissing },
            { true, PythonPreflightFailure.EntryPointMissing },
            { false, PythonPreflightFailure.EntryPointMismatch },
            { true, PythonPreflightFailure.EntryPointMismatch },
            { false, PythonPreflightFailure.BackendLoadFailure },
            { true, PythonPreflightFailure.BackendLoadFailure },
            { false, PythonPreflightFailure.HelperMissing },
            { true, PythonPreflightFailure.HelperMissing },
        };

    public enum PythonPreflightFailure
    {
        DistributionMissing,
        EntryPointMissing,
        EntryPointMismatch,
        BackendLoadFailure,
        HelperMissing,
    }

    private static ConfigurationPhase14VerticalSliceService CreateConfigurationService(
        InMemoryFileSystem fileSystem,
        PythonPhase11VerticalSliceService doctor
    ) =>
        new(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = fileSystem.IsPathFullyQualified(@"C:\")
                    ? @"C:\state\phase14"
                    : "/state/phase14",
                EnvironmentVariableReader = name =>
                    fileSystem.IsPathFullyQualified(@"C:\")
                        ? name switch
                        {
                            "LOCALAPPDATA" => @"C:\Users\Test\AppData\Local",
                            "USERPROFILE" => @"C:\Users\Test",
                            _ => null,
                        }
                        : name == "HOME"
                            ? "/home/test"
                            : null,
                ProductExecutablePath = fileSystem.IsPathFullyQualified(@"C:\")
                    ? @"C:\Program Files\AzureAuth\CredProvider\azureauth-credprovider.exe"
                    : "/opt/azureauth-credprovider/azureauth-credprovider",
                WorkspaceDirectoryPath = fileSystem.IsPathFullyQualified(@"C:\")
                    ? @"C:\workspace"
                    : "/workspace",
                PythonDoctorService = doctor,
            }
        );

    private static PythonPhase11VerticalSliceService CreateDoctor(
        InMemoryFileSystem fileSystem,
        FakeProcessRunner runner
    ) =>
        new(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = runner,
                EnvironmentVariableReader = name =>
                    name switch
                    {
                        "HOME" => "/home/test",
                        "PATH" => "/workspace/.venv/bin:/usr/bin",
                        _ => null,
                    },
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = SelectedPythonPath,
                CurrentDirectoryPath = "/workspace",
                PathListSeparator = ':',
                KeyringExecutableFileName = "keyring",
                EnableProductProbe = true,
            }
        );

    private static InMemoryFileSystem CreatePosixFileSystem(bool helperPresent)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        WriteExecutable(fileSystem, SelectedPythonPath);
        if (helperPresent)
        {
            WriteExecutable(fileSystem, HelperPath);
        }

        return fileSystem;
    }

    private static void AssertPrePlanState(InMemoryFileSystem fileSystem)
    {
        Assert.DoesNotContain(
            fileSystem.Files.Keys,
            static path =>
                path.Contains("python-user-ownership-manifest.json", StringComparison.Ordinal)
                || path.EndsWith("/backends/azureauth.py", StringComparison.Ordinal)
        );
        Assert.False(fileSystem.FileExists(ExpectedShimPath));
    }

    private static void WriteExecutable(InMemoryFileSystem fileSystem, string path)
    {
        int separatorIndex = path.LastIndexOfAny(['/', '\\']);
        string directory = path[..separatorIndex];
        fileSystem.CreateDirectory(directory);
        fileSystem.WriteAllText(path, "test executable");
        fileSystem.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
    }

    private static FakeProcessRunner HealthyGenericRunner(ProcessResult productResult)
    {
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(GenericFoundResult());
        runner.EnqueueResult(productResult);
        return runner;
    }

    private static ProcessResult ProductResultFor(PythonPreflightFailure failure) =>
        failure switch
        {
            PythonPreflightFailure.DistributionMissing => ProductResult(
                30,
                "ACP_AZUREAUTH_PRODUCT_PROBE_V1:DISTRIBUTION_MISSING"
            ),
            PythonPreflightFailure.EntryPointMissing => ProductResult(
                31,
                "ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISSING"
            ),
            PythonPreflightFailure.EntryPointMismatch => ProductResult(
                32,
                "ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISMATCH"
            ),
            PythonPreflightFailure.BackendLoadFailure => ProductResult(
                33,
                "ACP_AZUREAUTH_PRODUCT_PROBE_V1:LOAD_FAILURE"
            ),
            PythonPreflightFailure.HelperMissing => ProductHealthyResult(),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };

    private static ProcessResult GenericFoundResult() =>
        new(0, GenericFoundMarker + "\n", string.Empty);

    private static ProcessResult ProductHealthyResult() =>
        ProductResult(0, ProductHealthyMarker);

    private static ProcessResult ProductResult(int exitCode, string marker) =>
        new(exitCode, marker + "\n", string.Empty);

    [Fact]
    public async Task ConfigureAsync_WhenGenericKeyringIsMissing_DoesNotProbeProductPlanOrWrite()
    {
        var fileSystem = CreatePosixFileSystem(helperPresent: true);
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(
            new ProcessResult(
                20,
                "ACP_KEYRING_PROBE_V1:NOT_FOUND\n",
                string.Empty
            )
        );
        ConfigurationPhase14VerticalSliceService service = CreateConfigurationService(
            fileSystem,
            CreateDoctor(fileSystem, runner)
        );
        Dictionary<string, string> filesBefore = fileSystem.Files.ToDictionary();
        HashSet<string> directoriesBefore = [.. fileSystem.Directories];

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.PythonPreflightSucceeded);
        Assert.False(result.PythonPreflight!.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11ProductProbeStatus.NotAttempted,
            result.PythonPreflight.ProductProbe.Status
        );
        Assert.True(result.PythonPreflight.ProductProbe.Enabled);
        Assert.False(result.PythonPreflight.ProductProbe.Attempted);
        ProcessStartSpec genericProbe = Assert.Single(runner.StartSpecs);
        Assert.Contains(
            "importlib.util.find_spec('keyring')",
            genericProbe.Arguments[1],
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "importlib.metadata.distribution",
            genericProbe.Arguments[1],
            StringComparison.Ordinal
        );
        Assert.Empty(result.PlanResults);
        Assert.Null(result.PlanResult);
        Assert.Equal(0, result.ChangeCount);
        Assert.Equal(filesBefore, fileSystem.Files);
        Assert.Equal(directoriesBefore, fileSystem.Directories);
        Assert.False(fileSystem.FileExists(ExpectedShimPath));
    }
}
#pragma warning restore CA1707
