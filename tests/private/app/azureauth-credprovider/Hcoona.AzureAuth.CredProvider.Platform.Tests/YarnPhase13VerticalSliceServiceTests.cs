using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class YarnPhase13VerticalSliceServiceTests
{
    [Fact]
    public async Task DoctorDiscoversWorkspaceDefaultAndScopedRegistriesWithoutWritingFiles()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/project/"
                + "_packaging/feed/npm/registry/'\n"
                + "npmScopes:\n"
                + "  scope:\n"
                + "    npmRegistryServer: 'https://pkgs.dev.azure.com/org/"
                + "_packaging/scoped/npm/registry/'\n"
        );
        fileSystem.WriteAllText(
            "/home/alice/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/user/npm/registry/'\n"
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );
        fileSystem.Calls.Clear();

        YarnPhase13DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.WorkspaceYarnrcExists);
        Assert.True(result.EffectiveUserYarnrcExists);
        Assert.True(result.RegistryDeclarationDiscovered);
        Assert.Equal(2, result.RegistryDeclarations.Count);
        YarnPhase13RegistryDeclaration defaultDeclaration = Assert.Single(
            result.RegistryDeclarations,
            static declaration => declaration.Scope is null
        );
        YarnPhase13RegistryDeclaration scopedDeclaration = Assert.Single(
            result.RegistryDeclarations,
            static declaration =>
                string.Equals(declaration.Scope, "scope", StringComparison.Ordinal)
        );
        Assert.Equal("/workspace/.yarnrc.yml", defaultDeclaration.SourcePath);
        Assert.Equal("project", defaultDeclaration.ResourceIdentity.Project);
        Assert.Equal("feed", defaultDeclaration.ResourceIdentity.Feed);
        Assert.Equal("npmScopes.scope.npmRegistryServer", scopedDeclaration.Key);
        Assert.Equal("scoped", scopedDeclaration.ResourceIdentity.Feed);
        Assert.Equal(scopedDeclaration.RegistryUrl.AbsoluteUri, scopedDeclaration.NpmRegistriesKey);
        Assert.True(result.AzureArtifactsYarnEndpointCanonicalizationSuccess);
        Assert.False(result.WritesSupported);
        Assert.Contains("Phase 13B", result.UnsupportedWriteMessage, StringComparison.Ordinal);
        Assert.Contains("phase-1.4-accepted", result.WriteGateStatus, StringComparison.Ordinal);
        Assert.False(result.ForbiddenAuthIdentConflictDetected);
        AssertNoFilesystemMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task DoctorFallsBackToUserYarnrcWhenWorkspaceHasNoAzureArtifactsRegistry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://registry.yarnpkg.com'\n"
        );
        fileSystem.WriteAllText(
            "/home/alice/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/user/npm/registry/'\n"
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        YarnPhase13RegistryDeclaration declaration = Assert.Single(
            (await service.RunDoctorAsync(TestContext.Current.CancellationToken))
                .RegistryDeclarations
        );

        Assert.Equal("/home/alice/.yarnrc.yml", declaration.SourcePath);
        Assert.Equal("user", declaration.ResourceIdentity.Feed);
    }

    [Fact]
    public async Task DoctorReportsYarnRcFilenameOverrideAndUsesSelectedFileForDiscovery()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/tmp");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/"
                + "_packaging/workspace/npm/registry/'\n"
        );
        fileSystem.WriteAllText(
            "/tmp/selected.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/"
                + "_packaging/selected/npm/registry/'\n"
        );
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["YARN_RC_FILENAME"] = "/tmp/selected.yarnrc.yml",
            }
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        YarnPhase13DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );
        YarnPhase13RegistryDeclaration declaration = Assert.Single(result.RegistryDeclarations);

        Assert.True(result.WorkspaceYarnrcExists);
        Assert.True(result.YarnRcFilenameOverridePresent);
        Assert.Equal("/tmp/selected.yarnrc.yml", result.YarnRcFilenameOverride);
        Assert.Equal("/tmp/selected.yarnrc.yml", declaration.SourcePath);
        Assert.Equal("selected", declaration.ResourceIdentity.Feed);
    }

    [Fact]
    public async Task DoctorTreatsRelativeYarnRcFilenameOverrideAsWorkspaceAndUserFileName()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/"
                + "_packaging/default/npm/registry/'\n"
        );
        fileSystem.WriteAllText(
            "/workspace/.selected.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/"
                + "_packaging/selected/npm/registry/'\n"
        );
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["YARN_RC_FILENAME"] = ".selected.yarnrc.yml",
            }
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        YarnPhase13DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );
        YarnPhase13RegistryDeclaration declaration = Assert.Single(result.RegistryDeclarations);

        Assert.Equal(".selected.yarnrc.yml", result.YarnRcFilenameOverride);
        Assert.Equal("/workspace/.selected.yarnrc.yml", result.WorkspaceYarnrcPath);
        Assert.Equal("/home/alice/.selected.yarnrc.yml", result.EffectiveUserYarnrcPath);
        Assert.Equal("/workspace/.selected.yarnrc.yml", declaration.SourcePath);
        Assert.Equal("selected", declaration.ResourceIdentity.Feed);
    }

    [Fact]
    public async Task DoctorReportsForbiddenNpmAuthIdentConflicts()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            """
            npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'
            npmRegistries:
              'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/':
                npmAuthIdent: 'user:password'
                npmAuthToken: fake-token
            """
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        YarnPhase13DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );
        YarnPhase13AuthIdentConflict conflict = Assert.Single(result.AuthIdentConflicts);

        Assert.True(result.ForbiddenAuthIdentConflictDetected);
        Assert.Equal("/workspace/.yarnrc.yml", conflict.SourcePath);
        Assert.Equal(
            "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/",
            conflict.RegistryKey
        );
        Assert.DoesNotContain("user:password", conflict.Key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoctorReportsScopedNpmAuthIdentConflicts()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            """
            npmScopes:
              scope:
                npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/'
                npmAuthIdent: 'user:password'
            """
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        YarnPhase13DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );
        YarnPhase13AuthIdentConflict conflict = Assert.Single(result.AuthIdentConflicts);

        Assert.True(result.ForbiddenAuthIdentConflictDetected);
        Assert.Equal("npmScopes.scope", conflict.RegistryKey);
        Assert.Equal("npmScopes.scope.npmAuthIdent", conflict.Key);
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
                        or nameof(InMemoryFileSystem.AddSymbolicLink)
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
