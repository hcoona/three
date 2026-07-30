using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.Composition;

public interface ICredentialAcquisitionService
{
    ValueTask<CredentialResult> AcquireAsync(
        CredentialRequestV2 request,
        CancellationToken cancellationToken = default);
}

public sealed class BoundedCredentialAcquisitionAdapter
{
    private readonly ICredentialAcquisitionService service;
    private readonly TimeSpan timeout;

    public BoundedCredentialAcquisitionAdapter(
        ICredentialAcquisitionService service,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        this.service = service;
        this.timeout = timeout ?? TimeSpan.FromMinutes(16);
        if (this.timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public CredentialResult Acquire(
        CredentialRequestV2 request,
        CancellationToken cancellationToken = default)
    {
        var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        Task<CredentialResult>? acquisitionTask = null;
        var cancellationOwnershipTransferred = false;
        try
        {
            CancellationToken providerToken = providerCancellation.Token;
            acquisitionTask = Task.Run(
                async () =>
                    await service
                        .AcquireAsync(request, providerToken)
                        .ConfigureAwait(false));
            return acquisitionTask
                .WaitAsync(timeout, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            cancellationOwnershipTransferred = true;
            CancelAndDisposeWhenComplete(providerCancellation, acquisitionTask);
            return CredentialAcquisitionResultFactory.Failure(
                CredentialResultStatus.CredentialUnavailable,
                CredentialErrorKind.CredentialUnavailable,
                "CredentialAcquisitionTimedOut",
                "Credential acquisition timed out.");
        }
        catch (OperationCanceledException)
        {
            cancellationOwnershipTransferred = true;
            CancelAndDisposeWhenComplete(providerCancellation, acquisitionTask);
            return CredentialAcquisitionResultFactory.Failure(
                CredentialResultStatus.CredentialUnavailable,
                CredentialErrorKind.CredentialUnavailable,
                "CredentialAcquisitionCanceled",
                "Credential acquisition was canceled.");
        }
        finally
        {
            if (!cancellationOwnershipTransferred)
            {
                providerCancellation.Dispose();
            }
        }
    }

    private static void CancelAndDisposeWhenComplete(
        CancellationTokenSource providerCancellation,
        Task? acquisitionTask)
    {
        Task cancellationTask;
        try
        {
            cancellationTask = providerCancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            providerCancellation.Dispose();
            return;
        }

        ObserveFault(cancellationTask);
        if (acquisitionTask is not null)
        {
            ObserveFault(acquisitionTask);
        }

        Task cleanupTask =
            acquisitionTask is null
                ? cancellationTask
                : Task.WhenAll(cancellationTask, acquisitionTask);
        _ = cleanupTask.ContinueWith(
            static (completed, state) =>
            {
                _ = completed.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            providerCancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ObserveFault(Task task) =>
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}

internal sealed class ComposedCredentialAcquisitionService : ICredentialAcquisitionService
{
    private readonly Func<CancellationToken, IAccessTokenIdentityProvider> identityProviderFactory;
    private readonly CredentialMaterializationService materializationService;
    private readonly bool applyAzureAuthRequestPreflight;

    internal ComposedCredentialAcquisitionService(
        Func<CancellationToken, IAccessTokenIdentityProvider> identityProviderFactory,
        CredentialMaterializationService materializationService,
        bool applyAzureAuthRequestPreflight = false)
    {
        this.identityProviderFactory =
            identityProviderFactory ?? throw new ArgumentNullException(nameof(identityProviderFactory));
        this.materializationService =
            materializationService ?? throw new ArgumentNullException(nameof(materializationService));
        this.applyAzureAuthRequestPreflight = applyAzureAuthRequestPreflight;
    }

    public async ValueTask<CredentialResult> AcquireAsync(
        CredentialRequestV2 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (applyAzureAuthRequestPreflight)
        {
            AzureAuthRequestPreflightFailure? failure =
                AzureAuthRequestPreflightPolicy.Evaluate(request);
            if (failure is not null)
            {
                return MapAcquisitionFailure(failure.ToAcquisitionResult());
            }
        }

        if (request.AcquisitionMode == AcquisitionMode.Unspecified)
        {
            return CredentialAcquisitionResultFactory.Failure(
                CredentialResultStatus.ProtocolViolation,
                CredentialErrorKind.ProtocolViolation,
                "AcquisitionModeRequired",
                "Production credential requests must specify an acquisition mode.");
        }

        if (CredentialRequestV2Policy.GetViolation(request) is not null)
        {
            return CredentialAcquisitionResultFactory.Failure(
                CredentialResultStatus.ProtocolViolation,
                CredentialErrorKind.ProtocolViolation,
                "CredentialRequestRejected",
                "The credential acquisition request is invalid.");
        }

        IAccessTokenIdentityProvider identityProvider = identityProviderFactory(cancellationToken);
        AcquiredAccessTokenResult acquired = await identityProvider
            .AcquireAccessTokenAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!acquired.Succeeded || acquired.AccessToken is null)
        {
            return MapAcquisitionFailure(acquired);
        }

        CredentialMaterializationResult materialized = await materializationService
            .MaterializeAsync(request, acquired.AccessToken, cancellationToken)
            .ConfigureAwait(false);
        if (materialized.Status != CredentialMaterializationStatus.Success)
        {
            return MapMaterializationFailure(materialized);
        }

        return new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = materialized.Username,
            Password = materialized.Password,
            BearerToken = materialized.BearerToken,
            ExpiresAt = materialized.ExpiresAt,
            Account = acquired.AccessToken.AccountId,
            Tenant = acquired.AccessToken.TenantId,
            DiagnosticsCorrelationId = CorrelationId.New().ToString(),
        };
    }

    private static CredentialResult MapAcquisitionFailure(AcquiredAccessTokenResult result)
    {
        (CredentialResultStatus status, CredentialErrorKind kind) = result.Status switch
        {
            AcquiredAccessTokenStatus.InteractionRequired =>
                (CredentialResultStatus.InteractionRequired, CredentialErrorKind.InteractionRequired),
            AcquiredAccessTokenStatus.InteractionBlocked =>
                (CredentialResultStatus.InteractionBlocked, CredentialErrorKind.InteractionBlocked),
            AcquiredAccessTokenStatus.RequestRejected =>
                (CredentialResultStatus.ProtocolViolation, CredentialErrorKind.ProtocolViolation),
            AcquiredAccessTokenStatus.OutputRejected =>
                (CredentialResultStatus.IntegrityFailure, CredentialErrorKind.IntegrityFailure),
            AcquiredAccessTokenStatus.Canceled or AcquiredAccessTokenStatus.TimedOut =>
                (CredentialResultStatus.CredentialUnavailable, CredentialErrorKind.CredentialUnavailable),
            AcquiredAccessTokenStatus.Fatal =>
                (CredentialResultStatus.Fatal, CredentialErrorKind.Fatal),
            _ => (CredentialResultStatus.CredentialUnavailable, CredentialErrorKind.CredentialUnavailable),
        };
        return CredentialAcquisitionResultFactory.Failure(
            status,
            kind,
            result.Code ?? "ProviderUnavailable",
            result.SafeMessage ?? "The configured credential provider is unavailable.");
    }

    private static CredentialResult MapMaterializationFailure(
        CredentialMaterializationResult result)
    {
        (CredentialResultStatus status, CredentialErrorKind kind) = result.Status switch
        {
            CredentialMaterializationStatus.Canceled or CredentialMaterializationStatus.TimedOut =>
                (CredentialResultStatus.CredentialUnavailable, CredentialErrorKind.CredentialUnavailable),
            CredentialMaterializationStatus.InvalidRequest =>
                (CredentialResultStatus.ProtocolViolation, CredentialErrorKind.ProtocolViolation),
            CredentialMaterializationStatus.InvalidToken =>
                (CredentialResultStatus.IntegrityFailure, CredentialErrorKind.IntegrityFailure),
            _ => (CredentialResultStatus.CredentialUnavailable, CredentialErrorKind.CredentialUnavailable),
        };
        return CredentialAcquisitionResultFactory.Failure(
            status,
            kind,
            result.Code,
            result.SafeMessage);
    }
}

internal sealed class DirectMsalUnavailableAccessTokenProvider : IAccessTokenIdentityProvider
{
    public ValueTask<AcquiredAccessTokenResult> AcquireAccessTokenAsync(
        CredentialRequestV2 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AcquiredAccessTokenResult result;
        if (cancellationToken.IsCancellationRequested)
        {
            result = AcquiredAccessTokenResult.Failure(
                AcquiredAccessTokenStatus.Canceled,
                "CredentialAcquisitionCanceled",
                "Credential acquisition was canceled.");
        }
        else if (CredentialRequestV2Policy.GetViolation(request) is not null)
        {
            result = AcquiredAccessTokenResult.Failure(
                AcquiredAccessTokenStatus.RequestRejected,
                "DirectMsalRequestRejected",
                "Direct MSAL rejected the credential request.");
        }
        else if (request.AcquisitionMode == AcquisitionMode.SilentOnly)
        {
            result = AcquiredAccessTokenResult.Failure(
                AcquiredAccessTokenStatus.InteractionRequired,
                "SilentAcquisitionUnavailable",
                "Silent credential acquisition is not implemented; use explicit interactive "
                    + "login for interactive operations only. No automatic remediation is available.");
        }
        else
        {
            result = AcquiredAccessTokenResult.Failure(
                AcquiredAccessTokenStatus.PrerequisiteFailed,
                "DirectMsalNotImplemented",
                "Direct MSAL is selected but its production provider is not implemented.");
        }

        return ValueTask.FromResult(result);
    }
}

internal sealed class PrerequisiteUnavailableAccessTokenProvider(
    string code,
    string safeMessage)
    : IAccessTokenIdentityProvider
{
    public ValueTask<AcquiredAccessTokenResult> AcquireAccessTokenAsync(
        CredentialRequestV2 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        AcquiredAccessTokenResult result = cancellationToken.IsCancellationRequested
            ? AcquiredAccessTokenResult.Failure(
                AcquiredAccessTokenStatus.Canceled,
                "CredentialAcquisitionCanceled",
                "Credential acquisition was canceled.")
            : request.AcquisitionMode == AcquisitionMode.SilentOnly
                ? AcquiredAccessTokenResult.Failure(
                    AcquiredAccessTokenStatus.InteractionRequired,
                    "SilentAcquisitionUnavailable",
                    "Silent AzureAuth acquisition is not implemented; use explicit interactive "
                        + "login for interactive operations only. No automatic remediation is available.")
                : AcquiredAccessTokenResult.Failure(
                    AcquiredAccessTokenStatus.PrerequisiteFailed,
                    code,
                    safeMessage);
        return ValueTask.FromResult(result);
    }
}

internal sealed record AzureAuthProductionPrerequisiteFailure(
    AcquiredAccessTokenStatus Status,
    string Code,
    string SafeMessage);

internal static class AzureAuthProductionPrerequisitePolicy
{
    internal static AzureAuthProductionPrerequisiteFailure? Evaluate(
        AzureAuthProviderConfig config,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        IAzureAuthArtifactTrustInspector inspector,
        CredentialRequestV2? request = null)
    {
        return EvaluateWithTrust(
            config,
            bindingRecord,
            inspector,
            out _,
            request,
            CancellationToken.None);
    }

    internal static AzureAuthProductionPrerequisiteFailure? EvaluateWithTrust(
        AzureAuthProviderConfig config,
        AzureAuthPersistedRecord<AzureAuthBinding> bindingRecord,
        IAzureAuthArtifactTrustInspector inspector,
        out AzureAuthTrustResult trust,
        CredentialRequestV2? request = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        trust = AzureAuthTrustResult.Unspecified();
        if (config.Selection != AzureAuthProviderSelection.AzureAuth
            || config.DeploymentConfig is null)
        {
            return Failure(
                "AzureAuthProviderSelectionMismatch",
                "AzureAuth is not the selected provider for this binding.");
        }

        trust = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig,
            inspector,
            cancellationToken);
        if (!trust.IsReady || string.IsNullOrWhiteSpace(trust.DeploymentKey))
        {
            return trust.Status == AzureAuthArtifactTrustStatus.Deferred
                ? Failure(
                    "AzureAuthTrustDeferred",
                    trust.SafeDetail
                        ?? "AzureAuth trust inspection is deferred.")
                : Failure(
                    "AzureAuthTrustRejected",
                    trust.SafeDetail
                        ?? "AzureAuth trust inspection rejected the configured deployment.");
        }

        if (bindingRecord.Status != AzureAuthPersistedRecordStatus.Present
            || bindingRecord.Value?.State != AzureAuthBindingState.Bound)
        {
            return Failure(
                "AzureAuthBindingRequired",
                "AzureAuth account binding is required.");
        }

        AzureAuthBinding binding = bindingRecord.Value;
        if (binding.ProviderSelection != config.Selection)
        {
            return Failure(
                "AzureAuthBindingProviderMismatch",
                "AzureAuth binding does not match the selected provider.");
        }

        if (!string.Equals(
                binding.DeploymentKey,
                trust.DeploymentKey,
                StringComparison.Ordinal))
        {
            return Failure(
                "AzureAuthBindingDeploymentMismatch",
                "AzureAuth binding does not match the trusted deployment.");
        }

        if (request is null)
        {
            return null;
        }

        string? normalizedAccount = NormalizeHint(request.AccountHint);
        string? normalizedTenant = NormalizeHint(request.TenantHint);
        if ((request.AccountHint is not null && normalizedAccount is null)
            || (request.TenantHint is not null && normalizedTenant is null))
        {
            return new AzureAuthProductionPrerequisiteFailure(
                AcquiredAccessTokenStatus.RequestRejected,
                "AzureAuthRequestRejected",
                "AzureAuth rejected the credential request.");
        }

        if (normalizedAccount is not null
            && !string.Equals(normalizedAccount, binding.AccountId, StringComparison.Ordinal))
        {
            return Failure(
                "AzureAuthBindingAccountMismatch",
                "AzureAuth account hint does not match the current binding.");
        }

        if (normalizedTenant is not null
            && !string.Equals(normalizedTenant, binding.TenantId, StringComparison.Ordinal))
        {
            return Failure(
                "AzureAuthBindingTenantMismatch",
                "AzureAuth tenant hint does not match the current binding.");
        }

        return null;
    }

    private static string? NormalizeHint(string? hint)
    {
        if (hint is null)
        {
            return null;
        }

        try
        {
            return AzureAuthBindingPolicy.NormalizeObservedIdentifier(hint, nameof(hint));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static AzureAuthProductionPrerequisiteFailure Failure(
        string code,
        string safeMessage) =>
        new(AcquiredAccessTokenStatus.PrerequisiteFailed, code, safeMessage);
}

internal sealed class LegacyV1CredentialAcquisitionService(CredentialCoreService credentialCore)
    : ICredentialAcquisitionService
{
    public ValueTask<CredentialResult> AcquireAsync(
        CredentialRequestV2 request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(
                CredentialAcquisitionResultFactory.Failure(
                    CredentialResultStatus.CredentialUnavailable,
                    CredentialErrorKind.CredentialUnavailable,
                    "CredentialAcquisitionCanceled",
                    "Credential acquisition was canceled."));
        }

        return ValueTask.FromResult(
            credentialCore.Execute(
                new CredentialRequest
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
                    CachePolicy = request.CachePolicy,
                    CiContext = request.CiContext,
                    ExtensionData = request.ExtensionData,
                }));
    }
}

internal static class CredentialAcquisitionResultFactory
{
    internal static CredentialResult Failure(
        CredentialResultStatus status,
        CredentialErrorKind kind,
        string code,
        string safeMessage) =>
        new()
        {
            Status = status,
            DiagnosticsCorrelationId = CorrelationId.New().ToString(),
            Error = new CredentialError
            {
                Kind = kind,
                Code = code,
                SafeMessage = safeMessage,
            },
        };
}
