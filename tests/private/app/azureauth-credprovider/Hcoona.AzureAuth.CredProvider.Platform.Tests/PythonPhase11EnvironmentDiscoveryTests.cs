using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class PythonPhase11EnvironmentDiscoveryTests
{
    private const string ExpectedShimPath =
        "/home/alice/.local/share/azureauth-credprovider/keyring-shim/keyring";
    private const string ExpectedShimDirectory =
        "/home/alice/.local/share/azureauth-credprovider/keyring-shim";

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
        fileSystem.WriteAllText(ExpectedShimPath, "#!/bin/sh\n");
        fileSystem.WriteAllText("/usr/bin/keyring", "#!/bin/sh\n");
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
        fileSystem.WriteAllText(ExpectedShimPath, "#!/bin/sh\n");
        fileSystem.WriteAllText("/usr/bin/keyring", "#!/bin/sh\n");
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
        fileSystem.WriteAllText(ExpectedShimPath, "#!/bin/sh\n");
        fileSystem.WriteAllText("/workspace/keyring", "#!/bin/sh\n");
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
    public async Task DoctorChecksKeyringModuleByFilesystemWithoutExecutingPython()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace/.venv/bin");
        fileSystem.WriteAllText("/workspace/.venv/bin/python", "#!/bin/sh\n");
        CreateDirectory(fileSystem, "/workspace/.venv/lib/python3.11/site-packages/keyring");
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?> { ["HOME"] = "/home/alice" }
        );
        var service = new PythonPhase11VerticalSliceService(
            new PythonPhase11VerticalSliceOptions
            {
                FileSystem = fileSystem,
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
            "/workspace/.venv/lib/python3.11/site-packages",
            result.KeyringModuleProbe.SitePackagesPath
        );
        AssertNoFilesystemMutationCalls(fileSystem.Calls);
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
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string current = string.Empty;
        foreach (string segment in segments)
        {
            current += "/" + segment;
            if (!fileSystem.DirectoryExists(current))
            {
                fileSystem.CreateDirectory(current);
            }
        }
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
