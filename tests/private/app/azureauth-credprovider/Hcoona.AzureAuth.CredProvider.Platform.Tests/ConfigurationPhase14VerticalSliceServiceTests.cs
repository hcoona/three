using System.Text.Json;
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
                    EnvironmentVariableReader = ReadPosixHomeEnvironment,
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
                EnvironmentVariableReader = ReadPosixHomeEnvironment,
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
    public async Task RelativeYarnRcFilenameWritesYarn4HomeUserTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name switch
                {
                    "HOME" => "/home/alice",
                    "YARN_RC_FILENAME" => ".project.yarnrc.yml",
                    _ => null,
                }
        );

        Assert.Equal("/home/alice/.yarnrc.yml", service.Paths.YarnUserYarnrcPath);
        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.True(fileSystem.FileExists("/home/alice/.yarnrc.yml"));
        Assert.False(fileSystem.FileExists("/home/alice/.project.yarnrc.yml"));
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task NestedInvocationRejectsEffectiveAncestorCredentialShadow(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectoryTree(fileSystem, "/workspace/packages/app/src");
        switch (ecosystem)
        {
            case CredentialEcosystem.Npm:
                fileSystem.WriteAllText("/workspace/package.json", """{"name":"workspace"}""");
                fileSystem.WriteAllText(
                    "/workspace/.npmrc",
                    "//pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/:"
                        + "_authToken=repository-secret\n"
                );
                break;
            case CredentialEcosystem.Pnpm:
                fileSystem.WriteAllText("/workspace/pnpm-workspace.yaml", "packages: []\n");
                fileSystem.WriteAllText(
                    "/workspace/.npmrc",
                    "//pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/:"
                        + "_authToken=repository-secret\n"
                );
                break;
            case CredentialEcosystem.Yarn:
                fileSystem.WriteAllText(
                    "/workspace/.yarnrc.yml",
                    """
                    npmRegistries:
                      'https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/':
                        npmAuthToken: repository-secret
                    """
                );
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(ecosystem), ecosystem, null);
        }

        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            workspaceDirectoryPath: "/workspace/packages/app/src"
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    ecosystem,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains("Project-local", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("repository-secret", exception.Message, StringComparison.Ordinal);
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

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, configureResult.PlanResult.Operation);
        Assert.Equal(1, configureResult.ChangeCount);
        Assert.Contains("_authToken=fake-token-", configuredNpmrc, StringComparison.Ordinal);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, unconfigureResult.PlanResult.Operation);
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
    public async Task PathReplacementRejectsUnownedDestinationBeforeRemovingOwnedSelector(
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

        Assert.Contains(
            "without recognized ownership",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(oldContents, fileSystem.ReadAllText(oldPath));
        Assert.Equal(manifestContents, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(oldContents, fileSystem.ReadAllText(destinationPath));
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

    [Theory]
    [InlineData(CredentialEcosystem.Npm, PackageReplacementFailurePoint.CancellationAfterIntent)]
    [InlineData(CredentialEcosystem.Npm, PackageReplacementFailurePoint.TargetWriteAfterIntent)]
    [InlineData(CredentialEcosystem.Npm, PackageReplacementFailurePoint.FinalManifestAfterIntent)]
    [InlineData(CredentialEcosystem.Yarn, PackageReplacementFailurePoint.CancellationAfterIntent)]
    [InlineData(CredentialEcosystem.Yarn, PackageReplacementFailurePoint.TargetWriteAfterIntent)]
    [InlineData(CredentialEcosystem.Yarn, PackageReplacementFailurePoint.FinalManifestAfterIntent)]
    public async Task RegistryResourceReplacementFailureAfterIntentRetriesAndConverges(
        CredentialEcosystem ecosystem,
        PackageReplacementFailurePoint failurePoint
    )
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
        string targetPath = GetPackageConfigurationPath(replacement, ecosystem);
        string manifestPath = GetPackageManifestPath(replacement, ecosystem);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        InjectPackageReplacementFailure(
            fileSystem,
            targetPath,
            manifestPath,
            failurePoint,
            cancellation
        );

        if (failurePoint == PackageReplacementFailurePoint.CancellationAfterIntent)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await replacement.ConfigureAsync(
                    ecosystem,
                    ConfigurationPhase14Scope.User,
                    cancellation.Token
                )
            );
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(async () =>
                await replacement.ConfigureAsync(
                    ecosystem,
                    ConfigurationPhase14Scope.User,
                    cancellation.Token
                )
            );
        }

        ConfigurationOwnershipManifest transitional =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.Equal(ecosystem == CredentialEcosystem.Npm ? 2 : 4, transitional.Entries.Count);
        Assert.Contains(
            transitional.Entries,
            entry => entry.Key.Contains("test-feed", StringComparison.Ordinal)
        );
        Assert.Contains(
            transitional.Entries,
            entry => entry.Key.Contains("replacement-feed", StringComparison.Ordinal)
        );
        Assert.Equal(
            replacementRegistry,
            replacement.ResolvePersistedRegistryUrl(
                ecosystem,
                ConfigurationPhase14Scope.User
            )
        );

        await replacement.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        await replacement.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        ConfigurationOwnershipManifest recovered =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.Equal(ecosystem == CredentialEcosystem.Npm ? 1 : 2, recovered.Entries.Count);
        Assert.All(
            recovered.Entries,
            entry => Assert.Contains("replacement-feed", entry.Key, StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            "test-feed",
            fileSystem.ReadAllText(targetPath),
            StringComparison.Ordinal
        );

        await replacement.UnconfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult repeatedUnconfigure =
            await replacement.UnconfigureAsync(
                ecosystem,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            );

        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.False(repeatedUnconfigure.OwnershipManifestPresent);
        Assert.False(repeatedUnconfigure.OwnershipManifestCleanupIncomplete);
    }

    [Fact]
    public async Task TransitionalPackageOwnershipCanBeUnconfiguredDirectly()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService original = CreateService(fileSystem);
        await original.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14VerticalSliceService replacement = CreateService(
            fileSystem,
            registryUrl: new Uri(
                "https://pkgs.dev.azure.com/test-org/_packaging/replacement-feed/npm/registry/"
            )
        );
        string manifestPath = GetPackageManifestPath(replacement, CredentialEcosystem.Npm);
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.AtomicWriteAllBytes),
            replacement.Paths.NpmUserNpmrcPath,
            occurrence: 2,
            new IOException("Injected target write failure after ownership intent.")
        );
        await Assert.ThrowsAsync<IOException>(async () =>
            await replacement.ConfigureAsync(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            )
        );

        ConfigurationPhase14PlanResult result = await replacement.UnconfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.OwnershipManifestCleanupIncomplete);
        Assert.False(fileSystem.FileExists(manifestPath));
        Assert.DoesNotContain(
            "_authToken",
            fileSystem.ReadAllText(replacement.Paths.NpmUserNpmrcPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task TransitionalPackageOwnershipWithExtraPathRemainsRejected()
    {
        const string ForgedPath = "/forged/extra.npmrc";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService original = CreateService(fileSystem);
        await original.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14VerticalSliceService replacement = CreateService(
            fileSystem,
            registryUrl: new Uri(
                "https://pkgs.dev.azure.com/test-org/_packaging/replacement-feed/npm/registry/"
            )
        );
        string manifestPath = GetPackageManifestPath(replacement, CredentialEcosystem.Npm);
        fileSystem.FailMatchingCall(
            nameof(InMemoryFileSystem.AtomicWriteAllBytes),
            replacement.Paths.NpmUserNpmrcPath,
            occurrence: 2,
            new IOException("Injected target write failure after ownership intent.")
        );
        await Assert.ThrowsAsync<IOException>(async () =>
            await replacement.ConfigureAsync(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.User,
                TestContext.Current.CancellationToken
            )
        );
        ConfigurationOwnershipManifest transitional =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        fileSystem.CreateDirectory(Path.GetDirectoryName(ForgedPath)!);
        fileSystem.WriteAllText(ForgedPath, "sentinel=true\n");
        ConfigurationOwnershipManifest forged = transitional with
        {
            Entries =
            [
                .. transitional.Entries,
                transitional.Entries[0] with
                {
                    Sequence = transitional.Entries.Count + 1,
                    TargetPathOrName = ForgedPath,
                },
            ],
        };
        fileSystem.WriteAllText(
            manifestPath,
            ConfigurationOwnershipManifestSerializer.Serialize(forged)
        );

        InvalidOperationException configureException =
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await replacement.ConfigureAsync(
                    CredentialEcosystem.Npm,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
            );
        ConfigurationPhase14PlanResult unconfigure = await replacement.UnconfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Contains("invalid", configureException.Message, StringComparison.Ordinal);
        Assert.True(unconfigure.OwnershipManifestCleanupIncomplete);
        Assert.True(fileSystem.FileExists(manifestPath));
        Assert.Equal("sentinel=true\n", fileSystem.ReadAllText(ForgedPath));
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
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, refreshed.PlanResult.Operation);
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
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, refreshed.PlanResult.Operation);
        Assert.Equal(configured, fileSystem.ReadAllText(configurationPath));
    }

    [Fact]
    public async Task NpmConfigureRecreatesRemovedOwnedAuthSelector()
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
        string withoutAuthSelector = string.Join(
            '\n',
            configured
                .Split('\n')
                .Where(line => !string.Equals(line, authLine, StringComparison.Ordinal))
        );
        fileSystem.WriteAllText(service.Paths.NpmUserNpmrcPath, withoutAuthSelector);

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.AppliedChangeCount > 0);
        Assert.Contains(
            "_authToken",
            fileSystem.ReadAllText(service.Paths.NpmUserNpmrcPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task YarnConfigureRecreatesRemovedOwnedAuthSelector()
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

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.AppliedChangeCount > 0);
        Assert.Contains(
            "npmAuthToken",
            fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath),
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
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult executed = await executionService.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(executed.ChangeCount, dryRun.ChangeCount);
        Assert.Equal(
            executed.PlanResults.Select(static result => (result.Plan.PlanId, result.Plan.Scope)),
            dryRun.PlanResults.Select(static result => (result.Plan.PlanId, result.Plan.Scope))
        );
        Assert.Equal(
            executed.PlanResults.SelectMany(static result => result.Changes),
            dryRun.PlanResults.SelectMany(static result => result.Changes)
        );
        Assert.All(
            dryRun.PlanResults,
            static result => Assert.Equal(ConfigurationPlanOperation.DryRun, result.Operation)
        );
        Assert.False(dryRunFileSystem.DirectoryExists(dryRunService.Paths.ManifestDirectoryPath));
        Assert.False(dryRun.OwnershipManifestPresent);
    }

    [Fact]
    public async Task PythonDoctorRejectsCorruptedGeneratedTargetContent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifestEntry backend = Assert.Single(
            ReadPythonManifest(fileSystem, service).Entries,
            static entry =>
                entry.TargetKind == ConfigurationTargetKind.PythonKeyringBackend
        );
        fileSystem.WriteAllText(backend.TargetPathOrName, """{"corrupted":true}""");

        ConfigurationPhase14EcosystemDoctorResult python = GetPythonDoctorResult(
            await service.DoctorAsync(TestContext.Current.CancellationToken)
        );

        Assert.True(python.ConfigurationPlanValid);
        Assert.False(python.OwnedTargetPresent);
    }

    [Fact]
    public async Task PythonDoctorRejectsGeneratedTargetThatBecameDirectory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifestEntry backend = Assert.Single(
            ReadPythonManifest(fileSystem, service).Entries,
            static entry =>
                entry.TargetKind == ConfigurationTargetKind.PythonKeyringBackend
        );
        fileSystem.DeleteFile(backend.TargetPathOrName);
        fileSystem.CreateDirectory(backend.TargetPathOrName);

        ConfigurationPhase14EcosystemDoctorResult python = GetPythonDoctorResult(
            await service.DoctorAsync(TestContext.Current.CancellationToken)
        );

        Assert.True(python.ConfigurationPlanValid);
        Assert.False(python.OwnedTargetPresent);
    }

    [Fact]
    public async Task PythonDoctorRejectsNonExecutablePosixShim()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifestEntry shim = Assert.Single(
            ReadPythonManifest(fileSystem, service).Entries,
            static entry => entry.TargetKind == ConfigurationTargetKind.KeyringShim
        );
        fileSystem.SetUnixFileMode(
            shim.TargetPathOrName,
            UnixFileMode.UserRead | UnixFileMode.UserWrite
        );

        ConfigurationPhase14EcosystemDoctorResult python = GetPythonDoctorResult(
            await service.DoctorAsync(TestContext.Current.CancellationToken)
        );

        Assert.True(python.ConfigurationPlanValid);
        Assert.False(python.OwnedTargetPresent);
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
            executed.PlanResults.Select(static result => (result.Plan.PlanId, result.Plan.Scope)),
            dryRun.PlanResults.Select(static result => (result.Plan.PlanId, result.Plan.Scope))
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

    [Fact]
    public async Task PythonManifestWithTamperedSchemaIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            ConfigurationOwnershipManifestSerializer
                .Serialize(manifest)
                .Replace(
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":2",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public async Task PythonManifestWithTamperedOwnerIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(manifest with { OwnerProductId = "forged-owner" })
        );
    }

    [Fact]
    public async Task PythonManifestWithTamperedIdentityIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(manifest with { ManifestId = "forged-python-manifest" })
        );
    }

    [Fact]
    public async Task PythonManifestWithTamperedScopeIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(manifest with { Scope = ConfigurationScope.ExplicitPath })
        );
    }

    [Fact]
    public async Task PythonManifestWithTamperedSelectorIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(manifest with { EntrySelector = "python.forged" })
        );
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("host")]
    [InlineData("organization")]
    [InlineData("endpoint")]
    [InlineData("project")]
    [InlineData("feed")]
    [InlineData("repository")]
    public async Task PythonManifestWithTamperedResourceIdentityIsUnrecognizedAndPreserved(
        string field
    )
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
        {
            CanonicalResourceIdentity resource = Assert.IsType<CanonicalResourceIdentity>(
                manifest.ResourceIdentity
            );
            CanonicalResourceIdentity? tampered = field switch
            {
                "missing" => null,
                "host" => resource with { AzureDevOpsHost = "example.com" },
                "organization" => resource with { Organization = "forged-org" },
                "endpoint" => resource with
                {
                    ServiceEndpoint = new Uri("https://dev.azure.com/forged-org/"),
                },
                "project" => resource with { Project = "forged-project" },
                "feed" => resource with { Feed = "forged-feed" },
                "repository" => resource with { Repository = "forged-repository" },
                _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
            };
            return SerializeManifest(
                manifest with
                {
                    ResourceIdentity = tampered,
                }
            );
        });
    }

    [Fact]
    public async Task PythonManifestWithTamperedProductVersionIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(manifest with { ProductVersion = "0.0.0-forged" })
        );
    }

    [Fact]
    public async Task PythonManifestWithTamperedEcosystemMetadataIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(
                manifest with
                {
                    SafeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ecosystem"] = "npm",
                    },
                }
            )
        );
    }

    [Fact]
    public async Task PythonManifestWithUnexpectedEcosystemMetadataIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(
                manifest with
                {
                    SafeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["ecosystem"] = "python",
                        ["forged"] = "metadata",
                    },
                }
            )
        );
    }

    [Theory]
    [InlineData(ConfigurationTargetKind.PythonKeyringBackend)]
    [InlineData(ConfigurationTargetKind.KeyringShim)]
    public async Task PythonManifestWithMissingRequiredEntryRoleIsUnrecognizedAndPreserved(
        ConfigurationTargetKind missingTargetKind
    )
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(
                manifest with
                {
                    Entries = manifest
                        .Entries.Where(entry => entry.TargetKind != missingTargetKind)
                        .Select(
                            static (entry, index) => entry with { Sequence = index + 1 }
                        )
                        .ToArray(),
                }
            )
        );
    }

    [Fact]
    public async Task PythonManifestWithUnexpectedEntryCountIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
        {
            ConfigurationOwnershipManifestEntry first = manifest.Entries[0];
            return SerializeManifest(
                manifest with
                {
                    Entries =
                    [
                        .. manifest.Entries,
                        first with
                        {
                            Sequence = manifest.Entries.Count + 1,
                            TargetKind = ConfigurationTargetKind.Npmrc,
                            TargetPathOrName = "/forged/extra-entry",
                            Key = "forged-extra-entry",
                        },
                    ],
                }
            );
        });
    }

    [Fact]
    public async Task PythonManifestWithReorderedEntryRolesIsUnrecognizedAndPreserved()
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(
                manifest with
                {
                    Entries =
                    [
                        manifest.Entries[1] with { Sequence = 1 },
                        manifest.Entries[0] with { Sequence = 2 },
                    ],
                }
            )
        );
    }

    [Theory]
    [InlineData(ConfigurationTargetKind.PythonKeyringBackend)]
    [InlineData(ConfigurationTargetKind.KeyringShim)]
    public async Task PythonManifestWithUnsupportedEntryKindIsUnrecognizedAndPreserved(
        ConfigurationTargetKind targetKind
    )
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(
                manifest with
                {
                    Entries = manifest
                        .Entries.Select(entry =>
                            entry.TargetKind == targetKind
                                ? entry with
                                {
                                    TargetKind = ConfigurationTargetKind.Npmrc,
                                }
                                : entry
                        )
                        .ToArray(),
                }
            )
        );
    }

    [Theory]
    [InlineData(ConfigurationTargetKind.PythonKeyringBackend)]
    [InlineData(ConfigurationTargetKind.KeyringShim)]
    public async Task PythonManifestWithUnsupportedEntryKeyIsUnrecognizedAndPreserved(
        ConfigurationTargetKind targetKind
    )
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(
                manifest with
                {
                    Entries = manifest
                        .Entries.Select(entry =>
                            entry.TargetKind == targetKind
                                ? entry with
                                {
                                    Key = "forged-key",
                                }
                                : entry
                        )
                        .ToArray(),
                }
            )
        );
    }

    [Theory]
    [InlineData(ConfigurationTargetKind.PythonKeyringBackend)]
    [InlineData(ConfigurationTargetKind.KeyringShim)]
    public async Task PythonManifestWithNoncanonicalProjectedPathIsUnrecognizedAndPreserved(
        ConfigurationTargetKind targetKind
    )
    {
        await AssertPythonManifestTamperingIsRejectedAsync(manifest =>
            SerializeManifest(
                manifest with
                {
                    Entries = manifest
                        .Entries.Select(entry =>
                            entry.TargetKind == targetKind
                                ? entry with
                                {
                                    TargetPathOrName = "/alternate/noncanonical-python-target",
                                }
                                : entry
                        )
                        .ToArray(),
                }
            )
        );
    }

    [Fact]
    public async Task ForgedPythonTargetIsNotDeletedAndReportsIncomplete()
    {
        const string ForgedPath = "/arbitrary/forged-python-target";
        byte[] forgedBytes = [0, 1, 2, 3, 255, 13, 10];
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = ReadPythonManifest(fileSystem, service);
        string manifestPath = service.Paths.OwnershipManifestPath;
        fileSystem.AtomicWriteAllBytes(ForgedPath, forgedBytes);
        string forgedManifest = SerializeManifest(
            manifest with
            {
                Entries =
                [
                    manifest.Entries[0] with { TargetPathOrName = ForgedPath },
                    .. manifest.Entries.Skip(1),
                ],
            }
        );
        fileSystem.WriteAllText(manifestPath, forgedManifest);

        ConfigurationPhase14DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14EcosystemDoctorResult python = GetPythonDoctorResult(doctor);
        Assert.False(python.ConfigurationPlanValid);
        Assert.True(result.OwnershipManifestCleanupIncomplete);
        Assert.Equal(0, result.ChangeCount);
        Assert.Equal(0, result.AppliedChangeCount);
        Assert.True(result.OwnershipManifestPresent);
        Assert.Equal(forgedManifest, fileSystem.ReadAllText(manifestPath));
        Assert.True(fileSystem.FileExists(ForgedPath));
        Assert.Equal(forgedBytes, fileSystem.ReadAllBytes(ForgedPath));
    }

    [Fact]
    public async Task ConfigurePythonWithForgedManifestRejectsBeforeWritingAndPreservesBytes()
    {
        const string ForgedPath = "/arbitrary/configure-target";
        byte[] forgedBytes = [5, 4, 3, 2, 1];
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = ReadPythonManifest(fileSystem, service);
        Dictionary<string, byte[]> originalTargets = manifest.Entries.ToDictionary(
            static entry => entry.TargetPathOrName,
            entry => fileSystem.ReadAllBytes(entry.TargetPathOrName),
            StringComparer.Ordinal
        );
        fileSystem.AtomicWriteAllBytes(ForgedPath, forgedBytes);
        string forgedManifest = SerializeManifest(
            manifest with
            {
                Entries =
                [
                    manifest.Entries[0] with { TargetPathOrName = ForgedPath },
                    .. manifest.Entries.Skip(1),
                ],
            }
        );
        fileSystem.WriteAllText(service.Paths.OwnershipManifestPath, forgedManifest);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    CredentialEcosystem.Python,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Equal(
            "The existing Python ownership manifest is not recognized.",
            exception.Message
        );
        Assert.Equal(forgedManifest, fileSystem.ReadAllText(service.Paths.OwnershipManifestPath));
        Assert.Equal(forgedBytes, fileSystem.ReadAllBytes(ForgedPath));
        foreach ((string path, byte[] bytes) in originalTargets)
        {
            Assert.True(fileSystem.FileExists(path));
            Assert.Equal(bytes, fileSystem.ReadAllBytes(path));
        }
    }

    [Fact]
    public async Task PythonManifestCurrentWindowsLayoutRemainsRecognized()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = ReadPythonManifest(fileSystem, service);
        ConfigurationOwnershipManifestEntry backend = Assert.Single(manifest.Entries);
        Assert.Equal(ConfigurationTargetKind.PythonKeyringBackend, backend.TargetKind);

        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.OwnershipManifestCleanupIncomplete);
        Assert.False(result.OwnershipManifestPresent);
        Assert.Equal(1, result.ChangeCount);
        Assert.Equal(1, result.AppliedChangeCount);
        Assert.False(fileSystem.FileExists(backend.TargetPathOrName));
    }

    [Fact]
    public async Task PythonManifestLegacyWindowsLayoutRemainsRecognized()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = ReadPythonManifest(fileSystem, service);
        ConfigurationOwnershipManifestEntry backend = Assert.Single(manifest.Entries);
        string productRoot = Path.GetDirectoryName(
            Path.GetDirectoryName(backend.TargetPathOrName)!
        )!;
        string legacyShimPath = Path.Combine(productRoot, "keyring-shim", "keyring.exe");
        fileSystem.CreateDirectory(Path.GetDirectoryName(legacyShimPath)!);
        fileSystem.WriteAllText(legacyShimPath, "legacy product-owned shim");
        fileSystem.WriteAllText(
            service.Paths.OwnershipManifestPath,
            SerializeManifest(
                manifest with
                {
                    Entries =
                    [
                        backend,
                        backend with
                        {
                            Sequence = 2,
                            TargetKind = ConfigurationTargetKind.KeyringShim,
                            TargetPathOrName = legacyShimPath,
                        },
                    ],
                }
            )
        );

        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.False(result.OwnershipManifestCleanupIncomplete);
        Assert.False(result.OwnershipManifestPresent);
        Assert.Equal(2, result.ChangeCount);
        Assert.Equal(2, result.AppliedChangeCount);
        Assert.False(fileSystem.FileExists(backend.TargetPathOrName));
        Assert.False(fileSystem.FileExists(legacyShimPath));
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
        Assert.Equal(ConfigurationPlanOperation.DryRun, dryRun.PlanResult.Operation);
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, executed.PlanResult.Operation);
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
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.PlanResult.Operation);
        Assert.Equal(2, result.ChangeCount);
        Assert.Contains("npmAlwaysAuth: true", yarnrc);
        Assert.Contains("npmAuthToken: 'fake-token-", yarnrc);
    }

    [Fact]
    public async Task ConfigurePythonWritesProductExecutableManifest()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = CreateService(fileSystem);

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        int expectedChangeCount = UsesWindowsPaths(fileSystem) ? 1 : 2;
        Assert.Equal(expectedChangeCount, result.PlanResults.Count);
        Assert.Equal(expectedChangeCount, result.ChangeCount);
        Assert.All(
            result.PlanResults,
            planResult => Assert.NotEqual(ConfigurationPlanOperation.DryRun, planResult.Operation)
        );
        ConfigurationPlannedChange manifestChange = Assert.Single(result.PlanResults[0].Changes);
        using JsonDocument manifest = JsonDocument.Parse(
            fileSystem.ReadAllText(manifestChange.TargetPathOrName)
        );
        JsonElement root = manifest.RootElement;
        Assert.Equal(2, root.GetProperty("contractMajor").GetInt32());
        Assert.Equal("azureauth-credprovider", root.GetProperty("productId").GetString());
        Assert.Equal(
            GetTestProductExecutablePath(fileSystem),
            root.GetProperty("absoluteHelperPath").GetString()
        );
        Assert.Equal(GetTestProductPlatform(fileSystem), root.GetProperty("platform").GetString());
        if (!UsesWindowsPaths(fileSystem))
        {
            ConfigurationPlannedChange shimChange = Assert.Single(result.PlanResults[1].Changes);
            Assert.Equal(
                "#!/bin/sh\nexec azureauth-keyring \"$@\"\n",
                fileSystem.ReadAllText(shimChange.TargetPathOrName)
            );
        }
    }

    [Fact]
    public async Task ConfigurePythonRemovesObsoleteOwnedWindowsShim()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        string manifestPath = Path.Combine(
            service.Paths.ManifestDirectoryPath,
            "python-user-ownership-manifest.json"
        );
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        ConfigurationOwnershipManifestEntry backendEntry = Assert.Single(manifest.Entries);
        string productRoot = Path.GetDirectoryName(
            Path.GetDirectoryName(backendEntry.TargetPathOrName)!
        )!;
        string obsoleteShimPath = Path.Combine(productRoot, "keyring-shim", "keyring.exe");
        fileSystem.CreateDirectory(Path.GetDirectoryName(obsoleteShimPath)!);
        fileSystem.WriteAllText(obsoleteShimPath, "obsolete placeholder");
        fileSystem.WriteAllText(
            manifestPath,
            ConfigurationOwnershipManifestSerializer.Serialize(
                manifest with
                {
                    Entries =
                    [
                        backendEntry,
                        backendEntry with
                        {
                            Sequence = backendEntry.Sequence + 1,
                            TargetKind = ConfigurationTargetKind.KeyringShim,
                            TargetPathOrName = obsoleteShimPath,
                        },
                    ],
                }
            )
        );

        ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, result.ChangeCount);
        Assert.Equal(ConfigurationPlanOperation.Remove, result.PlanResults[0].Operation);
        Assert.Equal(ConfigurationPlanOperation.Apply, result.PlanResults[1].Operation);
        Assert.False(fileSystem.FileExists(obsoleteShimPath));
        ConfigurationOwnershipManifest reconciled =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.DoesNotContain(
            reconciled.Entries,
            static entry => entry.TargetKind == ConfigurationTargetKind.KeyringShim
        );
    }

    [Fact]
    public async Task ConfigurePythonRejectsUnrelatedWindowsShimWithoutDeletingIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string UnrelatedShimPath = @"C:\unrelated\keyring.exe";
        const string UnrelatedContents = "unrelated file";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        var service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string manifestPath = Path.Combine(
            service.Paths.ManifestDirectoryPath,
            "python-user-ownership-manifest.json"
        );
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        ConfigurationOwnershipManifestEntry backendEntry = Assert.Single(manifest.Entries);
        fileSystem.CreateDirectory(@"C:\unrelated");
        fileSystem.WriteAllText(UnrelatedShimPath, UnrelatedContents);
        fileSystem.WriteAllText(
            manifestPath,
            ConfigurationOwnershipManifestSerializer.Serialize(
                manifest with
                {
                    Entries =
                    [
                        backendEntry,
                        backendEntry with
                        {
                            Sequence = backendEntry.Sequence + 1,
                            TargetKind = ConfigurationTargetKind.KeyringShim,
                            TargetPathOrName = UnrelatedShimPath,
                        },
                    ],
                }
            )
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    CredentialEcosystem.Python,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Equal(
            "The existing Python ownership manifest is not recognized.",
            exception.Message
        );
        Assert.True(fileSystem.FileExists(UnrelatedShimPath));
        Assert.Equal(UnrelatedContents, fileSystem.ReadAllText(UnrelatedShimPath));
    }

    [Fact]
    public async Task ConfigureAndUnconfigurePythonRemoveBackendManifest()
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

        int expectedChangeCount = UsesWindowsPaths(fileSystem) ? 1 : 2;
        Assert.Equal(expectedChangeCount, result.PlanResults.Count);
        Assert.Equal(expectedChangeCount, result.ChangeCount);
        Assert.False(result.OwnershipManifestPresent);
        Assert.All(
            result.PlanResults,
            planResult => Assert.NotEqual(ConfigurationPlanOperation.DryRun, planResult.Operation)
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

        Assert.Equal(UsesWindowsPaths(fileSystem) ? 1 : 2, pythonResult.ChangeCount);
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
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.PlanResult.Operation);
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

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.PlanResult.Operation);
        Assert.False(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
        Assert.False(result.OwnershipManifestPresent);
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    public async Task CleanupCiTemporaryDeletesProductOwnedNpmrc(CredentialEcosystem ecosystem)
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
        Assert.Equal("removed", cleanup.State);
        Assert.False(cleanup.OwnershipManifestPresent);
        Assert.False(cleanup.TemporaryContainerPresent);
        Assert.False(fileSystem.FileExists(path));
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

        Assert.NotEqual(ConfigurationPlanOperation.DryRun, result.PlanResult.Operation);
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
    public async Task YarnAbsoluteFilenameOverrideSelectsNonRepositoryCredentialTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectoryTree(fileSystem, "/secure/yarn");
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name switch
                {
                    "YARN_RC_FILENAME" => "/secure/yarn/credentials.yml",
                    _ => null,
                },
            workspaceDirectoryPath: "/workspace"
        );

        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("/secure/yarn/credentials.yml", service.Paths.YarnUserYarnrcPath);
        Assert.Contains(
            "npmAuthToken",
            fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task YarnAncestorRelativeSubpathIsInspectedWithoutBecomingUserTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectoryTree(fileSystem, "/workspace/config");
        CreateDirectoryTree(fileSystem, "/workspace/packages/app/src");
        fileSystem.WriteAllText(
            "/workspace/config/team.yarnrc.yml",
            """
            npmRegistries:
              'https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/':
                npmAuthToken: repository-secret
            """
        );
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name switch
                {
                    "YARN_RC_FILENAME" => "config/team.yarnrc.yml",
                    _ => null,
                },
            workspaceDirectoryPath: "/workspace/packages/app/src"
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    CredentialEcosystem.Yarn,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Equal("/home/test/.yarnrc.yml", service.Paths.YarnUserYarnrcPath);
        Assert.Contains("Project-local Yarn selector", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("repository-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YarnAbsoluteFilenameOverrideRejectsRepositoryCredentialTarget()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectoryTree(fileSystem, "/repos/a/packages/app");
        CreateDirectoryTree(fileSystem, "/repos/a/.git");
        CreateDirectoryTree(fileSystem, "/repos/b/config");
        CreateDirectoryTree(fileSystem, "/repos/b/.git");
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: name =>
                name switch
                {
                    "YARN_RC_FILENAME" => "/repos/b/config/team.yarnrc.yml",
                    _ => null,
                },
            workspaceDirectoryPath: "/repos/a/packages/app"
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    CredentialEcosystem.Yarn,
                    ConfigurationPhase14Scope.User,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains("Repository-local Yarn", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.FileExists(service.Paths.YarnUserYarnrcPath));
    }

    [Fact]
    public async Task YarnCiTemporaryRootUnderCheckoutIsRejected()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CreateDirectoryTree(fileSystem, "/checkout/.git");
        CreateDirectoryTree(fileSystem, "/checkout/artifacts");
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            ciTemporaryProductRootPath: "/checkout/artifacts/azureauth"
        );

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await service.ConfigureAsync(
                    CredentialEcosystem.Yarn,
                    ConfigurationPhase14Scope.CiTemporary,
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains("Repository-local Yarn", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.DirectoryExists(service.Paths.YarnCiTemporaryHomePath));
    }

    [Fact]
    public async Task YarnDoctorMarksManifestInvalidWhenHomeBecomesRepository()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.True(fileSystem.FileExists(service.Paths.YarnUserYarnrcPath));
        fileSystem.CreateDirectory("/home/test/.git");

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
    }

    [Fact]
    public async Task YarnRelativeFilenameOverrideKeepsUserTargetForInspectionAndRemoval()
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
        Assert.Equal("/home/test/.yarnrc.yml", configuredService.Paths.YarnUserYarnrcPath);

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

        Assert.Throws<InvalidOperationException>(() =>
            CreateService(
                new InMemoryFileSystem(InMemoryPathSemantics.Posix),
                environmentVariableReader: name =>
                    name switch
                    {
                        "HOME" => "/home/test",
                        "YARN_RC_FILENAME" => "../outside.yml",
                        _ => null,
                    }
            )
        );
    }

    [Fact]
    public void EffectiveHomeUsesHomeOnPosix()
    {
        static string? ReadHomeEnvironment(string name) =>
            name switch
            {
                "HOME" => "/home/unix-user",
                _ => null,
            };

        var unix = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix),
                StateDirectoryPath = "/state/home",
                EnvironmentVariableReader = ReadHomeEnvironment,
            }
        );

        Assert.Equal("/home/unix-user/.npmrc", unix.Paths.NpmUserNpmrcPath);
        Assert.Equal("/home/unix-user/.yarnrc.yml", unix.Paths.YarnUserYarnrcPath);
    }

    [Fact]
    public async Task CleanupCiTemporaryDeletesProductOwnedYarnHome()
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
        Assert.Equal("removed", cleanupResult.State);
        Assert.True(cleanupResult.ChangeCount > 0);
        Assert.False(cleanupResult.OwnershipManifestPresent);
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
                FileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix),
                StateDirectoryPath = "/state/phase14-dry-run",
                AzurePipelinesJobScopeId = jobScopeId,
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(
                    new SilentTestAcquisitionService()
                ),
                EnvironmentVariableReader = ReadPosixHomeEnvironment,
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
                FileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix),
                StateDirectoryPath = "/state/phase14-dry-run",
                AzurePipelinesJobScopeId = "job-1",
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(
                    new SilentTestAcquisitionService()
                ),
                EnvironmentVariableReader = ReadPosixHomeEnvironment,
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

#pragma warning disable CA1707 // Exact regression-test names are required by the Phase 4 plan.
    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task DoctorAsync_MissingOwnedSelectorReportsMissingLifecycle(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        string configurationPath = GetPackageConfigurationPath(service, ecosystem);
        fileSystem.CreateDirectory(Path.GetDirectoryName(configurationPath)!);
        fileSystem.WriteAllText(
            configurationPath,
            ecosystem == CredentialEcosystem.Yarn
                ? "# unrelated yarn configuration\nnodeLinker: node-modules\n"
                : "# unrelated npm configuration\nfund=false\n"
        );
        await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string configured = fileSystem.ReadAllText(configurationPath);
        string withoutManagedSelectors = string.Join(
            '\n',
            configured
                .Split('\n')
                .Where(line =>
                    ecosystem == CredentialEcosystem.Yarn
                        ? !line.TrimStart().StartsWith("npmAuthToken:", StringComparison.Ordinal)
                        : !line.Contains(":_authToken=", StringComparison.Ordinal)
                )
        );
        fileSystem.WriteAllText(configurationPath, withoutManagedSelectors);

        ConfigurationPhase14DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14EcosystemDoctorResult package = Assert.Single(
            doctor.Ecosystems,
            result =>
                result.Ecosystem == ecosystem && result.Scope == ConfigurationPhase14Scope.User
        );
        Assert.True(fileSystem.FileExists(configurationPath));
        Assert.Contains("# unrelated", fileSystem.ReadAllText(configurationPath));
        if (ecosystem == CredentialEcosystem.Yarn)
        {
            Assert.Contains(
                "npmAlwaysAuth: true",
                fileSystem.ReadAllText(configurationPath),
                StringComparison.Ordinal
            );
        }
        Assert.True(package.OwnershipManifestPresent);
        Assert.False(package.OwnedTargetPresent);
        Assert.Equal(RegistryCredentialLifecycleState.Missing, package.LifecycleState);
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task DoctorAsync_ConfiguredCiPackageWithUnknownExpiryReportsFresh(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            environmentVariableReader: ReadCiEnvironment
        );
        await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14EcosystemDoctorResult package = Assert.Single(
            doctor.Ecosystems,
            result =>
                result.Ecosystem == ecosystem
                && result.Scope == ConfigurationPhase14Scope.CiTemporary
        );
        Assert.True(package.OwnershipManifestPresent);
        Assert.True(package.OwnedTargetPresent);
        Assert.True(package.TemporaryContainerPresent);
        Assert.Equal(RegistryCredentialLifecycleState.Fresh, package.LifecycleState);
        Assert.Null(package.CredentialExpiresAt);
    }

    [Theory]
    [InlineData(CredentialEcosystem.Npm)]
    [InlineData(CredentialEcosystem.Pnpm)]
    [InlineData(CredentialEcosystem.Yarn)]
    public async Task ConfigureUserPackageCredential_UnknownExpiryRecommendsRefresh(
        CredentialEcosystem ecosystem
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var acquisition = new CapturingCredentialAcquisitionService("unknown-expiry-token");
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            credentialAcquisition: acquisition
        );

        await service.ConfigureAsync(
            ecosystem,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );

        CredentialRequestV2 request = Assert.Single(acquisition.Requests);
        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
        Assert.Contains(
            "unknown-expiry-token",
            fileSystem.ReadAllText(GetPackageConfigurationPath(service, ecosystem)),
            StringComparison.Ordinal
        );
        ConfigurationPhase14EcosystemDoctorResult package = Assert.Single(
            doctor.Ecosystems,
            result =>
                result.Ecosystem == ecosystem && result.Scope == ConfigurationPhase14Scope.User
        );
        Assert.True(package.OwnedTargetPresent);
        Assert.Equal(RegistryCredentialLifecycleState.RefreshRecommended, package.LifecycleState);
        Assert.Null(package.CredentialExpiresAt);
    }

    [Fact]
    public async Task GetPackageCredential_CiUsesSystemTokenWithoutAcquisition()
    {
        const string SystemToken = "ambient-system-token";
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var acquisition = new CapturingCredentialAcquisitionService("must-not-be-used");
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/phase14-v2-ci",
                AzurePipelinesJobScopeId = "phase14-v2-ci-job",
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(acquisition),
                EnvironmentVariableReader = name =>
                    name switch
                    {
                        "SYSTEM_ACCESSTOKEN" => SystemToken,
                        "HOME" => "/home/test",
                        _ => null,
                    },
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = new(TestRegistryUrl),
                },
            }
        );

        Assert.Equal(
            "/state/phase14-v2-ci/ci-jobs/phase14-v2-ci-job",
            service.Paths.CiTemporaryRootPath
        );
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken
        );

        Assert.Empty(acquisition.Requests);
        Assert.Contains(
            SystemToken,
            fileSystem.ReadAllText(service.Paths.NpmCiTemporaryNpmrcPath),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "must-not-be-used",
            fileSystem.ReadAllText(service.Paths.NpmCiTemporaryNpmrcPath),
            StringComparison.Ordinal
        );
    }
#pragma warning restore CA1707

    private static ConfigurationPhase14VerticalSliceService CreateService(
        InMemoryFileSystem fileSystem,
        IIdentityProvider? identityProvider = null,
        Func<string, string?>? environmentVariableReader = null,
        TimeProvider? timeProvider = null,
        Uri? registryUrl = null,
        ICredentialAcquisitionService? credentialAcquisition = null,
        string? workspaceDirectoryPath = null,
        string? ciTemporaryProductRootPath = null
    ) =>
        new(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = UsesWindowsPaths(fileSystem)
                    ? @"C:\state\phase14"
                    : "/state/phase14",
                CiTemporaryProductRootPath = ciTemporaryProductRootPath,
                AzurePipelinesJobScopeId = "phase14-test-job",
                CredentialAcquisition = identityProvider is null
                    ? new BoundedCredentialAcquisitionAdapter(
                        credentialAcquisition ?? new SilentTestAcquisitionService()
                    )
                    : null,
                CredentialCoreService = identityProvider is null
                    ? null
                    : new CredentialCoreService(identityProvider),
                EnvironmentVariableReader = CreateTestEnvironmentReader(
                    fileSystem,
                    environmentVariableReader
                ),
                ProductExecutablePath = GetTestProductExecutablePath(fileSystem),
                WorkspaceDirectoryPath = workspaceDirectoryPath,
                TimeProvider = timeProvider,
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = registryUrl ?? new(TestRegistryUrl),
                    [CredentialEcosystem.Pnpm] = registryUrl ?? new(TestRegistryUrl),
                    [CredentialEcosystem.Yarn] = registryUrl ?? new(TestRegistryUrl),
                },
            }
        );

    private static void CreateDirectoryTree(InMemoryFileSystem fileSystem, string path)
    {
        string current = string.Empty;
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;
            if (!fileSystem.DirectoryExists(current))
            {
                fileSystem.CreateDirectory(current);
            }
        }
    }

    private static async Task AssertPythonManifestTamperingIsRejectedAsync(
        Func<ConfigurationOwnershipManifest, string> createTamperedManifest
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem);
        await service.ConfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationOwnershipManifest manifest = ReadPythonManifest(fileSystem, service);
        Dictionary<string, byte[]> originalTargets = manifest.Entries.ToDictionary(
            static entry => entry.TargetPathOrName,
            entry => fileSystem.ReadAllBytes(entry.TargetPathOrName),
            StringComparer.Ordinal
        );
        string tamperedManifest = createTamperedManifest(manifest);
        fileSystem.WriteAllText(service.Paths.OwnershipManifestPath, tamperedManifest);

        ConfigurationPhase14DoctorResult doctor = await service.DoctorAsync(
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Python,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        ConfigurationPhase14EcosystemDoctorResult python = GetPythonDoctorResult(doctor);
        Assert.False(python.ConfigurationPlanValid);
        Assert.True(python.OwnershipManifestPresent);
        Assert.False(python.OwnedTargetPresent);
        Assert.True(result.OwnershipManifestCleanupIncomplete);
        Assert.Equal(0, result.ChangeCount);
        Assert.Equal(0, result.AppliedChangeCount);
        Assert.True(result.OwnershipManifestPresent);
        Assert.Equal(
            tamperedManifest,
            fileSystem.ReadAllText(service.Paths.OwnershipManifestPath)
        );
        foreach ((string path, byte[] bytes) in originalTargets)
        {
            Assert.True(fileSystem.FileExists(path));
            Assert.Equal(bytes, fileSystem.ReadAllBytes(path));
        }
    }

    private static ConfigurationOwnershipManifest ReadPythonManifest(
        InMemoryFileSystem fileSystem,
        ConfigurationPhase14VerticalSliceService service
    ) =>
        ConfigurationOwnershipManifestSerializer.Deserialize(
            fileSystem.ReadAllText(service.Paths.OwnershipManifestPath)
        );

    private static string SerializeManifest(ConfigurationOwnershipManifest manifest) =>
        ConfigurationOwnershipManifestSerializer.Serialize(manifest);

    private static ConfigurationPhase14EcosystemDoctorResult GetPythonDoctorResult(
        ConfigurationPhase14DoctorResult doctor
    ) =>
        Assert.Single(
            doctor.Ecosystems,
            static result =>
                result.Ecosystem == CredentialEcosystem.Python
                && result.Scope == ConfigurationPhase14Scope.User
        );

    private static string GetTestProductExecutablePath(InMemoryFileSystem fileSystem) =>
        UsesWindowsPaths(fileSystem)
            ? @"C:\Program Files\AzureAuth\CredProvider\azureauth-credprovider.exe"
            : "/opt/azureauth-credprovider/azureauth-credprovider";

    private static string GetTestProductPlatform(InMemoryFileSystem fileSystem) =>
        UsesWindowsPaths(fileSystem) ? "windows"
        : OperatingSystem.IsMacOS() ? "macOs"
        : "linux";

    private static Func<string, string?> CreateTestEnvironmentReader(
        InMemoryFileSystem fileSystem,
        Func<string, string?>? configuredReader
    ) =>
        name =>
            configuredReader?.Invoke(name)
            ?? (
                UsesWindowsPaths(fileSystem)
                    ? name switch
                    {
                        "LOCALAPPDATA" => @"C:\Users\Test\AppData\Local",
                        "USERPROFILE" => @"C:\Users\Test",
                        _ => null,
                    }
                    : name == "HOME"
                        ? "/home/test"
                        : null
            );

    private static bool UsesWindowsPaths(InMemoryFileSystem fileSystem) =>
        fileSystem.IsPathFullyQualified(@"C:\");

    private static Func<string, string?> CreatePackagePathEnvironmentReader(
        CredentialEcosystem ecosystem,
        string suffix
    ) =>
        name =>
            (ecosystem, name) switch
            {
                (CredentialEcosystem.Npm, "NPM_CONFIG_USERCONFIG") => "/home/" + suffix + "/.npmrc",
                (CredentialEcosystem.Yarn, "YARN_RC_FILENAME") =>
                    "/home/" + suffix + "/.yarnrc.yml",
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
        name switch
        {
            "SYSTEM_ACCESSTOKEN" => "system-token",
            "HOME" => "/home/test",
            _ => null,
        };

    private static string? ReadPosixHomeEnvironment(string name) =>
        string.Equals(name, "HOME", StringComparison.Ordinal) ? "/home/test" : null;

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

    private sealed class CapturingCredentialAcquisitionService(string bearerToken)
        : ICredentialAcquisitionService
    {
        public List<CredentialRequestV2> Requests { get; } = [];

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    BearerToken = bearerToken,
                    DiagnosticsCorrelationId = "phase14-v2-ci-capturing-test",
                }
            );
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

    public enum PackageReplacementFailurePoint
    {
        CancellationAfterIntent,
        TargetWriteAfterIntent,
        FinalManifestAfterIntent,
    }

    private static void InjectPackageReplacementFailure(
        InMemoryFileSystem fileSystem,
        string targetPath,
        string manifestPath,
        PackageReplacementFailurePoint failurePoint,
        CancellationTokenSource cancellation
    )
    {
        if (failurePoint == PackageReplacementFailurePoint.CancellationAfterIntent)
        {
            fileSystem.RunOnMatchingCall(
                nameof(InMemoryFileSystem.AtomicWriteAllText),
                manifestPath,
                occurrence: 1,
                cancellation.Cancel
            );
            return;
        }

        (string operation, string path, int occurrence, Exception exception) = failurePoint switch
        {
            PackageReplacementFailurePoint.TargetWriteAfterIntent =>
                (
                    nameof(InMemoryFileSystem.AtomicWriteAllBytes),
                    targetPath,
                    2,
                    (Exception)
                        new IOException("Injected target write failure after ownership intent.")
                ),
            PackageReplacementFailurePoint.FinalManifestAfterIntent =>
                (
                    nameof(InMemoryFileSystem.AtomicWriteAllText),
                    manifestPath,
                    2,
                    (Exception)
                        new IOException("Injected final manifest failure after ownership intent.")
                ),
            _ => throw new ArgumentOutOfRangeException(nameof(failurePoint)),
        };
        fileSystem.FailMatchingCall(operation, path, occurrence, exception);
    }
}
