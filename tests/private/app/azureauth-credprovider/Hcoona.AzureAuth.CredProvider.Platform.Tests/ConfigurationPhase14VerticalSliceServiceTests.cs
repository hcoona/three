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
    public async Task ConfigureNpmCiTemporaryUsesAzurePipelinesCredentialRequest()
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

        CredentialRequest request = Assert.Single(identityProvider.Requests);
        Assert.Equal(ConfigurationPlanState.Applied, result.PlanResult.State);
        Assert.Equal(IdentityFlow.AzurePipelinesSystemAccessToken, request.IdentityFlow);
        Assert.Equal(InteractivePolicy.Never, request.InteractivePolicy);
        Assert.Equal(CachePolicyMode.NonPersistentCi, request.CachePolicy);
        CiContext ciContext = Assert.IsType<CiContext>(request.CiContext);
        Assert.Equal(CiProviderNames.AzurePipelines, ciContext.Provider);
        Assert.True(ciContext.ExplicitCiMode);
        Assert.True(ciContext.HasAzurePipelinesSystemAccessToken);
        Assert.False(ciContext.AllowsPersistentWrites);
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
