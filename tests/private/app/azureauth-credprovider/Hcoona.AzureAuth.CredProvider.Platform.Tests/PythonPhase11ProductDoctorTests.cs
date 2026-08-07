using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class PythonPhase11ProductDoctorTests
{
    private const string SelectedPythonPath = "/workspace/.venv/bin/python";
    private const string ExpectedShimPath =
        "/home/alice/.local/share/azureauth-credprovider/keyring-shim/keyring";
    private const string ExpectedHelperPath = "/workspace/.venv/bin/azureauth-keyring";
    private const string GenericFoundMarker = "ACP_KEYRING_PROBE_V1:FOUND";
    private const string ProductHealthyMarker = "ACP_AZUREAUTH_PRODUCT_PROBE_V1:HEALTHY";
    private const string DistributionMissingMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:DISTRIBUTION_MISSING";
    private const string EntryPointMissingMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISSING";
    private const string EntryPointMismatchMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:ENTRY_POINT_MISMATCH";
    private const string LoadFailureMarker =
        "ACP_AZUREAUTH_PRODUCT_PROBE_V1:LOAD_FAILURE";

    [Fact]
    public async Task RunDoctorRetainsGenericKeyringFindSpecProbe()
    {
        (PythonPhase11DoctorResult result, FakeProcessRunner runner, _) =
            await RunPosixDoctorAsync(ProductHealthyResult());

        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Collection(
            runner.StartSpecs,
            generic =>
            {
                string script = Assert.IsType<string>(generic.Arguments[1]);
                Assert.Contains("importlib.util.find_spec('keyring')", script, StringComparison.Ordinal);
                Assert.DoesNotContain("importlib.metadata.distribution", script, StringComparison.Ordinal);
            },
            product =>
            {
                string script = Assert.IsType<string>(product.Arguments[1]);
                Assert.Contains("importlib.metadata.distribution", script, StringComparison.Ordinal);
            }
        );
    }

    [Fact]
    public async Task RunDoctorUsesSelectedInterpreterToLocateExactProductDistribution()
    {
        (PythonPhase11DoctorResult result, FakeProcessRunner runner, _) =
            await RunPosixDoctorAsync(ProductHealthyResult());

        ProcessStartSpec productSpec = runner.StartSpecs[1];
        string script = Assert.IsType<string>(productSpec.Arguments[1]);
        Assert.Equal(SelectedPythonPath, productSpec.FileName);
        Assert.Contains(
            "importlib.metadata.distribution('azureauth-credprovider-keyring')",
            script,
            StringComparison.Ordinal
        );
        Assert.Equal(PythonPhase11ProductProbeStatus.Healthy, result.ProductProbe.Status);
        Assert.True(result.ProductProbe.BackendLoadable);
    }

    [Fact]
    public async Task RunDoctorRequiresExactAzureAuthKeyringEntryPoint()
    {
        (_, FakeProcessRunner runner, _) = await RunPosixDoctorAsync(ProductHealthyResult());

        string script = Assert.IsType<string>(runner.StartSpecs[1].Arguments[1]);
        Assert.Contains("'keyring.backends'", script, StringComparison.Ordinal);
        Assert.Contains("'azureauth'", script, StringComparison.Ordinal);
        Assert.Contains(
            "'azureauth_credprovider_keyring.backend:AzureAuthKeyringBackend'",
            script,
            StringComparison.Ordinal
        );
        Assert.Contains("if not backend_entry_points:", script, StringComparison.Ordinal);
        Assert.Contains("len(backend_entry_points) != 1", script, StringComparison.Ordinal);
        Assert.Contains("entry_point.name != 'azureauth'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("named_entry_points", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDoctorLoadsOnlyExpectedProductEntryPointWithoutCredentialAccess()
    {
        (_, FakeProcessRunner runner, _) = await RunPosixDoctorAsync(ProductHealthyResult());

        string script = Assert.IsType<string>(runner.StartSpecs[1].Arguments[1]);
        Assert.Contains("backend_type=entry_point.load()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AzureAuthKeyringBackend()", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".get_password(", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".get_credential(", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".set_password(", script, StringComparison.Ordinal);
        Assert.DoesNotContain(".delete_password(", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDoctorVerifiesAzureAuthBackendClassSubclassAndContract()
    {
        (PythonPhase11DoctorResult result, FakeProcessRunner runner, _) =
            await RunPosixDoctorAsync(ProductHealthyResult());

        string script = Assert.IsType<string>(runner.StartSpecs[1].Arguments[1]);
        Assert.Contains("isinstance(backend_type,type)", script, StringComparison.Ordinal);
        Assert.Contains("issubclass(backend_type,KeyringBackend)", script, StringComparison.Ordinal);
        Assert.Contains("backend_type.__name__", script, StringComparison.Ordinal);
        Assert.Contains("backend_type.__module__", script, StringComparison.Ordinal);
        Assert.Contains(
            "('get_password','get_credential','set_password','delete_password')",
            script,
            StringComparison.Ordinal
        );
        Assert.True(result.ProductProbe.BackendLoadable);
        Assert.Null(result.ProductProbe.FailureMessage);
    }

    [Fact]
    public async Task RunDoctorRejectsExactEntrypointAbstractSubclassWithoutInstantiation()
    {
        (_, FakeProcessRunner runner, _) = await RunPosixDoctorAsync(ProductHealthyResult());
        string productProbeScript = Assert.IsType<string>(runner.StartSpecs[1].Arguments[1]);
        string harness =
            """
            import abc,importlib.metadata,sys,types
            keyring_module=types.ModuleType('keyring')
            backend_module=types.ModuleType('keyring.backend')
            class KeyringBackend(abc.ABC):
                @property
                @abc.abstractmethod
                def priority(self):
                    raise NotImplementedError
            backend_module.KeyringBackend=KeyringBackend
            keyring_module.backend=backend_module
            sys.modules['keyring']=keyring_module
            sys.modules['keyring.backend']=backend_module
            product_module=types.ModuleType('azureauth_credprovider_keyring.backend')
            class AzureAuthKeyringBackend(KeyringBackend):
                def get_password(self,*args): return None
                def get_credential(self,*args): return None
                def set_password(self,*args): return None
                def delete_password(self,*args): return None
            AzureAuthKeyringBackend.__module__='azureauth_credprovider_keyring.backend'
            product_module.AzureAuthKeyringBackend=AzureAuthKeyringBackend
            sys.modules['azureauth_credprovider_keyring.backend']=product_module
            class EntryPoint:
                group='keyring.backends'
                name='azureauth'
                value='azureauth_credprovider_keyring.backend:AzureAuthKeyringBackend'
                def load(self): return AzureAuthKeyringBackend
            class Distribution:
                entry_points=[EntryPoint()]
            importlib.metadata.distribution=lambda name: Distribution()
            import base64
            exec(base64.b64decode(
            """
            + "'"
            + Convert.ToBase64String(Encoding.UTF8.GetBytes(productProbeScript))
            + "'))";
        string pythonCommand = OperatingSystem.IsWindows() ? "python" : "python3";
        ProcessResult result = await new SystemProcessRunner()
            .RunAsync(
                new ProcessStartSpec(
                    pythonCommand,
                    ["-c", harness],
                    timeout: TimeSpan.FromSeconds(10)
                ),
                TestContext.Current.CancellationToken
            );
        Assert.SkipWhen(
            result.Status == ProcessExecutionStatus.LaunchFailure,
            $"The {pythonCommand} command is required for this integration test."
        );

        Assert.Equal(33, result.ExitCode);
        Assert.Equal(
            LoadFailureMarker + "\n",
            result.StandardOutput.Replace("\r\n", "\n", StringComparison.Ordinal)
        );
        Assert.Equal(string.Empty, result.StandardError);
        Assert.Contains("import importlib.metadata,inspect,sys", productProbeScript);
        Assert.Contains("not inspect.isabstract(backend_type)", productProbeScript);
        Assert.DoesNotContain("backend_type()", productProbeScript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDoctorReturnsLoadFailureWhenBackendTypeContractIsInvalid()
    {
        (PythonPhase11DoctorResult result, _, _) = await RunPosixDoctorAsync(
            ProductResult(33, LoadFailureMarker)
        );

        Assert.False(result.ProductProbe.BackendLoadable);
        Assert.Equal(PythonPhase11ProductProbeStatus.LoadFailure, result.ProductProbe.Status);
        Assert.Equal(
            "The AzureAuth keyring backend could not be loaded or did not satisfy its required contract.",
            result.ProductProbe.FailureMessage
        );
    }

    [Fact]
    public void ProductProbeStatusValuesAndMarkersRemainStable()
    {
        Assert.Equal(0, (int)PythonPhase11ProductProbeStatus.NotAttempted);
        Assert.Equal(1, (int)PythonPhase11ProductProbeStatus.Healthy);
        Assert.Equal(2, (int)PythonPhase11ProductProbeStatus.DistributionMissing);
        Assert.Equal(3, (int)PythonPhase11ProductProbeStatus.EntryPointMissing);
        Assert.Equal(4, (int)PythonPhase11ProductProbeStatus.EntryPointMismatch);
        Assert.Equal(5, (int)PythonPhase11ProductProbeStatus.LoadFailure);
        Assert.Equal(6, (int)PythonPhase11ProductProbeStatus.LaunchFailure);
        Assert.Equal(7, (int)PythonPhase11ProductProbeStatus.TimedOut);
        Assert.Equal(8, (int)PythonPhase11ProductProbeStatus.UnexpectedNonZeroExit);
        Assert.Equal(9, (int)PythonPhase11ProductProbeStatus.OutputTooLarge);
        Assert.Equal(10, (int)PythonPhase11ProductProbeStatus.InvalidOutput);
    }

    [Theory]
    [InlineData(30, DistributionMissingMarker, PythonPhase11ProductProbeStatus.DistributionMissing)]
    [InlineData(31, EntryPointMissingMarker, PythonPhase11ProductProbeStatus.EntryPointMissing)]
    [InlineData(32, EntryPointMismatchMarker, PythonPhase11ProductProbeStatus.EntryPointMismatch)]
    [InlineData(33, LoadFailureMarker, PythonPhase11ProductProbeStatus.LoadFailure)]
    [InlineData(0, ProductHealthyMarker, PythonPhase11ProductProbeStatus.Healthy)]
    public async Task RunDoctorProductProbeStatusMatrixReturnsExpectedAllowlistedResult(
        int exitCode,
        string marker,
        PythonPhase11ProductProbeStatus expectedStatus
    )
    {
        (PythonPhase11DoctorResult result, _, _) = await RunPosixDoctorAsync(
            ProductResult(exitCode, marker)
        );

        Assert.Equal(expectedStatus, result.ProductProbe.Status);
        Assert.Equal(
            expectedStatus == PythonPhase11ProductProbeStatus.Healthy,
            result.ProductProbe.BackendLoadable
        );
        Assert.Equal(SelectedPythonPath, result.ProductProbe.PythonExecutablePath);
        Assert.True(result.ProductProbe.Attempted);
    }

    [Fact]
    public async Task RunDoctorDoesNotExposeContaminatedProductProbeOutput()
    {
        const string secret = "AZURE_TOKEN=do-not-leak";
        var contaminated = new ProcessResult(
            33,
            LoadFailureMarker + "\nTraceback: " + secret,
            "sitecustomize: " + secret
        );

        (PythonPhase11DoctorResult result, _, _) = await RunPosixDoctorAsync(contaminated);

        Assert.Equal(PythonPhase11ProductProbeStatus.InvalidOutput, result.ProductProbe.Status);
        Assert.Equal(
            "The AzureAuth keyring product probe did not produce a recognized marker and exit-code pair.",
            result.ProductProbe.FailureMessage
        );
        Assert.DoesNotContain("Traceback", result.ProductProbe.FailureMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, result.ProductProbe.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDoctorProductProbeUsesFiveSecondTimeoutAndFourKiBStreamLimits()
    {
        (_, FakeProcessRunner runner, _) = await RunPosixDoctorAsync(ProductHealthyResult());

        Assert.Equal(2, runner.StartSpecs.Count);
        Assert.All(
            runner.StartSpecs,
            spec =>
            {
                Assert.Equal(SelectedPythonPath, spec.FileName);
                Assert.Equal("-c", spec.Arguments[0]);
                Assert.Equal(TimeSpan.FromSeconds(5), spec.Timeout);
                Assert.Equal(4096, spec.OutputCaptureOptions.StandardOutputByteLimit);
                Assert.Equal(4096, spec.OutputCaptureOptions.StandardErrorByteLimit);
            }
        );
    }

    [Fact]
    public async Task RunDoctorOnWindowsReportsShimChecksNotApplicableAndRequiresBackend()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        CreateDirectory(fileSystem, @"C:\venv\Scripts");
        WriteExecutable(fileSystem, @"C:\venv\Scripts\python.exe");
        var runner = HealthyGenericRunner(ProductResult(30, DistributionMissingMarker));
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = runner,
                EnvironmentVariableReader = name =>
                    name == "PATH" ? @"C:\venv\Scripts;C:\Windows\System32" : null,
                ExpectedKeyringShimPath =
                    @"C:\Users\Alice\AppData\Local\azureauth-credprovider\keyring-shim\keyring.exe",
                PythonExecutablePath = @"C:\venv\Scripts\python.exe",
                CurrentDirectoryPath = @"C:\workspace",
                PathListSeparator = ';',
                KeyringExecutableFileName = "keyring.exe",
                EnableProductProbe = true,
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.False(result.KeyringShim.Applicable);
        Assert.False(result.AzureAuthKeyringHelper.Applicable);
        Assert.Equal(
            PythonPhase11AzureAuthKeyringHelperProbeStatus.NotApplicable,
            result.AzureAuthKeyringHelper.Status
        );
        Assert.Equal(PythonPhase11ProductProbeStatus.DistributionMissing, result.ProductProbe.Status);
        Assert.False(result.IsReady);
    }

    [Theory]
    [InlineData(@"C:\Users\Alice\AppData\Local\azureauth\keyring.exe")]
    [InlineData(@"\\server\share\azureauth\keyring.exe")]
    [InlineData(@"\\?\C:\Users\Alice\AppData\Local\azureauth\keyring.exe")]
    public async Task RunDoctorUsesWindowsFileSystemSemanticsForShimAndHelperApplicability(
        string expectedShimPath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        CreateDirectory(fileSystem, @"C:\venv\Scripts");
        WriteExecutable(fileSystem, @"C:\venv\Scripts\python.exe");
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = HealthyGenericRunner(ProductHealthyResult()),
                EnvironmentVariableReader = name =>
                    name == "PATH" ? @"C:\venv\Scripts;C:\Windows\System32" : null,
                ExpectedKeyringShimPath = expectedShimPath,
                PythonExecutablePath = @"C:\venv\Scripts\python.exe",
                CurrentDirectoryPath = @"C:\workspace",
                PathListSeparator = ';',
                KeyringExecutableFileName = "keyring.exe",
                EnableProductProbe = true,
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.False(result.KeyringShim.Applicable);
        Assert.False(result.AzureAuthKeyringHelper.Applicable);
        Assert.Equal(
            PythonPhase11AzureAuthKeyringHelperProbeStatus.NotApplicable,
            result.AzureAuthKeyringHelper.Status
        );
        Assert.True(result.IsConfigurationPreflightReady);
    }

    [Fact]
    public async Task RunDoctorOnPosixReportsConcreteShimAndHelperPathsWithoutExecutingHelper()
    {
        (PythonPhase11DoctorResult result, FakeProcessRunner runner, _) =
            await RunPosixDoctorAsync(ProductHealthyResult());

        Assert.True(result.KeyringShim.Applicable);
        Assert.True(result.AzureAuthKeyringHelper.Applicable);
        Assert.Equal(ExpectedHelperPath, result.AzureAuthKeyringHelper.ExpectedExecutablePath);
        Assert.Equal(ExpectedHelperPath, result.AzureAuthKeyringHelper.ResolvedExecutablePath);
        Assert.Equal(
            PythonPhase11AzureAuthKeyringHelperProbeStatus.Found,
            result.AzureAuthKeyringHelper.Status
        );
        Assert.DoesNotContain(
            runner.StartSpecs,
            spec => string.Equals(spec.FileName, ExpectedHelperPath, StringComparison.Ordinal)
        );
        Assert.True(result.IsReady);
    }

    [Fact]
    public async Task RunDoctorOnPosixReturnsHelperMissingWhenAzureAuthKeyringHelperIsMissing()
    {
        (PythonPhase11DoctorResult result, FakeProcessRunner runner, _) =
            await RunPosixDoctorAsync(ProductHealthyResult(), createHelper: false);

        Assert.Equal(
            PythonPhase11AzureAuthKeyringHelperProbeStatus.Missing,
            result.AzureAuthKeyringHelper.Status
        );
        Assert.Equal(ExpectedHelperPath, result.AzureAuthKeyringHelper.ExpectedExecutablePath);
        Assert.Null(result.AzureAuthKeyringHelper.ResolvedExecutablePath);
        Assert.False(result.IsReady);
        Assert.Equal(2, runner.StartSpecs.Count);
    }

    [Fact]
    public async Task RunDoctorOnPosixRejectsDifferentFirstPathResolvedHelper()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string differentHelperPath = "/other/bin/azureauth-keyring";
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        CreateDirectory(fileSystem, "/other/bin");
        CreateDirectory(
            fileSystem,
            "/home/alice/.local/share/azureauth-credprovider/keyring-shim"
        );
        WriteExecutable(fileSystem, SelectedPythonPath);
        WriteExecutable(fileSystem, ExpectedHelperPath);
        WriteExecutable(fileSystem, differentHelperPath);
        WriteExecutable(fileSystem, ExpectedShimPath);
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = HealthyGenericRunner(ProductHealthyResult()),
                EnvironmentVariableReader = name =>
                    name == "PATH"
                        ? "/other/bin:/workspace/.venv/bin:/home/alice/.local/share/azureauth-credprovider/keyring-shim"
                        : null,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = SelectedPythonPath,
                CurrentDirectoryPath = "/workspace",
                PathListSeparator = ':',
                EnableProductProbe = true,
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ExpectedHelperPath, result.AzureAuthKeyringHelper.ExpectedExecutablePath);
        Assert.Equal(differentHelperPath, result.AzureAuthKeyringHelper.ResolvedExecutablePath);
        Assert.Equal(
            PythonPhase11AzureAuthKeyringHelperProbeStatus.PathMismatch,
            result.AzureAuthKeyringHelper.Status
        );
        Assert.False(result.IsConfigurationPreflightReady);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task RunDoctorOnPosixAcceptsFirstPathResolvedHelperSymlinkToExpectedHelper()
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && File.Exists("/bin/sh"),
            "This integration test requires Linux and /bin/sh."
        );
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            "python-helper-same-file-integration",
            Guid.NewGuid().ToString("N")
        );
        string selectedDirectory = Path.Combine(directory, "selected");
        string aliasDirectory = Path.Combine(directory, "alias");
        Directory.CreateDirectory(selectedDirectory);
        Directory.CreateDirectory(aliasDirectory);
        try
        {
            string pythonPath = Path.Combine(selectedDirectory, "python");
            WriteShellExecutable(
                pythonPath,
                "#!/bin/sh\n"
                    + "case \"$2\" in\n"
                    + "  *\"importlib.util.find_spec('keyring')\"*) "
                    + $"printf '%s\\n' '{GenericFoundMarker}'; exit 0 ;;\n"
                    + "  *\"importlib.metadata.distribution('azureauth-credprovider-keyring')\"*) "
                    + $"printf '%s\\n' '{ProductHealthyMarker}'; exit 0 ;;\n"
                    + "  *) exit 99 ;;\n"
                    + "esac\n"
            );
            string expectedHelperPath = Path.Combine(selectedDirectory, "azureauth-keyring");
            WriteShellExecutable(expectedHelperPath, "#!/bin/sh\nexit 97\n");
            string helperAliasPath = Path.Combine(aliasDirectory, "azureauth-keyring");
            File.CreateSymbolicLink(helperAliasPath, expectedHelperPath);
            string shimPath = Path.Combine(directory, "keyring");
            WriteShellExecutable(shimPath, "#!/bin/sh\nexit 98\n");
            var service = new PythonPhase11VerticalSliceService(
                new PythonPhase11VerticalSliceOptions
                {
                    ProcessRunner = new SystemProcessRunner(),
                    EnvironmentVariableReader = name =>
                        name == "PATH"
                            ? string.Join(Path.PathSeparator, aliasDirectory, selectedDirectory)
                            : null,
                    ExpectedKeyringShimPath = shimPath,
                    PythonExecutablePath = pythonPath,
                    CurrentDirectoryPath = directory,
                    EnableProductProbe = true,
                }
            );

            PythonPhase11DoctorResult result = await service.RunDoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(helperAliasPath, result.AzureAuthKeyringHelper.ResolvedExecutablePath);
            Assert.Equal(
                PythonPhase11AzureAuthKeyringHelperProbeStatus.Found,
                result.AzureAuthKeyringHelper.Status
            );
            Assert.True(result.IsConfigurationPreflightReady);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunDoctorWithFakeRunnerUsesSelectedInterpreterAndVersionedProductProtocol()
    {
        (PythonPhase11DoctorResult result, FakeProcessRunner runner, _) =
            await RunPosixDoctorAsync(ProductHealthyResult());

        Assert.Collection(
            runner.StartSpecs,
            generic => Assert.Contains("ACP_KEYRING_PROBE_V1", generic.Arguments[1]),
            product => Assert.Contains("ACP_AZUREAUTH_PRODUCT_PROBE_V1", product.Arguments[1])
        );
        Assert.All(runner.StartSpecs, spec => Assert.Equal(SelectedPythonPath, spec.FileName));
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.True(result.ProductProbe.BackendLoadable);
        Assert.True(result.IsReady);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task RunDoctorWithSystemProcessRunnerParsesExecutableWrapperWithoutInvokingPython()
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && File.Exists("/bin/sh"),
            "This integration test requires Linux and /bin/sh."
        );
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            "python-product-doctor-integration",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string wrapperPath = Path.Combine(directory, "selected-interpreter");
            WriteShellExecutable(
                wrapperPath,
                "#!/bin/sh\n"
                    + "case \"$2\" in\n"
                    + "  *\"importlib.util.find_spec('keyring')\"*) "
                    + $"printf '%s\\n' '{GenericFoundMarker}'; exit 0 ;;\n"
                    + "  *\"importlib.metadata.distribution('azureauth-credprovider-keyring')\"*) "
                    + $"printf '%s\\n' '{ProductHealthyMarker}'; exit 0 ;;\n"
                    + "  *) exit 99 ;;\n"
                    + "esac\n"
            );
            string helperPath = Path.Combine(directory, "azureauth-keyring");
            WriteShellExecutable(helperPath, "#!/bin/sh\nexit 97\n");
            string shimPath = Path.Combine(directory, "keyring");
            WriteShellExecutable(shimPath, "#!/bin/sh\nexit 98\n");
            var service = new PythonPhase11VerticalSliceService(
                new PythonPhase11VerticalSliceOptions
                {
                    ProcessRunner = new SystemProcessRunner(),
                    EnvironmentVariableReader = name =>
                        name switch
                        {
                            "HOME" => directory,
                            "PATH" => directory,
                            _ => null,
                        },
                    ExpectedKeyringShimPath = shimPath,
                    PythonExecutablePath = wrapperPath,
                    CurrentDirectoryPath = directory,
                    PathListSeparator = Path.PathSeparator,
                    EnableProductProbe = true,
                }
            );

            PythonPhase11DoctorResult result = await service.RunDoctorAsync(
                TestContext.Current.CancellationToken
            );

            Assert.Equal(wrapperPath, result.ProductProbe.PythonExecutablePath);
            Assert.Equal(PythonPhase11ProductProbeStatus.Healthy, result.ProductProbe.Status);
            Assert.Equal(helperPath, result.AzureAuthKeyringHelper.ResolvedExecutablePath);
            Assert.True(result.IsReady);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(ProductProcessFailureCases))]
    public async Task RunDoctorMapsProductProcessFailuresToAllowlistedStatuses(
        ProcessResult processResult,
        PythonPhase11ProductProbeStatus expectedStatus,
        string expectedMessage
    )
    {
        (PythonPhase11DoctorResult result, _, _) = await RunPosixDoctorAsync(processResult);

        Assert.False(result.ProductProbe.BackendLoadable);
        Assert.Equal(expectedStatus, result.ProductProbe.Status);
        Assert.Equal(expectedMessage, result.ProductProbe.FailureMessage);
        Assert.DoesNotContain("sensitive-child-output", result.ProductProbe.FailureMessage);
    }

    public static TheoryData<
        ProcessResult,
        PythonPhase11ProductProbeStatus,
        string
    > ProductProcessFailureCases =>
        new()
        {
            {
                ProcessResult.LaunchFailure(),
                PythonPhase11ProductProbeStatus.LaunchFailure,
                "The selected Python interpreter could not be launched for the AzureAuth keyring product probe."
            },
            {
                ProcessResult.TimedOut("sensitive-child-output", "sensitive-child-output"),
                PythonPhase11ProductProbeStatus.TimedOut,
                "The AzureAuth keyring product probe timed out."
            },
            {
                ProcessResult.OutputTooLarge(
                    "sensitive-child-output",
                    "sensitive-child-output"
                ),
                PythonPhase11ProductProbeStatus.OutputTooLarge,
                "The AzureAuth keyring product probe exceeded its output limit."
            },
            {
                ProcessResult.InvalidOutput(
                    "sensitive-child-output",
                    "sensitive-child-output"
                ),
                PythonPhase11ProductProbeStatus.InvalidOutput,
                "The AzureAuth keyring product probe produced invalid output."
            },
            {
                new ProcessResult(47, "sensitive-child-output", "sensitive-child-output"),
                PythonPhase11ProductProbeStatus.UnexpectedNonZeroExit,
                "The AzureAuth keyring product probe exited unexpectedly with code 47."
            },
        };

    private static async Task<(
        PythonPhase11DoctorResult Result,
        FakeProcessRunner Runner,
        InMemoryFileSystem FileSystem
    )> RunPosixDoctorAsync(ProcessResult productResult, bool createHelper = true)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        CreateDirectory(
            fileSystem,
            "/home/alice/.local/share/azureauth-credprovider/keyring-shim"
        );
        WriteExecutable(fileSystem, SelectedPythonPath);
        WriteExecutable(fileSystem, ExpectedShimPath);
        if (createHelper)
        {
            WriteExecutable(fileSystem, ExpectedHelperPath);
        }

        FakeProcessRunner runner = HealthyGenericRunner(productResult);
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = runner,
                EnvironmentVariableReader = name =>
                    name switch
                    {
                        "HOME" => "/home/alice",
                        "PATH" =>
                            "/home/alice/.local/share/azureauth-credprovider/keyring-shim:/workspace/.venv/bin:/usr/bin",
                        _ => null,
                    },
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = SelectedPythonPath,
                CurrentDirectoryPath = "/workspace",
                PathListSeparator = ':',
                EnableProductProbe = true,
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );
        return (result, runner, fileSystem);
    }

    private static FakeProcessRunner HealthyGenericRunner(ProcessResult productResult)
    {
        var runner = new FakeProcessRunner();
        runner.EnqueueResult(new ProcessResult(0, GenericFoundMarker + "\n", string.Empty));
        runner.EnqueueResult(productResult);
        return runner;
    }

    private static ProcessResult ProductHealthyResult() =>
        ProductResult(0, ProductHealthyMarker);

    private static ProcessResult ProductResult(int exitCode, string marker) =>
        new(exitCode, marker + "\n", string.Empty);

    private static void CreateDirectory(InMemoryFileSystem fileSystem, string path) =>
        fileSystem.CreateDirectory(path);

    private static void WriteExecutable(InMemoryFileSystem fileSystem, string path)
    {
        fileSystem.WriteAllText(path, "#!/bin/sh\n");
        fileSystem.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void WriteShellExecutable(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
    }

    [Fact]
    public async Task RunDoctorProductProbeScriptPinsCompletePredicateConjunction()
    {
        (PythonPhase11DoctorResult result, FakeProcessRunner runner, _) =
            await RunPosixDoctorAsync(ProductHealthyResult());

        string script = Assert.IsType<string>(runner.StartSpecs[1].Arguments[1]);
        Assert.Contains(
            "backend_entry_points=[entry_point for entry_point in distribution.entry_points if entry_point.group == 'keyring.backends']",
            script,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "if not backend_entry_points:",
            script,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "if len(backend_entry_points) != 1:",
            script,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "if entry_point.name != 'azureauth' or entry_point.value != 'azureauth_credprovider_keyring.backend:AzureAuthKeyringBackend':",
            script,
            StringComparison.Ordinal
        );
        const string completePredicate =
            "valid=(isinstance(backend_type,type) and issubclass(backend_type,KeyringBackend) and not inspect.isabstract(backend_type) and backend_type.__name__ == 'AzureAuthKeyringBackend' and backend_type.__module__ == 'azureauth_credprovider_keyring.backend' and all(callable(getattr(backend_type,method,None)) for method in contract_methods))";
        Assert.Contains(completePredicate, script, StringComparison.Ordinal);
        Assert.Contains("if not valid:", script, StringComparison.Ordinal);
        Assert.Equal(PythonPhase11ProductProbeStatus.Healthy, result.ProductProbe.Status);
    }

    [Theory]
    [InlineData(0, DistributionMissingMarker)]
    [InlineData(30, ProductHealthyMarker)]
    [InlineData(31, EntryPointMismatchMarker)]
    [InlineData(32, LoadFailureMarker)]
    [InlineData(33, EntryPointMissingMarker)]
    public async Task RunDoctorProductProbeKnownExitWithWrongMarkerReturnsInvalidOutput(
        int exitCode,
        string wrongMarker
    )
    {
        (PythonPhase11DoctorResult result, _, _) = await RunPosixDoctorAsync(
            ProductResult(exitCode, wrongMarker)
        );

        Assert.Equal(PythonPhase11ProductProbeStatus.InvalidOutput, result.ProductProbe.Status);
        Assert.False(result.ProductProbe.BackendLoadable);
        Assert.Equal(
            "The AzureAuth keyring product probe did not produce a recognized marker and exit-code pair.",
            result.ProductProbe.FailureMessage
        );
    }

    [Fact]
    public async Task RunDoctorOnPosixTreatsPresentNonExecutableHelperAsMissing()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        CreateDirectory(
            fileSystem,
            "/home/alice/.local/share/azureauth-credprovider/keyring-shim"
        );
        WriteExecutable(fileSystem, SelectedPythonPath);
        WriteExecutable(fileSystem, ExpectedShimPath);
        fileSystem.WriteAllText(ExpectedHelperPath, "#!/bin/sh\n");
        fileSystem.SetUnixFileMode(
            ExpectedHelperPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite
        );
        FakeProcessRunner runner = HealthyGenericRunner(ProductHealthyResult());
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = runner,
                EnvironmentVariableReader = name =>
                    name switch
                    {
                        "HOME" => "/home/alice",
                        "PATH" =>
                            "/home/alice/.local/share/azureauth-credprovider/keyring-shim:/workspace/.venv/bin:/usr/bin",
                        _ => null,
                    },
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = SelectedPythonPath,
                CurrentDirectoryPath = "/workspace",
                PathListSeparator = ':',
                EnableProductProbe = true,
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(fileSystem.FileExists(ExpectedHelperPath));
        Assert.False(fileSystem.IsExecutableFile(ExpectedHelperPath));
        Assert.Equal(ExpectedHelperPath, result.AzureAuthKeyringHelper.ExpectedExecutablePath);
        Assert.Null(result.AzureAuthKeyringHelper.ResolvedExecutablePath);
        Assert.Equal(
            PythonPhase11AzureAuthKeyringHelperProbeStatus.Missing,
            result.AzureAuthKeyringHelper.Status
        );
        Assert.False(result.IsConfigurationPreflightReady);
        Assert.Equal(2, runner.StartSpecs.Count);
    }

    [Theory]
    [MemberData(nameof(RemainingIncorrectProductProtocolPairs))]
    public async Task RunDoctorProductProbeKnownExitWithEveryRemainingWrongMarkerReturnsInvalidOutput(
        int exitCode,
        string wrongMarker
    )
    {
        (PythonPhase11DoctorResult result, _, _) = await RunPosixDoctorAsync(
            ProductResult(exitCode, wrongMarker)
        );

        Assert.Equal(PythonPhase11ProductProbeStatus.InvalidOutput, result.ProductProbe.Status);
        Assert.False(result.ProductProbe.BackendLoadable);
        Assert.Equal(
            "The AzureAuth keyring product probe did not produce a recognized marker and exit-code pair.",
            result.ProductProbe.FailureMessage
        );
    }

    public static TheoryData<int, string> RemainingIncorrectProductProtocolPairs
    {
        get
        {
            string[] allMarkers =
            [
                ProductHealthyMarker,
                DistributionMissingMarker,
                EntryPointMissingMarker,
                EntryPointMismatchMarker,
                LoadFailureMarker,
            ];
            (int ExitCode, string CorrectMarker, string AlreadyCoveredWrongMarker)[] protocols =
            [
                (0, ProductHealthyMarker, DistributionMissingMarker),
                (30, DistributionMissingMarker, ProductHealthyMarker),
                (31, EntryPointMissingMarker, EntryPointMismatchMarker),
                (32, EntryPointMismatchMarker, LoadFailureMarker),
                (33, LoadFailureMarker, EntryPointMissingMarker),
            ];
            var data = new TheoryData<int, string>();

            foreach (
                (
                    int exitCode,
                    string correctMarker,
                    string alreadyCoveredWrongMarker
                ) in protocols
            )
            {
                foreach (string marker in allMarkers)
                {
                    if (
                        !string.Equals(marker, correctMarker, StringComparison.Ordinal)
                        && !string.Equals(
                            marker,
                            alreadyCoveredWrongMarker,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        data.Add(exitCode, marker);
                    }
                }
            }

            return data;
        }
    }
}
