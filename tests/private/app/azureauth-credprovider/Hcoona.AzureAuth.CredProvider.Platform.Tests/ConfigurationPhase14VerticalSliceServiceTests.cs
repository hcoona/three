using Hcoona.AzureAuth.CredProvider.Contracts;
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
                CredentialCoreService = identityProvider is null
                    ? null
                    : new CredentialCoreService(identityProvider),
                EnvironmentVariableReader = environmentVariableReader ?? (_ => null),
            }
        );

    private static string? ReadCiEnvironment(string name) =>
        string.Equals(name, "SYSTEM_ACCESSTOKEN", StringComparison.Ordinal)
            ? "system-token"
            : null;

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
}
