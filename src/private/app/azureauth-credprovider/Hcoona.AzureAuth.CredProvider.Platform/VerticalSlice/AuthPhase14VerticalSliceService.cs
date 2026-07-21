using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.AzurePipelines;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;
using Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record AuthPhase14VerticalSliceOptions
{
    public CredentialCoreService? CredentialCoreService { get; init; }

    public Func<string, string?>? EnvironmentVariableReader { get; init; }
}

public sealed record AuthPhase14LoginRequest
{
    public required IdentityFlow IdentityFlow { get; init; }

    public string? AccountHint { get; init; }

    public string? TenantHint { get; init; }

    public bool ExplicitPatMaterialProvided { get; init; }

    public bool ExplicitAzurePipelinesCiMode { get; init; }
}

public sealed record AuthPhase14LoginResult
{
    public required CredentialResult CredentialResult { get; init; }

    public required IdentityFlow IdentityFlow { get; init; }

    public required bool PersistentDerivedCredentialsStored { get; init; }
}

public sealed record AuthPhase14LogoutResult
{
    public required bool PersistentDerivedCredentialsRemoved { get; init; }
}

public sealed class AuthPhase14VerticalSliceService
{
    public const string AzurePipelinesSystemAccessTokenVariable = "SYSTEM_ACCESSTOKEN";

    private static readonly Uri DefaultServiceEndpoint = new("https://dev.azure.com/phase14");

    private readonly CredentialCoreService credentialCoreService;
    private readonly Func<string, string?> environmentVariableReader;

    public AuthPhase14VerticalSliceService(AuthPhase14VerticalSliceOptions? options = null)
    {
        credentialCoreService = options?.CredentialCoreService ?? new CredentialCoreService();
        environmentVariableReader =
            options?.EnvironmentVariableReader ?? Environment.GetEnvironmentVariable;
    }

    public AuthPhase14LoginResult Login(AuthPhase14LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateLoginRequest(request);

        CredentialRequest credentialRequest = CreateCredentialRequest(request);
        CredentialResult credentialResult = ExecuteCredentialRequest(credentialRequest);
        return new AuthPhase14LoginResult
        {
            CredentialResult = credentialResult,
            IdentityFlow = request.IdentityFlow,
            PersistentDerivedCredentialsStored = false,
        };
    }

    internal CredentialResult ExecuteCredentialRequest(CredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (CredentialCoreService.TryGetProtocolViolation(
                request,
                out string? protocolViolation))
        {
            return CreateProtocolViolationResult(protocolViolation);
        }

        return request.IdentityFlow switch
        {
            IdentityFlow.AzurePipelinesSystemAccessToken =>
                AzurePipelinesSystemAccessTokenService.Handle(
                        request,
                        environmentVariableReader(AzurePipelinesSystemAccessTokenVariable))
                    .CreateProtocolResult("wp5-azure-pipelines-system-access-token"),
            IdentityFlow.PatCompatibility => CreatePatDeferredResult(request),
            _ => credentialCoreService.Execute(request),
        };
    }

    public static AuthPhase14LogoutResult Logout() =>
        new() { PersistentDerivedCredentialsRemoved = false };

    private static void ValidateLoginRequest(AuthPhase14LoginRequest request)
    {
        IdentityFlowState state = IdentityFlowPolicy.GetMvpState(request.IdentityFlow);
        if (state == IdentityFlowState.Deferred)
        {
            throw new NotSupportedException("Requested identity flow is deferred for MVP.");
        }

        if (state != IdentityFlowState.AcceptedMvp)
        {
            throw new NotSupportedException("Requested identity flow is not supported for MVP.");
        }

        _ = request.ExplicitPatMaterialProvided;
    }

    private static CredentialRequest CreateCredentialRequest(AuthPhase14LoginRequest request)
    {
        bool azurePipelines =
            request.IdentityFlow == IdentityFlow.AzurePipelinesSystemAccessToken;
        return new CredentialRequest
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "phase14",
                DefaultServiceEndpoint
            ),
            ServiceIdentity = "default",
            AccountHint = NullIfWhiteSpace(request.AccountHint),
            TenantHint = NullIfWhiteSpace(request.TenantHint),
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = request.IdentityFlow == IdentityFlow.PatCompatibility
                ? CredentialKind.PatCompatibility
                : CredentialKind.BearerToken,
            IdentityFlow = request.IdentityFlow,
            InteractivePolicy = azurePipelines
                ? InteractivePolicy.Never
                : InteractivePolicy.UserAllowed,
            CachePolicy = azurePipelines
                ? CachePolicyMode.NonPersistentCi
                : CachePolicyMode.ProductPersistentCacheDisabled,
            CiContext = azurePipelines
                ? new CiContext
                {
                    ExplicitCiMode = request.ExplicitAzurePipelinesCiMode,
                    Provider = CiProviderNames.AzurePipelines,
                    HasAzurePipelinesSystemAccessToken = true,
                    AllowsPersistentWrites = false,
                }
                : null,
        };
    }

    private static CredentialResult CreatePatDeferredResult(CredentialRequest request)
    {
        PatCompatibilityPolicyDecision decision = PatCompatibilityPolicy.Evaluate(request);
        return new CredentialResult
        {
            Status = CredentialResultStatus.FlowDeferred,
            DiagnosticsCorrelationId = "wp5-pat-compatibility-deferred",
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.FlowDeferred,
                Code = decision.Code,
                SafeMessage = decision.SafeMessage,
            },
        };
    }

    private static CredentialResult CreateProtocolViolationResult(string safeMessage) =>
        new()
        {
            Status = CredentialResultStatus.ProtocolViolation,
            DiagnosticsCorrelationId = "wp5-credential-request-protocol-violation",
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.ProtocolViolation,
                Code = "ProtocolViolation",
                SafeMessage = safeMessage,
            },
        };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
