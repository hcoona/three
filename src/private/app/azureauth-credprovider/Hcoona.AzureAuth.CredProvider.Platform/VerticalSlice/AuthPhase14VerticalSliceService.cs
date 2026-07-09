using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

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
        CredentialResult credentialResult = credentialCoreService.Execute(credentialRequest);
        return new AuthPhase14LoginResult
        {
            CredentialResult = credentialResult,
            IdentityFlow = request.IdentityFlow,
            PersistentDerivedCredentialsStored = false,
        };
    }

    public static AuthPhase14LogoutResult Logout() =>
        new() { PersistentDerivedCredentialsRemoved = false };

    private void ValidateLoginRequest(AuthPhase14LoginRequest request)
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

        if (
            request.IdentityFlow == IdentityFlow.PatCompatibility
            && !request.ExplicitPatMaterialProvided
        )
        {
            throw new InvalidOperationException(
                "PAT compatibility login requires an explicit --pat value and does not persist it."
            );
        }

        if (request.IdentityFlow != IdentityFlow.AzurePipelinesSystemAccessToken)
        {
            return;
        }

        if (!request.ExplicitAzurePipelinesCiMode)
        {
            throw new InvalidOperationException(
                "Azure Pipelines system access token login requires explicit --ci azure-pipelines."
            );
        }

        if (string.IsNullOrWhiteSpace(environmentVariableReader(
                AzurePipelinesSystemAccessTokenVariable
            )))
        {
            throw new InvalidOperationException(
                "Azure Pipelines system access token is unavailable in the environment."
            );
        }
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
                    ExplicitCiMode = true,
                    Provider = CiProviderNames.AzurePipelines,
                    HasAzurePipelinesSystemAccessToken = true,
                    AllowsPersistentWrites = false,
                }
                : null,
        };
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
