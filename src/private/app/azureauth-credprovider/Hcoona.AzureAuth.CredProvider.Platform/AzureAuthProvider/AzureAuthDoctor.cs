using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public enum AzureAuthDoctorCheckStatus
{
    Unspecified = 0,
    Pass = 1,
    Warning = 2,
    Fail = 3,
    Deferred = 4,
    Unsupported = 5,
}

public sealed record AzureAuthDoctorCheck
{
    public required string Code { get; init; }

    public required AzureAuthDoctorCheckStatus Status { get; init; }

    public required string Message { get; init; }
}

public sealed record AzureAuthDoctorReport
{
    public required IReadOnlyList<AzureAuthDoctorCheck> Checks { get; init; }
}

public static class AzureAuthDoctor
{
    public static AzureAuthDoctorReport Run(
        AzureAuthProviderConfig config,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        AzureAuthTrustResult? trustResult = null
    )
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(bindingRecord);
        AzureAuthProviderConfigPolicy.EnsureValid(config);

        AzureAuthTrustResult effectiveTrust = trustResult ?? AzureAuthTrustResult.Unspecified();
        if (config.Selection == AzureAuthProviderSelection.AzureAuth)
        {
            effectiveTrust = AzureAuthTrustPolicy.Revalidate(config.DeploymentConfig!, effectiveTrust);
        }

        return new AzureAuthDoctorReport
        {
            Checks =
            [
                CreateProviderCheck(config),
                CreateTrustCheck(config, effectiveTrust),
                CreateBindingCheck(config, effectiveTrust, bindingRecord),
            ],
        };
    }

    private static AzureAuthDoctorCheck CreateProviderCheck(AzureAuthProviderConfig config) =>
        config.Selection switch
        {
            AzureAuthProviderSelection.DirectMsal => new AzureAuthDoctorCheck
            {
                Code = "provider-selection",
                Status = AzureAuthDoctorCheckStatus.Pass,
                Message =
                    "Provider selection is directMsal. WP2 persists this choice only; WP5 owns "
                    + "runtime composition.",
            },
            AzureAuthProviderSelection.AzureAuth => new AzureAuthDoctorCheck
            {
                Code = "provider-selection",
                Status = AzureAuthDoctorCheckStatus.Pass,
                Message =
                    "Provider selection is azureAuth. Readiness still depends on WP3 trust "
                    + "inspection and future WP5 composition.",
            },
            _ => throw new ArgumentException("Unsupported provider selection.", nameof(config)),
        };

    private static AzureAuthDoctorCheck CreateTrustCheck(
        AzureAuthProviderConfig config,
        AzureAuthTrustResult trustResult
    )
    {
        if (config.Selection == AzureAuthProviderSelection.DirectMsal)
        {
            return new AzureAuthDoctorCheck
            {
                Code = "deployment-trust",
                Status = AzureAuthDoctorCheckStatus.Pass,
                Message = "AzureAuth deployment trust is not required while directMsal is selected.",
            };
        }

        return trustResult.Status switch
        {
            AzureAuthArtifactTrustStatus.Trusted => new AzureAuthDoctorCheck
            {
                Code = "deployment-trust",
                Status = AzureAuthDoctorCheckStatus.Pass,
                Message = "Pinned AzureAuth deployment exactly matches trusted evidence.",
            },
            AzureAuthArtifactTrustStatus.Deferred => new AzureAuthDoctorCheck
            {
                Code = "deployment-trust",
                Status = AzureAuthDoctorCheckStatus.Deferred,
                Message =
                    "AzureAuth is configured, but the trusted inspector remains deferred until "
                    + "WP3.",
            },
            AzureAuthArtifactTrustStatus.Untrusted => new AzureAuthDoctorCheck
            {
                Code = "deployment-trust",
                Status = AzureAuthDoctorCheckStatus.Fail,
                Message = DescribeUntrustedDeployment(config, trustResult),
            },
            _ => new AzureAuthDoctorCheck
            {
                Code = "deployment-trust",
                Status = AzureAuthDoctorCheckStatus.Fail,
                Message = "AzureAuth is selected, but no trust result is available.",
            },
        };
    }

    private static AzureAuthDoctorCheck CreateBindingCheck(
        AzureAuthProviderConfig config,
        AzureAuthTrustResult trustResult,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord
    )
    {
        return bindingRecord.Status switch
        {
            AzureAuthPersistedRecordStatus.Missing => new AzureAuthDoctorCheck
            {
                Code = "binding-state",
                Status = AzureAuthDoctorCheckStatus.Warning,
                Message = "No binding record exists.",
            },
            AzureAuthPersistedRecordStatus.Malformed => new AzureAuthDoctorCheck
            {
                Code = "binding-state",
                Status = AzureAuthDoctorCheckStatus.Fail,
                Message = "Binding record is malformed. Use rebind or unbind to repair it.",
            },
            AzureAuthPersistedRecordStatus.Unsupported => new AzureAuthDoctorCheck
            {
                Code = "binding-state",
                Status = AzureAuthDoctorCheckStatus.Unsupported,
                Message = "Secure binding persistence is not implemented on this platform yet.",
            },
            AzureAuthPersistedRecordStatus.Unsafe => new AzureAuthDoctorCheck
            {
                Code = "binding-state",
                Status = AzureAuthDoctorCheckStatus.Fail,
                Message = "Secure binding persistence reported an unsafe location or policy.",
            },
            AzureAuthPersistedRecordStatus.Present => DescribePresentBinding(
                config,
                trustResult,
                AzureAuthPersistenceCore.RequireValue(bindingRecord)
            ),
            _ => throw new ArgumentException("Unsupported binding record status.", nameof(bindingRecord)),
        };
    }

    private static AzureAuthDoctorCheck DescribePresentBinding(
        AzureAuthProviderConfig config,
        AzureAuthTrustResult trustResult,
        AzureAuthBinding binding
    )
    {
        AzureAuthBindingPolicy.EnsureValid(binding);

        if (binding.State == AzureAuthBindingState.Unbound)
        {
            return new AzureAuthDoctorCheck
            {
                Code = "binding-state",
                Status = AzureAuthDoctorCheckStatus.Pass,
                Message = "Binding record is explicitly unbound.",
            };
        }

        if (binding.ProviderSelection != config.Selection)
        {
            return new AzureAuthDoctorCheck
            {
                Code = "binding-state",
                Status = AzureAuthDoctorCheckStatus.Fail,
                Message = "Binding provider does not match the current provider selection.",
            };
        }

        if (binding.ProviderSelection == AzureAuthProviderSelection.AzureAuth)
        {
            if (trustResult.Status == AzureAuthArtifactTrustStatus.Untrusted)
            {
                return new AzureAuthDoctorCheck
                {
                    Code = "binding-state",
                    Status = AzureAuthDoctorCheckStatus.Fail,
                    Message =
                        "Binding exists, but the current AzureAuth deployment failed trust "
                        + "validation.",
                };
            }

            if (!trustResult.IsReady || string.IsNullOrWhiteSpace(trustResult.DeploymentKey))
            {
                return new AzureAuthDoctorCheck
                {
                    Code = "binding-state",
                    Status = AzureAuthDoctorCheckStatus.Warning,
                    Message =
                        "Binding exists, but the current AzureAuth deployment is not ready for "
                        + "use.",
                };
            }

            if (!string.Equals(binding.DeploymentKey, trustResult.DeploymentKey, StringComparison.Ordinal))
            {
                return new AzureAuthDoctorCheck
                {
                    Code = "binding-state",
                    Status = AzureAuthDoctorCheckStatus.Fail,
                    Message = "Binding does not match the current trusted AzureAuth deployment.",
                };
            }
        }

        return new AzureAuthDoctorCheck
        {
            Code = "binding-state",
            Status = AzureAuthDoctorCheckStatus.Pass,
            Message = "Binding matches the current provider state.",
        };
    }

    private static string DescribeUntrustedDeployment(
        AzureAuthProviderConfig config,
        AzureAuthTrustResult trustResult
    )
    {
        AzureAuthDeploymentConfig deploymentConfig =
            config.DeploymentConfig
            ?? throw new ArgumentException(
                "AzureAuth provider configuration requires deployment configuration.",
                nameof(config)
            );

        AzureAuthArtifactEvidence? evidence = trustResult.Evidence;
        if (evidence is null)
        {
            return "Pinned AzureAuth deployment could not be trusted and no evidence was returned.";
        }

        var reasons = new List<string>();
        if (
            !WindowsPathPolicy.MatchesConfiguredCanonicalPath(
                deploymentConfig.ExecutablePath,
                evidence.CanonicalPath)
        )
        {
            reasons.Add("canonical path mismatch");
        }

        if (!string.Equals(deploymentConfig.ExecutableSha256, evidence.Sha256Hash, StringComparison.Ordinal))
        {
            reasons.Add("digest mismatch");
        }

        if (!string.Equals(deploymentConfig.SignerIdentity, evidence.SignerIdentity, StringComparison.Ordinal))
        {
            reasons.Add("signer mismatch");
        }

        if (!string.Equals(deploymentConfig.PublisherName, evidence.PublisherName, StringComparison.Ordinal))
        {
            reasons.Add("publisher mismatch");
        }

        if (
            !string.Equals(
                deploymentConfig.ExecutableVersion,
                evidence.ExecutableVersion,
                StringComparison.Ordinal)
        )
        {
            reasons.Add("version mismatch");
        }

        if (
            !string.Equals(
                deploymentConfig.ProvenanceIdentifier,
                evidence.ProvenanceIdentifier,
                StringComparison.Ordinal)
        )
        {
            reasons.Add("provenance mismatch");
        }

        if (!evidence.CurrentUserOwnsArtifact)
        {
            reasons.Add("artifact owner check failed");
        }

        if (!evidence.OwnerOnlyWritable)
        {
            reasons.Add("artifact is not owner-only writable");
        }

        if (
            evidence.StableArtifactIdentity is not { Value.Length: > 0 }
            || !string.Equals(
                evidence.StableArtifactIdentity.Value,
                evidence.StableArtifactIdentity.Value.Trim(),
                StringComparison.Ordinal)
        )
        {
            reasons.Add("stable artifact identity missing");
        }

        if (
            evidence.Owner is not { Id.Length: > 0 }
            || !string.Equals(evidence.Owner.Id, evidence.Owner.Id.Trim(), StringComparison.Ordinal)
        )
        {
            reasons.Add("artifact owner identity missing");
        }

        return reasons.Count == 0
            ? "Pinned AzureAuth deployment could not be trusted."
            : $"Pinned AzureAuth deployment could not be trusted: {string.Join(", ", reasons)}.";
    }
}
