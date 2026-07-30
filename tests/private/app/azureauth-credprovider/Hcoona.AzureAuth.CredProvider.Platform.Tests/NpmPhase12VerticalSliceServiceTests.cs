using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
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
        fileSystem.WriteAllText("/workspace/.npmrc", "registry=https://registry.npmjs.org/\n");
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
            new Dictionary<string, string?> { ["NPM_CONFIG_USERCONFIG"] = "/tmp/ci-user.npmrc" }
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
            "registry=https://pkgs.dev.azure.com/org/project/" + "_packaging/feed/npm/registry/\n"
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
    public void CreateCiTemporaryCredentialPlanDeclaresNpmrcActivationEnvironment()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
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

        ConfigurationChangePlan plan = service.CreateCiTemporaryCredentialPlan(
            new NpmPhase12CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
                TargetNpmrcPath = "/tmp/azureauth-ci/.npmrc",
            }
        );

        ConfigurationChange change = Assert.Single(plan.Changes);
        Assert.Equal(ConfigurationScope.CiTemporary, plan.Scope);
        Assert.Equal(
            ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible,
            plan.DeclarationPreservation
        );
        Assert.Equal("/tmp/azureauth-ci/.npmrc", change.TargetPathOrName);
        Assert.NotNull(plan.TemporaryContainer);
        Assert.Equal(ConfigurationTemporaryContainerKind.NpmrcFile, plan.TemporaryContainer.Kind);
        Assert.Equal("/tmp/azureauth-ci/.npmrc", plan.TemporaryContainer.ProductOwnedPath);
        Assert.NotNull(plan.TemporaryContainer.ActivationEnvironment);
        Assert.Equal("posix", plan.TemporaryContainer.ActivationEnvironment.Platform);
        Assert.Equal(
            "/tmp/azureauth-ci/.npmrc",
            plan.TemporaryContainer.ActivationEnvironment.SetVariables["NPM_CONFIG_USERCONFIG"]
        );
        Assert.Equal(
            "/tmp/azureauth-ci/.npmrc",
            plan.TemporaryContainer.ActivationEnvironment.SetVariables["npm_config_userconfig"]
        );
        Assert.Empty(plan.TemporaryContainer.ActivationEnvironment.ClearVariables);
        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
    }

    [Fact]
    public async Task CiTemporaryCredentialPlanAppliesThroughConfigurationManager()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/tmp/azureauth-ci");
        CreateDirectory(fileSystem, "/state");
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
        ConfigurationChangePlan plan = service.CreateCiTemporaryCredentialPlan(
            new NpmPhase12CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
                TargetNpmrcPath = "/tmp/azureauth-ci/.npmrc",
            }
        );
        var manager = new ConfigurationManager(
            fileSystem,
            "/state/phase12-ci-manifest.json",
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );

        ConfigurationPlanResult result = await manager.ApplyAsync(
            plan,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.Operation);
        Assert.Contains(
            "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken=short-lived-token",
            fileSystem.ReadAllText("/tmp/azureauth-ci/.npmrc"),
            StringComparison.Ordinal
        );
        Assert.Equal(
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n",
            fileSystem.ReadAllText("/workspace/.npmrc")
        );
    }

    [Fact]
    public async Task UserCredentialPlanAppliesAndRemovesThroughConfigurationManager()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        CreateDirectory(fileSystem, "/state");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
        );
        fileSystem.WriteAllText("/home/alice/.npmrc", "# user config\n");
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
        ConfigurationChangePlan applyPlan = service.CreateUserCredentialPlan(
            new NpmPhase12CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
            }
        );
        const string manifestPath = "/state/phase12-user-manifest.json";
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );

        ConfigurationPlanResult applyResult = await manager.ApplyAsync(
            applyPlan,
            TestContext.Current.CancellationToken
        );
        string manifestJson = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifestEntry ownedEntry = Assert.Single(
            applyResult.OwnershipManifest?.Entries ?? []
        );
        ConfigurationChangePlan removePlan = applyPlan with
        {
            Manifest = applyPlan.Manifest with { ResourceIdentity = null },
            Changes =
            [
                applyPlan.Changes[0] with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                },
            ],
        };

        ConfigurationPlanResult removeResult = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, applyResult.Operation);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, removeResult.Operation);
        Assert.Equal("# user config\n", fileSystem.ReadAllText("/home/alice/.npmrc"));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public void CreateCiTemporaryCredentialPlanRejectsUserOnlyRegistryDeclaration()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/home/alice/.npmrc",
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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateCiTemporaryCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = declaration,
                    AuthToken = "short-lived-token",
                    TargetNpmrcPath = "/tmp/azureauth-ci/.npmrc",
                }
            )
        );

        Assert.Contains("registry declaration to remain visible", exception.Message);
    }

    [Fact]
    public async Task DoctorReportsWorkspaceRegistryAndValidNpmPnpmPlansWithoutWritingFiles()
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
                CiTemporaryNpmrcPath = "/tmp/azureauth-ci/.npmrc",
            }
        );
        fileSystem.Calls.Clear();

        NpmPhase12DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/workspace/.npmrc", result.WorkspaceNpmrcPath);
        Assert.True(result.WorkspaceNpmrcExists);
        Assert.Equal("/home/alice/.npmrc", result.EffectiveUserNpmrcPath);
        Assert.False(result.EffectiveUserNpmrcExists);
        Assert.True(result.RegistryDeclarationDiscovered);
        Assert.True(result.AzureArtifactsNpmEndpointCanonicalizationSuccess);
        Assert.True(result.NpmUserCredentialPlanValid);
        Assert.True(result.PnpmUserCredentialPlanValid);
        Assert.True(result.CiTemporaryCredentialPlanValid);
        Assert.True(result.CiTemporaryAuthOnlyPlanSupported);
        Assert.False(result.EffectiveUserConfigEnvironmentOverridePresent);
        AssertNoFilesystemMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public async Task DoctorReportsCiTemporaryAuthOnlyUnsupportedForUserOnlyDeclaration()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/home/alice/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
                CiTemporaryNpmrcPath = "/tmp/azureauth-ci/.npmrc",
            }
        );

        NpmPhase12DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.True(result.RegistryDeclarationDiscovered);
        Assert.True(result.NpmUserCredentialPlanValid);
        Assert.True(result.PnpmUserCredentialPlanValid);
        Assert.False(result.CiTemporaryCredentialPlanValid);
        Assert.False(result.CiTemporaryAuthOnlyPlanSupported);
    }

    [Fact]
    public async Task DoctorReportsEffectiveUserConfigEnvironmentOverride()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/tmp");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
        );
        fileSystem.WriteAllText(
            "/tmp/override.npmrc",
            "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken=old\n"
        );
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?> { ["NPM_CONFIG_USERCONFIG"] = "/tmp/override.npmrc" }
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                EnvironmentVariableReader = environment.Get,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
                CiTemporaryNpmrcPath = "/tmp/azureauth-ci/.npmrc",
            }
        );

        NpmPhase12DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/tmp/override.npmrc", result.EffectiveUserNpmrcPath);
        Assert.True(result.EffectiveUserNpmrcExists);
        Assert.True(result.UppercaseUserConfigEnvironmentOverridePresent);
        Assert.False(result.LowercaseUserConfigEnvironmentOverridePresent);
        Assert.True(result.EffectiveUserConfigEnvironmentOverridePresent);
    }

    [Fact]
    public async Task DoctorReportsNoCredentialPlansWhenNoRegistryDeclarationExists()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText("/workspace/.npmrc", "registry=https://registry.npmjs.org/\n");
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
                CiTemporaryNpmrcPath = "/tmp/azureauth-ci/.npmrc",
            }
        );

        NpmPhase12DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.False(result.RegistryDeclarationDiscovered);
        Assert.True(result.AzureArtifactsNpmEndpointCanonicalizationSuccess);
        Assert.False(result.NpmUserCredentialPlanValid);
        Assert.False(result.PnpmUserCredentialPlanValid);
        Assert.False(result.CiTemporaryCredentialPlanValid);
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

    private static string ComputeSha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
