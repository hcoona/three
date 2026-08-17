using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class PythonPhase11EnvironmentDiscoveryTests
{
    private const string ExpectedShimPath =
        "/home/alice/.local/share/azureauth-credprovider/keyring-shim/keyring";
    private const string ExpectedShimDirectory =
        "/home/alice/.local/share/azureauth-credprovider/keyring-shim";
    private const string ExpectedWindowsShimPath =
        "C:/Users/Alice/AppData/Local/azureauth-credprovider/keyring-shim/keyring.exe";

    [Fact]
    public async Task DoctorDiscoversPythonEnvironmentHintsWithoutWritingUserState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv");
        CreateDirectory(fileSystem, "/workspace/.tox/py311");
        CreateDirectory(fileSystem, "/workspace/.nox/tests");
        CreateDirectory(fileSystem, "/home/alice/.local/pipx/venvs/twine");
        CreateDirectory(fileSystem, "/home/alice/.local/bin");
        fileSystem.WriteAllText("/home/alice/.local/bin/twine", "#!/bin/sh\n");
        CreateDirectory(fileSystem, "/workspace/.uv-env");
        CreateDirectory(fileSystem, "/home/alice/.local/share/uv/tools/keyring");

        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["VIRTUAL_ENV"] = "/workspace/.venv",
                ["TOX_ENV_DIR"] = "/workspace/.tox/py311",
                ["NOX_ENV_DIR"] = "/workspace/.nox/tests",
                ["PIPX_HOME"] = "/home/alice/.local/pipx",
                ["PIPX_BIN_DIR"] = "/home/alice/.local/bin",
                ["UV_PROJECT_ENVIRONMENT"] = "/workspace/.uv-env",
                ["UV_TOOL_DIR"] = "/home/alice/.local/share/uv/tools/keyring",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
            }
        );
        fileSystem.Calls.Clear();

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.ActiveVirtualEnvironmentDetected);
        Assert.True(result.ToxEnvironmentDetected);
        Assert.True(result.NoxEnvironmentDetected);
        Assert.True(result.PipxTwineDetected);
        Assert.True(result.UvEnvironmentDetected);
        Assert.All(result.EnvironmentProbes, probe => Assert.True(probe.Exists));
        AssertNoFilesystemMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task DoctorReportsWhetherProductKeyringShimWinsPathResolution()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, ExpectedShimDirectory);
        CreateDirectory(fileSystem, "/usr/bin");
        WriteExecutable(fileSystem, ExpectedShimPath);
        WriteExecutable(fileSystem, "/usr/bin/keyring");
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["PATH"] = ExpectedShimDirectory + ":/usr/bin",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringShim.ExpectedShimExists);
        Assert.True(result.KeyringShim.AnyKeyringExecutableOnPath);
        Assert.True(result.KeyringShim.ExpectedShimFirstOnPath);
        Assert.Equal(ExpectedShimPath, result.KeyringShim.FirstKeyringExecutablePath);
    }

    [Fact]
    public async Task DoctorReportsPathShadowedProductKeyringShim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, ExpectedShimDirectory);
        CreateDirectory(fileSystem, "/usr/bin");
        WriteExecutable(fileSystem, ExpectedShimPath);
        WriteExecutable(fileSystem, "/usr/bin/keyring");
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["PATH"] = "/usr/bin:" + ExpectedShimDirectory,
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringShim.ExpectedShimExists);
        Assert.True(result.KeyringShim.AnyKeyringExecutableOnPath);
        Assert.False(result.KeyringShim.ExpectedShimFirstOnPath);
        Assert.Equal("/usr/bin/keyring", result.KeyringShim.FirstKeyringExecutablePath);
    }

    [Fact]
    public async Task DoctorReportsCurrentDirectoryPathShadowing()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, ExpectedShimDirectory);
        CreateDirectory(fileSystem, "/workspace");
        WriteExecutable(fileSystem, ExpectedShimPath);
        WriteExecutable(fileSystem, "/workspace/keyring");
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["PATH"] = ":" + ExpectedShimDirectory,
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                CurrentDirectoryPath = "/workspace",
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.False(result.KeyringShim.ExpectedShimFirstOnPath);
        Assert.Equal("/workspace/keyring", result.KeyringShim.FirstKeyringExecutablePath);
    }

    [Fact]
    public async Task DoctorResolvesRelativeAndEmptyPathEntriesFromModeledCurrentDirectory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, ExpectedShimDirectory);
        CreateDirectory(fileSystem, "/workspace/tools");
        WriteExecutable(fileSystem, ExpectedShimPath);
        WriteExecutable(fileSystem, "/workspace/tools/keyring");
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["PATH"] = "tools::" + ExpectedShimDirectory + ":",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                CurrentDirectoryPath = "/workspace",
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            ["/workspace/tools", "/workspace", ExpectedShimDirectory, "/workspace"],
            result.KeyringShim.PathDirectories
        );
        Assert.Equal(
            "/workspace/tools/keyring",
            result.KeyringShim.FirstKeyringExecutablePath
        );
    }

    [Fact]
    public async Task DoctorSkipsNonExecutablePosixPathCandidate()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, ExpectedShimDirectory);
        CreateDirectory(fileSystem, "/untrusted");
        fileSystem.WriteAllText("/untrusted/keyring", "#!/bin/sh\n");
        WriteExecutable(fileSystem, ExpectedShimPath);
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["PATH"] = "/untrusted:" + ExpectedShimDirectory,
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringShim.ExpectedShimFirstOnPath);
        Assert.Equal(ExpectedShimPath, result.KeyringShim.FirstKeyringExecutablePath);
    }

    [Fact]
    public async Task DoctorAsksSelectedInterpreterWhetherKeyringIsResolvable()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        WriteExecutable(fileSystem, "/workspace/.venv/bin/python");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?> { ["HOME"] = "/home/alice" }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = "/workspace/.venv/bin/python",
            }
        );
        fileSystem.Calls.Clear();

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.True(result.KeyringModuleProbe.PythonExecutableExists);
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleFound,
            result.KeyringModuleProbe.Status
        );
        ProcessStartSpec startSpec = Assert.Single(processRunner.StartSpecs);
        Assert.Equal("/workspace/.venv/bin/python", startSpec.FileName);
        Assert.Equal(
            [
                "-c",
                KeyringModuleProbeScript,
            ],
            startSpec.Arguments
        );
        Assert.Equal(TimeSpan.FromSeconds(5), startSpec.Timeout);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardErrorByteLimit);
        AssertNoFilesystemMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task DoctorResolvesPosixVirtualEnvironmentPythonBeforePathLookup()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        WriteExecutable(fileSystem, "/workspace/.venv/bin/python");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["VIRTUAL_ENV"] = "/workspace/.venv",
                ["PATH"] = "/usr/bin:/bin",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal("/workspace/.venv/bin/python", result.KeyringModuleProbe.PythonExecutablePath);
        ProcessStartSpec startSpec = Assert.Single(processRunner.StartSpecs);
        Assert.Equal("/workspace/.venv/bin/python", startSpec.FileName);
    }

    [Fact]
    public async Task DoctorIgnoresWindowsScriptsPythonForPosixVirtualEnvironment()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/Scripts");
        WriteExecutable(fileSystem, "/workspace/.venv/Scripts/python.exe");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(ProcessResult.LaunchFailure());
        processRunner.EnqueueResult(ProcessResult.LaunchFailure());
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["VIRTUAL_ENV"] = "/workspace/.venv",
                ["PATH"] = "/usr/bin:/bin",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Null(result.KeyringModuleProbe.PythonExecutablePath);
        Assert.Collection(
            processRunner.StartSpecs,
            startSpec => Assert.Equal("python3", startSpec.FileName),
            startSpec => Assert.Equal("python", startSpec.FileName)
        );
    }

    [Fact]
    public async Task DoctorResolvesWindowsVirtualEnvironmentPythonFromScriptsDirectory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        CreateDirectory(fileSystem, "C:/workspace/.venv/Scripts");
        fileSystem.WriteAllText("C:/workspace/.venv/Scripts/python.exe", "@echo off\r\n");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["VIRTUAL_ENV"] = "C:/workspace/.venv",
                ["PATH"] = "C:/Windows/System32;C:/Python312",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedWindowsShimPath,
                PathListSeparator = ';',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            @"C:\workspace\.venv\Scripts\python.exe",
            result.KeyringModuleProbe.PythonExecutablePath
        );
        ProcessStartSpec startSpec = Assert.Single(processRunner.StartSpecs);
        Assert.Equal(
            @"C:\workspace\.venv\Scripts\python.exe",
            startSpec.FileName
        );
    }

    [Fact]
    public async Task DoctorIgnoresPosixBinPythonForWindowsVirtualEnvironment()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        CreateDirectory(fileSystem, "C:/workspace/.venv/bin");
        fileSystem.WriteAllText("C:/workspace/.venv/bin/python", "#!/bin/sh\n");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(ProcessResult.LaunchFailure());
        processRunner.EnqueueResult(ProcessResult.LaunchFailure());
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["VIRTUAL_ENV"] = "C:/workspace/.venv",
                ["PATH"] = "C:/Windows/System32;C:/Python312",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedWindowsShimPath,
                PathListSeparator = ';',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Null(result.KeyringModuleProbe.PythonExecutablePath);
        Assert.Collection(
            processRunner.StartSpecs,
            startSpec => Assert.Equal("python3", startSpec.FileName),
            startSpec => Assert.Equal("python", startSpec.FileName)
        );
    }

    [Fact]
    public async Task DoctorPreservesUnixPathVirtualEnvironmentSymlinkIdentity()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        WriteExecutable(fileSystem, "/workspace/.venv/bin/python3");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(0, "/workspace/.venv/bin/python3\n", string.Empty)
        );
        processRunner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["PATH"] = "/preferred/bin:/fallback/bin",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Collection(
            processRunner.StartSpecs,
            resolutionSpec =>
            {
                Assert.Equal("python3", resolutionSpec.FileName);
                Assert.Equal(
                    [
                        "-c",
                        "import os,sys; print(os.path.abspath(sys.executable))",
                    ],
                    resolutionSpec.Arguments
                );
                Assert.Equal(
                    "/preferred/bin:/fallback/bin",
                    resolutionSpec.Environment["PATH"]
                );
            },
            moduleProbeSpec =>
                Assert.Equal("/workspace/.venv/bin/python3", moduleProbeSpec.FileName)
        );
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            "/workspace/.venv/bin/python3",
            result.KeyringModuleProbe.PythonExecutablePath
        );
    }

    [Fact]
    public async Task DoctorFallsBackToPathWhenVirtualEnvironmentPythonIsNotExecutable()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        fileSystem.WriteAllText("/workspace/.venv/bin/python", "#!/bin/sh\n");
        CreateDirectory(fileSystem, "/resolved/python3/bin");
        WriteExecutable(fileSystem, "/resolved/python3/bin/python3");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, "/resolved/python3/bin/python3\n", ""));
        processRunner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["VIRTUAL_ENV"] = "/workspace/.venv",
                ["PATH"] = "/preferred/bin:/fallback/bin",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal("python3", processRunner.StartSpecs[0].FileName);
        Assert.Equal("/resolved/python3/bin/python3", processRunner.StartSpecs[1].FileName);
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            "/resolved/python3/bin/python3",
            result.KeyringModuleProbe.PythonExecutablePath
        );
    }

    [Fact]
    public async Task DoctorResolvesStandardWindowsPathPython()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        CreateDirectory(fileSystem, "C:/Python312");
        fileSystem.WriteAllText("C:/Python312/python.exe", "@echo off\r\n");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(ProcessResult.LaunchFailure());
        processRunner.EnqueueResult(
            new ProcessResult(0, "C:/Python312/python.exe\r\n", string.Empty)
        );
        processRunner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["PATH"] = "C:/Python312;C:/Windows/System32",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedWindowsShimPath,
                PathListSeparator = ';',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Collection(
            processRunner.StartSpecs,
            startSpec => Assert.Equal("python3", startSpec.FileName),
            startSpec => Assert.Equal("python", startSpec.FileName),
            startSpec => Assert.Equal(@"C:\Python312\python.exe", startSpec.FileName)
        );
        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.True(result.KeyringModuleProbe.PythonExecutableExists);
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            @"C:\Python312\python.exe",
            result.KeyringModuleProbe.PythonExecutablePath
        );
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleFound,
            result.KeyringModuleProbe.Status
        );
    }

    [Theory]
    [InlineData("/usr/lib/python3/dist-packages/keyring")]
    [InlineData("/home/alice/.local/lib/python3.12/site-packages/keyring")]
    [InlineData("/workspace/pythonpath/keyring")]
    public async Task DoctorTrustsSuccessfulInterpreterImportSemantics(
        string unconventionalModulePath
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/usr/bin");
        WriteExecutable(fileSystem, "/usr/bin/python3");
        CreateDirectory(fileSystem, unconventionalModulePath);
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["PYTHONPATH"] = "/workspace/pythonpath",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = "/usr/bin/python3",
            }
        );
        fileSystem.Calls.Clear();

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleFound,
            result.KeyringModuleProbe.Status
        );
        ProcessStartSpec startSpec = Assert.Single(processRunner.StartSpecs);
        Assert.Empty(startSpec.Environment);
        Assert.DoesNotContain(
            fileSystem.Calls,
            static call => call.Operation == nameof(InMemoryFileSystem.EnumerateDirectories)
        );
    }

    [Fact]
    public async Task DoctorDistinguishesMissingModuleFromProbeErrors()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        WriteExecutable(fileSystem, "/workspace/.venv/bin/python");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(
                KeyringModuleNotFoundExitCode,
                KeyringModuleNotFoundOutput,
                string.Empty
            )
        );
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = "/workspace/.venv/bin/python",
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.True(result.KeyringModuleProbe.PythonExecutableExists);
        Assert.False(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal("/workspace/.venv/bin/python", result.KeyringModuleProbe.PythonExecutablePath);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleNotFound,
            result.KeyringModuleProbe.Status
        );
        Assert.Equal(
            "The selected Python interpreter cannot resolve the keyring module.",
            result.KeyringModuleProbe.FailureMessage
        );
    }

    [Theory]
    [MemberData(nameof(KeyringModuleProbeFailureCases))]
    public async Task DoctorReportsKeyringModuleProbeFailures(
        ProcessResult processResult,
        PythonPhase11KeyringModuleProbeStatus expectedStatus,
        string expectedFailureMessage
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        WriteExecutable(fileSystem, "/workspace/.venv/bin/python");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(processResult);
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?> { ["HOME"] = "/home/alice" }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = "/workspace/.venv/bin/python",
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.False(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(expectedStatus, result.KeyringModuleProbe.Status);
        Assert.Equal(expectedFailureMessage, result.KeyringModuleProbe.FailureMessage);
    }

    public static TheoryData<
        ProcessResult,
        PythonPhase11KeyringModuleProbeStatus,
        string
    > KeyringModuleProbeFailureCases =>
        new()
        {
            {
                ProcessResult.LaunchFailure(),
                PythonPhase11KeyringModuleProbeStatus.LaunchFailure,
                "The selected Python interpreter could not be launched."
            },
            {
                ProcessResult.TimedOut(string.Empty, string.Empty),
                PythonPhase11KeyringModuleProbeStatus.TimedOut,
                "The keyring module probe timed out."
            },
            {
                new ProcessResult(17, string.Empty, string.Empty),
                PythonPhase11KeyringModuleProbeStatus.UnexpectedNonZeroExit,
                "The keyring module probe exited unexpectedly with code 17."
            },
        };

    [Fact]
    public async Task DoctorPropagatesUnexpectedProcessRunnerFailures()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        WriteExecutable(fileSystem, "/workspace/.venv/bin/python");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueFailure(new InvalidOperationException("injected"));
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = _ => null,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = "/workspace/.venv/bin/python",
            }
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.RunDoctorAsync(TestContext.Current.CancellationToken)
        );

        Assert.Equal("injected", exception.Message);
    }

    [Fact]
    public async Task DoctorPropagatesCancellationToKeyringModuleProbe()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        WriteExecutable(fileSystem, "/workspace/.venv/bin/python");
        var processRunner = new FakeProcessRunner();
        using var cancellation = new CancellationTokenSource();
        processRunner.EnqueueHandler(
            async (_, cancellationToken) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new ProcessResult(0, string.Empty, string.Empty);
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = _ => null,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = "/workspace/.venv/bin/python",
            }
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await service.RunDoctorAsync(cancellation.Token)
        );
    }

    [Fact]
    public async Task DoctorDoesNotClaimModuleHealthFromNonTerminalEnvironmentHints()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.tox/py311");
        CreateDirectory(fileSystem, "/workspace/.uv-env");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(ProcessResult.LaunchFailure());
        processRunner.EnqueueResult(ProcessResult.LaunchFailure());
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["TOX_ENV_DIR"] = "/workspace/.tox/py311",
                ["UV_PROJECT_ENVIRONMENT"] = "/workspace/.uv-env",
                ["PATH"] = "/preferred/bin:/fallback/bin",
            }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.ToxEnvironmentDetected);
        Assert.True(result.UvEnvironmentDetected);
        Assert.False(result.KeyringModuleProbe.Attempted);
        Assert.False(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Null(result.KeyringModuleProbe.PythonExecutablePath);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.InterpreterNotFound,
            result.KeyringModuleProbe.Status
        );
        Assert.Equal(
            "Current-terminal Python interpreter could not be resolved.",
            result.KeyringModuleProbe.FailureMessage
        );
    }

    [Fact]
    public async Task DoctorValidatesPythonSimpleAndUploadEndpointCanonicalization()
    {
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?> { ["HOME"] = "/home/alice" }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix),
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
            }
        );

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.AzureArtifactsPythonEndpointCanonicalizationSuccess);
    }

    private static void CreateDirectory(InMemoryFileSystem fileSystem, string path)
    {
        fileSystem.CreateDirectory(path);
    }

    private static void WriteExecutable(InMemoryFileSystem fileSystem, string path)
    {
        fileSystem.WriteAllText(path, "#!/bin/sh\n");
        fileSystem.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserExecute
        );
    }

    private static void AssertNoFilesystemMutationCalls(IEnumerable<FileSystemCall> calls)
    {
        Assert.DoesNotContain(
            calls,
            static call =>
                call.Operation
                    is nameof(InMemoryFileSystem.WriteAllText)
                        or nameof(InMemoryFileSystem.AtomicWriteAllText)
                        or nameof(InMemoryFileSystem.AtomicWriteAllBytes)
                        or nameof(InMemoryFileSystem.SetUnixFileMode)
                        or nameof(InMemoryFileSystem.CreateDirectory)
                        or nameof(InMemoryFileSystem.DeleteFile)
                        or nameof(InMemoryFileSystem.DeleteDirectory)
        );
    }

    private sealed class EnvironmentVariables
    {
        private readonly IReadOnlyDictionary<string, string?> variables;

        public EnvironmentVariables(IReadOnlyDictionary<string, string?> variables) =>
            this.variables = variables;

        public string? Get(string name) =>
            variables.TryGetValue(name, out string? value) ? value : null;
    }

    [Fact]
    public async Task DoctorDoesNotFallBackToPythonWhenPython3ResolutionTimesOut()
    {
        var fileSystem = CreatePosixFileSystemWithResolvedPython3();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            ProcessResult.TimedOut(string.Empty, "timed out", exitCode: 126)
        );
        var service = CreatePathResolvingService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInterpreterResolutionFailure(result);
        ProcessStartSpec resolutionSpec = Assert.Single(processRunner.StartSpecs);
        AssertPythonResolutionSpec(resolutionSpec, "python3");
    }

    [Fact]
    public async Task DoctorDoesNotFallBackToPythonWhenPython3ResolutionExitsNonzero()
    {
        var fileSystem = CreatePosixFileSystemWithResolvedPython3();
        var processRunner = new FakeProcessRunner();
        var nonzeroResult = new ProcessResult(17, string.Empty, "startup failed");
        processRunner.EnqueueResult(nonzeroResult);
        var service = CreatePathResolvingService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProcessTerminationReason.Exited, nonzeroResult.TerminationReason);
        AssertInterpreterResolutionFailure(result);
        ProcessStartSpec resolutionSpec = Assert.Single(processRunner.StartSpecs);
        AssertPythonResolutionSpec(resolutionSpec, "python3");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/resolved/python3/bin/python3\n/other/python3\n")]
    [InlineData("prefix /resolved/python3/bin/python3 suffix\n")]
    [InlineData("relative/python3\n")]
    public async Task DoctorDoesNotFallBackToPythonWhenPython3ResolutionOutputIsMalformed(
        string standardOutput
    )
    {
        var fileSystem = CreatePosixFileSystemWithResolvedPython3();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(0, standardOutput, string.Empty));
        var service = CreatePathResolvingService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInterpreterResolutionFailure(result);
        ProcessStartSpec resolutionSpec = Assert.Single(processRunner.StartSpecs);
        AssertPythonResolutionSpec(resolutionSpec, "python3");
    }

    [Fact]
    public async Task DoctorDoesNotFallBackToPythonWhenPython3ResolutionExceedsOutputLimit()
    {
        var fileSystem = CreatePosixFileSystemWithResolvedPython3();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            ProcessResult.OutputTooLarge(
                "/resolved/python3/bin/python3\n",
                string.Empty,
                exitCode: 127
            )
        );
        var service = CreatePathResolvingService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInterpreterResolutionFailure(result);
        ProcessStartSpec resolutionSpec = Assert.Single(processRunner.StartSpecs);
        AssertPythonResolutionSpec(resolutionSpec, "python3");
    }

    [Fact]
    public async Task DoctorDoesNotFallBackToPythonWhenPython3ResolutionHasInvalidOutput()
    {
        var fileSystem = CreatePosixFileSystemWithResolvedPython3();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            ProcessResult.InvalidOutput(
                "/resolved/python3/bin/python3\n",
                string.Empty,
                exitCode: 126
            )
        );
        var service = CreatePathResolvingService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInterpreterResolutionFailure(result);
        ProcessStartSpec resolutionSpec = Assert.Single(processRunner.StartSpecs);
        AssertPythonResolutionSpec(resolutionSpec, "python3");
    }

    [Fact]
    public async Task DoctorFallsBackToPythonWhenPython3ResolutionTerminationReasonIsLaunchFailure()
    {
        var fileSystem = CreatePosixFileSystemWithResolvedPython3();
        CreateDirectory(fileSystem, "/resolved/python/bin");
        WriteExecutable(fileSystem, "/resolved/python/bin/python");
        var processRunner = new FakeProcessRunner();
        var launchFailure = ProcessResult.LaunchFailure(standardError: "not found");
        processRunner.EnqueueResult(launchFailure);
        processRunner.EnqueueResult(
            new ProcessResult(0, "/resolved/python/bin/python\n", string.Empty)
        );
        processRunner.EnqueueResult(new ProcessResult(0, KeyringModuleFoundOutput, string.Empty));
        var service = CreatePathResolvingService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProcessTerminationReason.LaunchFailure, launchFailure.TerminationReason);
        Assert.Equal("/resolved/python/bin/python", result.KeyringModuleProbe.PythonExecutablePath);
        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.Collection(
            processRunner.StartSpecs,
            python3ResolutionSpec => AssertPythonResolutionSpec(
                python3ResolutionSpec,
                "python3"
            ),
            pythonResolutionSpec => AssertPythonResolutionSpec(
                pythonResolutionSpec,
                "python"
            ),
            moduleProbeSpec => Assert.Equal(
                "/resolved/python/bin/python",
                moduleProbeSpec.FileName
            )
        );
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    public async Task DoctorAppliesPosixUnavailableExitCodesOnlyOnNonWindows(
        int exitCode
    )
    {
        var fileSystem = CreatePosixFileSystemWithResolvedPython3();
        CreateDirectory(fileSystem, "/resolved/python/bin");
        WriteExecutable(fileSystem, "/resolved/python/bin/python");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(exitCode, "resolution output", "resolution error")
        );
        processRunner.EnqueueResult(
            new ProcessResult(0, "/resolved/python/bin/python\n", string.Empty)
        );
        processRunner.EnqueueResult(
            new ProcessResult(0, KeyringModuleFoundOutput, string.Empty)
        );
        var service = CreatePathResolvingService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        if (OperatingSystem.IsWindows())
        {
            AssertInterpreterResolutionFailure(result);
            AssertPythonResolutionSpec(Assert.Single(processRunner.StartSpecs), "python3");
            return;
        }

        Assert.Equal("/resolved/python/bin/python", result.KeyringModuleProbe.PythonExecutablePath);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleFound,
            result.KeyringModuleProbe.Status
        );
        Assert.Collection(
            processRunner.StartSpecs,
            python3ResolutionSpec => AssertPythonResolutionSpec(
                python3ResolutionSpec,
                "python3"
            ),
            pythonResolutionSpec => AssertPythonResolutionSpec(
                pythonResolutionSpec,
                "python"
            ),
            moduleProbeSpec =>
                Assert.Equal("/resolved/python/bin/python", moduleProbeSpec.FileName)
        );
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    public async Task DoctorDoesNotFallBackAfterSelectingPython3WhenModuleProbeFails(
        int exitCode
    )
    {
        var fileSystem = CreatePosixFileSystemWithResolvedPython3();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(0, "/resolved/python3/bin/python3\n", string.Empty)
        );
        processRunner.EnqueueResult(
            new ProcessResult(exitCode, string.Empty, "Fatal Python error")
        );
        var service = CreatePathResolvingService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.UnexpectedNonZeroExit,
            result.KeyringModuleProbe.Status
        );
        Assert.Collection(
            processRunner.StartSpecs,
            resolutionSpec => AssertPythonResolutionSpec(resolutionSpec, "python3"),
            moduleProbeSpec =>
                Assert.Equal("/resolved/python3/bin/python3", moduleProbeSpec.FileName)
        );
    }

    private static InMemoryFileSystem CreatePosixFileSystemWithResolvedPython3()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/resolved/python3/bin");
        WriteExecutable(fileSystem, "/resolved/python3/bin/python3");
        return fileSystem;
    }

    private static PythonPhase11VerticalSliceService CreatePathResolvingService(
        InMemoryFileSystem fileSystem,
        FakeProcessRunner processRunner
    )
    {
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["HOME"] = "/home/alice",
                ["PATH"] = "/preferred/bin:/fallback/bin",
            }
        );
        return new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PathListSeparator = ':',
            }
        );
    }

    private static void AssertInterpreterResolutionFailure(PythonPhase11DoctorResult result)
    {
        Assert.Null(result.KeyringModuleProbe.PythonExecutablePath);
        Assert.False(result.KeyringModuleProbe.PythonExecutableExists);
        Assert.False(result.KeyringModuleProbe.Attempted);
        Assert.False(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.InterpreterNotFound,
            result.KeyringModuleProbe.Status
        );
        Assert.Equal(
            "Current-terminal Python interpreter could not be resolved.",
            result.KeyringModuleProbe.FailureMessage
        );
    }

    private static void AssertPythonResolutionSpec(
        ProcessStartSpec startSpec,
        string expectedFileName
    )
    {
        Assert.Equal(expectedFileName, startSpec.FileName);
        Assert.Equal(
            ["-c", "import os,sys; print(os.path.abspath(sys.executable))"],
            startSpec.Arguments
        );
        Assert.Equal("/preferred/bin:/fallback/bin", startSpec.Environment["PATH"]);
        Assert.Equal(TimeSpan.FromSeconds(5), startSpec.Timeout);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardErrorByteLimit);
    }

    [Fact]
    public async Task DoctorDoesNotReportMissingModuleForUnmarkedExitOne()
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(1, string.Empty, "Fatal Python error: init_fs_encoding")
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.False(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.UnexpectedNonZeroExit,
            result.KeyringModuleProbe.Status
        );
        Assert.Equal(
            "The keyring module probe exited unexpectedly with code 1.",
            result.KeyringModuleProbe.FailureMessage
        );
        Assert.DoesNotContain(
            "Fatal Python error",
            result.KeyringModuleProbe.FailureMessage,
            StringComparison.Ordinal
        );
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Fact]
    public async Task DoctorRejectsKeyringProbeFailureReportedOnlyOnStandardError()
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(
                KeyringModuleProbeFailureExitCode,
                string.Empty,
                KeyringModuleProbeFailureMarker
            )
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInvalidKeyringProtocol(result);
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Fact]
    public async Task DoctorBuildsKeyringProbeThatMapsFinderBaseExceptionToProbeFailure()
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(
                KeyringModuleProbeFailureExitCode,
                KeyringModuleProbeFailureOutput,
                string.Empty
            )
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.False(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleFinderError,
            result.KeyringModuleProbe.Status
        );
        Assert.Equal(
            "The keyring module finder raised an exception.",
            result.KeyringModuleProbe.FailureMessage
        );
        ProcessStartSpec probeSpec = Assert.Single(processRunner.StartSpecs);
        AssertKeyringProbeSpec(probeSpec);
        string script = Assert.Single(probeSpec.Arguments.Skip(1));
        Assert.DoesNotContain("import keyring", script, StringComparison.Ordinal);
        Assert.Contains("except BaseException:", script, StringComparison.Ordinal);
        Assert.Contains(
            $"print('{KeyringModuleProbeFailureMarker}')",
            script,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"sys.exit({KeyringModuleProbeFailureExitCode})",
            script,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(KeyringModuleFoundMarker, KeyringModuleNotFoundExitCode)]
    [InlineData(KeyringModuleFoundMarker, KeyringModuleProbeFailureExitCode)]
    [InlineData(KeyringModuleNotFoundMarker, KeyringModuleFoundExitCode)]
    [InlineData(KeyringModuleNotFoundMarker, KeyringModuleProbeFailureExitCode)]
    [InlineData(KeyringModuleProbeFailureMarker, KeyringModuleFoundExitCode)]
    [InlineData(KeyringModuleProbeFailureMarker, KeyringModuleNotFoundExitCode)]
    public async Task DoctorRejectsMismatchedKeyringMarkerAndExitCode(
        string marker,
        int exitCode
    )
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(new ProcessResult(exitCode, marker + "\n", string.Empty));
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInvalidKeyringProtocol(result);
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Theory]
    [InlineData(
        KeyringModuleFoundExitCode,
        KeyringModuleFoundMarker,
        PythonPhase11KeyringModuleProbeStatus.ModuleFound
    )]
    [InlineData(
        KeyringModuleNotFoundExitCode,
        KeyringModuleNotFoundMarker,
        PythonPhase11KeyringModuleProbeStatus.ModuleNotFound
    )]
    [InlineData(
        KeyringModuleProbeFailureExitCode,
        KeyringModuleProbeFailureMarker,
        PythonPhase11KeyringModuleProbeStatus.ModuleFinderError
    )]
    public async Task DoctorAcceptsExactKeyringProtocolWithWindowsLineEnding(
        int exitCode,
        string marker,
        PythonPhase11KeyringModuleProbeStatus expectedStatus
    )
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(exitCode, marker + "\r\n", string.Empty)
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expectedStatus, result.KeyringModuleProbe.Status);
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Theory]
    [InlineData("", KeyringModuleFoundExitCode)]
    [InlineData("   \n", KeyringModuleFoundExitCode)]
    [InlineData("", KeyringModuleNotFoundExitCode)]
    [InlineData("\t\r\n", KeyringModuleNotFoundExitCode)]
    public async Task DoctorRejectsKeyringProbeWithMissingOutput(
        string standardOutput,
        int exitCode
    )
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(exitCode, standardOutput, string.Empty)
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInvalidKeyringProtocol(result);
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Theory]
    [InlineData(
        KeyringModuleFoundMarker + "\nadditional output\n",
        KeyringModuleFoundExitCode
    )]
    [InlineData(
        "additional output\n" + KeyringModuleFoundMarker + "\n",
        KeyringModuleFoundExitCode
    )]
    [InlineData(
        "prefix " + KeyringModuleFoundMarker + " suffix\n",
        KeyringModuleFoundExitCode
    )]
    [InlineData(
        KeyringModuleNotFoundMarker + "\nadditional output\n",
        KeyringModuleNotFoundExitCode
    )]
    [InlineData(
        "additional output\n" + KeyringModuleNotFoundMarker + "\n",
        KeyringModuleNotFoundExitCode
    )]
    [InlineData(
        "prefix " + KeyringModuleNotFoundMarker + " suffix\n",
        KeyringModuleNotFoundExitCode
    )]
    public async Task DoctorRejectsKeyringProbeWithExtraOutput(
        string standardOutput,
        int exitCode
    )
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(exitCode, standardOutput, string.Empty)
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInvalidKeyringProtocol(result);
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Theory]
    [InlineData(
        "Error processing line 1 of site-packages.pth\n"
            + KeyringModuleFoundMarker
            + "\n",
        KeyringModuleFoundExitCode
    )]
    [InlineData(
        "sitecustomize loaded\n" + KeyringModuleNotFoundMarker + "\n",
        KeyringModuleNotFoundExitCode
    )]
    [InlineData(
        "UserWarning: startup customization\n"
            + KeyringModuleFoundMarker
            + "\ntelemetry enabled\n",
        KeyringModuleFoundExitCode
    )]
    public async Task DoctorRejectsKeyringProbeWithContaminatedOutput(
        string standardOutput,
        int exitCode
    )
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(exitCode, standardOutput, string.Empty)
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInvalidKeyringProtocol(result);
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Fact]
    public async Task DoctorRejectsKeyringProbeWithStandardErrorContamination()
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(
                KeyringModuleFoundExitCode,
                KeyringModuleFoundOutput,
                "startup warning"
            )
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertInvalidKeyringProtocol(result);
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Fact]
    public async Task DoctorReportsModuleFoundOnlyForExactFoundMarkerAndExitCode()
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(
                KeyringModuleFoundExitCode,
                KeyringModuleFoundOutput,
                string.Empty
            )
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(SelectedPythonPath, result.KeyringModuleProbe.PythonExecutablePath);
        Assert.True(result.KeyringModuleProbe.PythonExecutableExists);
        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleFound,
            result.KeyringModuleProbe.Status
        );
        Assert.Null(result.KeyringModuleProbe.FailureMessage);
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    [Fact]
    public async Task DoctorReportsModuleNotFoundOnlyForExactNotFoundMarkerAndExitCode()
    {
        var fileSystem = CreatePosixFileSystemWithSelectedPython();
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            new ProcessResult(
                KeyringModuleNotFoundExitCode,
                KeyringModuleNotFoundOutput,
                string.Empty
            )
        );
        var service = CreateKeyringProbeService(fileSystem, processRunner);

        PythonPhase11DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(SelectedPythonPath, result.KeyringModuleProbe.PythonExecutablePath);
        Assert.True(result.KeyringModuleProbe.PythonExecutableExists);
        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.False(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleNotFound,
            result.KeyringModuleProbe.Status
        );
        Assert.Equal(
            "The selected Python interpreter cannot resolve the keyring module.",
            result.KeyringModuleProbe.FailureMessage
        );
        AssertKeyringProbeSpec(Assert.Single(processRunner.StartSpecs));
    }

    private const string SelectedPythonPath = "/workspace/.venv/bin/python";
    private const string KeyringModuleFoundMarker =
        "ACP_KEYRING_PROBE_V1:FOUND";
    private const string KeyringModuleNotFoundMarker =
        "ACP_KEYRING_PROBE_V1:NOT_FOUND";
    private const string KeyringModuleProbeFailureMarker =
        "ACP_KEYRING_PROBE_V1:ERROR";
    private const int KeyringModuleFoundExitCode = 0;
    private const int KeyringModuleNotFoundExitCode = 20;
    private const int KeyringModuleProbeFailureExitCode = 21;
    private const string KeyringModuleFoundOutput = KeyringModuleFoundMarker + "\n";
    private const string KeyringModuleNotFoundOutput =
        KeyringModuleNotFoundMarker + "\n";
    private const string KeyringModuleProbeFailureOutput =
        KeyringModuleProbeFailureMarker + "\n";
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

    private static InMemoryFileSystem CreatePosixFileSystemWithSelectedPython()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        WriteExecutable(fileSystem, SelectedPythonPath);
        return fileSystem;
    }

    private static PythonPhase11VerticalSliceService CreateKeyringProbeService(
        InMemoryFileSystem fileSystem,
        FakeProcessRunner processRunner
    )
    {
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?> { ["HOME"] = "/home/alice" }
        );
        return new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                EnvironmentVariableReader = environment.Get,
                ExpectedKeyringShimPath = ExpectedShimPath,
                PythonExecutablePath = SelectedPythonPath,
            }
        );
    }

    private static void AssertInvalidKeyringProtocol(PythonPhase11DoctorResult result)
    {
        Assert.Equal(SelectedPythonPath, result.KeyringModuleProbe.PythonExecutablePath);
        Assert.True(result.KeyringModuleProbe.PythonExecutableExists);
        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.False(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.InvalidOutput,
            result.KeyringModuleProbe.Status
        );
        Assert.Equal(
            "The keyring module probe did not produce a recognized marker and exit-code pair.",
            result.KeyringModuleProbe.FailureMessage
        );
    }

    private static void AssertKeyringProbeSpec(ProcessStartSpec startSpec)
    {
        Assert.Equal(SelectedPythonPath, startSpec.FileName);
        Assert.Equal(["-c", KeyringModuleProbeScript], startSpec.Arguments);
        Assert.Empty(startSpec.Environment);
        Assert.Equal(TimeSpan.FromSeconds(5), startSpec.Timeout);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardErrorByteLimit);
    }

    [Theory]
    [InlineData(126)]
    [InlineData(127)]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task DoctorWithSystemProcessRunnerFallsBackFromUnavailablePython3OnLinux(
        int exitCode
    )
    {
        AssertLinuxShellIntegrationAvailable();
        string directory = CreateIntegrationTestDirectory();
        try
        {
            WriteShellExecutable(
                Path.Combine(directory, "python3"),
                $"#!/bin/sh\nexit {exitCode}\n"
            );
            string pythonPath = Path.Combine(directory, "python");
            WriteWorkingPythonWrapper(pythonPath);

            PythonPhase11DoctorResult result =
                await CreateSystemRunnerPathResolvingService(directory)
                    .RunDoctorAsync(TestContext.Current.CancellationToken);

            AssertSuccessfulSystemRunnerFallback(result, pythonPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task DoctorWithSystemProcessRunnerFallsBackFromNonExecutablePython3OnLinux()
    {
        AssertLinuxShellIntegrationAvailable();
        string directory = CreateIntegrationTestDirectory();
        try
        {
            string python3Path = Path.Combine(directory, "python3");
            File.WriteAllText(python3Path, "#!/bin/sh\nexit 99\n");
            File.SetUnixFileMode(
                python3Path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite
            );
            string pythonPath = Path.Combine(directory, "python");
            WriteWorkingPythonWrapper(pythonPath);

            PythonPhase11DoctorResult result =
                await CreateSystemRunnerPathResolvingService(directory)
                    .RunDoctorAsync(TestContext.Current.CancellationToken);

            AssertSuccessfulSystemRunnerFallback(result, pythonPath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PythonKeyringModuleProbeStatusPreservesNumericValues()
    {
        Assert.Equal(0, (int)PythonPhase11KeyringModuleProbeStatus.InterpreterNotFound);
        Assert.Equal(1, (int)PythonPhase11KeyringModuleProbeStatus.ModuleFound);
        Assert.Equal(2, (int)PythonPhase11KeyringModuleProbeStatus.ModuleNotFound);
        Assert.Equal(3, (int)PythonPhase11KeyringModuleProbeStatus.LaunchFailure);
        Assert.Equal(4, (int)PythonPhase11KeyringModuleProbeStatus.TimedOut);
        Assert.Equal(5, (int)PythonPhase11KeyringModuleProbeStatus.UnexpectedNonZeroExit);
        Assert.Equal(6, (int)PythonPhase11KeyringModuleProbeStatus.OutputTooLarge);
        Assert.Equal(7, (int)PythonPhase11KeyringModuleProbeStatus.InvalidOutput);
        Assert.Equal(8, (int)PythonPhase11KeyringModuleProbeStatus.ModuleFinderError);
    }

    private static void AssertLinuxShellIntegrationAvailable()
    {
        Assert.SkipUnless(
            OperatingSystem.IsLinux() && File.Exists("/bin/sh"),
            "This integration test requires Linux and /bin/sh."
        );
    }

    private static string CreateIntegrationTestDirectory()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "python-phase11-integration",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static PythonPhase11VerticalSliceService CreateSystemRunnerPathResolvingService(
        string directory
    ) =>
        new(
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
                ExpectedKeyringShimPath = Path.Combine(directory, "keyring"),
                CurrentDirectoryPath = directory,
                PathListSeparator = Path.PathSeparator,
            }
        );

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static void WriteWorkingPythonWrapper(string path)
    {
        string quotedPath = path.Replace("'", "'\"'\"'", StringComparison.Ordinal);
        WriteShellExecutable(
            path,
            "#!/bin/sh\n"
                + "if [ \"$2\" = 'import os,sys; print(os.path.abspath(sys.executable))' ]; then\n"
                + $"  printf '%s\\n' '{quotedPath}'\n"
                + "  exit 0\n"
                + "fi\n"
                + $"printf '%s\\n' '{KeyringModuleFoundMarker}'\n"
                + "exit 0\n"
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

    private static void AssertSuccessfulSystemRunnerFallback(
        PythonPhase11DoctorResult result,
        string expectedPythonPath
    )
    {
        Assert.Equal(expectedPythonPath, result.KeyringModuleProbe.PythonExecutablePath);
        Assert.True(result.KeyringModuleProbe.PythonExecutableExists);
        Assert.True(result.KeyringModuleProbe.Attempted);
        Assert.True(result.KeyringModuleProbe.KeyringModuleResolvable);
        Assert.Equal(
            PythonPhase11KeyringModuleProbeStatus.ModuleFound,
            result.KeyringModuleProbe.Status
        );
        Assert.Null(result.KeyringModuleProbe.FailureMessage);
    }
}
