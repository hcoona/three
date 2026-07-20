using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

/// <summary>
/// Trusted WP3 adapter seam. The implementation performs canonical-path, same-artifact, hash,
/// Authenticode, owner, and writability checks. When it returns evidence, the string fields are
/// already the adapter's normalized observations: canonical Windows path casing, lowercase
/// SHA-256, exact signer and publisher text, exact version and provenance text, and trimmed stable
/// identity and owner identifiers. WP2 compares that evidence byte-for-byte against the current
/// deployment config and treats any non-normalized or mismatched value as untrusted instead of
/// repairing it.
/// </summary>
public interface IAzureAuthArtifactTrustInspector
{
    AzureAuthArtifactInspection Inspect(AzureAuthDeploymentConfig config);
}

/// <summary>Explicit WP2 placeholder until WP3 ships a real inspector.</summary>
public sealed class DeferredAzureAuthArtifactTrustInspector : IAzureAuthArtifactTrustInspector
{
    public AzureAuthArtifactInspection Inspect(AzureAuthDeploymentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return AzureAuthArtifactInspection.Deferred();
    }
}

public enum AzureAuthArtifactTrustStatus
{
    Unspecified = 0,
    Deferred = 1,
    Untrusted = 2,
    Trusted = 3,
}

/// <summary>Trusted structured evidence produced by the inspector.</summary>
public sealed record AzureAuthArtifactEvidence
{
    public required string CanonicalPath { get; init; }

    public required FileSystemEntryIdentity StableArtifactIdentity { get; init; }

    public required string Sha256Hash { get; init; }

    public required string SignerIdentity { get; init; }

    public required string PublisherName { get; init; }

    public required string ExecutableVersion { get; init; }

    public required string ProvenanceIdentifier { get; init; }

    public required FileSystemOwner Owner { get; init; }

    public required bool CurrentUserOwnsArtifact { get; init; }

    public required bool OwnerOnlyWritable { get; init; }
}

/// <summary>
/// One raw trusted-adapter inspection result. Non-trusted results may still carry raw evidence for
/// diagnostics even when it is mismatched or non-normalized.
/// </summary>
public sealed record AzureAuthArtifactInspection
{
    public required AzureAuthArtifactTrustStatus Status { get; init; }

    public AzureAuthArtifactEvidence? Evidence { get; init; }

    public static AzureAuthArtifactInspection Deferred(AzureAuthArtifactEvidence? evidence = null) =>
        new() { Status = AzureAuthArtifactTrustStatus.Deferred, Evidence = evidence };

    public static AzureAuthArtifactInspection Untrusted(AzureAuthArtifactEvidence? evidence = null) =>
        new() { Status = AzureAuthArtifactTrustStatus.Untrusted, Evidence = evidence };

    public static AzureAuthArtifactInspection Trusted(AzureAuthArtifactEvidence evidence) =>
        new() { Status = AzureAuthArtifactTrustStatus.Trusted, Evidence = evidence };
}

/// <summary>
/// Final WP2 trust decision after exact pin validation. Non-trusted results may still carry raw
/// evidence for diagnostics even when it is mismatched or non-normalized.
/// </summary>
public sealed record AzureAuthTrustResult
{
    public required AzureAuthArtifactTrustStatus Status { get; init; }

    public AzureAuthArtifactEvidence? Evidence { get; init; }

    public string? DeploymentKey { get; init; }

    public bool IsReady => Status == AzureAuthArtifactTrustStatus.Trusted;

    public static AzureAuthTrustResult Unspecified() =>
        new() { Status = AzureAuthArtifactTrustStatus.Unspecified };

    public static AzureAuthTrustResult Deferred(AzureAuthArtifactEvidence? evidence = null) =>
        new() { Status = AzureAuthArtifactTrustStatus.Deferred, Evidence = evidence };

    public static AzureAuthTrustResult Untrusted(AzureAuthArtifactEvidence? evidence = null) =>
        new() { Status = AzureAuthArtifactTrustStatus.Untrusted, Evidence = evidence };

    public static AzureAuthTrustResult Trusted(
        AzureAuthArtifactEvidence evidence,
        string deploymentKey
    ) =>
        new()
        {
            Status = AzureAuthArtifactTrustStatus.Trusted,
            Evidence = evidence,
            DeploymentKey = deploymentKey,
        };
}

public static class AzureAuthTrustPolicy
{
    public static AzureAuthTrustResult Evaluate(
        AzureAuthDeploymentConfig config,
        IAzureAuthArtifactTrustInspector inspector
    )
    {
        ArgumentNullException.ThrowIfNull(inspector);
        AzureAuthDeploymentConfigPolicy.EnsureValid(config);

        AzureAuthArtifactInspection? inspection = inspector.Inspect(config);
        return inspection is null ? AzureAuthTrustResult.Untrusted() : EvaluateCore(config, inspection);
    }

    public static AzureAuthTrustResult Evaluate(
        AzureAuthDeploymentConfig config,
        AzureAuthArtifactInspection inspection
    )
    {
        AzureAuthDeploymentConfigPolicy.EnsureValid(config);
        ArgumentNullException.ThrowIfNull(inspection);
        return EvaluateCore(config, inspection);
    }

    /// <summary>
    /// Revalidates a cached trust result against the current deployment config. Trusted results are
    /// accepted only when the evidence still matches the current pins and the deployment key
    /// recomputes to the same value.
    /// </summary>
    public static AzureAuthTrustResult Revalidate(
        AzureAuthDeploymentConfig config,
        AzureAuthTrustResult result
    )
    {
        ArgumentNullException.ThrowIfNull(result);
        AzureAuthDeploymentConfigPolicy.EnsureValid(config);

        return result.Status switch
        {
            AzureAuthArtifactTrustStatus.Deferred => AzureAuthTrustResult.Deferred(result.Evidence),
            AzureAuthArtifactTrustStatus.Untrusted => AzureAuthTrustResult.Untrusted(result.Evidence),
            AzureAuthArtifactTrustStatus.Trusted => result.DeploymentKey is null
                ? AzureAuthTrustResult.Untrusted(result.Evidence)
                : EvaluateTrustedEvidence(config, result.Evidence, result.DeploymentKey),
            AzureAuthArtifactTrustStatus.Unspecified => AzureAuthTrustResult.Unspecified(),
            _ => AzureAuthTrustResult.Untrusted(result.Evidence),
        };
    }

    public static void EnsureValid(AzureAuthArtifactInspection inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        EnsureKnownStatus(inspection.Status, nameof(inspection));

        if (inspection.Status == AzureAuthArtifactTrustStatus.Trusted && inspection.Evidence is null)
        {
            throw new ArgumentException(
                "Trusted inspection results must include evidence.",
                nameof(inspection)
            );
        }

        if (inspection.Status == AzureAuthArtifactTrustStatus.Trusted)
        {
            EnsureValidEvidence(inspection.Evidence!);
        }
    }

    public static void EnsureValid(AzureAuthTrustResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureKnownStatus(result.Status, nameof(result));

        if (result.Status == AzureAuthArtifactTrustStatus.Trusted)
        {
            if (result.Evidence is null)
            {
                throw new ArgumentException(
                    "Trusted results must include evidence.",
                    nameof(result)
                );
            }

            EnsureValidEvidence(result.Evidence);
            AzureAuthDeploymentKey.EnsureValid(result.DeploymentKey, nameof(result.DeploymentKey));
            return;
        }

        if (result.DeploymentKey is not null)
        {
            throw new ArgumentException(
                "Only trusted results may carry a deployment key.",
                nameof(result)
            );
        }
    }

    internal static void EnsureValidEvidence(AzureAuthArtifactEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        WindowsPathPolicy.ValidateExecutablePath(evidence.CanonicalPath);
        AzureAuthDeploymentConfigPolicy.EnsureValidSha256(
            evidence.Sha256Hash,
            nameof(evidence.Sha256Hash)
        );
        AzureAuthDeploymentConfigPolicy.EnsureValidPrintableAsciiPin(
            evidence.SignerIdentity,
            nameof(evidence.SignerIdentity)
        );
        AzureAuthDeploymentConfigPolicy.EnsureValidPrintableAsciiPin(
            evidence.PublisherName,
            nameof(evidence.PublisherName)
        );
        AzureAuthDeploymentConfigPolicy.EnsureValidExactVersion(
            evidence.ExecutableVersion,
            nameof(evidence.ExecutableVersion)
        );
        AzureAuthDeploymentConfigPolicy.EnsureValidProvenanceIdentifier(
            evidence.ProvenanceIdentifier,
            nameof(evidence.ProvenanceIdentifier)
        );

        if (
            evidence.StableArtifactIdentity is not { Value.Length: > 0 }
            || !string.Equals(
                evidence.StableArtifactIdentity.Value,
                evidence.StableArtifactIdentity.Value.Trim(),
                StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                "Stable artifact identity is required.",
                nameof(evidence)
            );
        }

        if (
            evidence.Owner is not { Id.Length: > 0 }
            || !string.Equals(evidence.Owner.Id, evidence.Owner.Id.Trim(), StringComparison.Ordinal)
        )
        {
            throw new ArgumentException("Artifact owner is required.", nameof(evidence));
        }
    }

    private static bool MatchesPins(
        AzureAuthDeploymentConfig config,
        AzureAuthArtifactEvidence evidence
    )
    {
        return evidence.CurrentUserOwnsArtifact
            && evidence.OwnerOnlyWritable
            && WindowsPathPolicy.MatchesConfiguredCanonicalPath(
                config.ExecutablePath,
                evidence.CanonicalPath)
            && string.Equals(config.ExecutableSha256, evidence.Sha256Hash, StringComparison.Ordinal)
            && string.Equals(config.SignerIdentity, evidence.SignerIdentity, StringComparison.Ordinal)
            && string.Equals(config.PublisherName, evidence.PublisherName, StringComparison.Ordinal)
            && string.Equals(
                config.ExecutableVersion,
                evidence.ExecutableVersion,
                StringComparison.Ordinal)
            && string.Equals(
                config.ProvenanceIdentifier,
                evidence.ProvenanceIdentifier,
                StringComparison.Ordinal);
    }

    private static AzureAuthTrustResult EvaluateCore(
        AzureAuthDeploymentConfig config,
        AzureAuthArtifactInspection inspection
    ) =>
        inspection.Status switch
        {
            AzureAuthArtifactTrustStatus.Deferred => AzureAuthTrustResult.Deferred(inspection.Evidence),
            AzureAuthArtifactTrustStatus.Untrusted => AzureAuthTrustResult.Untrusted(inspection.Evidence),
            AzureAuthArtifactTrustStatus.Trusted => EvaluateTrustedEvidence(
                config,
                inspection.Evidence
            ),
            AzureAuthArtifactTrustStatus.Unspecified => AzureAuthTrustResult.Untrusted(
                inspection.Evidence
            ),
            _ => AzureAuthTrustResult.Untrusted(inspection.Evidence),
        };

    private static AzureAuthTrustResult EvaluateTrustedEvidence(
        AzureAuthDeploymentConfig config,
        AzureAuthArtifactEvidence? evidence,
        string? expectedDeploymentKey = null
    )
    {
        if (evidence is null || !IsValidTrustedEvidence(evidence))
        {
            return AzureAuthTrustResult.Untrusted(evidence);
        }

        if (!MatchesPins(config, evidence))
        {
            return AzureAuthTrustResult.Untrusted(evidence);
        }

        string deploymentKey = AzureAuthDeploymentKey.Compute(config, evidence);
        if (
            expectedDeploymentKey is not null
            && (!IsValidDeploymentKey(expectedDeploymentKey) || !string.Equals(
                expectedDeploymentKey,
                deploymentKey,
                StringComparison.Ordinal))
        )
        {
            return AzureAuthTrustResult.Untrusted(evidence);
        }

        return AzureAuthTrustResult.Trusted(evidence, deploymentKey);
    }

    private static bool IsValidDeploymentKey(string? deploymentKey)
    {
        try
        {
            AzureAuthDeploymentKey.EnsureValid(deploymentKey, nameof(deploymentKey));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidTrustedEvidence(AzureAuthArtifactEvidence evidence)
    {
        try
        {
            EnsureValidEvidence(evidence);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void EnsureKnownStatus(AzureAuthArtifactTrustStatus status, string paramName)
    {
        if (
            status
                is not (
                    AzureAuthArtifactTrustStatus.Unspecified
                    or AzureAuthArtifactTrustStatus.Deferred
                    or AzureAuthArtifactTrustStatus.Untrusted
                    or AzureAuthArtifactTrustStatus.Trusted
                )
        )
        {
            throw new ArgumentException("Unsupported AzureAuth trust status.", paramName);
        }
    }
}

internal static class AzureAuthDeploymentKey
{
    internal static string Compute(
        AzureAuthDeploymentConfig config,
        AzureAuthArtifactEvidence evidence
    )
    {
        var builder = new StringBuilder();
        AppendField(builder, config.ExecutablePath);
        AppendField(builder, config.ExecutableSha256);
        AppendField(builder, config.SignerIdentity);
        AppendField(builder, config.PublisherName);
        AppendField(builder, config.ExecutableVersion);
        AppendField(builder, config.ProvenanceIdentifier);
        AppendField(builder, evidence.CanonicalPath);
        AppendField(builder, evidence.StableArtifactIdentity.Value);
        AppendField(builder, evidence.Sha256Hash);
        AppendField(builder, evidence.SignerIdentity);
        AppendField(builder, evidence.PublisherName);
        AppendField(builder, evidence.ExecutableVersion);
        AppendField(builder, evidence.ProvenanceIdentifier);
        AppendField(builder, evidence.Owner.Id);
        AppendField(builder, evidence.CurrentUserOwnsArtifact ? "1" : "0");
        AppendField(builder, evidence.OwnerOnlyWritable ? "1" : "0");

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static void EnsureValid(string? value, string paramName) =>
        AzureAuthDeploymentConfigPolicy.EnsureValidSha256(value, paramName);

    private static void AppendField(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append(';');
    }
}
