using System.Diagnostics.CodeAnalysis;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public sealed class CredentialCoreService
{
    private const string AzureDevOpsUsername = "AzureDevOps";
    private const string CacheUnavailableCode = "CacheUnavailable";
    private const string SuccessCode = "CredentialIssued";
    private const string FatalCode = "CredentialCoreFailure";
    private const string OperationNotSupportedCode = "OperationNotSupported";
    private const string ProtocolViolationCode = "ProtocolViolation";
    private const string TokenExchangeFailedCode = "TokenExchangeFailed";
    private const string TokenExchangeUnavailableCode = "TokenExchangeUnavailable";
    private const string OpaqueAzurePipelinesTokenCode =
        "AzurePipelinesSystemAccessTokenDedicatedServiceRequired";
    private const string PatCompatibilityDeferredCode = "PatCompatibilityDeferred";

    private readonly DiagnosticRouter? _diagnosticRouter;
    private readonly IDerivedCredentialCache _derivedCredentialCache;
    private readonly IIdentityProvider _identityProvider;
    private readonly ITokenExchange _tokenExchange;

    public CredentialCoreService()
        : this(new DirectMsalIdentityProvider())
    { }

    public CredentialCoreService(
        IIdentityProvider identityProvider,
        DiagnosticRouter? diagnosticRouter = null,
        IDerivedCredentialCache? derivedCredentialCache = null)
        : this(identityProvider, diagnosticRouter, derivedCredentialCache, tokenExchange: null)
    { }

    internal CredentialCoreService(
        IIdentityProvider identityProvider,
        DiagnosticRouter? diagnosticRouter,
        IDerivedCredentialCache? derivedCredentialCache,
        ITokenExchange? tokenExchange)
    {
        ArgumentNullException.ThrowIfNull(identityProvider);

        _identityProvider = identityProvider;
        _diagnosticRouter = diagnosticRouter;
        _derivedCredentialCache =
            derivedCredentialCache ?? new NoPersistentDerivedCredentialCache();
        _tokenExchange = tokenExchange ?? new IdentityMaterialTokenExchange();
    }

    public CredentialResult Execute(CredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        CorrelationId correlationId = CorrelationId.New();

        if (TryGetProtocolViolation(request, out string? protocolViolation))
        {
            return CreateFailureResult(
                request,
                correlationId,
                CredentialResultStatus.ProtocolViolation,
                CredentialErrorKind.ProtocolViolation,
                ProtocolViolationCode,
                protocolViolation);
        }

        if (request.IdentityFlow == IdentityFlow.AzurePipelinesSystemAccessToken)
        {
            return CreateFailureResult(
                request,
                correlationId,
                CredentialResultStatus.CredentialUnavailable,
                CredentialErrorKind.CredentialUnavailable,
                OpaqueAzurePipelinesTokenCode,
                "Azure Pipelines system access tokens require the dedicated opaque credential service.");
        }

        if (request.IdentityFlow == IdentityFlow.PatCompatibility)
        {
            return CreateFailureResult(
                request,
                correlationId,
                CredentialResultStatus.FlowDeferred,
                CredentialErrorKind.FlowDeferred,
                PatCompatibilityDeferredCode,
                "PAT compatibility is deferred and has no production acquisition or materialization path.",
                IdentityFlowState.Deferred);
        }

        if (request.Operation != CredentialOperation.Get)
        {
            return CreateFailureResult(
                request,
                correlationId,
                CredentialResultStatus.CredentialUnavailable,
                CredentialErrorKind.CredentialUnavailable,
                OperationNotSupportedCode,
                "Credential core scaffold only supports get operations.");
        }

        IdentityFlowState flowState = IdentityFlowPolicy.GetMvpState(request.IdentityFlow);
        if (flowState != IdentityFlowState.AcceptedMvp)
        {
            return flowState switch
            {
                IdentityFlowState.Deferred => CreateFailureResult(
                    request,
                    correlationId,
                    CredentialResultStatus.FlowDeferred,
                    CredentialErrorKind.FlowDeferred,
                    "FlowDeferred",
                    "Requested identity flow is deferred by the MVP scaffold.",
                    flowState),
                IdentityFlowState.Disabled => CreateFailureResult(
                    request,
                    correlationId,
                    CredentialResultStatus.FlowDisabled,
                    CredentialErrorKind.FlowDisabled,
                    "FlowDisabled",
                    GetCredentialCoreFallbackMessage("FlowDisabled"),
                    flowState),
                _ => CreateFailureResult(
                    request,
                    correlationId,
                    CredentialResultStatus.UnsupportedFlow,
                    CredentialErrorKind.UnsupportedFlow,
                    "UnsupportedFlow",
                    GetCredentialCoreFallbackMessage("UnsupportedFlow"),
                    flowState),
            };
        }

        if (
            TryGetPersistentCacheFailureResult(
                request,
                correlationId,
                out CredentialResult? cacheFailureResult)
        )
        {
            return cacheFailureResult;
        }

        if (!IdentityFlowPolicy.IsAcceptedMvpRequest(request))
        {
            if (IsInteractionBlockedRequest(request))
            {
                return CreateFailureResult(
                    request,
                    correlationId,
                    CredentialResultStatus.InteractionBlocked,
                    CredentialErrorKind.InteractionBlocked,
                    "InteractionBlocked",
                    "Credential request requires interaction, but interaction is blocked by "
                        + "policy.",
                    flowState);
            }

            return CreateFailureResult(
                request,
                correlationId,
                CredentialResultStatus.FlowDisabled,
                CredentialErrorKind.FlowDisabled,
                "FlowDisabled",
                GetCredentialCoreFallbackMessage("FlowDisabled"),
                flowState);
        }

        try
        {
            IdentityMaterial identity = NormalizeAndEnsureValid(
                _identityProvider.GetIdentity(request),
                request.CredentialKind
            );

            CacheKey cacheKey = CacheKeySchema.Create(
                request,
                identity.Account,
                identity.Tenant);
            TokenExchangeResult exchangeResult = NormalizeTokenExchangeResult(
                _tokenExchange.Exchange(request, identity, cacheKey));

            if (exchangeResult.Status == TokenExchangeStatus.Unavailable)
            {
                return CreateFailureResult(
                    request,
                    correlationId,
                    CredentialResultStatus.CredentialUnavailable,
                    CredentialErrorKind.CredentialUnavailable,
                    TokenExchangeUnavailableCode,
                    "Credential token exchange is unavailable.");
            }

            if (exchangeResult.Status != TokenExchangeStatus.Success)
            {
                return CreateFailureResult(
                    request,
                    correlationId,
                    CredentialResultStatus.Fatal,
                    CredentialErrorKind.Fatal,
                    TokenExchangeFailedCode,
                    "Credential token exchange failed.");
            }

            TokenExchangeMaterial exchangeMaterial = NormalizeAndEnsureValid(
                exchangeResult.Material
                    ?? throw new InvalidOperationException(
                        "Token exchange returned incomplete credential output material."),
                request.CredentialKind);
            CredentialResult result = CreateSuccessResult(
                request,
                correlationId,
                identity,
                cacheKey,
                exchangeMaterial
            );

            WriteSafeDiagnostic(
                DiagnosticSeverity.Information,
                correlationId,
                "Credential request succeeded.",
                new Dictionary<string, string?>
                {
                    ["code"] = SuccessCode,
                    ["status"] = result.Status.ToString(),
                    ["ecosystem"] = request.Ecosystem.ToString(),
                    ["credentialKind"] = request.CredentialKind.ToString(),
                    ["identityFlow"] = request.IdentityFlow.ToString(),
                });

            return result;
        }
        catch (DirectMsalIdentityProviderUnavailableException exception)
        {
            return CreateFailureResult(
                request,
                correlationId,
                CredentialResultStatus.CredentialUnavailable,
                CredentialErrorKind.CredentialUnavailable,
                exception.Code,
                exception.SafeMessage);
        }
        catch (Exception)
        {
            return CreateFailureResult(
                request,
                correlationId,
                CredentialResultStatus.Fatal,
                CredentialErrorKind.Fatal,
                FatalCode,
                "Credential core execution failed.");
        }
    }

    private bool TryGetPersistentCacheFailureResult(
        CredentialRequest request,
        CorrelationId correlationId,
        [NotNullWhen(true)]
        out CredentialResult? result)
    {
        if (!RequiresPersistentDerivedCredentialCache(request))
        {
            result = null;
            return false;
        }

        DerivedCredentialCacheAvailabilityStatus availabilityStatus;

        try
        {
            availabilityStatus = NormalizeCacheAvailabilityStatus(
                _derivedCredentialCache.GetPersistentAvailability(request));
        }
        catch (Exception)
        {
            availabilityStatus = DerivedCredentialCacheAvailabilityStatus.Unavailable;
        }

        if (availabilityStatus == DerivedCredentialCacheAvailabilityStatus.Available)
        {
            result = null;
            return false;
        }

        result = CreateCacheUnavailableResult(request, correlationId, availabilityStatus);
        return true;
    }

    private static CredentialResult CreateSuccessResult(
        CredentialRequest request,
        CorrelationId correlationId,
        IdentityMaterial identity,
        CacheKey cacheKey,
        TokenExchangeMaterial exchangeMaterial)
    {
        _ = request;

        return new CredentialResult
        {
            Status = CredentialResultStatus.Success,
            Username = exchangeMaterial.Username,
            Password = exchangeMaterial.Password,
            BearerToken = exchangeMaterial.BearerToken,
            ExpiresAt = identity.ExpiresAt,
            Account = identity.Account,
            Tenant = identity.Tenant,
            CacheKey = cacheKey,
            DiagnosticsCorrelationId = correlationId.ToString(),
        };
    }

    private CredentialResult CreateCacheUnavailableResult(
        CredentialRequest request,
        CorrelationId correlationId,
        DerivedCredentialCacheAvailabilityStatus availabilityStatus)
    {
        const string safeMessage = "Persistent derived credential cache is unavailable.";
        Dictionary<string, string> safeDetails = CreateSafeDetails(
            request,
            CredentialResultStatus.CacheUnavailable,
            flowState: null);
        safeDetails["cachePolicy"] = request.CachePolicy.ToString();
        safeDetails["cacheAvailability"] = availabilityStatus.ToString();

        Dictionary<string, string?> properties = CreateDiagnosticProperties(
            CacheUnavailableCode,
            request,
            CredentialResultStatus.CacheUnavailable,
            flowState: null);
        properties["cachePolicy"] = request.CachePolicy.ToString();
        properties["cacheAvailability"] = availabilityStatus.ToString();

        WriteSafeDiagnostic(
            DiagnosticSeverity.Warning,
            correlationId,
            safeMessage,
            properties);

        return new CredentialResult
        {
            Status = CredentialResultStatus.CacheUnavailable,
            DiagnosticsCorrelationId = correlationId.ToString(),
            Error = new CredentialError
            {
                Kind = CredentialErrorKind.CacheUnavailable,
                Code = CacheUnavailableCode,
                SafeMessage = safeMessage,
                SafeDetails = safeDetails,
            },
        };
    }

    private CredentialResult CreateFailureResult(
        CredentialRequest request,
        CorrelationId correlationId,
        CredentialResultStatus status,
        CredentialErrorKind errorKind,
        string code,
        string safeMessage,
        IdentityFlowState? flowState = null)
    {
        Dictionary<string, string> safeDetails = CreateSafeDetails(request, status, flowState);

        WriteSafeDiagnostic(
            status == CredentialResultStatus.Fatal
                ? DiagnosticSeverity.Error
                : DiagnosticSeverity.Warning,
            correlationId,
            safeMessage,
            CreateDiagnosticProperties(code, request, status, flowState));

        return new CredentialResult
        {
            Status = status,
            DiagnosticsCorrelationId = correlationId.ToString(),
            Error = new CredentialError
            {
                Kind = errorKind,
                Code = code,
                SafeMessage = safeMessage,
                SafeDetails = safeDetails,
            },
        };
    }

    private void WriteSafeDiagnostic(
        DiagnosticSeverity severity,
        CorrelationId correlationId,
        string message,
        IReadOnlyDictionary<string, string?> properties)
    {
        if (_diagnosticRouter is null)
        {
            return;
        }

        DiagnosticCommitTrackingScope? capturedCommitTrackingScope =
            _diagnosticRouter.CaptureActiveCommitTrackingScope();
        bool restoreCapturedCommitTrackingScope = false;

        if (
            capturedCommitTrackingScope is not null
            && (
                capturedCommitTrackingScope.OutputCommitted
                || capturedCommitTrackingScope.SuppressesLateCredentialCoreRecovery
                || capturedCommitTrackingScope
                    .SuppressesDirectCredentialCoreSafeDiagnosticRoutes
            )
        )
        {
            return;
        }

        if (capturedCommitTrackingScope?.IsClosed == true)
        {
            _diagnosticRouter.PruneClosedActiveCommitTrackingScope();
            restoreCapturedCommitTrackingScope = true;
        }

        try
        {
            try
            {
                _diagnosticRouter.Route(
                    new DiagnosticEvent(
                        severity,
                        DiagnosticChannel.Diagnostic,
                        message,
                        correlationId,
                        properties,
                        isSafeDiagnosticEnvelope: true)
                    {
                        AllowCodeSpecificFallback = true,
                        FallbackScope = SafeDiagnosticFallbackScope.CredentialCore,
                    });
            }
            finally
            {
                if (restoreCapturedCommitTrackingScope)
                {
                    _diagnosticRouter.RestoreCapturedActiveCommitTrackingScope(
                        capturedCommitTrackingScope);
                }
            }
        }
        catch (Exception)
        {
            // Credential core diagnostics are best-effort and must not alter the returned result.
        }
    }

    private static Dictionary<string, string> CreateSafeDetails(
        CredentialRequest request,
        CredentialResultStatus status,
        IdentityFlowState? flowState)
    {
        var safeDetails = new Dictionary<string, string>
        {
            ["status"] = status.ToString(),
            ["operation"] = request.Operation.ToString(),
            ["ecosystem"] = request.Ecosystem.ToString(),
            ["credentialKind"] = request.CredentialKind.ToString(),
            ["identityFlow"] = request.IdentityFlow.ToString(),
        };

        if (flowState is not null)
        {
            safeDetails["flowState"] = flowState.Value.ToString();
        }

        return safeDetails;
    }

    private static Dictionary<string, string?> CreateDiagnosticProperties(
        string code,
        CredentialRequest request,
        CredentialResultStatus status,
        IdentityFlowState? flowState)
    {
        var properties = new Dictionary<string, string?>
        {
            ["code"] = code,
            ["status"] = status.ToString(),
            ["operation"] = request.Operation.ToString(),
            ["ecosystem"] = request.Ecosystem.ToString(),
            ["credentialKind"] = request.CredentialKind.ToString(),
            ["identityFlow"] = request.IdentityFlow.ToString(),
        };

        if (flowState is not null)
        {
            properties["flowState"] = flowState.Value.ToString();
        }

        return properties;
    }

    private static string GetCredentialCoreFallbackMessage(string code)
    {
        return SafeDiagnosticMessageFallback.GetDefaultMessage(
            SafeDiagnosticFallbackScope.CredentialCore,
            code);
    }

    private static IdentityMaterial NormalizeAndEnsureValid(
        IdentityMaterial identity,
        CredentialKind credentialKind)
    {
        ArgumentNullException.ThrowIfNull(identity);

        if (string.IsNullOrWhiteSpace(identity.Account)
            || string.IsNullOrWhiteSpace(identity.Tenant)
            || (RequiresSecret(credentialKind) && string.IsNullOrWhiteSpace(identity.Secret))
            || (RequiresAccessToken(credentialKind)
                && string.IsNullOrWhiteSpace(identity.AccessToken))
            || identity.ExpiresAt == default)
        {
            throw new InvalidOperationException(
                "Identity provider returned incomplete credential core material.");
        }

        if (ContainsAdapterProtocolLineBreak(identity.Account)
            || ContainsAdapterProtocolLineBreak(identity.Tenant)
            || (RequiresSecret(credentialKind) && ContainsControlCharacters(identity.Secret))
            || (RequiresAccessToken(credentialKind)
                && ContainsControlCharacters(identity.AccessToken)))
        {
            throw new InvalidOperationException(
                "Identity provider returned protocol-incompatible credential core material.");
        }

        return identity with
        {
            Account = CanonicalizeIdentityPartition(identity.Account),
            Tenant = CanonicalizeIdentityPartition(identity.Tenant),
        };
    }

    private static TokenExchangeMaterial NormalizeAndEnsureValid(
        TokenExchangeMaterial material,
        CredentialKind credentialKind)
    {
        ArgumentNullException.ThrowIfNull(material);

        bool requiresSecret = RequiresSecret(credentialKind);
        bool requiresAccessToken = RequiresAccessToken(credentialKind);

        if ((requiresSecret
                && (string.IsNullOrWhiteSpace(material.Username)
                    || string.IsNullOrWhiteSpace(material.Password)))
            || (requiresAccessToken && string.IsNullOrWhiteSpace(material.BearerToken))
            || (!requiresSecret
                && (material.Username is not null || material.Password is not null))
            || (!requiresAccessToken && material.BearerToken is not null))
        {
            throw new InvalidOperationException(
                "Token exchange returned incomplete credential output material.");
        }

        if (ContainsControlCharacters(material.Username)
            || ContainsControlCharacters(material.Password)
            || ContainsControlCharacters(material.BearerToken))
        {
            throw new InvalidOperationException(
                "Token exchange returned protocol-incompatible credential output material.");
        }

        return material with
        {
            Username = requiresSecret ? AzureDevOpsUsername : null,
            Password = requiresSecret ? material.Password : null,
            BearerToken = requiresAccessToken ? material.BearerToken : null,
        };
    }

    private static TokenExchangeResult NormalizeTokenExchangeResult(TokenExchangeResult result) =>
        result.Status switch
        {
            TokenExchangeStatus.Success when result.Material is not null => result,
            TokenExchangeStatus.Unavailable => TokenExchangeResult.Unavailable,
            TokenExchangeStatus.Failed => TokenExchangeResult.Failed,
            _ => TokenExchangeResult.Failed,
        };

    private static string CanonicalizeIdentityPartition(string value) =>
        value.Trim().ToLowerInvariant();

    private static bool RequiresSecret(CredentialKind credentialKind) =>
        credentialKind
            is CredentialKind.BasicPassword
                or CredentialKind.NuGetPluginCredential
                or CredentialKind.PatCompatibility;

    private static bool RequiresAccessToken(CredentialKind credentialKind) =>
        credentialKind is CredentialKind.BearerToken or CredentialKind.NpmAuthToken;

    private static bool ContainsControlCharacters(string? value) =>
        value is not null && value.Any(char.IsControl);

    private static bool ContainsAdapterProtocolLineBreak(string? value) =>
        value is not null && value.AsSpan().IndexOfAny('\r', '\n') >= 0;

    private static bool RequiresPersistentDerivedCredentialCache(CredentialRequest request) =>
        request.CachePolicy == CachePolicyMode.FuturePersistentCacheRequested
        && IdentityFlowPolicy.IsAcceptedMvpRequest(
            request with
            {
                CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
            });

    private static DerivedCredentialCacheAvailabilityStatus NormalizeCacheAvailabilityStatus(
        DerivedCredentialCacheAvailability availability) =>
        availability.Status switch
        {
            DerivedCredentialCacheAvailabilityStatus.Available =>
                DerivedCredentialCacheAvailabilityStatus.Available,
            DerivedCredentialCacheAvailabilityStatus.Unavailable =>
                DerivedCredentialCacheAvailabilityStatus.Unavailable,
            DerivedCredentialCacheAvailabilityStatus.Denied =>
                DerivedCredentialCacheAvailabilityStatus.Denied,
            DerivedCredentialCacheAvailabilityStatus.Unsupported =>
                DerivedCredentialCacheAvailabilityStatus.Unsupported,
            DerivedCredentialCacheAvailabilityStatus.VerificationFailed =>
                DerivedCredentialCacheAvailabilityStatus.VerificationFailed,
            _ => DerivedCredentialCacheAvailabilityStatus.VerificationFailed,
        };

    private static bool IsInteractionBlockedRequest(CredentialRequest request) =>
        request.IdentityFlow is IdentityFlow.InteractiveBrowser or IdentityFlow.DeviceCode
        && request.InteractivePolicy == InteractivePolicy.Never
        && IdentityFlowPolicy.IsAcceptedMvpRequest(
            request with
            {
                InteractivePolicy = InteractivePolicy.HostToolAllows,
            });

    internal static bool TryGetProtocolViolation(
        CredentialRequest request,
        [NotNullWhen(true)]
        out string? protocolViolation)
    {
        if (request.ContractMajor != ContractVersions.CredentialContractMajor)
        {
            protocolViolation = "Protocol violation: credential request contract major must be 1.";
            return true;
        }

        if (!HasSpecifiedDefinedRequiredRequestEnums(request))
        {
            protocolViolation =
                "Protocol violation: credential request contains an unspecified or unknown "
                + "required enum value.";
            return true;
        }

        if (request.Resource is null)
        {
            protocolViolation = "Protocol violation: canonical resource identity is required.";
            return true;
        }

        if (!ServiceIdentityContract.IsCanonical(request.ServiceIdentity))
        {
            protocolViolation =
                "Protocol violation: service identity must use canonical lower-case form.";
            return true;
        }

        if (ContainsControlCharacters(request.AccountHint))
        {
            protocolViolation =
                "Protocol violation: account hint must not contain control characters.";
            return true;
        }

        if (ContainsControlCharacters(request.TenantHint))
        {
            protocolViolation =
                "Protocol violation: tenant hint must not contain control characters.";
            return true;
        }

        protocolViolation = CanonicalResourceIdentityPolicy.GetViolation(request.Resource);
        if (protocolViolation is not null)
        {
            return true;
        }

        if (!IsResourceShapeAllowed(request))
        {
            protocolViolation =
                "Protocol violation: credential request resource shape must match the selected "
                + "ecosystem, audience, and credential kind.";
            return true;
        }

        if (!IsFlowCredentialShapeAllowed(request))
        {
            protocolViolation = request.IdentityFlow == IdentityFlow.AzurePipelinesSystemAccessToken
                && request.Ecosystem == CredentialEcosystem.Git
                ? "Protocol violation: Azure Pipelines system access token git requests must use "
                    + "bearer-token credentials."
                : "Protocol violation: PAT compatibility requests must pair the patCompatibility "
                    + "flow and credential kind.";
            return true;
        }

        protocolViolation = null;
        return false;
    }

    private static bool HasSpecifiedDefinedRequiredRequestEnums(CredentialRequest request) =>
        IsSpecifiedDefinedEnum(request.Ecosystem)
        && IsSpecifiedDefinedEnum(request.Operation)
        && IsSpecifiedDefinedEnum(request.RequestedAudience)
        && IsSpecifiedDefinedEnum(request.CredentialKind)
        && IsSpecifiedDefinedEnum(request.IdentityFlow)
        && IsSpecifiedDefinedEnum(request.InteractivePolicy)
        && IsSpecifiedDefinedEnum(request.CachePolicy);

    private static bool IsSpecifiedDefinedEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Enum.IsDefined(value)
            && !EqualityComparer<TEnum>.Default.Equals(value, default);
    }

    private static bool IsResourceShapeAllowed(CredentialRequest request) =>
        request.Ecosystem switch
        {
            CredentialEcosystem.Git => string.IsNullOrWhiteSpace(request.Resource.Feed)
                && request.RequestedAudience == TokenAudience.AzureDevOps
                && request.CredentialKind
                    is CredentialKind.BasicPassword
                        or CredentialKind.BearerToken
                        or CredentialKind.PatCompatibility
                && CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                    request.Resource.ServiceEndpoint,
                    request.Ecosystem),
            CredentialEcosystem.NuGet => IsPackageResourceShapeAllowed(
                request,
                CredentialKind.NuGetPluginCredential),
            CredentialEcosystem.Python => IsPackageResourceShapeAllowed(
                request,
                CredentialKind.BasicPassword),
            CredentialEcosystem.Npm or CredentialEcosystem.Pnpm or CredentialEcosystem.Yarn =>
                IsPackageResourceShapeAllowed(request, CredentialKind.NpmAuthToken),
            _ => false,
        };

    private static bool IsPackageResourceShapeAllowed(
        CredentialRequest request,
        CredentialKind expectedCredentialKind) =>
        !string.IsNullOrWhiteSpace(request.Resource.Feed)
        && string.IsNullOrWhiteSpace(request.Resource.Repository)
        && request.RequestedAudience == TokenAudience.AzureArtifacts
        && request.CredentialKind == expectedCredentialKind
        && CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
            request.Resource.ServiceEndpoint,
            request.Ecosystem);

    private static bool IsFlowCredentialShapeAllowed(CredentialRequest request) =>
        (request.IdentityFlow != IdentityFlow.PatCompatibility
            || request.CredentialKind == CredentialKind.PatCompatibility)
        && (request.IdentityFlow == IdentityFlow.PatCompatibility
            || request.CredentialKind != CredentialKind.PatCompatibility)
        && (request.IdentityFlow != IdentityFlow.AzurePipelinesSystemAccessToken
            || request.Ecosystem != CredentialEcosystem.Git
            || request.CredentialKind == CredentialKind.BearerToken);
}
