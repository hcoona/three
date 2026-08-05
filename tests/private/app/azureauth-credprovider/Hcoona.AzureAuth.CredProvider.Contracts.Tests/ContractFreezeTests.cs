using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Contracts.Tests;

public sealed class ContractFreezeTests
{
    [Fact]
    public void CredentialRequestCarriesAcceptedMvpIdentityAndNonPersistentCachePolicy()
    {
        var request = CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword);

        Assert.Equal(ContractVersions.CredentialContractMajor, request.ContractMajor);
        Assert.Equal(
            IdentityFlowState.AcceptedMvp,
            IdentityFlowPolicy.GetMvpState(request.IdentityFlow)
        );
        Assert.Equal(CachePolicyMode.ProductPersistentCacheDisabled, request.CachePolicy);
        Assert.False(request.CiContext?.AllowsPersistentWrites);
    }

    [Theory]
    [InlineData(IdentityFlow.ServicePrincipal)]
    [InlineData(IdentityFlow.ManagedIdentity)]
    [InlineData(IdentityFlow.WorkloadIdentityFederation)]
    public void DeferredFlowsStayRepresentableWithoutBeingMvpSupported(IdentityFlow flow)
    {
        var request = CreateRequest(flow, CredentialKind.BasicPassword);

        Assert.Equal(IdentityFlowState.Deferred, IdentityFlowPolicy.GetMvpState(flow));
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "user@example.com", "tenant-1")
        );
    }

    [Fact]
    public void PatCompatibilityRequiresExplicitFlowAndCredentialKind()
    {
        var entraRequest = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        );
        var patRequest = CreateRequest(
            IdentityFlow.PatCompatibility,
            CredentialKind.PatCompatibility
        );

        Assert.False(IdentityFlowPolicy.CanUsePatCompatibility(entraRequest));
        Assert.True(IdentityFlowPolicy.CanUsePatCompatibility(patRequest));
        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(patRequest));
    }

    [Fact]
    public void GitRequestsAcceptBearerTokenCredentialKindForMvp()
    {
        var request = CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BearerToken);

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.True(
            CacheKeySchema.IsValid(CacheKeySchema.Create(request, "user@example.com", "tenant-1"))
        );
    }

    [Fact]
    public void CacheKeySchemaIncludesDefaultGitPartitionDimensions()
    {
        var request = CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword);
        CacheKey cacheKey = CacheKeySchema.Create(
            request,
            account: "User@Example.COM",
            tenant: "Tenant-1"
        );

        Assert.Equal(ContractVersions.CacheKeySchemaMajor, cacheKey.SchemaMajor);
        Assert.Equal(
            "azdo-cache-v1|Z2l0|ZGV2LmF6dXJlLmNvbQ==|b3Jn|-|-|-"
                + "|ZGVmYXVsdA==|dXNlckBleGFtcGxlLmNvbQ==|dGVuYW50LTE=|YXp1cmVkZXZvcHM=|YmFzaWNwYXN"
                + "zd29yZA==",
            cacheKey.Value
        );
        Assert.DoesNotContain("password", cacheKey.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitRepoScopedAzureReposResourceIsAcceptedButUsesDefaultOrganizationCachePartition()
    {
        var orgOnlyRequest = CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword);
        var repoScopedRequest = orgOnlyRequest with
        {
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org/project/_git/repo"),
                project: "project",
                repository: "repo"
            ),
        };

        CacheKey orgOnlyCacheKey = CacheKeySchema.Create(
            orgOnlyRequest,
            "user@example.com",
            "tenant-1"
        );
        CacheKey repoScopedCacheKey = CacheKeySchema.Create(
            repoScopedRequest,
            "user@example.com",
            "tenant-1"
        );

        Assert.True(CanonicalResourceIdentityPolicy.IsValid(repoScopedRequest.Resource));
        Assert.True(
            CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                repoScopedRequest.Resource.ServiceEndpoint,
                CredentialEcosystem.Git
            )
        );
        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(repoScopedRequest));
        Assert.Equal(orgOnlyCacheKey.Value, repoScopedCacheKey.Value);
        Assert.Contains("|-|-|-|", repoScopedCacheKey.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pkgs.dev.azure.com", "https://pkgs.dev.azure.com/org")]
    [InlineData("org.pkgs.visualstudio.com", "https://org.pkgs.visualstudio.com")]
    public void GitRequestsRejectAzureArtifactsPackageHosts(string host, string serviceEndpoint)
    {
        var request = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        ) with
        {
            Resource = CanonicalResourceIdentity.Create(host, "org", new Uri(serviceEndpoint)),
        };

        Assert.True(CanonicalResourceIdentityPolicy.IsValid(request.Resource));
        Assert.False(
            CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                request.Resource.ServiceEndpoint,
                CredentialEcosystem.Git
            )
        );
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "user@example.com", "tenant-1")
        );
    }

    [Theory]
    [InlineData("pkgs.dev.azure.com")]
    [InlineData("org.pkgs.visualstudio.com")]
    public void GitCacheKeysRejectAzureArtifactsPackageHosts(string host)
    {
        CacheKey valid = CacheKeySchema.Create(
            CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword),
            "user@example.com",
            "tenant-1"
        );
        string[] parts = valid.Value.Split('|');
        parts[2] = EncodeCacheKeyPart(host);
        var invalidCacheKey = new CacheKey { Value = string.Join('|', parts) };

        Assert.False(CacheKeySchema.IsValid(invalidCacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(invalidCacheKey));

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                Username = "AzureDevOps",
                Password = "generated-password",
                CacheKey = invalidCacheKey,
                DiagnosticsCorrelationId = "corr-git-package-host-cache-key",
            }
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedCacheKeySchemaMajor", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [InlineData(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential)]
    [InlineData(CredentialEcosystem.Python, CredentialKind.BasicPassword)]
    [InlineData(CredentialEcosystem.Npm, CredentialKind.NpmAuthToken)]
    [InlineData(CredentialEcosystem.Pnpm, CredentialKind.NpmAuthToken)]
    [InlineData(CredentialEcosystem.Yarn, CredentialKind.NpmAuthToken)]
    public void PackageEcosystemRequestsRequireFeedScopedAzureArtifactsResources(
        CredentialEcosystem ecosystem,
        CredentialKind credentialKind
    )
    {
        CredentialRequest validPackageRequest = CreatePackageRequest(ecosystem, credentialKind);
        CanonicalResourceIdentity orgOnlyResource = CanonicalResourceIdentity.Create(
            "dev.azure.com",
            "org",
            new Uri("https://dev.azure.com/org")
        );
        CanonicalResourceIdentity gitResource = CreateRequest(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword
        ).Resource;
        var noFeedPackageResource = new CanonicalResourceIdentity
        {
            AzureDevOpsHost = "pkgs.dev.azure.com",
            Organization = "org",
            ServiceEndpoint = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
        };

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(validPackageRequest));
        Assert.NotNull(CacheKeySchema.Create(validPackageRequest, "user@example.com", "tenant-1"));

        CredentialRequest[] invalidRequests =
        [
            validPackageRequest with
            {
                Resource = orgOnlyResource,
            },
            validPackageRequest with
            {
                Resource = gitResource,
            },
            validPackageRequest with
            {
                Resource = noFeedPackageResource,
            },
            validPackageRequest with
            {
                RequestedAudience = TokenAudience.AzureDevOps,
            },
            validPackageRequest with
            {
                CredentialKind = CredentialKind.BearerToken,
            },
        ];

        Assert.All(
            invalidRequests,
            request =>
            {
                Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
                Assert.Throws<ArgumentException>(() =>
                    CacheKeySchema.Create(request, "user@example.com", "tenant-1")
                );
            }
        );
    }

    [Theory]
    [InlineData(
        CredentialEcosystem.NuGet,
        CredentialKind.NuGetPluginCredential,
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple"
    )]
    [InlineData(
        CredentialEcosystem.NuGet,
        CredentialKind.NuGetPluginCredential,
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/upload"
    )]
    [InlineData(
        CredentialEcosystem.NuGet,
        CredentialKind.NuGetPluginCredential,
        "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
    )]
    [InlineData(
        CredentialEcosystem.Python,
        CredentialKind.BasicPassword,
        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
    )]
    [InlineData(
        CredentialEcosystem.Python,
        CredentialKind.BasicPassword,
        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"
    )]
    [InlineData(
        CredentialEcosystem.Npm,
        CredentialKind.NpmAuthToken,
        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
    )]
    [InlineData(
        CredentialEcosystem.Npm,
        CredentialKind.NpmAuthToken,
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple"
    )]
    [InlineData(
        CredentialEcosystem.Npm,
        CredentialKind.NpmAuthToken,
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/upload"
    )]
    [InlineData(
        CredentialEcosystem.Pnpm,
        CredentialKind.NpmAuthToken,
        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
    )]
    [InlineData(
        CredentialEcosystem.Yarn,
        CredentialKind.NpmAuthToken,
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple"
    )]
    public void PackageEcosystemRequestsRejectCrossEcosystemServiceEndpointSuffixes(
        CredentialEcosystem ecosystem,
        CredentialKind credentialKind,
        string serviceEndpoint
    )
    {
        CredentialRequest request = CreatePackageRequest(ecosystem, credentialKind) with
        {
            Resource = CanonicalResourceIdentity.Create(
                "pkgs.dev.azure.com",
                "org",
                new Uri(serviceEndpoint),
                feed: "feed"
            ),
        };

        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "user@example.com", "tenant-1")
        );
    }

    [Theory]
    [InlineData(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential)]
    [InlineData(CredentialEcosystem.Python, CredentialKind.BasicPassword)]
    [InlineData(CredentialEcosystem.Npm, CredentialKind.NpmAuthToken)]
    [InlineData(CredentialEcosystem.Pnpm, CredentialKind.NpmAuthToken)]
    [InlineData(CredentialEcosystem.Yarn, CredentialKind.NpmAuthToken)]
    public void CacheKeySchemaRejectsPackageEcosystemKeysWithoutFeedPartitions(
        CredentialEcosystem ecosystem,
        CredentialKind credentialKind
    )
    {
        CacheKey valid = CacheKeySchema.Create(
            CreatePackageRequest(ecosystem, credentialKind),
            "user@example.com",
            "tenant-1"
        );
        string[] parts = valid.Value.Split('|');
        parts[5] = "-";
        var missingFeed = new CacheKey { Value = string.Join('|', parts) };

        Assert.False(CacheKeySchema.IsValid(missingFeed));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(missingFeed));
    }

    [Theory]
    [MemberData(nameof(CacheKeyResourcePartitionsWithDecodedSeparators))]
    public void CacheKeySchemaRejectsDecodedSeparatorsInsideResourcePartitions(CacheKey cacheKey)
    {
        Assert.False(CacheKeySchema.IsValid(cacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(cacheKey));

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                Username = "AzureDevOps",
                Password = "generated-password",
                CacheKey = cacheKey,
                DiagnosticsCorrelationId = "corr-cache-key-separator",
            }
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedCacheKeySchemaMajor", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(CacheKeyResourcePartitionsWithControlCharacters))]
    public void CacheKeySchemaRejectsControlCharactersInsideResourcePartitions(CacheKey cacheKey)
    {
        string violation = Assert.IsType<string>(CacheKeySchema.GetViolation(cacheKey));

        Assert.Contains("control", violation, StringComparison.OrdinalIgnoreCase);
        Assert.False(CacheKeySchema.IsValid(cacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(cacheKey));

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                Username = "AzureDevOps",
                Password = "generated-password",
                CacheKey = cacheKey,
                DiagnosticsCorrelationId = "corr-cache-key-control-character",
            }
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedCacheKeySchemaMajor", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(CacheKeyAccountAndTenantPartitionsWithControlCharacters))]
    public void CacheKeySchemaRejectsControlCharactersInsideAccountAndTenantPartitions(
        CacheKey cacheKey
    )
    {
        string violation = Assert.IsType<string>(CacheKeySchema.GetViolation(cacheKey));

        Assert.Contains("control", violation, StringComparison.OrdinalIgnoreCase);
        Assert.False(CacheKeySchema.IsValid(cacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(cacheKey));

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                Username = "AzureDevOps",
                Password = "generated-password",
                CacheKey = cacheKey,
                DiagnosticsCorrelationId = "corr-cache-key-account-tenant-control-character",
            }
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedCacheKeySchemaMajor", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(NonCanonicalCacheKeyPartitionAliases))]
    public void CacheKeySchemaRejectsNonCanonicalDecodedPartitionAliases(
        string requestKind,
        int partitionIndex,
        string alias
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        CredentialRequest request =
            requestKind == "package"
                ? CreatePackageRequest(
                    CredentialEcosystem.NuGet,
                    CredentialKind.NuGetPluginCredential
                )
                : CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword);
        CacheKey valid = CacheKeySchema.Create(request, "user@example.com", "tenant-1");
        string[] parts = valid.Value.Split('|');
        parts[partitionIndex] = EncodeCacheKeyPart(alias);
        var invalidCacheKey = new CacheKey { Value = string.Join('|', parts) };
        string json = $$"""
            {
              "schemaMajor": 1,
              "value": {{JsonSerializer.Serialize(invalidCacheKey.Value, options)}}
            }
            """;
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "generated-password",
            CacheKey = invalidCacheKey,
            DiagnosticsCorrelationId = "corr-noncanonical-cache-key",
        };

        Assert.False(CacheKeySchema.IsValid(invalidCacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(invalidCacheKey));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<CacheKey>(json, options));
        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedCacheKeySchemaMajor", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(NonCanonicalCacheKeyEncodingAliases))]
    public void CacheKeySchemaRejectsNonCanonicalEncodedPartitionAliases(
        string account,
        int partitionIndex,
        string aliasKind
    )
    {
        CacheKey valid = CacheKeySchema.Create(
            CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword),
            account,
            "tenant-1"
        );
        string[] parts = valid.Value.Split('|');
        parts[partitionIndex] = aliasKind switch
        {
            "unpadded" => parts[partitionIndex].TrimEnd('='),
            "base64url" => parts[partitionIndex].Replace('+', '-'),
            _ => throw new ArgumentOutOfRangeException(nameof(aliasKind)),
        };
        var invalidCacheKey = new CacheKey { Value = string.Join('|', parts) };

        Assert.False(CacheKeySchema.IsValid(invalidCacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(invalidCacheKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void ServiceIdentityIsRequiredForAcceptedRequestsAndCacheKeyCreation(
        string? serviceIdentity
    )
    {
        var request = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        ) with
        {
            ServiceIdentity = serviceIdentity!,
        };

        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "user@example.com", "tenant-1")
        );
    }

    [Fact]
    public void ServiceIdentityMustBeCanonicalLowerCaseBeforeCacheKeyCreation()
    {
        var lowerCaseRequest = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        ) with
        {
            ServiceIdentity = "prodapp",
        };
        var mixedCaseRequest = lowerCaseRequest with { ServiceIdentity = "ProdApp" };

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(lowerCaseRequest));
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(mixedCaseRequest));
        CacheKey lowerCaseCacheKey = CacheKeySchema.Create(
            lowerCaseRequest,
            "user@example.com",
            "tenant-1"
        );
        string mixedCaseCacheKeyValue = lowerCaseCacheKey.Value.Replace(
            "|cHJvZGFwcA==|",
            "|UHJvZEFwcA==|",
            StringComparison.Ordinal
        );

        Assert.Contains("|cHJvZGFwcA==|", lowerCaseCacheKey.Value, StringComparison.Ordinal);
        Assert.False(CacheKeySchema.IsValid(new CacheKey { Value = mixedCaseCacheKeyValue }));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(mixedCaseRequest, "user@example.com", "tenant-1")
        );
    }

    [Theory]
    [InlineData("default\u001B")]
    [InlineData("default\u009F")]
    public void ServiceIdentityRejectsControlCharactersBeforeCacheKeyCreation(
        string serviceIdentity
    )
    {
        CredentialRequest request = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        ) with
        {
            ServiceIdentity = serviceIdentity,
        };

        Assert.False(ServiceIdentityContract.IsCanonical(serviceIdentity));
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "user@example.com", "tenant-1")
        );

        CacheKey validCacheKey = CacheKeySchema.Create(
            CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword),
            "user@example.com",
            "tenant-1"
        );
        string[] parts = validCacheKey.Value.Split('|');
        parts[7] = EncodeCacheKeyPart(serviceIdentity);
        var invalidCacheKey = new CacheKey { Value = string.Join('|', parts) };

        string violation = Assert.IsType<string>(CacheKeySchema.GetViolation(invalidCacheKey));

        Assert.Contains("service identity", violation, StringComparison.OrdinalIgnoreCase);
        Assert.False(CacheKeySchema.IsValid(invalidCacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(invalidCacheKey));
    }

    [Fact]
    public void CredentialRequestJsonRequiresServiceIdentity()
    {
        var options = ContractJson.CreateSerializerOptions();
        string missingServiceIdentity = CreateCredentialRequestJson(
            "\"ecosystem\":\"git\"",
            includeContractMajor: true,
            includeServiceIdentity: false
        );

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CredentialRequest>(missingServiceIdentity, options)
        );
    }

    [Fact]
    public void CredentialRequestJsonAcceptsExplicitDefaultServiceIdentity()
    {
        var options = ContractJson.CreateSerializerOptions();
        string explicitDefaultServiceIdentity = CreateCredentialRequestJson(
            "\"ecosystem\":\"git\"",
            includeContractMajor: true
        );

        var request = JsonSerializer.Deserialize<CredentialRequest>(
            explicitDefaultServiceIdentity,
            options
        );

        Assert.NotNull(request);
        Assert.Equal("default", request.ServiceIdentity);
        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
    }

    [Fact]
    public void CacheKeyJsonRequiresSchemaMajor()
    {
        var options = ContractJson.CreateSerializerOptions();

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CacheKey>("""{"value":"azdo-cache-v1"}""", options)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void CacheKeySchemaRejectsUnsupportedSchemaMajorFromDeserializedContracts(
        int schemaMajor
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "schemaMajor": {{schemaMajor}},
              "value": "azdo-cache-v1|Z2l0"
            }
            """;
        string resultJson = $$"""
            {
              "contractMajor": 1,
              "status": "success",
              "username": "AzureDevOps",
              "password": "generated-password",
              "cacheKey": {
                "schemaMajor": {{schemaMajor}},
                "value": "azdo-cache-v1|Z2l0"
              },
              "diagnosticsCorrelationId": "corr-cache-key-schema-major-json"
            }
            """;
        var invalidCacheKey = new CacheKey
        {
            SchemaMajor = schemaMajor,
            Value = "azdo-cache-v1|Z2l0",
        };
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "generated-password",
            CacheKey = invalidCacheKey,
            DiagnosticsCorrelationId = "corr-cache-key-schema-major",
        };

        Assert.False(CacheKeySchema.IsValid(invalidCacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(invalidCacheKey));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<CacheKey>(json, options));
        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<CredentialResult>(resultJson, options)
        );
        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedCacheKeySchemaMajor", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(MalformedCacheKeyValues))]
    public void CacheKeySchemaRejectsMalformedSchemaMajorOneValues(string value)
    {
        var options = ContractJson.CreateSerializerOptions();
        var invalidCacheKey = new CacheKey { Value = value };
        string json = $$"""
            {
              "schemaMajor": 1,
              "value": {{JsonSerializer.Serialize(value, options)}}
            }
            """;
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "generated-password",
            CacheKey = invalidCacheKey,
            DiagnosticsCorrelationId = "corr-malformed-cache-key",
        };

        Assert.False(CacheKeySchema.IsValid(invalidCacheKey));
        Assert.Throws<ArgumentException>(() => CacheKeySchema.EnsureValid(invalidCacheKey));
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<CacheKey>(json, options));
        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedCacheKeySchemaMajor", mapped.SafeDiagnosticCode);
    }

    [Fact]
    public void CanonicalResourceIdentityRequiresHttpsEndpointMatchingCanonicalHostAndOrganization()
    {
        var request = CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword);
        var mismatchedResource = request.Resource with
        {
            ServiceEndpoint = new Uri("https://dev.azure.com/other-org/proj/_git/repo"),
        };

        Assert.True(CanonicalResourceIdentityPolicy.IsValid(request.Resource));
        Assert.False(CanonicalResourceIdentityPolicy.IsValid(mismatchedResource));
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(request with { Resource = mismatchedResource })
        );
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(
                request with
                {
                    Resource = mismatchedResource,
                },
                "user@example.com",
                "tenant-1"
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("http://dev.azure.com/org/proj/_git/repo")
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://example.com/org/proj/_git/repo")
            )
        );
    }

    [Theory]
    [MemberData(nameof(PaddedCanonicalResourceIdentityFields))]
    public void CanonicalResourceIdentityRejectsPaddedCanonicalFieldsBeforeEndpointComparison(
        string fieldName,
        CredentialRequest request
    )
    {
        Assert.False(CanonicalResourceIdentityPolicy.IsValid(request.Resource));
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentityPolicy.EnsureValid(request.Resource)
        );
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "user@example.com", "tenant-1")
        );
        Assert.NotEmpty(fieldName);
    }

    [Theory]
    [MemberData(nameof(CanonicalResourceIdentityFieldsWithControlCharacters))]
    // editorconfig-checker-disable
    public void CanonicalResourceIdentityRejectsControlCharactersInCanonicalFieldsBeforeEndpointComparison(
        string fieldName,
        CredentialRequest request
    )
    // editorconfig-checker-enable
    {
        string violation = Assert.IsType<string>(
            CanonicalResourceIdentityPolicy.GetViolation(request.Resource)
        );

        Assert.Contains("control", violation, StringComparison.OrdinalIgnoreCase);
        Assert.False(CanonicalResourceIdentityPolicy.IsValid(request.Resource));
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentityPolicy.EnsureValid(request.Resource)
        );
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "user@example.com", "tenant-1")
        );
        Assert.NotEmpty(fieldName);
    }

    [Fact]
    public void CanonicalResourceIdentityFactoryRejectsPaddedCanonicalInputsBeforeCanonicalization()
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                " dev.azure.com ",
                "org",
                new Uri("https://dev.azure.com/org")
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                "dev.azure.com",
                " org ",
                new Uri("https://dev.azure.com/org")
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org/proj/_git/repo"),
                project: " proj ",
                repository: "repo"
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                "pkgs.dev.azure.com",
                "org",
                new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
                feed: " feed "
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org/proj/_git/repo"),
                project: "proj",
                repository: " repo "
            )
        );
    }

    [Theory]
    [InlineData("https://@dev.azure.com/org/proj/_git/repo")]
    [InlineData("https://user:secret@dev.azure.com/org/proj/_git/repo")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo?token=secret")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo#secret")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo?")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo#")]
    public void CanonicalResourceIdentityRejectsServiceEndpointUserInfoQueryOrFragment(
        string endpoint
    )
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri(endpoint),
                project: "proj",
                repository: "repo"
            )
        );
    }

    [Theory]
    [InlineData(
        "https://dev.azure.com/org%2Fother/proj/_git/repo",
        "dev.azure.com",
        "proj",
        null,
        "repo"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj%2Fother/_git/repo",
        "dev.azure.com",
        "proj/other",
        null,
        "repo"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_git/repo%2Fother",
        "dev.azure.com",
        "proj",
        null,
        "repo/other"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/project%2Fother/_packaging/feed/npm",
        "pkgs.dev.azure.com",
        "project/other",
        "feed",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed%2Fother/npm",
        "pkgs.dev.azure.com",
        null,
        "feed/other",
        null
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project%5Cother/_packaging/feed/npm",
        "org.visualstudio.com",
        "project\\other",
        "feed",
        null
    )]
    public void CanonicalResourceIdentityRejectsSeparatorsInsideEndpointIdentityComponents(
        string endpoint,
        string host,
        string? project,
        string? feed,
        string? repository
    )
    {
        var serviceEndpoint = new Uri(endpoint);

        Assert.False(CanonicalResourceIdentityPolicy.IsSupportedServiceEndpoint(serviceEndpoint));
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                host,
                "org",
                serviceEndpoint,
                project: project,
                feed: feed,
                repository: repository
            )
        );
    }

    [Theory]
    [InlineData(
        "https://pkgs.dev.azure.com/org%0Aother/_packaging/feed/pypi/simple/",
        "pkgs.dev.azure.com",
        null,
        "feed",
        null
    )]
    [InlineData(
        "https://dev.azure.com/org/project%0Dother/_packaging/feed/pypi/simple/",
        "dev.azure.com",
        "project\rother",
        "feed",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed%09other/pypi/simple/",
        "pkgs.dev.azure.com",
        null,
        "feed\tother",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed%C2%85other/pypi/simple/",
        "pkgs.dev.azure.com",
        null,
        "feed\u0085other",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org%1Bother/_packaging/feed/pypi/simple/",
        "pkgs.dev.azure.com",
        null,
        "feed",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed%7Fother/pypi/simple/",
        "pkgs.dev.azure.com",
        null,
        "feed\u007Fother",
        null
    )]
    [InlineData(
        "https://dev.azure.com/org/project/_git/repo%C2%9Fother",
        "dev.azure.com",
        "project",
        null,
        "repo\u009Fother"
    )]
    [InlineData(
        "https://dev.azure.com/org/project/_git/repo%0Aother",
        "dev.azure.com",
        "project",
        null,
        "repo\nother"
    )]
    public void CanonicalResourceIdentityRejectsControlCharactersInsideEndpointIdentityComponents(
        string endpoint,
        string host,
        string? project,
        string? feed,
        string? repository
    )
    {
        var serviceEndpoint = new Uri(endpoint);

        Assert.False(CanonicalResourceIdentityPolicy.IsSupportedServiceEndpoint(serviceEndpoint));
        Assert.NotNull(
            CanonicalResourceIdentityPolicy.GetServiceEndpointViolation(serviceEndpoint)
        );
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                host,
                "org",
                serviceEndpoint,
                project: project,
                feed: feed,
                repository: repository
            )
        );
    }

    [Theory]
    [InlineData(
        "https://dev.azure.com:444/org/proj/_git/repo",
        "dev.azure.com",
        "proj",
        null,
        "repo"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com:8443/org/_packaging/feed/npm/registry",
        "pkgs.dev.azure.com",
        null,
        "feed",
        null
    )]
    public void CanonicalResourceIdentityRejectsServiceEndpointNonDefaultPorts(
        string endpoint,
        string host,
        string? project,
        string? feed,
        string? repository
    )
    {
        var resource = new CanonicalResourceIdentity
        {
            AzureDevOpsHost = host,
            Organization = "org",
            ServiceEndpoint = new Uri(endpoint),
            Project = project,
            Feed = feed,
            Repository = repository,
        };

        Assert.False(CanonicalResourceIdentityPolicy.IsValid(resource));
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                host,
                "org",
                new Uri(endpoint),
                project: project,
                feed: feed,
                repository: repository
            )
        );
    }

    [Fact]
    public void CanonicalResourceIdentityRequiresEndpointPathComponentsToMatchCanonicalFields()
    {
        var gitResource = CanonicalResourceIdentity.Create(
            "dev.azure.com",
            "org",
            new Uri("https://dev.azure.com/org/proj/_git/repo"),
            project: "proj",
            repository: "repo"
        );
        var feedResource = CanonicalResourceIdentity.Create(
            "dev.azure.com",
            "org",
            new Uri("https://dev.azure.com/org/proj/_packaging/feed/nuget/v3/index.json"),
            project: "proj",
            feed: "feed"
        );
        var orgFeedResource = CanonicalResourceIdentity.Create(
            "dev.azure.com",
            "org",
            new Uri("https://dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
            feed: "feed"
        );

        Assert.True(CanonicalResourceIdentityPolicy.IsValid(orgFeedResource));
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(gitResource with { Project = "other-proj" })
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(gitResource with { Repository = "other-repo" })
        );
        Assert.False(CanonicalResourceIdentityPolicy.IsValid(feedResource with { Project = null }));
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(feedResource with { Feed = "other-feed" })
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(orgFeedResource with { Feed = "other-feed" })
        );
    }

    [Fact]
    public void CanonicalResourceIdentityRejectsUnderSpecifiedEndpointsForSpecificFields()
    {
        var gitResource = CanonicalResourceIdentity.Create(
            "dev.azure.com",
            "org",
            new Uri("https://dev.azure.com/org/proj/_git/repo"),
            project: "proj",
            repository: "repo"
        );
        var request = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        ) with
        {
            Resource = gitResource,
        };
        var orgOnlyEndpointForGitResource = gitResource with
        {
            ServiceEndpoint = new Uri("https://dev.azure.com/org"),
        };
        var orgFeedEndpointForProjectFeedResource = CanonicalResourceIdentity.Create(
            "dev.azure.com",
            "org",
            new Uri("https://dev.azure.com/org/proj/_packaging/feed"),
            project: "proj",
            feed: "feed"
        ) with
        {
            ServiceEndpoint = new Uri("https://dev.azure.com/org/_packaging/feed"),
        };
        var validOrgFeedResource = CanonicalResourceIdentity.Create(
            "dev.azure.com",
            "org",
            new Uri("https://dev.azure.com/org/_packaging/feed"),
            feed: "feed"
        );

        Assert.False(CanonicalResourceIdentityPolicy.IsValid(orgOnlyEndpointForGitResource));
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(
                request with
                {
                    Resource = orgOnlyEndpointForGitResource,
                }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(
                request with
                {
                    Resource = orgOnlyEndpointForGitResource,
                },
                "user@example.com",
                "tenant-1"
            )
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(orgFeedEndpointForProjectFeedResource)
        );
        Assert.True(CanonicalResourceIdentityPolicy.IsValid(validOrgFeedResource));
    }

    [Fact]
    public void CanonicalResourceIdentityRejectsUnsupportedHostsEvenWhenCanonicalHostMatches()
    {
        var request = CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword);
        var unsupportedHostResource = request.Resource with
        {
            AzureDevOpsHost = "example.com",
            ServiceEndpoint = new Uri("https://example.com/org/proj/_git/repo"),
        };

        Assert.False(CanonicalResourceIdentityPolicy.IsValid(unsupportedHostResource));
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(
                request with
                {
                    Resource = unsupportedHostResource,
                }
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(
                request with
                {
                    Resource = unsupportedHostResource,
                },
                "user@example.com",
                "tenant-1"
            )
        );
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                "example.com",
                "org",
                new Uri("https://example.com/org/proj/_git/repo"),
                project: "proj",
                repository: "repo"
            )
        );
    }

    [Theory]
    [InlineData("https://dev.azure.com/org/proj/_packaging/feed", "dev.azure.com", "proj")]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/nuget/v3/index.json",
        "dev.azure.com",
        "proj"
    )]
    [InlineData("https://dev.azure.com/org/proj/_packaging/feed/npm", "dev.azure.com", "proj")]
    [InlineData("https://dev.azure.com/org/proj/_packaging/feed/npm/", "dev.azure.com", "proj")]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/npm/registry",
        "dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/npm/registry/",
        "dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/pypi/simple/",
        "dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/pypi/upload/",
        "dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/upload/",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/npm", "pkgs.dev.azure.com", null)]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/npm/", "pkgs.dev.azure.com", null)]
    [InlineData(
        "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry",
        "pkgs.dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/",
        "pkgs.dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/nuget/"
            + "v3/index.json",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/npm",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/npm/",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/npm/registry",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/npm/registry/",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/simple/",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/upload/",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.pkgs.visualstudio.com/_packaging/feed/nuget/v3/index.json",
        "org.pkgs.visualstudio.com",
        null
    )]
    public void CanonicalResourceIdentityPreservesExplicitlySupportedFeedEndpointSuffixes(
        string endpoint,
        string host,
        string? project
    )
    {
        var resource = CanonicalResourceIdentity.Create(
            host,
            "org",
            new Uri(endpoint),
            project: project,
            feed: "feed"
        );

        Assert.True(CanonicalResourceIdentityPolicy.IsValid(resource));
    }

    [Fact]
    public void CanonicalResourceIdentityAcceptsTerminalSlashWithoutChangingStoredEndpoint()
    {
        var endpoint = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/");

        var resource = CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            endpoint,
            feed: "feed"
        );

        Assert.True(CanonicalResourceIdentityPolicy.IsValid(resource));
        Assert.Same(endpoint, resource.ServiceEndpoint);
        Assert.Equal(
            "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/",
            resource.ServiceEndpoint.AbsoluteUri
        );
    }

    [Theory]
    [InlineData("https://dev.azure.com/org/proj/_packaging/feed/maven/v1", "dev.azure.com", "proj")]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/nuget/v2/index.json",
        "dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/nuget/v3/index.json/extra",
        "dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/npm/extra",
        "dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/npm/registry/extra",
        "dev.azure.com",
        "proj"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/maven/v1",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/extra",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/extra",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/extra",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/upload/extra",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/maven/v1",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/nuget/"
            + "v2/index.json",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/nuget/v3/"
            + "index.json/extra",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/npm/registry/extra",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.pkgs.visualstudio.com/_packaging/feed/nuget/v2/index.json",
        "org.pkgs.visualstudio.com",
        null
    )]
    public void CanonicalResourceIdentityRejectsUnsupportedFeedEndpointSuffixes(
        string endpoint,
        string host,
        string? project
    )
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                host,
                "org",
                new Uri(endpoint),
                project: project,
                feed: "feed"
            )
        );
    }

    [Theory]
    [InlineData(
        "https://dev.azure.com/org/proj/_git/repo/_packaging/other",
        "dev.azure.com",
        "proj",
        null,
        "repo"
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_packaging/feed/_git/other",
        "dev.azure.com",
        "proj",
        "feed",
        null
    )]
    [InlineData(
        "https://org.visualstudio.com/project/_git/repo/_packaging/other",
        "org.visualstudio.com",
        "project",
        null,
        "repo"
    )]
    [InlineData(
        "https://org.visualstudio.com/project/_packaging/feed/_git/other",
        "org.visualstudio.com",
        "project",
        "feed",
        null
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_git/repo/_packaging/other",
        "org.visualstudio.com",
        "project",
        null,
        "repo"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/_git/other",
        "org.visualstudio.com",
        "project",
        "feed",
        null
    )]
    public void CanonicalResourceIdentityRejectsTrailingResourceMarkersOutsideSupportedShapes(
        string endpoint,
        string host,
        string project,
        string? feed,
        string? repository
    )
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                host,
                "org",
                new Uri(endpoint),
                project: project,
                feed: feed,
                repository: repository
            )
        );
    }

    [Theory]
    [InlineData("https://dev.azure.com//org/proj/_git/repo")]
    [InlineData("https://dev.azure.com/org//proj/_git/repo")]
    [InlineData("https://dev.azure.com/org/proj/_git//repo")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo//")]
    [InlineData("https://org.visualstudio.com/DefaultCollection//project/_git/repo")]
    [InlineData("https://org.visualstudio.com/project//_git/repo")]
    public void CanonicalResourceIdentityRejectsEmptyEndpointPathSegments(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                endpoint.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase)
                    ? "org.visualstudio.com"
                    : "dev.azure.com",
                "org",
                new Uri(endpoint),
                project: endpoint.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase)
                    ? "project"
                    : "proj",
                repository: "repo"
            )
        );
    }

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging//npm")]
    [InlineData("https://dev.azure.com/org//_packaging/feed/npm")]
    [InlineData("https://dev.azure.com/org/proj/_git/")]
    public void CanonicalResourceIdentityEndpointOnlyValidationRejectsEmptyRequiredPathComponents(
        string endpoint
    )
    {
        var serviceEndpoint = new Uri(endpoint);

        Assert.False(CanonicalResourceIdentityPolicy.IsSupportedServiceEndpoint(serviceEndpoint));
        Assert.Contains(
            "service endpoint path",
            CanonicalResourceIdentityPolicy.GetServiceEndpointViolation(serviceEndpoint),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_git/_packaging/feed/npm",
        "pkgs.dev.azure.com",
        "_git",
        "feed",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/_git/npm",
        "pkgs.dev.azure.com",
        null,
        "_git",
        null
    )]
    [InlineData(
        "https://dev.azure.com/org/_packaging/_packaging/npm",
        "dev.azure.com",
        null,
        "_packaging",
        null
    )]
    [InlineData(
        "https://dev.azure.com/org/proj/_git/_packaging",
        "dev.azure.com",
        "proj",
        null,
        "_packaging"
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/_git/_packaging/feed/npm",
        "pkgs.dev.azure.com",
        null,
        "feed",
        null
    )]
    public void CanonicalResourceIdentityRejectsReservedMarkersAsEndpointIdentityValues(
        string endpoint,
        string host,
        string? project,
        string? feed,
        string? repository
    )
    {
        var serviceEndpoint = new Uri(endpoint);

        Assert.False(CanonicalResourceIdentityPolicy.IsSupportedServiceEndpoint(serviceEndpoint));
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                host,
                endpoint.Contains("pkgs.dev.azure.com/_git", StringComparison.OrdinalIgnoreCase)
                    ? "_git"
                    : "org",
                serviceEndpoint,
                project: project,
                feed: feed,
                repository: repository
            )
        );
    }

    [Theory]
    [InlineData(
        "https://pkgs.dev.azure.com//org/_packaging/feed/pypi/simple/",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/npm//", "pkgs.dev.azure.com", null)]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry//",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData(
        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple//",
        "pkgs.dev.azure.com",
        null
    )]
    [InlineData(
        "https://org.pkgs.visualstudio.com//_packaging/feed/pypi/simple/",
        "org.pkgs.visualstudio.com",
        null
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/npm/registry//",
        "org.visualstudio.com",
        "project"
    )]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/pypi/simple//",
        "org.visualstudio.com",
        "project"
    )]
    public void CanonicalResourceIdentityRejectsPackageEndpointBoundaryEmptySegments(
        string endpoint,
        string host,
        string? project
    )
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                host,
                "org",
                new Uri(endpoint),
                project: project,
                feed: "feed"
            )
        );
    }

    [Theory]
    [InlineData("https://dev.azure.com/org/proj/_unknown/repo")]
    [InlineData("https://dev.azure.com/org/_git/repo")]
    [InlineData("https://dev.azure.com/org/proj/_git/npm/")]
    [InlineData("https://dev.azure.com/org/proj/repo/_git")]
    [InlineData("https://dev.azure.com/org/proj/feed/_packaging")]
    [InlineData("https://dev.azure.com/org/proj/_git")]
    [InlineData("https://org.visualstudio.com/DefaultCollection/OtherProject/_unknown/OtherRepo")]
    [InlineData("https://org.visualstudio.com/DefaultCollection/_git/OtherRepo")]
    [InlineData("https://org.visualstudio.com/DefaultCollection/OtherProject/OtherRepo/_git")]
    public void CanonicalResourceIdentityFailsClosedForUnsupportedOrAmbiguousEndpointPaths(
        string endpoint
    )
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                endpoint.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase)
                    ? "org.visualstudio.com"
                    : "dev.azure.com",
                "org",
                new Uri(endpoint),
                project: "proj",
                repository: "repo"
            )
        );
    }

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org/proj/_git/repo", "pkgs.dev.azure.com")]
    [InlineData("https://org.pkgs.visualstudio.com/project/_git/repo", "org.pkgs.visualstudio.com")]
    [InlineData(
        "https://org.pkgs.visualstudio.com/DefaultCollection/project/_git/repo",
        "org.pkgs.visualstudio.com"
    )]
    public void CanonicalResourceIdentityRejectsGitEndpointPathsOnPackageHosts(
        string endpoint,
        string host
    )
    {
        Assert.Throws<ArgumentException>(() =>
            CanonicalResourceIdentity.Create(
                host,
                "org",
                new Uri(endpoint),
                project: "proj",
                repository: "repo"
            )
        );
    }

    [Fact]
    public void CanonicalResourceIdentitySupportsLegacyVisualStudioOrganizationInHostShapes()
    {
        var gitResource = CanonicalResourceIdentity.Create(
            "org.visualstudio.com",
            "org",
            new Uri("https://org.visualstudio.com/project/_git/repo"),
            project: "project",
            repository: "repo"
        );
        var feedResource = CanonicalResourceIdentity.Create(
            "org.pkgs.visualstudio.com",
            "org",
            new Uri("https://org.pkgs.visualstudio.com/_packaging/feed/nuget/v3/index.json"),
            feed: "feed"
        );
        var defaultCollectionGitResource = CanonicalResourceIdentity.Create(
            "org.visualstudio.com",
            "org",
            new Uri("https://org.visualstudio.com/DefaultCollection/project/_git/repo"),
            project: "project",
            repository: "repo"
        );
        var defaultCollectionFeedResource = CanonicalResourceIdentity.Create(
            "org.visualstudio.com",
            "org",
            new Uri(
                "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/nuget/v3/"
                    + "index.json"
            ),
            project: "project",
            feed: "feed"
        );
        var mismatchedDefaultCollectionGitResource = new CanonicalResourceIdentity
        {
            AzureDevOpsHost = "org.visualstudio.com",
            Organization = "org",
            ServiceEndpoint = new Uri(
                "https://org.visualstudio.com/DefaultCollection/OtherProject/_git/OtherRepo"
            ),
            Project = "project",
            Repository = "repo",
        };
        var mismatchedDefaultCollectionFeedResource = new CanonicalResourceIdentity
        {
            AzureDevOpsHost = "org.visualstudio.com",
            Organization = "org",
            ServiceEndpoint = new Uri(
                "https://org.visualstudio.com/DefaultCollection/OtherProject/_packaging/OtherFeed/"
                    + "nuget/v3/index.json"
            ),
            Project = "project",
            Feed = "feed",
        };

        Assert.True(CanonicalResourceIdentityPolicy.IsValid(gitResource));
        Assert.True(CanonicalResourceIdentityPolicy.IsValid(feedResource));
        Assert.True(CanonicalResourceIdentityPolicy.IsValid(defaultCollectionGitResource));
        Assert.True(CanonicalResourceIdentityPolicy.IsValid(defaultCollectionFeedResource));
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(gitResource with { Organization = "other-org" })
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(gitResource with { Project = "other-project" })
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(gitResource with { Repository = "other-repo" })
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(feedResource with { Feed = "other-feed" })
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(
                defaultCollectionGitResource with
                {
                    Project = "other-project",
                }
            )
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(
                defaultCollectionGitResource with
                {
                    Repository = "other-repo",
                }
            )
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(
                defaultCollectionFeedResource with
                {
                    Feed = "other-feed",
                }
            )
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(mismatchedDefaultCollectionGitResource)
        );
        Assert.False(
            CanonicalResourceIdentityPolicy.IsValid(mismatchedDefaultCollectionFeedResource)
        );
    }

    [Fact]
    public void CredentialResultSeparatesSuccessCredentialsFromTypedErrors()
    {
        var request = CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword);
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "secret-value",
            CacheKey = CacheKeySchema.Create(request, "user@example.com", "tenant-1"),
            DiagnosticsCorrelationId = "corr-1",
        };

        Assert.True(result.ContainsCredentialMaterial);
        Assert.Null(result.Error);
        Assert.DoesNotContain("secret-value", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SuccessWithoutCredentialMaterialIsProtocolViolationNotProtocolStdout()
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            DiagnosticsCorrelationId = "corr-missing-material",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
    }

    [Fact]
    public void AdapterHostMappingKeepsProtocolStdoutEmptyForFailures()
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.CacheUnavailable,
            DiagnosticsCorrelationId = "corr-cache",
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.CacheUnavailable,
                Code = "CacheUnavailable",
                SafeMessage = "Secure credential cache is unavailable.",
            },
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.CacheUnavailable, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("CacheUnavailable", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [InlineData(CredentialErrorKind.Unauthorized, "Unauthorized")]
    [InlineData(CredentialErrorKind.CacheUnavailable, "CacheUnavailable")]
    [InlineData(CredentialErrorKind.InteractionRequired, "InteractionRequired")]
    [InlineData(CredentialErrorKind.IntegrityFailure, "IntegrityFailure")]
    [InlineData(CredentialErrorKind.Fatal, "Fatal")]
    [InlineData(CredentialErrorKind.UnsupportedFlow, "UnsupportedFlow")]
    public void AdapterHostMappingTreatsErrorBearingSuccessAsMapperOwnedProtocolViolation(
        CredentialErrorKind errorKind,
        string code
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-success-error",
            Error = new CredentialError
            {
                Kind = errorKind,
                Code = code,
                SafeMessage = "Success must not carry a typed error.",
            },
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
        Assert.NotEqual(code, mapped.SafeDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(AdapterHostStatusMappingCases))]
    public void AdapterHostMappingCoversEveryFrozenCredentialResultStatus(
        CredentialResultStatus status,
        AdapterHostExitCode exitCode,
        bool writeProtocolStdout,
        bool writeDiagnosticStderr,
        string? diagnosticCode
    )
    {
        var result = new CredentialResult
        {
            Status = status,
            Username = status == CredentialResultStatus.Success ? "AzureDevOps" : null,
            Password = status == CredentialResultStatus.Success ? "generated-password" : null,
            DiagnosticsCorrelationId = "corr-adapter-status-map",
            Error = diagnosticCode is null
                ? null
                : new CredentialError
                {
                    Kind = ToErrorKind(status),
                    Code = diagnosticCode,
                    SafeMessage = "Mapped status.",
                },
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(exitCode, mapped.ExitCode);
        Assert.Equal(writeProtocolStdout, mapped.WriteProtocolStdout);
        Assert.Equal(writeDiagnosticStderr, mapped.WriteDiagnosticStderr);
        Assert.Equal(diagnosticCode, mapped.SafeDiagnosticCode);
    }

    [Fact]
    public void AdapterHostStatusMappingCasesCoverEveryFrozenCredentialResultStatus()
    {
        CredentialResultStatus[] mappedStatuses = AdapterHostStatusMappingCases
            .Select(row => Assert.IsType<CredentialResultStatus>(row[0]))
            .Order()
            .ToArray();
        CredentialResultStatus[] frozenStatuses = Enum.GetValues<CredentialResultStatus>()
            .Order()
            .ToArray();

        Assert.Equal(frozenStatuses, mappedStatuses);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void AdapterHostMappingRejectsUnsupportedCredentialResultContractMajor(int contractMajor)
    {
        var result = new CredentialResult
        {
            ContractMajor = contractMajor,
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-result-version",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedContractMajor", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [InlineData(AdapterProtocol.Unspecified)]
    [InlineData((AdapterProtocol)999)]
    public void AdapterHostMappingRejectsUnsupportedProtocolBeforeStatusMapping(
        AdapterProtocol protocol
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        CredentialResult[] results =
        [
            new()
            {
                Status = CredentialResultStatus.Success,
                Password = "generated-password",
                DiagnosticsCorrelationId = "corr-unknown-protocol-success",
            },
            new()
            {
                Status = CredentialResultStatus.NoCredential,
                DiagnosticsCorrelationId = "corr-unknown-protocol-no-credential",
            },
            new()
            {
                Status = CredentialResultStatus.CacheUnavailable,
                DiagnosticsCorrelationId = "corr-unknown-protocol-cache",
                Error = new CredentialError
                {
                    Kind = CredentialErrorKind.CacheUnavailable,
                    Code = "CacheUnavailable",
                    SafeMessage = "Secure credential cache is unavailable.",
                },
            },
        ];

        Assert.All(
            results,
            result =>
            {
                AdapterHostResult mapped = AdapterHostResultMapper.Map(protocol, result);

                Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
                Assert.Equal(AdapterProtocol.Unspecified, mapped.Protocol);
                Assert.False(mapped.WriteProtocolStdout);
                Assert.True(mapped.WriteDiagnosticStderr);
                Assert.Equal("UnsupportedAdapterProtocol", mapped.SafeDiagnosticCode);

                string json = JsonSerializer.Serialize(mapped, options);
                AdapterHostResult? roundTripped = JsonSerializer.Deserialize<AdapterHostResult>(
                    json,
                    options
                );

                Assert.Contains("\"protocol\":\"unspecified\"", json, StringComparison.Ordinal);
                Assert.DoesNotContain("999", json, StringComparison.Ordinal);
                Assert.NotNull(roundTripped);
                Assert.Equal(AdapterProtocol.Unspecified, roundTripped.Protocol);
                Assert.Equal(AdapterHostExitCode.ConfigurationError, roundTripped.ExitCode);
                Assert.False(roundTripped.WriteProtocolStdout);
                Assert.True(roundTripped.WriteDiagnosticStderr);
                Assert.Equal("UnsupportedAdapterProtocol", roundTripped.SafeDiagnosticCode);
            }
        );
    }

    [Theory]
    [InlineData(
        CredentialResultStatus.FlowDeferred,
        CredentialErrorKind.FlowDeferred,
        "FlowDeferred"
    )]
    [InlineData(
        CredentialResultStatus.FlowDisabled,
        CredentialErrorKind.FlowDisabled,
        "FlowDisabled"
    )]
    [InlineData(
        CredentialResultStatus.UnsupportedFlow,
        CredentialErrorKind.UnsupportedFlow,
        "UnsupportedFlow"
    )]
    [InlineData(
        CredentialResultStatus.CredentialUnavailable,
        CredentialErrorKind.CredentialUnavailable,
        "CredentialUnavailable"
    )]
    public void AdapterHostMappingFailsClosedForDeferredDisabledUnsupportedOrUnavailableStatuses(
        CredentialResultStatus status,
        CredentialErrorKind errorKind,
        string code
    )
    {
        var result = new CredentialResult
        {
            Status = status,
            DiagnosticsCorrelationId = "corr-adapter-fail-closed-status",
            Error = new CredentialError
            {
                Kind = errorKind,
                Code = code,
                SafeMessage = "Credential flow cannot produce credential material.",
            },
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal(code, mapped.SafeDiagnosticCode);
    }

    [Theory]
    [MemberData(nameof(HardCredentialErrorMappingCases))]
    public void AdapterHostMappingHonorsHardErrorsBeforeBenignStatusMapping(
        CredentialErrorKind errorKind,
        AdapterHostExitCode exitCode,
        string code
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.NoCredential,
            DiagnosticsCorrelationId = "corr-hard-error-over-status",
            Error = new CredentialError
            {
                Kind = errorKind,
                Code = code,
                SafeMessage = "Hard error must not be downgraded to no credential.",
            },
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(exitCode, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal(code, mapped.SafeDiagnosticCode);
    }

    [Fact]
    public void AdapterHostMappingKeepsBenignNoCredentialErrorsNonDiagnostic()
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.NoCredential,
            DiagnosticsCorrelationId = "corr-benign-no-credential",
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.UnsupportedHost,
                Code = "UnsupportedHost",
                SafeMessage = "Host is unsupported.",
            },
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterHostExitCode.NoCredential, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.False(mapped.WriteDiagnosticStderr);
        Assert.Equal("UnsupportedHost", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [InlineData(AdapterProtocol.GitCredentialHelper, null, null, "bearer-only", true, true)]
    [InlineData(AdapterProtocol.GitCredentialHelper, null, "password", null, false, false)]
    [InlineData(AdapterProtocol.GitCredentialHelper, "", "password", null, false, false)]
    [InlineData(AdapterProtocol.GitCredentialHelper, "AzureDevOps", null, null, false, false)]
    [InlineData(AdapterProtocol.GitCredentialHelper, "AzureDevOps", "password", null, true, true)]
    [InlineData(AdapterProtocol.NuGetPlugin, null, "password", null, false, false)]
    [InlineData(AdapterProtocol.NuGetPlugin, "AzureDevOps", null, "bearer", false, false)]
    [InlineData(AdapterProtocol.NuGetPlugin, "AzureDevOps", "password", null, true, true)]
    [InlineData(AdapterProtocol.NuGetPlugin, null, null, null, false, false)]
    [InlineData(AdapterProtocol.NpmConfiguration, null, "password-only", null, false, false)]
    [InlineData(AdapterProtocol.NpmConfiguration, null, null, "bearer", true, false)]
    [InlineData(AdapterProtocol.PythonKeyringBackend, null, "password", null, true, false)]
    [InlineData(AdapterProtocol.PythonKeyringBackend, null, null, "bearer-only", false, false)]
    public void AdapterHostSuccessMappingRequiresProtocolSpecificOutputMaterial(
        AdapterProtocol protocol,
        string? username,
        string? password,
        string? bearerToken,
        bool hasRequiredSuccessMaterial,
        bool writesProtocolStdout
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = username,
            Password = password,
            BearerToken = bearerToken,
            DiagnosticsCorrelationId = "corr-protocol-material",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(protocol, result);

        Assert.Equal(protocol, mapped.Protocol);
        Assert.Equal(
            hasRequiredSuccessMaterial
                ? AdapterHostExitCode.Success
                : AdapterHostExitCode.ConfigurationError,
            mapped.ExitCode
        );
        Assert.Equal(writesProtocolStdout, mapped.WriteProtocolStdout);
        Assert.Equal(!hasRequiredSuccessMaterial, mapped.WriteDiagnosticStderr);
    }

    [Theory]
    [InlineData(CredentialOperation.Store)]
    [InlineData(CredentialOperation.Erase)]
    public void GitCredentialHelperLifecycleSuccessDoesNotRequireProtocolStdoutMaterial(
        CredentialOperation operation
    )
    {
        var request = CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword);
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            CacheKey = CacheKeySchema.Create(request, "user@example.com", "tenant-1"),
            DiagnosticsCorrelationId = "corr-git-lifecycle-success",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            operation,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.Success, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.False(mapped.WriteDiagnosticStderr);
    }

    [Theory]
    [InlineData(CredentialOperation.Unspecified)]
    [InlineData(CredentialOperation.Refresh)]
    [InlineData(CredentialOperation.Configure)]
    [InlineData(CredentialOperation.Doctor)]
    [InlineData((CredentialOperation)999)]
    public void GitCredentialHelperRejectsUnsupportedOperationsBeforeSuccessMapping(
        CredentialOperation operation
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-git-unsupported-operation",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            operation,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [InlineData(CredentialOperation.Store, "basic")]
    [InlineData(CredentialOperation.Store, "username")]
    [InlineData(CredentialOperation.Store, "password")]
    [InlineData(CredentialOperation.Store, "bearer")]
    [InlineData(CredentialOperation.Erase, "basic")]
    [InlineData(CredentialOperation.Erase, "username")]
    [InlineData(CredentialOperation.Erase, "password")]
    [InlineData(CredentialOperation.Erase, "bearer")]
    public void GitCredentialHelperLifecycleSuccessRejectsCredentialMaterial(
        CredentialOperation operation,
        string materialKind
    )
    {
        CredentialResult result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            DiagnosticsCorrelationId = "corr-git-lifecycle-material",
        };
        result = materialKind switch
        {
            "basic" => result with { Username = "AzureDevOps", Password = "generated-password" },
            "username" => result with { Username = "AzureDevOps" },
            "password" => result with { Password = "generated-password" },
            "bearer" => result with { BearerToken = "bearer-token" },
            _ => throw new ArgumentOutOfRangeException(nameof(materialKind)),
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            operation,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
    }

    [Fact]
    public void GitCredentialHelperMapsBearerTokenSuccessToFixedBasicMaterial()
    {
        var request = CreateRequest(
            IdentityFlow.AzurePipelinesSystemAccessToken,
            CredentialKind.BearerToken
        ) with
        {
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
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            BearerToken = "system-access-token",
            CacheKey = CacheKeySchema.Create(request, "build-service@org", "tenant-1"),
            DiagnosticsCorrelationId = "corr-git-bearer-basic-material",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );
        bool hasBasicMaterial = AdapterHostResultMapper.TryMapGitCredentialHelperBasicMaterial(
            result,
            out string? username,
            out string? password
        );

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Equal(AdapterHostExitCode.Success, mapped.ExitCode);
        Assert.True(mapped.WriteProtocolStdout);
        Assert.False(mapped.WriteDiagnosticStderr);
        Assert.True(hasBasicMaterial);
        Assert.Equal(AdapterHostResultMapper.GitCredentialHelperBearerTokenUsername, username);
        Assert.Equal(result.BearerToken, password);
    }

    [Theory]
    [InlineData(CredentialKind.BasicPassword, IdentityFlow.InteractiveBrowser)]
    [InlineData(CredentialKind.PatCompatibility, IdentityFlow.PatCompatibility)]
    public void GitCredentialHelperRejectsBearerOnlyMaterialForBasicOrPatCacheKeys(
        CredentialKind credentialKind,
        IdentityFlow identityFlow
    )
    {
        var request = CreateRequest(identityFlow, credentialKind);
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            BearerToken = "bearer-token",
            CacheKey = CacheKeySchema.Create(request, "user@example.com", "tenant-1"),
            DiagnosticsCorrelationId = "corr-git-bearer-basic-cache-key",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
    }

    [Fact]
    public void GitCredentialHelperRejectsBasicOnlyMaterialForBearerCacheKey()
    {
        var request = CreateRequest(
            IdentityFlow.AzurePipelinesSystemAccessToken,
            CredentialKind.BearerToken
        ) with
        {
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
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "generated-password",
            CacheKey = CacheKeySchema.Create(request, "build-service@org", "tenant-1"),
            DiagnosticsCorrelationId = "corr-git-basic-bearer-cache-key",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [InlineData("AzureDevOps", "generated-password")]
    [InlineData("AzureDevOps", null)]
    [InlineData(null, "generated-password")]
    [InlineData("Azure\nDevOps", "generated-password")]
    [InlineData("AzureDevOps", "generated\npassword")]
    public void GitCredentialHelperRejectsBearerWithAnyBasicMaterial(
        string? basicUsername,
        string? basicPassword
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = basicUsername,
            Password = basicPassword,
            BearerToken = "bearer-token",
            DiagnosticsCorrelationId = "corr-git-mixed-material",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );
        bool hasBasicMaterial = AdapterHostResultMapper.TryMapGitCredentialHelperBasicMaterial(
            result,
            out string? mappedUsername,
            out string? mappedPassword
        );

        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
        Assert.False(hasBasicMaterial);
        Assert.Null(mappedUsername);
        Assert.Null(mappedPassword);
    }

    [Fact]
    public void AdapterHostMappingAcceptsOnlyExpectedValidCacheKeyShapesForEachProtocol()
    {
        AdapterProtocol[] protocols =
        [
            AdapterProtocol.GitCredentialHelper,
            AdapterProtocol.NuGetPlugin,
            AdapterProtocol.PythonKeyringBackend,
            AdapterProtocol.KeyringHelper,
            AdapterProtocol.NpmConfiguration,
        ];
        (
            string Name,
            CacheKey CacheKey,
            AdapterProtocol[] AcceptedProtocols
        )[] validCacheKeyShapes =
        [
            (
                "git-basic",
                CacheKeySchema.Create(
                    CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.GitCredentialHelper]
            ),
            (
                "git-bearer",
                CacheKeySchema.Create(
                    CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BearerToken),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.GitCredentialHelper]
            ),
            (
                "git-pat",
                CacheKeySchema.Create(
                    CreateRequest(IdentityFlow.PatCompatibility, CredentialKind.PatCompatibility),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.GitCredentialHelper]
            ),
            (
                "nuget-feed",
                CacheKeySchema.Create(
                    CreatePackageRequest(
                        CredentialEcosystem.NuGet,
                        CredentialKind.NuGetPluginCredential
                    ),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.NuGetPlugin]
            ),
            (
                "nuget-project-feed",
                CacheKeySchema.Create(
                    CreateProjectScopedPackageRequest(
                        CredentialEcosystem.NuGet,
                        CredentialKind.NuGetPluginCredential
                    ),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.NuGetPlugin]
            ),
            (
                "python-feed",
                CacheKeySchema.Create(
                    CreatePackageRequest(CredentialEcosystem.Python, CredentialKind.BasicPassword),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.PythonKeyringBackend, AdapterProtocol.KeyringHelper]
            ),
            (
                "python-project-feed",
                CacheKeySchema.Create(
                    CreateProjectScopedPackageRequest(
                        CredentialEcosystem.Python,
                        CredentialKind.BasicPassword
                    ),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.PythonKeyringBackend, AdapterProtocol.KeyringHelper]
            ),
            (
                "npm-feed",
                CacheKeySchema.Create(
                    CreatePackageRequest(CredentialEcosystem.Npm, CredentialKind.NpmAuthToken),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.NpmConfiguration]
            ),
            (
                "npm-project-feed",
                CacheKeySchema.Create(
                    CreateProjectScopedPackageRequest(
                        CredentialEcosystem.Npm,
                        CredentialKind.NpmAuthToken
                    ),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.NpmConfiguration]
            ),
            (
                "pnpm-feed",
                CacheKeySchema.Create(
                    CreatePackageRequest(CredentialEcosystem.Pnpm, CredentialKind.NpmAuthToken),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.NpmConfiguration]
            ),
            (
                "pnpm-project-feed",
                CacheKeySchema.Create(
                    CreateProjectScopedPackageRequest(
                        CredentialEcosystem.Pnpm,
                        CredentialKind.NpmAuthToken
                    ),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.NpmConfiguration]
            ),
            (
                "yarn-feed",
                CacheKeySchema.Create(
                    CreatePackageRequest(CredentialEcosystem.Yarn, CredentialKind.NpmAuthToken),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.NpmConfiguration]
            ),
            (
                "yarn-project-feed",
                CacheKeySchema.Create(
                    CreateProjectScopedPackageRequest(
                        CredentialEcosystem.Yarn,
                        CredentialKind.NpmAuthToken
                    ),
                    "user@example.com",
                    "tenant-1"
                ),
                [AdapterProtocol.NpmConfiguration]
            ),
        ];

        Assert.All(
            validCacheKeyShapes,
            shape => Assert.True(CacheKeySchema.IsValid(shape.CacheKey), shape.Name)
        );
        foreach (
            (
                string name,
                CacheKey cacheKey,
                AdapterProtocol[] acceptedProtocols
            ) in validCacheKeyShapes
        )
        {
            foreach (AdapterProtocol protocol in protocols)
            {
                CredentialResult result = CreateProtocolSuccessResult(protocol, cacheKey);

                AdapterHostResult mapped = AdapterHostResultMapper.Map(protocol, result);

                bool shouldAccept = acceptedProtocols.Contains(protocol);
                Assert.Equal(protocol, mapped.Protocol);
                Assert.Equal(
                    shouldAccept
                        ? AdapterHostExitCode.Success
                        : AdapterHostExitCode.ConfigurationError,
                    mapped.ExitCode
                );
                Assert.Equal(
                    shouldAccept
                        && protocol
                            is AdapterProtocol.GitCredentialHelper
                                or AdapterProtocol.NuGetPlugin
                                or AdapterProtocol.KeyringHelper,
                    mapped.WriteProtocolStdout
                );
                Assert.Equal(!shouldAccept, mapped.WriteDiagnosticStderr);
                Assert.Equal(shouldAccept ? null : "ProtocolViolation", mapped.SafeDiagnosticCode);
                Assert.NotEmpty(name);
            }
        }
    }

    [Fact]
    public void NpmConfigurationSuccessUsesConfigurationChangePlanNotProtocolStdout()
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            BearerToken = "bearer-token",
            DiagnosticsCorrelationId = "corr-npm-configuration-success",
        };
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-npm-configuration-success",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            Manifest = CreateManifest("npm-configuration-success"),
            ContainsCredentialMaterial = true,
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Create,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = "user .npmrc",
                    Key = "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
                    Value = result.BearerToken,
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                },
            ],
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.NpmConfiguration,
            result
        );

        Assert.Equal(AdapterHostExitCode.Success, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.False(mapped.WriteDiagnosticStderr);
        ConfigurationChange change = Assert.Single(plan.Changes);
        Assert.Equal(result.BearerToken, change.Value);
        Assert.True(change.IsSecretValue);
        Assert.Equal(ConfigurationTargetKind.Npmrc, change.TargetKind);
    }

    [Theory]
    [InlineData("bearer\rtoken")]
    [InlineData("bearer\ntoken")]
    public void NpmConfigurationRejectsBearerTokenLineBreaksBeforePlanMaterial(string bearerToken)
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            BearerToken = bearerToken,
            DiagnosticsCorrelationId = "corr-npm-configuration-bearer-line-break",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.NpmConfiguration,
            result
        );

        Assert.Equal(AdapterProtocol.NpmConfiguration, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [InlineData(
        "pkgs.dev.azure.com",
        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/",
        null,
        "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry\"]"
            + ".npmAuthToken"
    )]
    [InlineData(
        "org.pkgs.visualstudio.com",
        "https://org.pkgs.visualstudio.com/_packaging/feed/npm/registry",
        null,
        "//org.pkgs.visualstudio.com/_packaging/feed/npm/registry/:_authToken",
        "npmRegistries[\"https://org.pkgs.visualstudio.com/_packaging/feed/npm/registry\"]"
            + ".npmAuthToken"
    )]
    public void NpmCompatibleAuthSelectorsAreDerivedFromAcceptedRegistryEndpoint(
        string host,
        string serviceEndpoint,
        string? project,
        string expectedNpmSelector,
        string expectedYarnTokenSelector
    )
    {
        CanonicalResourceIdentity resource = CanonicalResourceIdentity.Create(
            host,
            "org",
            new Uri(serviceEndpoint),
            project: project,
            feed: "feed"
        );

        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(resource);

        Assert.Equal(expectedNpmSelector, selectors.NpmAuthTokenKey);
        Assert.Equal(expectedYarnTokenSelector, selectors.YarnAuthTokenKey);
        Assert.Equal(
            expectedYarnTokenSelector.Replace(
                ".npmAuthToken",
                ".npmAlwaysAuth",
                StringComparison.Ordinal
            ),
            selectors.YarnAlwaysAuthKey
        );
    }

    [Fact]
    public void ContractJsonRoundTripsNpmCompatibleAuthSelectorsThroughSourceGeneratedMetadata()
    {
        var options = ContractJson.CreateSerializerOptions();
        CanonicalResourceIdentity resource = CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"),
            feed: "feed"
        );
        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(resource);

        string json = JsonSerializer.Serialize(selectors, options);
        NpmCompatibleAuthSelectors? roundTripped =
            JsonSerializer.Deserialize<NpmCompatibleAuthSelectors>(json, options);

        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        Assert.NotNull(options.TypeInfoResolver);
        Assert.Contains(
            $"\"npmAuthTokenKey\":{JsonSerializer.Serialize(selectors.NpmAuthTokenKey, options)}",
            json,
            StringComparison.Ordinal
        );
        Assert.Contains(
            $"\"yarnAuthTokenKey\":{JsonSerializer.Serialize(selectors.YarnAuthTokenKey, options)}",
            json,
            StringComparison.Ordinal
        );
        Assert.Contains(
            string.Concat(
                "\"yarnAlwaysAuthKey\":",
                JsonSerializer.Serialize(selectors.YarnAlwaysAuthKey, options)
            ),
            json,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("npmAuthIdent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"NpmAuthTokenKey\"", json, StringComparison.Ordinal);
        Assert.NotNull(roundTripped);
        Assert.Equal(selectors.NpmAuthTokenKey, roundTripped.NpmAuthTokenKey);
        Assert.Equal(selectors.YarnAuthTokenKey, roundTripped.YarnAuthTokenKey);
        Assert.Equal(selectors.YarnAlwaysAuthKey, roundTripped.YarnAlwaysAuthKey);
    }

    [Fact]
    public void NpmCompatibleAuthSelectorsRejectNonNpmPackageEndpoints()
    {
        CanonicalResourceIdentity nugetResource = CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"),
            feed: "feed"
        );

        Assert.Throws<ArgumentException>(() =>
            NpmCompatibleAuthSelectorPolicy.Create(nugetResource)
        );
    }

    [Theory]
    [InlineData(
        "dev.azure.com",
        "https://dev.azure.com/org/proj/_packaging/feed/npm/registry",
        "proj"
    )]
    [InlineData(
        "org.visualstudio.com",
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/npm/registry",
        "project"
    )]
    public void NpmCompatibleAuthSelectorsRejectNonRegistryWebEndpoints(
        string host,
        string serviceEndpoint,
        string project
    )
    {
        CanonicalResourceIdentity resource = CanonicalResourceIdentity.Create(
            host,
            "org",
            new Uri(serviceEndpoint),
            project: project,
            feed: "feed"
        );

        Assert.Throws<ArgumentException>(() => NpmCompatibleAuthSelectorPolicy.Create(resource));
    }

    [Theory]
    [InlineData("Azure\nDevOps", "generated-password")]
    [InlineData("Azure\rDevOps", "generated-password")]
    [InlineData("AzureDevOps", "generated\npassword")]
    [InlineData("AzureDevOps", "generated\rpassword")]
    public void GitCredentialHelperSuccessMappingRejectsLineBreaksInStdoutMaterial(
        string username,
        string password
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = username,
            Password = password,
            DiagnosticsCorrelationId = "corr-git-credential-line-break",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
    }

    [Theory]
    [InlineData("system\naccess-token")]
    [InlineData("system\raccess-token")]
    public void GitCredentialHelperBearerTokenMappingRejectsLineBreaksInPasswordMaterial(
        string bearerToken
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            BearerToken = bearerToken,
            DiagnosticsCorrelationId = "corr-git-bearer-line-break",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(
            AdapterProtocol.GitCredentialHelper,
            result
        );
        bool hasBasicMaterial = AdapterHostResultMapper.TryMapGitCredentialHelperBasicMaterial(
            result,
            out string? username,
            out string? password
        );

        Assert.Equal(AdapterProtocol.GitCredentialHelper, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
        Assert.False(hasBasicMaterial);
        Assert.Null(username);
        Assert.Null(password);
    }

    [Theory]
    [InlineData("Azure\nDevOps", "generated-password")]
    [InlineData("Azure\rDevOps", "generated-password")]
    [InlineData("AzureDevOps", "generated\npassword")]
    [InlineData("AzureDevOps", "generated\rpassword")]
    public void NuGetPluginSuccessMappingRequiresCompleteBasicMaterialWithoutLineBreaks(
        string username,
        string password
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = username,
            Password = password,
            DiagnosticsCorrelationId = "corr-nuget-basic-line-break",
        };

        AdapterHostResult mapped = AdapterHostResultMapper.Map(AdapterProtocol.NuGetPlugin, result);

        Assert.Equal(AdapterProtocol.NuGetPlugin, mapped.Protocol);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, mapped.ExitCode);
        Assert.False(mapped.WriteProtocolStdout);
        Assert.True(mapped.WriteDiagnosticStderr);
        Assert.Equal("ProtocolViolation", mapped.SafeDiagnosticCode);
    }

    [Fact]
    public void ConfigurationChangePlanIsDeclarativeOwnedAndSecretAware()
    {
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-npm-user-auth",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            Manifest = CreateManifest("npm-user-auth"),
            ContainsCredentialMaterial = true,
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Create,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = "user .npmrc",
                    Key = "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
                    Value = "secret-token",
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Create,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = "user .npmrc",
                    Key = "//pkgs.dev.azure.com/org/_packaging/feed/npm/:_authToken",
                    Value = "secret-token",
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Create,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = "user .npmrc",
                    Key = "//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken",
                    Value = "secret-token",
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                },
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Create,
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = "user .npmrc",
                    Key = "//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/:_authToken",
                    Value = "secret-token",
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                },
            ],
        };

        Assert.Equal(ContractVersions.ConfigurationChangePlanMajor, plan.ContractMajor);
        Assert.All(plan.Changes, change => Assert.True(change.RequiresOwnershipRecord));
        Assert.True(plan.ContainsCredentialMaterial);
        Assert.All(plan.Changes, change => Assert.True(change.IsSecretValue));
        Assert.Equal("manifest-npm-user-auth", plan.Manifest.ManifestId);
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key == "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken"
        );
        Assert.Contains(
            plan.Changes,
            change => change.Key == "//pkgs.dev.azure.com/org/_packaging/feed/npm/:_authToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken"
        );
        Assert.Contains(
            plan.Changes,
            change => change.Key == "//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/:_authToken"
        );
        Assert.All(
            plan.Changes,
            change =>
                Assert.DoesNotContain("secret-token", change.ToString(), StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ConfigurationChangePlanRepresentsYarnBerryRegistryAuthEntriesSeparately()
    {
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-yarn-user-auth",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            Manifest = CreateManifest("yarn-user-auth"),
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateYarnAuthTokenChange("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
                CreateYarnAlwaysAuthChange("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
                CreateYarnAuthTokenChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"
                ),
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"
                ),
                CreateYarnAuthTokenChange(
                    "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm"
                ),
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm"
                ),
                CreateYarnAuthTokenChange(
                    "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry"
                ),
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry"
                ),
            ],
        };

        Assert.Equal(ConfigurationScope.User, plan.Scope);
        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "canonical registry identity",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.Ordinal
        );
        Assert.All(
            plan.Changes,
            change => Assert.Equal(ConfigurationTargetKind.Yarnrc, change.TargetKind)
        );
        Assert.All(
            plan.Changes,
            change => Assert.Equal("user .yarnrc.yml", change.TargetPathOrName)
        );
        Assert.Equal(
            4,
            plan.Changes.Count(change =>
                change.Key.EndsWith(".npmAuthToken", StringComparison.Ordinal)
            )
        );
        Assert.DoesNotContain(
            plan.Changes,
            change => change.Key.EndsWith(".npmAuthIdent", StringComparison.Ordinal)
        );
        Assert.Equal(
            4,
            plan.Changes.Count(change =>
                change.Key.EndsWith(".npmAlwaysAuth", StringComparison.Ordinal)
            )
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"]"
                    + ".npmAuthToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"]"
                    + ".npmAlwaysAuth"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry\"]"
                    + ".npmAuthToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry\"]"
                    + ".npmAlwaysAuth"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm\"]"
                    + ".npmAuthToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm\"]"
                    + ".npmAlwaysAuth"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/"
                    + "registry\"].npmAuthToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/"
                    + "registry\"].npmAlwaysAuth"
        );
        Assert.All(
            plan.Changes.Where(change =>
                change.Key.EndsWith(".npmAuthToken", StringComparison.Ordinal)
            ),
            change =>
            {
                Assert.True(change.IsSecretValue);
                Assert.Equal("secret-token", change.Value);
                Assert.DoesNotContain("secret-token", change.ToString(), StringComparison.Ordinal);
            }
        );
        Assert.All(
            plan.Changes.Where(change =>
                change.Key.EndsWith(".npmAlwaysAuth", StringComparison.Ordinal)
            ),
            change =>
            {
                Assert.False(change.IsSecretValue);
                Assert.Equal("true", change.Value);
            }
        );
    }

    [Fact]
    public void ConfigurationChangePlanRepresentsCiTemporaryYarnBerryRegistryAuth()
    {
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-ci-yarn-auth",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            ExpiresAt = DateTimeOffset.Parse("2026-06-06T12:00:00Z", CultureInfo.InvariantCulture),
            Manifest = CreateManifest("ci-yarn-auth"),
            TemporaryContainer = CreateTemporaryHomeContainer(
                @"C:\agent\_temp\azureauth-credprovider\yarn-home"
            ),
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateYarnAuthTokenChange("https://pkgs.dev.azure.com/org/_packaging/feed/npm") with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
                CreateYarnAuthTokenChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
                CreateYarnAuthTokenChange(
                    "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
                CreateYarnAuthTokenChange(
                    "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
            ],
        };

        Assert.Equal(ConfigurationScope.CiTemporary, plan.Scope);
        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "canonical registry identity",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.Ordinal
        );
        Assert.Equal(
            ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            plan.DeclarationPreservation
        );
        Assert.Equal(
            ConfigurationTemporaryContainerKind.TemporaryHome,
            plan.TemporaryContainer?.Kind
        );
        Assert.Equal(
            @"C:\agent\_temp\azureauth-credprovider\yarn-home",
            plan.TemporaryContainer?.ProductOwnedPath
        );
        Assert.All(
            plan.Changes,
            change =>
            {
                Assert.Equal(ConfigurationTargetKind.Yarnrc, change.TargetKind);
                Assert.Equal(
                    @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                    change.TargetPathOrName
                );
                Assert.True(change.PreserveDeclarationsAndComments);
                Assert.True(change.RequiresOwnershipRecord);
            }
        );
        Assert.Equal(
            4,
            plan.Changes.Count(change =>
                change.Key.EndsWith(".npmAuthToken", StringComparison.Ordinal)
            )
        );
        Assert.DoesNotContain(
            plan.Changes,
            change => change.Key.EndsWith(".npmAuthIdent", StringComparison.Ordinal)
        );
        Assert.Equal(
            4,
            plan.Changes.Count(change =>
                change.Key.EndsWith(".npmAlwaysAuth", StringComparison.Ordinal)
            )
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"]"
                    + ".npmAuthToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"]"
                    + ".npmAlwaysAuth"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry\"]"
                    + ".npmAuthToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry\"]"
                    + ".npmAlwaysAuth"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm\"]"
                    + ".npmAuthToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm\"]"
                    + ".npmAlwaysAuth"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/"
                    + "registry\"].npmAuthToken"
        );
        Assert.Contains(
            plan.Changes,
            change =>
                change.Key
                == "npmRegistries[\"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/"
                    + "registry\"].npmAlwaysAuth"
        );
    }

    [Fact]
    public void DoctorCheckFreezesYarnCiTemporaryProjectLocalAuthShadowingConflict()
    {
        var check = new DoctorCheck
        {
            CheckId = "yarn.ci.project-local-auth-shadowing",
            Status = DoctorCheckStatus.Fail,
            Severity = DoctorCheckSeverity.Error,
            Target = @"C:\repo\.yarnrc.yml",
            Summary =
                "Project-local Yarn auth for the same registry would shadow CI temporary HOME "
                + "auth.",
            DiagnosticsCorrelationId = "corr-yarn-ci-shadowing",
            ObservedValue =
                "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm/"
                + "registry\"].npmAuthToken in C:\\repo\\.yarnrc.yml",
            ExpectedValue =
                "No same-registry project-local Yarn auth when CI temporary HOME auth is planned.",
            Remediation =
                "Remove or migrate the project-local Yarn auth entry before applying the CI "
                + "temporary plan.",
            SafeDetails = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>
                {
                    ["registry"] = "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry",
                    ["shadowingScope"] = "project-local",
                    ["plannedScope"] = "ci-temporary",
                    ["shadowingSelectors"] =
                        "npmRegistries[registry].npmAuthToken;"
                        + "npmRegistries[registry].npmAuthIdent;"
                        + "npmRegistries[registry].npmAlwaysAuth=false;"
                        + "npmScopes[*].npmAuthToken;"
                        + "npmScopes[*].npmAuthIdent;"
                        + "npmScopes[*].npmAlwaysAuth=false",
                    ["registryNormalization"] =
                        "match project-local npmScopes[*].npmRegistryServer after normalizing "
                        + "terminal slashes",
                }
            ),
        };

        Assert.Equal(DoctorCheckStatus.Fail, check.Status);
        Assert.Equal(DoctorCheckSeverity.Error, check.Severity);
        Assert.Contains("same registry", check.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "project-local",
            check.SafeDetails["shadowingScope"],
            StringComparison.Ordinal
        );
        Assert.Equal("ci-temporary", check.SafeDetails["plannedScope"]);
        Assert.Contains(
            "npmRegistries[registry].npmAuthToken",
            check.SafeDetails["shadowingSelectors"],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "npmRegistries[registry].npmAuthIdent",
            check.SafeDetails["shadowingSelectors"],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "npmRegistries[registry].npmAlwaysAuth=false",
            check.SafeDetails["shadowingSelectors"],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "npmScopes[*].npmAuthToken",
            check.SafeDetails["shadowingSelectors"],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "npmScopes[*].npmAuthIdent",
            check.SafeDetails["shadowingSelectors"],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "npmScopes[*].npmAlwaysAuth=false",
            check.SafeDetails["shadowingSelectors"],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "normalizing",
            check.SafeDetails["registryNormalization"],
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ConfigurationChangePlanRepresentsCreateUpdateRefreshAndCiTemporaryRules()
    {
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-ci-npm-auth",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            ExpiresAt = DateTimeOffset.Parse("2026-06-06T12:00:00Z", CultureInfo.InvariantCulture),
            Manifest = CreateManifest("ci-npm-auth"),
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = @"C:\agent\_temp\azureauth-credprovider\.npmrc",
                ActivationEnvironment = CreateNpmrcFileActivationEnvironment(
                    @"C:\agent\_temp\azureauth-credprovider\.npmrc"
                ),
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create),
                CreateConfigurationChange(ConfigurationChangeOperation.Update) with
                { },
                CreateConfigurationChange(ConfigurationChangeOperation.Refresh) with
                { },
            ],
        };

        Assert.Equal(ConfigurationScope.CiTemporary, plan.Scope);
        Assert.Equal(
            ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            plan.DeclarationPreservation
        );
        Assert.Equal("windows", plan.TemporaryContainer?.ActivationEnvironment?.Platform);
        Assert.Equal(
            @"C:\agent\_temp\azureauth-credprovider\.npmrc",
            plan.TemporaryContainer?.ActivationEnvironment?.SetVariables["NPM_CONFIG_USERCONFIG"]
        );
        Assert.DoesNotContain(
            "npm_config_userconfig",
            plan.TemporaryContainer?.ActivationEnvironment?.SetVariables.Keys!
        );
        Assert.Empty(
            plan.TemporaryContainer?.ActivationEnvironment?.ClearVariables ?? ["unexpected"]
        );
        Assert.Contains(
            plan.Changes,
            change => change.Operation == ConfigurationChangeOperation.Create
        );
        Assert.Contains(
            plan.Changes,
            change => change.Operation == ConfigurationChangeOperation.Update
        );
        Assert.Contains(
            plan.Changes,
            change => change.Operation == ConfigurationChangeOperation.Refresh
        );
        Assert.All(plan.Changes, change => Assert.True(change.PreserveDeclarationsAndComments));
    }

    [Fact]
    public void ConfigurationScopeRepresentsGlobalPersistentFiles()
    {
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-global-yarn-auth",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.Global,
            Manifest = CreateManifest("global-yarn-auth"),
        };

        Assert.Equal(ConfigurationScope.Global, plan.Scope);
    }

    [Fact]
    public void ConfigurationPlanContractSurfaceKeepsFrozenRemoveExplicitPathMembers()
    {
        var explicitPathRemovePlan = new ConfigurationChangePlan
        {
            PlanId = "plan-explicit-path-remove",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.ExplicitPath,
            Manifest = CreateManifest("explicit-path-remove"),
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Remove) with
                {
                    TargetKind = ConfigurationTargetKind.Npmrc,
                    TargetPathOrName = @"C:\repo\.npmrc",
                    Value = null,
                    IsSecretValue = false,
                },
            ],
        };
        var workspaceReadOnlyPlan = explicitPathRemovePlan with
        {
            PlanId = "plan-workspace-read-only",
            Scope = ConfigurationScope.WorkspaceReadOnly,
            Changes = [],
        };

        Assert.Equal(
            ConfigurationChangeOperation.Remove,
            explicitPathRemovePlan.Changes.Single().Operation
        );
        Assert.Equal(ConfigurationScope.ExplicitPath, explicitPathRemovePlan.Scope);
        Assert.Equal(ConfigurationScope.WorkspaceReadOnly, workspaceReadOnlyPlan.Scope);
        Assert.Equal(
            ConfigurationTargetKind.Npmrc,
            explicitPathRemovePlan.Changes.Single().TargetKind
        );
        Assert.Empty(workspaceReadOnlyPlan.Changes);
        Assert.Equal("Remove", Enum.GetName(ConfigurationChangeOperation.Remove));
        Assert.Equal("ExplicitPath", Enum.GetName(ConfigurationScope.ExplicitPath));
        Assert.Equal("WorkspaceReadOnly", Enum.GetName(ConfigurationScope.WorkspaceReadOnly));
        Assert.True(Enum.IsDefined(ConfigurationChangeOperation.Remove));
        Assert.True(Enum.IsDefined(ConfigurationScope.ExplicitPath));
        Assert.True(Enum.IsDefined(ConfigurationScope.WorkspaceReadOnly));
    }

    [Fact]
    public void ConfigurationChangePlanPolicyRejectsWorkspaceReadOnlyChanges()
    {
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-workspace-read-only-with-change",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.WorkspaceReadOnly,
            Manifest = CreateManifest("workspace-read-only-with-change"),
            Changes =
            [
                CreateYarnAlwaysAuthChange("https://pkgs.dev.azure.com/org/_packaging/feed/npm"),
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "workspace read-only",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
        Assert.Throws<ArgumentException>(() =>
            ConfigurationChangePlanPolicy.Create(
                "plan-workspace-read-only-factory",
                "azureauth-credprovider",
                ConfigurationScope.WorkspaceReadOnly,
                CreateManifest("workspace-read-only-factory"),
                [CreateYarnAlwaysAuthChange("https://pkgs.dev.azure.com/org/_packaging/feed/npm")]
            )
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyDerivesAndValidatesCredentialMaterialFlag()
    {
        ConfigurationChangePlan validSecretPlan = CreateYarnSecretAuthTokenPlan();
        ConfigurationChange secretChange = Assert.Single(validSecretPlan.Changes);
        ConfigurationChange intrinsicSecretChange = secretChange with { IsSecretValue = false };

        ConfigurationChangePlan derivedPlan = ConfigurationChangePlanPolicy.Create(
            "plan-secret-material-derived",
            "azureauth-credprovider",
            ConfigurationScope.User,
            validSecretPlan.Manifest with { ManifestId = "manifest-secret-material-derived" },
            [secretChange]
        );
        var inconsistentPlan = derivedPlan with { ContainsCredentialMaterial = false };

        Assert.True(derivedPlan.ContainsCredentialMaterial);
        Assert.False(ConfigurationChangePlanPolicy.IsValid(inconsistentPlan));
        Assert.Contains(
            "credential material",
            ConfigurationChangePlanPolicy.GetViolation(inconsistentPlan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() =>
            ConfigurationChangePlanPolicy.EnsureValid(inconsistentPlan)
        );
        Assert.Throws<ArgumentException>(() =>
            ConfigurationChangePlanPolicy.Create(
                "plan-secret-material-false",
                "azureauth-credprovider",
                ConfigurationScope.User,
                validSecretPlan.Manifest with { ManifestId = "manifest-secret-material-false" },
                [secretChange],
                containsCredentialMaterial: false
            )
        );
        Assert.DoesNotContain(
            "secret-token",
            intrinsicSecretChange.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyFactoryRejectsNullChangeEntries()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            ConfigurationChangePlanPolicy.Create(
                "plan-null-change-entry",
                "azureauth-credprovider",
                ConfigurationScope.User,
                CreateManifest("null-change-entry"),
                [null!]
            )
        );

        Assert.Contains("null entries", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        ConfigurationTargetKind.Npmrc,
        "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken"
    )]
    [InlineData(ConfigurationTargetKind.Npmrc, "_authToken")]
    [InlineData(
        ConfigurationTargetKind.Yarnrc,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthToken"
    )]
    [InlineData(ConfigurationTargetKind.Yarnrc, "npmAuthToken")]
    public void ConfigurationChangePlanPolicyRejectsIntrinsicNpmCompatibleAuthTokensNotMarkedSecret(
        ConfigurationTargetKind targetKind,
        string key
    )
    {
        ConfigurationChange change = CreateConfigurationChange(
            ConfigurationChangeOperation.Create
        ) with
        {
            TargetKind = targetKind,
            TargetPathOrName =
                targetKind == ConfigurationTargetKind.Yarnrc ? "user .yarnrc.yml" : "user .npmrc",
            Key = key,
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            ContainsCredentialMaterial = true,
            Changes = [change],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "marked as secret",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("secret-token", change.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData("_authToken")]
    [InlineData("//evil.example/org/_packaging/feed/npm/registry/:_authToken")]
    [InlineData("//pkgs.dev.azure.com/org/_packaging/feed/npm/:_authToken")]
    // editorconfig-checker-disable
    public void ConfigurationChangePlanPolicyRejectsNpmrcSecretAuthTokenKeysThatDoNotMatchCanonicalSelector(
        string key
    )
    // editorconfig-checker-enable
    {
        const string canonicalSelector =
            "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken";
        ConfigurationChange change = CreateConfigurationChange(
            ConfigurationChangeOperation.Create
        ) with
        {
            Key = key,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            ContainsCredentialMaterial = true,
            Manifest = CreateManifest("npm-secret-selector") with
            {
                EntrySelector = canonicalSelector,
                ResourceIdentity = CreateNpmResourceIdentity(),
            },
            Changes = [change],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        string? violation = ConfigurationChangePlanPolicy.GetViolation(plan);
        Assert.NotNull(violation);
        Assert.Contains("canonical registry identity", violation, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData("reg#istry")]
    [InlineData("reg;istry")]
    public void ConfigurationChangePlanPolicyRejectsNpmrcKeysContainingCommentMarkers(string key)
    {
        ConfigurationChange change = CreateConfigurationChange(
            ConfigurationChangeOperation.Create
        ) with
        {
            Key = key,
            Value = "planned-value",
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with { Changes = [change] };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "comment markers",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData("\"registry\"")]
    [InlineData("'registry'")]
    public void ConfigurationChangePlanPolicyRejectsQuotedNpmrcKeys(string key)
    {
        ConfigurationChange change = CreateConfigurationChange(
            ConfigurationChangeOperation.Create
        ) with
        {
            Key = key,
            Value = "planned-value",
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with { Changes = [change] };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "quoted",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData("\"planned-value\"")]
    [InlineData("'planned-value'")]
    public void ConfigurationChangePlanPolicyRejectsQuotedNpmrcValues(string value)
    {
        ConfigurationChange change = CreateConfigurationChange(
            ConfigurationChangeOperation.Create
        ) with
        {
            Key = "registry",
            Value = value,
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with { Changes = [change] };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "quoted",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData("//evil.example/org/_packaging/feed/npm/registry/:_authToken")]
    [InlineData("//pkgs.dev.azure.com/org/_packaging/feed/npm/:_authToken")]
    public void ConfigurationChangePlanPolicyRejectsCiTemporaryNpmrcSecretAuthTokenSelectorMismatch(
        string key
    )
    {
        const string canonicalSelector =
            "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken";
        const string npmrcPath = "/agent/_temp/azureauth-credprovider/.npmrc";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = CreateNpmrcFileContainer(npmrcPath),
            ContainsCredentialMaterial = true,
            Manifest = CreateManifest("npm-ci-secret-selector") with
            {
                EntrySelector = canonicalSelector,
                ResourceIdentity = CreateNpmResourceIdentity(),
            },
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                    Key = key,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        string? violation = ConfigurationChangePlanPolicy.GetViolation(plan);
        Assert.NotNull(violation);
        Assert.Contains("canonical registry identity", violation, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Fact]
    // editorconfig-checker-disable
    public void ConfigurationChangePlanPolicyAcceptsCiTemporaryNpmrcSecretAuthTokenCanonicalSelector()
    // editorconfig-checker-enable
    {
        const string canonicalSelector =
            "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken";
        const string npmrcPath = "/agent/_temp/azureauth-credprovider/.npmrc";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = CreateNpmrcFileContainer(npmrcPath),
            ContainsCredentialMaterial = true,
            Manifest = CreateManifest("npm-ci-secret-selector") with
            {
                EntrySelector = canonicalSelector,
                ResourceIdentity = CreateNpmResourceIdentity(),
            },
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                    Key = canonicalSelector,
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Null(ConfigurationChangePlanPolicy.GetViolation(plan));
    }

    [Theory]
    [InlineData("missing-resource")]
    [InlineData("manifest-selector")]
    [InlineData("change-key")]
    public void ConfigurationChangePlanPolicyRejectsYarnSecretAuthTokenBindingMismatch(
        string mismatch
    )
    {
        ConfigurationChangePlan validPlan = CreateYarnSecretAuthTokenPlan();
        Assert.True(ConfigurationChangePlanPolicy.IsValid(validPlan));
        Assert.Null(ConfigurationChangePlanPolicy.GetViolation(validPlan));

        ConfigurationChangePlan plan = mismatch switch
        {
            "missing-resource" => validPlan with
            {
                Manifest = validPlan.Manifest with { ResourceIdentity = null },
            },
            "manifest-selector" => validPlan with
            {
                Manifest = validPlan.Manifest with
                {
                    EntrySelector =
                        "npmRegistries.https://pkgs.dev.azure.com/org/_packaging/other/npm/"
                        + "registry/.npmAuthToken",
                },
            },
            "change-key" => validPlan with
            {
                Changes =
                [
                    Assert.Single(validPlan.Changes) with
                    {
                        Key =
                            "npmRegistries.https://pkgs.dev.azure.com/org/_packaging/other/npm/"
                            + "registry/.npmAuthToken",
                    },
                ],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null),
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        string violation = Assert.IsType<string>(
            ConfigurationChangePlanPolicy.GetViolation(plan)
        );
        Assert.Contains("canonical registry identity", violation, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Set)]
    [InlineData(ConfigurationChangeOperation.Create)]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Refresh)]
    public void ConfigurationChangePlanPolicyBindsEveryYarnSecretAuthTokenValueWrite(
        ConfigurationChangeOperation operation
    )
    {
        ConfigurationChangePlan validPlan = CreateYarnSecretAuthTokenPlan();
        ConfigurationChangePlan plan = validPlan with
        {
            Manifest = validPlan.Manifest with { ResourceIdentity = null },
            Changes =
            [
                Assert.Single(validPlan.Changes) with
                {
                    Operation = operation,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        string violation = Assert.IsType<string>(
            ConfigurationChangePlanPolicy.GetViolation(plan)
        );
        Assert.Contains("canonical registry identity", violation, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Fact]
    public void ConfigurationChangePlanPolicyScopesYarnBindingToSecretAuthTokenValueWrites()
    {
        ConfigurationChangePlan validPlan = CreateYarnSecretAuthTokenPlan();
        ConfigurationChangePlan nonSecretAlwaysAuthPlan = validPlan with
        {
            Manifest = validPlan.Manifest with
            {
                EntrySelector = "unbound-npm-always-auth",
                ResourceIdentity = null,
            },
            ContainsCredentialMaterial = false,
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"
                ),
            ],
        };
        ConfigurationChangePlan removeOnlyAuthTokenPlan = validPlan with
        {
            Manifest = validPlan.Manifest with { ResourceIdentity = null },
            Changes =
            [
                Assert.Single(validPlan.Changes) with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    Value = null,
                },
            ],
        };
        ConfigurationChangePlan secretAlwaysAuthPlan = validPlan with
        {
            Manifest = validPlan.Manifest with
            {
                EntrySelector = "unbound-secret-npm-always-auth",
                ResourceIdentity = null,
            },
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"
                ) with
                {
                    IsSecretValue = true,
                },
            ],
        };

        Assert.All(
            [nonSecretAlwaysAuthPlan, removeOnlyAuthTokenPlan, secretAlwaysAuthPlan],
            plan =>
            {
                Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
                Assert.Null(ConfigurationChangePlanPolicy.GetViolation(plan));
            }
        );
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Set, "npmAuthIdent")]
    [InlineData(
        ConfigurationChangeOperation.Set,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthIdent"
    )]
    [InlineData(ConfigurationChangeOperation.Create, "npmAuthIdent")]
    [InlineData(
        ConfigurationChangeOperation.Create,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthIdent"
    )]
    [InlineData(ConfigurationChangeOperation.Update, "npmAuthIdent")]
    [InlineData(
        ConfigurationChangeOperation.Update,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthIdent"
    )]
    [InlineData(ConfigurationChangeOperation.Refresh, "npmAuthIdent")]
    [InlineData(
        ConfigurationChangeOperation.Refresh,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthIdent"
    )]
    [InlineData(ConfigurationChangeOperation.Remove, "npmAuthIdent")]
    [InlineData(
        ConfigurationChangeOperation.Remove,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthIdent"
    )]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter, "npmAuthIdent")]
    [InlineData(
        ConfigurationChangeOperation.RemoveAdapter,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthIdent"
    )]
    [InlineData(ConfigurationChangeOperation.EnsureFile, "npmAuthIdent")]
    [InlineData(
        ConfigurationChangeOperation.EnsureFile,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthIdent"
    )]
    [InlineData(ConfigurationChangeOperation.InstallAdapter, "npmAuthIdent")]
    [InlineData(
        ConfigurationChangeOperation.InstallAdapter,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthIdent"
    )]
    public void ConfigurationChangePlanPolicyRejectsYarnNpmAuthIdentPlanEntries(
        ConfigurationChangeOperation operation,
        string key
    )
    {
        bool requiresValue =
            operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;
        ConfigurationChange change = CreateConfigurationChange(operation) with
        {
            TargetKind = ConfigurationTargetKind.Yarnrc,
            TargetPathOrName = "user .yarnrc.yml",
            Key = key,
            Value = requiresValue ? "AzureDevOps:secret-token" : null,
            IsSecretValue = true,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            ContainsCredentialMaterial = true,
            Changes = [change],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "unsupported",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("secret-token", change.ToString(), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Set)]
    [InlineData(ConfigurationChangeOperation.Create)]
    [InlineData(ConfigurationChangeOperation.Update)]
    [InlineData(ConfigurationChangeOperation.Refresh)]
    public void ConfigurationChangePlanPolicyRejectsNullValuesForValueWritingChanges(
        ConfigurationChangeOperation operation
    )
    {
        ConfigurationChange change = CreateConfigurationChange(operation) with
        {
            Value = null,
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with { Changes = [change] };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "value",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Fact]
    public void ConfigurationChangePlanPolicyAllowsNullValuesForRemoveStyleAndNonValueChanges()
    {
        ConfigurationChange CreateNonValueChange(
            ConfigurationChangeOperation changeOperation,
            string? value
        ) =>
            changeOperation switch
            {
                ConfigurationChangeOperation.Remove => CreateConfigurationChange(
                    changeOperation
                ) with
                {
                    Key = "registry",
                    Value = value,
                    IsSecretValue = false,
                },
                _ => CreateConfigurationChange(changeOperation) with
                {
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = "user .gitconfig",
                    Key = "credential.helper",
                    Value = value,
                    IsSecretValue = false,
                },
            };

        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Changes =
            [
                CreateNonValueChange(ConfigurationChangeOperation.Remove, null),
                CreateNonValueChange(ConfigurationChangeOperation.RemoveAdapter, null),
                CreateNonValueChange(ConfigurationChangeOperation.EnsureFile, null),
                CreateNonValueChange(ConfigurationChangeOperation.InstallAdapter, null),
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.Remove)]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter)]
    [InlineData(ConfigurationChangeOperation.EnsureFile)]
    [InlineData(ConfigurationChangeOperation.InstallAdapter)]
    public void ConfigurationChangePlanPolicyRejectsValuesForNonValueChanges(
        ConfigurationChangeOperation operation
    )
    {
        ConfigurationChange CreateNonValueChange(
            ConfigurationChangeOperation changeOperation,
            string value
        ) =>
            changeOperation switch
            {
                ConfigurationChangeOperation.Remove => CreateConfigurationChange(
                    changeOperation
                ) with
                {
                    Key = "registry",
                    Value = value,
                    IsSecretValue = false,
                },
                _ => CreateConfigurationChange(changeOperation) with
                {
                    TargetKind = ConfigurationTargetKind.GitConfig,
                    TargetPathOrName = "user .gitconfig",
                    Key = "credential.helper",
                    Value = value,
                    IsSecretValue = false,
                },
            };

        ConfigurationChange change = CreateNonValueChange(operation, "helper-value");
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with { Changes = [change] };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "must not carry a value",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData(ConfigurationChangeOperation.EnsureFile)]
    [InlineData(ConfigurationChangeOperation.InstallAdapter)]
    [InlineData(ConfigurationChangeOperation.RemoveAdapter)]
    public void ConfigurationChangePlanPolicyRejectsUnsupportedNpmrcOperations(
        ConfigurationChangeOperation operation
    )
    {
        ConfigurationChange change = CreateConfigurationChange(operation) with
        {
            Value = null,
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with { Changes = [change] };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        string? violation = ConfigurationChangePlanPolicy.GetViolation(plan);
        Assert.NotNull(violation);
        Assert.Contains("unsupported", violation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            operation switch
            {
                ConfigurationChangeOperation.EnsureFile => "ensure-file",
                ConfigurationChangeOperation.InstallAdapter => "install-adapter",
                ConfigurationChangeOperation.RemoveAdapter => "remove-adapter",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
            },
            violation,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData(
        ConfigurationTargetKind.Npmrc,
        "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
        "secret\ntoken"
    )]
    [InlineData(ConfigurationTargetKind.Npmrc, "_authToken", "secret\rtoken")]
    [InlineData(
        ConfigurationTargetKind.Yarnrc,
        "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"].npmAuthToken",
        "secret\ntoken"
    )]
    [InlineData(ConfigurationTargetKind.Yarnrc, "npmAuthToken", "secret\rtoken")]
    public void ConfigurationChangePlanPolicyRejectsLineBreaksInNpmCompatibleAuthValuesBySelector(
        ConfigurationTargetKind targetKind,
        string key,
        string value
    )
    {
        ConfigurationChange change = CreateConfigurationChange(
            ConfigurationChangeOperation.Create
        ) with
        {
            TargetKind = targetKind,
            TargetPathOrName =
                targetKind == ConfigurationTargetKind.Yarnrc ? "user .yarnrc.yml" : "user .npmrc",
            Key = key,
            Value = value,
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            ContainsCredentialMaterial = true,
            Changes = [change],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "CR or LF",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.Ordinal
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData(
        ConfigurationTargetKind.GitConfig,
        "credential.https://dev.azure.com.helper",
        "helper\nother"
    )]
    [InlineData(ConfigurationTargetKind.Npmrc, "always-auth", "true\n_authToken=secret")]
    [InlineData(ConfigurationTargetKind.Yarnrc, "npmAlwaysAuth", "true\rnpmAuthToken: secret")]
    public void ConfigurationChangePlanPolicyRejectsLineBreaksInNonSecretLineConfigurationValues(
        ConfigurationTargetKind targetKind,
        string key,
        string value
    )
    {
        ConfigurationChange change = CreateConfigurationChange(
            ConfigurationChangeOperation.Create
        ) with
        {
            TargetKind = targetKind,
            TargetPathOrName = targetKind switch
            {
                ConfigurationTargetKind.GitConfig => "user .gitconfig",
                ConfigurationTargetKind.Yarnrc => "user .yarnrc.yml",
                _ => "user .npmrc",
            },
            Key = key,
            Value = value,
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            ContainsCredentialMaterial = false,
            Changes = [change],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "CR or LF",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.Ordinal
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Theory]
    [InlineData("always-auth\ntrue")]
    [InlineData("always-auth\rtrue")]
    [InlineData(
        "always-auth\n//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken=secret-"
            + "token"
    )]
    public void ConfigurationChangePlanPolicyRejectsLineBreaksInConfigurationKeys(string key)
    {
        ConfigurationChange change = CreateConfigurationChange(
            ConfigurationChangeOperation.Create
        ) with
        {
            Key = key,
            Value = "true",
            IsSecretValue = false,
        };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            ContainsCredentialMaterial = false,
            Changes = [change],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "CR or LF",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.Ordinal
        );
        Assert.Throws<ArgumentException>(() => ConfigurationChangePlanPolicy.EnsureValid(plan));
    }

    [Fact]
    public void ConfigurationChangePlanPolicyRejectsFrozenPlanShapeViolations()
    {
        ConfigurationChangePlan valid = CreateValidConfigurationPlan();

        ConfigurationChangePlan[] invalidPlans =
        [
            valid with
            {
                PlanId = "",
            },
            valid with
            {
                OwnerProductId = "",
            },
            valid with
            {
                Scope = ConfigurationScope.Unspecified,
            },
            valid with
            {
                Scope = (ConfigurationScope)999,
            },
            valid with
            {
                DeclarationPreservation = ConfigurationDeclarationPreservation.Unspecified,
            },
            valid with
            {
                DeclarationPreservation =
                    ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            },
            valid with
            {
                Manifest = null!,
            },
            valid with
            {
                Manifest = valid.Manifest with { ManifestId = "" },
            },
            valid with
            {
                Manifest = valid.Manifest with { OwnerProductId = "other-product" },
            },
            valid with
            {
                Manifest = valid.Manifest with { EntrySelector = " " },
            },
            valid with
            {
                Changes = null!,
            },
            valid with
            {
                Changes = [null!],
            },
            valid with
            {
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        Operation = ConfigurationChangeOperation.Unspecified,
                    },
                ],
            },
            valid with
            {
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        Operation = (ConfigurationChangeOperation)999,
                    },
                ],
            },
            valid with
            {
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        TargetKind = ConfigurationTargetKind.Unspecified,
                    },
                ],
            },
            valid with
            {
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        TargetKind = (ConfigurationTargetKind)999,
                    },
                ],
            },
            valid with
            {
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        TargetPathOrName = "",
                    },
                ],
            },
            valid with
            {
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        Key = " ",
                    },
                ],
            },
            valid with
            {
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        RequiresOwnershipRecord = false,
                    },
                ],
            },
        ];

        Assert.All(
            invalidPlans,
            plan =>
            {
                Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
                Assert.Throws<ArgumentException>(() =>
                    ConfigurationChangePlanPolicy.EnsureValid(plan)
                );
            }
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyValidatesCiTemporaryContainerScopeCombinations()
    {
        const string npmrcPath = @"C:\agent\_temp\azureauth-credprovider\.npmrc";
        ConfigurationChangePlan ciTemporaryPlan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = npmrcPath,
                ActivationEnvironment = CreateNpmrcFileActivationEnvironment(npmrcPath),
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };
        ConfigurationChangePlan temporaryHomePlan = ciTemporaryPlan with
        {
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(
                @"C:\agent\_temp\azureauth-credprovider\yarn-home"
            ),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
            ],
        };
        ConfigurationChangePlan standaloneYarnRcFilePlan = ciTemporaryPlan with
        {
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.YarnRcFile,
                ProductOwnedPath = @"C:\agent\_temp\azureauth-credprovider\.yarnrc.yml",
            },
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName = @"C:\agent\_temp\azureauth-credprovider\.yarnrc.yml",
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(ciTemporaryPlan));
        Assert.True(ConfigurationChangePlanPolicy.IsValid(temporaryHomePlan));
        Assert.False(ConfigurationChangePlanPolicy.IsValid(standaloneYarnRcFilePlan));
        Assert.Contains(
            "valid product-owned temporary container",
            ConfigurationChangePlanPolicy.GetViolation(standaloneYarnRcFilePlan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                ciTemporaryPlan with
                {
                    TemporaryContainer = null,
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                ciTemporaryPlan with
                {
                    DeclarationPreservation = ConfigurationDeclarationPreservation.NotApplicable,
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                ciTemporaryPlan with
                {
                    TemporaryContainer = ciTemporaryPlan.TemporaryContainer with
                    {
                        Kind = ConfigurationTemporaryContainerKind.Unspecified,
                    },
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                ciTemporaryPlan with
                {
                    TemporaryContainer = ciTemporaryPlan.TemporaryContainer with
                    {
                        Kind = ConfigurationTemporaryContainerKind.None,
                    },
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                ciTemporaryPlan with
                {
                    TemporaryContainer = ciTemporaryPlan.TemporaryContainer with
                    {
                        ProductOwnedPath = null!,
                    },
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                ciTemporaryPlan with
                {
                    TemporaryContainer = ciTemporaryPlan.TemporaryContainer with
                    {
                        ProductOwnedPath = "",
                    },
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                ciTemporaryPlan with
                {
                    TemporaryContainer = ciTemporaryPlan.TemporaryContainer with
                    {
                        ProductOwnedPath = " ",
                    },
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                ciTemporaryPlan with
                {
                    Scope = ConfigurationScope.User,
                }
            )
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyBindsCiTemporaryChangesToDeclaredContainer()
    {
        const string npmrcPath = @"C:\agent\_temp\azureauth-credprovider\.npmrc";
        const string yarnHomePath = @"C:\agent\_temp\azureauth-credprovider\yarn-home";
        ConfigurationChangePlan npmrcFilePlan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = npmrcPath,
                ActivationEnvironment = CreateNpmrcFileActivationEnvironment(npmrcPath),
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };
        ConfigurationChangePlan yarnHomePlan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(yarnHomePath),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
            ],
        };
        ConfigurationChangePlan standaloneYarnRcFilePlan = yarnHomePlan with
        {
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.YarnRcFile,
                ProductOwnedPath = @"C:\agent\_temp\azureauth-credprovider\.yarnrc.yml",
            },
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName = @"C:\agent\_temp\azureauth-credprovider\.yarnrc.yml",
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(npmrcFilePlan));
        Assert.True(ConfigurationChangePlanPolicy.IsValid(yarnHomePlan));
        Assert.False(ConfigurationChangePlanPolicy.IsValid(standaloneYarnRcFilePlan));
        Assert.Contains(
            "valid product-owned temporary container",
            ConfigurationChangePlanPolicy.GetViolation(standaloneYarnRcFilePlan),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                npmrcFilePlan with
                {
                    Changes =
                    [
                        CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                        {
                            TargetPathOrName = @"C:\Users\runneradmin\.npmrc",
                        },
                    ],
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                npmrcFilePlan with
                {
                    Changes =
                    [
                        CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                        {
                            TargetPathOrName = @"C:\agent\_temp\azureauth-credprovider\other.npmrc",
                        },
                    ],
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                yarnHomePlan with
                {
                    Changes =
                    [
                        CreateYarnAlwaysAuthChange(
                            "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                        ) with
                        {
                            TargetPathOrName = @"C:\repo\.yarnrc.yml",
                        },
                    ],
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                yarnHomePlan with
                {
                    Changes =
                    [
                        CreateYarnAlwaysAuthChange(
                            "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                        ) with
                        {
                            TargetPathOrName =
                                "C:\\agent\\_temp\\azureauth-credprovider\\"
                                + "yarn-home-sibling\\.yarnrc.yml",
                        },
                    ],
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                yarnHomePlan with
                {
                    Changes =
                    [
                        CreateYarnAlwaysAuthChange(
                            "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                        ) with
                        {
                            TargetPathOrName =
                                "C:\\agent\\_temp\\azureauth-credprovider\\"
                                + "yarn-home\\nested\\.yarnrc.yml",
                        },
                    ],
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                yarnHomePlan with
                {
                    Changes =
                    [
                        CreateYarnAlwaysAuthChange(
                            "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                        ) with
                        {
                            TargetPathOrName =
                                @"C:\agent\_temp\azureauth-credprovider\yarn-home\other.yml",
                        },
                    ],
                }
            )
        );
        Assert.False(
            ConfigurationChangePlanPolicy.IsValid(
                standaloneYarnRcFilePlan with
                {
                    Changes =
                    [
                        CreateYarnAlwaysAuthChange(
                            "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                        ) with
                        {
                            TargetPathOrName = @"C:\repo\.yarnrc.yml",
                        },
                    ],
                }
            )
        );
    }

    [Theory]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\yarn-home",
        @"C:\agent\_temp\azureauth-credprovider\yarn-home\nested\.yarnrc.yml"
    )]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\yarn-home",
        @"C:\agent\_temp\azureauth-credprovider\yarn-home\other.yml"
    )]
    [InlineData(
        "/agent/_temp/azureauth-credprovider/yarn-home",
        "/agent/_temp/azureauth-credprovider/yarn-home/nested/.yarnrc.yml"
    )]
    [InlineData(
        "/agent/_temp/azureauth-credprovider/yarn-home",
        "/agent/_temp/azureauth-credprovider/yarn-home/other.yml"
    )]
    [InlineData(
        "//agent-share/temp/azureauth-credprovider/yarn-home",
        "//agent-share/temp/azureauth-credprovider/yarn-home/nested/.yarnrc.yml"
    )]
    [InlineData(
        "//agent-share/temp/azureauth-credprovider/yarn-home",
        "//agent-share/temp/azureauth-credprovider/yarn-home/other.yml"
    )]
    public void ConfigurationChangePlanPolicyRejectsCiTemporaryHomeYarnrcTargetsOutsideChild(
        string productOwnedPath,
        string targetPath
    )
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(productOwnedPath),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName = targetPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "declared product-owned temporary container",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyAcceptsUnixStyleCiTemporaryHomePaths()
    {
        const string yarnHomePath = "/agent/_temp/azureauth-credprovider/yarn-home";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(yarnHomePath),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName = "/agent/_temp/azureauth-credprovider/yarn-home/.yarnrc.yml",
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
    }

    [Fact]
    public void ConfigurationChangePlanPolicyPinsWindowsYarnCiTemporaryHomeActivationEnvironment()
    {
        const string yarnHomePath = @"C:\agent\_temp\azureauth-credprovider\yarn-home";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(yarnHomePath),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
            ],
        };

        Assert.NotNull(plan.TemporaryContainer?.ActivationEnvironment);
        ConfigurationActivationEnvironment activationEnvironment =
            plan.TemporaryContainer.ActivationEnvironment;
        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Equal(yarnHomePath, activationEnvironment.SetVariables["USERPROFILE"]);
        Assert.Equal(yarnHomePath, activationEnvironment.SetVariables["HOME"]);
        Assert.Equal(
            ["HOMEDRIVE", "HOMEPATH", "YARN_RC_FILENAME"],
            activationEnvironment.ClearVariables
        );
        Assert.DoesNotContain("USERPROFILE", activationEnvironment.ClearVariables);
    }

    [Theory]
    [InlineData("USERPROFILE", null, null, null, "HOMEDRIVE", "HOMEPATH")]
    [InlineData("USERPROFILE", "C:\\other", "HOME", null, "HOMEDRIVE", "HOMEPATH")]
    [InlineData("USERPROFILE", null, "HOME", null, "HOMEDRIVE", null)]
    [InlineData("USERPROFILE", null, "HOME", null, null, "HOMEPATH")]
    public void PlanPolicyRejectsIncompleteWindowsYarnHomeActivation(
        string? firstSetVariable,
        string? firstSetValue,
        string? secondSetVariable,
        string? secondSetValue,
        string? firstClearVariable,
        string? secondClearVariable
    )
    {
        const string yarnHomePath = @"C:\agent\_temp\azureauth-credprovider\yarn-home";
        var setVariables = new Dictionary<string, string>();
        if (firstSetVariable is not null)
        {
            setVariables[firstSetVariable] = firstSetValue ?? yarnHomePath;
        }

        if (secondSetVariable is not null)
        {
            setVariables[secondSetVariable] = secondSetValue ?? yarnHomePath;
        }

        string[] clearVariables = new[] { firstClearVariable, secondClearVariable }
            .Where(variable => variable is not null)
            .Cast<string>()
            .ToArray();
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.TemporaryHome,
                ProductOwnedPath = yarnHomePath,
                ActivationEnvironment = new ConfigurationActivationEnvironment
                {
                    SetVariables = setVariables,
                    ClearVariables = clearVariables,
                },
            },
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "Windows CI temporary HOME activation",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyPinsPosixYarnCiTemporaryHomeActivationEnvironment()
    {
        const string yarnHomePath = "/agent/_temp/azureauth-credprovider/yarn-home";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(yarnHomePath),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName = "/agent/_temp/azureauth-credprovider/yarn-home/.yarnrc.yml",
                },
            ],
        };

        Assert.NotNull(plan.TemporaryContainer?.ActivationEnvironment);
        ConfigurationActivationEnvironment activationEnvironment =
            plan.TemporaryContainer.ActivationEnvironment;
        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Equal(yarnHomePath, activationEnvironment.SetVariables["HOME"]);
        Assert.DoesNotContain("USERPROFILE", activationEnvironment.SetVariables.Keys);
        Assert.DoesNotContain("HOMEDRIVE", activationEnvironment.SetVariables.Keys);
        Assert.DoesNotContain("HOMEPATH", activationEnvironment.SetVariables.Keys);
        Assert.Equal(["YARN_RC_FILENAME"], activationEnvironment.ClearVariables);
    }

    [Fact]
    public void ConfigurationChangePlanPolicyPinsNpmrcFileCiTemporaryActivationEnvironment()
    {
        const string npmrcPath = @"C:\agent\_temp\azureauth-credprovider\.npmrc";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = CreateNpmrcFileContainer(npmrcPath),
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Equal("windows", plan.TemporaryContainer?.ActivationEnvironment?.Platform);
        Assert.Equal(
            npmrcPath,
            plan.TemporaryContainer?.ActivationEnvironment?.SetVariables["NPM_CONFIG_USERCONFIG"]
        );
        Assert.DoesNotContain(
            "npm_config_userconfig",
            plan.TemporaryContainer?.ActivationEnvironment?.SetVariables.Keys!
        );
        Assert.Empty(
            plan.TemporaryContainer?.ActivationEnvironment?.ClearVariables ?? ["unexpected"]
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyPinsWindowsUncNpmrcFileCiTemporaryEnvironment()
    {
        const string npmrcPath = "//agent-share/temp/azureauth-credprovider/.npmrc";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = CreateNpmrcFileContainer(npmrcPath),
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Equal("windows", plan.TemporaryContainer?.ActivationEnvironment?.Platform);
        Assert.Equal(
            npmrcPath,
            plan.TemporaryContainer?.ActivationEnvironment?.SetVariables["NPM_CONFIG_USERCONFIG"]
        );
        Assert.DoesNotContain(
            "npm_config_userconfig",
            plan.TemporaryContainer?.ActivationEnvironment?.SetVariables.Keys!
        );
        Assert.Empty(
            plan.TemporaryContainer?.ActivationEnvironment?.ClearVariables ?? ["unexpected"]
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyPinsPosixNpmrcFileCiTemporaryActivationEnvironment()
    {
        const string npmrcPath = "/agent/_temp/azureauth-credprovider/.npmrc";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = CreateNpmrcFileContainer(npmrcPath),
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Equal("posix", plan.TemporaryContainer?.ActivationEnvironment?.Platform);
        Assert.Equal(
            npmrcPath,
            plan.TemporaryContainer?.ActivationEnvironment?.SetVariables["NPM_CONFIG_USERCONFIG"]
        );
        Assert.Equal(
            npmrcPath,
            plan.TemporaryContainer?.ActivationEnvironment?.SetVariables["npm_config_userconfig"]
        );
        Assert.Empty(
            plan.TemporaryContainer?.ActivationEnvironment?.ClearVariables ?? ["unexpected"]
        );
    }

    [Fact]
    public void PlanPolicyRejectsPosixNpmrcActivationWithOnlyUppercaseUserConfig()
    {
        const string npmrcPath = "/agent/_temp/azureauth-credprovider/.npmrc";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = npmrcPath,
                ActivationEnvironment = new ConfigurationActivationEnvironment
                {
                    Platform = "posix",
                    SetVariables = new Dictionary<string, string>
                    {
                        ["NPM_CONFIG_USERCONFIG"] = npmrcPath,
                    },
                    ClearVariables = [],
                },
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "POSIX",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void PlanPolicyRejectsWindowsNpmrcActivationWithExtraLowercaseUserConfig()
    {
        const string npmrcPath = @"C:\agent\_temp\azureauth-credprovider\.npmrc";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = npmrcPath,
                ActivationEnvironment = new ConfigurationActivationEnvironment
                {
                    Platform = "windows",
                    SetVariables = new Dictionary<string, string>
                    {
                        ["NPM_CONFIG_USERCONFIG"] = npmrcPath,
                        ["npm_config_userconfig"] = npmrcPath,
                    },
                    ClearVariables = [],
                },
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "Windows",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("NPM_CONFIG_USERCONFIG", "C:\\other\\.npmrc", "npm_config_userconfig")]
    [InlineData("npm_config_userconfig", null, null)]
    public void ConfigurationChangePlanPolicyRejectsIncompleteNpmrcFileCiTemporaryEnvironment(
        string? firstSetVariable,
        string? firstSetValue,
        string? secondSetVariable
    )
    {
        const string npmrcPath = @"C:\agent\_temp\azureauth-credprovider\.npmrc";
        var setVariables = new Dictionary<string, string>();
        if (firstSetVariable is not null)
        {
            setVariables[firstSetVariable] = firstSetValue ?? npmrcPath;
        }

        if (secondSetVariable is not null)
        {
            setVariables[secondSetVariable] = npmrcPath;
        }

        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = npmrcPath,
                ActivationEnvironment =
                    firstSetVariable is null && secondSetVariable is null
                        ? null
                        : new ConfigurationActivationEnvironment
                        {
                            Platform = "windows",
                            SetVariables = setVariables,
                            ClearVariables = [],
                        },
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "npmrc file",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("linux")]
    public void ConfigurationChangePlanPolicyRejectsNpmrcFileCiTemporaryWithoutKnownPlatform(
        string? platform
    )
    {
        const string npmrcPath = @"C:\agent\_temp\azureauth-credprovider\.npmrc";
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = npmrcPath,
                ActivationEnvironment = new ConfigurationActivationEnvironment
                {
                    Platform = platform,
                    SetVariables = new Dictionary<string, string>
                    {
                        ["NPM_CONFIG_USERCONFIG"] = npmrcPath,
                    },
                    ClearVariables = [],
                },
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "platform",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(@"C:\agent\_temp\azureauth-credprovider\.npmrc", "posix")]
    [InlineData("//agent-share/temp/azureauth-credprovider/.npmrc", "posix")]
    [InlineData("/agent/_temp/azureauth-credprovider/.npmrc", "windows")]
    public void ConfigurationChangePlanPolicyRejectsNpmrcFileCiTemporaryPlatformKindMismatch(
        string npmrcPath,
        string platform
    )
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = npmrcPath,
                ActivationEnvironment = CreateNpmrcFileActivationEnvironment(npmrcPath) with
                {
                    Platform = platform,
                },
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = npmrcPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "product-owned path",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(
        @" C:\agent\_temp\azureauth-credprovider\.npmrc",
        @"C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\.npmrc ",
        @"C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\.npmrc",
        @" C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\.npmrc",
        @"C:\agent\_temp\azureauth-credprovider\.npmrc "
    )]
    public void ConfigurationChangePlanPolicyRejectsPaddedCiTemporaryContainerAndTargetPaths(
        string containerPath,
        string targetPath
    )
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = containerPath,
                ActivationEnvironment = CreateNpmrcFileActivationEnvironment(containerPath),
            },
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = targetPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "fully qualified canonical",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\\.npmrc",
        @"C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\.npmrc\",
        @"C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        @"C:/agent/_temp/azureauth-credprovider/.npmrc",
        @"C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\.npmrc",
        @"C:\agent\_temp\azureauth-credprovider\\.npmrc"
    )]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\.npmrc",
        @"C:\agent\_temp\azureauth-credprovider\.npmrc\"
    )]
    [InlineData(
        @"C:\agent\_temp\azureauth-credprovider\.npmrc",
        @"C:/agent/_temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData(
        "/agent//_temp/azureauth-credprovider/.npmrc",
        "/agent/_temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData(
        "/agent/_temp/azureauth-credprovider/.npmrc/",
        "/agent/_temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData(
        "/agent/_temp/azureauth-credprovider/.npmrc",
        "/agent//_temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData(
        "/agent/_temp/azureauth-credprovider/.npmrc",
        "/agent/_temp/azureauth-credprovider/.npmrc/"
    )]
    [InlineData(
        "//agent-share/temp/azureauth-credprovider//.npmrc",
        "//agent-share/temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData(
        "//agent-share/temp/azureauth-credprovider/.npmrc/",
        "//agent-share/temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData(
        @"\\agent-share\temp\azureauth-credprovider\.npmrc",
        "//agent-share/temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData(
        "//agent-share/temp/azureauth-credprovider/.npmrc",
        "//agent-share/temp/azureauth-credprovider//.npmrc"
    )]
    [InlineData(
        "//agent-share/temp/azureauth-credprovider/.npmrc",
        "//agent-share/temp/azureauth-credprovider/.npmrc/"
    )]
    [InlineData(
        "//agent-share/temp/azureauth-credprovider/.npmrc",
        @"\\agent-share\temp\azureauth-credprovider\.npmrc"
    )]
    public void ConfigurationChangePlanPolicyRejectsNonCanonicalCiTemporaryPathAliases(
        string containerPath,
        string targetPath
    )
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = containerPath,
                ActivationEnvironment = CreateNpmrcFileActivationEnvironment(containerPath),
            },
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = targetPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "fully qualified canonical",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(
        @"\\?\C:\agent\_temp\azureauth-credprovider\.npmrc",
        @"\\?\C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        @"\\.\C:\agent\_temp\azureauth-credprovider\.npmrc",
        @"\\.\C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        "//?/C:/agent/_temp/azureauth-credprovider/.npmrc",
        "//?/C:/agent/_temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData(
        "//./C:/agent/_temp/azureauth-credprovider/.npmrc",
        "//./C:/agent/_temp/azureauth-credprovider/.npmrc"
    )]
    [InlineData("//?/UNC/server/share", "//?/UNC/server/share/.npmrc")]
    [InlineData(
        "//?/UNC/server/share/agent/_temp/azureauth-credprovider/.npmrc",
        "//?/UNC/server/share/agent/_temp/azureauth-credprovider/.npmrc"
    )]
    public void ConfigurationChangePlanPolicyRejectsWindowsExtendedCiTemporaryPaths(
        string containerPath,
        string targetPath
    )
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                ProductOwnedPath = containerPath,
                ActivationEnvironment = CreateNpmrcFileActivationEnvironment(containerPath),
            },
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetPathOrName = targetPath,
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "fully qualified canonical",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyRejectsMixedUncContainerAndPosixTarget()
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(
                "//agent-share/temp/azureauth-credprovider/yarn-home"
            ),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        "/agent-share/temp/azureauth-credprovider/yarn-home/.yarnrc.yml",
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "declared product-owned temporary container",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyPreservesUncRootWhenCheckingContainment()
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(
                "//agent-share/temp/azureauth-credprovider/yarn-home"
            ),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        "//agent-share/temp/azureauth-credprovider/yarn-home/.yarnrc.yml",
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
    }

    [Fact]
    public void ConfigurationChangePlanPolicyRejectsPosixAbsolutePathsContainingBackslashes()
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(
                "/agent/_temp/azureauth-credprovider/yarn-home"
            ),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName = @"/agent/_temp/azureauth-credprovider/yarn-home\.yarnrc.yml",
                },
            ],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "fully qualified canonical",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyAcceptsCompatibleWindowsDrivePaths()
    {
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = CreateTemporaryHomeContainer(
                @"C:\agent\_temp\azureauth-credprovider\yarn-home"
            ),
            Changes =
            [
                CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                },
            ],
        };

        Assert.True(ConfigurationChangePlanPolicy.IsValid(plan));
    }

    [Theory]
    [InlineData(
        ConfigurationTemporaryContainerKind.NpmrcFile,
        ConfigurationTargetKind.Npmrc,
        "/",
        "/"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.TemporaryHome,
        ConfigurationTargetKind.Yarnrc,
        "/",
        "/.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.YarnRcFile,
        ConfigurationTargetKind.Yarnrc,
        "/",
        "/.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.NpmrcFile,
        ConfigurationTargetKind.Npmrc,
        @"C:\",
        @"C:\"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.TemporaryHome,
        ConfigurationTargetKind.Yarnrc,
        @"C:\",
        @"C:\.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.YarnRcFile,
        ConfigurationTargetKind.Yarnrc,
        @"C:\",
        @"C:\.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.NpmrcFile,
        ConfigurationTargetKind.Npmrc,
        "//agent-share/temp",
        "//agent-share/temp"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.TemporaryHome,
        ConfigurationTargetKind.Yarnrc,
        "//agent-share/temp",
        "//agent-share/temp/.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.YarnRcFile,
        ConfigurationTargetKind.Yarnrc,
        "//agent-share/temp",
        "//agent-share/temp/.yarnrc.yml"
    )]
    public void ConfigurationChangePlanPolicyRejectsCiTemporaryContainerFilesystemRoots(
        ConfigurationTemporaryContainerKind containerKind,
        ConfigurationTargetKind targetKind,
        string productOwnedPath,
        string targetPath
    )
    {
        ConfigurationChange change =
            targetKind == ConfigurationTargetKind.Yarnrc
                ? CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName = targetPath,
                }
                : CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetKind = targetKind,
                    TargetPathOrName = targetPath,
                };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            TemporaryContainer = new ConfigurationTemporaryContainer
            {
                Kind = containerKind,
                ProductOwnedPath = productOwnedPath,
            },
            Changes = [change],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "filesystem root",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Theory]
    [InlineData(
        ConfigurationTemporaryContainerKind.TemporaryHome,
        ConfigurationTargetKind.Yarnrc,
        @"C:\agent\_temp\azureauth-credprovider\yarn-home",
        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.\.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.TemporaryHome,
        ConfigurationTargetKind.Yarnrc,
        @"C:\agent\_temp\azureauth-credprovider\yarn-home",
        @"C:\agent\_temp\azureauth-credprovider\yarn-home\..\other\.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.TemporaryHome,
        ConfigurationTargetKind.Yarnrc,
        "/agent/_temp/azureauth-credprovider/yarn-home",
        "/agent/_temp/azureauth-credprovider/yarn-home/../other/.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.TemporaryHome,
        ConfigurationTargetKind.Yarnrc,
        "agent/_temp/azureauth-credprovider/yarn-home",
        "agent/_temp/azureauth-credprovider/yarn-home/.yarnrc.yml"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.NpmrcFile,
        ConfigurationTargetKind.Npmrc,
        @"C:\agent\_temp\azureauth-credprovider\.\.npmrc",
        @"C:\agent\_temp\azureauth-credprovider\.npmrc"
    )]
    [InlineData(
        ConfigurationTemporaryContainerKind.NpmrcFile,
        ConfigurationTargetKind.Npmrc,
        @"C:agent\_temp\azureauth-credprovider\.npmrc",
        @"C:agent\_temp\azureauth-credprovider\.npmrc"
    )]
    public void ConfigurationChangePlanPolicyRejectsNonCanonicalOrRelativeCiTemporaryPaths(
        ConfigurationTemporaryContainerKind containerKind,
        ConfigurationTargetKind targetKind,
        string containerPath,
        string targetPath
    )
    {
        ConfigurationChange change =
            targetKind == ConfigurationTargetKind.Yarnrc
                ? CreateYarnAlwaysAuthChange(
                    "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                ) with
                {
                    TargetPathOrName = targetPath,
                }
                : CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetKind = targetKind,
                    TargetPathOrName = targetPath,
                };
        ConfigurationChangePlan plan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            TemporaryContainer =
                containerKind == ConfigurationTemporaryContainerKind.TemporaryHome
                    ? CreateTemporaryHomeContainer(containerPath)
                    : new ConfigurationTemporaryContainer
                    {
                        Kind = containerKind,
                        ProductOwnedPath = containerPath,
                    },
            Changes = [change],
        };

        Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
        Assert.Contains(
            "fully qualified canonical",
            ConfigurationChangePlanPolicy.GetViolation(plan),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void ConfigurationChangePlanPolicyRejectsCiTemporaryContainerTargetKindMismatches()
    {
        const string npmrcPath = @"C:\agent\_temp\azureauth-credprovider\.npmrc";
        const string yarnHomePath = @"C:\agent\_temp\azureauth-credprovider\yarn-home";
        ConfigurationChangePlan validCiPlan = CreateValidConfigurationPlan() with
        {
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
        };
        ConfigurationChangePlan[] invalidPlans =
        [
            validCiPlan with
            {
                TemporaryContainer = new ConfigurationTemporaryContainer
                {
                    Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                    ProductOwnedPath = npmrcPath,
                    ActivationEnvironment = CreateNpmrcFileActivationEnvironment(npmrcPath),
                },
                Changes =
                [
                    CreateYarnAlwaysAuthChange(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/npm"
                    ) with
                    {
                        TargetPathOrName = npmrcPath,
                    },
                ],
            },
            validCiPlan with
            {
                TemporaryContainer = new ConfigurationTemporaryContainer
                {
                    Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                    ProductOwnedPath = npmrcPath,
                    ActivationEnvironment = CreateNpmrcFileActivationEnvironment(npmrcPath),
                },
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        TargetKind = ConfigurationTargetKind.GitConfig,
                        TargetPathOrName = npmrcPath,
                        Key = "credential.helper",
                    },
                ],
            },
            validCiPlan with
            {
                TemporaryContainer = new ConfigurationTemporaryContainer
                {
                    Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
                    ProductOwnedPath = npmrcPath,
                    ActivationEnvironment = CreateNpmrcFileActivationEnvironment(npmrcPath),
                },
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        TargetKind = ConfigurationTargetKind.NuGetPluginLayout,
                        TargetPathOrName = npmrcPath,
                        Key = "nuget-plugin-layout",
                    },
                ],
            },
            validCiPlan with
            {
                TemporaryContainer = CreateGenericTemporaryHomeContainer(yarnHomePath),
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        TargetPathOrName =
                            @"C:\agent\_temp\azureauth-credprovider\yarn-home\.npmrc",
                    },
                ],
            },
            validCiPlan with
            {
                TemporaryContainer = CreateGenericTemporaryHomeContainer(yarnHomePath),
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        TargetKind = ConfigurationTargetKind.GitConfig,
                        TargetPathOrName =
                            @"C:\agent\_temp\azureauth-credprovider\yarn-home\.gitconfig",
                        Key = "credential.helper",
                    },
                ],
            },
        ];

        Assert.All(
            invalidPlans,
            plan =>
            {
                Assert.False(ConfigurationChangePlanPolicy.IsValid(plan));
                Assert.Contains(
                    "compatible",
                    ConfigurationChangePlanPolicy.GetViolation(plan),
                    StringComparison.OrdinalIgnoreCase
                );
            }
        );
    }

    [Fact]
    public void ConfigurationChangePlanDeserializationRejectsFrozenInvariantViolations()
    {
        var options = ContractJson.CreateSerializerOptions();
        const string alwaysAuthKey =
            "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"]"
            + ".npmAlwaysAuth";
        const string authTokenKey =
            "npmRegistries[\"https://pkgs.dev.azure.com/org/_packaging/feed/npm\"]"
            + ".npmAuthToken";
        string workspaceReadOnlyWithChangeJson = $$"""
            {
              "contractMajor": 1,
              "planId": "plan-json-workspace-read-only-with-change",
              "ownerProductId": "azureauth-credprovider",
              "scope": "workspaceReadOnly",
              "manifest": {
                "manifestId": "manifest-json-workspace-read-only-with-change",
                "ownerProductId": "azureauth-credprovider",
                "entrySelector": "json-workspace-read-only-with-change"
              },
              "containsCredentialMaterial": false,
              "changes": [
                {
                  "operation": "create",
                  "targetKind": "yarnrc",
                  "targetPathOrName": "user .yarnrc.yml",
                  "key": "{{alwaysAuthKey}}",
                  "value": "true",
                  "requiresOwnershipRecord": true,
                  "isSecretValue": false
                }
              ]
            }
            """;
        string secretChangeWithoutCredentialMaterialJson = $$"""
            {
              "contractMajor": 1,
              "planId": "plan-json-secret-without-material",
              "ownerProductId": "azureauth-credprovider",
              "scope": "user",
              "manifest": {
                "manifestId": "manifest-json-secret-without-material",
                "ownerProductId": "azureauth-credprovider",
                "entrySelector": "json-secret-without-material"
              },
              "containsCredentialMaterial": false,
              "changes": [
                {
                  "operation": "create",
                  "targetKind": "yarnrc",
                  "targetPathOrName": "user .yarnrc.yml",
                  "key": "{{authTokenKey}}",
                  "value": "secret-token",
                  "requiresOwnershipRecord": true,
                  "isSecretValue": true
                }
              ]
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                workspaceReadOnlyWithChangeJson,
                options
            )
        );
        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                secretChangeWithoutCredentialMaterialJson,
                options
            )
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(999)]
    public void ConfigurationChangePlanDeserializationRejectsUnsupportedContractMajor(
        int contractMajor
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = CreateConfigurationPlanJson()
            .Replace(
                "\"contractMajor\":1",
                $"\"contractMajor\":{contractMajor}",
                StringComparison.Ordinal
            );

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(json, options)
        );
    }

    [Fact]
    public void ConfigurationChangePlanDeserializationRejectsExplicitNullRequiredSubgraphs()
    {
        var options = ContractJson.CreateSerializerOptions();
        string validJson = CreateConfigurationPlanJson();

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                validJson.Replace(
                    "\"planId\":\"plan-json-required\"",
                    "\"planId\":null",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                validJson.Replace(
                    "\"ownerProductId\":\"azureauth-credprovider\"",
                    "\"ownerProductId\":null",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                validJson.Replace(
                    "\"manifest\":{\"manifestId\":\"manifest-json-"
                        + "required\",\"ownerProductId\":\"azureauth-"
                        + "credprovider\",\"entrySelector\":\"json-"
                        + "required\",\"productVersion\":\"1.0.0\"}",
                    "\"manifest\":null",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                validJson.Replace(
                    "\"changes\":[{\"operation\":\"create\",\"targetKind\":\"npmrc\",\"targetPathOr"
                        + "Name\":\"user .npmrc\",\"key\":\"always-"
                        + "auth\",\"value\":\"true\",\"requiresOwnershipRecord\":"
                        + "true,\"isSecretValue\":false}]",
                    "\"changes\":null",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                validJson.Replace(
                    "\"changes\":[{\"operation\":\"create\",\"targetKind\":\"npmrc\",\"targetPathOr"
                        + "Name\":\"user .npmrc\",\"key\":\"always-"
                        + "auth\",\"value\":\"true\",\"requiresOwnershipRecord\":"
                        + "true,\"isSecretValue\":false}]",
                    "\"changes\":[null]",
                    StringComparison.Ordinal
                ),
                options
            )
        );
    }

    [Fact]
    public void DoctorCheckCanRepresentUnsupportedAndDeferredModes()
    {
        var gitWindowsCheck = new DoctorCheck
        {
            CheckId = "git.windows.discovery",
            Status = DoctorCheckStatus.Deferred,
            Severity = DoctorCheckSeverity.Info,
            Target = "Git for Windows helper discovery",
            Summary = "Git for Windows discovery is deferred for MVP.",
            DiagnosticsCorrelationId = "corr-doctor-deferred",
            ObservedValue = "not-probed",
            ExpectedValue = "probe implemented",
        };

        var guiCheck = gitWindowsCheck with
        {
            CheckId = "git.gui.discovery",
            Status = DoctorCheckStatus.Unsupported,
            Target = "GUI-launched Git helper discovery",
            Summary = "GUI-launched Git is unsupported for MVP.",
            DiagnosticsCorrelationId = "corr-doctor-unsupported",
        };
        var notApplicableCheck = gitWindowsCheck with
        {
            CheckId = "ci.user-write",
            Status = DoctorCheckStatus.NotApplicable,
            Target = "Persistent user configuration",
            Summary = "Persistent user writes are not applicable in CI temporary mode.",
            DiagnosticsCorrelationId = "corr-doctor-na",
            ObservedValue = "ci-temporary",
            ExpectedValue = "user-or-global",
        };

        Assert.Equal(DoctorCheckStatus.Deferred, gitWindowsCheck.Status);
        Assert.Equal(DoctorCheckStatus.Unsupported, guiCheck.Status);
        Assert.Equal(DoctorCheckStatus.NotApplicable, notApplicableCheck.Status);
        Assert.Equal("ci-temporary", notApplicableCheck.ObservedValue);
        Assert.Equal("user-or-global", notApplicableCheck.ExpectedValue);
    }

    [Theory]
    [InlineData("status", DoctorCheckStatus.Unspecified, DoctorCheckSeverity.Info)]
    [InlineData("status", (DoctorCheckStatus)999, DoctorCheckSeverity.Info)]
    [InlineData("severity", DoctorCheckStatus.Pass, DoctorCheckSeverity.Unspecified)]
    [InlineData("severity", DoctorCheckStatus.Pass, (DoctorCheckSeverity)999)]
    public void DoctorCheckPolicyRejectsUnspecifiedAndUnknownStatusOrSeverity(
        string invalidField,
        DoctorCheckStatus status,
        DoctorCheckSeverity severity
    )
    {
        var check = new DoctorCheck
        {
            CheckId = "doctor.invalid-status-severity",
            Status = status,
            Severity = severity,
            Target = "Doctor check validation",
            Summary = "Doctor check status and severity must be frozen values.",
            DiagnosticsCorrelationId = "corr-doctor-invalid-status-severity",
        };

        Assert.False(DoctorCheckPolicy.IsValid(check));
        Assert.Contains(
            invalidField,
            DoctorCheckPolicy.GetViolation(check),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => DoctorCheckPolicy.EnsureValid(check));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(999)]
    public void DoctorCheckPolicyRejectsUnsupportedContractMajor(int contractMajor)
    {
        var check = new DoctorCheck
        {
            ContractMajor = contractMajor,
            CheckId = "doctor.invalid-contract-major",
            Status = DoctorCheckStatus.Pass,
            Severity = DoctorCheckSeverity.Info,
            Target = "Doctor check validation",
            Summary = "Doctor check contract major must be frozen.",
            DiagnosticsCorrelationId = "corr-doctor-invalid-contract-major",
        };

        Assert.False(DoctorCheckPolicy.IsValid(check));
        Assert.Contains(
            "contract major",
            DoctorCheckPolicy.GetViolation(check),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => DoctorCheckPolicy.EnsureValid(check));
    }

    [Theory]
    [InlineData("checkId", null)]
    [InlineData("checkId", "")]
    [InlineData("checkId", "   ")]
    [InlineData("target", null)]
    [InlineData("target", "")]
    [InlineData("target", "   ")]
    [InlineData("summary", null)]
    [InlineData("summary", "")]
    [InlineData("summary", "   ")]
    [InlineData("diagnosticsCorrelationId", null)]
    [InlineData("diagnosticsCorrelationId", "")]
    [InlineData("diagnosticsCorrelationId", "   ")]
    public void DoctorCheckPolicyRejectsBlankRequiredIdentityFields(string fieldName, string? value)
    {
        var check = new DoctorCheck
        {
            CheckId = "doctor.required-identity",
            Status = DoctorCheckStatus.Pass,
            Severity = DoctorCheckSeverity.Info,
            Target = "Doctor check validation",
            Summary = "Doctor check required identity fields must be populated.",
            DiagnosticsCorrelationId = "corr-doctor-required-identity",
        };

        check = fieldName switch
        {
            "checkId" => check with { CheckId = value! },
            "target" => check with { Target = value! },
            "summary" => check with { Summary = value! },
            "diagnosticsCorrelationId" => check with { DiagnosticsCorrelationId = value! },
            _ => throw new ArgumentOutOfRangeException(
                nameof(fieldName),
                fieldName,
                "Unknown doctor check field."
            ),
        };

        Assert.False(DoctorCheckPolicy.IsValid(check));
        Assert.Contains(
            "required identity",
            DoctorCheckPolicy.GetViolation(check),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Throws<ArgumentException>(() => DoctorCheckPolicy.EnsureValid(check));
    }

    [Fact]
    public void DoctorCheckDeserializationRejectsUnspecifiedAndUnknownStatusOrSeverity()
    {
        var options = ContractJson.CreateSerializerOptions();
        string validJson = """
            {
              "contractMajor": 1,
              "checkId": "doctor.json.invalid-status-severity",
              "status": "pass",
              "severity": "info",
              "target": "Doctor check validation",
              "summary": "Doctor check status and severity must be frozen values.",
              "diagnosticsCorrelationId": "corr-doctor-json-invalid-status-severity"
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<DoctorCheck>(
                validJson.Replace(
                    "\"status\": \"pass\"",
                    "\"status\": \"unspecified\"",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<DoctorCheck>(
                validJson.Replace(
                    "\"severity\": \"info\"",
                    "\"severity\": \"unspecified\"",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DoctorCheck>(
                validJson.Replace(
                    "\"status\": \"pass\"",
                    "\"status\": \"futureStatus\"",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DoctorCheck>(
                validJson.Replace(
                    "\"severity\": \"info\"",
                    "\"severity\": \"futureSeverity\"",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DoctorCheck>(
                validJson.Replace(
                    "\"status\": \"pass\"",
                    "\"status\": 999",
                    StringComparison.Ordinal
                ),
                options
            )
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(999)]
    public void DoctorCheckDeserializationRejectsUnsupportedContractMajor(int contractMajor)
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": {{contractMajor}},
              "checkId": "doctor.json.invalid-contract-major",
              "status": "pass",
              "severity": "info",
              "target": "Doctor check validation",
              "summary": "Doctor check contract major must be frozen.",
              "diagnosticsCorrelationId": "corr-doctor-json-invalid-contract-major"
            }
            """;

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<DoctorCheck>(json, options));
    }

    [Theory]
    [InlineData("\"checkId\": \"doctor.json.required-identity\"", "\"checkId\": \"\"")]
    [InlineData("\"checkId\": \"doctor.json.required-identity\"", "\"checkId\": \"   \"")]
    [InlineData("\"checkId\": \"doctor.json.required-identity\"", "\"checkId\": null")]
    [InlineData("\"target\": \"Doctor check validation\"", "\"target\": \"\"")]
    [InlineData("\"target\": \"Doctor check validation\"", "\"target\": \"   \"")]
    [InlineData("\"target\": \"Doctor check validation\"", "\"target\": null")]
    [InlineData(
        "\"summary\": \"Doctor check required identity fields must be populated.\"",
        "\"summary\": \"\""
    )]
    [InlineData(
        "\"summary\": \"Doctor check required identity fields must be populated.\"",
        "\"summary\": \"   \""
    )]
    [InlineData(
        "\"summary\": \"Doctor check required identity fields must be populated.\"",
        "\"summary\": null"
    )]
    [InlineData(
        "\"diagnosticsCorrelationId\": \"corr-doctor-json-required-identity\"",
        "\"diagnosticsCorrelationId\": \"\""
    )]
    [InlineData(
        "\"diagnosticsCorrelationId\": \"corr-doctor-json-required-identity\"",
        "\"diagnosticsCorrelationId\": \"   \""
    )]
    [InlineData(
        "\"diagnosticsCorrelationId\": \"corr-doctor-json-required-identity\"",
        "\"diagnosticsCorrelationId\": null"
    )]
    public void DoctorCheckDeserializationRejectsBlankRequiredIdentityFields(
        string oldJson,
        string newJson
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string validJson = """
            {
              "contractMajor": 1,
              "checkId": "doctor.json.required-identity",
              "status": "pass",
              "severity": "info",
              "target": "Doctor check validation",
              "summary": "Doctor check required identity fields must be populated.",
              "diagnosticsCorrelationId": "corr-doctor-json-required-identity"
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<DoctorCheck>(
                validJson.Replace(oldJson, newJson, StringComparison.Ordinal),
                options
            )
        );
    }

    [Fact]
    public void KeyringHelperV2BuildsFixedNonShellCommandShape()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Username = "user",
            Mode = KeyringHelperMode.Credentials,
        };

        IReadOnlyList<string> arguments = KeyringHelperV2.BuildArguments(request);

        Assert.Equal(
            [
                "python-keyring",
                "get",
                "--protocol-version",
                "2",
                "--service",
                "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
                "--username",
                "user",
                "--mode",
                "creds",
            ],
            arguments
        );
    }

    [Fact]
    public void KeyringHelperV2RejectsRequestsForNonFixedCommand()
    {
        var request = new KeyringHelperRequest
        {
            Command = "python -m keyring",
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-command",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains("python-keyring", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void KeyringHelperV2RejectsUnsupportedRequestContractMajor(int contractMajor)
    {
        var request = new KeyringHelperRequest
        {
            ContractMajor = contractMajor,
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-request-version",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains("contract major", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperV2RejectsNullServiceWithoutStdout()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = null!,
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-null-service",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains("service", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains("service", response.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://dev.azure.com/org/_packaging/feed/pypi/simple")]
    [InlineData("https://dev.azure.com/org/proj/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/proj/_packaging/feed/pypi/simple")]
    [InlineData("https://org.pkgs.visualstudio.com/_packaging/feed/pypi/simple")]
    [InlineData("https://org.pkgs.visualstudio.com/proj/_packaging/feed/pypi/simple/")]
    [InlineData("https://org.pkgs.visualstudio.com/DefaultCollection/_packaging/feed/pypi/simple/")]
    [InlineData("https://org.visualstudio.com/_packaging/feed/pypi/simple")]
    [InlineData("https://org.visualstudio.com/project/_packaging/feed/pypi/simple/")]
    [InlineData("https://org.visualstudio.com/DefaultCollection/_packaging/feed/pypi/simple/")]
    [InlineData(
        "https://org.visualstudio.com/DefaultCollection/project/_packaging/feed/" + "pypi/simple/"
    )]
    public void KeyringHelperV2AcceptsOnlyAzureArtifactsPythonFeedServiceUris(string service)
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(service),
            Mode = KeyringHelperMode.Password,
        };

        IReadOnlyList<string> arguments = KeyringHelperV2.BuildArguments(request);

        Assert.Equal(KeyringHelperV2.CommandName, arguments[0]);
        Assert.Contains("--service", arguments);
        Assert.Contains(request.Service.AbsoluteUri, arguments);
    }

    [Theory]
    [InlineData("https://dev.azure.com/org/_packaging/feed/pypi/simple")]
    [InlineData("https://dev.azure.com/org/project/_packaging/feed/pypi/simple/")]
    public void KeyringHelperV2SerializesSuccessfulCredentialsForDevAzureComPythonFeedServiceUris(
        string service
    )
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(service),
            Mode = KeyringHelperMode.Credentials,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-dev-azure-python-service",
        };

        IReadOnlyList<string> arguments = KeyringHelperV2.BuildArguments(request);
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains("--service", arguments);
        Assert.Contains(request.Service.AbsoluteUri, arguments);
        Assert.Equal(AdapterHostExitCode.Success, response.ExitCode);
        Assert.Equal("AzureDevOps\ngenerated-password\n", response.Stdout);
        Assert.Equal(string.Empty, response.Stderr);
    }

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org/_git/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/_git/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/_packaging/_packaging/feed/pypi/simple/")]
    [InlineData("https://_git.pkgs.visualstudio.com/_packaging/feed/pypi/simple/")]
    public void KeyringHelperV2RejectsReservedMarkerNamesAsServiceIdentityComponentValues(
        string service
    )
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(service),
            Mode = KeyringHelperMode.Password,
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(
            request,
            new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                Password = "generated-password",
                DiagnosticsCorrelationId = "corr-keyring-reserved-service-component",
            }
        );

        Assert.Contains("service", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains("service", response.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperV2RejectsServiceUserInfoWithoutStdoutOrArgvLeak()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(
                "https://user:secret@pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"
            ),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-userinfo-service",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains("service", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains("service", response.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user:secret", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperV2RejectsEmptyServiceUserInfoWithoutStdoutOrArgvLeak()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://@pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-empty-userinfo-service",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains("service", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains("service", response.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/?token=secret")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/#secret")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/?")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/#")]
    public void KeyringHelperV2RejectsServiceQueryOrFragmentWithoutStdoutOrArgvLeak(string service)
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(service),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-uri-secret",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains(
            "query, or fragment",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain("secret", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains("query, or fragment", response.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperV2RejectsRelativeServiceWithoutStdout()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("/org/_packaging/feed/pypi/simple/", UriKind.Relative),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-relative-service",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains("service", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains("service", response.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://pkgs.dev.azure.com/org")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/npm")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json")]
    [InlineData("http://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com:8443/org/_packaging/feed/pypi/simple/")]
    [InlineData("https://example.com/org/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/maven/v1")]
    [InlineData("https://pkgs.dev.azure.com/org/_git/repo")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging//pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org%2Fother/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/project%2Fother/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed%2Fother/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org%0Aother/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/project%0Dother/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed%09other/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed%C2%85other/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org%1Bother/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/project%C2%9Fother/_packaging/feed/pypi/simple/")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed%7Fother/pypi/simple/")]
    [InlineData(
        "https://org.pkgs.visualstudio.com/DefaultCollection/_packaging/feed%5Cother/pypi/simple/"
    )]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/extra")]
    [InlineData("https://dev.azure.com/org/proj/_unknown/repo")]
    [InlineData("https://dev.azure.com")]
    public void KeyringHelperV2RejectsUnsupportedServiceUrisBeforeArgvOrStdout(string service)
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri(service),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-unsupported-service",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains(
            "keyring helper service",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains(
            "keyring helper service",
            response.Stderr,
            StringComparison.OrdinalIgnoreCase
        );
        Assert.DoesNotContain(service, response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperV2SerializesCredentialsAndNoCredentialPrecisely()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Credentials,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = "AzureDevOps",
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring",
        };
        var noCredential = new CredentialResult
        {
            Status = CredentialResultStatus.NoCredential,
            DiagnosticsCorrelationId = "corr-no-credential",
        };

        KeyringHelperResponse successResponse = KeyringHelperV2.ToResponse(request, success);
        KeyringHelperResponse noCredentialResponse = KeyringHelperV2.ToResponse(
            request,
            noCredential
        );

        Assert.Equal(AdapterHostExitCode.Success, successResponse.ExitCode);
        Assert.Equal("AzureDevOps\ngenerated-password\n", successResponse.Stdout);
        Assert.Equal(string.Empty, successResponse.Stderr);
        Assert.Equal(AdapterHostExitCode.NoCredential, noCredentialResponse.ExitCode);
        Assert.Equal(string.Empty, noCredentialResponse.Stdout);
        Assert.DoesNotContain(
            "generated-password",
            successResponse.ToString(),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void KeyringHelperV2CredentialsModeUsesRequestUsernameWhenResultUsernameIsNull()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Username = "request-user",
            Mode = KeyringHelperMode.Credentials,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = null,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-request-username",
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Equal(AdapterHostExitCode.Success, response.ExitCode);
        Assert.Equal("request-user\ngenerated-password\n", response.Stdout);
        Assert.Equal(string.Empty, response.Stderr);
    }

    [Fact]
    public void KeyringHelperV2CredentialsModeRejectsMissingUsernamesWithoutPasswordLeak()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Credentials,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-missing-username",
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains("credential material", response.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("generated-password", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperV2PasswordModeUsesExplicitLfStdout()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-lf",
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Equal(AdapterHostExitCode.Success, response.ExitCode);
        Assert.Equal("generated-password\n", response.Stdout);
        Assert.DoesNotContain("\r", response.Stdout, StringComparison.Ordinal);
        Assert.Equal(string.Empty, response.Stderr);
    }

    [Theory]
    [InlineData(KeyringHelperMode.Password, null, null, "generated\rpassword")]
    [InlineData(KeyringHelperMode.Password, null, null, "generated\npassword")]
    [InlineData(KeyringHelperMode.Credentials, null, "Azure\rDevOps", "generated-password")]
    [InlineData(KeyringHelperMode.Credentials, "Azure\nDevOps", null, "generated-password")]
    public void KeyringHelperV2RejectsSuccessCredentialFieldsContainingCrOrLf(
        KeyringHelperMode mode,
        string? requestUsername,
        string? resultUsername,
        string password
    )
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Username = requestUsername,
            Mode = mode,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = resultUsername,
            Password = password,
            DiagnosticsCorrelationId = "corr-keyring-crlf",
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Contains("CR or LF", response.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(password, response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperV2UsesMapperDiagnosticWhenMappedSuccessCarriesError()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Password,
        };
        var errorBearingSuccess = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-success-error",
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.CredentialUnavailable,
                Code = "CredentialUnavailable",
                SafeMessage = "Credential material is unavailable.",
            },
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, errorBearingSuccess);

        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Equal("ProtocolViolation", response.Stderr);
        Assert.DoesNotContain(
            "Credential material is unavailable.",
            response.Stderr,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void KeyringHelperV2FailsClosedForUnsupportedCredentialResultContractMajor(
        int contractMajor
    )
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Password,
        };
        var success = new CredentialResult
        {
            ContractMajor = contractMajor,
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-result-version",
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Equal("UnsupportedContractMajor", response.Stderr);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void KeyringHelperV2UsesMapperDiagnosticForUnsupportedCredentialResultContractMajor(
        int contractMajor
    )
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Password,
        };
        var rejectedResult = new CredentialResult
        {
            ContractMajor = contractMajor,
            Status = CredentialResultStatus.Fatal,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-result-version-error",
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.Fatal,
                Code = "ProducerControlledCode",
                SafeMessage = "producer-controlled safe message",
            },
        };

        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, rejectedResult);

        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.Equal("UnsupportedContractMajor", response.Stderr);
        Assert.DoesNotContain(
            "producer-controlled safe message",
            response.Stderr,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(KeyringHelperMode.Unspecified)]
    [InlineData((KeyringHelperMode)999)]
    public void KeyringHelperV2RejectsUnspecifiedOrUnknownMode(KeyringHelperMode mode)
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = mode,
        };
        var success = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Password = "generated-password",
            DiagnosticsCorrelationId = "corr-keyring-mode",
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            KeyringHelperV2.BuildArguments(request)
        );
        KeyringHelperResponse response = KeyringHelperV2.ToResponse(request, success);

        Assert.Contains("mode", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, response.ExitCode);
        Assert.Equal(string.Empty, response.Stdout);
        Assert.DoesNotContain("generated-password", response.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperResponseToStringRedactsStdoutAndStderr()
    {
        var response = new KeyringHelperResponse
        {
            ExitCode = AdapterHostExitCode.ConfigurationError,
            Stdout = "stdout-secret",
            Stderr = "stderr-secret",
        };

        string text = response.ToString();

        Assert.DoesNotContain("stdout-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stderr-secret", text, StringComparison.Ordinal);
        Assert.Contains("Stdout = <redacted>", text, StringComparison.Ordinal);
        Assert.Contains("Stderr = <redacted>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyringHelperV2EmitsIntegrityFailureAndProtocolViolationExitCodes()
    {
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Password,
        };
        var integrityFailure = new CredentialResult
        {
            Status = CredentialResultStatus.IntegrityFailure,
            DiagnosticsCorrelationId = "corr-integrity",
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.IntegrityFailure,
                Code = "HelperIntegrityFailure",
                SafeMessage = "Helper integrity validation failed.",
            },
        };
        var protocolViolation = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            DiagnosticsCorrelationId = "corr-protocol",
        };

        KeyringHelperResponse integrityResponse = KeyringHelperV2.ToResponse(
            request,
            integrityFailure
        );
        KeyringHelperResponse protocolResponse = KeyringHelperV2.ToResponse(
            request,
            protocolViolation
        );

        Assert.Equal(AdapterHostExitCode.IntegrityFailure, integrityResponse.ExitCode);
        Assert.Equal(AdapterHostExitCode.ConfigurationError, protocolResponse.ExitCode);
        Assert.Equal(string.Empty, integrityResponse.Stdout);
        Assert.Equal(string.Empty, protocolResponse.Stdout);
    }

    [Fact]
    public void IdentityFlowPolicyRejectsSilentFallbacksAndPatCompatibilityByDefault()
    {
        var entraRequest = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        );
        var implicitPatRequest = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.PatCompatibility
        );
        var noCacheRequest = CreateRequest(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword
        ) with
        {
            CachePolicy = CachePolicyMode.NoCache,
        };

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(entraRequest));
        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(noCacheRequest));
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(implicitPatRequest));
        Assert.False(
            IdentityFlowPolicy.IsSilentFallbackAllowed(
                IdentityFlow.InteractiveBrowser,
                IdentityFlow.PatCompatibility
            )
        );
        Assert.False(
            IdentityFlowPolicy.IsSilentFallbackAllowed(
                IdentityFlow.InteractiveBrowser,
                IdentityFlow.DeviceCode
            )
        );
        Assert.False(
            IdentityFlowPolicy.IsSilentFallbackAllowed(
                IdentityFlow.DeviceCode,
                IdentityFlow.AzurePipelinesSystemAccessToken
            )
        );
    }

    [Fact]
    public void IdentityFlowPolicyRejectsFuturePersistentCacheRequestsForMvp()
    {
        var request = CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword) with
        {
            CachePolicy = CachePolicyMode.FuturePersistentCacheRequested,
        };

        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
    }

    [Fact]
    public void IdentityFlowPolicyRejectsRequestsAllowingPersistentWritesForMvpAndCacheKeys()
    {
        var request = CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword) with
        {
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = true },
        };

        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "user@example.com", "tenant-1")
        );
    }

    [Fact]
    public void AzurePipelinesSystemAccessTokenRejectsPersistentWritesForAcceptanceAndCacheKeys()
    {
        var request = CreateRequest(
            IdentityFlow.AzurePipelinesSystemAccessToken,
            CredentialKind.BearerToken
        ) with
        {
            InteractivePolicy = InteractivePolicy.Never,
            CachePolicy = CachePolicyMode.NonPersistentCi,
            CiContext = new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = true,
            },
        };

        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "build-service@org", "tenant-1")
        );
    }

    [Theory]
    [InlineData(IdentityFlow.InteractiveBrowser)]
    [InlineData(IdentityFlow.DeviceCode)]
    public void IdentityFlowPolicyRejectsUserInteractiveFlowsWhenInteractionIsForbidden(
        IdentityFlow flow
    )
    {
        var request = CreateRequest(flow, CredentialKind.BasicPassword) with
        {
            InteractivePolicy = InteractivePolicy.Never,
        };

        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
    }

    [Fact]
    public void IdentityFlowPolicyAndCacheKeySchemaPreserveV1HintControlCharacterBehavior()
    {
        CredentialRequest baselineRequest = CreateRequest(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword
        );
        CredentialRequest hintedRequest = baselineRequest with
        {
            AccountHint = "user\u0001@example.com",
            TenantHint = "tenant\u0002hint",
        };

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(hintedRequest));
        Assert.Equal(
            CacheKeySchema.Create(baselineRequest, "user@example.com", "tenant-1"),
            CacheKeySchema.Create(hintedRequest, "user@example.com", "tenant-1")
        );
    }

    [Fact]
    public void IdentityFlowPolicyRequiresExplicitCiModeAndProvidedAzurePipelinesToken()
    {
        var ciContext = new CiContext
        {
            ExplicitCiMode = true,
            HasAzurePipelinesSystemAccessToken = true,
            AllowsPersistentWrites = false,
            Provider = CiProviderNames.AzurePipelines,
        };
        var validRequest = CreateRequest(
            IdentityFlow.AzurePipelinesSystemAccessToken,
            CredentialKind.BearerToken
        ) with
        {
            InteractivePolicy = InteractivePolicy.Never,
            CachePolicy = CachePolicyMode.NonPersistentCi,
            CiContext = ciContext,
        };

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(validRequest));
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(validRequest with { CiContext = null })
        );
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(
                validRequest with
                {
                    CiContext = ciContext with { ExplicitCiMode = false },
                }
            )
        );
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(
                validRequest with
                {
                    CiContext = ciContext with { Provider = null },
                }
            )
        );
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(
                validRequest with
                {
                    CiContext = ciContext with { Provider = string.Empty },
                }
            )
        );
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(
                validRequest with
                {
                    CiContext = ciContext with { Provider = "GitHubActions" },
                }
            )
        );
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(
                validRequest with
                {
                    CiContext = ciContext with { HasAzurePipelinesSystemAccessToken = false },
                }
            )
        );
        Assert.False(
            IdentityFlowPolicy.IsAcceptedMvpRequest(
                validRequest with
                {
                    CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
                }
            )
        );
    }

    [Fact]
    public void IdentityFlowPolicyRejectsExplicitCiPatCompatibilityForAcceptanceAndPatUse()
    {
        var request = CreateRequest(
            IdentityFlow.PatCompatibility,
            CredentialKind.PatCompatibility
        ) with
        {
            CachePolicy = CachePolicyMode.NonPersistentCi,
            CiContext = new CiContext
            {
                ExplicitCiMode = true,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
                Provider = CiProviderNames.AzurePipelines,
            },
        };

        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.False(IdentityFlowPolicy.CanUsePatCompatibility(request));
    }

    [Theory]
    [InlineData(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword)]
    [InlineData(IdentityFlow.DeviceCode, CredentialKind.BasicPassword)]
    [InlineData(IdentityFlow.PatCompatibility, CredentialKind.PatCompatibility)]
    public void IdentityFlowPolicyRejectsNonSystemTokenFlowsInExplicitCiMode(
        IdentityFlow flow,
        CredentialKind credentialKind
    )
    {
        var request = CreateRequest(flow, credentialKind) with
        {
            CachePolicy = CachePolicyMode.NonPersistentCi,
            CiContext = new CiContext
            {
                ExplicitCiMode = true,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
                Provider = CiProviderNames.AzurePipelines,
            },
        };

        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(request, "build-service@org", "tenant-1")
        );
    }

    [Fact]
    public void AzPipelinesSystemTokenGitRequiresBearerCredentialKind()
    {
        var ciContext = new CiContext
        {
            ExplicitCiMode = true,
            HasAzurePipelinesSystemAccessToken = true,
            AllowsPersistentWrites = false,
            Provider = CiProviderNames.AzurePipelines,
        };
        var bearerRequest = CreateRequest(
            IdentityFlow.AzurePipelinesSystemAccessToken,
            CredentialKind.BearerToken
        ) with
        {
            InteractivePolicy = InteractivePolicy.Never,
            CachePolicy = CachePolicyMode.NonPersistentCi,
            CiContext = ciContext,
        };
        var basicRequest = bearerRequest with { CredentialKind = CredentialKind.BasicPassword };

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(bearerRequest));
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(basicRequest));
        Assert.Throws<ArgumentException>(() =>
            CacheKeySchema.Create(basicRequest, "build-service@org", "tenant-1")
        );
    }

    [Theory]
    [InlineData(CredentialEcosystem.NuGet, CredentialKind.NuGetPluginCredential)]
    [InlineData(CredentialEcosystem.Python, CredentialKind.BasicPassword)]
    [InlineData(CredentialEcosystem.Npm, CredentialKind.NpmAuthToken)]
    [InlineData(CredentialEcosystem.Pnpm, CredentialKind.NpmAuthToken)]
    [InlineData(CredentialEcosystem.Yarn, CredentialKind.NpmAuthToken)]
    public void AzPipelinesSystemTokenPackagesUseEcosystemCredentialKind(
        CredentialEcosystem ecosystem,
        CredentialKind credentialKind
    )
    {
        var ciContext = new CiContext
        {
            ExplicitCiMode = true,
            HasAzurePipelinesSystemAccessToken = true,
            AllowsPersistentWrites = false,
            Provider = CiProviderNames.AzurePipelines,
        };
        CredentialRequest normalPackageRequest = CreatePackageRequest(ecosystem, credentialKind);
        CredentialRequest systemTokenRequest = normalPackageRequest with
        {
            IdentityFlow = IdentityFlow.AzurePipelinesSystemAccessToken,
            InteractivePolicy = InteractivePolicy.Never,
            CachePolicy = CachePolicyMode.NonPersistentCi,
            CiContext = ciContext,
        };

        CacheKey normalCacheKey = CacheKeySchema.Create(
            normalPackageRequest,
            "build-service@org",
            "tenant-1"
        );
        CacheKey systemTokenCacheKey = CacheKeySchema.Create(
            systemTokenRequest,
            "build-service@org",
            "tenant-1"
        );

        Assert.True(IdentityFlowPolicy.IsAcceptedMvpRequest(systemTokenRequest));
        Assert.Equal(credentialKind, CacheKeySchema.GetCredentialKind(systemTokenCacheKey));
        Assert.Equal(normalCacheKey.Value, systemTokenCacheKey.Value);
    }

    [Theory]
    [InlineData(CredentialKind.Unspecified)]
    [InlineData((CredentialKind)999)]
    public void IdentityFlowPolicyRejectsUnspecifiedOrUnknownCredentialKindsForAcceptedNonPatFlows(
        CredentialKind kind
    )
    {
        var request = CreateRequest(IdentityFlow.DeviceCode, kind);

        Assert.Equal(
            IdentityFlowState.AcceptedMvp,
            IdentityFlowPolicy.GetMvpState(request.IdentityFlow)
        );
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
    }

    [Fact]
    public void IdentityFlowPolicyFailsClosedForUnspecifiedOrUnknownRequiredRequestEnums()
    {
        CredentialRequest valid = CreateRequest(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword
        );

        CredentialRequest[] invalidRequests =
        [
            valid with
            {
                ContractMajor = 0,
            },
            valid with
            {
                ContractMajor = 2,
            },
            valid with
            {
                Ecosystem = CredentialEcosystem.Unspecified,
            },
            valid with
            {
                Ecosystem = (CredentialEcosystem)999,
            },
            valid with
            {
                Operation = CredentialOperation.Unspecified,
            },
            valid with
            {
                Operation = (CredentialOperation)999,
            },
            valid with
            {
                RequestedAudience = TokenAudience.Unspecified,
            },
            valid with
            {
                RequestedAudience = (TokenAudience)999,
            },
            valid with
            {
                IdentityFlow = IdentityFlow.Unspecified,
            },
            valid with
            {
                IdentityFlow = (IdentityFlow)999,
            },
            valid with
            {
                InteractivePolicy = InteractivePolicy.Unspecified,
            },
            valid with
            {
                InteractivePolicy = (InteractivePolicy)999,
            },
            valid with
            {
                CachePolicy = CachePolicyMode.Unspecified,
            },
            valid with
            {
                CachePolicy = (CachePolicyMode)999,
            },
        ];

        Assert.All(
            invalidRequests,
            request => Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request))
        );
    }

    [Fact]
    public void CacheKeySchemaFailsClosedForRequestsRejectedByFrozenRequestPolicy()
    {
        CredentialRequest valid = CreateRequest(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword
        );

        CredentialRequest[] invalidRequests =
        [
            valid with
            {
                ContractMajor = 0,
            },
            valid with
            {
                ContractMajor = 2,
            },
            valid with
            {
                Ecosystem = CredentialEcosystem.Unspecified,
            },
            valid with
            {
                Ecosystem = (CredentialEcosystem)999,
            },
            valid with
            {
                Operation = CredentialOperation.Unspecified,
            },
            valid with
            {
                Operation = (CredentialOperation)999,
            },
            valid with
            {
                RequestedAudience = TokenAudience.Unspecified,
            },
            valid with
            {
                RequestedAudience = (TokenAudience)999,
            },
            valid with
            {
                CredentialKind = CredentialKind.Unspecified,
            },
            valid with
            {
                CredentialKind = (CredentialKind)999,
            },
            valid with
            {
                IdentityFlow = IdentityFlow.Unspecified,
            },
            valid with
            {
                IdentityFlow = (IdentityFlow)999,
            },
            valid with
            {
                InteractivePolicy = InteractivePolicy.Unspecified,
            },
            valid with
            {
                InteractivePolicy = (InteractivePolicy)999,
            },
            valid with
            {
                CachePolicy = CachePolicyMode.Unspecified,
            },
            valid with
            {
                CachePolicy = (CachePolicyMode)999,
            },
            valid with
            {
                CachePolicy = CachePolicyMode.FuturePersistentCacheRequested,
            },
        ];

        Assert.All(
            invalidRequests,
            request =>
            {
                Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(request));
                Assert.Throws<ArgumentException>(() =>
                    CacheKeySchema.Create(request, "user@example.com", "tenant-1")
                );
            }
        );
    }

    [Fact]
    public void ContractJsonPinsWireShapeAndStringEnumRepresentation()
    {
        var options = ContractJson.CreateSerializerOptions();
        var request = CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword);

        string json = JsonSerializer.Serialize(request, options);
        var roundTripped = JsonSerializer.Deserialize<CredentialRequest>(json, options);

        Assert.Contains("\"contractMajor\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"ecosystem\":\"git\"", json, StringComparison.Ordinal);
        Assert.Contains("\"identityFlow\":\"deviceCode\"", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"cachePolicy\":\"productPersistentCacheDisabled\"",
            json,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("\"Ecosystem\"", json, StringComparison.Ordinal);
        Assert.NotNull(roundTripped);
        Assert.Equal(CredentialEcosystem.Git, roundTripped.Ecosystem);
        Assert.Equal(IdentityFlow.DeviceCode, roundTripped.IdentityFlow);
    }

    [Fact]
    public void ContractJsonRejectsNumericEnumWireShape()
    {
        var options = ContractJson.CreateSerializerOptions();
        string numericEcosystemJson = CreateCredentialRequestJson(
            "\"ecosystem\":1",
            includeContractMajor: true
        );
        string numericStatusJson = """
            {
              "contractMajor": 1,
              "status": 13,
              "diagnosticsCorrelationId": "corr-numeric-status"
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CredentialRequest>(numericEcosystemJson, options)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CredentialResult>(numericStatusJson, options)
        );
    }

    [Fact]
    public void ContractJsonRejectsNonCanonicalEnumReading()
    {
        var options = ContractJson.CreateSerializerOptions();
        string pascalEcosystemJson = CreateCredentialRequestJson(
            "\"ecosystem\":\"Git\"",
            includeContractMajor: true
        );
        string upperStatusJson = """
            {
              "contractMajor": 1,
              "status": "SUCCESS",
              "diagnosticsCorrelationId": "corr-uppercase-status"
            }
            """;
        string pascalAdapterJson = """
            {
              "contractMajor": 1,
              "protocol": "GitCredentialHelper",
              "exitCode": "configurationError",
              "writeProtocolStdout": false,
              "writeDiagnosticStderr": true
            }
            """;
        string upperConfigurationJson = CreateConfigurationPlanJson()
            .Replace("\"scope\":\"user\"", "\"scope\":\"USER\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CredentialRequest>(pascalEcosystemJson, options)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CredentialResult>(upperStatusJson, options)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AdapterHostResult>(pascalAdapterJson, options)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(upperConfigurationJson, options)
        );
    }

    [Fact]
    public void ContractJsonPinsAdapterAndKeyringEnumWireShapes()
    {
        var options = ContractJson.CreateSerializerOptions();
        var adapter = new AdapterHostResult
        {
            Protocol = AdapterProtocol.GitCredentialHelper,
            ExitCode = AdapterHostExitCode.ConfigurationError,
            WriteProtocolStdout = false,
            WriteDiagnosticStderr = true,
        };
        var request = new KeyringHelperRequest
        {
            Command = KeyringHelperV2.CommandName,
            Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
            Mode = KeyringHelperMode.Credentials,
        };
        var response = new KeyringHelperResponse
        {
            ExitCode = AdapterHostExitCode.NoCredential,
            Stdout = string.Empty,
            Stderr = string.Empty,
        };

        string adapterJson = JsonSerializer.Serialize(adapter, options);
        string requestJson = JsonSerializer.Serialize(request, options);
        string responseJson = JsonSerializer.Serialize(response, options);

        Assert.Contains(
            "\"protocol\":\"gitCredentialHelper\"",
            adapterJson,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\"exitCode\":\"configurationError\"",
            adapterJson,
            StringComparison.Ordinal
        );
        Assert.Contains("\"mode\":\"credentials\"", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"contractMajor\":2", responseJson, StringComparison.Ordinal);
        Assert.Contains("\"exitCode\":\"noCredential\"", responseJson, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AdapterHostResult>(
                adapterJson.Replace("\"gitCredentialHelper\"", "1", StringComparison.Ordinal),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AdapterHostResult>(
                adapterJson.Replace("\"configurationError\"", "64", StringComparison.Ordinal),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<KeyringHelperRequest>(
                requestJson.Replace("\"credentials\"", "2", StringComparison.Ordinal),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(
                responseJson.Replace("\"noCredential\"", "1", StringComparison.Ordinal),
                options
            )
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(999)]
    public void AdapterHostResultDeserializationRejectsUnsupportedContractMajor(int contractMajor)
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": {{contractMajor}},
              "protocol": "gitCredentialHelper",
              "exitCode": "configurationError",
              "writeProtocolStdout": false,
              "writeDiagnosticStderr": true
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<AdapterHostResult>(json, options)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(999)]
    public void CredentialResultDeserializationRejectsUnsupportedContractMajor(int contractMajor)
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": {{contractMajor}},
              "status": "protocolViolation",
              "diagnosticsCorrelationId": "corr-unsupported-credential-result-contract-major"
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<CredentialResult>(json, options)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(999)]
    public void CredentialRequestDeserializationRejectsUnsupportedContractMajor(int contractMajor)
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = CreateCredentialRequestJson(
                "\"ecosystem\":\"git\"",
                includeContractMajor: true
            )
            .Replace(
                "\"contractMajor\": 1",
                $"\"contractMajor\": {contractMajor}",
                StringComparison.Ordinal
            );

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<CredentialRequest>(json, options)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(999)]
    public void KeyringHelperRequestDeserializationRejectsUnsupportedContractMajor(
        int contractMajor
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": {{contractMajor}},
              "command": "python-keyring",
              "service": "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/",
              "mode": "credentials"
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperRequest>(json, options)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(999)]
    public void KeyringHelperResponseDeserializationRejectsUnsupportedContractMajor(
        int contractMajor
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": {{contractMajor}},
              "exitCode": "noCredential",
              "stdout": "",
              "stderr": ""
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(json, options)
        );
    }

    [Theory]
    [InlineData("success", "password\n")]
    [InlineData("noCredential", "")]
    public void KeyringHelperResponseSerializationRejectsSuccessAndNoCredentialStderr(
        string exitCode,
        string stdout
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        var response = new KeyringHelperResponse
        {
            ExitCode = Enum.Parse<AdapterHostExitCode>(exitCode, ignoreCase: true),
            Stdout = stdout,
            Stderr = "diagnostic",
        };

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(response, options));
    }

    [Theory]
    [InlineData("noCredential")]
    [InlineData("interactionRequired")]
    [InlineData("unauthorized")]
    [InlineData("configurationError")]
    [InlineData("integrityFailure")]
    [InlineData("cacheUnavailable")]
    [InlineData("fatal")]
    public void KeyringHelperResponseSerializationRejectsFailureStdout(string exitCode)
    {
        var options = ContractJson.CreateSerializerOptions();
        var response = new KeyringHelperResponse
        {
            ExitCode = Enum.Parse<AdapterHostExitCode>(exitCode, ignoreCase: true),
            Stdout = "unexpected protocol output",
            Stderr = string.Empty,
        };

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(response, options));
    }

    [Fact]
    public void KeyringHelperResponseSerializationRejectsEmptySuccessStdout()
    {
        var options = ContractJson.CreateSerializerOptions();
        var response = new KeyringHelperResponse
        {
            ExitCode = AdapterHostExitCode.Success,
            Stdout = string.Empty,
            Stderr = string.Empty,
        };

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(response, options));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\npassword\n")]
    [InlineData("username\n\n")]
    public void KeyringHelperResponseSerializationRejectsEmptySuccessStdoutRecords(string stdout)
    {
        var options = ContractJson.CreateSerializerOptions();
        var response = new KeyringHelperResponse
        {
            ExitCode = AdapterHostExitCode.Success,
            Stdout = stdout,
            Stderr = string.Empty,
        };

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(response, options));
    }

    [Theory]
    [InlineData("username\npassword\nextra\n")]
    [InlineData("username\npassword\n\n")]
    public void KeyringHelperResponseSerializationRejectsMalformedSuccessStdoutRecordCounts(
        string stdout
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        var response = new KeyringHelperResponse
        {
            ExitCode = AdapterHostExitCode.Success,
            Stdout = stdout,
            Stderr = string.Empty,
        };

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(response, options));
    }

    [Theory]
    [InlineData("protocol output")]
    [InlineData("password")]
    [InlineData("username\npassword")]
    public void KeyringHelperResponseSerializationRejectsSuccessStdoutWithoutTrailingLf(
        string stdout
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        var response = new KeyringHelperResponse
        {
            ExitCode = AdapterHostExitCode.Success,
            Stdout = stdout,
            Stderr = string.Empty,
        };

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(response, options));
    }

    [Theory]
    [InlineData("password\r\n")]
    [InlineData("username\r\npassword\n")]
    [InlineData("username\npassword\r\n")]
    public void KeyringHelperResponseSerializationRejectsSuccessStdoutWithCr(string stdout)
    {
        var options = ContractJson.CreateSerializerOptions();
        var response = new KeyringHelperResponse
        {
            ExitCode = AdapterHostExitCode.Success,
            Stdout = stdout,
            Stderr = string.Empty,
        };

        Assert.ThrowsAny<Exception>(() => JsonSerializer.Serialize(response, options));
    }

    [Theory]
    [InlineData(AdapterHostExitCode.Success, "success", "password\n", "")]
    [InlineData(AdapterHostExitCode.NoCredential, "noCredential", "", "")]
    [InlineData(AdapterHostExitCode.ConfigurationError, "configurationError", "", "diagnostic")]
    public void KeyringHelperResponseSerializationAllowsValidResponses(
        AdapterHostExitCode exitCode,
        string exitCodeWireName,
        string stdout,
        string stderr
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        var response = new KeyringHelperResponse
        {
            ExitCode = exitCode,
            Stdout = stdout,
            Stderr = stderr,
        };

        string json = JsonSerializer.Serialize(response, options);

        Assert.Contains("\"contractMajor\":2", json, StringComparison.Ordinal);
        Assert.Contains($"\"exitCode\":\"{exitCodeWireName}\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stdout")]
    [InlineData("stderr")]
    public void KeyringHelperResponseDeserializationRejectsExplicitNullOutputFields(
        string nullField
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string stdout = nullField == "stdout" ? "null" : "\"\"";
        string stderr = nullField == "stderr" ? "null" : "\"\"";
        string json = $$"""
            {
              "contractMajor": 2,
              "exitCode": "noCredential",
              "stdout": {{stdout}},
              "stderr": {{stderr}}
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(json, options)
        );
    }

    [Theory]
    [InlineData("noCredential")]
    [InlineData("interactionRequired")]
    [InlineData("unauthorized")]
    [InlineData("configurationError")]
    [InlineData("integrityFailure")]
    [InlineData("cacheUnavailable")]
    [InlineData("fatal")]
    public void KeyringHelperResponseDeserializationRejectsFailureStdout(string exitCode)
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": 2,
              "exitCode": "{{exitCode}}",
              "stdout": "unexpected protocol output",
              "stderr": ""
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(json, options)
        );
    }

    [Theory]
    [InlineData("password\n")]
    [InlineData("username\npassword\n")]
    public void KeyringHelperResponseDeserializationAllowsLfTerminatedSuccessStdout(string stdout)
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": 2,
              "exitCode": "success",
              "stdout": {{JsonSerializer.Serialize(stdout, options)}},
              "stderr": ""
            }
            """;

        KeyringHelperResponse? response = JsonSerializer.Deserialize<KeyringHelperResponse>(
            json,
            options
        );

        Assert.NotNull(response);
        Assert.Equal(AdapterHostExitCode.Success, response.ExitCode);
        Assert.Equal(stdout, response.Stdout);
    }

    [Theory]
    [InlineData("success", "password\n")]
    [InlineData("noCredential", "")]
    public void KeyringHelperResponseDeserializationRejectsSuccessAndNoCredentialStderr(
        string exitCode,
        string stdout
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": 2,
              "exitCode": "{{exitCode}}",
              "stdout": {{JsonSerializer.Serialize(stdout, options)}},
              "stderr": "diagnostic"
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(json, options)
        );
    }

    [Fact]
    public void KeyringHelperResponseDeserializationRejectsEmptySuccessStdout()
    {
        var options = ContractJson.CreateSerializerOptions();
        const string json = """
            {
              "contractMajor": 2,
              "exitCode": "success",
              "stdout": "",
              "stderr": ""
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(json, options)
        );
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\npassword\n")]
    [InlineData("username\n\n")]
    [InlineData("username\npassword\nextra\n")]
    [InlineData("username\npassword\n\n")]
    public void KeyringHelperResponseDeserializationRejectsMalformedSuccessStdoutRecords(
        string stdout
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": 2,
              "exitCode": "success",
              "stdout": {{JsonSerializer.Serialize(stdout, options)}},
              "stderr": ""
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(json, options)
        );
    }

    [Theory]
    [InlineData("protocol output")]
    [InlineData("password")]
    [InlineData("username\npassword")]
    public void KeyringHelperResponseDeserializationRejectsSuccessStdoutWithoutTrailingLf(
        string stdout
    )
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": 2,
              "exitCode": "success",
              "stdout": {{JsonSerializer.Serialize(stdout, options)}},
              "stderr": ""
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(json, options)
        );
    }

    [Theory]
    [InlineData("password\r\n")]
    [InlineData("username\r\npassword\n")]
    [InlineData("username\npassword\r\n")]
    public void KeyringHelperResponseDeserializationRejectsSuccessStdoutWithCr(string stdout)
    {
        var options = ContractJson.CreateSerializerOptions();
        string json = $$"""
            {
              "contractMajor": 2,
              "exitCode": "success",
              "stdout": {{JsonSerializer.Serialize(stdout, options)}},
              "stderr": ""
            }
            """;

        Assert.ThrowsAny<Exception>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(json, options)
        );
    }

    [Fact]
    public void ContractJsonPinsConfigurationPlanEnumWireShapesAndNpmConfigurationProtocol()
    {
        var options = ContractJson.CreateSerializerOptions();
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-json-configuration-enums",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            Manifest = CreateManifest("json-configuration-enums"),
            TemporaryContainer = CreateTemporaryHomeContainer(
                @"C:\agent\_temp\azureauth-credprovider\yarn-home"
            ),
            Changes =
            [
                CreateYarnAuthTokenChange("https://pkgs.dev.azure.com/org/_packaging/feed/npm") with
                {
                    Operation = ConfigurationChangeOperation.Remove,
                    TargetKind = ConfigurationTargetKind.Yarnrc,
                    TargetPathOrName =
                        @"C:\agent\_temp\azureauth-credprovider\yarn-home\.yarnrc.yml",
                    Value = null,
                    IsSecretValue = false,
                },
            ],
        };
        var adapter = new AdapterHostResult
        {
            Protocol = AdapterProtocol.NpmConfiguration,
            ExitCode = AdapterHostExitCode.Success,
            WriteProtocolStdout = false,
            WriteDiagnosticStderr = false,
        };

        string planJson = JsonSerializer.Serialize(plan, options);
        string adapterJson = JsonSerializer.Serialize(adapter, options);

        Assert.Contains("\"targetKind\":\"yarnrc\"", planJson, StringComparison.Ordinal);
        Assert.Contains(
            "\"declarationPreservation\":\"copyHiddenDeclarationsToTemporaryConfig\"",
            planJson,
            StringComparison.Ordinal
        );
        Assert.Contains("\"kind\":\"temporaryHome\"", planJson, StringComparison.Ordinal);
        Assert.Contains("\"activationEnvironment\"", planJson, StringComparison.Ordinal);
        Assert.Contains(
            "\"USERPROFILE\":\"C:\\\\agent\\\\_temp\\\\azureauth-credprovider\\\\yarn-home\"",
            planJson,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\"HOME\":\"C:\\\\agent\\\\_temp\\\\azureauth-credprovider\\\\yarn-home\"",
            planJson,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "\"clearVariables\":[\"HOMEDRIVE\",\"HOMEPATH\",\"YARN_RC_FILENAME\"]",
            planJson,
            StringComparison.Ordinal
        );
        Assert.Contains("\"protocol\":\"npmConfiguration\"", adapterJson, StringComparison.Ordinal);
        Assert.Equal(
            ConfigurationTargetKind.Yarnrc,
            JsonSerializer
                .Deserialize<ConfigurationChangePlan>(planJson, options)
                ?.Changes.Single()
                .TargetKind
        );
        Assert.Equal(
            ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
            JsonSerializer
                .Deserialize<ConfigurationChangePlan>(planJson, options)
                ?.DeclarationPreservation
        );
        Assert.Equal(
            ConfigurationTemporaryContainerKind.TemporaryHome,
            JsonSerializer
                .Deserialize<ConfigurationChangePlan>(planJson, options)
                ?.TemporaryContainer?.Kind
        );
        Assert.Equal(
            AdapterProtocol.NpmConfiguration,
            JsonSerializer.Deserialize<AdapterHostResult>(adapterJson, options)?.Protocol
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                planJson.Replace("\"yarnrc\"", "6", StringComparison.Ordinal),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                planJson.Replace(
                    "\"copyHiddenDeclarationsToTemporaryConfig\"",
                    "3",
                    StringComparison.Ordinal
                ),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                planJson.Replace("\"temporaryHome\"", "3", StringComparison.Ordinal),
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AdapterHostResult>(
                adapterJson.Replace("\"npmConfiguration\"", "5", StringComparison.Ordinal),
                options
            )
        );
    }

    [Fact]
    public void ContractJsonRequiresActivationEnvironmentClearVariablesOnWire()
    {
        var options = ContractJson.CreateSerializerOptions();
        const string temporaryHomePath = "/agent/_temp/azureauth-credprovider/generic-home";
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-json-posix-activation-clear-required",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.CiTemporary,
            DeclarationPreservation =
                ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig,
            Manifest = CreateManifest("json-posix-activation-clear-required"),
            TemporaryContainer = CreateGenericTemporaryHomeContainer(temporaryHomePath),
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    TargetKind = ConfigurationTargetKind.CiTemporaryFile,
                    TargetPathOrName = temporaryHomePath + "/owned.txt",
                    IsSecretValue = false,
                },
            ],
        };

        string planJson = JsonSerializer.Serialize(plan, options);
        string missingClearVariablesJson = planJson.Replace(
            ",\"clearVariables\":[]",
            string.Empty,
            StringComparison.Ordinal
        );

        Assert.Contains("\"clearVariables\":[]", planJson, StringComparison.Ordinal);
        Assert.True(
            ConfigurationChangePlanPolicy.IsValid(
                JsonSerializer.Deserialize<ConfigurationChangePlan>(planJson, options)!
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(missingClearVariablesJson, options)
        );
    }

    [Fact]
    public void ContractJsonPinsEveryPublicFrozenEnumMemberWireValue()
    {
        var options = ContractJson.CreateSerializerOptions();

        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        VerifyEnumWireValues(
            options,
            new Dictionary<CredentialEcosystem, string>
            {
                [CredentialEcosystem.Unspecified] = "unspecified",
                [CredentialEcosystem.Git] = "git",
                [CredentialEcosystem.NuGet] = "nuGet",
                [CredentialEcosystem.Python] = "python",
                [CredentialEcosystem.Npm] = "npm",
                [CredentialEcosystem.Pnpm] = "pnpm",
                [CredentialEcosystem.Yarn] = "yarn",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<CredentialOperation, string>
            {
                [CredentialOperation.Unspecified] = "unspecified",
                [CredentialOperation.Get] = "get",
                [CredentialOperation.Store] = "store",
                [CredentialOperation.Erase] = "erase",
                [CredentialOperation.Refresh] = "refresh",
                [CredentialOperation.Configure] = "configure",
                [CredentialOperation.Doctor] = "doctor",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<TokenAudience, string>
            {
                [TokenAudience.Unspecified] = "unspecified",
                [TokenAudience.AzureDevOps] = "azureDevOps",
                [TokenAudience.AzureArtifacts] = "azureArtifacts",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<CredentialKind, string>
            {
                [CredentialKind.Unspecified] = "unspecified",
                [CredentialKind.BasicPassword] = "basicPassword",
                [CredentialKind.BearerToken] = "bearerToken",
                [CredentialKind.NpmAuthToken] = "npmAuthToken",
                [CredentialKind.NuGetPluginCredential] = "nuGetPluginCredential",
                [CredentialKind.PatCompatibility] = "patCompatibility",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<IdentityFlow, string>
            {
                [IdentityFlow.Unspecified] = "unspecified",
                [IdentityFlow.InteractiveBrowser] = "interactiveBrowser",
                [IdentityFlow.DeviceCode] = "deviceCode",
                [IdentityFlow.PatCompatibility] = "patCompatibility",
                [IdentityFlow.AzurePipelinesSystemAccessToken] = "azurePipelinesSystemAccessToken",
                [IdentityFlow.ServicePrincipal] = "servicePrincipal",
                [IdentityFlow.ManagedIdentity] = "managedIdentity",
                [IdentityFlow.WorkloadIdentityFederation] = "workloadIdentityFederation",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<IdentityFlowState, string>
            {
                [IdentityFlowState.Unspecified] = "unspecified",
                [IdentityFlowState.AcceptedMvp] = "acceptedMvp",
                [IdentityFlowState.Deferred] = "deferred",
                [IdentityFlowState.Disabled] = "disabled",
                [IdentityFlowState.Unsupported] = "unsupported",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<InteractivePolicy, string>
            {
                [InteractivePolicy.Unspecified] = "unspecified",
                [InteractivePolicy.Never] = "never",
                [InteractivePolicy.HostToolAllows] = "hostToolAllows",
                [InteractivePolicy.UserAllowed] = "userAllowed",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<CachePolicyMode, string>
            {
                [CachePolicyMode.Unspecified] = "unspecified",
                [CachePolicyMode.NoCache] = "noCache",
                [CachePolicyMode.ProductPersistentCacheDisabled] = "productPersistentCacheDisabled",
                [CachePolicyMode.NonPersistentCi] = "nonPersistentCi",
                [CachePolicyMode.FuturePersistentCacheRequested] = "futurePersistentCacheRequested",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<CredentialResultStatus, string>
            {
                [CredentialResultStatus.Unspecified] = "unspecified",
                [CredentialResultStatus.Success] = "success",
                [CredentialResultStatus.NoCredential] = "noCredential",
                [CredentialResultStatus.InteractionRequired] = "interactionRequired",
                [CredentialResultStatus.InteractionBlocked] = "interactionBlocked",
                [CredentialResultStatus.Unauthorized] = "unauthorized",
                [CredentialResultStatus.CredentialUnavailable] = "credentialUnavailable",
                [CredentialResultStatus.FlowDeferred] = "flowDeferred",
                [CredentialResultStatus.FlowDisabled] = "flowDisabled",
                [CredentialResultStatus.UnsupportedFlow] = "unsupportedFlow",
                [CredentialResultStatus.CacheUnavailable] = "cacheUnavailable",
                [CredentialResultStatus.Fatal] = "fatal",
                [CredentialResultStatus.IntegrityFailure] = "integrityFailure",
                [CredentialResultStatus.ProtocolViolation] = "protocolViolation",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<CredentialErrorKind, string>
            {
                [CredentialErrorKind.Unspecified] = "unspecified",
                [CredentialErrorKind.UnsupportedHost] = "unsupportedHost",
                [CredentialErrorKind.UnsupportedFlow] = "unsupportedFlow",
                [CredentialErrorKind.FlowDeferred] = "flowDeferred",
                [CredentialErrorKind.FlowDisabled] = "flowDisabled",
                [CredentialErrorKind.InteractionRequired] = "interactionRequired",
                [CredentialErrorKind.InteractionBlocked] = "interactionBlocked",
                [CredentialErrorKind.CredentialUnavailable] = "credentialUnavailable",
                [CredentialErrorKind.Unauthorized] = "unauthorized",
                [CredentialErrorKind.CacheUnavailable] = "cacheUnavailable",
                [CredentialErrorKind.PolicyViolation] = "policyViolation",
                [CredentialErrorKind.IntegrityFailure] = "integrityFailure",
                [CredentialErrorKind.ProtocolViolation] = "protocolViolation",
                [CredentialErrorKind.Fatal] = "fatal",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<AcquisitionMode, string>
            {
                [AcquisitionMode.Unspecified] = "unspecified",
                [AcquisitionMode.SilentOnly] = "silentOnly",
                [AcquisitionMode.InteractionAllowed] = "interactionAllowed",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<AdapterProtocol, string>
            {
                [AdapterProtocol.Unspecified] = "unspecified",
                [AdapterProtocol.GitCredentialHelper] = "gitCredentialHelper",
                [AdapterProtocol.NuGetPlugin] = "nuGetPlugin",
                [AdapterProtocol.PythonKeyringBackend] = "pythonKeyringBackend",
                [AdapterProtocol.KeyringHelper] = "keyringHelper",
                [AdapterProtocol.NpmConfiguration] = "npmConfiguration",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<AdapterHostExitCode, string>
            {
                [AdapterHostExitCode.Success] = "success",
                [AdapterHostExitCode.NoCredential] = "noCredential",
                [AdapterHostExitCode.InteractionRequired] = "interactionRequired",
                [AdapterHostExitCode.Unauthorized] = "unauthorized",
                [AdapterHostExitCode.ConfigurationError] = "configurationError",
                [AdapterHostExitCode.IntegrityFailure] = "integrityFailure",
                [AdapterHostExitCode.CacheUnavailable] = "cacheUnavailable",
                [AdapterHostExitCode.Fatal] = "fatal",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<ConfigurationChangeOperation, string>
            {
                [ConfigurationChangeOperation.Unspecified] = "unspecified",
                [ConfigurationChangeOperation.Set] = "set",
                [ConfigurationChangeOperation.Remove] = "remove",
                [ConfigurationChangeOperation.EnsureFile] = "ensureFile",
                [ConfigurationChangeOperation.InstallAdapter] = "installAdapter",
                [ConfigurationChangeOperation.RemoveAdapter] = "removeAdapter",
                [ConfigurationChangeOperation.Create] = "create",
                [ConfigurationChangeOperation.Update] = "update",
                [ConfigurationChangeOperation.Refresh] = "refresh",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<ConfigurationScope, string>
            {
                [ConfigurationScope.Unspecified] = "unspecified",
                [ConfigurationScope.User] = "user",
                [ConfigurationScope.WorkspaceReadOnly] = "workspaceReadOnly",
                [ConfigurationScope.ExplicitPath] = "explicitPath",
                [ConfigurationScope.CiTemporary] = "ciTemporary",
                [ConfigurationScope.Global] = "global",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<ConfigurationTargetKind, string>
            {
                [ConfigurationTargetKind.Unspecified] = "unspecified",
                [ConfigurationTargetKind.GitConfig] = "gitConfig",
                [ConfigurationTargetKind.NuGetPluginLayout] = "nuGetPluginLayout",
                [ConfigurationTargetKind.PythonKeyringBackend] = "pythonKeyringBackend",
                [ConfigurationTargetKind.KeyringShim] = "keyringShim",
                [ConfigurationTargetKind.Npmrc] = "npmrc",
                [ConfigurationTargetKind.Yarnrc] = "yarnrc",
                [ConfigurationTargetKind.CiTemporaryFile] = "ciTemporaryFile",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<ConfigurationDeclarationPreservation, string>
            {
                [ConfigurationDeclarationPreservation.Unspecified] = "unspecified",
                [ConfigurationDeclarationPreservation.NotApplicable] = "notApplicable",
                [ConfigurationDeclarationPreservation.AuthOnlyWhenDeclarationsRemainVisible] =
                    "authOnlyWhenDeclarationsRemainVisible",
                [ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig] =
                    "copyHiddenDeclarationsToTemporaryConfig",
                [ConfigurationDeclarationPreservation.CompleteMergedTemporaryConfig] =
                    "completeMergedTemporaryConfig",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<ConfigurationTemporaryContainerKind, string>
            {
                [ConfigurationTemporaryContainerKind.Unspecified] = "unspecified",
                [ConfigurationTemporaryContainerKind.None] = "none",
                [ConfigurationTemporaryContainerKind.NpmrcFile] = "npmrcFile",
                [ConfigurationTemporaryContainerKind.TemporaryHome] = "temporaryHome",
                [ConfigurationTemporaryContainerKind.YarnRcFile] = "yarnRcFile",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<DoctorCheckStatus, string>
            {
                [DoctorCheckStatus.Unspecified] = "unspecified",
                [DoctorCheckStatus.Pass] = "pass",
                [DoctorCheckStatus.Warning] = "warning",
                [DoctorCheckStatus.Fail] = "fail",
                [DoctorCheckStatus.Skipped] = "skipped",
                [DoctorCheckStatus.Unsupported] = "unsupported",
                [DoctorCheckStatus.Deferred] = "deferred",
                [DoctorCheckStatus.NotApplicable] = "notApplicable",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<DoctorCheckSeverity, string>
            {
                [DoctorCheckSeverity.Unspecified] = "unspecified",
                [DoctorCheckSeverity.Info] = "info",
                [DoctorCheckSeverity.Warning] = "warning",
                [DoctorCheckSeverity.Error] = "error",
            }
        );
        VerifyEnumWireValues(
            options,
            new Dictionary<KeyringHelperMode, string>
            {
                [KeyringHelperMode.Unspecified] = "unspecified",
                [KeyringHelperMode.Password] = "password",
                [KeyringHelperMode.Credentials] = "credentials",
            }
        );
    }

    [Fact]
    public void ContractJsonRejectsNonCanonicalEnumDeclaredNameCasing()
    {
        var options = ContractJson.CreateSerializerOptions();
        Type[] publicEnumTypes = typeof(ContractVersions)
            .Assembly.ExportedTypes.Where(static type => type.IsEnum)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        Assert.All(
            publicEnumTypes,
            enumType =>
            {
                Assert.All(
                    Enum.GetNames(enumType),
                    declaredName =>
                    {
                        string canonicalName = JsonNamingPolicy.CamelCase.ConvertName(declaredName);
                        Assert.NotEqual(canonicalName, declaredName);
                        Assert.Throws<JsonException>(() =>
                            JsonSerializer.Deserialize(
                                SerializeJsonStringLiteral(declaredName),
                                enumType,
                                options
                            )
                        );
                    }
                );
            }
        );
    }

    [Fact]
    public void EveryPublicContractEnumRetainsFrozenLiteralNumericValues()
    {
        var expected = new Dictionary<Type, string>
        {
            [typeof(AcquisitionMode)] = "Unspecified=0;SilentOnly=1;InteractionAllowed=2",
            [typeof(AdapterHostExitCode)] =
                "Success=0;NoCredential=1;InteractionRequired=2;Unauthorized=3;"
                + "ConfigurationError=64;IntegrityFailure=65;CacheUnavailable=69;Fatal=70",
            [typeof(AdapterProtocol)] =
                "Unspecified=0;GitCredentialHelper=1;NuGetPlugin=2;PythonKeyringBackend=3;"
                + "KeyringHelper=4;NpmConfiguration=5",
            [typeof(CachePolicyMode)] =
                "Unspecified=0;NoCache=1;ProductPersistentCacheDisabled=2;NonPersistentCi=3;"
                + "FuturePersistentCacheRequested=4",
            [typeof(ConfigurationChangeOperation)] =
                "Unspecified=0;Set=1;Remove=2;EnsureFile=3;InstallAdapter=4;RemoveAdapter=5;"
                + "Create=6;Update=7;Refresh=8",
            [typeof(ConfigurationDeclarationPreservation)] =
                "Unspecified=0;NotApplicable=1;AuthOnlyWhenDeclarationsRemainVisible=2;"
                + "CopyHiddenDeclarationsToTemporaryConfig=3;CompleteMergedTemporaryConfig=4",
            [typeof(ConfigurationScope)] =
                "Unspecified=0;User=1;WorkspaceReadOnly=2;ExplicitPath=3;CiTemporary=4;Global=5",
            [typeof(ConfigurationTargetKind)] =
                "Unspecified=0;GitConfig=1;NuGetPluginLayout=2;PythonKeyringBackend=3;"
                + "KeyringShim=4;Npmrc=5;Yarnrc=6;CiTemporaryFile=7",
            [typeof(ConfigurationTemporaryContainerKind)] =
                "Unspecified=0;None=1;NpmrcFile=2;TemporaryHome=3;YarnRcFile=4",
            [typeof(CredentialEcosystem)] =
                "Unspecified=0;Git=1;NuGet=2;Python=3;Npm=4;Pnpm=5;Yarn=6",
            [typeof(CredentialErrorKind)] =
                "Unspecified=0;UnsupportedHost=1;UnsupportedFlow=2;FlowDeferred=3;"
                + "FlowDisabled=4;InteractionRequired=5;InteractionBlocked=6;"
                + "CredentialUnavailable=7;Unauthorized=8;CacheUnavailable=9;"
                + "PolicyViolation=10;IntegrityFailure=11;ProtocolViolation=12;Fatal=13",
            [typeof(CredentialKind)] =
                "Unspecified=0;BasicPassword=1;BearerToken=2;NpmAuthToken=3;"
                + "NuGetPluginCredential=4;PatCompatibility=5",
            [typeof(CredentialOperation)] =
                "Unspecified=0;Get=1;Store=2;Erase=3;Refresh=4;Configure=5;Doctor=6",
            [typeof(CredentialResultStatus)] =
                "Unspecified=0;Success=1;NoCredential=2;InteractionRequired=3;"
                + "InteractionBlocked=4;Unauthorized=5;CredentialUnavailable=6;"
                + "FlowDeferred=7;FlowDisabled=8;UnsupportedFlow=9;CacheUnavailable=10;"
                + "Fatal=11;IntegrityFailure=12;ProtocolViolation=13",
            [typeof(DoctorCheckSeverity)] = "Unspecified=0;Info=1;Warning=2;Error=3",
            [typeof(DoctorCheckStatus)] =
                "Unspecified=0;Pass=1;Warning=2;Fail=3;Skipped=4;Unsupported=5;Deferred=6;"
                + "NotApplicable=7",
            [typeof(IdentityFlow)] =
                "Unspecified=0;InteractiveBrowser=1;DeviceCode=2;PatCompatibility=3;"
                + "AzurePipelinesSystemAccessToken=4;ServicePrincipal=5;ManagedIdentity=6;"
                + "WorkloadIdentityFederation=7",
            [typeof(IdentityFlowState)] =
                "Unspecified=0;AcceptedMvp=1;Deferred=2;Disabled=3;Unsupported=4",
            [typeof(InteractivePolicy)] =
                "Unspecified=0;Never=1;HostToolAllows=2;UserAllowed=3",
            [typeof(KeyringHelperMode)] = "Unspecified=0;Password=1;Credentials=2",
            [typeof(TokenAudience)] = "Unspecified=0;AzureDevOps=1;AzureArtifacts=2",
        };
        Type[] publicEnumTypes = typeof(ContractVersions)
            .Assembly.ExportedTypes.Where(static type => type.IsEnum)
            .OrderBy(static type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expected.Keys.OrderBy(static type => type.FullName, StringComparer.Ordinal),
            publicEnumTypes
        );
        Assert.All(
            expected,
            pair =>
            {
                string actual = string.Join(
                    ';',
                    Enum.GetNames(pair.Key)
                        .Select(name =>
                            string.Create(
                                CultureInfo.InvariantCulture,
                                $"{name}={Convert.ToInt32(
                                    Enum.Parse(pair.Key, name),
                                    CultureInfo.InvariantCulture
                                )}"
                            )
                        )
                );

                Assert.Equal(pair.Value, actual);
            }
        );
    }

    [Fact]
    public void ContractJsonRejectsUndefinedAdapterProtocolDuringSourceGeneratedRootSerialization()
    {
        var options = ContractJson.CreateSerializerOptions();
        var adapter = new AdapterHostResult
        {
            Protocol = (AdapterProtocol)999,
            ExitCode = AdapterHostExitCode.ConfigurationError,
            WriteProtocolStdout = false,
            WriteDiagnosticStderr = true,
        };

        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(adapter, options));
    }

    [Fact]
    public void ContractJsonRequiresContractMajorOnVersionedContracts()
    {
        var options = ContractJson.CreateSerializerOptions();
        string missingCredentialRequestVersion = CreateCredentialRequestJson(
            "\"ecosystem\":\"git\"",
            includeContractMajor: false
        );
        string missingCredentialResultVersion = """
            {
              "status": "protocolViolation",
              "diagnosticsCorrelationId": "corr-missing-version"
            }
            """;
        string missingConfigurationVersion = """
            {
              "planId": "plan-missing-version",
              "ownerProductId": "azureauth-credprovider",
              "scope": "global",
              "manifest": {
                "manifestId": "manifest-missing-version",
                "ownerProductId": "azureauth-credprovider",
                "entrySelector": "missing-version"
              }
            }
            """;
        string missingDoctorVersion = """
            {
              "checkId": "doctor.missing.version",
              "status": "fail",
              "severity": "error",
              "target": "JSON wire shape",
              "summary": "Missing version.",
              "diagnosticsCorrelationId": "corr-doctor-missing-version"
            }
            """;
        string missingAdapterHostResultVersion = """
            {
              "protocol": "gitCredentialHelper",
              "exitCode": "configurationError",
              "writeProtocolStdout": false,
              "writeDiagnosticStderr": true
            }
            """;
        string missingKeyringHelperRequestVersion = """
            {
              "command": "python-keyring",
              "service": "https://dev.azure.com/org/proj/_git/repo",
              "mode": "password"
            }
            """;
        string missingKeyringHelperResponseVersion = """
            {
              "exitCode": "noCredential",
              "stdout": "",
              "stderr": ""
            }
            """;
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CredentialRequest>(missingCredentialRequestVersion, options)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CredentialResult>(missingCredentialResultVersion, options)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<ConfigurationChangePlan>(
                missingConfigurationVersion,
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DoctorCheck>(missingDoctorVersion, options)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AdapterHostResult>(missingAdapterHostResultVersion, options)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<KeyringHelperRequest>(
                missingKeyringHelperRequestVersion,
                options
            )
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<KeyringHelperResponse>(
                missingKeyringHelperResponseVersion,
                options
            )
        );
    }

    [Fact]
    public void ContractJsonRoundTripsResultConfigurationAndDoctorContracts()
    {
        var options = ContractJson.CreateSerializerOptions();
        const string canonicalSelector =
            "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken";
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.ProtocolViolation,
            DiagnosticsCorrelationId = "corr-json-result",
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.ProtocolViolation,
                Code = "ProtocolViolation",
                SafeMessage = "Protocol output was malformed.",
            },
        };
        var plan = new ConfigurationChangePlan
        {
            PlanId = "plan-json",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.Global,
            Manifest = CreateManifest("json") with
            {
                EntrySelector = canonicalSelector,
                ResourceIdentity = CreateNpmResourceIdentity(),
            },
            ContainsCredentialMaterial = true,
            Changes = [CreateConfigurationChange(ConfigurationChangeOperation.Refresh) with { }],
        };
        var check = new DoctorCheck
        {
            CheckId = "doctor.json",
            Status = DoctorCheckStatus.NotApplicable,
            Severity = DoctorCheckSeverity.Info,
            Target = "JSON wire shape",
            Summary = "JSON shape check.",
            DiagnosticsCorrelationId = "corr-json-doctor",
            ObservedValue = "notApplicable",
            ExpectedValue = "notApplicable",
        };

        string resultJson = JsonSerializer.Serialize(result, options);
        string planJson = JsonSerializer.Serialize(plan, options);
        string checkJson = JsonSerializer.Serialize(check, options);

        Assert.Contains("\"status\":\"protocolViolation\"", resultJson, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"protocolViolation\"", resultJson, StringComparison.Ordinal);
        Assert.Contains("\"scope\":\"global\"", planJson, StringComparison.Ordinal);
        Assert.Contains("\"operation\":\"refresh\"", planJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"notApplicable\"", checkJson, StringComparison.Ordinal);
        Assert.Equal(
            CredentialResultStatus.ProtocolViolation,
            JsonSerializer.Deserialize<CredentialResult>(resultJson, options)?.Status
        );
        Assert.Equal(
            ConfigurationScope.Global,
            JsonSerializer.Deserialize<ConfigurationChangePlan>(planJson, options)?.Scope
        );
        Assert.Equal(
            DoctorCheckStatus.NotApplicable,
            JsonSerializer.Deserialize<DoctorCheck>(checkJson, options)?.Status
        );
    }

    [Fact]
    public void ContractJsonRoundTripsCredentialResultBearerTokenWireShapeForGitBasicMapping()
    {
        var options = ContractJson.CreateSerializerOptions();
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            BearerToken = "system-access-token",
            DiagnosticsCorrelationId = "corr-json-bearer-token-result",
        };

        string json = JsonSerializer.Serialize(result, options);
        var roundTripped = JsonSerializer.Deserialize<CredentialResult>(json, options);

        Assert.Contains("\"bearerToken\":\"system-access-token\"", json, StringComparison.Ordinal);
        Assert.NotNull(roundTripped);
        Assert.True(
            AdapterHostResultMapper.TryMapGitCredentialHelperBasicMaterial(
                roundTripped,
                out string? username,
                out string? password
            )
        );
        Assert.Equal(AdapterHostResultMapper.GitCredentialHelperBearerTokenUsername, username);
        Assert.Equal("system-access-token", password);
    }

    [Fact]
    public void ContractJsonRoundTripsAllFrozenContractRootsWithoutReflectionResolver()
    {
        var options = ContractJson.CreateSerializerOptions();
        CacheKey cacheKey = CacheKeySchema.Create(
            CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword),
            "user@example.com",
            "tenant-1"
        );
        object[] roots =
        [
            CreateRequest(IdentityFlow.DeviceCode, CredentialKind.BasicPassword),
            new CredentialResult
            {
                Status = CredentialResultStatus.Success,
                Username = "AzureDevOps",
                Password = "generated-password",
                DiagnosticsCorrelationId = "corr-json-all-roots",
            },
            cacheKey,
            new ConfigurationChangePlan
            {
                PlanId = "plan-json-all-roots",
                OwnerProductId = "azureauth-credprovider",
                Scope = ConfigurationScope.CiTemporary,
                Manifest = CreateManifest("json-all-roots") with
                {
                    EntrySelector =
                        "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
                    ResourceIdentity = CreateNpmResourceIdentity(),
                },
                ContainsCredentialMaterial = true,
                TemporaryContainer = CreateNpmrcFileContainer(
                    @"C:\agent\_temp\azureauth-credprovider\.npmrc"
                ),
                DeclarationPreservation =
                    ConfigurationDeclarationPreservation.CopyHiddenDeclarationsToTemporaryConfig,
                Changes =
                [
                    CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                    {
                        TargetPathOrName = @"C:\agent\_temp\azureauth-credprovider\.npmrc",
                    },
                ],
            },
            new DoctorCheck
            {
                CheckId = "doctor.json.all.roots",
                Status = DoctorCheckStatus.Pass,
                Severity = DoctorCheckSeverity.Info,
                Target = "JSON source generation",
                Summary = "Source-generated contract JSON is available.",
                DiagnosticsCorrelationId = "corr-json-doctor-all-roots",
            },
            AdapterHostResultMapper.Map(
                AdapterProtocol.GitCredentialHelper,
                new CredentialResult
                {
                    Status = CredentialResultStatus.NoCredential,
                    DiagnosticsCorrelationId = "corr-json-adapter-root",
                }
            ),
            new KeyringHelperRequest
            {
                Command = KeyringHelperV2.CommandName,
                Service = new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple/"),
                Mode = KeyringHelperMode.Password,
            },
            new KeyringHelperResponse
            {
                ExitCode = AdapterHostExitCode.NoCredential,
                Stdout = string.Empty,
                Stderr = string.Empty,
            },
            NpmCompatibleAuthSelectorPolicy.Create(
                CanonicalResourceIdentity.Create(
                    "pkgs.dev.azure.com",
                    "org",
                    new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"),
                    feed: "feed"
                )
            ),
        ];

        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        Assert.NotNull(options.TypeInfoResolver);
        Assert.All(
            roots,
            root =>
            {
                string json = JsonSerializer.Serialize(root, root.GetType(), options);
                object? roundTripped = JsonSerializer.Deserialize(json, root.GetType(), options);

                Assert.NotNull(roundTripped);
                if (root is CacheKey expectedCacheKey)
                {
                    var actualCacheKey = Assert.IsType<CacheKey>(roundTripped);
                    Assert.Equal(expectedCacheKey.SchemaMajor, actualCacheKey.SchemaMajor);
                    Assert.Equal(expectedCacheKey.Value, actualCacheKey.Value);
                }
            }
        );
        Assert.Equal(ContractVersions.CacheKeySchemaMajor, cacheKey.SchemaMajor);
        Assert.Equal(
            "azdo-cache-v1|Z2l0|ZGV2LmF6dXJlLmNvbQ==|b3Jn|-|-|-"
                + "|ZGVmYXVsdA==|dXNlckBleGFtcGxlLmNvbQ==|dGVuYW50LTE=|YXp1cmVkZXZvcHM=|YmFzaWNwYXN"
                + "zd29yZA==",
            cacheKey.Value
        );
    }

    public static IEnumerable<object?[]> AdapterHostStatusMappingCases =>
        [
            [
                CredentialResultStatus.Unspecified,
                AdapterHostExitCode.ConfigurationError,
                false,
                true,
                null,
            ],
            [CredentialResultStatus.Success, AdapterHostExitCode.Success, true, false, null],
            [
                CredentialResultStatus.NoCredential,
                AdapterHostExitCode.NoCredential,
                false,
                false,
                null,
            ],
            [
                CredentialResultStatus.InteractionRequired,
                AdapterHostExitCode.InteractionRequired,
                false,
                true,
                "InteractionRequired",
            ],
            [
                CredentialResultStatus.InteractionBlocked,
                AdapterHostExitCode.InteractionRequired,
                false,
                true,
                "InteractionBlocked",
            ],
            [
                CredentialResultStatus.Unauthorized,
                AdapterHostExitCode.Unauthorized,
                false,
                true,
                "Unauthorized",
            ],
            [
                CredentialResultStatus.CredentialUnavailable,
                AdapterHostExitCode.ConfigurationError,
                false,
                true,
                "CredentialUnavailable",
            ],
            [
                CredentialResultStatus.FlowDeferred,
                AdapterHostExitCode.ConfigurationError,
                false,
                true,
                "FlowDeferred",
            ],
            [
                CredentialResultStatus.FlowDisabled,
                AdapterHostExitCode.ConfigurationError,
                false,
                true,
                "FlowDisabled",
            ],
            [
                CredentialResultStatus.UnsupportedFlow,
                AdapterHostExitCode.ConfigurationError,
                false,
                true,
                "UnsupportedFlow",
            ],
            [
                CredentialResultStatus.CacheUnavailable,
                AdapterHostExitCode.CacheUnavailable,
                false,
                true,
                "CacheUnavailable",
            ],
            [CredentialResultStatus.Fatal, AdapterHostExitCode.Fatal, false, true, "Fatal"],
            [
                CredentialResultStatus.IntegrityFailure,
                AdapterHostExitCode.IntegrityFailure,
                false,
                true,
                "IntegrityFailure",
            ],
            [
                CredentialResultStatus.ProtocolViolation,
                AdapterHostExitCode.ConfigurationError,
                false,
                true,
                null,
            ],
        ];

    public static IEnumerable<object[]> HardCredentialErrorMappingCases =>
        [
            [
                CredentialErrorKind.InteractionRequired,
                AdapterHostExitCode.InteractionRequired,
                "InteractionRequired",
            ],
            [
                CredentialErrorKind.InteractionBlocked,
                AdapterHostExitCode.InteractionRequired,
                "InteractionBlocked",
            ],
            [CredentialErrorKind.Unauthorized, AdapterHostExitCode.Unauthorized, "Unauthorized"],
            [
                CredentialErrorKind.CacheUnavailable,
                AdapterHostExitCode.CacheUnavailable,
                "CacheUnavailable",
            ],
            [
                CredentialErrorKind.CredentialUnavailable,
                AdapterHostExitCode.ConfigurationError,
                "CredentialUnavailable",
            ],
            [
                CredentialErrorKind.FlowDeferred,
                AdapterHostExitCode.ConfigurationError,
                "FlowDeferred",
            ],
            [
                CredentialErrorKind.FlowDisabled,
                AdapterHostExitCode.ConfigurationError,
                "FlowDisabled",
            ],
            [
                CredentialErrorKind.UnsupportedFlow,
                AdapterHostExitCode.ConfigurationError,
                "UnsupportedFlow",
            ],
            [
                CredentialErrorKind.PolicyViolation,
                AdapterHostExitCode.ConfigurationError,
                "PolicyViolation",
            ],
            [
                CredentialErrorKind.IntegrityFailure,
                AdapterHostExitCode.IntegrityFailure,
                "IntegrityFailure",
            ],
            [
                CredentialErrorKind.ProtocolViolation,
                AdapterHostExitCode.ConfigurationError,
                "ProtocolViolation",
            ],
            [CredentialErrorKind.Fatal, AdapterHostExitCode.Fatal, "Fatal"],
        ];

    private static void VerifyEnumWireValues<TEnum>(
        JsonSerializerOptions options,
        IReadOnlyDictionary<TEnum, string> expected
    )
        where TEnum : struct, Enum
    {
        TEnum[] definedValues = Enum.GetValues<TEnum>().Order().ToArray();

        Assert.Equal(definedValues, expected.Keys.Order().ToArray());
        foreach ((TEnum value, string wireValue) in expected)
        {
            string json = JsonSerializer.Serialize(value, options);
            string numericString = Convert
                .ToInt32(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);

            Assert.Equal($"\"{wireValue}\"", json);
            Assert.Equal(value, JsonSerializer.Deserialize<TEnum>(json, options));
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<TEnum>(
                    Convert
                        .ToInt32(value, CultureInfo.InvariantCulture)
                        .ToString(CultureInfo.InvariantCulture),
                    options
                )
            );
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<TEnum>(
                    SerializeJsonStringLiteral(numericString),
                    options
                )
            );
            Assert.Throws<JsonException>(() =>
                JsonSerializer.Deserialize<TEnum>("\"futureValue\"", options)
            );
        }

        string compositeWireValue = string.Join(
            ',',
            expected
                .OrderBy(pair => Convert.ToInt32(pair.Key, CultureInfo.InvariantCulture))
                .Skip(1)
                .Take(2)
                .Select(static pair => pair.Value)
        );
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<TEnum>(
                SerializeJsonStringLiteral(compositeWireValue),
                options
            )
        );

        var undefined = (TEnum)Enum.ToObject(typeof(TEnum), 999);
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(undefined, options));
    }

    private static ConfigurationManifestMetadata CreateManifest(string suffix) =>
        new()
        {
            ManifestId = $"manifest-{suffix}",
            OwnerProductId = "azureauth-credprovider",
            EntrySelector = suffix,
            ProductVersion = "1.0.0",
        };

    private static CanonicalResourceIdentity CreateNpmResourceIdentity() =>
        CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"),
            feed: "feed"
        );

    private static ConfigurationChangePlan CreateYarnSecretAuthTokenPlan()
    {
        const string registryUrl =
            "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/";
        CanonicalResourceIdentity resource = CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri(registryUrl),
            feed: "feed"
        );
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).YarnAuthTokenKey;

        return new ConfigurationChangePlan
        {
            PlanId = "plan-yarn-secret-auth-token",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            Manifest = CreateManifest("yarn-secret-auth-token") with
            {
                EntrySelector = selector,
                ResourceIdentity = resource,
            },
            ContainsCredentialMaterial = true,
            Changes =
            [
                new ConfigurationChange
                {
                    Operation = ConfigurationChangeOperation.Set,
                    TargetKind = ConfigurationTargetKind.Yarnrc,
                    TargetPathOrName = "user .yarnrc.yml",
                    Key = selector,
                    Value = "secret-token",
                    RequiresOwnershipRecord = true,
                    IsSecretValue = true,
                },
            ],
        };
    }

    private static ConfigurationChangePlan CreateValidConfigurationPlan() =>
        new()
        {
            PlanId = "plan-valid-configuration",
            OwnerProductId = "azureauth-credprovider",
            Scope = ConfigurationScope.User,
            Manifest = CreateManifest("valid-configuration") with
            {
                EntrySelector = "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
                ResourceIdentity = CreateNpmResourceIdentity(),
            },
            Changes =
            [
                CreateConfigurationChange(ConfigurationChangeOperation.Create) with
                {
                    Key = "always-auth",
                    IsSecretValue = false,
                    Value = "true",
                },
            ],
        };

    private static ConfigurationTemporaryContainer CreateTemporaryHomeContainer(
        string productOwnedPath
    ) =>
        new()
        {
            Kind = ConfigurationTemporaryContainerKind.TemporaryHome,
            ProductOwnedPath = productOwnedPath,
            ActivationEnvironment = CreateTemporaryHomeActivationEnvironment(productOwnedPath),
        };

    private static ConfigurationTemporaryContainer CreateGenericTemporaryHomeContainer(
        string productOwnedPath
    ) =>
        new()
        {
            Kind = ConfigurationTemporaryContainerKind.TemporaryHome,
            ProductOwnedPath = productOwnedPath,
            ActivationEnvironment = IsPosixPath(productOwnedPath)
                ? new ConfigurationActivationEnvironment
                {
                    SetVariables = new Dictionary<string, string> { ["HOME"] = productOwnedPath },
                    ClearVariables = [],
                }
                : new ConfigurationActivationEnvironment
                {
                    SetVariables = new Dictionary<string, string>
                    {
                        ["USERPROFILE"] = productOwnedPath,
                        ["HOME"] = productOwnedPath,
                    },
                    ClearVariables = ["HOMEDRIVE", "HOMEPATH"],
                },
        };

    private static ConfigurationTemporaryContainer CreateNpmrcFileContainer(
        string productOwnedPath
    ) =>
        new()
        {
            Kind = ConfigurationTemporaryContainerKind.NpmrcFile,
            ProductOwnedPath = productOwnedPath,
            ActivationEnvironment = CreateNpmrcFileActivationEnvironment(productOwnedPath),
        };

    private static ConfigurationActivationEnvironment CreateNpmrcFileActivationEnvironment(
        string productOwnedPath
    ) =>
        new()
        {
            Platform = IsPosixPath(productOwnedPath) ? "posix" : "windows",
            SetVariables = IsPosixPath(productOwnedPath)
                ? new Dictionary<string, string>
                {
                    ["NPM_CONFIG_USERCONFIG"] = productOwnedPath,
                    ["npm_config_userconfig"] = productOwnedPath,
                }
                : new Dictionary<string, string> { ["NPM_CONFIG_USERCONFIG"] = productOwnedPath },
            ClearVariables = [],
        };

    private static ConfigurationActivationEnvironment CreateTemporaryHomeActivationEnvironment(
        string productOwnedPath
    )
    {
        return IsPosixPath(productOwnedPath)
            ? new ConfigurationActivationEnvironment
            {
                SetVariables = new Dictionary<string, string> { ["HOME"] = productOwnedPath },
                ClearVariables = ["YARN_RC_FILENAME"],
            }
            : new ConfigurationActivationEnvironment
            {
                SetVariables = new Dictionary<string, string>
                {
                    ["USERPROFILE"] = productOwnedPath,
                    ["HOME"] = productOwnedPath,
                },
                ClearVariables = ["HOMEDRIVE", "HOMEPATH", "YARN_RC_FILENAME"],
            };
    }

    private static bool IsPosixPath(string path) =>
        path.Length > 0 && path[0] == '/' && !path.StartsWith("//", StringComparison.Ordinal);

    private static string CreateConfigurationPlanJson() =>
        "{\"contractMajor\":1,\"planId\":\"plan-json-required\","
        + "\"ownerProductId\":\"azureauth-credprovider\",\"scope\":\"user\","
        + "\"manifest\":{\"manifestId\":\"manifest-json-required\","
        + "\"ownerProductId\":\"azureauth-credprovider\","
        + "\"entrySelector\":\"json-required\",\"productVersion\":\"1.0.0\"},"
        + "\"declarationPreservation\":\"notApplicable\","
        + "\"containsCredentialMaterial\":false,"
        + "\"changes\":[{\"operation\":\"create\",\"targetKind\":\"npmrc\","
        + "\"targetPathOrName\":\"user .npmrc\",\"key\":\"always-auth\","
        + "\"value\":\"true\",\"requiresOwnershipRecord\":true,"
        + "\"isSecretValue\":false}]}";

    public static TheoryData<string> MalformedCacheKeyValues()
    {
        string valid = CacheKeySchema
            .Create(
                CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword),
                "user@example.com",
                "tenant-1"
            )
            .Value;
        string[] parts = valid.Split('|');
        string WithPart(int index, string value)
        {
            string[] copy = (string[])parts.Clone();
            copy[index] = value;
            return string.Join('|', copy);
        }

        string WithProjectAndRepository()
        {
            string[] copy = (string[])parts.Clone();
            copy[4] = "cHJvag==";
            copy[6] = "cmVwbw==";
            return string.Join('|', copy);
        }

        return
        [
            string.Join('|', parts[..^1]),
            valid + "|ZXh0cmE=",
            valid.Replace("|b3Jn|", "||", StringComparison.Ordinal),
            valid.Replace("|b3Jn|", "|not@base64|", StringComparison.Ordinal),
            valid.Replace("|Z2l0|", "|c3Zu|", StringComparison.Ordinal),
            valid.Replace("|YXp1cmVkZXZvcHM=|", "|dW5rbm93bg==|", StringComparison.Ordinal),
            valid.Replace("|YmFzaWNwYXNzd29yZA==", "|dW5rbm93bg==", StringComparison.Ordinal),
            WithPart(0, "-"),
            WithPart(1, "-"),
            valid.Replace("|ZGVmYXVsdA==|", "|-|", StringComparison.Ordinal),
            valid.Replace("|ZGVmYXVsdA==|", "|UHJvZEFwcA==|", StringComparison.Ordinal),
            valid.Replace("|ZGV2LmF6dXJlLmNvbQ==|", "|ZXhhbXBsZS5jb20=|", StringComparison.Ordinal),
            valid.Replace("|b3Jn|", "|X2dpdA==|", StringComparison.Ordinal),
            WithPart(4, "cHJvag=="),
            WithPart(6, "X3BhY2thZ2luZw=="),
            WithProjectAndRepository(),
            valid
                .Replace(
                    "|ZGV2LmF6dXJlLmNvbQ==|",
                    "|b3JnLnZpc3VhbHN0dWRpby5jb20=|",
                    StringComparison.Ordinal
                )
                .Replace("|b3Jn|", "|b3RoZXI=|", StringComparison.Ordinal),
        ];
    }

    public static TheoryData<string, int, string> NonCanonicalCacheKeyPartitionAliases() =>
        new()
        {
            { "git", 1, "Git" },
            { "git", 2, "DEV.AZURE.COM" },
            { "git", 2, " dev.azure.com " },
            { "git", 3, " Org " },
            { "git", 4, "Proj" },
            { "package", 5, " Feed " },
            { "git", 6, "Repo" },
            { "git", 7, "Default" },
            { "git", 8, "User@Example.COM" },
            { "git", 9, " Tenant-1 " },
            { "git", 10, "AzureDevOps" },
            { "git", 11, "BasicPassword" },
        };

    public static TheoryData<string, int, string> NonCanonicalCacheKeyEncodingAliases() =>
        new() { { "user@example.com", 8, "unpadded" }, { "acct¾", 8, "base64url" } };

    public static TheoryData<CacheKey> CacheKeyResourcePartitionsWithDecodedSeparators()
    {
        string git = CacheKeySchema
            .Create(
                CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword),
                "user@example.com",
                "tenant-1"
            )
            .Value;
        string[] gitParts = git.Split('|');
        CredentialRequest projectPackageRequest = CreatePackageRequest(
            CredentialEcosystem.NuGet,
            CredentialKind.NuGetPluginCredential
        ) with
        {
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org/proj/_packaging/feed/nuget/v3/index.json"),
                project: "proj",
                feed: "feed"
            ),
        };
        string package = CacheKeySchema
            .Create(projectPackageRequest, "user@example.com", "tenant-1")
            .Value;
        string[] packageParts = package.Split('|');

        CacheKey WithPart(string[] parts, int index, string value)
        {
            string[] copy = (string[])parts.Clone();
            copy[index] = EncodeCacheKeyPart(value);
            return new CacheKey { Value = string.Join('|', copy) };
        }

        return
        [
            WithPart(gitParts, 3, "org/other"),
            WithPart(gitParts, 3, @"org\other"),
            WithPart(packageParts, 4, "proj/other"),
            WithPart(packageParts, 4, @"proj\other"),
            WithPart(packageParts, 5, "feed/other"),
            WithPart(packageParts, 5, @"feed\other"),
            WithPart(gitParts, 6, "repo/other"),
            WithPart(gitParts, 6, @"repo\other"),
        ];
    }

    public static TheoryData<CacheKey> CacheKeyResourcePartitionsWithControlCharacters()
    {
        string git = CacheKeySchema
            .Create(
                CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword),
                "user@example.com",
                "tenant-1"
            )
            .Value;
        string[] gitParts = git.Split('|');
        CredentialRequest projectPackageRequest = CreatePackageRequest(
            CredentialEcosystem.NuGet,
            CredentialKind.NuGetPluginCredential
        ) with
        {
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org/proj/_packaging/feed/nuget/v3/index.json"),
                project: "proj",
                feed: "feed"
            ),
        };
        string package = CacheKeySchema
            .Create(projectPackageRequest, "user@example.com", "tenant-1")
            .Value;
        string[] packageParts = package.Split('|');

        CacheKey WithPart(string[] parts, int index, string value)
        {
            string[] copy = (string[])parts.Clone();
            copy[index] = EncodeCacheKeyPart(value);
            return new CacheKey { Value = string.Join('|', copy) };
        }

        return
        [
            WithPart(gitParts, 3, "org\nother"),
            WithPart(gitParts, 3, "org\u001Bother"),
            WithPart(packageParts, 4, "proj\rother"),
            WithPart(packageParts, 4, "proj\u009Fother"),
            WithPart(packageParts, 5, "feed\tother"),
            WithPart(packageParts, 5, "feed\u0085other"),
            WithPart(packageParts, 5, "feed\u007Fother"),
        ];
    }

    public static TheoryData<CacheKey> CacheKeyAccountAndTenantPartitionsWithControlCharacters()
    {
        string git = CacheKeySchema
            .Create(
                CreateRequest(IdentityFlow.InteractiveBrowser, CredentialKind.BasicPassword),
                "user@example.com",
                "tenant-1"
            )
            .Value;
        string[] gitParts = git.Split('|');

        CacheKey WithPart(int index, string value)
        {
            string[] copy = (string[])gitParts.Clone();
            copy[index] = EncodeCacheKeyPart(value);
            return new CacheKey { Value = string.Join('|', copy) };
        }

        return [WithPart(8, "user\u0001@contoso.com"), WithPart(9, "tenant\u0085one")];
    }

    public static TheoryData<string, CredentialRequest> PaddedCanonicalResourceIdentityFields()
    {
        CredentialRequest gitRequest = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        );
        CredentialRequest packageRequest = CreatePackageRequest(
            CredentialEcosystem.NuGet,
            CredentialKind.NuGetPluginCredential
        );

        return new TheoryData<string, CredentialRequest>
        {
            {
                "host",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { AzureDevOpsHost = " dev.azure.com " },
                }
            },
            {
                "organization",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Organization = " org " },
                }
            },
            {
                "project",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Project = " proj " },
                }
            },
            {
                "repository",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Repository = " repo " },
                }
            },
            {
                "feed",
                packageRequest with
                {
                    Resource = packageRequest.Resource with { Feed = " feed " },
                }
            },
        };
    }

    public static TheoryData<
        string,
        CredentialRequest
    > CanonicalResourceIdentityFieldsWithControlCharacters()
    {
        CredentialRequest gitRequest = CreateRequest(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword
        );
        CredentialRequest packageRequest = CreatePackageRequest(
            CredentialEcosystem.NuGet,
            CredentialKind.NuGetPluginCredential
        );

        return new TheoryData<string, CredentialRequest>
        {
            {
                "organization",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Organization = "or\ng" },
                }
            },
            {
                "organization-esc",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Organization = "or\u001Bg" },
                }
            },
            {
                "project",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Project = "pr\roj" },
                }
            },
            {
                "project-c1",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Project = "pr\u009Foj" },
                }
            },
            {
                "repository",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Repository = "re\ttpo" },
                }
            },
            {
                "repository-del",
                gitRequest with
                {
                    Resource = gitRequest.Resource with { Repository = "re\u007Fpo" },
                }
            },
            {
                "feed",
                packageRequest with
                {
                    Resource = packageRequest.Resource with { Feed = "fe\ted" },
                }
            },
            {
                "feed-c1",
                packageRequest with
                {
                    Resource = packageRequest.Resource with { Feed = "fe\u0085ed" },
                }
            },
        };
    }

    // ---- V5-B: Versioned acquisition contract v2 tests ----

    [Fact]
    public void CredentialRequestV1PublicSurfaceAndWireShapeRemainFrozen()
    {
        JsonSerializerOptions options = ContractJson.CreateSerializerOptions();
        CredentialRequest request = CreateRequest(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword
        );
        string json = JsonSerializer.Serialize(request, options);

        Assert.Null(
            typeof(CredentialRequest).GetProperty(nameof(CredentialRequestV2.AcquisitionMode))
        );
        Assert.Equal(CreateExpectedCredentialRequestV1Json(), json);
    }

    [Fact]
    public void CredentialRequestV1JsonRoundTripsFuturePersistentCacheStateWithoutMvpAcceptance()
    {
        JsonSerializerOptions options = ContractJson.CreateSerializerOptions();
        CredentialRequest request = CreateRequest(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword
        ) with
        {
            CachePolicy = CachePolicyMode.FuturePersistentCacheRequested,
        };
        string json = JsonSerializer.Serialize(request, options);
        CredentialRequest roundTripped = Assert.IsType<CredentialRequest>(
            JsonSerializer.Deserialize<CredentialRequest>(json, options)
        );

        Assert.Contains(
            "\"cachePolicy\":\"futurePersistentCacheRequested\"",
            json,
            StringComparison.Ordinal
        );
        Assert.Equal(CachePolicyMode.FuturePersistentCacheRequested, roundTripped.CachePolicy);
        Assert.False(IdentityFlowPolicy.IsAcceptedMvpRequest(roundTripped));
    }

    [Fact]
    public void CredentialRequestV2CarriesDedicatedVersionConstants()
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.Unspecified
        );

        Assert.Equal(ContractVersions.CredentialContractV2Major, request.ContractMajor);
        Assert.Equal(
            "azureauth-credprovider-credential-contract-v2",
            ContractVersions.CredentialContractV2Id
        );
        Assert.False(typeof(CredentialRequest).IsAssignableFrom(typeof(CredentialRequestV2)));
        Assert.NotNull(
            typeof(CredentialRequestV2).GetProperty(nameof(CredentialRequestV2.AcquisitionMode))
        );
    }

    [Fact]
    public void CredentialRequestV2JsonPublicSurfaceExposesOnlyStrictFacadeMethods()
    {
        string[] publicMethodNames = typeof(CredentialRequestV2Json)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(static method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Deserialize", "Serialize"], publicMethodNames);
        Assert.Empty(
            typeof(CredentialRequestV2Json).GetProperties(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
            )
        );
        Assert.Null(typeof(CredentialRequestV2Json).GetMethod("CreateSerializerOptions"));
        Assert.Null(typeof(CredentialRequestV2Json).GetProperty("Default"));
    }

    [Fact]
    public void CredentialRequestV2JsonSerializeUsesExactStrictEnumWireValues()
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed
        );
        string json = CredentialRequestV2Json.Serialize(request);

        Assert.Equal(CreateExpectedCredentialRequestV2Json(), json);
    }

    [Fact]
    public void CredentialRequestV2UsesOrdinaryContractJsonSerialization()
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed
        );
        JsonSerializerOptions options = ContractJson.CreateSerializerOptions();

        string json = JsonSerializer.Serialize(request, options);
        CredentialRequestV2 roundTripped = Assert.IsType<CredentialRequestV2>(
            JsonSerializer.Deserialize<CredentialRequestV2>(json, options)
        );

        Assert.Equal(CreateExpectedCredentialRequestV2Json(), json);
        Assert.Equal(json, JsonSerializer.Serialize(roundTripped, options));
        Assert.Null(CredentialRequestV2Policy.GetViolation(roundTripped));
    }

    [Theory]
    [InlineData(IdentityFlow.InteractiveBrowser, InteractivePolicy.HostToolAllows)]
    [InlineData(IdentityFlow.DeviceCode, InteractivePolicy.UserAllowed)]
    public void CredentialRequestV2JsonRoundTripsValidInteractionAllowedFlowsWithoutReflection(
        IdentityFlow flow,
        InteractivePolicy interactivePolicy
    )
    {
        CredentialRequestV2 request = CreateRequestV2(
            flow,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed,
            interactivePolicy: interactivePolicy
        );
        string json = CredentialRequestV2Json.Serialize(request);
        CredentialRequestV2 roundTripped = CredentialRequestV2Json.Deserialize(json);

        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);
        Assert.Equal(json, CredentialRequestV2Json.Serialize(roundTripped));
        Assert.Equal(ContractVersions.CredentialContractV2Major, roundTripped.ContractMajor);
        Assert.Equal(AcquisitionMode.InteractionAllowed, roundTripped.AcquisitionMode);
        Assert.Equal(flow, roundTripped.IdentityFlow);
        Assert.Equal(interactivePolicy, roundTripped.InteractivePolicy);
    }

    [Fact]
    public void CredentialRequestV2PolicyAllowsInteractionAllowedWithFrozenFutureCacheState()
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed,
            cachePolicy: CachePolicyMode.FuturePersistentCacheRequested
        );

        Assert.True(CredentialRequestV2Policy.IsValid(request));
        Assert.Null(CredentialRequestV2Policy.GetViolation(request));
    }

    [Fact]
    public void CredentialRequestV2JsonRoundTripsInteractionAllowedWithFrozenFutureCacheState()
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed,
            cachePolicy: CachePolicyMode.FuturePersistentCacheRequested
        );
        string json = CredentialRequestV2Json.Serialize(request);
        CredentialRequestV2 roundTripped = CredentialRequestV2Json.Deserialize(json);

        Assert.Contains(
            "\"cachePolicy\":\"futurePersistentCacheRequested\"",
            json,
            StringComparison.Ordinal
        );
        Assert.Equal(AcquisitionMode.InteractionAllowed, roundTripped.AcquisitionMode);
        Assert.Equal(CachePolicyMode.FuturePersistentCacheRequested, roundTripped.CachePolicy);
        Assert.Equal(json, CredentialRequestV2Json.Serialize(roundTripped));
        Assert.Null(CredentialRequestV2Policy.GetViolation(roundTripped));
    }

    [Fact]
    public void CredentialRequestV2PolicyRejectsUnspecifiedAcquisitionMode()
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.InteractiveBrowser,
            CredentialKind.BasicPassword,
            AcquisitionMode.Unspecified,
            interactivePolicy: InteractivePolicy.Never
        );

        Assert.False(CredentialRequestV2Policy.IsValid(request));
        Assert.Contains(
            "must specify an acquisition mode",
            CredentialRequestV2Policy.GetViolation(request),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void CredentialRequestV2JsonRejectsUnspecifiedAcquisitionMode()
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.Unspecified,
            cachePolicy: CachePolicyMode.FuturePersistentCacheRequested
        );

        Assert.Throws<ArgumentException>(() => CredentialRequestV2Json.Serialize(request));
    }

    public static TheoryData<CachePolicyMode> InvalidInteractionAllowedCachePolicyCases =>
        new() { { CachePolicyMode.Unspecified }, { (CachePolicyMode)999 } };

    [Theory]
    [MemberData(nameof(InvalidInteractionAllowedCachePolicyCases))]
    public void CredentialRequestV2PolicyRejectsInteractionAllowedWithInvalidCachePolicies(
        CachePolicyMode cachePolicy
    )
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed,
            cachePolicy: cachePolicy
        );
        string? violation = CredentialRequestV2Policy.GetViolation(request);

        Assert.False(CredentialRequestV2Policy.IsValid(request));
        Assert.NotNull(violation);
        Assert.Contains("frozen v1 request shape", violation, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidInteractionAllowedCachePolicyCases))]
    public void CredentialRequestV2JsonSerializeRejectsInteractionAllowedWithInvalidCachePolicies(
        CachePolicyMode cachePolicy
    )
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed,
            cachePolicy: cachePolicy
        );

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Serialize(request)
        );
        Assert.Contains("frozen v1 request shape", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialRequestV2JsonDeserializeRejectsInteractionAllowedWithUnspecifiedCache()
    {
        string json = CreateCredentialRequestV2JsonWithOverrides(
            cachePolicyJsonLiteral: SerializeEnumWireLiteral(CachePolicyMode.Unspecified)
        );

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Deserialize(json)
        );
        Assert.Contains("frozen v1 request shape", ex.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string?, string?> InvalidCredentialRequestV2HintCases =>
        new()
        {
            { "user\u001B@example.com", null },
            { "user\u009F@example.com", null },
            { null, "tenant\u001Bhint" },
            { null, "tenant\u009Fhint" },
        };

    [Theory]
    [MemberData(nameof(InvalidCredentialRequestV2HintCases))]
    public void CredentialRequestV2PolicyRejectsControlCharactersInAccountAndTenantHints(
        string? accountHint,
        string? tenantHint
    )
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed,
            accountHint: accountHint,
            tenantHint: tenantHint
        );
        string? violation = CredentialRequestV2Policy.GetViolation(request);

        Assert.False(CredentialRequestV2Policy.IsValid(request));
        Assert.NotNull(violation);
        Assert.Contains("control characters", violation, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidCredentialRequestV2HintCases))]
    public void CredentialRequestV2JsonSerializeRejectsControlCharactersInAccountAndTenantHints(
        string? accountHint,
        string? tenantHint
    )
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed,
            accountHint: accountHint,
            tenantHint: tenantHint
        );

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Serialize(request)
        );
        Assert.Contains("control characters", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidCredentialRequestV2HintCases))]
    public void CredentialRequestV2JsonDeserializeRejectsControlCharactersInAccountAndTenantHints(
        string? accountHint,
        string? tenantHint
    )
    {
        string json = CreateCredentialRequestV2JsonWithOverrides(accountHint, tenantHint);

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Deserialize(json)
        );
        Assert.Contains("control characters", ex.Message, StringComparison.Ordinal);
    }

    public static TheoryData<
        CredentialOperation,
        AcquisitionMode
    > NonGetOperationNonDefaultAcquisitionModeCases =>
        new()
        {
            { CredentialOperation.Store, AcquisitionMode.InteractionAllowed },
            { CredentialOperation.Store, AcquisitionMode.SilentOnly },
            { CredentialOperation.Erase, AcquisitionMode.InteractionAllowed },
            { CredentialOperation.Erase, AcquisitionMode.SilentOnly },
            { CredentialOperation.Refresh, AcquisitionMode.InteractionAllowed },
            { CredentialOperation.Refresh, AcquisitionMode.SilentOnly },
            { CredentialOperation.Configure, AcquisitionMode.InteractionAllowed },
            { CredentialOperation.Configure, AcquisitionMode.SilentOnly },
            { CredentialOperation.Doctor, AcquisitionMode.InteractionAllowed },
            { CredentialOperation.Doctor, AcquisitionMode.SilentOnly },
        };

    [Theory]
    [MemberData(nameof(NonGetOperationNonDefaultAcquisitionModeCases))]
    public void CredentialRequestV2PolicyRejectsNonDefaultAcquisitionModesForNonGetOperations(
        CredentialOperation operation,
        AcquisitionMode acquisitionMode
    )
    {
        InteractivePolicy interactivePolicy =
            acquisitionMode == AcquisitionMode.SilentOnly
                ? InteractivePolicy.Never
                : InteractivePolicy.UserAllowed;
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            acquisitionMode,
            interactivePolicy: interactivePolicy
        ) with
        {
            Operation = operation,
        };
        string? violation = CredentialRequestV2Policy.GetViolation(request);

        Assert.False(CredentialRequestV2Policy.IsValid(request));
        Assert.NotNull(violation);
        Assert.Contains("operation get", violation, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => CredentialRequestV2Policy.EnsureValid(request));
    }

    public static TheoryData<CredentialRequestV2> ValidSilentOnlyFlowCases =>
        new()
        {
            {
                CreateRequestV2(
                    IdentityFlow.InteractiveBrowser,
                    CredentialKind.BasicPassword,
                    AcquisitionMode.SilentOnly,
                    interactivePolicy: InteractivePolicy.Never
                )
            },
            {
                CreateRequestV2(
                    IdentityFlow.DeviceCode,
                    CredentialKind.BasicPassword,
                    AcquisitionMode.SilentOnly,
                    interactivePolicy: InteractivePolicy.Never
                )
            },
            {
                CreateRequestV2(
                    IdentityFlow.PatCompatibility,
                    CredentialKind.PatCompatibility,
                    AcquisitionMode.SilentOnly,
                    interactivePolicy: InteractivePolicy.Never
                )
            },
            {
                CreateRequestV2(
                    IdentityFlow.DeviceCode,
                    CredentialKind.BasicPassword,
                    AcquisitionMode.SilentOnly,
                    interactivePolicy: InteractivePolicy.Never,
                    cachePolicy: CachePolicyMode.FuturePersistentCacheRequested
                )
            },
            { CreateAzurePipelinesSystemAccessTokenRequestV2(AcquisitionMode.SilentOnly) },
        };

    [Theory]
    [MemberData(nameof(ValidSilentOnlyFlowCases))]
    public void CredentialRequestV2PolicyAllowsStructurallyValidSilentOnlyRequests(
        CredentialRequestV2 request
    )
    {
        Assert.True(CredentialRequestV2Policy.IsValid(request));
        Assert.Null(CredentialRequestV2Policy.GetViolation(request));
        CredentialRequestV2Policy.EnsureValid(request);
    }

    public static TheoryData<CredentialRequestV2, string> InvalidSilentOnlyCases =>
        new()
        {
            {
                CreateRequestV2(
                    IdentityFlow.InteractiveBrowser,
                    CredentialKind.BasicPassword,
                    AcquisitionMode.SilentOnly,
                    interactivePolicy: InteractivePolicy.UserAllowed
                ),
                "Protocol violation: SilentOnly requires interactivePolicy never."
            },
            {
                CreateRequestV2(
                    IdentityFlow.DeviceCode,
                    CredentialKind.BasicPassword,
                    AcquisitionMode.SilentOnly,
                    interactivePolicy: InteractivePolicy.Never,
                    ciContext: new CiContext
                    {
                        ExplicitCiMode = true,
                        AllowsPersistentWrites = false,
                    }
                ),
                "Protocol violation: SilentOnly Azure Pipelines opaque-token requests must "
                    + "satisfy the explicit CI policy."
            },
            {
                CreateAzurePipelinesSystemAccessTokenRequestV2(AcquisitionMode.SilentOnly) with
                {
                    CiContext = new CiContext
                    {
                        ExplicitCiMode = false,
                        AllowsPersistentWrites = false,
                    },
                },
                "Protocol violation: SilentOnly Azure Pipelines opaque-token requests must "
                    + "satisfy the explicit CI policy."
            },
            {
                CreateRequestV2(
                    IdentityFlow.DeviceCode,
                    CredentialKind.BasicPassword,
                    AcquisitionMode.SilentOnly,
                    interactivePolicy: InteractivePolicy.Never,
                    cachePolicy: (CachePolicyMode)999
                ),
                "Protocol violation: credential request v2 must preserve "
                    + "the frozen v1 request shape."
            },
        };

    [Theory]
    [MemberData(nameof(InvalidSilentOnlyCases))]
    public void CredentialRequestV2PolicyRejectsInvalidSilentOnlyCombinations(
        CredentialRequestV2 request,
        string expectedViolation
    )
    {
        Assert.False(CredentialRequestV2Policy.IsValid(request));
        Assert.Equal(expectedViolation, CredentialRequestV2Policy.GetViolation(request));
        Assert.Throws<ArgumentException>(() => CredentialRequestV2Policy.EnsureValid(request));
    }

    public static TheoryData<CredentialEcosystem> SupportedAzurePipelinesOpaqueTokenEcosystems =>
        new()
        {
            CredentialEcosystem.Git,
            CredentialEcosystem.Npm,
            CredentialEcosystem.Pnpm,
            CredentialEcosystem.Yarn,
        };

    [Theory]
    [MemberData(nameof(SupportedAzurePipelinesOpaqueTokenEcosystems))]
    public void CredentialRequestV2PolicyAllowsExplicitSilentOnlyAzurePipelinesOpaqueTokens(
        CredentialEcosystem ecosystem
    )
    {
        CredentialRequestV2 request = CreateAzurePipelinesSystemAccessTokenRequestV2(
            AcquisitionMode.SilentOnly,
            ecosystem
        );

        Assert.Null(CredentialRequestV2Policy.GetViolation(request));
        string json = CredentialRequestV2Json.Serialize(request);
        CredentialRequestV2 roundTripped = CredentialRequestV2Json.Deserialize(json);
        Assert.Equal(AcquisitionMode.SilentOnly, roundTripped.AcquisitionMode);
        Assert.Equal(ecosystem, roundTripped.Ecosystem);
    }

    [Theory]
    [InlineData(AcquisitionMode.Unspecified)]
    [InlineData(AcquisitionMode.InteractionAllowed)]
    public void CredentialRequestV2PolicyRejectsOtherModesForAzurePipelinesOpaqueTokens(
        AcquisitionMode acquisitionMode
    )
    {
        CredentialRequestV2 request = CreateAzurePipelinesSystemAccessTokenRequestV2(
            acquisitionMode
        );

        Assert.False(CredentialRequestV2Policy.IsValid(request));
        Assert.Throws<ArgumentException>(() => CredentialRequestV2Policy.EnsureValid(request));
    }

    public static TheoryData<CredentialRequestV2> InvalidAzurePipelinesOpaqueTokenCases =>
        new()
        {
            CreateAzurePipelinesSystemAccessTokenRequestV2(AcquisitionMode.SilentOnly) with
            {
                AccountHint = "user@example.com",
            },
            CreateAzurePipelinesSystemAccessTokenRequestV2(AcquisitionMode.SilentOnly) with
            {
                TenantHint = "tenant-1",
            },
            CreateAzurePipelinesSystemAccessTokenRequestV2(AcquisitionMode.SilentOnly) with
            {
                CachePolicy = CachePolicyMode.NoCache,
            },
            CreateAzurePipelinesSystemAccessTokenRequestV2(AcquisitionMode.SilentOnly) with
            {
                CiContext = new CiContext
                {
                    ExplicitCiMode = true,
                    Provider = "Other",
                    HasAzurePipelinesSystemAccessToken = true,
                    AllowsPersistentWrites = false,
                },
            },
            CreateAzurePipelinesSystemAccessTokenRequestV2(AcquisitionMode.SilentOnly) with
            {
                CiContext = new CiContext
                {
                    ExplicitCiMode = true,
                    Provider = CiProviderNames.AzurePipelines,
                    HasAzurePipelinesSystemAccessToken = false,
                    AllowsPersistentWrites = false,
                },
            },
            CreateAzurePipelinesSystemAccessTokenRequestV2(AcquisitionMode.SilentOnly) with
            {
                CiContext = new CiContext
                {
                    ExplicitCiMode = true,
                    Provider = CiProviderNames.AzurePipelines,
                    HasAzurePipelinesSystemAccessToken = true,
                    AllowsPersistentWrites = true,
                },
            },
            CreateAzurePipelinesSystemAccessTokenRequestV2(
                AcquisitionMode.SilentOnly,
                CredentialEcosystem.NuGet
            ),
            CreateAzurePipelinesSystemAccessTokenRequestV2(
                AcquisitionMode.SilentOnly,
                CredentialEcosystem.Python
            ),
        };

    [Theory]
    [MemberData(nameof(InvalidAzurePipelinesOpaqueTokenCases))]
    public void CredentialRequestV2PolicyRejectsInvalidAzurePipelinesOpaqueTokenPolicy(
        CredentialRequestV2 request
    )
    {
        Assert.Equal(
            "Protocol violation: SilentOnly Azure Pipelines opaque-token requests must "
                + "satisfy the explicit CI policy.",
            CredentialRequestV2Policy.GetViolation(request)
        );
    }

    [Theory]
    [InlineData(IdentityFlow.InteractiveBrowser, InteractivePolicy.HostToolAllows)]
    [InlineData(IdentityFlow.DeviceCode, InteractivePolicy.UserAllowed)]
    public void CredentialRequestV2PolicyAllowsInteractionAllowedOnlyForExplicitHumanFlows(
        IdentityFlow flow,
        InteractivePolicy interactivePolicy
    )
    {
        CredentialRequestV2 request = CreateRequestV2(
            flow,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed,
            interactivePolicy: interactivePolicy
        );

        Assert.True(CredentialRequestV2Policy.IsValid(request));
        Assert.Null(CredentialRequestV2Policy.GetViolation(request));
    }

    public static TheoryData<CredentialRequestV2> InvalidInteractionAllowedCases =>
        new()
        {
            {
                CreateRequestV2(
                    IdentityFlow.PatCompatibility,
                    CredentialKind.PatCompatibility,
                    AcquisitionMode.InteractionAllowed,
                    interactivePolicy: InteractivePolicy.UserAllowed
                )
            },
            {
                CreateRequestV2(
                    IdentityFlow.InteractiveBrowser,
                    CredentialKind.BasicPassword,
                    AcquisitionMode.InteractionAllowed,
                    interactivePolicy: InteractivePolicy.Never
                )
            },
            {
                CreateRequestV2(
                    IdentityFlow.DeviceCode,
                    CredentialKind.BasicPassword,
                    AcquisitionMode.InteractionAllowed,
                    ciContext: new CiContext
                    {
                        ExplicitCiMode = true,
                        AllowsPersistentWrites = false,
                    }
                )
            },
        };

    [Theory]
    [MemberData(nameof(InvalidInteractionAllowedCases))]
    public void CredentialRequestV2PolicyRejectsInteractionAllowedOutsideExplicitHumanFlows(
        CredentialRequestV2 request
    )
    {
        string? violation = CredentialRequestV2Policy.GetViolation(request);

        Assert.False(CredentialRequestV2Policy.IsValid(request));
        Assert.NotNull(violation);
        Assert.Contains("InteractionAllowed", violation, StringComparison.Ordinal);
    }

    public static TheoryData<string> InvalidInteractionAllowedJsonCases =>
        new()
        {
            {
                ReplaceInCredentialRequestV2Json(
                    (
                        "\"credentialKind\":\"basicPassword\"",
                        "\"credentialKind\":\"patCompatibility\""
                    ),
                    ("\"identityFlow\":\"deviceCode\"", "\"identityFlow\":\"patCompatibility\"")
                )
            },
            {
                ReplaceInCredentialRequestV2Json(
                    (
                        "\"ciContext\":{\"explicitCiMode\":false,\"provider\":null,"
                            + "\"hasAzurePipelinesSystemAccessToken\":false,"
                            + "\"allowsPersistentWrites\":false}",
                        "\"ciContext\":{\"explicitCiMode\":true,"
                            + "\"provider\":\"AzurePipelines\","
                            + "\"hasAzurePipelinesSystemAccessToken\":true,"
                            + "\"allowsPersistentWrites\":false}"
                    )
                )
            },
        };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(999)]
    public void CredentialRequestV2JsonSerializeRejectsUnsupportedContractMajor(int contractMajor)
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.InteractionAllowed
        ) with
        {
            ContractMajor = contractMajor,
        };

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Serialize(request)
        );
        Assert.Contains("contract major must be 2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialRequestV2JsonRoundTripsValidSilentOnly()
    {
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            AcquisitionMode.SilentOnly,
            interactivePolicy: InteractivePolicy.Never
        );

        string json = CredentialRequestV2Json.Serialize(request);
        CredentialRequestV2 roundTripped = CredentialRequestV2Json.Deserialize(json);

        Assert.Contains("\"acquisitionMode\":\"silentOnly\"", json, StringComparison.Ordinal);
        Assert.Equal(AcquisitionMode.SilentOnly, roundTripped.AcquisitionMode);
        Assert.Equal(InteractivePolicy.Never, roundTripped.InteractivePolicy);
        Assert.Null(CredentialRequestV2Policy.GetViolation(roundTripped));
    }

    [Theory]
    [MemberData(nameof(InvalidInteractionAllowedCases))]
    public void CredentialRequestV2JsonSerializeRejectsInvalidInteractionAllowed(
        CredentialRequestV2 request
    )
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Serialize(request)
        );
        Assert.Contains("InteractionAllowed", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NonGetOperationNonDefaultAcquisitionModeCases))]
    public void CredentialRequestV2JsonSerializeRejectsNonDefaultModesForNonGetOperations(
        CredentialOperation operation,
        AcquisitionMode acquisitionMode
    )
    {
        InteractivePolicy interactivePolicy =
            acquisitionMode == AcquisitionMode.SilentOnly
                ? InteractivePolicy.Never
                : InteractivePolicy.UserAllowed;
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.DeviceCode,
            CredentialKind.BasicPassword,
            acquisitionMode,
            interactivePolicy: interactivePolicy
        ) with
        {
            Operation = operation,
        };

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Serialize(request)
        );
        Assert.Contains("operation get", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CredentialRequestV2JsonRequiresContractMajor()
    {
        string json = CreateCredentialRequestV2Json()
            .Replace("\"contractMajor\":2,", string.Empty, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => CredentialRequestV2Json.Deserialize(json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(999)]
    public void CredentialRequestV2JsonRejectsUnsupportedContractMajor(int contractMajor)
    {
        string json = CreateCredentialRequestV2Json()
            .Replace(
                "\"contractMajor\":2",
                $"\"contractMajor\":{contractMajor}",
                StringComparison.Ordinal
            );

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Deserialize(json)
        );
        Assert.Contains("contract major must be 2", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("1")]
    [InlineData("999")]
    public void CredentialRequestV2JsonRejectsQuotedContractMajor(string contractMajor)
    {
        string json = CreateCredentialRequestV2Json()
            .Replace(
                "\"contractMajor\":2",
                $"\"contractMajor\":\"{contractMajor}\"",
                StringComparison.Ordinal
            );

        Assert.Throws<JsonException>(() => CredentialRequestV2Json.Deserialize(json));
    }

    [Fact]
    public void CredentialRequestV2JsonRoundTripsNumericContractMajor()
    {
        string json = CreateCredentialRequestV2Json();
        CredentialRequestV2 roundTripped = CredentialRequestV2Json.Deserialize(json);

        Assert.Equal(ContractVersions.CredentialContractV2Major, roundTripped.ContractMajor);
        Assert.Equal(json, CredentialRequestV2Json.Serialize(roundTripped));
    }

    [Fact]
    public void CredentialRequestV2JsonDeserializeAcceptsValidSilentOnly()
    {
        string json = ReplaceInCredentialRequestV2Json(
            ("\"interactivePolicy\":\"userAllowed\"", "\"interactivePolicy\":\"never\""),
            ("\"acquisitionMode\":\"interactionAllowed\"", "\"acquisitionMode\":\"silentOnly\"")
        );

        CredentialRequestV2 request = CredentialRequestV2Json.Deserialize(json);

        Assert.Equal(AcquisitionMode.SilentOnly, request.AcquisitionMode);
        Assert.Equal(InteractivePolicy.Never, request.InteractivePolicy);
        Assert.Null(CredentialRequestV2Policy.GetViolation(request));
    }

    [Theory]
    [MemberData(nameof(InvalidInteractionAllowedJsonCases))]
    public void CredentialRequestV2JsonDeserializeRejectsInvalidInteractionAllowed(string json)
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Deserialize(json)
        );
        Assert.Contains("InteractionAllowed", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NonGetOperationNonDefaultAcquisitionModeCases))]
    public void CredentialRequestV2JsonDeserializeRejectsNonDefaultModesForNonGetOperations(
        CredentialOperation operation,
        AcquisitionMode acquisitionMode
    )
    {
        string json = ReplaceInCredentialRequestV2Json(
            ("\"operation\":\"get\"", $"\"operation\":{SerializeEnumWireLiteral(operation)}"),
            (
                "\"acquisitionMode\":\"interactionAllowed\"",
                $"\"acquisitionMode\":{SerializeEnumWireLiteral(acquisitionMode)}"
            )
        );

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CredentialRequestV2Json.Deserialize(json)
        );
        Assert.Contains("operation get", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CredentialRequestV2JsonRequiresAcquisitionMode()
    {
        string json = CreateCredentialRequestV2Json()
            .Replace(
                ",\"acquisitionMode\":\"interactionAllowed\"",
                string.Empty,
                StringComparison.Ordinal
            );

        Assert.Throws<JsonException>(() => CredentialRequestV2Json.Deserialize(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\r\n\t")]
    public void CredentialRequestV2JsonRejectsEmptyOrWhitespacePayloadAsMalformedJson(string json)
    {
        Assert.Throws<JsonException>(() => CredentialRequestV2Json.Deserialize(json));
    }

    [Fact]
    public void CredentialRequestV2JsonRejectsNullPayload()
    {
        Assert.Throws<ArgumentNullException>(() => CredentialRequestV2Json.Deserialize(null!));
    }

    [Fact]
    public void CredentialRequestV2JsonUsesOrdinaryFrameworkPropertyHandling()
    {
        string json = CreateCredentialRequestV2Json()
            .Replace(
                "\"acquisitionMode\":\"interactionAllowed\"",
                "\"AcquisitionMode\":\"interactionAllowed\"",
                StringComparison.Ordinal
            )
            .Replace(
                "\"extensionData\":{}",
                "\"extensionData\":{},\"futurePolicy\":true",
                StringComparison.Ordinal
            );

        CredentialRequestV2 request = CredentialRequestV2Json.Deserialize(json);

        Assert.Equal(AcquisitionMode.InteractionAllowed, request.AcquisitionMode);
    }

    [Theory]
    [InlineData("\"interactionAllowed\"", "\"futureMode\"")]
    [InlineData("\"interactionAllowed\"", "1")]
    public void CredentialRequestV2JsonRejectsUnknownAndNumericEnumValues(
        string currentValue,
        string replacementValue
    )
    {
        string json = CreateCredentialRequestV2Json()
            .Replace(currentValue, replacementValue, StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => CredentialRequestV2Json.Deserialize(json));
    }

    private static ConfigurationChange CreateConfigurationChange(
        ConfigurationChangeOperation operation
    ) =>
        new()
        {
            Operation = operation,
            TargetKind = ConfigurationTargetKind.Npmrc,
            TargetPathOrName = @"C:\Users\runneradmin\.npmrc",
            Key = "//pkgs.dev.azure.com/org/_packaging/feed/npm/registry/:_authToken",
            Value = "secret-token",
            RequiresOwnershipRecord = true,
            IsSecretValue = true,
        };

    private static ConfigurationChange CreateYarnAuthTokenChange(string registryUrl) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Create,
            TargetKind = ConfigurationTargetKind.Yarnrc,
            TargetPathOrName = "user .yarnrc.yml",
            Key = $"""npmRegistries["{registryUrl}"].npmAuthToken""",
            Value = "secret-token",
            RequiresOwnershipRecord = true,
            IsSecretValue = true,
        };

    private static ConfigurationChange CreateYarnAlwaysAuthChange(string registryUrl) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Create,
            TargetKind = ConfigurationTargetKind.Yarnrc,
            TargetPathOrName = "user .yarnrc.yml",
            Key = $"""npmRegistries["{registryUrl}"].npmAlwaysAuth""",
            Value = "true",
            RequiresOwnershipRecord = true,
            IsSecretValue = false,
        };

    private static string EncodeCacheKeyPart(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static CredentialResult CreateProtocolSuccessResult(
        AdapterProtocol protocol,
        CacheKey? cacheKey = null
    )
    {
        var result = new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            DiagnosticsCorrelationId = "corr-protocol-cache-key",
            CacheKey = cacheKey,
        };

        return protocol switch
        {
            AdapterProtocol.GitCredentialHelper
                when cacheKey is not null
                    && CacheKeySchema.GetCredentialKind(cacheKey) == CredentialKind.BearerToken =>
                result with
                {
                    BearerToken = "bearer-token",
                },
            AdapterProtocol.GitCredentialHelper or AdapterProtocol.NuGetPlugin => result with
            {
                Username = "AzureDevOps",
                Password = "generated-password",
            },
            AdapterProtocol.PythonKeyringBackend or AdapterProtocol.KeyringHelper => result with
            {
                Password = "generated-password",
            },
            AdapterProtocol.NpmConfiguration => result with { BearerToken = "bearer-token" },
            _ => result,
        };
    }

    private static CredentialRequestV2 CreateRequestV2(
        IdentityFlow flow,
        CredentialKind kind,
        AcquisitionMode acquisitionMode,
        InteractivePolicy interactivePolicy = InteractivePolicy.UserAllowed,
        CachePolicyMode cachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
        CiContext? ciContext = null,
        string? accountHint = null,
        string? tenantHint = null
    ) =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org")
            ),
            ServiceIdentity = "default",
            AccountHint = accountHint,
            TenantHint = tenantHint,
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = kind,
            IdentityFlow = flow,
            InteractivePolicy = interactivePolicy,
            AcquisitionMode = acquisitionMode,
            CachePolicy = cachePolicy,
            CiContext =
                ciContext
                ?? new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
        };

    private static CredentialRequestV2 CreateAzurePipelinesSystemAccessTokenRequestV2(
        AcquisitionMode acquisitionMode,
        CredentialEcosystem ecosystem = CredentialEcosystem.Git
    )
    {
        CredentialKind credentialKind = ecosystem switch
        {
            CredentialEcosystem.Git => CredentialKind.BearerToken,
            CredentialEcosystem.NuGet => CredentialKind.NuGetPluginCredential,
            CredentialEcosystem.Python => CredentialKind.BasicPassword,
            _ => CredentialKind.NpmAuthToken,
        };
        CredentialRequestV2 request = CreateRequestV2(
            IdentityFlow.AzurePipelinesSystemAccessToken,
            credentialKind,
            acquisitionMode,
            interactivePolicy: InteractivePolicy.Never,
            cachePolicy: CachePolicyMode.NonPersistentCi,
            ciContext: new CiContext
            {
                ExplicitCiMode = true,
                Provider = CiProviderNames.AzurePipelines,
                HasAzurePipelinesSystemAccessToken = true,
                AllowsPersistentWrites = false,
            }
        );
        if (ecosystem == CredentialEcosystem.Git)
        {
            return request;
        }

        CredentialRequest packageRequest = CreatePackageRequest(ecosystem, credentialKind);
        return request with
        {
            Ecosystem = ecosystem,
            Resource = packageRequest.Resource,
            RequestedAudience = packageRequest.RequestedAudience,
        };
    }

    private static string CreateCredentialRequestV2Json() =>
        CredentialRequestV2Json.Serialize(
            CreateRequestV2(
                IdentityFlow.DeviceCode,
                CredentialKind.BasicPassword,
                AcquisitionMode.InteractionAllowed
            )
        );

    private static string CreateCredentialRequestV2JsonWithOverrides(
        string? accountHint = null,
        string? tenantHint = null,
        string? cachePolicyJsonLiteral = null
    )
    {
        var replacements = new List<(string Current, string Replacement)>();

        if (accountHint is not null)
        {
            replacements.Add(
                (
                    "\"accountHint\":null",
                    $"\"accountHint\":{SerializeJsonStringLiteral(accountHint)}"
                )
            );
        }

        if (tenantHint is not null)
        {
            replacements.Add(
                ("\"tenantHint\":null", $"\"tenantHint\":{SerializeJsonStringLiteral(tenantHint)}")
            );
        }

        if (cachePolicyJsonLiteral is not null)
        {
            replacements.Add(
                (
                    "\"cachePolicy\":\"productPersistentCacheDisabled\"",
                    $"\"cachePolicy\":{cachePolicyJsonLiteral}"
                )
            );
        }

        return ReplaceInCredentialRequestV2Json(replacements.ToArray());
    }

    private static string ReplaceInCredentialRequestV2Json(
        params (string Current, string Replacement)[] replacements
    )
    {
        string json = CreateCredentialRequestV2Json();
        foreach ((string current, string replacement) in replacements)
        {
            json = json.Replace(current, replacement, StringComparison.Ordinal);
        }

        return json;
    }

    private static string SerializeEnumWireLiteral<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        JsonSerializer.Serialize(value, ContractJson.CreateSerializerOptions());

    private static string SerializeJsonStringLiteral(string value) =>
        $"\"{JsonEncodedText.Encode(value).ToString()}\"";

    private static CredentialRequest CreateRequest(IdentityFlow flow, CredentialKind kind) =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "org",
                new Uri("https://dev.azure.com/org")
            ),
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = kind,
            IdentityFlow = flow,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            ServiceIdentity = "default",
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
        };

    private static CredentialRequest CreatePackageRequest(
        CredentialEcosystem ecosystem,
        CredentialKind kind
    ) =>
        new()
        {
            Ecosystem = ecosystem,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "pkgs.dev.azure.com",
                "org",
                ecosystem switch
                {
                    CredentialEcosystem.NuGet => new Uri(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json"
                    ),
                    CredentialEcosystem.Python => new Uri(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/pypi/simple"
                    ),
                    _ => new Uri(
                        "https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry"
                    ),
                },
                feed: "feed"
            ),
            RequestedAudience = TokenAudience.AzureArtifacts,
            CredentialKind = kind,
            IdentityFlow = IdentityFlow.DeviceCode,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            ServiceIdentity = "default",
            CiContext = new CiContext { ExplicitCiMode = false, AllowsPersistentWrites = false },
        };

    private static CredentialRequest CreateProjectScopedPackageRequest(
        CredentialEcosystem ecosystem,
        CredentialKind kind
    )
    {
        bool npmCompatible =
            ecosystem
            is CredentialEcosystem.Npm
                or CredentialEcosystem.Pnpm
                or CredentialEcosystem.Yarn;
        string host = npmCompatible ? "pkgs.dev.azure.com" : "dev.azure.com";
        Uri endpoint = ecosystem switch
        {
            CredentialEcosystem.NuGet => new Uri(
                "https://dev.azure.com/org/project/_packaging/feed/nuget/v3/index.json"
            ),
            CredentialEcosystem.Python => new Uri(
                "https://dev.azure.com/org/project/_packaging/feed/pypi/simple"
            ),
            _ => new Uri(
                "https://pkgs.dev.azure.com/org/project/_packaging/feed/npm/registry"
            ),
        };

        return CreatePackageRequest(ecosystem, kind) with
        {
            Resource = CanonicalResourceIdentity.Create(
                host,
                "org",
                endpoint,
                project: "project",
                feed: "feed"
            ),
        };
    }

    private static string CreateCredentialRequestJson(
        string ecosystemProperty,
        bool includeContractMajor,
        bool includeServiceIdentity = true
    ) =>
        $$"""
            {
              {{(includeContractMajor ? "\"contractMajor\": 1," : string.Empty)}}
              {{ecosystemProperty}},
              "operation": "get",
              "resource": {
                "azureDevOpsHost": "dev.azure.com",
                "organization": "org",
                "serviceEndpoint": "https://dev.azure.com/org"
              },
              {{(includeServiceIdentity ? "\"serviceIdentity\": \"default\"," : string.Empty)}}
              "requestedAudience": "azureDevOps",
              "credentialKind": "basicPassword",
              "identityFlow": "deviceCode",
              "interactivePolicy": "userAllowed",
              "cachePolicy": "productPersistentCacheDisabled"
            }
            """;

    private static string CreateExpectedCredentialRequestV1Json() =>
        string.Concat(
            "{",
            "\"contractMajor\":1,",
            "\"ecosystem\":\"git\",",
            "\"operation\":\"get\",",
            "\"resource\":{",
            "\"azureDevOpsHost\":\"dev.azure.com\",",
            "\"organization\":\"org\",",
            "\"project\":null,",
            "\"feed\":null,",
            "\"repository\":null,",
            "\"serviceEndpoint\":\"https://dev.azure.com/org\"",
            "},",
            "\"serviceIdentity\":\"default\",",
            "\"accountHint\":null,",
            "\"tenantHint\":null,",
            "\"requestedAudience\":\"azureDevOps\",",
            "\"credentialKind\":\"basicPassword\",",
            "\"identityFlow\":\"deviceCode\",",
            "\"interactivePolicy\":\"userAllowed\",",
            "\"cachePolicy\":\"productPersistentCacheDisabled\",",
            "\"ciContext\":{",
            "\"explicitCiMode\":false,",
            "\"provider\":null,",
            "\"hasAzurePipelinesSystemAccessToken\":false,",
            "\"allowsPersistentWrites\":false",
            "},",
            "\"extensionData\":{}}"
        );

    private static string CreateExpectedCredentialRequestV2Json() =>
        string.Concat(
            "{",
            "\"contractMajor\":2,",
            "\"ecosystem\":\"git\",",
            "\"operation\":\"get\",",
            "\"resource\":{",
            "\"azureDevOpsHost\":\"dev.azure.com\",",
            "\"organization\":\"org\",",
            "\"project\":null,",
            "\"feed\":null,",
            "\"repository\":null,",
            "\"serviceEndpoint\":\"https://dev.azure.com/org\"",
            "},",
            "\"serviceIdentity\":\"default\",",
            "\"accountHint\":null,",
            "\"tenantHint\":null,",
            "\"requestedAudience\":\"azureDevOps\",",
            "\"credentialKind\":\"basicPassword\",",
            "\"identityFlow\":\"deviceCode\",",
            "\"interactivePolicy\":\"userAllowed\",",
            "\"acquisitionMode\":\"interactionAllowed\",",
            "\"cachePolicy\":\"productPersistentCacheDisabled\",",
            "\"ciContext\":{",
            "\"explicitCiMode\":false,",
            "\"provider\":null,",
            "\"hasAzurePipelinesSystemAccessToken\":false,",
            "\"allowsPersistentWrites\":false",
            "},",
            "\"extensionData\":{}}"
        );

    private static CredentialErrorKind ToErrorKind(CredentialResultStatus status) =>
        status switch
        {
            CredentialResultStatus.InteractionRequired => CredentialErrorKind.InteractionRequired,
            CredentialResultStatus.InteractionBlocked => CredentialErrorKind.InteractionBlocked,
            CredentialResultStatus.Unauthorized => CredentialErrorKind.Unauthorized,
            CredentialResultStatus.CredentialUnavailable =>
                CredentialErrorKind.CredentialUnavailable,
            CredentialResultStatus.FlowDeferred => CredentialErrorKind.FlowDeferred,
            CredentialResultStatus.FlowDisabled => CredentialErrorKind.FlowDisabled,
            CredentialResultStatus.UnsupportedFlow => CredentialErrorKind.UnsupportedFlow,
            CredentialResultStatus.CacheUnavailable => CredentialErrorKind.CacheUnavailable,
            CredentialResultStatus.Fatal => CredentialErrorKind.Fatal,
            CredentialResultStatus.IntegrityFailure => CredentialErrorKind.IntegrityFailure,
            CredentialResultStatus.ProtocolViolation => CredentialErrorKind.ProtocolViolation,
            _ => CredentialErrorKind.ProtocolViolation,
        };
}
