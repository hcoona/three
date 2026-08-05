using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
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
        Assert.True(result.WritesSupported);
        Assert.Contains("Phase 13B", result.UnsupportedWriteMessage, StringComparison.Ordinal);
        Assert.Contains("phase-1.4-accepted", result.WriteGateStatus, StringComparison.Ordinal);
        Assert.False(result.ForbiddenAuthIdentConflictDetected);
        AssertNoFilesystemMutationCalls(fileSystem.Calls);
    }

    [Fact]
    public void CreateUserCredentialPlanTargetsUserYarnrcAndWritesAuthPair()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
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
            service.DiscoverRegistryDeclarations()
        );

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
            }
        );

        Assert.Equal(ConfigurationScope.User, plan.Scope);
        Assert.True(plan.ContainsCredentialMaterial);
        Assert.Null(plan.TemporaryContainer);
        Assert.Equal("yarn", plan.Manifest.SafeMetadata["ecosystem"]);
        Assert.Equal(declaration.Key, plan.Manifest.SafeMetadata["registry-key"]);
        Assert.Equal(
            ConfigurationDeclarationPreservation.NotApplicable,
            plan.DeclarationPreservation
        );
        ConfigurationChange alwaysAuthChange = Assert.Single(
            plan.Changes,
            static change => change.Key.EndsWith(".npmAlwaysAuth", StringComparison.Ordinal)
        );
        ConfigurationChange authTokenChange = Assert.Single(
            plan.Changes,
            static change => change.Key.EndsWith(".npmAuthToken", StringComparison.Ordinal)
        );
        NpmCompatibleAuthSelectors expectedSelectors = NpmCompatibleAuthSelectorPolicy.Create(
            declaration.ResourceIdentity
        );
        Assert.Equal("/home/alice/.yarnrc.yml", alwaysAuthChange.TargetPathOrName);
        Assert.Equal(ConfigurationTargetKind.Yarnrc, alwaysAuthChange.TargetKind);
        Assert.Equal(ConfigurationChangeOperation.Set, alwaysAuthChange.Operation);
        Assert.False(alwaysAuthChange.IsSecretValue);
        Assert.Equal("true", alwaysAuthChange.Value);
        Assert.Equal(expectedSelectors.YarnAlwaysAuthKey, alwaysAuthChange.Key);
        Assert.Equal("/home/alice/.yarnrc.yml", authTokenChange.TargetPathOrName);
        Assert.Equal(expectedSelectors.YarnAuthTokenKey, authTokenChange.Key);
        Assert.True(authTokenChange.IsSecretValue);
        Assert.Equal("short-lived-token", authTokenChange.Value);
        Assert.Equal(expectedSelectors.YarnAuthTokenKey, plan.Manifest.EntrySelector);
        Assert.Equal(declaration.ResourceIdentity, plan.Manifest.ResourceIdentity);
        Assert.Equal(
            declaration.RegistryUrl,
            Assert.IsType<CanonicalResourceIdentity>(plan.Manifest.ResourceIdentity)
                .ServiceEndpoint
        );
        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.True(new ConfigurationManager().ValidatePlan(plan).IsValid);
    }

    [Fact]
    public void CreateUserCredentialPlanCanonicalizesTrailingSlashForBothAuthSelectors()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
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
            service.DiscoverRegistryDeclarations()
        );

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
            }
        );

        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(
            declaration.ResourceIdentity
        );
        Assert.Contains(plan.Changes, change => change.Key == selectors.YarnAuthTokenKey);
        Assert.Contains(plan.Changes, change => change.Key == selectors.YarnAlwaysAuthKey);
        Assert.All(
            plan.Changes,
            static change =>
                Assert.DoesNotContain("/registry/\"].npm", change.Key, StringComparison.Ordinal)
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
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
        );
        fileSystem.WriteAllText("/home/alice/.yarnrc.yml", "# user config\n");
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );
        YarnPhase13RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );
        ConfigurationChangePlan applyPlan = service.CreateUserCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
            }
        );
        const string manifestPath = "/state/phase13-yarn-user-manifest.json";
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
        string appliedYarnrc = fileSystem.ReadAllText("/home/alice/.yarnrc.yml");
        ConfigurationOwnershipManifestEntry[] ownedEntries = (
            applyResult.OwnershipManifest?.Entries ?? []
        ).ToArray();
        ConfigurationChangePlan removePlan = applyPlan with
        {
            Changes = applyPlan
                .Changes.Select(change =>
                    change with
                    {
                        Operation = ConfigurationChangeOperation.Remove,
                        Value = null,
                    }
                )
                .ToArray(),
        };

        ConfigurationPlanResult removeResult = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, applyResult.Operation);
        Assert.Equal(2, ownedEntries.Length);
        Assert.Contains("npmAlwaysAuth: true", appliedYarnrc);
        Assert.Contains("npmAuthToken: 'short-lived-token'", appliedYarnrc);
        Assert.DoesNotContain("short-lived-token", manifestJson, StringComparison.Ordinal);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, removeResult.Operation);
        Assert.Equal("# user config\n", fileSystem.ReadAllText("/home/alice/.yarnrc.yml"));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public void CreateCiTemporaryCredentialPlanUsesTemporaryHomeActivation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
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
            service.DiscoverRegistryDeclarations()
        );

        ConfigurationChangePlan plan = service.CreateCiTemporaryCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
                TemporaryHomePath = "/tmp/azureauth-yarn-home",
            }
        );

        Assert.Equal(ConfigurationScope.CiTemporary, plan.Scope);
        Assert.Equal(
            ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible,
            plan.DeclarationPreservation
        );
        Assert.NotNull(plan.TemporaryContainer);
        Assert.Equal(
            ConfigurationTemporaryContainerKind.TemporaryHome,
            plan.TemporaryContainer.Kind
        );
        Assert.Equal("/tmp/azureauth-yarn-home", plan.TemporaryContainer.ProductOwnedPath);
        Assert.NotNull(plan.TemporaryContainer.ActivationEnvironment);
        Assert.Equal("posix", plan.TemporaryContainer.ActivationEnvironment.Platform);
        Assert.Equal(
            "/tmp/azureauth-yarn-home",
            plan.TemporaryContainer.ActivationEnvironment.SetVariables["HOME"]
        );
        Assert.DoesNotContain(
            "USERPROFILE",
            plan.TemporaryContainer.ActivationEnvironment.SetVariables.Keys
        );
        Assert.Equal(
            ["YARN_RC_FILENAME"],
            plan.TemporaryContainer.ActivationEnvironment.ClearVariables
        );
        Assert.All(
            plan.Changes,
            static change =>
                Assert.Equal("/tmp/azureauth-yarn-home/.yarnrc.yml", change.TargetPathOrName)
        );
        ConfigurationChange alwaysAuthChange = Assert.Single(
            plan.Changes,
            static change => change.Key.EndsWith(".npmAlwaysAuth", StringComparison.Ordinal)
        );
        ConfigurationChange authTokenChange = Assert.Single(
            plan.Changes,
            static change => change.Key.EndsWith(".npmAuthToken", StringComparison.Ordinal)
        );
        string expectedAuthTokenSelector = NpmCompatibleAuthSelectorPolicy
            .Create(declaration.ResourceIdentity)
            .YarnAuthTokenKey;
        Assert.False(alwaysAuthChange.IsSecretValue);
        Assert.Equal("true", alwaysAuthChange.Value);
        Assert.True(authTokenChange.IsSecretValue);
        Assert.Equal("short-lived-token", authTokenChange.Value);
        Assert.Equal(expectedAuthTokenSelector, authTokenChange.Key);
        Assert.Equal(expectedAuthTokenSelector, plan.Manifest.EntrySelector);
        Assert.Equal(declaration.ResourceIdentity, plan.Manifest.ResourceIdentity);
        Assert.Equal(
            declaration.RegistryUrl,
            Assert.IsType<CanonicalResourceIdentity>(plan.Manifest.ResourceIdentity)
                .ServiceEndpoint
        );
        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.True(new ConfigurationManager().ValidatePlan(plan).IsValid);
    }

    [Fact]
    public void CreateCiTemporaryCredentialPlanRejectsAuthIdentConflictInExistingTemporaryHome()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/tmp/azureauth-yarn-home");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
        );
        fileSystem.WriteAllText(
            "/tmp/azureauth-yarn-home/.yarnrc.yml",
            """
            npmRegistries:
              'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/':
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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateCiTemporaryCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                    AuthToken = "short-lived-token",
                    TemporaryHomePath = "/tmp/azureauth-yarn-home",
                }
            )
        );

        Assert.Contains("npmAuthIdent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUserCredentialPlanRejectsAuthIdentConflictInCustomTargetYarnrc()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/custom");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
        );
        fileSystem.WriteAllText(
            "/custom/.yarnrc.yml",
            """
            npmRegistries:
              'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/':
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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateUserCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                    AuthToken = "short-lived-token",
                    TargetYarnrcPath = "/custom/.yarnrc.yml",
                }
            )
        );

        Assert.Contains("npmAuthIdent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CiTemporaryCredentialPlanAppliesAndRemoveDeletesTemporaryHome()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/tmp");
        CreateDirectory(fileSystem, "/state");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );
        ConfigurationChangePlan applyPlan = service.CreateCiTemporaryCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                AuthToken = "short-lived-token",
                TemporaryHomePath = "/tmp/azureauth-yarn-home",
            }
        );
        const string manifestPath = "/state/phase13-yarn-ci-manifest.json";
        var manager = new ConfigurationManager(
            fileSystem,
            manifestPath,
            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
        );

        ConfigurationPlanResult applyResult = await manager.ApplyAsync(
            applyPlan,
            TestContext.Current.CancellationToken
        );
        ConfigurationChangePlan removePlan = applyPlan with
        {
            Changes = applyPlan
                .Changes.Select(change =>
                    change with
                    {
                        Operation = ConfigurationChangeOperation.Remove,
                        Value = null,
                    }
                )
                .ToArray(),
        };

        ConfigurationPlanResult removeResult = await manager.RemoveAsync(
            removePlan,
            TestContext.Current.CancellationToken
        );

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, applyResult.Operation);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, removeResult.Operation);
        Assert.False(fileSystem.DirectoryExists("/tmp/azureauth-yarn-home"));
        Assert.False(fileSystem.FileExists("/tmp/azureauth-yarn-home/.yarnrc.yml"));
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public void CreateCiTemporaryCredentialPlanRejectsUserOnlyRegistryDeclaration()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/home/alice/.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
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
            service.DiscoverRegistryDeclarations()
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateCiTemporaryCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = declaration,
                    AuthToken = "short-lived-token",
                    TemporaryHomePath = "/tmp/azureauth-yarn-home",
                }
            )
        );

        Assert.Contains("registry declaration to remain visible", exception.Message);
    }

    [Fact]
    public void CreateCiTemporaryCredentialPlanClearsYarnRcFilenameOverrideDuringActivation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        var environment = new EnvironmentVariables(
            new Dictionary<string, string?> { ["YARN_RC_FILENAME"] = ".selected.yarnrc.yml" }
        );
        fileSystem.WriteAllText(
            "/workspace/.selected.yarnrc.yml",
            "npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/'\n"
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
        YarnPhase13RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        ConfigurationChangePlan plan = service.CreateCiTemporaryCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
                TemporaryHomePath = "/tmp/azureauth-yarn-home",
            }
        );

        ConfigurationActivationEnvironment activation =
            plan.TemporaryContainer!.ActivationEnvironment!;
        Assert.Equal("/tmp/azureauth-yarn-home", activation.SetVariables["HOME"]);
        Assert.Equal(["YARN_RC_FILENAME"], activation.ClearVariables);
    }

    [Fact]
    public void CreateUserCredentialPlanRejectsScopedNpmAuthIdentConflict()
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
        YarnPhase13RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateUserCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = declaration,
                    AuthToken = "short-lived-token",
                }
            )
        );

        Assert.Contains("npmAuthIdent", exception.Message, StringComparison.Ordinal);
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
            (
                await service.RunDoctorAsync(TestContext.Current.CancellationToken)
            ).RegistryDeclarations
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
            new Dictionary<string, string?> { ["YARN_RC_FILENAME"] = "/tmp/selected.yarnrc.yml" }
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
            new Dictionary<string, string?> { ["YARN_RC_FILENAME"] = ".selected.yarnrc.yml" }
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

    [Theory]
    [InlineData("npmAuthToken", "project-secret-value")]
    [InlineData("npmAuthIdent", "user:password")]
    [InlineData("npmAlwaysAuth", "false")]
    public void CreateUserCredentialPlanRejectsProjectRegistryAuthShadow(
        string selector,
        string value
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
                + "npmRegistries:\n"
                + "  https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:\n"
                + "    "
                + selector
                + ": '"
                + value
                + "'\n"
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateUserCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                    AuthToken = "short-lived-token",
                }
            )
        );

        Assert.Contains(selector, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("npmAuthToken", "project-secret-value")]
    [InlineData("npmAuthIdent", "user:password")]
    [InlineData("npmAlwaysAuth", "false")]
    public void CreateCiTemporaryCredentialPlanRejectsMatchingProjectScopeAuthShadow(
        string selector,
        string value
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
                + "npmScopes:\n"
                + "  shadow:\n"
                + "    npmRegistryServer: "
                + "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry\n"
                + "    "
                + selector
                + ": '"
                + value
                + "'\n"
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateCiTemporaryCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = Assert.Single(
                        service.DiscoverRegistryDeclarations(),
                        static declaration => declaration.Scope is null
                    ),
                    AuthToken = "short-lived-token",
                    TemporaryHomePath = "/work/ci-yarn-home",
                }
            )
        );

        Assert.Contains("npmScopes.shadow." + selector, exception.Message);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("npmAuthToken", "project-secret-value")]
    [InlineData("npmAuthIdent", "user:password")]
    public void CreateUserCredentialPlanRejectsSameScopeProjectAuthWithoutRegistry(
        string selector,
        string value
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmScopes:\n"
                + "  azure:\n"
                + "    "
                + selector
                + ": '"
                + value
                + "'\n"
        );
        fileSystem.WriteAllText(
            "/home/alice/.yarnrc.yml",
            """
            npmScopes:
              '@azure':
                npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/
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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateUserCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                    AuthToken = "short-lived-token",
                }
            )
        );

        Assert.Contains("npmScopes.azure." + selector, exception.Message);
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUserCredentialPlanAllowsUnrelatedProjectScopeAuthWithoutRegistry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            """
            npmScopes:
              unrelated:
                npmAuthToken: project-secret-value
            """
        );
        fileSystem.WriteAllText(
            "/home/alice/.yarnrc.yml",
            """
            npmScopes:
              azure:
                npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/
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

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = Assert.Single(service.DiscoverRegistryDeclarations()),
                AuthToken = "short-lived-token",
            }
        );

        Assert.Equal(2, plan.Changes.Count);
    }

    [Fact]
    public void CreateUserCredentialPlanAllowsSameScopeProjectAuthForNonUserDeclaration()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            """
            npmScopes:
              azure:
                npmAuthToken: project-secret-value
            """
        );
        fileSystem.WriteAllText(
            "/home/alice/.yarnrc.yml",
            """
            npmScopes:
              azure:
                npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/
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
        YarnPhase13RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        ) with
        {
            SourcePath = "/state/explicit-registry-declaration.yarnrc.yml",
        };

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
            }
        );

        Assert.Equal(2, plan.Changes.Count);
    }

    [Fact]
    public void CreateUserCredentialPlanAllowsSameScopeProjectAuthForDifferentRegistry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/home/alice/.yarnrc.yml",
            """
            npmScopes:
              azure:
                npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/
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
        YarnPhase13RegistryDeclaration declaration = Assert.Single(
            service.DiscoverRegistryDeclarations()
        );
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            """
            npmScopes:
              azure:
                npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/other/npm/registry/
                npmAuthToken: project-secret-value
            """
        );

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = declaration,
                AuthToken = "short-lived-token",
            }
        );

        Assert.Equal(2, plan.Changes.Count);
    }

    [Theory]
    [InlineData("npmRegistries")]
    [InlineData("npmScopes")]
    public void CreateUserCredentialPlanRejectsFourSpaceProjectAuthShadow(string blockKind)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        const string RegistryUrl =
            "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/";
        string authBlock =
            blockKind == "npmRegistries"
                ? $"""
                  npmRegistries:
                      {RegistryUrl}:
                          npmAuthToken: project-secret-value
                  """
                : $"""
                  npmScopes:
                      shadow:
                          npmRegistryServer: {RegistryUrl}
                          npmAuthToken: project-secret-value
                  """;
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            "npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
                + authBlock
        );
        var service = new YarnPhase13VerticalSliceService(
            new YarnPhase13VerticalSliceOptions
            {
                FileSystem = fileSystem,
                WorkspaceDirectoryPath = "/workspace",
                UserHomeDirectoryPath = "/home/alice",
            }
        );

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateUserCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = Assert.Single(
                        service.DiscoverRegistryDeclarations(),
                        static declaration => declaration.Scope is null
                    ),
                    AuthToken = "short-lived-token",
                }
            )
        );

        Assert.Contains("npmAuthToken", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("project-secret-value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateUserCredentialPlanAllowsUnrelatedFourSpaceRegistryAndScopeAuth()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        const string RegistryUrl =
            "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/";
        const string OtherRegistryUrl =
            "https://pkgs.dev.azure.com/org/_packaging/other/npm/registry/";
        const string ScopedRegistryUrl =
            "https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/";
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            $"""
            npmRegistryServer: {RegistryUrl}
            npmRegistries:
                {OtherRegistryUrl}:
                    npmAuthToken: unrelated-registry-token
            npmScopes:
                unrelated:
                    npmRegistryServer: {ScopedRegistryUrl}
                    npmAlwaysAuth: false
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

        ConfigurationChangePlan plan = service.CreateUserCredentialPlan(
            new YarnPhase13CredentialPlanRequest
            {
                Declaration = Assert.Single(
                    service.DiscoverRegistryDeclarations(),
                    static declaration => declaration.Scope is null
                ),
                AuthToken = "short-lived-token",
            }
        );

        Assert.Equal(2, plan.Changes.Count);
    }

    [Fact]
    public void CreateUserCredentialPlanRejectsMalformedManagedProjectAuthStructure()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectory(fileSystem, "/workspace");
        CreateDirectory(fileSystem, "/home/alice");
        fileSystem.WriteAllText(
            "/workspace/.yarnrc.yml",
            """
            npmRegistryServer: https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/
            npmRegistries:
                https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:
                    npmAlwaysAuth: true
                  npmAuthToken: project-secret-value
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

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.CreateUserCredentialPlan(
                new YarnPhase13CredentialPlanRequest
                {
                    Declaration = Assert.Single(
                        service.DiscoverRegistryDeclarations(),
                        static declaration => declaration.Scope is null
                    ),
                    AuthToken = "short-lived-token",
                }
            )
        );

        Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("project-secret-value", exception.Message, StringComparison.Ordinal);
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
