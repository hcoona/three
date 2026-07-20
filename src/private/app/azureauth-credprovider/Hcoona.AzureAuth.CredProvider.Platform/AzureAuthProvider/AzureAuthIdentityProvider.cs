using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

/// <summary>
/// Optional async AzureAuth-backed token acquisition path for a future composed runtime.
/// WP3 narrows the inspector/runner handoff window by re-running the trusted inspection in
/// <see cref="ProcessStartSpec.PreStartValidation" />, but the current inspector contract still
/// attests only path-based same-artifact inspection and does not provide a retained launch lease.
/// The residual TOCTOU window between that final validation and OS process creation is therefore
/// documented and not claimed away.
/// </summary>
public sealed class AzureAuthIdentityProvider : IAccessTokenIdentityProvider
{
    internal const string AzureDevOpsResourceId = "499b84ac-1321-427f-aa17-267ca6975798";
    internal const string AzureDevOpsDefaultScope = AzureDevOpsResourceId + "/.default";
    internal const string AzureDevOpsPublicClientId = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";

    private readonly AzureAuthBinding _binding;
    private readonly AzureAuthProviderConfig _providerConfig;
    private readonly AzureAuthProcessLaunchOptions _launchOptions;
    private readonly IProcessRunner _processRunner;
    private readonly TimeProvider _timeProvider;
    private readonly IAzureAuthArtifactTrustInspector _trustInspector;

    public AzureAuthIdentityProvider(
        AzureAuthProviderConfig providerConfig,
        AzureAuthBinding binding,
        AzureAuthProcessLaunchOptions launchOptions,
        IAzureAuthArtifactTrustInspector? trustInspector = null,
        IProcessRunner? processRunner = null,
        TimeProvider? timeProvider = null
    )
    {
        ArgumentNullException.ThrowIfNull(providerConfig);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(launchOptions);

        AzureAuthProviderConfigPolicy.EnsureValid(providerConfig);
        AzureAuthBindingPolicy.EnsureValid(binding);
        launchOptions.Validate();

        _providerConfig = providerConfig;
        _binding = binding;
        _launchOptions = launchOptions;
        _trustInspector = trustInspector ?? new DeferredAzureAuthArtifactTrustInspector();
        _processRunner = processRunner ?? new SystemProcessRunner();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<AcquiredAccessTokenResult> AcquireAccessTokenAsync(
        CredentialRequestV2 request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                AcquiredAccessTokenStatus.Canceled,
                "AzureAuthProcessCanceled",
                "AzureAuth token acquisition was canceled."
            );
        }

        try
        {
            PreflightOutcome preflight = GetPreflightOutcome(request);
            if (preflight.Result is not null)
            {
                return preflight.Result;
            }

            ProcessResult processResult = await _processRunner
                .RunAsync(CreateStartSpec(preflight.Authorization!, request), cancellationToken)
                .ConfigureAwait(false);
            return MapProcessResult(processResult, preflight.Authorization!);
        }
        catch (AzureAuthLaunchAuthorizationException exception)
        {
            return exception.Result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                AcquiredAccessTokenStatus.Canceled,
                "AzureAuthProcessCanceled",
                "AzureAuth token acquisition was canceled."
            );
        }
        catch (Exception)
        {
            return Failure(
                AcquiredAccessTokenStatus.Fatal,
                "AzureAuthProviderFailure",
                "AzureAuth token acquisition failed."
            );
        }
    }

    private PreflightOutcome GetPreflightOutcome(CredentialRequestV2 request)
    {
        AcquiredAccessTokenResult? failure = GetRequestFailure(request);
        if (failure is not null)
        {
            return new PreflightOutcome(failure);
        }

        failure = GetConfigurationFailure(request);
        if (failure is not null)
        {
            return new PreflightOutcome(failure);
        }

        AzureAuthDeploymentConfig deploymentConfig = _providerConfig.DeploymentConfig!;
        AzureAuthTrustResult trustResult = AzureAuthTrustPolicy.Evaluate(deploymentConfig, _trustInspector);
        if (!trustResult.IsReady || string.IsNullOrWhiteSpace(trustResult.DeploymentKey))
        {
            return new PreflightOutcome(GetTrustFailure(trustResult));
        }

        failure = GetBindingFailure(trustResult, request);
        if (failure is not null)
        {
            return new PreflightOutcome(failure);
        }

        return new PreflightOutcome(
            new AzureAuthLaunchAuthorization(
                deploymentConfig,
                AssertBoundValue(_binding.TenantId),
                "web",
                trustResult.Evidence!,
                trustResult.DeploymentKey
            )
        );
    }

    private static AcquiredAccessTokenResult? GetRequestFailure(CredentialRequestV2 request)
    {
        switch (request.AcquisitionMode)
        {
            case AcquisitionMode.Unspecified:
                return Failure(
                    AcquiredAccessTokenStatus.InteractionBlocked,
                    "AzureAuthAcquisitionModeRequired",
                    "AzureAuth requires acquisitionMode interactionAllowed."
                );
            case AcquisitionMode.SilentOnly:
                return Failure(
                    AcquiredAccessTokenStatus.InteractionRequired,
                    "AzureAuthSilentOnlyUnsupported",
                    "AzureAuth does not have a validated silent token acquisition path."
                );
            case AcquisitionMode.InteractionAllowed:
                break;
            default:
                return Failure(
                    AcquiredAccessTokenStatus.RequestRejected,
                    "AzureAuthRequestRejected",
                    "AzureAuth rejected the credential request."
                );
        }

        if (request.IdentityFlow == IdentityFlow.DeviceCode)
        {
            return Failure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthDeviceCodeUnsupported",
                "AzureAuth device-code interaction is unavailable until a secret-safe interaction channel exists."
            );
        }

        if (request.CachePolicy == CachePolicyMode.FuturePersistentCacheRequested)
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthPersistentCacheUnsupported",
                "AzureAuth persistent cache is not enabled in this work package."
            );
        }

        if (!IsValidHint(request.AccountHint) || !IsValidHint(request.TenantHint))
        {
            return Failure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthRequestRejected",
                "AzureAuth rejected the credential request."
            );
        }

        if (CredentialRequestV2Policy.GetViolation(request) is not null)
        {
            return Failure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthRequestRejected",
                "AzureAuth rejected the credential request."
            );
        }

        if (!IdentityFlowPolicy.IsAcceptedMvpRequest(ToV1Projection(request)))
        {
            return Failure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthPolicyRejected",
                "AzureAuth rejected the credential request."
            );
        }

        return null;
    }

    private AcquiredAccessTokenResult? GetConfigurationFailure(CredentialRequestV2 request)
    {
        _ = request;
        return _providerConfig.Selection switch
        {
            AzureAuthProviderSelection.AzureAuth when _providerConfig.DeploymentConfig is not null => null,
            _ => Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthProviderSelectionMismatch",
                "AzureAuth is not the selected provider for this binding."
            ),
        };
    }

    private static AcquiredAccessTokenResult GetTrustFailure(AzureAuthTrustResult trustResult) =>
        trustResult.Status switch
        {
            AzureAuthArtifactTrustStatus.Deferred => Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthTrustDeferred",
                "AzureAuth executable trust is deferred for the current deployment."
            ),
            _ => Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthTrustRejected",
                "AzureAuth executable trust does not match the current deployment."
            ),
        };

    private AcquiredAccessTokenResult? GetBindingFailure(
        AzureAuthTrustResult trustResult,
        CredentialRequestV2 request
    )
    {
        if (_binding.State == AzureAuthBindingState.Unbound)
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthBindingRequired",
                "AzureAuth binding is required before requesting a token."
            );
        }

        if (_binding.ProviderSelection != AzureAuthProviderSelection.AzureAuth)
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthBindingProviderMismatch",
                "AzureAuth binding does not match the selected provider."
            );
        }

        if (!string.Equals(_binding.DeploymentKey, trustResult.DeploymentKey, StringComparison.Ordinal))
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthBindingDeploymentMismatch",
                "AzureAuth binding does not match the trusted deployment."
            );
        }

        if (IsHintMismatch(request.AccountHint, _binding.AccountId))
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthBindingAccountMismatch",
                "AzureAuth account hint does not match the current binding."
            );
        }

        if (IsHintMismatch(request.TenantHint, _binding.TenantId))
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthBindingTenantMismatch",
                "AzureAuth tenant hint does not match the current binding."
            );
        }

        return null;
    }

    private ProcessStartSpec CreateStartSpec(
        AzureAuthLaunchAuthorization authorization,
        CredentialRequestV2 request
    )
    {
        return new ProcessStartSpec(
            authorization.DeploymentConfig.ExecutablePath,
            [
                "aad",
                "--client",
                AzureDevOpsPublicClientId,
                "--tenant",
                authorization.TenantId,
                "--scope",
                AzureDevOpsDefaultScope,
                "--mode",
                authorization.ModeArgument,
                "--output",
                "token",
            ],
            workingDirectory: authorization.Evidence.TrustedWorkingDirectory,
            environment: _launchOptions.CreateEnvironment(
                authorization.Evidence.TrustedPathEntries,
                disableMsalCache: request.CachePolicy
                    is CachePolicyMode.NoCache
                        or CachePolicyMode.ProductPersistentCacheDisabled
                        or CachePolicyMode.NonPersistentCi
            ),
            standardInput: null,
            environmentMode: ProcessEnvironmentMode.ExplicitOnly,
            preStartValidation: cancellationToken =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return ValueTask.FromCanceled(cancellationToken);
                }

                PreflightOutcome revalidated = GetPreflightOutcome(request);
                return revalidated.Result is null
                    ? ValueTask.CompletedTask
                    : ValueTask.FromException(
                        new AzureAuthLaunchAuthorizationException(revalidated.Result)
                    );
            },
            timeout: _launchOptions.Timeout,
            outputCaptureOptions: _launchOptions.ToOutputCaptureOptions(),
            useWindowsEnvironmentVariableSemantics: true
        );
    }

    private AcquiredAccessTokenResult MapProcessResult(
        ProcessResult processResult,
        AzureAuthLaunchAuthorization authorization
    ) =>
        processResult.Status switch
        {
            ProcessExecutionStatus.Success => ValidateSuccessfulProcessOutput(
                processResult.StandardOutput,
                authorization
            ),
            ProcessExecutionStatus.NonZeroExit => Failure(
                AcquiredAccessTokenStatus.ProcessFailed,
                "AzureAuthProcessExitNonZero",
                "AzureAuth process did not return a token."
            ),
            ProcessExecutionStatus.LaunchFailure => Failure(
                AcquiredAccessTokenStatus.ProcessFailed,
                "AzureAuthProcessLaunchFailed",
                "AzureAuth process launch failed."
            ),
            ProcessExecutionStatus.OutputTooLarge => Failure(
                AcquiredAccessTokenStatus.OutputRejected,
                "AzureAuthProcessOutputTooLarge",
                "AzureAuth process output exceeded the configured limit."
            ),
            ProcessExecutionStatus.InvalidOutput => Failure(
                AcquiredAccessTokenStatus.OutputRejected,
                "AzureAuthProcessOutputInvalid",
                "AzureAuth process output was invalid."
            ),
            ProcessExecutionStatus.Canceled => Failure(
                AcquiredAccessTokenStatus.Canceled,
                "AzureAuthProcessCanceled",
                "AzureAuth token acquisition was canceled."
            ),
            ProcessExecutionStatus.TimedOut => Failure(
                AcquiredAccessTokenStatus.TimedOut,
                "AzureAuthProcessTimedOut",
                "AzureAuth token acquisition timed out."
            ),
            _ => Failure(
                AcquiredAccessTokenStatus.Fatal,
                "AzureAuthProcessFailed",
                "AzureAuth token acquisition failed."
            ),
        };

    private AcquiredAccessTokenResult ValidateSuccessfulProcessOutput(
        string standardOutput,
        AzureAuthLaunchAuthorization authorization
    )
    {
        string? token = NormalizeTokenOutput(standardOutput);
        if (token is null)
        {
            return Failure(
                AcquiredAccessTokenStatus.OutputRejected,
                "AzureAuthTokenOutputInvalid",
                "AzureAuth token output was invalid."
            );
        }

        if (
            !AzureDevOpsJwtClaimConsistencyValidator.TryValidate(
                token,
                authorization.TenantId,
                _timeProvider.GetUtcNow(),
                out AzureDevOpsJwtClaimConsistency? consistency)
            || consistency is null
        )
        {
            return Failure(
                AcquiredAccessTokenStatus.OutputRejected,
                "AzureAuthTokenClaimsInconsistent",
                "AzureAuth token claim consistency validation failed."
            );
        }

        return AcquiredAccessTokenResult.Success(
            new AcquiredAccessToken
            {
                AccountId = null,
                TenantId = authorization.TenantId,
                DeploymentKey = authorization.DeploymentKey,
                Token = new SecretText { Value = token },
                IssuedAt = consistency.IssuedAt,
                NotBefore = consistency.NotBefore,
                ExpiresAt = consistency.ExpiresAt,
                Provenance = AccessTokenAcquisitionProvenance.AzureAuthProcess,
                ClaimValidation = AccessTokenClaimValidation.AzureDevOpsClaimConsistency,
            }
        );
    }

    private static string? NormalizeTokenOutput(string standardOutput)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);

        string token = standardOutput.EndsWith("\r\n", StringComparison.Ordinal)
            ? standardOutput[..^2]
            : standardOutput.EndsWith('\n')
                ? standardOutput[..^1]
                : standardOutput;

        if (
            token.Length == 0
            || token.Contains('\r')
            || token.Contains('\n')
            || char.IsWhiteSpace(token[0])
            || char.IsWhiteSpace(token[^1])
            || token.Any(char.IsControl)
        )
        {
            return null;
        }

        return token;
    }

    private static bool IsHintMismatch(string? hint, string? boundValue)
    {
        if (hint is null)
        {
            return false;
        }

        string normalized = AzureAuthBindingPolicy.NormalizeObservedIdentifier(hint, nameof(hint));
        return !string.Equals(normalized, boundValue, StringComparison.Ordinal);
    }

    private static bool IsValidHint(string? hint)
    {
        if (hint is null)
        {
            return true;
        }

        try
        {
            _ = AzureAuthBindingPolicy.NormalizeObservedIdentifier(hint, nameof(hint));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string AssertBoundValue(string? value) =>
        value ?? throw new InvalidOperationException("Bound AzureAuth identities must be present.");

    private static CredentialRequest ToV1Projection(CredentialRequestV2 request) =>
        new()
        {
            ContractMajor = ContractVersions.CredentialContractMajor,
            Ecosystem = request.Ecosystem,
            Operation = request.Operation,
            Resource = request.Resource!,
            ServiceIdentity = request.ServiceIdentity!,
            AccountHint = request.AccountHint,
            TenantHint = request.TenantHint,
            RequestedAudience = request.RequestedAudience,
            CredentialKind = request.CredentialKind,
            IdentityFlow = request.IdentityFlow,
            InteractivePolicy = request.InteractivePolicy,
            CachePolicy = request.CachePolicy,
            CiContext = request.CiContext,
            ExtensionData = request.ExtensionData,
        };

    private static AcquiredAccessTokenResult Failure(
        AcquiredAccessTokenStatus status,
        string code,
        string safeMessage
    ) => AcquiredAccessTokenResult.Failure(status, code, safeMessage);

    private sealed record AzureAuthLaunchAuthorization(
        AzureAuthDeploymentConfig DeploymentConfig,
        string TenantId,
        string ModeArgument,
        AzureAuthArtifactEvidence Evidence,
        string DeploymentKey
    );

    private sealed class AzureAuthLaunchAuthorizationException(AcquiredAccessTokenResult result)
        : Exception(result.SafeMessage)
    {
        public AcquiredAccessTokenResult Result { get; } = result;
    }

    private sealed class PreflightOutcome
    {
        public PreflightOutcome(AcquiredAccessTokenResult result)
        {
            Result = result;
        }

        public PreflightOutcome(AzureAuthLaunchAuthorization authorization)
        {
            Authorization = authorization;
        }

        public AzureAuthLaunchAuthorization? Authorization { get; }

        public AcquiredAccessTokenResult? Result { get; }
    }
}
