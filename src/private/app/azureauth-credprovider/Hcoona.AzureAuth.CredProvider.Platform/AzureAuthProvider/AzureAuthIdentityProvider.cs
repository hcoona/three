using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed class AzureAuthIdentityProvider : IAccessTokenIdentityProvider
{
    internal const string AzureDevOpsResourceId = "499b84ac-1321-427f-aa17-267ca6975798";
    internal const string AzureDevOpsDefaultScope = AzureDevOpsResourceId + "/.default";
    internal const string AzureDevOpsPublicClientId = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";

    private readonly AzureAuthBinding binding;
    private readonly AzureAuthProviderConfig providerConfig;
    private readonly AzureAuthProcessLaunchOptions launchOptions;
    private readonly IProcessRunner processRunner;

    public AzureAuthIdentityProvider(
        AzureAuthProviderConfig providerConfig,
        AzureAuthBinding binding,
        AzureAuthProcessLaunchOptions launchOptions,
        IProcessRunner? processRunner = null
    )
    {
        AzureAuthProviderConfigPolicy.EnsureValid(providerConfig);
        AzureAuthBindingPolicy.EnsureValid(binding);
        ArgumentNullException.ThrowIfNull(launchOptions);
        launchOptions.Validate();

        this.providerConfig = providerConfig;
        this.binding = binding;
        this.launchOptions = launchOptions;
        this.processRunner = processRunner ?? new SystemProcessRunner();
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
            AcquiredAccessTokenResult? preflight = GetPreflightFailure(request);
            if (preflight is not null)
            {
                return preflight;
            }

            ProcessResult processResult = await processRunner
                .RunAsync(CreateStartSpec(), cancellationToken)
                .ConfigureAwait(false);
            return MapProcessResult(processResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                AcquiredAccessTokenStatus.Canceled,
                "AzureAuthProcessCanceled",
                "AzureAuth token acquisition was canceled."
            );
        }
    }

    private AcquiredAccessTokenResult? GetPreflightFailure(CredentialRequestV2 request)
    {
        AzureAuthRequestPreflightFailure? requestFailure = AzureAuthRequestPreflightPolicy.Evaluate(
            request
        );
        if (requestFailure is not null)
        {
            return requestFailure.ToAcquisitionResult();
        }

        if (providerConfig.Selection != AzureAuthProviderSelection.AzureAuth)
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthProviderSelectionMismatch",
                "AzureAuth is not the selected provider."
            );
        }

        if (binding.ProviderSelection != AzureAuthProviderSelection.AzureAuth)
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthBindingProviderMismatch",
                "AzureAuth binding does not match the selected provider."
            );
        }

        if (
            request.AccountHint is not null
            && !string.Equals(
                AzureAuthBindingPolicy.NormalizeOptionalIdentifier(request.AccountHint),
                binding.AccountId,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthBindingAccountMismatch",
                "AzureAuth account hint does not match the current binding."
            );
        }

        if (
            request.TenantHint is not null
            && !string.Equals(
                AzureAuthBindingPolicy.NormalizeRequiredIdentifier(
                    request.TenantHint,
                    nameof(request.TenantHint)
                ),
                binding.TenantId,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "AzureAuthBindingTenantMismatch",
                "AzureAuth tenant hint does not match the current binding."
            );
        }

        return null;
    }

    private ProcessStartSpec CreateStartSpec()
    {
        var arguments = new List<string>
        {
            "aad",
            "--client",
            AzureDevOpsPublicClientId,
            "--tenant",
            binding.TenantId!,
            "--scope",
            AzureDevOpsDefaultScope,
        };
        string? domain = TryGetAccountDomain(binding.AccountId);
        if (domain is not null)
        {
            arguments.Add("--domain");
            arguments.Add(domain);
        }

        arguments.Add("--output");
        arguments.Add("token");
        return new ProcessStartSpec(
            launchOptions.ExecutablePath,
            arguments,
            workingDirectory: launchOptions.WorkingDirectory,
            timeout: launchOptions.Timeout,
            outputCaptureOptions: launchOptions.ToOutputCaptureOptions()
        );
    }

    private AcquiredAccessTokenResult MapProcessResult(ProcessResult processResult) =>
        processResult.Status switch
        {
            ProcessExecutionStatus.Success => ValidateSuccessfulProcessOutput(
                processResult.StandardOutput
            ),
            ProcessExecutionStatus.NonZeroExit => Failure(
                AcquiredAccessTokenStatus.ProcessFailed,
                "AzureAuthProcessExitNonZero",
                "AzureAuth did not return a token."
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

    private AcquiredAccessTokenResult ValidateSuccessfulProcessOutput(string standardOutput)
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

        DateTimeOffset? expiresAt = JwtExpiryMetadataReader.TryReadExpiration(
            token,
            out DateTimeOffset expiration
        )
            ? expiration
            : null;

        return AcquiredAccessTokenResult.Success(
            new AcquiredAccessToken
            {
                AccountId = null,
                TenantId = binding.TenantId!,
                Token = new SecretText { Value = token },
                ExpiresAt = expiresAt,
                Provenance = AccessTokenAcquisitionProvenance.AzureAuthProcess,
            }
        );
    }

    private static string? NormalizeTokenOutput(string standardOutput)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        string token =
            standardOutput.EndsWith("\r\n", StringComparison.Ordinal) ? standardOutput[..^2]
            : standardOutput.EndsWith('\n') ? standardOutput[..^1]
            : standardOutput;
        return
            token.Length == 0
            || token.Contains('\r')
            || token.Contains('\n')
            || token.Any(static character =>
                char.IsControl(character) || char.IsWhiteSpace(character)
            )
            ? null
            : token;
    }

    internal static string? TryGetAccountDomain(string? accountId)
    {
        string? account = AzureAuthBindingPolicy.NormalizeOptionalIdentifier(accountId);
        int separator = account?.LastIndexOf('@') ?? -1;
        if (separator <= 0 || separator == account!.Length - 1)
        {
            return null;
        }

        string domain = account[(separator + 1)..].Trim();
        return
            domain.Length == 0
            || domain.Any(static character =>
                char.IsControl(character) || char.IsWhiteSpace(character)
            )
            ? null
            : domain;
    }

    private static AcquiredAccessTokenResult Failure(
        AcquiredAccessTokenStatus status,
        string code,
        string safeMessage
    ) => AcquiredAccessTokenResult.Failure(status, code, safeMessage);
}
