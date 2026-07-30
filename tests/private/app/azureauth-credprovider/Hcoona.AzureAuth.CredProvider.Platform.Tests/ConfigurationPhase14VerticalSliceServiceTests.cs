using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class ConfigurationPhase14VerticalSliceServiceTests
{
    private const string TestRegistryUrl =
        "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/";

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
            }
        );
        ConfigurationPhase14VerticalSliceService service = root.CreateConfigurationService();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    CredentialEcosystem.Npm,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Equal(
            "Package registry configuration is required. Run azureauth-credprovider configure "
                + "npm --registry-url <azure-artifacts-npm-url>.",
            exception.Message
        );
        Assert.False(fileSystem.FileExists(service.Paths.NpmUserNpmrcPath));
        Assert.False(fileSystem.DirectoryExists(service.Paths.ManifestDirectoryPath));
    }

    [Fact]
    public async Task ExplicitRegistryDeclarationRoundTripsWithoutSyntheticTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        Uri registryUrl = new(
            "https://pkgs.dev.azure.com/real-org/real-project/"
                + "_packaging/real-feed/npm/registry/"
        );
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/explicit",
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(
                    new SilentTestAcquisitionService()
                ),
                EnvironmentVariableReader = _ => null,
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = registryUrl,
                },
            }
        );

        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        string configured = fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath);
        Assert.Contains(
            "//pkgs.dev.azure.com/real-org/real-project/_packaging/real-feed/npm/registry/",
            configured,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "pkgs.dev.azure.com/org/_packaging/feed",
            configured,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("/home/shared/.npmrc", "/home/shared/.npmrc", "/home/shared/.npmrc")]
    [InlineData("/home/upper/.npmrc", null, "/home/upper/.npmrc")]
    [InlineData(null, "/home/lower/.npmrc", "/home/lower/.npmrc")]
    public void NpmAndPnpmResolveConsistentUserConfigOverrides(
        string? uppercase,
        string? lowercase,
        string expected
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name switch
                {
                    "NPM_CONFIG_USERCONFIG" => uppercase,
                    "npm_config_userconfig" => lowercase,
                    _ => null,
                }
        );

        Assert.Equal(expected, service.Paths.NpmUserNpmrcPath);
        Assert.Equal(expected, service.Paths.PnpmUserNpmrcPath);
    }

    [Fact]
    public void ConflictingNpmUserConfigOverridesFailClearly()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            CreateService(
                fileSystem,
                environmentVariableReader: name =>
                    name switch
                    {
                        "NPM_CONFIG_USERCONFIG" => "/home/upper/.npmrc",
                        "npm_config_userconfig" => "/home/lower/.npmrc",
                        _ => null,
                    }
            )
        );

        Assert.Contains("resolve to different", exception.Message, StringComparison.Ordinal);
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
        Assert.Contains("_authToken=fake-token-", configuredNpmrc, StringComparison.Ordinal);
        Assert.Equal(ConfigurationPlanState.Applied, unconfigureResult.PlanResult.State);
        Assert.Equal(1, unconfigureResult.ChangeCount);
        Assert.DoesNotContain(
            "fake-token-",
            fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    public async Task UnconfigurePreservesBenignNpmrcEdits(CredentialEcosystem ecosystem)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        fileSystem.WriteAllText(
            service.Paths.NpmUserNpmrcPath,
            fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath) + "fund=false\n"
        );

        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        string remaining = fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath);
        Assert.False(result.OwnershipManifestCleanupIncomplete);
        Assert.False(result.OwnershipManifestPresent);
        Assert.Contains("fund=false", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("_authToken", remaining, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PnpmUnconfigureUsesSharedNpmOwnershipAfterBenignEdit()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        fileSystem.WriteAllText(
            service.Paths.NpmUserNpmrcPath,
            fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath) + "audit=false\n"
        );

        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        string remaining = fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath);
        Assert.False(result.OwnershipManifestCleanupIncomplete);
        Assert.Contains("audit=false", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("_authToken", remaining, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LogoutPreservesBenignYarnrcEdits()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        fileSystem.WriteAllText(
            service.Paths.YarnUserYarnrcPath,
            fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath) + "enableTelemetry: false\n"
        );

        ConfigurationPhase14CleanupResult result = await service.LogoutAsync(
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult yarn = Assert.Single(
            result.Ecosystems,
            item =>
                item.Ecosystem == CredentialEcosystem.Yarn
                && item.Scope == ConfigurationPhase14Scope.User
        );
        string remaining = fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath);
        Assert.Equal("removed", yarn.State);
        Assert.Contains("enableTelemetry: false", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("npmAuthToken", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("npmAlwaysAuth", remaining, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathChangePreservesBenignNpmrcEdits()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService original = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name == "NPM_CONFIG_USERCONFIG" ? "/home/old/.npmrc" : null
        );
        await original.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        fileSystem.WriteAllText(
            original.Paths.NpmUserNpmrcPath,
            fileSystem.ReadAllText(original.Paths.NpmUserNpmrcPath) + "legacy-peer-deps=true\n"
        );
        ConfigurationPhase14VerticalSliceService migrated = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name == "NPM_CONFIG_USERCONFIG" ? "/home/new/.npmrc" : null
        );

        await migrated.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        string oldContents = fileSystem.ReadAllText(original.Paths.NpmUserNpmrcPath);
        Assert.Contains("legacy-peer-deps=true", oldContents, StringComparison.Ordinal);
        Assert.DoesNotContain("_authToken", oldContents, StringComparison.Ordinal);
        Assert.Contains(
            "_authToken",
            fileSystem.ReadAllText(migrated.Paths.NpmUserNpmrcPath),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task ReplacementDestinationConflictPreservesOldConfigurationAndManifest(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService original = CreateService(
            fileSystem,
            environmentVariableReader: CreatePackagePathEnvironmentReader(ecosystem, "old")
        );
        await original.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string oldPath = GetPackageConfigurationPath(original, ecosystem);
        string oldContents = fileSystem.ReadAllText(oldPath);
        string manifestPath = GetPackageManifestPath(original, ecosystem);
        string manifestContents = fileSystem.ReadAllText(manifestPath);
        ConfigurationPhase14VerticalSliceService replacement = CreateService(
            fileSystem,
            environmentVariableReader: CreatePackagePathEnvironmentReader(ecosystem, "new")
        );
        string destinationPath = GetPackageConfigurationPath(replacement, ecosystem);
        fileSystem.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        fileSystem.WriteAllText(destinationPath, oldContents);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await replacement.ConfigureAsync(
                    ecosystem,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains("Configuration conflict", exception.Message, StringComparison.Ordinal);
        Assert.Equal(oldContents, fileSystem.ReadAllText(oldPath));
        Assert.Equal(manifestContents, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(oldContents, fileSystem.ReadAllText(destinationPath));
        Assert.DoesNotContain(
            fileSystem.Files.Keys,
            path => path.Contains(".replacement-preview", StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task CancellationAfterReplacementRemovalStartsDoesNotInterruptCommit(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService original = CreateService(
            fileSystem,
            environmentVariableReader: CreatePackagePathEnvironmentReader(ecosystem, "old")
        );
        await original.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string oldPath = GetPackageConfigurationPath(original, ecosystem);
        ConfigurationPhase14VerticalSliceService replacement = CreateService(
            fileSystem,
            environmentVariableReader: CreatePackagePathEnvironmentReader(ecosystem, "new")
        );
        string destinationPath = GetPackageConfigurationPath(replacement, ecosystem);
        using var cancellation = new CancellationTokenSource();
        fileSystem.AfterRecord = (call, _) =>
        {
            if (
                call.Operation == nameof(IFileSystem.AtomicWriteAllText)
                && string.Equals(call.Path, oldPath, StringComparison.Ordinal)
            )
            {
                cancellation.Cancel();
            }
        };

        await replacement.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            cancellation.Token
        );

        Assert.True(cancellation.IsCancellationRequested);
        Assert.DoesNotContain(
            ecosystem == CredentialEcosystem.Yarn ? "npmAuthToken" : "_authToken",
            fileSystem.ReadAllText(oldPath),
            StringComparison.Ordinal
        );
        Assert.Contains(
            ecosystem == CredentialEcosystem.Yarn ? "npmAuthToken" : "_authToken",
            fileSystem.ReadAllText(destinationPath),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task RegistryResourceReplacementSucceeds(CredentialEcosystem ecosystem)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService original = CreateService(fileSystem);
        await original.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        var replacementRegistry = new Uri(
            "https://pkgs.dev.azure.com/test-org/_packaging/replacement-feed/npm/registry/"
        );
        ConfigurationPhase14VerticalSliceService replacement = CreateService(
            fileSystem,
            registryUrl: replacementRegistry
        );

        await replacement.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        string contents = fileSystem.ReadAllText(
            GetPackageConfigurationPath(replacement, ecosystem)
        );
        Assert.Contains("replacement-feed", contents, StringComparison.Ordinal);
        Assert.DoesNotContain("test-feed", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnconfigureRemovesOwnedSelectorWhenItsValueChanged()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string configured = fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath);
        string tokenLine = Assert.Single(
            configured.Split('\n'),
            static line => line.Contains(":_authToken=", StringComparison.Ordinal)
        );
        string changed = configured.Replace(
            tokenLine,
            tokenLine[..(tokenLine.IndexOf('=') + 1)] + "changed-token",
            StringComparison.Ordinal
        );
        fileSystem.WriteAllText(service.Paths.NpmUserNpmrcPath, changed);

        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.OwnershipManifestCleanupIncomplete);
        Assert.False(result.OwnershipManifestPresent);
        Assert.DoesNotContain("_authToken", fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath));
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task FreshConfigureDoesNotInspectSecretAndRefreshReplacesIt(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string configurationPath =
            ecosystem == CredentialEcosystem.Yarn
                ? service.Paths.YarnUserYarnrcPath
                : service.Paths.NpmUserNpmrcPath;
        string configured = fileSystem.ReadAllText(configurationPath);
        string changed =
            ecosystem == CredentialEcosystem.Yarn
                ? configured.Replace(
                    "npmAuthToken: 'fake-token-silent'",
                    "npmAuthToken: 'user-modified-token'",
                    StringComparison.Ordinal
                )
                : configured.Replace(
                    "_authToken=fake-token-silent",
                    "_authToken=user-modified-token",
                    StringComparison.Ordinal
                );
        Assert.NotEqual(configured, changed);
        fileSystem.WriteAllText(configurationPath, changed);

        ConfigurationPhase14PlanResult noOp = await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, noOp.AppliedChangeCount);
        Assert.Equal(changed, fileSystem.ReadAllText(configurationPath));

        ConfigurationPhase14PlanResult refreshed = await service.RefreshAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(ConfigurationPlanState.Applied, refreshed.PlanResult.State);
        Assert.Equal(configured, fileSystem.ReadAllText(configurationPath));
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task RepeatedFreshConfigureIsNoOpAndUnchangedRefreshSucceeds(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string configurationPath =
            ecosystem == CredentialEcosystem.Yarn
                ? service.Paths.YarnUserYarnrcPath
                : service.Paths.NpmUserNpmrcPath;
        string configured = fileSystem.ReadAllText(configurationPath);

        ConfigurationPhase14PlanResult repeated = await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult refreshed = await service.RefreshAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, repeated.AppliedChangeCount);
        Assert.Equal(ConfigurationPlanState.Applied, refreshed.PlanResult.State);
        Assert.Equal(configured, fileSystem.ReadAllText(configurationPath));
    }

    [Fact]
    public async Task NpmConfigureWithRemovedOwnedAuthSelectorSurfacesConflict()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string configured = fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath);
        string authLine = Assert.Single(
            configured.Split('\n'),
            static line => line.Contains(":_authToken=", StringComparison.Ordinal)
        );
        string withoutAuthSelector = configured.Replace(
            authLine + "\n",
            string.Empty,
            StringComparison.Ordinal
        );
        fileSystem.WriteAllText(service.Paths.NpmUserNpmrcPath, withoutAuthSelector);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    CredentialEcosystem.Npm,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains(
            "Configuration conflict: Npmrc retained ownership proof does not match any existing file",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(withoutAuthSelector, fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath));
    }

    [Fact]
    public async Task YarnConfigureWithRemovedOwnedAuthSelectorSurfacesConflict()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string configured = fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath);
        string authLine = Assert.Single(
            configured.Split('\n'),
            static line => line.Contains("npmAuthToken:", StringComparison.Ordinal)
        );
        string withoutAuthSelector = configured.Replace(
            authLine + "\n",
            string.Empty,
            StringComparison.Ordinal
        );
        fileSystem.WriteAllText(service.Paths.YarnUserYarnrcPath, withoutAuthSelector);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    CredentialEcosystem.Yarn,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains(
            "Configuration conflict: Yarnrc retained ownership proof does not match any existing file",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(withoutAuthSelector, fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath));
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
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult executed = await executionService.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(executed.ChangeCount, dryRun.ChangeCount);
        Assert.Equal(
            executed.PlanResults.Select(static result =>
                (
                    result.Plan.PlanId,
                    result.Plan.ChangeSetId,
                    result.Plan.Scope,
                    result.Plan.Manifest.PreviousOwnedEntryHash
                )
            ),
            dryRun.PlanResults.Select(static result =>
                (
                    result.Plan.PlanId,
                    result.Plan.ChangeSetId,
                    result.Plan.Scope,
                    result.Plan.Manifest.PreviousOwnedEntryHash
                )
            )
        );
        Assert.Equal(
            executed.PlanResults.SelectMany(static result => result.Changes),
            dryRun.PlanResults.SelectMany(static result => result.Changes)
        );
        Assert.All(
            dryRun.PlanResults,
            static result => Assert.Equal(ConfigurationPlanState.Planned, result.State)
        );
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
            TestContext.Current.CancellationToken
        );
        await executionService.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14PlanResult dryRun = await dryRunService.DryRunUnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult executed = await executionService.UnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(executed.ChangeCount, dryRun.ChangeCount);
        Assert.Equal(
            executed.PlanResults.Select(static result =>
                (
                    result.Plan.PlanId,
                    result.Plan.ChangeSetId,
                    result.Plan.Scope,
                    result.Plan.Manifest.PreviousOwnedEntryHash
                )
            ),
            dryRun.PlanResults.Select(static result =>
                (
                    result.Plan.PlanId,
                    result.Plan.ChangeSetId,
                    result.Plan.Scope,
                    result.Plan.Manifest.PreviousOwnedEntryHash
                )
            )
        );
        Assert.Equal(
            executed.PlanResults.SelectMany(static result => result.Changes),
            dryRun.PlanResults.SelectMany(static result => result.Changes)
        );
        Assert.True(dryRun.OwnershipManifestPresent);
        Assert.False(executed.OwnershipManifestPresent);
    }

    [Fact]
    public async Task UnconfigurePreservesMalformedOwnershipManifestAndReportsIncomplete()
    {
        var dryRunFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var executionFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var dryRunService = CreateService(dryRunFileSystem);
        var executionService = CreateService(executionFileSystem);
        string dryRunManifestPath = Path.Combine(
            dryRunService.Paths.ManifestDirectoryPath,
            "python-user-ownership-manifest.json"
        );
        string executionManifestPath = Path.Combine(
            executionService.Paths.ManifestDirectoryPath,
            "python-user-ownership-manifest.json"
        );
        dryRunFileSystem.CreateDirectory(dryRunService.Paths.ManifestDirectoryPath);
        executionFileSystem.CreateDirectory(executionService.Paths.ManifestDirectoryPath);
        dryRunFileSystem.WriteAllText(dryRunManifestPath, """{"malformed":true}""");
        executionFileSystem.WriteAllText(executionManifestPath, """{"malformed":true}""");

        ConfigurationPhase14PlanResult dryRun = await dryRunService.DryRunUnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult executed = await executionService.UnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.True(dryRun.OwnershipManifestCleanupIncomplete);
        Assert.True(executed.OwnershipManifestCleanupIncomplete);
        Assert.Equal("""{"malformed":true}""", dryRunFileSystem.ReadAllText(dryRunManifestPath));
        Assert.Equal(
            """{"malformed":true}""",
            executionFileSystem.ReadAllText(executionManifestPath)
        );
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task CiUnconfigureLeavesMalformedManifestAndContainerUntouched(
        CredentialEcosystem ecosystem
    )
    {
        const string MalformedManifest = """{"malformed":true}""";
        var dryRunFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var executionFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var dryRunService = CreateService(dryRunFileSystem);
        var executionService = CreateService(executionFileSystem);
        string manifestName =
            ecosystem.ToString().ToLowerInvariant() + "-ci-temporary-ownership-manifest.json";
        if (ecosystem is CredentialEcosystem.Npm or CredentialEcosystem.Pnpm)
        {
            manifestName = "npm-compatible-ci-temporary-ownership-manifest.json";
        }
        string dryRunManifestPath = Path.Combine(
            dryRunService.Paths.CiTemporaryManifestDirectoryPath,
            manifestName
        );
        string executionManifestPath = Path.Combine(
            executionService.Paths.CiTemporaryManifestDirectoryPath,
            manifestName
        );
        string dryRunContainerPath = CreateKnownCiContainer(
            dryRunFileSystem,
            dryRunService.Paths,
            ecosystem
        );
        string executionContainerPath = CreateKnownCiContainer(
            executionFileSystem,
            executionService.Paths,
            ecosystem
        );
        dryRunFileSystem.CreateDirectory(dryRunService.Paths.CiTemporaryManifestDirectoryPath);
        executionFileSystem.CreateDirectory(
            executionService.Paths.CiTemporaryManifestDirectoryPath
        );
        dryRunFileSystem.WriteAllText(dryRunManifestPath, MalformedManifest);
        executionFileSystem.WriteAllText(executionManifestPath, MalformedManifest);

        ConfigurationPhase14PlanResult dryRun = await dryRunService.DryRunUnconfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult executed = await executionService.UnconfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        Assert.True(dryRun.OwnershipManifestCleanupIncomplete);
        Assert.True(executed.OwnershipManifestCleanupIncomplete);
        Assert.Equal(ConfigurationPlanOperation.DryRun, dryRun.PlanResult.Operation);
        Assert.Equal(ConfigurationPlanOperation.Remove, executed.PlanResult.Operation);
        Assert.Equal(ConfigurationPlanState.Planned, dryRun.PlanResult.State);
        Assert.Equal(ConfigurationPlanState.Applied, executed.PlanResult.State);
        Assert.Empty(dryRun.PlanResult.Changes);
        Assert.Empty(executed.PlanResult.Changes);
        Assert.Equal(0, dryRun.ChangeCount);
        Assert.Equal(0, dryRun.AppliedChangeCount);
        Assert.Equal(0, executed.AppliedChangeCount);
        Assert.True(KnownCiContainerExists(dryRunFileSystem, dryRunContainerPath, ecosystem));
        Assert.True(KnownCiContainerExists(executionFileSystem, executionContainerPath, ecosystem));
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
    public async Task ConfigureUsesEffectiveSharedNpmConfigAndIndependentYarnConfig()
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
        await service.UnconfigureAsync(
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
        Assert.Equal(service.Paths.NpmUserNpmrcPath, service.Paths.PnpmUserNpmrcPath);
        Assert.All(
            new[] { pythonResult, pnpmResult, yarnResult },
            result => Assert.True(result.OwnershipManifestPresent)
        );
    }

    [Fact]
    public async Task ConfigureNpmCiTemporaryRequiresAzurePipelinesSystemAccessToken()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
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
            name =>
                string.Equals(name, "SYSTEM_ACCESSTOKEN", StringComparison.Ordinal)
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

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    public async Task CleanupCiTemporaryPreservesUnrelatedNpmrcContent(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);
        await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        string path = service.Paths.NpmCiTemporaryNpmrcPath;
        fileSystem.WriteAllText(path, fileSystem.ReadAllText(path) + "legacy-peer-deps=true\n");

        ConfigurationPhase14CleanupResult result = await service.CleanupAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult cleanup = Assert.Single(result.Ecosystems);
        Assert.Equal("incomplete", cleanup.State);
        Assert.False(cleanup.OwnershipManifestPresent);
        Assert.True(cleanup.TemporaryContainerPresent);
        string remaining = fileSystem.ReadAllText(path);
        Assert.Contains("legacy-peer-deps=true", remaining, StringComparison.Ordinal);
        Assert.DoesNotContain("_authToken", remaining, StringComparison.Ordinal);
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
            ecosystemResult =>
                ecosystemResult.Ecosystem == CredentialEcosystem.Yarn
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
        Assert.Equal(5, result.ChangeCount);
        Assert.Equal(
            ["removed", "not-needed", "removed"],
            result.Ecosystems.Select(static cleanupResult => cleanupResult.State)
        );
        Assert.All(
            result.Ecosystems,
            cleanupResult =>
            {
                Assert.False(cleanupResult.OwnershipManifestPresent);
                Assert.False(cleanupResult.TemporaryContainerPresent);
            }
        );
        Assert.False(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
        Assert.False(fileSystem.FileExists(service.Paths.PnpmCiTemporaryNpmrcPath));
        Assert.False(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));
    }

    [Fact]
    public async Task CleanupDryRunInspectsRealStateAndMatchesExecutionWithoutMutation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: ReadCiEnvironment
        );
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        string manifestPath = Path.Combine(
            service.Paths.CiTemporaryManifestDirectoryPath,
            "npm-compatible-ci-temporary-ownership-manifest.json"
        );
        string npmrcBefore = fileSystem.ReadAllText(service.Paths.NpmCiTemporaryNpmrcPath);
        string manifestBefore = fileSystem.ReadAllText(manifestPath);

        ConfigurationPhase14CleanupResult dryRun = await service.DryRunCleanupAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult planned = Assert.Single(dryRun.Ecosystems);
        Assert.Equal("removed", planned.State);
        Assert.True(planned.ChangeCount > 0);
        Assert.Equal(0, planned.AppliedChangeCount);
        Assert.Equal(npmrcBefore, fileSystem.ReadAllText(service.Paths.NpmCiTemporaryNpmrcPath));
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));

        ConfigurationPhase14CleanupResult executed = await service.CleanupAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(planned.State, Assert.Single(executed.Ecosystems).State);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.DryRunCleanupAsync(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.CiTemporary,
                cancellation.Token
            )
        );
    }

    [Fact]
    public async Task CleanupAllDryRunAndExecutionProcessSharedNpmStateOnce()
    {
        var dryRunFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var executionFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService dryRunService = CreateService(
            dryRunFileSystem,
            environmentVariableReader: ReadCiEnvironment
        );
        ConfigurationPhase14VerticalSliceService executionService = CreateService(
            executionFileSystem,
            environmentVariableReader: ReadCiEnvironment
        );
        await dryRunService.ConfigureAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        await executionService.ConfigureAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupResult dryRun = await dryRunService.DryRunCleanupAsync(
            ecosystem: null,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14CleanupResult executed = await executionService.CleanupAsync(
            ecosystem: null,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            dryRun.Ecosystems.Select(static result =>
                (result.Ecosystem, result.State, result.ChangeCount)
            ),
            executed.Ecosystems.Select(static result =>
                (result.Ecosystem, result.State, result.ChangeCount)
            )
        );
        Assert.Equal(
            [
                (CredentialEcosystem.Npm, "removed", 2),
                (CredentialEcosystem.Pnpm, "not-needed", 0),
                (CredentialEcosystem.Yarn, "not-needed", 0),
            ],
            dryRun.Ecosystems.Select(static result =>
                (result.Ecosystem, result.State, result.ChangeCount)
            )
        );
        Assert.True(dryRunFileSystem.FileExists(dryRunService.Paths.NpmCiTemporaryNpmrcPath));
        Assert.False(
            executionFileSystem.FileExists(executionService.Paths.NpmCiTemporaryNpmrcPath)
        );
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"not":"a manifest"}""")]
    public async Task YarnMalformedManifestIsReportedInvalidAndCleanupIncomplete(
        string malformedManifest
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        string manifestPath = Path.Combine(
            service.Paths.ManifestDirectoryPath,
            "yarn-user-ownership-manifest.json"
        );
        fileSystem.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        fileSystem.WriteAllText(manifestPath, malformedManifest);

        ConfigurationPhase14DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14EcosystemDoctorResult yarn = Assert.Single(
            doctor.Ecosystems,
            result =>
                result.Ecosystem == CredentialEcosystem.Yarn
                && result.Scope == ConfigurationPhase14Scope.User
        );
        Assert.False(yarn.ConfigurationPlanValid);
        Assert.Equal(RegistryCredentialLifecycleState.Invalid, yarn.LifecycleState);

        ConfigurationPhase14PlanResult removed = await service.UnconfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.True(removed.OwnershipManifestCleanupIncomplete);
        Assert.Equal(malformedManifest, fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task CleanupCiTemporaryRemovesEmptyKnownTemporaryContainer()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);
        fileSystem.CreateDirectory(Path.GetDirectoryName(service.Paths.NpmCiTemporaryNpmrcPath)!);
        fileSystem.WriteAllText(service.Paths.NpmCiTemporaryNpmrcPath, string.Empty);

        ConfigurationPhase14CleanupResult result = await service.CleanupAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult cleanupResult = Assert.Single(result.Ecosystems);
        Assert.Equal("removed", cleanupResult.State);
        Assert.Equal(0, cleanupResult.ChangeCount);
        Assert.False(cleanupResult.TemporaryContainerPresent);
        Assert.False(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
    }

    [Fact]
    public async Task CleanupCiTemporaryPreservesNonemptyContainerWithoutOwnership()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);
        fileSystem.CreateDirectory(Path.GetDirectoryName(service.Paths.NpmCiTemporaryNpmrcPath)!);
        fileSystem.WriteAllText(
            service.Paths.NpmCiTemporaryNpmrcPath,
            "//registry/:_authToken=unknown"
        );

        ConfigurationPhase14CleanupResult result = await service.CleanupAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14CleanupEcosystemResult cleanupResult = Assert.Single(result.Ecosystems);
        Assert.Equal("incomplete", cleanupResult.State);
        Assert.True(cleanupResult.TemporaryContainerPresent);
        Assert.True(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task RepeatedFreshCiConfigureReturnsSamePackageActivationAndMetadata(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem, environmentVariableReader: ReadCiEnvironment);

        ConfigurationPhase14PlanResult initial = await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        string manifestPath =
            ecosystem == CredentialEcosystem.Yarn
                ? Path.Combine(
                    service.Paths.CiTemporaryManifestDirectoryPath,
                    "yarn-ci-temporary-ownership-manifest.json"
                )
                : Path.Combine(
                    service.Paths.CiTemporaryManifestDirectoryPath,
                    "npm-compatible-ci-temporary-ownership-manifest.json"
                );
        string manifestBefore = fileSystem.ReadAllText(manifestPath);
        ConfigurationOwnershipManifest persisted =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestBefore);
        string configurationPath =
            ecosystem == CredentialEcosystem.Yarn
                ? Path.Combine(service.Paths.YarnCiTemporaryHomePath, ".yarnrc.yml")
                : service.Paths.NpmCiTemporaryNpmrcPath;
        string configurationBefore = fileSystem.ReadAllText(configurationPath);
        ConfigurationPhase14PlanResult repeated = await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationTemporaryContainer initialContainer =
            Assert.IsType<ConfigurationTemporaryContainer>(
                initial.PlanResult.Plan.TemporaryContainer
            );
        ConfigurationTemporaryContainer repeatedContainer =
            Assert.IsType<ConfigurationTemporaryContainer>(
                repeated.PlanResult.Plan.TemporaryContainer
            );
        Assert.Equal(0, repeated.AppliedChangeCount);
        Assert.Equal(initialContainer.Kind, repeatedContainer.Kind);
        Assert.Equal(initialContainer.ProductOwnedPath, repeatedContainer.ProductOwnedPath);
        Assert.Equal(
            initialContainer.ActivationEnvironment!.Platform,
            repeatedContainer.ActivationEnvironment!.Platform
        );
        Assert.Equal(
            initialContainer.ActivationEnvironment.SetVariables,
            repeatedContainer.ActivationEnvironment!.SetVariables
        );
        Assert.Equal(
            initialContainer.ActivationEnvironment.ClearVariables,
            repeatedContainer.ActivationEnvironment.ClearVariables
        );
        Assert.Equal(
            initial.PlanResult.Plan.Manifest.ResourceIdentity,
            repeated.PlanResult.Plan.Manifest.ResourceIdentity
        );
        Assert.Equal(
            initial.PlanResult.Plan.Manifest.EntrySelector,
            repeated.PlanResult.Plan.Manifest.EntrySelector
        );
        Assert.Equal(persisted.SafeMetadata, repeated.PlanResult.Plan.Manifest.SafeMetadata);
        Assert.Equal(manifestBefore, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(configurationBefore, fileSystem.ReadAllText(configurationPath));
        Assert.Contains(TestRegistryUrl, configurationBefore, StringComparison.Ordinal);
        Assert.Contains("system-token", configurationBefore, StringComparison.Ordinal);
        if (ecosystem == CredentialEcosystem.Yarn)
        {
            Assert.Contains(
                "npmRegistryServer: '" + TestRegistryUrl + "'",
                configurationBefore,
                StringComparison.Ordinal
            );
            Assert.Equal(
                service.Paths.YarnCiTemporaryHomePath,
                initialContainer.ActivationEnvironment.SetVariables["HOME"]
            );
        }
        else
        {
            Assert.Contains(
                "registry=" + TestRegistryUrl,
                configurationBefore,
                StringComparison.Ordinal
            );
            Assert.Equal(
                service.Paths.NpmCiTemporaryNpmrcPath,
                initialContainer.ActivationEnvironment.SetVariables["NPM_CONFIG_USERCONFIG"]
            );
            Assert.Equal(
                service.Paths.NpmCiTemporaryNpmrcPath,
                initialContainer.ActivationEnvironment.SetVariables["npm_config_userconfig"]
            );
        }

        Assert.NotEqual("phase14-python-keyring", repeated.PlanResult.Plan.Manifest.ManifestId);
        Assert.DoesNotContain(
            repeated.PlanResult.Plan.Manifest.SafeMetadata,
            pair => pair.Value == "python"
        );
    }

    [Fact]
    public async Task YarnCiDryRunAndApplyPlansIncludeRegistryRouting()
    {
        var dryRunFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var applyFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService dryRunService = CreateService(
            dryRunFileSystem,
            environmentVariableReader: ReadCiEnvironment
        );
        ConfigurationPhase14VerticalSliceService applyService = CreateService(
            applyFileSystem,
            environmentVariableReader: ReadCiEnvironment
        );

        ConfigurationPhase14PlanResult dryRun = await dryRunService.DryRunConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult applied = await applyService.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, dryRun.ChangeCount);
        Assert.Equal(dryRun.ChangeCount, applied.ChangeCount);
        Assert.Equal(
            dryRun.PlanResult.Plan.Changes.Select(static change => change.Key),
            applied.PlanResult.Plan.Changes.Select(static change => change.Key)
        );
        Assert.Contains(
            dryRun.PlanResult.Plan.Changes,
            change => string.Equals(change.Key, "npmRegistryServer", StringComparison.Ordinal)
        );
        Assert.False(
            dryRunFileSystem.FileExists(
                Path.Combine(dryRunService.Paths.YarnCiTemporaryHomePath, ".yarnrc.yml")
            )
        );
        Assert.Contains(
            "npmRegistryServer: '" + TestRegistryUrl + "'",
            applyFileSystem.ReadAllText(
                Path.Combine(applyService.Paths.YarnCiTemporaryHomePath, ".yarnrc.yml")
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task YarnCiPlanWithAmbientYarnRcFilenameClearsOverrideDuringActivation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name switch
                {
                    "SYSTEM_ACCESSTOKEN" => "system-token",
                    "YARN_RC_FILENAME" => "team.yarnrc.yml",
                    _ => null,
                }
        );

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationActivationEnvironment activation =
            Assert.IsType<ConfigurationActivationEnvironment>(
                result.PlanResult.Plan.TemporaryContainer!.ActivationEnvironment
            );
        Assert.Equal(service.Paths.YarnCiTemporaryHomePath, activation.SetVariables["HOME"]);
        Assert.Equal(["YARN_RC_FILENAME"], activation.ClearVariables);
    }

    [Theory]
    [InlineData(2042, 1, 2)]
    [InlineData(2099, 12, 31)]
    public async Task DryRunLifecycleUsesInjectedFrozenFutureClock(int year, int month, int day)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        DateTimeOffset now = new(year, month, day, 3, 4, 5, TimeSpan.Zero);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            timeProvider: new FixedTimeProvider(now)
        );

        ConfigurationPhase14PlanResult result = await service.DryRunConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.True(
            RegistryCredentialLifecycleMetadataCodec.TryRead(
                result.PlanResult.Plan.Manifest.SafeMetadata,
                out RegistryCredentialLifecycleMetadata? lifecycle
            )
        );
        Assert.Equal(now, lifecycle!.IssuedAt);
        Assert.Equal(now.AddHours(1), lifecycle.ExpiresAt);
        Assert.Null(RegistryCredentialLifecycleMetadataCodec.GetViolation(lifecycle));
    }

    [Fact]
    public async Task CleanupLeavesMalformedManifestAndContainerUntouched()
    {
        const string MalformedManifest = """{"not":"a manifest"}""";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: ReadCiEnvironment
        );
        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        string manifestPath = Path.Combine(
            service.Paths.CiTemporaryManifestDirectoryPath,
            "yarn-ci-temporary-ownership-manifest.json"
        );
        fileSystem.WriteAllText(manifestPath, MalformedManifest);

        ConfigurationPhase14CleanupResult dryRun = await service.DryRunCleanupAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("incomplete", Assert.Single(dryRun.Ecosystems).State);
        Assert.Equal(0, Assert.Single(dryRun.Ecosystems).ChangeCount);
        Assert.Equal(0, Assert.Single(dryRun.Ecosystems).AppliedChangeCount);
        Assert.Equal(MalformedManifest, fileSystem.ReadAllText(manifestPath));
        Assert.True(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));

        ConfigurationPhase14CleanupResult executed = await service.CleanupAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(
            Assert.Single(dryRun.Ecosystems).State,
            Assert.Single(executed.Ecosystems).State
        );
        Assert.Equal(MalformedManifest, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(0, Assert.Single(executed.Ecosystems).AppliedChangeCount);
        Assert.True(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));
    }

    [Fact]
    public async Task YarnFilenameEnvironmentIsValidatedAndPersistedForInspectionAndRemoval()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService configuredService = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name switch
                {
                    "HOME" => "/home/test",
                    "YARN_RC_FILENAME" => "team.yarnrc.yml",
                    _ => null,
                }
        );
        await configuredService.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("/home/test/team.yarnrc.yml", configuredService.Paths.YarnUserYarnrcPath);

        ConfigurationPhase14VerticalSliceService changedEnvironmentService = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name switch
                {
                    "HOME" => "/home/test",
                    "YARN_RC_FILENAME" => "other.yarnrc.yml",
                    _ => null,
                }
        );
        ConfigurationPhase14DoctorResult doctor = await changedEnvironmentService.DoctorAsync(
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14EcosystemDoctorResult yarn = Assert.Single(
            doctor.Ecosystems,
            result =>
                result.Ecosystem == CredentialEcosystem.Yarn
                && result.Scope == ConfigurationPhase14Scope.User
        );
        Assert.True(yarn.OwnedTargetPresent);
        Assert.Equal(RegistryCredentialLifecycleState.Fresh, yarn.LifecycleState);

        await changedEnvironmentService.UnconfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.DoesNotContain(
            "fake-token-",
            fileSystem.ReadAllText(configuredService.Paths.YarnUserYarnrcPath),
            StringComparison.Ordinal
        );

        ConfigurationPhase14VerticalSliceService invalidFilenameService = CreateService(
            new InMemoryFileSystem(InMemoryPathSemantics.Posix),
            environmentVariableReader: name =>
                name switch
                {
                    "HOME" => "/home/test",
                    "YARN_RC_FILENAME" => "../outside.yml",
                    _ => null,
                }
        );
        Assert.Equal("/home/test/.yarnrc.yml", invalidFilenameService.Paths.YarnUserYarnrcPath);
    }

    [Fact]
    public void EffectiveHomeUsesUserProfileOnWindowsAndHomeOnUnix()
    {
        static string? ReadHomeEnvironment(string name) =>
            name switch
            {
                "HOME" => "/home/unix-user",
                "USERPROFILE" => @"C:\Users\windows-user",
                _ => null,
            };

        var windows = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows),
                StateDirectoryPath = @"C:\State\home",
                EnvironmentVariableReader = ReadHomeEnvironment,
            }
        );
        var unix = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix),
                StateDirectoryPath = "/state/home",
                EnvironmentVariableReader = ReadHomeEnvironment,
            }
        );

        Assert.Equal(@"C:\Users\windows-user\.npmrc", windows.Paths.NpmUserNpmrcPath);
        Assert.Equal(@"C:\Users\windows-user\.yarnrc.yml", windows.Paths.YarnUserYarnrcPath);
        Assert.Equal("/home/unix-user/.npmrc", unix.Paths.NpmUserNpmrcPath);
        Assert.Equal("/home/unix-user/.yarnrc.yml", unix.Paths.YarnUserYarnrcPath);
    }

    [Fact]
    public async Task CleanupCiTemporaryPreservesUnrelatedYarnHomeFiles()
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

        ConfigurationPhase14CleanupEcosystemResult cleanupResult = Assert.Single(result.Ecosystems);
        Assert.Equal("incomplete", cleanupResult.State);
        Assert.True(cleanupResult.ChangeCount > 0);
        Assert.False(cleanupResult.OwnershipManifestPresent);
        Assert.True(cleanupResult.TemporaryContainerPresent);
        Assert.True(fileSystem.FileExists(Path.Combine(ciArtifactDirectory, "metadata.txt")));
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
                    new SilentTestAcquisitionService()
                ),
                EnvironmentVariableReader = _ => null,
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = new(
                        "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"
                    ),
                },
            }
        );

        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateConfigureRequest(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.CiTemporary
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateUnconfigureRequest(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.CiTemporary
            )
        );
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
                    new SilentTestAcquisitionService()
                ),
                EnvironmentVariableReader = _ => null,
            }
        );

        Assert.Throws<NotSupportedException>(() =>
            service.ValidateConfigureRequest(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.CiTemporary
            )
        );
        Assert.Throws<NotSupportedException>(() =>
            service.ValidateUnconfigureRequest(
                CredentialEcosystem.Python,
                ConfigurationPhase14Scope.CiTemporary
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            service.ValidateConfigureRequest(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.User
            )
        );
    }

    private static ConfigurationPhase14VerticalSliceService CreateService(
        InMemoryFileSystem fileSystem,
        IIdentityProvider? identityProvider = null,
        Func<string, string?>? environmentVariableReader = null,
        TimeProvider? timeProvider = null,
        Uri? registryUrl = null
    ) =>
        new(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/phase14",
                AzurePipelinesJobScopeId = "phase14-test-job",
                CredentialAcquisition = identityProvider is null
                    ? new BoundedCredentialAcquisitionAdapter(new SilentTestAcquisitionService())
                    : null,
                CredentialCoreService = identityProvider is null
                    ? null
                    : new CredentialCoreService(identityProvider),
                EnvironmentVariableReader = environmentVariableReader ?? (_ => null),
                TimeProvider = timeProvider,
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = registryUrl ?? new(TestRegistryUrl),
                    [CredentialEcosystem.Pnpm] = registryUrl ?? new(TestRegistryUrl),
                    [CredentialEcosystem.Yarn] = registryUrl ?? new(TestRegistryUrl),
                },
            }
        );

    private static Func<string, string?> CreatePackagePathEnvironmentReader(
        CredentialEcosystem ecosystem,
        string suffix
    ) =>
        name =>
            (ecosystem, name) switch
            {
                (CredentialEcosystem.Npm, "NPM_CONFIG_USERCONFIG") => "/home/" + suffix + "/.npmrc",
                (CredentialEcosystem.Yarn, "YARN_RC_FILENAME") => "." + suffix + ".yarnrc.yml",
                _ => null,
            };

    private static string GetPackageConfigurationPath(
        ConfigurationPhase14VerticalSliceService service,
        CredentialEcosystem ecosystem
    ) =>
        ecosystem == CredentialEcosystem.Yarn
            ? service.Paths.YarnUserYarnrcPath
            : service.Paths.NpmUserNpmrcPath;

    private static string GetPackageManifestPath(
        ConfigurationPhase14VerticalSliceService service,
        CredentialEcosystem ecosystem
    ) =>
        Path.Combine(
            service.Paths.ManifestDirectoryPath,
            ecosystem == CredentialEcosystem.Yarn
                ? "yarn-user-ownership-manifest.json"
                : "npm-compatible-user-ownership-manifest.json"
        );

    private static string? ReadCiEnvironment(string name) =>
        string.Equals(name, "SYSTEM_ACCESSTOKEN", StringComparison.Ordinal) ? "system-token" : null;

    private static string CreateKnownCiContainer(
        InMemoryFileSystem fileSystem,
        ConfigurationPhase14ResolvedPaths paths,
        CredentialEcosystem ecosystem
    )
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
        CredentialEcosystem ecosystem
    ) =>
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
            CancellationToken cancellationToken = default
        )
        {
            Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
            Assert.Equal(InteractivePolicy.UserAllowed, request.InteractivePolicy);
            Assert.Equal(IdentityFlow.InteractiveBrowser, request.IdentityFlow);
            Assert.True(
                CredentialRequestV2Policy.IsValid(request),
                CredentialRequestV2Policy.GetViolation(request)
            );
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    BearerToken = "fake-token-silent",
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    DiagnosticsCorrelationId = "phase14-silent-test",
                }
            );
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
