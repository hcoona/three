using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class NpmPhase12VerticalSliceServiceTests
{
    [Fact]
    public void DiscoverRegistryDeclarationsReadsWorkspaceNpmrcBeforeUserNpmrc()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            """
            registry=https://registry.npmjs.org/
            @scope:registry=https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/
            """
        );
        fileSystem.WriteAllText(
            "/home/alice/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/user/npm/registry/\n"
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        NpmPhase12RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        Assert.Equal("/workspace/.npmrc", declaration.SourcePath);
        Assert.Equal("@scope:registry", declaration.Key);
        Assert.Equal("org", declaration.ResourceIdentity.Organization);
        Assert.Equal("scoped", declaration.ResourceIdentity.Feed);
        Assert.Equal(
            "//pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/:_authToken",
            declaration.AuthSelectors.NpmAuthTokenKey
        );
    }

    [Fact]
    public void DiscoverRegistryDeclarationsFallsBackToUserNpmrc()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry=https://registry.npmjs.org/\n"
        );
        fileSystem.WriteAllText(
            "/home/alice/.npmrc",
            "registry=https://dev.azure.com/org/project/_packaging/feed/npm/registry/\n"
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        NpmPhase12RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        Assert.Equal("/home/alice/.npmrc", declaration.SourcePath);
        Assert.Equal("org", declaration.ResourceIdentity.Organization);
        Assert.Equal("project", declaration.ResourceIdentity.Project);
        Assert.Equal("feed", declaration.ResourceIdentity.Feed);
    }

    [Fact]
    public void CreateUserCredentialPlanTargetsUserNpmrcAndOnlyWritesSecretAuthToken()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );
        NpmPhase12RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new NpmPhase12CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
                Ecosystem = CredentialEcosystem.Pnpm,
            }
        );

        ConfigurationChange change = Assert.Single(plan.Changes);
        Assert.Equal(ConfigurationScope.User, plan.Scope);
        Assert.True(plan.ContainsCredentialMaterial);
        Assert.Equal("/home/alice/.npmrc", change.TargetPathOrName);
        Assert.Equal(ConfigurationTargetKind.Npmrc, change.TargetKind);
        Assert.Equal(ConfigurationChangeOperation.Set, change.Operation);
        Assert.True(change.IsSecretValue);
        Assert.Equal("short-lived-token", change.Value);
        Assert.Equal(plan.Manifest.EntrySelector, change.Key);
        Assert.Equal("pnpm", plan.Manifest.SafeMetadata["ecosystem"]);
        Assert.DoesNotContain(
            plan.Changes,
            static plannedChange =>
                string.Equals(plannedChange.Key, "registry", StringComparison.Ordinal)
        );
        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
    }

    [Fact]
    public void CreateUserCredentialPlanHonorsNpmUserConfigOverride()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
        );
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?>
            {
                ["NPM_CONFIG_USERCONFIG"] = "/tmp/ci-user.npmrc",
            }
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );
        NpmPhase12RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new NpmPhase12CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
            }
        );

        ConfigurationChange change = Assert.Single(plan.Changes);
        Assert.Equal("/tmp/ci-user.npmrc", change.TargetPathOrName);
    }

    [Fact]
    public void DiscoverRegistryDeclarationsAcceptsLegacyVisualStudioRegistry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry=https://org.visualstudio.com/DefaultCollection/project/"
                + "_packaging/feed/npm/registry/\n"
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        NpmPhase12RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        Assert.Equal("org", declaration.ResourceIdentity.Organization);
        Assert.Equal("project", declaration.ResourceIdentity.Project);
        Assert.Equal("feed", declaration.ResourceIdentity.Feed);
    }

    [Fact]
    public void DiscoverRegistryDeclarationsAcceptsPkgsDevAzureProjectScopedRegistry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/project/"
                + "_packaging/feed/npm/registry/\n"
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        NpmPhase12RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        Assert.Equal("org", declaration.ResourceIdentity.Organization);
        Assert.Equal("project", declaration.ResourceIdentity.Project);
        Assert.Equal("feed", declaration.ResourceIdentity.Feed);
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

    private sealed class EnvironmentVariables
    {
        private readonly IReadOnlyDictionary<string, string?> variables;

        public EnvironmentVariables(IReadOnlyDictionary<string, string?> variables) =>
            this.variables = variables;

        public string? Get(string name) =>
            variables.TryGetValue(name, out string? value) ? value : null;
    }
}
