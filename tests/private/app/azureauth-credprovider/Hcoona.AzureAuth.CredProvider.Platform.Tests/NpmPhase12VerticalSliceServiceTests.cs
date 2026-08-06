using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
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
            "registry=https://pkgs.dev.azure.com/org/project/_packaging/feed/npm/registry/\n"
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
            "registry=https://org.pkgs.visualstudio.com/DefaultCollection/project/"
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

    [Theory]
    [InlineData("https://dev.azure.com/org/project/_packaging/feed/npm/registry/")]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/npm/registry/"
    )]
    public void DiscoverRegistryDeclarationsRejectsNonRegistryWebEndpoints(string registry)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        fileSystem.WriteAllText("/workspace/.npmrc", "registry=" + registry + "\n");
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        Assert.Empty(service.DiscoverRegistryDeclarations());
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

    [Theory]
    [InlineData("_authToken")]
    [InlineData("_auth")]
    [InlineData("username")]
    [InlineData("_password")]
    public void CreateUserCredentialPlanRejectsProjectAuthSelectors(string authSelector)
    {
        const string ProjectSecret = "project-secret-value";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        string registry =
            "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/";
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry="
                + registry
                + "\n//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:"
                + authSelector
                + "="
                + ProjectSecret
                + "\n"
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateUserCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                    AuthToken = "short-lived-token",
                }
            )
        );

        Assert.Contains("Project-local npm authentication", exception.Message);
        Assert.DoesNotContain(ProjectSecret, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/@scope/package/:_authToken"
    )]
    [InlineData(
        "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/@scope/package:_authToken"
    )]
    public void CreateUserCredentialPlanRejectsDescendantPackageAuthSelectors(string selector)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
                + selector
                + "=project-secret-value\n"
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateUserCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                    AuthToken = "short-lived-token",
                }
            )
        );

        Assert.Contains("Project-local npm authentication", exception.Message);
        Assert.DoesNotContain("project-secret-value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUserCredentialPlanAllowsUnrelatedRegistryPathPrefix()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            """
            registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/
            //pkgs.dev.azure.com/org/_packaging/feed/npm/registry-other:_authToken=unrelated
            """
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new NpmPhase12CredentialPlanRequest
            {
                Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                AuthToken = "short-lived-token",
            }
        );

        Assert.Single(plan.Changes);
    }

    [Fact]
    public void CreateCiTemporaryCredentialPlanRejectsProjectAuthToken()
    {
        const string ProjectSecret = "project-secret-value";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        fileSystem.WriteAllText(
            "/workspace/.npmrc",
            """
            registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/
            //pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken=project-secret-value
            """
        );
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateCiTemporaryCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                    AuthToken = "short-lived-token",
                    TargetNpmrcPath = "/work/ci/.npmrc",
                }
            )
        );

        Assert.Contains("Project-local npm authentication", exception.Message);
        Assert.DoesNotContain(ProjectSecret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverRegistryDeclarationsDelegatesNpmWorkspaceResolutionToProcessRunnerForSupportedCharacterClassGlob()
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            new ProcessResult(0, "/repo\n", string.Empty),
            out _,
            out FakeProcessRunner processRunner
        );

        NpmPhase12RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        ProcessStartSpec startSpec = Assert.Single(processRunner.RecordedStartSpecs);
        Assert.Equal("npm", startSpec.FileName);
        Assert.Equal(["prefix"], startSpec.Arguments);
        Assert.Equal("/repo/packages/apple", startSpec.WorkingDirectory);
        Assert.InRange(
            startSpec.Timeout,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(10)
        );
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardErrorByteLimit);
        Assert.Equal("/repo/.npmrc", declaration.SourcePath);
        Assert.Equal("@scope:registry", declaration.Key);
        Assert.Equal("root", declaration.ResourceIdentity.Feed);
        Assert.Equal(
            "//pkgs.dev.azure.com/org/_packaging/root/npm/registry/:_authToken",
            declaration.AuthSelectors.NpmAuthTokenKey
        );
        Assert.DoesNotContain("credential", declaration.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DiscoverRegistryDeclarationsFailsActionablyWhenNpmIsUnavailable()
    {
        const string SensitiveEnvironmentValue = "NPM_CONFIG_TOKEN=unavailable-secret";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            ProcessResult.LaunchFailure(standardError: SensitiveEnvironmentValue),
            out _,
            out FakeProcessRunner processRunner
        );

        NpmWorkspaceResolutionException exception =
            Assert.Throws<NpmWorkspaceResolutionException>(
            service.DiscoverRegistryDeclarations
        );

        AssertNpmWorkspaceResolutionFailure(exception, processRunner);
        Assert.Contains("available on PATH", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SensitiveEnvironmentValue,
            exception.ToString(),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("unavailable-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDoctorReportsLaunchFailureWhenNpmIsUnavailable()
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            ProcessResult.LaunchFailure(),
            out _,
            out FakeProcessRunner processRunner
        );

        NpmPhase12DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            NpmWorkspaceResolutionStatus.LaunchFailure,
            result.WorkspaceResolutionStatus
        );
        Assert.False(result.WorkspaceResolutionSucceeded);
        Assert.Empty(result.RegistryDeclarations);
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public void CreateUserCredentialPlanFailsActionablyWhenNpmIsUnavailable()
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            ProcessResult.LaunchFailure(),
            out _,
            out FakeProcessRunner processRunner
        );

        NpmWorkspaceResolutionException exception =
            Assert.Throws<NpmWorkspaceResolutionException>(() =>
            service.CreateUserCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = CreateNpmDeclaration("/repo/.npmrc", "root"),
                    AuthToken = "short-lived-token",
                }
            )
        );

        AssertNpmWorkspaceResolutionFailure(exception, processRunner);
        Assert.Contains("available on PATH", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverRegistryDeclarationsFailsActionablyWhenNpmReturnsNonZeroExit()
    {
        const string SensitiveStandardError = "_authToken=stderr-secret-value";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            new ProcessResult(17, string.Empty, SensitiveStandardError),
            out _,
            out FakeProcessRunner processRunner
        );

        NpmWorkspaceResolutionException exception =
            Assert.Throws<NpmWorkspaceResolutionException>(
            service.DiscoverRegistryDeclarations
        );

        AssertNpmWorkspaceResolutionFailure(exception, processRunner);
        Assert.Contains("exit code 17", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SensitiveStandardError,
            exception.ToString(),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("stderr-secret-value", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverRegistryDeclarationsFailsActionablyWhenNpmWorkspaceResolutionTimesOut()
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            ProcessResult.TimedOut("/repo\n", "timeout-secret-value"),
            out _,
            out FakeProcessRunner processRunner
        );

        NpmWorkspaceResolutionException exception =
            Assert.Throws<NpmWorkspaceResolutionException>(
            service.DiscoverRegistryDeclarations
        );

        AssertNpmWorkspaceResolutionFailure(exception, processRunner);
        Assert.Contains("timed out", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("timeout-secret-value", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoverRegistryDeclarationsFailsActionablyWhenNpmOutputExceedsBound()
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            ProcessResult.OutputTooLarge("/repo\n", "oversized-secret-value"),
            out _,
            out FakeProcessRunner processRunner
        );

        NpmWorkspaceResolutionException exception =
            Assert.Throws<NpmWorkspaceResolutionException>(
            service.DiscoverRegistryDeclarations
        );

        AssertNpmWorkspaceResolutionFailure(exception, processRunner);
        Assert.Contains("too much output", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("oversized-secret-value", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ProcessExecutionStatus.InvalidOutput, "/repo\n")]
    [InlineData(ProcessExecutionStatus.Success, "")]
    [InlineData(ProcessExecutionStatus.Success, "relative/workspace\n")]
    [InlineData(ProcessExecutionStatus.Success, "/repo/missing\n")]
    [InlineData(ProcessExecutionStatus.Success, "/outside/workspace\n")]
    [InlineData(ProcessExecutionStatus.Success, "/repo\n/repo/packages/apple\n")]
    public void DiscoverRegistryDeclarationsFailsActionablyWhenNpmWorkspaceResolutionReturnsInvalidOutput(
        ProcessExecutionStatus status,
        string standardOutput
    )
    {
        const string SensitiveStandardError = "invalid-output-secret-value";
        ProcessResult processResult =
            status == ProcessExecutionStatus.InvalidOutput
                ? ProcessResult.InvalidOutput(standardOutput, SensitiveStandardError)
                : new ProcessResult(0, standardOutput, SensitiveStandardError);
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            processResult,
            out InMemoryFileSystem fileSystem,
            out FakeProcessRunner processRunner
        );
        CreateDirectory(fileSystem, "/outside/workspace");

        NpmWorkspaceResolutionException exception =
            Assert.Throws<NpmWorkspaceResolutionException>(
            service.DiscoverRegistryDeclarations
        );

        AssertNpmWorkspaceResolutionFailure(exception, processRunner);
        Assert.Contains("invalid", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SensitiveStandardError,
            exception.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void CreateUserCredentialPlanPreservesPnpmWorkspaceDiscoveryWithoutInvokingNpm()
    {
        const string Registry =
            "https://pkgs.dev.azure.com/org/_packaging/root/npm/registry/";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/repo/packages/apple");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText("/repo/pnpm-workspace.yaml", "packages:\n  - packages/*\n");
        fileSystem.WriteAllText("/repo/.npmrc", "registry=" + Registry + "\n");
        fileSystem.WriteAllText(
            "/repo/packages/apple/.npmrc",
            "//pkgs.dev.azure.com/org/_packaging/root/npm/registry/:_authToken=leaf-secret\n"
        );
        var processRunner = new FakeProcessRunner();
        var service = new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                WorkspaceDirectoryPath = "/repo/packages/apple",
                UserNpmrcPath = "/home/alice/.npmrc",
            }
        );
        var registryUrl = new Uri(Registry, UriKind.Absolute);
        CanonicalResourceIdentity resource = CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            registryUrl,
            feed: "root"
        );
        var declaration = new NpmPhase12RegistryDeclaration
        {
            SourcePath = "/repo/.npmrc",
            Key = "registry",
            RegistryUrl = registryUrl,
            ResourceIdentity = resource,
            AuthSelectors = NpmCompatibleAuthSelectorPolicy.Create(resource),
        };

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new NpmPhase12CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
                Ecosystem = CredentialEcosystem.Pnpm,
                TargetNpmrcPath = "/repo/.npmrc",
            }
        );

        ConfigurationChange change = Assert.Single(plan.Changes);
        Assert.Equal("/repo/.npmrc", change.TargetPathOrName);
        Assert.Equal(declaration.AuthSelectors.NpmAuthTokenKey, change.Key);
        Assert.Equal("short-lived-token", change.Value);
        Assert.True(change.IsSecretValue);
        Assert.Equal("pnpm", plan.Manifest.SafeMetadata["ecosystem"]);
        Assert.Equal(ConfigurationScope.User, plan.Scope);
        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Empty(processRunner.RecordedStartSpecs);
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

    private static NpmPhase12VerticalSliceService CreateNpmWorkspaceProcessFixture(
        ProcessResult processResult,
        out InMemoryFileSystem fileSystem,
        out FakeProcessRunner processRunner
    )
    {
        fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/repo/packages/apple");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/repo/package.json",
            """{"name":"root","private":true,"workspaces":["packages/[a-z]*"]}"""
        );
        fileSystem.WriteAllText(
            "/repo/packages/apple/package.json",
            """{"name":"apple"}"""
        );
        fileSystem.WriteAllText(
            "/repo/.npmrc",
            "@scope:registry=https://pkgs.dev.azure.com/org/_packaging/root/npm/registry/\n"
        );
        fileSystem.WriteAllText(
            "/repo/packages/apple/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/apple/npm/registry/\n"
        );
        processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(processResult);
        return new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                WorkspaceDirectoryPath = "/repo/packages/apple",
                UserNpmrcPath = "/home/alice/.npmrc",
            }
        );
    }

    private static NpmPhase12RegistryDeclaration CreateNpmDeclaration(
        string sourcePath,
        string feed
    )
    {
        var registryUrl = new Uri(
            "https://pkgs.dev.azure.com/org/_packaging/" + feed + "/npm/registry/",
            UriKind.Absolute
        );
        CanonicalResourceIdentity resource = CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            registryUrl,
            feed: feed
        );
        return new NpmPhase12RegistryDeclaration
        {
            SourcePath = sourcePath,
            Key = "registry",
            RegistryUrl = registryUrl,
            ResourceIdentity = resource,
            AuthSelectors = NpmCompatibleAuthSelectorPolicy.Create(resource),
        };
    }

    private static void AssertNpmWorkspaceResolutionFailure(
        NpmWorkspaceResolutionException exception,
        FakeProcessRunner processRunner
    )
    {
        ProcessStartSpec startSpec = Assert.Single(processRunner.RecordedStartSpecs);
        Assert.Equal("npm", startSpec.FileName);
        Assert.Equal(["prefix"], startSpec.Arguments);
        Assert.Equal("/repo/packages/apple", startSpec.WorkingDirectory);
        Assert.False(exception.Resolution.Succeeded);
        Assert.NotEmpty(exception.Message);
    }

    private sealed class EnvironmentVariables
    {
        private readonly IReadOnlyDictionary<string, string?> variables;

        public EnvironmentVariables(IReadOnlyDictionary<string, string?> variables) =>
            this.variables = variables;

        public string? Get(string name) =>
            variables.TryGetValue(name, out string? value) ? value : null;
    }

#pragma warning disable CA1707 // Test names mirror the behavior names in the approved phase plan.
    [Fact]
    public async Task ResolveWorkspaceAsync_ReturnsSucceeded_ForZeroExitWithValidPrefix()
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            new ProcessResult(0, "/repo/\n", string.Empty),
            out _,
            out FakeProcessRunner processRunner
        );

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertResolution(result, "Succeeded", "/repo", expectedFailureDetail: null);
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public async Task ResolveWorkspaceAsync_ReturnsNotRequired_WhenRegistryDoesNotRequireNpmResolution()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/repo/package");
        fileSystem.WriteAllText("/repo/package/package.json", """{"name":"package"}""");
        var processRunner = new FakeProcessRunner();
        var service = CreateService(fileSystem, processRunner, "/repo/package");

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertResolution(result, "NotRequired", expectedWorkspaceRoot: null, expectedFailureDetail: null);
        Assert.Empty(processRunner.RecordedStartSpecs);
    }

    [Fact]
    public async Task ResolveWorkspaceAsync_ReturnsLaunchFailure_WhenProcessCannotLaunch()
    {
        const string SensitiveError = "_authToken=launch-secret";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            ProcessResult.LaunchFailure(standardError: SensitiveError),
            out _,
            out FakeProcessRunner processRunner
        );

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertFailureResolution(result, "LaunchFailure", SensitiveError);
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public async Task ResolveWorkspaceAsync_ReturnsTimedOut_WhenNpmPrefixTimesOut()
    {
        const string SensitiveError = "timeout-secret";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            ProcessResult.TimedOut("/repo\n", SensitiveError),
            out _,
            out FakeProcessRunner processRunner
        );

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertFailureResolution(result, "TimedOut", SensitiveError);
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public async Task ResolveWorkspaceAsync_ReturnsNonZeroExit_WhenNpmPrefixExitsNonZero()
    {
        const string SensitiveError = "_authToken=nonzero-secret";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            new ProcessResult(23, string.Empty, SensitiveError),
            out _,
            out FakeProcessRunner processRunner
        );

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertFailureResolution(result, "NonZeroExit", SensitiveError);
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public async Task ResolveWorkspaceAsync_ReturnsOutputTooLarge_WhenNpmPrefixExceedsLimit()
    {
        const string SensitiveOutput = "/repo/partial-secret";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            ProcessResult.OutputTooLarge(SensitiveOutput, "stderr-secret"),
            out _,
            out FakeProcessRunner processRunner
        );

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertFailureResolution(result, "OutputTooLarge", SensitiveOutput, "stderr-secret");
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n")]
    [InlineData("/repo\n/repo/packages/apple\n")]
    [InlineData("relative/workspace\n")]
    [InlineData("/repo/missing\n")]
    public async Task ResolveWorkspaceAsync_ReturnsInvalidOutput_WhenNpmPrefixOutputIsNotOneValidDirectory(
        string standardOutput
    )
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            new ProcessResult(0, standardOutput, "invalid-output-secret"),
            out _,
            out FakeProcessRunner processRunner
        );

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertFailureResolution(result, "InvalidOutput", standardOutput, "invalid-output-secret");
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Theory]
    [InlineData("LaunchFailure")]
    [InlineData("TimedOut")]
    [InlineData("NonZeroExit")]
    [InlineData("OutputTooLarge")]
    [InlineData("InvalidOutput")]
    public void ConfigurePath_ThrowsNpmWorkspaceResolutionException_WithResolutionStatus(
        string expectedStatus
    )
    {
        const string SensitiveError = "_authToken=configure-secret";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            CreateResolutionFailureProcessResult(expectedStatus, SensitiveError),
            out _,
            out FakeProcessRunner processRunner
        );

        Exception exception = Assert.ThrowsAny<Exception>(
            service.DiscoverRegistryDeclarations
        );

        AssertTypedWorkspaceResolutionException(exception, expectedStatus, SensitiveError);
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Theory]
    [InlineData("LaunchFailure")]
    [InlineData("TimedOut")]
    [InlineData("NonZeroExit")]
    [InlineData("OutputTooLarge")]
    [InlineData("InvalidOutput")]
    public void PlanPath_ThrowsNpmWorkspaceResolutionException_WithResolutionStatus(
        string expectedStatus
    )
    {
        const string SensitiveError = "_authToken=plan-secret";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            CreateResolutionFailureProcessResult(expectedStatus, SensitiveError),
            out _,
            out FakeProcessRunner processRunner
        );

        Exception exception = Assert.ThrowsAny<Exception>(() =>
            service.CreateUserCredentialPlan(
                new NpmPhase12CredentialPlanRequest
                {
                    Declaration = CreateNpmDeclaration("/repo/.npmrc", "root"),
                    AuthToken = "short-lived-token",
                }
            )
        );

        AssertTypedWorkspaceResolutionException(exception, expectedStatus, SensitiveError);
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public void ResolveNpmExecutable_ReturnsDirectExecutable_WhenConfiguredPathIsLaunchable()
    {
        const string NpmExecutable = @"C:\tools\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsExecutableFixture(
            NpmExecutable,
            [NpmExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(NpmExecutable, GetRequiredString(result, "FileName", "ExecutablePath"));
        Assert.Equal(["prefix"], GetRequiredStringList(result, "Arguments"));
        Assert.Null(GetOptionalString(result, "FailureDetail"));
    }

    [Fact]
    public void ResolveNpmExecutable_ReturnsNodeAndNpmCliScript_ForStandardWindowsShimLayout()
    {
        const string NpmShim = @"C:\Program Files\nodejs\npm.cmd";
        const string NodeExecutable = @"C:\Program Files\nodejs\node.exe";
        const string NpmCliScript =
            @"C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js";
        NpmPhase12VerticalSliceService service = CreateWindowsExecutableFixture(
            NpmShim,
            [NpmShim, NodeExecutable, NpmCliScript]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(NodeExecutable, GetRequiredString(result, "FileName", "ExecutablePath"));
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
        Assert.Null(GetOptionalString(result, "FailureDetail"));
    }

    [Fact]
    public async Task ResolveWorkspaceAsync_LaunchesAppDataNpmShimWithFirstQuotedPathNode()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string FirstNodeFile = @"C:\Node First\NODE.EXE";
        const string FirstNodeExecutable = @"C:\Node First\node.exe";
        const string LaterNodeExecutable = @"C:\Node Later\node.exe";
        const string LaterNpmExecutable = @"C:\later\npm.exe";
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            ProcessResult.LaunchFailure(standardError: "expected-launch-failure")
        );
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"""C:\Users\alice\AppData\Roaming\npm"";""C:\Node First"";C:\Node Later;C:\later",
            @""".cMd""; "".eXe""",
            [
                NpmShim,
                NpmCliScript,
                FirstNodeFile,
                LaterNodeExecutable,
                LaterNpmExecutable,
            ],
            processRunner
        );

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertFailureResolution(result, "LaunchFailure", "expected-launch-failure");
        ProcessStartSpec startSpec = Assert.Single(processRunner.RecordedStartSpecs);
        Assert.Equal(FirstNodeExecutable, startSpec.FileName);
        Assert.Equal([NpmCliScript, "prefix"], startSpec.Arguments);
        Assert.Equal(@"C:\repo\packages\apple", startSpec.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(5), startSpec.Timeout);
        Assert.DoesNotContain(NpmShim, startSpec.Arguments);
        Assert.DoesNotContain(LaterNpmExecutable, startSpec.Arguments);
    }

    [Fact]
    public void ResolveNpmExecutable_PrefersSiblingNodeOverEarlierPathNode()
    {
        const string PathNodeExecutable = @"C:\Path Node\node.exe";
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string SiblingNodeExecutable =
            @"C:\Users\alice\AppData\Roaming\npm\node.exe";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"""C:\Path Node"";C:\Users\alice\AppData\Roaming\npm",
            @".EXE;.CMD",
            [PathNodeExecutable, NpmShim, SiblingNodeExecutable, NpmCliScript]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            SiblingNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
        Assert.Null(GetOptionalString(result, "FailureDetail"));
    }

    [Fact]
    public void ResolveNpmExecutable_DoesNotUseLaterNpmWhenAuthoritativeShimScriptIsMissing()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string SiblingNodeExecutable =
            @"C:\Users\alice\AppData\Roaming\npm\node.exe";
        const string LaterNpmExecutable = @"C:\later\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;C:\later",
            @".CMD;.EXE",
            [NpmShim, SiblingNodeExecutable, LaterNpmExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("MissingCandidate", GetStatusText(result));
        Assert.Contains(
            "script",
            GetFailureDetail(result),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_DoesNotUseLaterNpmWhenAuthoritativeShimHasNoNode()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string LaterNpmExecutable = @"C:\later\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;C:\later",
            @".CMD;.EXE",
            [NpmShim, NpmCliScript, LaterNpmExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("MissingCandidate", GetStatusText(result));
        Assert.Contains(
            "node.exe",
            GetFailureDetail(result),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_DoesNotTreatPathNodeAsExecutableWhenExeIsAbsentFromPathext()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string PathNodeExecutable = @"C:\Node\node.exe";
        const string LaterNpmShim = @"C:\later\npm.cmd";
        const string LaterNodeExecutable = @"C:\later\node.exe";
        const string LaterNpmCliScript =
            @"C:\later\node_modules\npm\bin\npm-cli.js";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;C:\Node;C:\later",
            @""".CmD""",
            [
                NpmShim,
                NpmCliScript,
                PathNodeExecutable,
                LaterNpmShim,
                LaterNodeExecutable,
                LaterNpmCliScript,
            ]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("MissingCandidate", GetStatusText(result));
        Assert.Contains(
            "node.exe",
            GetFailureDetail(result),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_UsesFirstPathDirectoryBeforeLaterExecutable()
    {
        const string UnsupportedNpmBat = @"C:\first\npm.bat";
        const string NpmShim = @"C:\first\npm.cmd";
        const string UnsupportedNpmCom = @"C:\first\npm.com";
        const string NodeExecutable = @"C:\first\node.exe";
        const string NpmCliScript = @"C:\first\node_modules\npm\bin\npm-cli.js";
        const string NpmExecutable = @"C:\second\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @";;""C:\first"";;C:\second;",
            @".BAT;.CmD;.EXE;.COM",
            [
                UnsupportedNpmBat,
                NpmShim,
                UnsupportedNpmCom,
                NodeExecutable,
                NpmCliScript,
                NpmExecutable,
            ]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(NodeExecutable, GetRequiredString(result, "FileName"));
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
    }

    [Fact]
    public void ResolveNpmExecutable_HonorsCmdBeforeExeInPathextWithinDirectory()
    {
        const string NpmShim = @"C:\tools\npm.cmd";
        const string NodeExecutable = @"C:\tools\node.exe";
        const string NpmCliScript = @"C:\tools\node_modules\npm\bin\npm-cli.js";
        const string NpmExecutable = @"C:\tools\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\tools",
            @".CMD;.EXE",
            [NpmShim, NodeExecutable, NpmCliScript, NpmExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(NodeExecutable, GetRequiredString(result, "FileName"));
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
    }

    [Fact]
    public void ResolveNpmExecutable_HonorsExeBeforeCmdInPathextWithinDirectory()
    {
        const string NpmShim = @"C:\tools\npm.cmd";
        const string NodeExecutable = @"C:\tools\node.exe";
        const string NpmCliScript = @"C:\tools\node_modules\npm\bin\npm-cli.js";
        const string NpmExecutable = @"C:\tools\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\tools",
            @".EXE;.CMD",
            [NpmShim, NodeExecutable, NpmCliScript, NpmExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(NpmExecutable, GetRequiredString(result, "FileName"));
        Assert.Equal(["prefix"], GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_UsesDefaultWindowsPathextOrderWhenAbsent()
    {
        const string NpmShim = @"C:\tools\npm.cmd";
        const string NodeExecutable = @"C:\tools\node.exe";
        const string NpmCliScript = @"C:\tools\node_modules\npm\bin\npm-cli.js";
        const string NpmExecutable = @"C:\tools\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\tools",
            pathExtValue: null,
            [NpmShim, NodeExecutable, NpmCliScript, NpmExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(NpmExecutable, GetRequiredString(result, "FileName"));
        Assert.Equal(["prefix"], GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_FailsClosedWhenFirstPathCmdLayoutIsInvalid()
    {
        const string UnsupportedNpmShim = @"C:\first\npm.cmd";
        const string LaterNpmExecutable = @"C:\second\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\first;C:\second",
            @".CMD;.EXE",
            [UnsupportedNpmShim, LaterNpmExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("InvalidCandidate", GetStatusText(result));
        Assert.NotEmpty(GetFailureDetail(result));
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_ReturnsMissingCandidateFailure_WhenDirectExecutableIsAbsent()
    {
        const string NpmExecutable = @"C:\tools\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsExecutableFixture(
            NpmExecutable,
            []
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("MissingCandidate", GetStatusText(result));
        Assert.Contains("npm", GetFailureDetail(result), StringComparison.OrdinalIgnoreCase);
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_ReturnsMissingCandidateFailure_WhenNodeExecutableIsAbsent()
    {
        const string NpmShim = @"C:\Program Files\nodejs\npm.cmd";
        const string NpmCliScript =
            @"C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js";
        NpmPhase12VerticalSliceService service = CreateWindowsExecutableFixture(
            NpmShim,
            [NpmShim, NpmCliScript]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("MissingCandidate", GetStatusText(result));
        Assert.Contains("node", GetFailureDetail(result), StringComparison.OrdinalIgnoreCase);
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_ReturnsMissingCandidateFailure_WhenNpmCliScriptIsAbsent()
    {
        const string NpmShim = @"C:\Program Files\nodejs\npm.cmd";
        const string NodeExecutable = @"C:\Program Files\nodejs\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsExecutableFixture(
            NpmShim,
            [NpmShim, NodeExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("MissingCandidate", GetStatusText(result));
        Assert.Contains("npm", GetFailureDetail(result), StringComparison.OrdinalIgnoreCase);
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_ReturnsInvalidCandidateFailure_WhenCandidateLayoutIsUnsupported()
    {
        const string UnsupportedNpmShim = @"C:\custom-layout\npm.cmd";
        NpmPhase12VerticalSliceService service = CreateWindowsExecutableFixture(
            UnsupportedNpmShim,
            [UnsupportedNpmShim, @"C:\custom-layout\unrelated.js"]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("InvalidCandidate", GetStatusText(result));
        Assert.NotEmpty(GetFailureDetail(result));
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public async Task ResolveWorkspaceAsync_ReturnsLaunchFailure_WhenResolvedWindowsCommandCannotLaunch()
    {
        const string NpmShim = @"C:\Program Files\nodejs\npm.cmd";
        const string NodeExecutable = @"C:\Program Files\nodejs\node.exe";
        const string NpmCliScript =
            @"C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js";
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueResult(
            ProcessResult.LaunchFailure(standardError: "windows-launch-secret")
        );
        NpmPhase12VerticalSliceService service = CreateWindowsExecutableFixture(
            NpmShim,
            [NpmShim, NodeExecutable, NpmCliScript],
            processRunner
        );

        object result = await InvokeResolveWorkspaceAsync(
            service,
            TestContext.Current.CancellationToken
        );

        AssertFailureResolution(result, "LaunchFailure", "windows-launch-secret");
        ProcessStartSpec startSpec = Assert.Single(processRunner.RecordedStartSpecs);
        Assert.Equal(NodeExecutable, startSpec.FileName);
        Assert.Equal([NpmCliScript, "prefix"], startSpec.Arguments);
        Assert.Equal(@"C:\repo\packages\apple", startSpec.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(5), startSpec.Timeout);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardErrorByteLimit);
    }

    [Fact]
    public async Task RunDoctorAsync_RemainsIncomplete_WhileResolutionProbeIsPending()
    {
        var probe = new TaskCompletionSource<ProcessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var runnerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var invocationReturned = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        NpmPhase12VerticalSliceService service = CreateNpmWorkspacePendingFixture(
            async (_, _) =>
            {
                runnerEntered.TrySetResult();
                return await probe.Task.ConfigureAwait(false);
            },
            out FakeProcessRunner processRunner
        );
        Task<NpmPhase12DoctorResult> doctorTask = Task.Run(async () =>
        {
            ValueTask<NpmPhase12DoctorResult> invocation = service.RunDoctorAsync(
                TestContext.Current.CancellationToken
            );
            invocationReturned.TrySetResult();
            return await invocation.ConfigureAwait(false);
        });

        await runnerEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        try
        {
            await invocationReturned.Task.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken
            );
            Assert.False(doctorTask.IsCompleted);
        }
        finally
        {
            probe.TrySetResult(new ProcessResult(0, "/repo\n", string.Empty));
            await doctorTask;
        }

        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public async Task RunDoctorAsync_ForwardsCallerCancellationToResolutionProbe()
    {
        using var cancellationSource = new CancellationTokenSource();
        var probe = new TaskCompletionSource<ProcessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var runnerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken observedToken = default;
        NpmPhase12VerticalSliceService service = CreateNpmWorkspacePendingFixture(
            async (_, cancellationToken) =>
            {
                observedToken = cancellationToken;
                runnerEntered.TrySetResult();
                return await probe.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            },
            out FakeProcessRunner processRunner
        );
        Task<NpmPhase12DoctorResult> doctorTask = Task.Run(async () =>
            await service.RunDoctorAsync(cancellationSource.Token).ConfigureAwait(false)
        );

        await runnerEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        cancellationSource.Cancel();
        try
        {
            Assert.Equal(cancellationSource.Token, observedToken);
            Assert.True(observedToken.IsCancellationRequested);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await doctorTask.ConfigureAwait(false)
            );
            AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
        }
        finally
        {
            probe.TrySetResult(new ProcessResult(0, "/repo\n", string.Empty));
            if (!doctorTask.IsCompleted)
            {
                await doctorTask;
            }
        }
    }

    [Fact]
    public async Task RunDoctorAsync_ReusesOneResolutionProbeAcrossDeclarationAndPlanChecks()
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            new ProcessResult(0, "/repo\n", string.Empty),
            out _,
            out FakeProcessRunner processRunner
        );
        for (int index = 0; index < 9; index++)
        {
            processRunner.EnqueueResult(new ProcessResult(0, "/repo\n", string.Empty));
        }

        NpmPhase12DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
        Assert.Equal("/repo/.npmrc", result.WorkspaceNpmrcPath);
        Assert.Equal("/repo/.npmrc", Assert.Single(result.RegistryDeclarations).SourcePath);
        Assert.True(result.NpmUserCredentialPlanValid);
        Assert.True(result.PnpmUserCredentialPlanValid);
        Assert.True(result.CiTemporaryCredentialPlanValid);
    }

    [Fact]
    public async Task RunDoctorAsync_DoesNotCacheResolutionAcrossCalls()
    {
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            new ProcessResult(0, "/repo\n", string.Empty),
            out InMemoryFileSystem fileSystem,
            out FakeProcessRunner processRunner
        );
        CreateDirectory(fileSystem, "/repo/packages");
        fileSystem.WriteAllText(
            "/repo/packages/.npmrc",
            "@scope:registry=https://pkgs.dev.azure.com/org/_packaging/second/npm/registry/\n"
        );
        for (int index = 0; index < 9; index++)
        {
            processRunner.EnqueueResult(new ProcessResult(0, "/repo/packages\n", string.Empty));
        }

        NpmPhase12DoctorResult first = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );
        NpmPhase12DoctorResult second = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, processRunner.RecordedStartSpecs.Count);
        Assert.All(processRunner.RecordedStartSpecs, AssertNpmPrefixStartSpec);
        Assert.Equal("/repo/.npmrc", first.WorkspaceNpmrcPath);
        Assert.Equal("/repo/packages/.npmrc", second.WorkspaceNpmrcPath);
    }

    [Theory]
    [InlineData("LaunchFailure")]
    [InlineData("TimedOut")]
    [InlineData("NonZeroExit")]
    [InlineData("OutputTooLarge")]
    [InlineData("InvalidOutput")]
    public async Task RunDoctorAsync_MapsExpectedResolutionFailureToTypedDoctorResult(
        string expectedStatus
    )
    {
        const string SensitiveError = "_authToken=doctor-secret";
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceProcessFixture(
            CreateResolutionFailureProcessResult(expectedStatus, SensitiveError),
            out _,
            out FakeProcessRunner processRunner
        );

        NpmPhase12DoctorResult result = await service.RunDoctorAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(expectedStatus, GetDoctorResolutionStatus(result));
        Assert.False(result.WorkspaceResolutionSucceeded);
        Assert.Null(result.WorkspaceNpmrcPath);
        Assert.False(result.WorkspaceNpmrcExists);
        Assert.Empty(result.RegistryDeclarations);
        Assert.False(result.RegistryDeclarationDiscovered);
        Assert.False(result.NpmUserCredentialPlanValid);
        Assert.False(result.PnpmUserCredentialPlanValid);
        Assert.False(result.CiTemporaryCredentialPlanValid);
        Assert.DoesNotContain(
            SensitiveError,
            result.ToString(),
            StringComparison.Ordinal
        );
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public async Task RunDoctorAsync_PropagatesUnexpectedResolutionException()
    {
        var sentinel = new InvalidOperationException("unexpected sentinel");
        var processRunner = new FakeProcessRunner();
        processRunner.EnqueueFailure(sentinel);
        NpmPhase12VerticalSliceService service = CreateNpmWorkspaceFixture(processRunner, out _);

        Exception? exception = await Record.ExceptionAsync(async () =>
            await service.RunDoctorAsync(TestContext.Current.CancellationToken)
        );

        Assert.Same(sentinel, exception);
        AssertNpmPrefixStartSpec(Assert.Single(processRunner.RecordedStartSpecs));
    }

    [Fact]
    public void ResolveNpmExecutable_WhenFirstGlobalShimUsesRelativeNodePath_ResolvesNodeAgainstWorkspace()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string WorkspaceNodeExecutable = @"C:\repo\packages\apple\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;.",
            @".CMD;.EXE",
            [NpmShim, NpmCliScript, WorkspaceNodeExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            WorkspaceNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
        Assert.Null(GetOptionalString(result, "FailureDetail"));
    }

    [Fact]
    public void ResolveNpmExecutable_WhenRelativeNodePathIsQuotedAndContainsSpaces_ResolvesNodeAgainstWorkspace()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string WorkspaceNodeExecutable =
            @"C:\repo\packages\apple\tools with spaces\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;"".\tools with spaces""",
            @".CMD;.EXE",
            [NpmShim, NpmCliScript, WorkspaceNodeExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            WorkspaceNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
        Assert.Null(GetOptionalString(result, "FailureDetail"));
    }

    [Fact]
    public void ResolveNpmExecutable_WhenNodePathIsFullyQualified_PreservesNormalizedAbsolutePath()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string NodeExecutable = @"C:\absolute\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;C:\intermediate\..\absolute",
            @".CMD;.EXE",
            [NpmShim, NpmCliScript, NodeExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            NodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
        Assert.Null(GetOptionalString(result, "FailureDetail"));
    }

    [Fact]
    public void ResolveNpmExecutable_WhenNodePathIsDriveRelative_SkipsItAndUsesLaterValidDirectory()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string DriveRelativeNodeExecutable = @"C:\tools\node.exe";
        const string LaterNodeExecutable =
            @"C:\repo\packages\apple\later\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;C:tools;.\later",
            @".CMD;.EXE",
            [
                NpmShim,
                NpmCliScript,
                DriveRelativeNodeExecutable,
                LaterNodeExecutable,
            ]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            LaterNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.NotEqual(
            DriveRelativeNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
    }

    [Fact]
    public void ResolveNpmExecutable_WhenRelativeNodeExistsButPathExtExcludesExe_DoesNotSelectNode()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string WorkspaceNodeExecutable =
            @"C:\repo\packages\apple\tools\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;.\tools",
            @".CMD",
            [NpmShim, NpmCliScript, WorkspaceNodeExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("MissingCandidate", GetStatusText(result));
        Assert.Contains(
            "node.exe",
            GetFailureDetail(result),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Null(GetOptionalString(result, "FileName", "ExecutablePath"));
        Assert.Empty(GetRequiredStringList(result, "Arguments"));
    }

    [Fact]
    public void ResolveNpmExecutable_WhenMultipleRelativeNodeDirectoriesExist_SelectsFirstPathMatch()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string FirstNodeExecutable =
            @"C:\repo\packages\apple\first\node.exe";
        const string SecondNodeExecutable =
            @"C:\repo\packages\apple\second\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;.\first;.\second",
            @".CMD;.EXE",
            [NpmShim, NpmCliScript, FirstNodeExecutable, SecondNodeExecutable]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            FirstNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.NotEqual(
            SecondNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
    }

    [Fact]
    public void ResolveNpmExecutable_WhenFirstNpmShimNeedsFallbackNode_DoesNotSwitchToLaterNpmBinding()
    {
        const string FirstNpmShim =
            @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string FirstNpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string WorkspaceNodeExecutable =
            @"C:\repo\packages\apple\tools\node.exe";
        const string LaterNpmExecutable = @"C:\later\npm.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;.\tools;C:\later",
            @".CMD;.EXE",
            [
                FirstNpmShim,
                FirstNpmCliScript,
                WorkspaceNodeExecutable,
                LaterNpmExecutable,
            ]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            WorkspaceNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [FirstNpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
        Assert.DoesNotContain(
            LaterNpmExecutable,
            GetRequiredStringList(result, "Arguments")
        );
        Assert.Null(GetOptionalString(result, "FailureDetail"));
    }

    [Fact]
    public void ResolveNpmExecutable_WhenFirstNpmShimNeedsFallbackNode_DoesNotSwitchToLaterNpmShim()
    {
        const string FirstNpmShim =
            @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string FirstNpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string WorkspaceNodeExecutable =
            @"C:\repo\packages\apple\tools\node.exe";
        const string LaterNpmShim = @"C:\later\npm.cmd";
        const string LaterNpmCliScript =
            @"C:\later\node_modules\npm\bin\npm-cli.js";
        const string LaterNodeExecutable = @"C:\later\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;.\tools;C:\later",
            @".CMD;.EXE",
            [
                FirstNpmShim,
                FirstNpmCliScript,
                WorkspaceNodeExecutable,
                LaterNpmShim,
                LaterNpmCliScript,
                LaterNodeExecutable,
            ]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            WorkspaceNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [FirstNpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
        Assert.DoesNotContain(
            LaterNpmCliScript,
            GetRequiredStringList(result, "Arguments")
        );
        Assert.NotEqual(
            LaterNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Null(GetOptionalString(result, "FailureDetail"));
    }

    [Fact]
    public void ResolveNpmExecutable_WhenNodePathIsBareDriveRelative_SkipsItAndUsesLaterValidDirectory()
    {
        const string NpmShim = @"C:\Users\alice\AppData\Roaming\npm\npm.cmd";
        const string NpmCliScript =
            @"C:\Users\alice\AppData\Roaming\npm\node_modules\npm\bin\npm-cli.js";
        const string WorkspaceNodeExecutable =
            @"C:\repo\packages\apple\node.exe";
        const string LaterNodeExecutable =
            @"C:\repo\packages\apple\later\node.exe";
        NpmPhase12VerticalSliceService service = CreateWindowsPathFixture(
            @"C:\Users\alice\AppData\Roaming\npm;C:;.\later",
            @".CMD;.EXE",
            [
                NpmShim,
                NpmCliScript,
                WorkspaceNodeExecutable,
                LaterNodeExecutable,
            ]
        );

        object result = InvokeResolveNpmExecutable(service);

        Assert.Equal("Succeeded", GetStatusText(result));
        Assert.Equal(
            LaterNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.NotEqual(
            WorkspaceNodeExecutable,
            GetRequiredString(result, "FileName", "ExecutablePath")
        );
        Assert.Equal(
            [NpmCliScript, "prefix"],
            GetRequiredStringList(result, "Arguments")
        );
    }

    private static NpmPhase12VerticalSliceService CreateService(
        InMemoryFileSystem fileSystem,
        FakeProcessRunner processRunner,
        string workspaceDirectory
    ) =>
        new(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner,
                WorkspaceDirectoryPath = workspaceDirectory,
                UserNpmrcPath = "/home/alice/.npmrc",
            }
        );

    private static NpmPhase12VerticalSliceService CreateNpmWorkspaceFixture(
        FakeProcessRunner processRunner,
        out InMemoryFileSystem fileSystem
    )
    {
        fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/repo/packages/apple");
        fileSystem.WriteAllText(
            "/repo/package.json",
            """{"name":"root","private":true,"workspaces":["packages/*"]}"""
        );
        fileSystem.WriteAllText(
            "/repo/packages/apple/package.json",
            """{"name":"apple"}"""
        );
        fileSystem.WriteAllText(
            "/repo/.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/root/npm/registry/\n"
        );
        return CreateService(fileSystem, processRunner, "/repo/packages/apple");
    }

    private static NpmPhase12VerticalSliceService CreateNpmWorkspacePendingFixture(
        Func<ProcessStartSpec, CancellationToken, Task<ProcessResult>> handler,
        out FakeProcessRunner processRunner
    )
    {
        processRunner = new FakeProcessRunner();
        processRunner.EnqueueHandler(handler);
        for (int index = 0; index < 9; index++)
        {
            processRunner.EnqueueResult(new ProcessResult(0, "/repo\n", string.Empty));
        }

        return CreateNpmWorkspaceFixture(processRunner, out _);
    }

    private static NpmPhase12VerticalSliceService CreateWindowsExecutableFixture(
        string configuredNpmPath,
        IReadOnlyList<string> existingFiles,
        FakeProcessRunner? processRunner = null
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        fileSystem.CreateDirectory(@"C:\repo\packages\apple");
        fileSystem.WriteAllText(
            @"C:\repo\package.json",
            """{"name":"root","private":true,"workspaces":["packages/*"]}"""
        );
        fileSystem.WriteAllText(
            @"C:\repo\packages\apple\package.json",
            """{"name":"apple"}"""
        );
        fileSystem.WriteAllText(
            @"C:\repo\.npmrc",
            "registry=https://pkgs.dev.azure.com/org/_packaging/root/npm/registry/\r\n"
        );
        foreach (string file in existingFiles)
        {
            int separatorIndex = file.LastIndexOf('\\');
            Assert.True(separatorIndex > 2);
            string directory = file[..separatorIndex];
            fileSystem.CreateDirectory(directory);
            fileSystem.WriteAllText(file, "fixture");
        }

        var options = new NpmPhase12VerticalSliceOptions
        {
            FileSystem = fileSystem,
            ProcessRunner = processRunner ?? new FakeProcessRunner(),
            WorkspaceDirectoryPath = @"C:\repo\packages\apple",
            UserNpmrcPath = @"C:\Users\alice\.npmrc",
        };
        System.Reflection.PropertyInfo? configuredPathProperty = options
            .GetType()
            .GetProperty("NpmExecutablePath");
        Assert.NotNull(configuredPathProperty);
        configuredPathProperty.SetValue(options, configuredNpmPath);
        return new NpmPhase12VerticalSliceService(options);
    }

    private static NpmPhase12VerticalSliceService CreateWindowsPathFixture(
        string pathValue,
        string? pathExtValue,
        IReadOnlyList<string> existingFiles,
        FakeProcessRunner? processRunner = null
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        fileSystem.CreateDirectory(@"C:\repo\packages\apple");
        fileSystem.WriteAllText(
            @"C:\repo\package.json",
            """{"name":"root","private":true,"workspaces":["packages/*"]}"""
        );
        fileSystem.WriteAllText(
            @"C:\repo\packages\apple\package.json",
            """{"name":"apple"}"""
        );
        foreach (string file in existingFiles)
        {
            int separatorIndex = file.LastIndexOf('\\');
            Assert.True(separatorIndex > 2);
            fileSystem.CreateDirectory(file[..separatorIndex]);
            fileSystem.WriteAllText(file, "fixture");
        }

        return new NpmPhase12VerticalSliceService(
            new NpmPhase12VerticalSliceOptions
            {
                FileSystem = fileSystem,
                ProcessRunner = processRunner ?? new FakeProcessRunner(),
                WorkspaceDirectoryPath = @"C:\repo\packages\apple",
                UserNpmrcPath = @"C:\Users\alice\.npmrc",
                EnvironmentVariableReader = name =>
                    string.Equals(name, "PATH", StringComparison.Ordinal)
                        ? pathValue
                    : string.Equals(name, "PATHEXT", StringComparison.Ordinal)
                        ? pathExtValue
                        : null,
            }
        );
    }

    private static ProcessResult CreateResolutionFailureProcessResult(
        string status,
        string sensitiveError
    ) =>
        status switch
        {
            "LaunchFailure" => ProcessResult.LaunchFailure(standardError: sensitiveError),
            "TimedOut" => ProcessResult.TimedOut("/repo\n", sensitiveError),
            "NonZeroExit" => new ProcessResult(19, string.Empty, sensitiveError),
            "OutputTooLarge" => ProcessResult.OutputTooLarge("/repo/partial", sensitiveError),
            "InvalidOutput" => new ProcessResult(0, "relative/workspace\n", sensitiveError),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };

    private static async Task<object> InvokeResolveWorkspaceAsync(
        NpmPhase12VerticalSliceService service,
        CancellationToken cancellationToken
    )
    {
        System.Reflection.MethodInfo? method = service
            .GetType()
            .GetMethods(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            )
            .SingleOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    "ResolveWorkspaceAsync",
                    StringComparison.Ordinal
                )
            );
        Assert.NotNull(method);
        object? invocation = method.Invoke(
            service,
            CreateInvocationArguments(method, cancellationToken)
        );
        return await AwaitInvocationResultAsync(invocation).ConfigureAwait(false);
    }

    private static object InvokeResolveNpmExecutable(NpmPhase12VerticalSliceService service)
    {
        System.Reflection.MethodInfo? method = service
            .GetType()
            .GetMethods(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            )
            .SingleOrDefault(candidate =>
                string.Equals(
                    candidate.Name,
                    "ResolveNpmExecutable",
                    StringComparison.Ordinal
                )
            );
        Assert.NotNull(method);
        object? result = method.Invoke(
            service,
            CreateInvocationArguments(method, TestContext.Current.CancellationToken)
        );
        Assert.NotNull(result);
        return result;
    }

    private static object?[] CreateInvocationArguments(
        System.Reflection.MethodInfo method,
        CancellationToken cancellationToken
    ) =>
        method
            .GetParameters()
            .Select(parameter =>
            {
                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    return (object)cancellationToken;
                }

                if (parameter.ParameterType == typeof(CredentialEcosystem))
                {
                    return CredentialEcosystem.Npm;
                }

                if (parameter.HasDefaultValue)
                {
                    return parameter.DefaultValue;
                }

                Assert.Fail(
                    $"Unsupported {method.Name} parameter '{parameter.Name}' of type "
                        + $"'{parameter.ParameterType.FullName}'."
                );
                return null;
            })
            .ToArray();

    private static async Task<object> AwaitInvocationResultAsync(object? invocation)
    {
        Assert.NotNull(invocation);
        Task task;
        if (invocation is Task directTask)
        {
            task = directTask;
        }
        else
        {
            System.Reflection.MethodInfo? asTask = invocation
                .GetType()
                .GetMethod("AsTask", Type.EmptyTypes);
            Assert.NotNull(asTask);
            task = Assert.IsAssignableFrom<Task>(asTask.Invoke(invocation, null));
        }

        await task.ConfigureAwait(false);
        System.Reflection.PropertyInfo? resultProperty = task
            .GetType()
            .GetProperty("Result");
        Assert.NotNull(resultProperty);
        object? result = resultProperty.GetValue(task);
        Assert.NotNull(result);
        return result;
    }

    private static void AssertResolution(
        object result,
        string expectedStatus,
        string? expectedWorkspaceRoot,
        string? expectedFailureDetail
    )
    {
        Assert.Equal(expectedStatus, GetStatusText(result));
        Assert.Equal(
            expectedWorkspaceRoot,
            GetOptionalString(result, "WorkspaceRootPath", "WorkspaceRoot")
        );
        Assert.Equal(expectedFailureDetail, GetOptionalString(result, "FailureDetail"));
    }

    private static void AssertFailureResolution(
        object result,
        string expectedStatus,
        params string[] sensitiveValues
    )
    {
        Assert.Equal(expectedStatus, GetStatusText(result));
        Assert.Null(GetOptionalString(result, "WorkspaceRootPath", "WorkspaceRoot"));
        string detail = GetFailureDetail(result);
        Assert.InRange(detail.Length, 1, 300);
        foreach (string sensitiveValue in sensitiveValues.Where(value => value.Length > 0))
        {
            Assert.DoesNotContain(sensitiveValue, detail, StringComparison.Ordinal);
            Assert.DoesNotContain(sensitiveValue, result.ToString(), StringComparison.Ordinal);
        }
    }

    private static void AssertTypedWorkspaceResolutionException(
        Exception exception,
        string expectedStatus,
        string sensitiveValue
    )
    {
        Assert.Equal("NpmWorkspaceResolutionException", exception.GetType().Name);
        Assert.Equal(expectedStatus, GetStatusText(exception));
        Assert.InRange(exception.Message.Length, 1, 300);
        Assert.DoesNotContain(sensitiveValue, exception.ToString(), StringComparison.Ordinal);
    }

    private static void AssertNpmPrefixStartSpec(ProcessStartSpec startSpec)
    {
        Assert.Equal("npm", startSpec.FileName);
        Assert.Equal(["prefix"], startSpec.Arguments);
        Assert.Equal("/repo/packages/apple", startSpec.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(5), startSpec.Timeout);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardOutputByteLimit);
        Assert.Equal(4096, startSpec.OutputCaptureOptions.StandardErrorByteLimit);
    }

    private static string GetDoctorResolutionStatus(NpmPhase12DoctorResult result)
    {
        object? directStatus = GetOptionalProperty(result, "WorkspaceResolutionStatus");
        if (directStatus is not null)
        {
            return directStatus.ToString() ?? string.Empty;
        }

        object resolution = GetRequiredProperty(result, "WorkspaceResolution");
        return GetStatusText(resolution);
    }

    private static string GetStatusText(object value) =>
        GetRequiredProperty(value, "Status").ToString() ?? string.Empty;

    private static string GetFailureDetail(object value) =>
        GetRequiredString(value, "FailureDetail");

    private static bool GetRequiredBoolean(object value, params string[] propertyNames)
    {
        object propertyValue = GetRequiredProperty(value, propertyNames);
        return Assert.IsType<bool>(propertyValue);
    }

    private static string GetRequiredString(object value, params string[] propertyNames)
    {
        object propertyValue = GetRequiredProperty(value, propertyNames);
        return Assert.IsType<string>(propertyValue);
    }

    private static IReadOnlyList<string> GetRequiredStringList(
        object value,
        params string[] propertyNames
    )
    {
        object propertyValue = GetRequiredProperty(value, propertyNames);
        return Assert.IsAssignableFrom<IReadOnlyList<string>>(propertyValue);
    }

    private static string? GetOptionalString(object value, params string[] propertyNames)
    {
        object? propertyValue = GetOptionalProperty(value, propertyNames);
        return propertyValue is null ? null : Assert.IsType<string>(propertyValue);
    }

    private static object GetRequiredProperty(object value, params string[] propertyNames)
    {
        object? propertyValue = GetOptionalProperty(value, propertyNames);
        Assert.NotNull(propertyValue);
        return propertyValue;
    }

    private static object? GetOptionalProperty(object value, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            System.Reflection.PropertyInfo? property = value
                .GetType()
                .GetProperty(
                    propertyName,
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                );
            if (property is not null)
            {
                return property.GetValue(value);
            }
        }

        return null;
    }
#pragma warning restore CA1707
}
