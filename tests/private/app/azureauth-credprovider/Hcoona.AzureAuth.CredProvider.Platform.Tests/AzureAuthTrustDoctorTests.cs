using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class AzureAuthTrustDoctorTests
{
    public static TheoryData<string, AzureAuthArtifactEvidence, string> TrustMismatchCases =>
        new()
        {
            {
                "path",
                CreateMatchingEvidence(CreateDeploymentConfig()) with { CanonicalPath = @"c:\Tools\AzureAuth.exe" },
                "canonical path mismatch"
            },
            {
                "digest",
                CreateMatchingEvidence(CreateDeploymentConfig()) with { Sha256Hash = new string('A', 64) },
                "digest mismatch"
            },
            {
                "signer",
                CreateMatchingEvidence(CreateDeploymentConfig()) with
                {
                    SignerIdentity = "CN=AzureAuth, O=Hcoona Labs, C=US",
                },
                "signer mismatch"
            },
            {
                "publisher",
                CreateMatchingEvidence(CreateDeploymentConfig()) with
                {
                    PublisherName = "Hcoona AzureAuth ",
                },
                "publisher mismatch"
            },
            {
                "version",
                CreateMatchingEvidence(CreateDeploymentConfig()) with
                {
                    ExecutableVersion = "1.0-beta",
                },
                "version mismatch"
            },
            {
                "provenance",
                CreateMatchingEvidence(CreateDeploymentConfig()) with
                {
                    ProvenanceIdentifier = "Foundation/wp2",
                },
                "provenance mismatch"
            },
            {
                "owner",
                CreateMatchingEvidence(CreateDeploymentConfig()) with { CurrentUserOwnsArtifact = false },
                "artifact owner check failed"
            },
            {
                "writable",
                CreateMatchingEvidence(CreateDeploymentConfig()) with { OwnerOnlyWritable = false },
                "artifact is not owner-only writable"
            },
        };

    public static TheoryData<string, AzureAuthDeploymentConfig, string> DeploymentDriftCases =>
        new()
        {
            {
                "path",
                CreateDeploymentConfig() with { ExecutablePath = @"D:\Tools\AzureAuth.exe" },
                "canonical path mismatch"
            },
            {
                "digest",
                CreateDeploymentConfig() with { ExecutableSha256 = new string('b', 64) },
                "digest mismatch"
            },
            {
                "signer",
                CreateDeploymentConfig() with { SignerIdentity = "CN=AzureAuth, O=Contoso, C=US" },
                "signer mismatch"
            },
            {
                "publisher",
                CreateDeploymentConfig() with { PublisherName = "Hcoona AzureAuth Preview" },
                "publisher mismatch"
            },
            {
                "version",
                CreateDeploymentConfig() with { ExecutableVersion = "1.0.1.0" },
                "version mismatch"
            },
            {
                "provenance",
                CreateDeploymentConfig() with { ProvenanceIdentifier = "foundation/wp2b" },
                "provenance mismatch"
            },
        };

    [Fact]
    public void EvaluateReturnsTrustedOnlyForExactMatch()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        AzureAuthArtifactInspection inspection = AzureAuthArtifactInspection.Trusted(
            CreateMatchingEvidence(config.DeploymentConfig!)
        );

        AzureAuthTrustResult result = AzureAuthTrustPolicy.Evaluate(config.DeploymentConfig!, inspection);

        Assert.Equal(AzureAuthArtifactTrustStatus.Trusted, result.Status);
        Assert.True(result.IsReady);
        Assert.NotNull(result.Evidence);
        Assert.Matches("^[a-f0-9]{64}$", Assert.IsType<string>(result.DeploymentKey));
    }

    [Fact]
    public void EvaluateValidatesDeploymentConfigBeforeCallingInspector()
    {
        AzureAuthDeploymentConfig invalidConfig = CreateDeploymentConfig() with
        {
            ExecutablePath = @"C:/Tools/AzureAuth.exe",
        };
        var inspector = new CountingInspector(
            AzureAuthArtifactInspection.Trusted(CreateMatchingEvidence(CreateDeploymentConfig()))
        );

        Assert.Throws<ArgumentException>(() => AzureAuthTrustPolicy.Evaluate(invalidConfig, inspector));
        Assert.Equal(0, inspector.CallCount);
    }

    [Theory]
    [MemberData(nameof(TrustMismatchCases))]
    public void EvaluateReturnsUntrustedWithProductDetailForEachMismatch(
        string field,
        AzureAuthArtifactEvidence evidence,
        string expectedReason
    )
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        Assert.False(string.IsNullOrWhiteSpace(field));

        AzureAuthTrustResult result = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig!,
            AzureAuthArtifactInspection.Trusted(evidence)
        );
        AzureAuthDoctorReport report = AzureAuthDoctor.Run(
            config,
            AzureAuthPersistedRecord<AzureAuthBinding>.Missing("azureauth/binding.json"),
            result
        );

        Assert.Equal(AzureAuthArtifactTrustStatus.Untrusted, result.Status);
        Assert.False(result.IsReady);
        Assert.Equal(evidence, result.Evidence);
        Assert.Equal(AzureAuthDoctorCheckStatus.Fail, report.Checks[1].Status);
        Assert.Contains(expectedReason, report.Checks[1].Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TrustMismatchCases))]
    public void EnsureValidAcceptsUntrustedInspectionAndEvaluateProducedResult(
        string field,
        AzureAuthArtifactEvidence evidence,
        string expectedReason
    )
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        Assert.False(string.IsNullOrWhiteSpace(field));
        Assert.False(string.IsNullOrWhiteSpace(expectedReason));

        AzureAuthArtifactInspection inspection = AzureAuthArtifactInspection.Untrusted(evidence);
        AzureAuthTrustResult result = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig!,
            AzureAuthArtifactInspection.Trusted(evidence)
        );

        Assert.Equal(AzureAuthArtifactTrustStatus.Untrusted, result.Status);
        AzureAuthTrustPolicy.EnsureValid(inspection);
        AzureAuthTrustPolicy.EnsureValid(result);
    }

    [Fact]
    public void EnsureValidStillRejectsTrustedEvidenceThatIsNotNormalized()
    {
        AzureAuthArtifactEvidence evidence = CreateMatchingEvidence(CreateDeploymentConfig()) with
        {
            CanonicalPath = @"c:\Tools\AzureAuth.exe",
        };

        Assert.Throws<ArgumentException>(() =>
            AzureAuthTrustPolicy.EnsureValid(AzureAuthArtifactInspection.Trusted(evidence))
        );
        Assert.Throws<ArgumentException>(() =>
            AzureAuthTrustPolicy.EnsureValid(
                AzureAuthTrustResult.Trusted(evidence, new string('a', 64))
            )
        );
    }

    [Fact]
    public void EvaluateDoesNotUpgradeInspectorUntrustedOrDeferredResults()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        AzureAuthArtifactEvidence evidence = CreateMatchingEvidence(config.DeploymentConfig!);

        AzureAuthTrustResult untrusted = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig!,
            AzureAuthArtifactInspection.Untrusted(evidence)
        );
        AzureAuthTrustResult deferred = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig!,
            new DeferredAzureAuthArtifactTrustInspector()
        );

        Assert.Equal(AzureAuthArtifactTrustStatus.Untrusted, untrusted.Status);
        Assert.Equal(AzureAuthArtifactTrustStatus.Deferred, deferred.Status);
        Assert.False(untrusted.IsReady);
        Assert.False(deferred.IsReady);
    }

    [Theory]
    [MemberData(nameof(DeploymentDriftCases))]
    public void BindRejectsCachedTrustedResultFromDifferentDeployment(
        string field,
        AzureAuthDeploymentConfig currentDeployment,
        string expectedReason
    )
    {
        AzureAuthProviderConfig originalConfig = CreateAzureAuthConfig();
        Assert.False(string.IsNullOrWhiteSpace(field));
        AzureAuthTrustResult trustedForOriginal = CreateTrustedResult(originalConfig.DeploymentConfig!);
        AzureAuthProviderConfig currentConfig = AzureAuthProviderConfig.CreateAzureAuth(currentDeployment);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            AzureAuthBindingPolicy.Bind(
                AzureAuthBindingPolicy.CreateUnbound(
                    new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero)
                ),
                currentConfig,
                "user@example.com",
                "tenant-one",
                new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero),
                trustedForOriginal
            )
        );
        AzureAuthTrustResult revalidated = AzureAuthTrustPolicy.Revalidate(
            currentDeployment,
            trustedForOriginal
        );
        AzureAuthDoctorReport report = AzureAuthDoctor.Run(
            currentConfig,
            AzureAuthPersistedRecord<AzureAuthBinding>.Missing("azureauth/binding.json"),
            trustedForOriginal
        );

        Assert.Contains("trusted deployment result", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AzureAuthArtifactTrustStatus.Untrusted, revalidated.Status);
        Assert.Equal(AzureAuthDoctorCheckStatus.Fail, report.Checks[1].Status);
        Assert.Contains(expectedReason, report.Checks[1].Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DeploymentDriftCases))]
    public void DoctorRejectsCachedTrustedResultAndBindingFromDifferentDeployment(
        string field,
        AzureAuthDeploymentConfig currentDeployment,
        string expectedReason
    )
    {
        AzureAuthProviderConfig originalConfig = CreateAzureAuthConfig();
        Assert.False(string.IsNullOrWhiteSpace(field));
        AzureAuthTrustResult trustedForOriginal = CreateTrustedResult(originalConfig.DeploymentConfig!);
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            originalConfig,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            trustedForOriginal
        );
        AzureAuthDoctorReport report = AzureAuthDoctor.Run(
            AzureAuthProviderConfig.CreateAzureAuth(currentDeployment),
            AzureAuthPersistedRecord<AzureAuthBinding>.Present(
                "azureauth/binding.json",
                "r1",
                binding
            ),
            trustedForOriginal
        );

        Assert.Equal(AzureAuthDoctorCheckStatus.Fail, report.Checks[1].Status);
        Assert.Contains(expectedReason, report.Checks[1].Message, StringComparison.Ordinal);
        Assert.Equal(AzureAuthDoctorCheckStatus.Fail, report.Checks[2].Status);
    }

    [Fact]
    public void BindAndDoctorRejectTrustedResultWithTamperedDeploymentKey()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        AzureAuthTrustResult trust = CreateTrustedResult(config.DeploymentConfig!);
        AzureAuthTrustResult tamperedTrust = trust with { DeploymentKey = new string('b', 64) };
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            trust
        );

        Assert.Throws<InvalidOperationException>(() =>
            AzureAuthBindingPolicy.Bind(
                AzureAuthBindingPolicy.CreateUnbound(
                    new DateTimeOffset(2026, 7, 20, 1, 0, 0, TimeSpan.Zero)
                ),
                config,
                "user@example.com",
                "tenant-one",
                new DateTimeOffset(2026, 7, 20, 2, 0, 0, TimeSpan.Zero),
                tamperedTrust
            )
        );

        AzureAuthDoctorReport report = AzureAuthDoctor.Run(
            config,
            AzureAuthPersistedRecord<AzureAuthBinding>.Present(
                "azureauth/binding.json",
                "r2",
                binding
            ),
            tamperedTrust
        );

        Assert.Equal(AzureAuthDoctorCheckStatus.Fail, report.Checks[1].Status);
        Assert.Equal(AzureAuthDoctorCheckStatus.Fail, report.Checks[2].Status);
    }

    [Fact]
    public void DoctorIsNonMutatingAndReportsCoherentPasses()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        AzureAuthTrustResult trust = CreateTrustedResult(config.DeploymentConfig!);
        AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
            config,
            "User@Example.com",
            "Tenant-One",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            trust
        );
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord =
            AzureAuthPersistedRecord<AzureAuthBinding>.Present(
                "azureauth/binding.json",
                "r1",
                binding
            );
        AzureAuthProviderConfig configBefore = config with { DeploymentConfig = config.DeploymentConfig! with { } };
        AzureAuthTrustResult trustBefore = trust with { };
        AzureAuthPersistedRecord<AzureAuthBinding> bindingBefore = bindingRecord with { };

        AzureAuthDoctorReport report = AzureAuthDoctor.Run(config, bindingRecord, trust);

        Assert.Equal(
            [
                AzureAuthDoctorCheckStatus.Pass,
                AzureAuthDoctorCheckStatus.Pass,
                AzureAuthDoctorCheckStatus.Pass,
            ],
            report.Checks.Select(static check => check.Status).ToArray()
        );
        Assert.Equal(configBefore, config);
        Assert.Equal(trustBefore, trust);
        Assert.Equal(bindingBefore, bindingRecord);
    }

    [Fact]
    public void DoctorReportsDeferredTrustAndBindingDrift()
    {
        AzureAuthProviderConfig config = CreateAzureAuthConfig();
        AzureAuthTrustResult trusted = CreateTrustedResult(config.DeploymentConfig!);
        AzureAuthBinding driftedBinding = AzureAuthBindingPolicy.CreateBound(
            config,
            "user@example.com",
            "tenant-one",
            new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero),
            trusted
        ) with
        {
            DeploymentKey = new string('b', 64),
        };
        AzureAuthPersistedRecord<AzureAuthBinding> driftedRecord =
            AzureAuthPersistedRecord<AzureAuthBinding>.Present(
                "azureauth/binding.json",
                "r2",
                driftedBinding
            );
        AzureAuthDoctorReport deferredReport = AzureAuthDoctor.Run(
            config,
            AzureAuthPersistedRecord<AzureAuthBinding>.Malformed("azureauth/binding.json", "r3"),
            AzureAuthTrustResult.Deferred()
        );
        AzureAuthDoctorReport driftedReport = AzureAuthDoctor.Run(config, driftedRecord, trusted);

        Assert.Equal(AzureAuthDoctorCheckStatus.Deferred, deferredReport.Checks[1].Status);
        Assert.Equal(AzureAuthDoctorCheckStatus.Fail, deferredReport.Checks[2].Status);
        Assert.Equal(AzureAuthDoctorCheckStatus.Fail, driftedReport.Checks[2].Status);
    }

    private static AzureAuthProviderConfig CreateAzureAuthConfig(
        AzureAuthDeploymentConfig? deploymentConfig = null
    ) => AzureAuthProviderConfig.CreateAzureAuth(deploymentConfig ?? CreateDeploymentConfig());

    private static AzureAuthDeploymentConfig CreateDeploymentConfig() =>
        new()
        {
            SchemaVersion = ContractVersions.AzureAuthDeploymentConfigSchemaMajor,
            ExecutablePath = @"C:\Tools\AzureAuth.exe",
            ExecutableSha256 = new string('a', 64),
            SignerIdentity = "CN=AzureAuth, O=Hcoona, C=US",
            PublisherName = "Hcoona AzureAuth",
            ExecutableVersion = "1.0.0.0",
            ProvenanceIdentifier = "foundation/wp2",
        };

    private static AzureAuthArtifactEvidence CreateMatchingEvidence(
        AzureAuthDeploymentConfig deploymentConfig
    ) =>
        new()
        {
            CanonicalPath = deploymentConfig.ExecutablePath,
            StableArtifactIdentity = new FileSystemEntryIdentity("artifact-1"),
            Sha256Hash = deploymentConfig.ExecutableSha256,
            SignerIdentity = deploymentConfig.SignerIdentity,
            PublisherName = deploymentConfig.PublisherName,
            ExecutableVersion = deploymentConfig.ExecutableVersion,
            ProvenanceIdentifier = deploymentConfig.ProvenanceIdentifier,
            Owner = new FileSystemOwner("current-user"),
            CurrentUserOwnsArtifact = true,
            OwnerOnlyWritable = true,
            DiscretionaryAclsPresentAndNonNull = true,
            TrustedExecutableDirectory = @"C:\Tools",
            ExecutableDirectoryChainHasNoReparsePoints = true,
            ExecutableDirectoryChainOwnerOnlyWritable = true,
            TrustedSystemDirectory = @"C:\Windows\System32",
            SystemDirectoryChainHasNoReparsePoints = true,
            SystemDirectoryChainOwnerOnlyWritable = true,
            TrustedWorkingDirectory = @"C:\Windows\System32",
            TrustedPathEntries = [@"C:\Windows\System32"],
        };

    private static AzureAuthTrustResult CreateTrustedResult(AzureAuthDeploymentConfig deploymentConfig) =>
        AzureAuthTrustPolicy.Evaluate(
            deploymentConfig,
            AzureAuthArtifactInspection.Trusted(CreateMatchingEvidence(deploymentConfig))
        );

    private sealed class CountingInspector(AzureAuthArtifactInspection inspection)
        : IAzureAuthArtifactTrustInspector
    {
        private readonly AzureAuthArtifactInspection _inspection = inspection;

        public int CallCount { get; private set; }

        public AzureAuthArtifactInspection Inspect(AzureAuthDeploymentConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            CallCount++;
            return _inspection;
        }
    }
}
