using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class ConfigurationPhase14VerticalSliceServiceTests
{
    [Fact]
    public async Task ProductionConfigureWithoutRegistryDeclarationFailsBeforeAnyWrite()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CredentialProviderCompositionRoot root = CredentialProviderCompositionRoot.CreateProduction(
            new CredentialProviderProductionOptions
            {
                FileSystem = fileSystem,
                ConfigurationOptions = new ConfigurationPhase14VerticalSliceOptions
                {
                    FileSystem = fileSystem,
                    StateDirectoryPath = "/state/production",
                    EnvironmentVariableReader = _ => null,
                },
            });
        ConfigurationPhase14VerticalSliceService service = root.CreateConfigurationService();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ConfigureAsync(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            "Package registry configuration is required. Run azureauth-credprovider configure "
                + "npm --registry-url <azure-artifacts-npm-url>.",
            exception.Message);
        Assert.False(fileSystem.FileExists(service.Paths.NpmUserNpmrcPath));
        Assert.False(fileSystem.DirectoryExists(service.Paths.ManifestDirectoryPath));
    }

    [Fact]
    public async Task ExplicitRegistryDeclarationRoundTripsWithoutSyntheticTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        Uri registryUrl = new(
            "https://pkgs.dev.azure.com/real-org/real-project/"
                + "_packaging/real-feed/npm/registry/");
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/explicit",
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(
                    new SilentTestAcquisitionService()),
                EnvironmentVariableReader = _ => null,
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = registryUrl,
                },
            });

        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken);

        string configured = fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath);
        Assert.Contains(
            "//pkgs.dev.azure.com/real-org/real-project/_packaging/real-feed/npm/registry/",
            configured,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "pkgs.dev.azure.com/org/_packaging/feed",
            configured,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfigureAndUnconfigureNpmApplyAndRemoveOwnedNpmrcToken()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        ConfigurationPhase14PlanResult configureResult = await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string configuredNpmrc = fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath);
        ConfigurationPhase14PlanResult unconfigureResult = await service.UnconfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, configureResult.PlanResult.State);
        Assert.Equal(1, configureResult.ChangeCount);
        Assert.Contains(
            "_authToken=fake-token-",
            configuredNpmrc,
            StringComparison.Ordinal
        );
        Assert.Equal(ConfigurationPlanState.Applied, unconfigureResult.PlanResult.State);
        Assert.Equal(1, unconfigureResult.ChangeCount);
        Assert.DoesNotContain(
            "fake-token-",
            fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PythonDryRunAndExecutionProduceEquivalentPlansWithoutDryRunMutation()
    {
        var dryRunFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var executionFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var dryRunService = CreateService(dryRunFileSystem);
        var executionService = CreateService(executionFileSystem);

        ConfigurationPhase14PlanResult dryRun = await dryRunService.DryRunConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken);
        ConfigurationPhase14PlanResult executed = await executionService.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken);

        Assert.Equal(executed.ChangeCount, dryRun.ChangeCount);
        Assert.Equal(
            executed.PlanResults.Select(static result => (
                result.Plan.PlanId,
                result.Plan.ChangeSetId,
                result.Plan.Scope,
                result.Plan.Manifest.PreviousOwnedEntryHash)),
            dryRun.PlanResults.Select(static result => (
                result.Plan.PlanId,
                result.Plan.ChangeSetId,
                result.Plan.Scope,
                result.Plan.Manifest.PreviousOwnedEntryHash)));
        Assert.Equal(
            executed.PlanResults.SelectMany(static result => result.Changes),
            dryRun.PlanResults.SelectMany(static result => result.Changes));
        Assert.All(
            dryRun.PlanResults,
            static result => Assert.Equal(ConfigurationPlanState.Planned, result.State));
        Assert.False(dryRunFileSystem.DirectoryExists(dryRunService.Paths.ManifestDirectoryPath));
        Assert.False(dryRun.OwnershipManifestPresent);
    }

    [Fact]
    public async Task PythonUnconfigureDryRunAndExecutionProduceEquivalentRemovalPlans()
    {
        var dryRunFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var executionFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var dryRunService = CreateService(dryRunFileSystem);
        var executionService = CreateService(executionFileSystem);
        await dryRunService.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken);
        await executionService.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken);

        ConfigurationPhase14PlanResult dryRun = await dryRunService.DryRunUnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken);
        ConfigurationPhase14PlanResult executed = await executionService.UnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken);

        Assert.Equal(executed.ChangeCount, dryRun.ChangeCount);
        Assert.Equal(
            executed.PlanResults.Select(static result => (
                result.Plan.PlanId,
                result.Plan.ChangeSetId,
                result.Plan.Scope,
                result.Plan.Manifest.PreviousOwnedEntryHash)),
            dryRun.PlanResults.Select(static result => (
                result.Plan.PlanId,
                result.Plan.ChangeSetId,
                result.Plan.Scope,
                result.Plan.Manifest.PreviousOwnedEntryHash)));
        Assert.Equal(
            executed.PlanResults.SelectMany(static result => result.Changes),
            dryRun.PlanResults.SelectMany(static result => result.Changes));
        Assert.True(dryRun.OwnershipManifestPresent);
        Assert.False(executed.OwnershipManifestPresent);
    }

    [Fact]
    public async Task UnconfigureDryRunAndExecutionBothRejectMalformedOwnershipManifest()
    {
        var dryRunFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var executionFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var dryRunService = CreateService(dryRunFileSystem);
        var executionService = CreateService(executionFileSystem);
        string dryRunManifestPath = Path.Combine(
            dryRunService.Paths.ManifestDirectoryPath,
            "python-user-ownership-manifest.json");
        string executionManifestPath = Path.Combine(
            executionService.Paths.ManifestDirectoryPath,
            "python-user-ownership-manifest.json");
        dryRunFileSystem.CreateDirectory(dryRunService.Paths.ManifestDirectoryPath);
        executionFileSystem.CreateDirectory(executionService.Paths.ManifestDirectoryPath);
        dryRunFileSystem.WriteAllText(dryRunManifestPath, """{"malformed":true}""");
        executionFileSystem.WriteAllText(executionManifestPath, """{"malformed":true}""");

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await dryRunService.DryRunUnconfigureAsync(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await executionService.UnconfigureAsync(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            """{"malformed":true}""",
            dryRunFileSystem.ReadAllText(dryRunManifestPath));
        Assert.Equal(
            """{"malformed":true}""",
            executionFileSystem.ReadAllText(executionManifestPath));
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task CiUnconfigureMalformedManifestPlansKnownContainerAndDryRunDoesNotMutate(
        CredentialEcosystem ecosystem)
    {
        const string MalformedManifest = """{"malformed":true}""";
        var dryRunFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var executionFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var dryRunService = CreateService(dryRunFileSystem);
        var executionService = CreateService(executionFileSystem);
        string manifestName = ecosystem.ToString().ToLowerInvariant()
            + "-ci-temporary-ownership-manifest.json";
        string dryRunManifestPath = Path.Combine(
            dryRunService.Paths.CiTemporaryManifestDirectoryPath,
            manifestName);
        string executionManifestPath = Path.Combine(
            executionService.Paths.CiTemporaryManifestDirectoryPath,
            manifestName);
        string dryRunContainerPath = CreateKnownCiContainer(
            dryRunFileSystem,
            dryRunService.Paths,
            ecosystem);
        string executionContainerPath = CreateKnownCiContainer(
            executionFileSystem,
            executionService.Paths,
            ecosystem);
        dryRunFileSystem.CreateDirectory(dryRunService.Paths.CiTemporaryManifestDirectoryPath);
        executionFileSystem.CreateDirectory(executionService.Paths.CiTemporaryManifestDirectoryPath);
        dryRunFileSystem.WriteAllText(dryRunManifestPath, MalformedManifest);
        executionFileSystem.WriteAllText(executionManifestPath, MalformedManifest);

        ConfigurationPhase14PlanResult dryRun = await dryRunService.DryRunUnconfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken);
        ConfigurationPhase14PlanResult executed = await executionService.UnconfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken);

        Assert.True(dryRun.OwnershipManifestCleanupIncomplete);
        Assert.True(executed.OwnershipManifestCleanupIncomplete);
        Assert.Equal(ConfigurationPlanOperation.DryRun, dryRun.PlanResult.Operation);
        Assert.Equal(ConfigurationPlanOperation.Remove, executed.PlanResult.Operation);
        Assert.Equal(ConfigurationPlanState.Planned, dryRun.PlanResult.State);
        Assert.Equal(ConfigurationPlanState.Applied, executed.PlanResult.State);
        Assert.Equal(executed.PlanResult.Plan.PlanId, dryRun.PlanResult.Plan.PlanId);
        Assert.Equal(executed.PlanResult.Plan.ChangeSetId, dryRun.PlanResult.Plan.ChangeSetId);
        Assert.Equal(executed.PlanResult.Plan.Scope, dryRun.PlanResult.Plan.Scope);
        Assert.Equal(
            executed.PlanResult.Plan.DeclarationPreservation,
            dryRun.PlanResult.Plan.DeclarationPreservation);
        Assert.Equal(
            executed.PlanResult.Plan.TemporaryContainer!.Kind,
            dryRun.PlanResult.Plan.TemporaryContainer!.Kind);
        Assert.Equal(
            executed.PlanResult.Plan.TemporaryContainer.ProductOwnedPath,
            dryRun.PlanResult.Plan.TemporaryContainer.ProductOwnedPath);
        Assert.Equal(executed.PlanResult.Changes, dryRun.PlanResult.Changes);
        ConfigurationPlannedChange action = Assert.Single(dryRun.PlanResult.Changes);
        Assert.Equal(ConfigurationChangeOperation.Remove, action.Operation);
        Assert.Equal(
            ecosystem == CredentialEcosystem.Yarn
                ? ConfigurationTargetKind.Yarnrc
                : ConfigurationTargetKind.Npmrc,
            action.TargetKind);
        Assert.Equal(
            ecosystem == CredentialEcosystem.Yarn
                ? Path.Combine(dryRunContainerPath, ".yarnrc.yml")
                : dryRunContainerPath,
            action.TargetPathOrName);
        Assert.Equal(
            executionContainerPath,
            executed.PlanResult.Plan.TemporaryContainer!.ProductOwnedPath);
        Assert.True(KnownCiContainerExists(dryRunFileSystem, dryRunContainerPath, ecosystem));
        Assert.False(KnownCiContainerExists(
            executionFileSystem,
            executionContainerPath,
            ecosystem));
        Assert.Equal(MalformedManifest, dryRunFileSystem.ReadAllText(dryRunManifestPath));
        Assert.Equal(MalformedManifest, executionFileSystem.ReadAllText(executionManifestPath));
    }

    [Fact]
    public async Task ConfigureYarnAppliesOwnedAuthPair()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        string yarnrc = fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath);
        Assert.Equal(ConfigurationPlanState.Applied, result.PlanResult.State);
        Assert.Equal(2, result.ChangeCount);
        Assert.Contains("npmAlwaysAuth: true", yarnrc);
        Assert.Contains("npmAuthToken: 'fake-token-", yarnrc);
    }

    [Fact]
    public async Task ConfigurePythonAppliesBackendAndShimPlans()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.PlanResults.Count);
        Assert.Equal(2, result.ChangeCount);
        Assert.All(
            result.PlanResults,
            planResult => Assert.Equal(ConfigurationPlanState.Applied, planResult.State)
        );
    }

    [Fact]
    public async Task ConfigureAndUnconfigurePythonRemoveBackendAndShimPlans()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.PlanResults.Count);
        Assert.Equal(2, result.ChangeCount);
        Assert.False(result.OwnershipManifestPresent);
        Assert.All(
            result.PlanResults,
            planResult => Assert.Equal(ConfigurationPlanState.Applied, planResult.State)
        );
    }

    [Fact]
    public async Task ConfigureSupportedUserEcosystemsUseIndependentOwnershipManifests()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        ConfigurationPhase14PlanResult pythonResult = await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult npmResult = await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult pnpmResult = await service.ConfigureAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult yarnResult = await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, pythonResult.ChangeCount);
        Assert.Equal(1, npmResult.ChangeCount);
        Assert.Equal(1, pnpmResult.ChangeCount);
        Assert.Equal(2, yarnResult.ChangeCount);
        Assert.All(
            new[] { pythonResult, npmResult, pnpmResult, yarnResult },
            result => Assert.True(result.OwnershipManifestPresent)
        );
    }

    [Fact]
    public async Task ConfigureNpmCiTemporaryRequiresAzurePipelinesSystemAccessToken()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ConfigureAsync(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.CiTemporary,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(
            "Azure Pipelines system access token is unavailable in the environment.",
            exception.Message
        );
    }

    [Fact]
    public async Task ConfigureNpmCiTemporaryBypassesIdentityProvider()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var identityProvider = new CapturingIdentityProvider();
        var service = CreateService(
            fileSystem,
            identityProvider,
            name => string.Equals(name, "SYSTEM_ACCESSTOKEN", StringComparison.Ordinal)
                ? "system-token"
                : null
        );

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(identityProvider.Requests);
        Assert.Equal(ConfigurationPlanState.Applied, result.PlanResult.State);
        Assert.Equal(ConfigurationScope.CiTemporary, result.PlanResult.Plan.Scope);
        Assert.True(result.PlanResult.Plan.ContainsCredentialMaterial);
        Assert.Contains(
            "system-token",
            fileSystem.ReadAllText(service.Paths.NpmCiTemporaryNpmrcPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task UnconfigureNpmCiTemporaryRemovesTemporaryNpmrcContainer()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);

        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        Assert.True(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));

        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.PlanResult.State);
        Assert.False(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
        Assert.False(result.OwnershipManifestPresent);
    }

    [Fact]
    public async Task UnconfigureYarnCiTemporaryRemovesTemporaryHomeContainer()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);

        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        Assert.True(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));

        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ConfigurationPlanState.Applied, result.PlanResult.State);
        Assert.False(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));
        Assert.False(result.OwnershipManifestPresent);
    }

    [Fact]
    public async Task DoctorAggregatesUserConfigurationAndCiGuidanceWithoutSecrets()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14DoctorResult result = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14EcosystemDoctorResult yarn = Assert.Single(
            result.Ecosystems,
            ecosystemResult => ecosystemResult.Ecosystem == CredentialEcosystem.Yarn
                && ecosystemResult.Scope == ConfigurationPhase14Scope.User
        );
        Assert.True(yarn.ConfigurationPlanValid);
        Assert.True(yarn.OwnershipManifestPresent);
        Assert.True(yarn.OwnedTargetPresent);
        Assert.False(yarn.TemporaryContainerPresent);
        Assert.False(result.AzurePipelinesSystemAccessTokenPresent);
        Assert.False(result.PersistentDerivedCredentialCacheEnabled);
    }

    [Fact]
    public async Task CleanupCiTemporaryRemovesAllOwnedPackageContainers()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);

        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        await service.ConfigureAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupResult result = await service.CleanupAsync(
            ecosystem: null,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.Ecosystems.Count);
        Assert.Equal(4, result.ChangeCount);
        Assert.All(result.Ecosystems, cleanupResult =>
        {
            Assert.Equal("removed", cleanupResult.State);
            Assert.False(cleanupResult.OwnershipManifestPresent);
            Assert.False(cleanupResult.TemporaryContainerPresent);
        });
        Assert.False(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
        Assert.False(fileSystem.FileExists(service.Paths.PnpmCiTemporaryNpmrcPath));
        Assert.False(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));
    }

    [Fact]
    public async Task LogoutWithMalformedManifestRemovesKnownContainersAndContinuesCleanup()
    {
        const string MalformedManifest = """{"secret":"must-not-be-reported"}""";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);
        foreach (CredentialEcosystem ecosystem in
            new[] { CredentialEcosystem.Npm, CredentialEcosystem.Pnpm, CredentialEcosystem.Yarn })
        {
            await service.ConfigureAsync(
                ecosystem,
                ConfigurationPhase14Scope.CiTemporary,
                TestContext.Current.CancellationToken
            );
        }

        string npmManifestPath = Path.Combine(
            service.Paths.CiTemporaryManifestDirectoryPath,
            "npm-ci-temporary-ownership-manifest.json"
        );
        fileSystem.WriteAllText(npmManifestPath, MalformedManifest);

        ConfigurationPhase14CleanupResult result = await service.LogoutAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, result.Ecosystems.Count);
        ConfigurationPhase14CleanupEcosystemResult npm = Assert.Single(
            result.Ecosystems,
            ecosystem => ecosystem.Ecosystem == CredentialEcosystem.Npm
        );
        Assert.Equal("incomplete", npm.State);
        Assert.True(npm.OwnershipManifestPresent);
        Assert.False(npm.TemporaryContainerPresent);
        Assert.Equal(MalformedManifest, fileSystem.ReadAllText(npmManifestPath));
        Assert.False(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
        Assert.False(fileSystem.FileExists(service.Paths.PnpmCiTemporaryNpmrcPath));
        Assert.False(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));
        Assert.All(
            result.Ecosystems.Where(ecosystem => ecosystem.Ecosystem != CredentialEcosystem.Npm),
            ecosystem =>
            {
                Assert.Equal("removed", ecosystem.State);
                Assert.False(ecosystem.OwnershipManifestPresent);
                Assert.False(ecosystem.TemporaryContainerPresent);
            }
        );
    }

    [Fact]
    public async Task CleanupCiTemporaryRemovesOrphanedKnownTemporaryContainer()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);
        fileSystem.CreateDirectory(Path.GetDirectoryName(service.Paths.NpmCiTemporaryNpmrcPath)!);
        fileSystem.WriteAllText(
            service.Paths.NpmCiTemporaryNpmrcPath,
            "//registry/:_authToken=secret"
        );

        ConfigurationPhase14CleanupResult result = await service.CleanupAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult cleanupResult = Assert.Single(
            result.Ecosystems
        );
        Assert.Equal("removed", cleanupResult.State);
        Assert.Equal(0, cleanupResult.ChangeCount);
        Assert.False(cleanupResult.TemporaryContainerPresent);
        Assert.False(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
    }

    [Fact]
    public async Task CleanupCiTemporaryRemovesCompleteOwnedManifestOnlyState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        string manifestPath = Path.Combine(
            service.Paths.CiTemporaryManifestDirectoryPath,
            "npm-ci-temporary-ownership-manifest.json"
        );
        fileSystem.DeleteFile(service.Paths.NpmCiTemporaryNpmrcPath);

        ConfigurationPhase14CleanupResult result = await service.CleanupAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult cleanupResult = Assert.Single(
            result.Ecosystems
        );
        Assert.Equal("removed", cleanupResult.State);
        Assert.False(cleanupResult.OwnershipManifestPresent);
        Assert.False(cleanupResult.TemporaryContainerPresent);
        Assert.False(fileSystem.FileExists(manifestPath));
    }

    [Fact]
    public async Task CleanupCiTemporaryPreservesMismatchedManifestOnlyStateAsIncomplete()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        string npmManifestPath = Path.Combine(
            service.Paths.CiTemporaryManifestDirectoryPath,
            "npm-ci-temporary-ownership-manifest.json"
        );
        string pnpmManifestPath = Path.Combine(
            service.Paths.CiTemporaryManifestDirectoryPath,
            "pnpm-ci-temporary-ownership-manifest.json"
        );
        string manifestJson = fileSystem.ReadAllText(npmManifestPath);
        fileSystem.DeleteFile(service.Paths.NpmCiTemporaryNpmrcPath);
        fileSystem.DeleteFile(npmManifestPath);
        fileSystem.WriteAllText(pnpmManifestPath, manifestJson);

        ConfigurationPhase14CleanupResult result = await service.CleanupAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult cleanupResult = Assert.Single(
            result.Ecosystems
        );
        Assert.Equal("incomplete", cleanupResult.State);
        Assert.True(cleanupResult.OwnershipManifestPresent);
        Assert.False(cleanupResult.TemporaryContainerPresent);
        Assert.True(fileSystem.FileExists(pnpmManifestPath));
    }

    [Fact]
    public async Task CleanupCiTemporaryRemovesYarnHomeWithCiArtifacts()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);
        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        string ciArtifactDirectory = Path.Combine(
            service.Paths.YarnCiTemporaryHomePath,
            "ci-artifacts"
        );
        fileSystem.CreateDirectory(ciArtifactDirectory);
        fileSystem.WriteAllText(Path.Combine(ciArtifactDirectory, "metadata.txt"), "ci");

        ConfigurationPhase14CleanupResult result = await service.CleanupAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult cleanupResult = Assert.Single(
            result.Ecosystems
        );
        Assert.Equal("removed", cleanupResult.State);
        Assert.True(cleanupResult.ChangeCount > 0);
        Assert.False(cleanupResult.TemporaryContainerPresent);
        Assert.False(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../unsafe")]
    public void DryRunValidationRejectsMissingOrInvalidCiJobScope(string? jobScopeId)
    {
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = new InMemoryFileSystem(),
                StateDirectoryPath = "/state/phase14-dry-run",
                AzurePipelinesJobScopeId = jobScopeId,
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(
                    new SilentTestAcquisitionService()),
                EnvironmentVariableReader = _ => null,
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = new(
                        "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"),
                },
            });

        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateConfigureRequest(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.CiTemporary));
        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateUnconfigureRequest(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.CiTemporary));
    }

    [Fact]
    public void DryRunValidationRejectsUnsupportedScopeAndMissingRegistry()
    {
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = new InMemoryFileSystem(),
                StateDirectoryPath = "/state/phase14-dry-run",
                AzurePipelinesJobScopeId = "job-1",
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(
                    new SilentTestAcquisitionService()),
                EnvironmentVariableReader = _ => null,
            });

        Assert.Throws<NotSupportedException>(() =>
            service.ValidateConfigureRequest(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.CiTemporary));
        Assert.Throws<NotSupportedException>(() =>
            service.ValidateUnconfigureRequest(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.CiTemporary));
        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateConfigureRequest(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.User));
    }

    private static ConfigurationPhase14VerticalSliceService CreateService(
        InMemoryFileSystem fileSystem,
        IIdentityProvider? identityProvider = null,
        Func<string, string?>? environmentVariableReader = null
    ) =>
        new(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/phase14",
                AzurePipelinesJobScopeId = "phase14-test-job",
                CredentialAcquisition = identityProvider is null
                    ? new BoundedCredentialAcquisitionAdapter(
                        new SilentTestAcquisitionService())
                    : null,
                CredentialCoreService = identityProvider is null
                    ? null
                    : new CredentialCoreService(identityProvider),
                EnvironmentVariableReader = environmentVariableReader ?? (_ => null),
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = new(
                        "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"),
                    [CredentialEcosystem.Pnpm] = new(
                        "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"),
                    [CredentialEcosystem.Yarn] = new(
                        "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"),
                },
            }
        );

    private static string? ReadCiEnvironment(string name) =>
        string.Equals(name, "SYSTEM_ACCESSTOKEN", StringComparison.Ordinal)
            ? "system-token"
            : null;

    private static string CreateKnownCiContainer(
        InMemoryFileSystem fileSystem,
        ConfigurationPhase14ResolvedPaths paths,
        CredentialEcosystem ecosystem)
    {
        string path = ecosystem switch
        {
            CredentialEcosystem.Npm => paths.NpmCiTemporaryNpmrcPath,
            CredentialEcosystem.Pnpm => paths.PnpmCiTemporaryNpmrcPath,
            CredentialEcosystem.Yarn => paths.YarnCiTemporaryHomePath,
            _ => throw new ArgumentOutOfRangeException(nameof(ecosystem)),
        };
        if (ecosystem == CredentialEcosystem.Yarn)
        {
            fileSystem.CreateDirectory(path);
            fileSystem.WriteAllText(Path.Combine(path, ".yarnrc.yml"), "owned");
        }
        else
        {
            fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
            fileSystem.WriteAllText(path, "owned");
        }

        return path;
    }

    private static bool KnownCiContainerExists(
        InMemoryFileSystem fileSystem,
        string path,
        CredentialEcosystem ecosystem) =>
        ecosystem == CredentialEcosystem.Yarn
            ? fileSystem.DirectoryExists(path)
            : fileSystem.FileExists(path);

    private sealed class CapturingIdentityProvider : IIdentityProvider
    {
        public List<CredentialRequest> Requests { get; } = [];

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            Requests.Add(request);
            return new IdentityMaterial
            {
                Account = "account@example.test",
                Tenant = "tenant",
                AccessToken = "identity-token",
                ExpiresAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            };
        }
    }

    private sealed class SilentTestAcquisitionService : ICredentialAcquisitionService
    {
        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(AcquisitionMode.SilentOnly, request.AcquisitionMode);
            Assert.Equal(InteractivePolicy.Never, request.InteractivePolicy);
            Assert.True(
                CredentialRequestV2Policy.IsValid(request),
                CredentialRequestV2Policy.GetViolation(request));
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    BearerToken = "fake-token-silent",
                    DiagnosticsCorrelationId = "phase14-silent-test",
                });
        }
    }
}
