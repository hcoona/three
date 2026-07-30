using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class RegistryCredentialLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 4, 56, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiryPolicyClassifiesLifecycleTimestamps()
    {
        var time = new MutableTimeProvider(Now);
        var policy = new RegistryCredentialExpiryPolicy(time);
        RegistryCredentialLifecycleMetadata metadata = policy.Create(
            ConfigurationScope.User,
            Now.AddHours(1)
        );

        Assert.Equal(RegistryCredentialLifecycleState.Fresh, policy.Evaluate(metadata));
        time.Now = Now.AddMinutes(50);
        Assert.Equal(
            RegistryCredentialLifecycleState.RefreshRecommended,
            policy.Evaluate(metadata)
        );
        time.Now = Now.AddHours(1);
        Assert.Equal(RegistryCredentialLifecycleState.Expired, policy.Evaluate(metadata));
        time.Now = Now;
        Assert.Equal(
            RegistryCredentialLifecycleState.Fresh,
            policy.Evaluate(
                metadata with
                {
                    IssuedAt = Now.AddMinutes(10),
                    ExpiresAt = Now.AddHours(2),
                    RefreshBefore = Now.AddHours(1),
                }
            )
        );
        Assert.Equal(
            RegistryCredentialLifecycleState.Fresh,
            policy.Evaluate(
                policy.Create(ConfigurationScope.CiTemporary, expiresAt: null),
                ConfigurationScope.CiTemporary
            )
        );
    }

    [Fact]
    public void MetadataCodecPreservesUnrelatedAndUnknownLifecycleValues()
    {
        RegistryCredentialLifecycleMetadata metadata = new RegistryCredentialExpiryPolicy(
            new MutableTimeProvider(Now)
        ).Create(ConfigurationScope.User, Now.AddHours(1));
        Dictionary<string, string> values = new(
            RegistryCredentialLifecycleMetadataCodec.Write(
                new Dictionary<string, string> { ["benign"] = "preserved" },
                metadata
            )
        );

        Assert.True(
            RegistryCredentialLifecycleMetadataCodec.TryRead(
                values,
                out RegistryCredentialLifecycleMetadata? parsed
            )
        );
        Assert.Equal(metadata, parsed);
        Assert.Equal("preserved", values["benign"]);

        values["hcoona.azureAuthCredProvider.registryCredential.unknown"] = "value";
        Assert.True(RegistryCredentialLifecycleMetadataCodec.TryRead(values, out _));
    }

    [Fact]
    public async Task ConfigureNoOpRefreshAndSharedNpmOwnershipAcquireAsExpected()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var acquisition = new CountingAcquisitionService();
        ConfigurationPhase14VerticalSliceService service = CreateService(fileSystem, acquisition);

        await service.DryRunConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(0, acquisition.Count);

        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        ConfigurationPhase14PlanResult pnpmNoOp = await service.ConfigureAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(1, acquisition.Count);
        Assert.Equal(0, pnpmNoOp.AppliedChangeCount);
        Assert.Equal(service.Paths.NpmUserNpmrcPath, service.Paths.PnpmUserNpmrcPath);

        await service.RefreshAsync(
            CredentialEcosystem.Pnpm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.Equal(2, acquisition.Count);

        string manifestPath = GetManifestPath(service, "npm-compatible-user");
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.True(RegistryCredentialLifecycleMetadataCodec.TryRead(manifest.SafeMetadata, out _));

        ConfigurationPhase14PlanResult removed = await service.UnconfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.False(removed.OwnershipManifestPresent);
    }

    [Fact]
    public async Task PathChangeRemovesOldOwnedSelectorAndPreservesUnrelatedContent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var acquisition = new CountingAcquisitionService();
        ConfigurationPhase14VerticalSliceService first = CreateService(
            fileSystem,
            acquisition,
            npmrcPath: "/home/test/first.npmrc"
        );
        await first.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        fileSystem.WriteAllText(
            first.Paths.NpmUserNpmrcPath,
            fileSystem.ReadAllText(first.Paths.NpmUserNpmrcPath) + "audit=false\n"
        );
        ConfigurationPhase14VerticalSliceService second = CreateService(
            fileSystem,
            acquisition,
            npmrcPath: "/home/test/second.npmrc"
        );

        await second.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("audit=false\n", fileSystem.ReadAllText(first.Paths.NpmUserNpmrcPath));
        Assert.Contains(
            "test-token-not-for-output",
            fileSystem.ReadAllText(second.Paths.NpmUserNpmrcPath),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task RefreshReconcilesMalformedLifecycleForRecognizedOwnership()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            new CountingAcquisitionService()
        );
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string manifestPath = GetManifestPath(service, "npm-compatible-user");
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        manifest = manifest with
        {
            SafeMetadata = new Dictionary<string, string>(manifest.SafeMetadata)
            {
                ["hcoona.azureAuthCredProvider.registryCredential.issuedAtUtc"] = "not-a-timestamp",
            },
        };
        string malformedJson = ConfigurationOwnershipManifestSerializer.Serialize(manifest);
        fileSystem.WriteAllText(manifestPath, malformedJson);

        ConfigurationPhase14PlanResult refreshed = await service.RefreshAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        Assert.NotEqual(ConfigurationPlanOperation.DryRun, refreshed.PlanResult.Operation);

        ConfigurationOwnershipManifest reconciled =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        Assert.True(
            RegistryCredentialLifecycleMetadataCodec.TryRead(
                reconciled.SafeMetadata,
                out RegistryCredentialLifecycleMetadata? lifecycle
            )
        );
        Assert.NotNull(lifecycle);
        Assert.DoesNotContain("not-a-timestamp", fileSystem.ReadAllText(manifestPath));
    }

    [Fact]
    public async Task YarnManifestRequiresAlwaysAuthForItsExactRegistry()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService service = CreateService(
            fileSystem,
            new CountingAcquisitionService()
        );
        await service.ConfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );
        string manifestPath = GetManifestPath(service, "yarn-user");
        ConfigurationOwnershipManifest manifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(manifestPath)
            );
        manifest = manifest with
        {
            Entries = manifest
                .Entries.Select(entry =>
                    entry.Key.EndsWith(".npmAlwaysAuth", StringComparison.Ordinal)
                        ? entry with
                        {
                            Key =
                                "npmRegistries.https://pkgs.dev.azure.com/other/"
                                + "_packaging/feed/npm/registry/.npmAlwaysAuth",
                        }
                        : entry
                )
                .ToArray(),
        };
        string unrecognizedJson = ConfigurationOwnershipManifestSerializer.Serialize(manifest);
        fileSystem.WriteAllText(manifestPath, unrecognizedJson);
        string yarnBefore = fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath);

        ConfigurationPhase14PlanResult result = await service.UnconfigureAsync(
            CredentialEcosystem.Yarn,
            ConfigurationPhase14Scope.User,
            TestContext.Current.CancellationToken
        );

        Assert.True(result.OwnershipManifestCleanupIncomplete);
        Assert.Equal(unrecognizedJson, fileSystem.ReadAllText(manifestPath));
        Assert.Equal(yarnBefore, fileSystem.ReadAllText(service.Paths.YarnUserYarnrcPath));
    }

    [Theory]
    [InlineData(ConfigurationScope.User, RegistryCredentialLifecycleState.RefreshRecommended)]
    [InlineData(ConfigurationScope.CiTemporary, RegistryCredentialLifecycleState.Fresh)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1707:Identifiers should not contain underscores",
        Justification = "The exact regression test name is part of the Phase 1 plan."
    )]
    public void CreateAndEvaluate_UnknownExpiry_UsesScopeSpecificStateWithoutFabricatedTimestamps(
        ConfigurationScope scope,
        RegistryCredentialLifecycleState expectedState
    )
    {
        var policy = new RegistryCredentialExpiryPolicy(new MutableTimeProvider(Now));

        RegistryCredentialLifecycleMetadata metadata = policy.Create(scope, expiresAt: null);
        RegistryCredentialLifecycleState state = policy.Evaluate(metadata, scope);

        Assert.Equal(Now, metadata.IssuedAt);
        Assert.Null(metadata.ExpiresAt);
        Assert.Null(metadata.RefreshBefore);
        Assert.Equal(expectedState, state);
    }

    private static ConfigurationPhase14VerticalSliceService CreateService(
        InMemoryFileSystem fileSystem,
        ICredentialAcquisitionService acquisition,
        string npmrcPath = "/home/test/.npmrc"
    ) =>
        new(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/wp7",
                EnvironmentVariableReader = name =>
                    name switch
                    {
                        "NPM_CONFIG_USERCONFIG" => npmrcPath,
                        "HOME" => "/home/test",
                        _ => null,
                    },
                TimeProvider = new MutableTimeProvider(Now),
                RegistryUrls = new Dictionary<CredentialEcosystem, Uri>
                {
                    [CredentialEcosystem.Npm] = new(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"
                    ),
                    [CredentialEcosystem.Pnpm] = new(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"
                    ),
                    [CredentialEcosystem.Yarn] = new(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"
                    ),
                },
                CredentialAcquisition = new BoundedCredentialAcquisitionAdapter(acquisition),
            }
        );

    private static string GetManifestPath(
        ConfigurationPhase14VerticalSliceService service,
        string prefix
    ) => Path.Combine(service.Paths.ManifestDirectoryPath, prefix + "-ownership-manifest.json");

    private sealed class CountingAcquisitionService : ICredentialAcquisitionService
    {
        public int Count { get; private set; }

        public ValueTask<CredentialResult> AcquireAsync(
            CredentialRequestV2 request,
            CancellationToken cancellationToken = default
        )
        {
            Count++;
            return ValueTask.FromResult(
                new CredentialResult
                {
                    Status = CredentialResultStatus.Success,
                    BearerToken = "test-token-not-for-output",
                    ExpiresAt = Now.AddHours(1),
                    DiagnosticsCorrelationId = "wp7-test",
                }
            );
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
