using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzurePipelines;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;
using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

[Collection("ConfigurationManagerExecution")]
public sealed class AzurePipelinesSystemAccessTokenWp5Tests
{
    private const string Secret = "opaque-system-access-token";

    [Theory]
    [InlineData(null, AzurePipelinesSystemAccessTokenResultStatus.CredentialUnavailable)]
    [InlineData("", AzurePipelinesSystemAccessTokenResultStatus.CredentialUnavailable)]
    [InlineData("   ", AzurePipelinesSystemAccessTokenResultStatus.CredentialUnavailable)]
    [InlineData("token\ninjection", AzurePipelinesSystemAccessTokenResultStatus.InvalidToken)]
    [InlineData("token\u0000injection", AzurePipelinesSystemAccessTokenResultStatus.InvalidToken)]
    public void TokenValidationReturnsStableSecretFreeFailures(
        string? value,
        AzurePipelinesSystemAccessTokenResultStatus expectedStatus)
    {
        AzurePipelinesSystemAccessTokenResult result =
            AzurePipelinesSystemAccessTokenService.Handle(CreateV1Request(
                CredentialEcosystem.Git), value);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        if (!string.IsNullOrEmpty(value))
        {
            Assert.DoesNotContain(value, result.SafeMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TokenValidationRejectsOverlongInputWithoutEchoingIt()
    {
        string overlong = new('x', AzurePipelinesSystemAccessToken.MaximumLength + 1);

        AzurePipelinesSystemAccessTokenResult result =
            AzurePipelinesSystemAccessTokenService.Handle(
                CreateV1Request(CredentialEcosystem.Git),
                overlong);

        Assert.Equal(AzurePipelinesSystemAccessTokenResultStatus.InvalidToken, result.Status);
        Assert.DoesNotContain(overlong, result.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CredentialEcosystem.Git, null, true)]
    [InlineData(CredentialEcosystem.Npm, null, true)]
    [InlineData(CredentialEcosystem.Pnpm, null, true)]
    [InlineData(CredentialEcosystem.Yarn, null, true)]
    public void ProtocolMatrixMaterializesOnlyEvidenceBackedForm(
        CredentialEcosystem ecosystem,
        string? expectedUsername,
        bool bearer)
    {
        AzurePipelinesSystemAccessTokenResult result =
            AzurePipelinesSystemAccessTokenService.Handle(CreateV1Request(ecosystem), Secret);

        Assert.True(result.Succeeded);
        Assert.Equal(expectedUsername, result.Username);
        Assert.Equal(
            bearer ? Secret : null,
            result.BearerToken?.Value);
        Assert.Equal(
            bearer ? null : Secret,
            result.Password?.Value);
        Assert.Equal(
            AzurePipelinesCredentialLifetime.JobScopedUnknownExpiry,
            result.Lifetime);

        CredentialResult protocolResult = result.CreateProtocolResult("wp5-test");
        Assert.Null(protocolResult.ExpiresAt);
        Assert.Null(protocolResult.Account);
        Assert.Null(protocolResult.Tenant);
        Assert.Null(protocolResult.CacheKey);
    }

    [Theory]
    [MemberData(nameof(EcosystemAndFormCases))]
    public void EveryEcosystemAndFormFailsClosedUnlessExplicitlyMapped(
        CredentialEcosystem ecosystem,
        CredentialKind kind,
        bool expectedSuccess)
    {
        CredentialRequest request = CreateV1Request(ecosystem) with
        {
            CredentialKind = kind,
        };

        AzurePipelinesSystemAccessTokenResult result =
            AzurePipelinesSystemAccessTokenService.Handle(request, Secret);

        Assert.Equal(expectedSuccess, result.Succeeded);
        if (!expectedSuccess)
        {
            Assert.Null(result.Password);
            Assert.Null(result.BearerToken);
        }
    }

    public static TheoryData<CredentialEcosystem, CredentialKind, bool> EcosystemAndFormCases
    {
        get
        {
            var data = new TheoryData<CredentialEcosystem, CredentialKind, bool>();
            foreach (CredentialEcosystem ecosystem in new[]
                     {
                         CredentialEcosystem.Git,
                         CredentialEcosystem.NuGet,
                         CredentialEcosystem.Python,
                         CredentialEcosystem.Npm,
                         CredentialEcosystem.Pnpm,
                         CredentialEcosystem.Yarn,
                     })
            {
                foreach (CredentialKind kind in new[]
                         {
                             CredentialKind.BasicPassword,
                             CredentialKind.BearerToken,
                             CredentialKind.NpmAuthToken,
                             CredentialKind.NuGetPluginCredential,
                             CredentialKind.PatCompatibility,
                         })
                {
                    bool expectedSuccess = (ecosystem, kind) is
                        (CredentialEcosystem.Git, CredentialKind.BearerToken)
                        or (
                            CredentialEcosystem.Npm
                                or CredentialEcosystem.Pnpm
                                or CredentialEcosystem.Yarn,
                            CredentialKind.NpmAuthToken
                        );
                    data.Add(ecosystem, kind, expectedSuccess);
                }
            }

            return data;
        }
    }

    [Fact]
    public void GitBearerProjectionUsesFrozenCredentialHelperBasicAdaptation()
    {
        CredentialResult protocolResult = AzurePipelinesSystemAccessTokenService
            .Handle(CreateV1Request(CredentialEcosystem.Git), Secret)
            .CreateProtocolResult("wp5-test");

        Assert.True(
            AdapterHostResultMapper.TryMapGitCredentialHelperBasicMaterial(
                protocolResult,
                out string? username,
                out string? password));
        Assert.Equal("AzureDevOps", username);
        Assert.Equal(Secret, password);
    }

    [Theory]
    [MemberData(nameof(InvalidRequestCases))]
    public void RequestPolicyRejectsInvalidContextWithoutCredentialOutput(
        CredentialRequest request)
    {
        AzurePipelinesSystemAccessTokenResult result =
            AzurePipelinesSystemAccessTokenService.Handle(request, Secret);

        Assert.False(result.Succeeded);
        Assert.Null(result.Password);
        Assert.Null(result.BearerToken);
        Assert.DoesNotContain(Secret, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.SafeMessage, StringComparison.Ordinal);
    }

    public static TheoryData<CredentialRequest> InvalidRequestCases
    {
        get
        {
            CredentialRequest valid = CreateV1Request(CredentialEcosystem.Git);
            return new TheoryData<CredentialRequest>
            {
                valid with { Operation = CredentialOperation.Configure },
                valid with { IdentityFlow = IdentityFlow.InteractiveBrowser },
                valid with { InteractivePolicy = InteractivePolicy.UserAllowed },
                valid with { CachePolicy = CachePolicyMode.NoCache },
                valid with { AccountHint = "account" },
                valid with { TenantHint = "tenant" },
                valid with { CiContext = null },
                valid with
                {
                    CiContext = valid.CiContext! with { ExplicitCiMode = false },
                },
                valid with
                {
                    CiContext = valid.CiContext! with { Provider = "Other" },
                },
                valid with
                {
                    CiContext = valid.CiContext! with
                    {
                        HasAzurePipelinesSystemAccessToken = false,
                    },
                },
                valid with
                {
                    CiContext = valid.CiContext! with { AllowsPersistentWrites = true },
                },
                valid with { CredentialKind = CredentialKind.BasicPassword },
                valid with { CredentialKind = CredentialKind.PatCompatibility },
            };
        }
    }

    [Theory]
    [InlineData(AcquisitionMode.SilentOnly)]
    [InlineData(AcquisitionMode.InteractionAllowed)]
    public void V2RejectsAmbiguousOrInteractiveAcquisitionModes(AcquisitionMode mode)
    {
        CredentialRequestV2 request = CreateV2Request(CredentialEcosystem.Git) with
        {
            AcquisitionMode = mode,
        };

        AzurePipelinesSystemAccessTokenResult result =
            AzurePipelinesSystemAccessTokenService.Handle(request, Secret);

        Assert.Equal(AzurePipelinesSystemAccessTokenResultStatus.InvalidRequest, result.Status);
        Assert.Null(result.BearerToken);
    }

    [Fact]
    public void V1AndV2UnspecifiedUseSameOpaqueSemanticsWithoutChangingV1Wire()
    {
        CredentialRequest v1 = CreateV1Request(CredentialEcosystem.Git);
        CredentialRequestV2 v2 = CreateV2Request(CredentialEcosystem.Git);

        AzurePipelinesSystemAccessTokenResult v1Result =
            AzurePipelinesSystemAccessTokenService.Handle(v1, Secret);
        AzurePipelinesSystemAccessTokenResult v2Result =
            AzurePipelinesSystemAccessTokenService.Handle(v2, Secret);
        string v1Json = JsonSerializer.Serialize(v1, ContractJson.CreateSerializerOptions());

        Assert.True(v1Result.Succeeded);
        Assert.True(v2Result.Succeeded);
        Assert.Equal(v1Result.BearerToken?.Value, v2Result.BearerToken?.Value);
        Assert.DoesNotContain("acquisitionMode", v1Json, StringComparison.Ordinal);
        Assert.Contains(
            "\"identityFlow\":\"azurePipelinesSystemAccessToken\"",
            v1Json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SecretBearingTypesAndProtocolProjectionRedactAndNeverInventIdentity()
    {
        Assert.True(
            AzurePipelinesSystemAccessToken.TryCreate(Secret, out var token, out _));
        AzurePipelinesSystemAccessTokenResult result =
            AzurePipelinesSystemAccessTokenService.Handle(
                CreateV1Request(CredentialEcosystem.Git),
                Secret);
        CredentialResult projection = result.CreateProtocolResult("wp5-test");

        Assert.DoesNotContain(Secret, token!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, projection.ToString(), StringComparison.Ordinal);
        Assert.Null(projection.Account);
        Assert.Null(projection.Tenant);
        Assert.Null(projection.CacheKey);
        Assert.Null(projection.ExpiresAt);
    }

    [Fact]
    public async Task ConcurrentCallsUseOnlyTheirCallerProvidedToken()
    {
        Task<AzurePipelinesSystemAccessTokenResult>[] calls = Enumerable
            .Range(0, 16)
            .Select(index => Task.Run(
                () => AzurePipelinesSystemAccessTokenService.Handle(
                    CreateV1Request(CredentialEcosystem.Npm),
                    "caller-token-" + index)))
            .ToArray();

        AzurePipelinesSystemAccessTokenResult[] results = await Task.WhenAll(calls);

        Assert.Equal(
            Enumerable.Range(0, 16).Select(index => "caller-token-" + index),
            results.Select(result => result.BearerToken?.Value));
    }

    [Fact]
    public async Task CiTemporaryPlansBypassIdentityProviderAndMarkOnlyTokenValuesSecret()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var identityProvider = new ThrowingIdentityProvider();
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/wp5",
                AzurePipelinesJobScopeId = "job-a",
                CredentialCoreService = new CredentialCoreService(identityProvider),
                RegistryUrls = CreateTestRegistryUrls(),
                EnvironmentVariableReader = name =>
                    name == AuthPhase14VerticalSliceService
                        .AzurePipelinesSystemAccessTokenVariable
                        ? Secret
                        : null,
            });

        foreach (CredentialEcosystem ecosystem in new[]
                 {
                     CredentialEcosystem.Npm,
                     CredentialEcosystem.Pnpm,
                     CredentialEcosystem.Yarn,
                 })
        {
            ConfigurationPhase14PlanResult result = await service.ConfigureAsync(
                ecosystem,
                ConfigurationPhase14Scope.CiTemporary,
                TestContext.Current.CancellationToken);

            Assert.Equal(ConfigurationScope.CiTemporary, result.PlanResult.Plan.Scope);
            Assert.True(result.PlanResult.Plan.ContainsCredentialMaterial);
            Assert.Null(result.PlanResult.Plan.ExpiresAt);
            Assert.NotNull(result.PlanResult.Plan.TemporaryContainer);
            ConfigurationPlannedChange secretChange = Assert.Single(
                result.PlanResult.Changes,
                change => change.IsSecretValue);
            Assert.True(secretChange.HasPlannedValue);
            Assert.Null(secretChange.PlannedValueSha256);
            Assert.DoesNotContain(
                Secret,
                result.PlanResult.ToString(),
                StringComparison.Ordinal);

            string manifestPath = Path.Combine(
                service.Paths.CiTemporaryManifestDirectoryPath,
                ecosystem.ToString().ToLowerInvariant()
                    + "-ci-temporary-ownership-manifest.json");
            Assert.DoesNotContain(
                Secret,
                fileSystem.ReadAllText(manifestPath),
                StringComparison.Ordinal);
        }

        Assert.Equal(0, identityProvider.CallCount);
    }

    [Fact]
    public async Task CiTemporaryCleanupRemovesGeneratedTokenBearingStateAndOwnership()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/wp5-cleanup",
                AzurePipelinesJobScopeId = "job-cleanup",
                RegistryUrls = CreateTestRegistryUrls(),
                EnvironmentVariableReader = name =>
                    name == AuthPhase14VerticalSliceService
                        .AzurePipelinesSystemAccessTokenVariable
                        ? Secret
                        : null,
            });
        await service.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken);

        ConfigurationPhase14CleanupResult cleanup = await service.CleanupAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken);

        ConfigurationPhase14CleanupEcosystemResult ecosystem = Assert.Single(cleanup.Ecosystems);
        Assert.Equal("removed", ecosystem.State);
        Assert.False(ecosystem.OwnershipManifestPresent);
        Assert.False(ecosystem.TemporaryContainerPresent);
        Assert.False(fileSystem.FileExists(service.Paths.NpmCiTemporaryNpmrcPath));
    }

    [Fact]
    public async Task LogoutLifecycleRemovesAllGeneratedCiTemporaryState()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/wp5-logout",
                AzurePipelinesJobScopeId = "job-logout",
                RegistryUrls = CreateTestRegistryUrls(),
                EnvironmentVariableReader = name =>
                    name == AuthPhase14VerticalSliceService
                        .AzurePipelinesSystemAccessTokenVariable
                        ? Secret
                        : null,
            });
        foreach (CredentialEcosystem ecosystem in new[]
                 {
                     CredentialEcosystem.Npm,
                     CredentialEcosystem.Pnpm,
                     CredentialEcosystem.Yarn,
                 })
        {
            await service.ConfigureAsync(
                ecosystem,
                ConfigurationPhase14Scope.CiTemporary,
                TestContext.Current.CancellationToken);
        }

        ConfigurationPhase14CleanupResult logout = await service.LogoutAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(3, logout.Ecosystems.Count);
        Assert.All(logout.Ecosystems, result =>
        {
            Assert.Equal("removed", result.State);
            Assert.False(result.OwnershipManifestPresent);
            Assert.False(result.TemporaryContainerPresent);
        });
    }

    [Fact]
    public void PatCompatibilityIsDeferredWithoutFallbackOrMaterialization()
    {
        CredentialRequest request = CreateV1Request(CredentialEcosystem.Git) with
        {
            IdentityFlow = IdentityFlow.PatCompatibility,
            CredentialKind = CredentialKind.PatCompatibility,
            InteractivePolicy = InteractivePolicy.Never,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = null,
        };

        PatCompatibilityPolicyDecision policy = PatCompatibilityPolicy.Evaluate(request);
        AzurePipelinesSystemAccessTokenResult ciResult =
            AzurePipelinesSystemAccessTokenService.Handle(request, Secret);

        Assert.Equal(IdentityFlowState.Deferred, policy.State);
        Assert.Equal("PatCompatibilityDeferred", policy.Code);
        Assert.False(ciResult.Succeeded);
        Assert.Null(ciResult.Password);
        Assert.Null(ciResult.BearerToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../other-job")]
    [InlineData("job/other")]
    [InlineData("job\\other")]
    public async Task CiTemporaryMaterializationRejectsMissingOrInvalidJobScopeBeforeWriting(
        string? jobScopeId)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var service = new ConfigurationPhase14VerticalSliceService(
            new ConfigurationPhase14VerticalSliceOptions
            {
                FileSystem = fileSystem,
                StateDirectoryPath = "/state/wp5-job-validation",
                AzurePipelinesJobScopeId = jobScopeId,
                RegistryUrls = CreateTestRegistryUrls(),
                EnvironmentVariableReader = name =>
                    name == AuthPhase14VerticalSliceService
                        .AzurePipelinesSystemAccessTokenVariable
                        ? Secret
                        : null,
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.ConfigureAsync(
                CredentialEcosystem.Npm,
                ConfigurationPhase14Scope.CiTemporary,
                TestContext.Current.CancellationToken));

        Assert.False(fileSystem.DirectoryExists("/state/wp5-job-validation"));
    }

    [Fact]
    public async Task CiTemporaryJobsUseIndependentRootsAndCleanupOnlyCurrentJob()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        ConfigurationPhase14VerticalSliceService jobA = CreateJobService(fileSystem, "job-a");
        ConfigurationPhase14VerticalSliceService jobB = CreateJobService(fileSystem, "job-b");

        await jobA.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken);
        await jobB.ConfigureAsync(
            CredentialEcosystem.Npm,
            ConfigurationPhase14Scope.CiTemporary,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(jobA.Paths.CiTemporaryRootPath, jobB.Paths.CiTemporaryRootPath);
        await jobA.LogoutAsync(TestContext.Current.CancellationToken);

        Assert.False(fileSystem.FileExists(jobA.Paths.NpmCiTemporaryNpmrcPath));
        Assert.True(fileSystem.FileExists(jobB.Paths.NpmCiTemporaryNpmrcPath));
        Assert.StartsWith(
            "/product-temp/azureauth-credprovider/ci-jobs/job-b",
            jobB.Paths.NpmCiTemporaryNpmrcPath,
            StringComparison.Ordinal);
    }

    private static ConfigurationPhase14VerticalSliceService CreateJobService(
        InMemoryFileSystem fileSystem,
        string jobScopeId) =>
        new(new ConfigurationPhase14VerticalSliceOptions
        {
            FileSystem = fileSystem,
            StateDirectoryPath = "/state/wp5-isolation",
            CiTemporaryProductRootPath = "/product-temp/azureauth-credprovider/ci-jobs",
            AzurePipelinesJobScopeId = jobScopeId,
            RegistryUrls = CreateTestRegistryUrls(),
            EnvironmentVariableReader = name =>
                name == AuthPhase14VerticalSliceService
                    .AzurePipelinesSystemAccessTokenVariable
                    ? Secret
                    : null,
        });

    private static Dictionary<CredentialEcosystem, Uri> CreateTestRegistryUrls() =>
        new()
        {
            [CredentialEcosystem.Npm] = new(
                "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"),
            [CredentialEcosystem.Pnpm] = new(
                "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"),
            [CredentialEcosystem.Yarn] = new(
                "https://pkgs.dev.azure.com/test-org/_packaging/test-feed/npm/registry/"),
        };

    private static CredentialRequest CreateV1Request(CredentialEcosystem ecosystem)
    {
        bool git = ecosystem == CredentialEcosystem.Git;
        Uri endpoint = ecosystem switch
        {
            CredentialEcosystem.Git => new("https://dev.azure.com/org/project/_git/repo"),
            CredentialEcosystem.NuGet =>
                new("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
            CredentialEcosystem.Python =>
                new("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            _ => new("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"),
        };
        CredentialKind kind = ecosystem switch
        {
            CredentialEcosystem.Git => CredentialKind.BearerToken,
            CredentialEcosystem.NuGet => CredentialKind.NuGetPluginCredential,
            CredentialEcosystem.Python => CredentialKind.BasicPassword,
            _ => CredentialKind.NpmAuthToken,
        };
        return new CredentialRequest
        {
            Ecosystem = ecosystem,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                endpoint.Host,
                "org",
                endpoint,
                project: git ? "project" : null,
                feed: git ? null : "feed",
                repository: git ? "repo" : null),
            ServiceIdentity = "default",
            RequestedAudience = git ? TokenAudience.AzureDevOps : TokenAudience.AzureArtifacts,
            CredentialKind = kind,
            IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
            InteractivePolicy = InteractivePolicy.Never,
            CachePolicy = CachePolicyMode.NonPersistentCi,
            CiContext = new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
            },
        };
    }

    private static CredentialRequestV2 CreateV2Request(CredentialEcosystem ecosystem)
    {
        CredentialRequest request = CreateV1Request(ecosystem);
        return new CredentialRequestV2
        {
            Ecosystem = request.Ecosystem,
            Operation = request.Operation,
            Resource = request.Resource,
            ServiceIdentity = request.ServiceIdentity,
            AccountHint = request.AccountHint,
            TenantHint = request.TenantHint,
            RequestedAudience = request.RequestedAudience,
            CredentialKind = request.CredentialKind,
            IdentityFlow = request.IdentityFlow,
            InteractivePolicy = request.InteractivePolicy,
            AcquisitionMode = AcquisitionMode.Unspecified,
            CachePolicy = request.CachePolicy,
            CiContext = request.CiContext,
        };
    }

    private sealed class ThrowingIdentityProvider : IIdentityProvider
    {
        public int CallCount { get; private set; }

        public IdentityMaterial GetIdentity(CredentialRequest request)
        {
            CallCount++;
            throw new InvalidOperationException("Identity provider must not be called.");
        }
    }
}
