using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

/// <summary>
/// AzureAuth-backed token acquisition for the composed runtime.
/// The trust result is validated once per acquisition and reused for process launch. The inspector
/// contract attests path-based same-artifact inspection and does not provide a retained launch lease.
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
    private readonly AzureAuthTrustResult? _trustedResult;

    public AzureAuthIdentityProvider(
        AzureAuthProviderConfig providerConfig,
        AzureAuthBinding binding,
        AzureAuthProcessLaunchOptions launchOptions,
        IAzureAuthArtifactTrustInspector? trustInspector = null,
        IProcessRunner? processRunner = null,
        TimeProvider? timeProvider = null,
        AzureAuthTrustResult? trustedResult = null
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
        _trustedResult = trustedResult;
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
            PreflightOutcome preflight = GetPreflightOutcome(request, cancellationToken);
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

    private PreflightOutcome GetPreflightOutcome(
        CredentialRequestV2 request,
        CancellationToken cancellationToken)
    {
        AzureAuthRequestPreflightFailure? requestFailure =
            AzureAuthRequestPreflightPolicy.Evaluate(request);
        if (requestFailure is not null)
        {
            return new PreflightOutcome(requestFailure.ToAcquisitionResult());
        }

        if (!_launchOptions.TryValidateInteractiveContext(out string launchCode, out string launchMessage))
        {
            return new PreflightOutcome(
                Failure(
                    AcquiredAccessTokenStatus.PrerequisiteFailed,
                    launchCode,
                    launchMessage));
        }

        AcquiredAccessTokenResult? failure = GetConfigurationFailure(request);
        if (failure is not null)
        {
            return new PreflightOutcome(failure);
        }

        AzureAuthDeploymentConfig deploymentConfig = _providerConfig.DeploymentConfig!;
        AzureAuthTrustResult? knownTrust = _trustedResult;
        AzureAuthTrustResult trustResult = knownTrust is null
            ? AzureAuthTrustPolicy.Evaluate(deploymentConfig, _trustInspector, cancellationToken)
            : AzureAuthTrustPolicy.Revalidate(deploymentConfig, knownTrust);
        if (!trustResult.IsReady || string.IsNullOrWhiteSpace(trustResult.DeploymentKey))
        {
            return new PreflightOutcome(GetTrustFailure(trustResult));
        }

        failure = GetBindingFailure(trustResult, request);
        if (failure is not null)
        {
            return new PreflightOutcome(failure);
        }

        if (!AzureAuthProcessLaunchDiscovery.TryResolveHostLaunchPaths(
                _launchOptions.HostContext,
                deploymentConfig,
                trustResult.Evidence!,
                out _,
                out _))
        {
            return new PreflightOutcome(
                Failure(
                    AcquiredAccessTokenStatus.PrerequisiteFailed,
                    "AzureAuthLaunchContextInvalid",
                    "The trusted AzureAuth host launch paths could not be derived."));
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
        if (!AzureAuthProcessLaunchDiscovery.TryResolveHostLaunchPaths(
                _launchOptions.HostContext,
                authorization.DeploymentConfig,
                authorization.Evidence,
                out string executablePath,
                out string workingDirectory))
        {
            throw new AzureAuthLaunchAuthorizationException(
                Failure(
                    AcquiredAccessTokenStatus.PrerequisiteFailed,
                    "AzureAuthLaunchContextInvalid",
                    "The trusted AzureAuth host launch paths could not be derived."));
        }

        return new ProcessStartSpec(
            executablePath,
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
            workingDirectory: workingDirectory,
            environment: _launchOptions.CreateEnvironment(
                authorization.Evidence.TrustedPathEntries,
                disableMsalCache: request.CachePolicy
                    is CachePolicyMode.NoCache
                        or CachePolicyMode.ProductPersistentCacheDisabled
                        or CachePolicyMode.NonPersistentCi
            ),
            standardInput: null,
            environmentMode: ProcessEnvironmentMode.ExplicitOnly,
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

    private static string AssertBoundValue(string? value) =>
        value ?? throw new InvalidOperationException("Bound AzureAuth identities must be present.");

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
