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
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
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
                "import importlib.util,sys; sys.exit(0 if importlib.util.find_spec('keyring') is not None else 1)",
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
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
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
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
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
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
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
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
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
        Assert.Equal("/resolved/python3/bin/python3", result.KeyringModuleProbe.PythonExecutablePath);
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
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
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
        processRunner.EnqueueResult(new ProcessResult(0, string.Empty, string.Empty));
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
        processRunner.EnqueueResult(new ProcessResult(1, string.Empty, string.Empty));
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
}
