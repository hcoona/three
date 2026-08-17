namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

internal static class SafeDiagnosticMessageFallback
{
    internal const string GenericMessage = "Adapter host execution failed.";
    internal const string CredentialCoreGenericMessage =
        "Credential core diagnostic details are unavailable.";

    internal static string Create(
        string? safeCode,
        string? safeMessage,
        bool allowCodeSpecificFallback = true)
    {
        return Create(
            SafeDiagnosticFallbackScope.AdapterHost,
            safeCode,
            safeMessage,
            allowCodeSpecificFallback);
    }

    internal static string Create(
        SafeDiagnosticFallbackScope fallbackScope,
        string? safeCode,
        string? safeMessage,
        bool allowCodeSpecificFallback = true)
    {
        string message = SafeDiagnosticEnvelopeSanitizer.SanitizeMessage(safeMessage);
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        if (allowCodeSpecificFallback)
        {
            message = SafeDiagnosticEnvelopeSanitizer.SanitizeMessage(
                GetDefaultMessage(fallbackScope, safeCode));
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }

        return GetGenericMessage(fallbackScope);
    }

    internal static string GetDefaultMessage(string? safeCode)
    {
        return GetDefaultMessage(SafeDiagnosticFallbackScope.AdapterHost, safeCode);
    }

    internal static string GetDefaultMessage(
        SafeDiagnosticFallbackScope fallbackScope,
        string? safeCode)
    {
        return fallbackScope switch
        {
            SafeDiagnosticFallbackScope.CredentialCore => GetCredentialCoreDefaultMessage(
                safeCode),
            _ => GetAdapterHostDefaultMessage(safeCode),
        };
    }

    private static string GetAdapterHostDefaultMessage(string? safeCode)
    {
        return safeCode switch
        {
            "InvocationBoundaryMismatch" =>
                "Adapter host invocation boundary is unsupported.",
            "ProtocolViolation" => "Adapter host protocol output was invalid.",
            "UnsupportedAdapterProtocol" => "Adapter host protocol is unsupported.",
            "UnsupportedContractMajor" =>
                "Credential result contract major is unsupported.",
            "UnsupportedCacheKeySchemaMajor" =>
                "Credential cache-key schema is unsupported.",
            _ => GenericMessage,
        };
    }

    private static string GetCredentialCoreDefaultMessage(string? safeCode)
    {
        return safeCode switch
        {
            "CredentialIssued" => "Credential request succeeded.",
            "CacheUnavailable" => "Persistent derived credential cache is unavailable.",
            "CredentialCoreFailure" => "Credential core execution failed.",
            "OperationNotSupported" =>
                "Credential core scaffold only supports get operations.",
            "ProtocolViolation" => "Credential request was invalid.",
            "TokenExchangeFailed" => "Credential token exchange failed.",
            "TokenExchangeUnavailable" => "Credential token exchange is unavailable.",
            "DirectMsalUnavailable" => "Direct MSAL identity provider is unavailable.",
            "DirectMsalNotImplemented" =>
                "Direct MSAL identity provider is not implemented.",
            "FlowDeferred" => "Requested identity flow is deferred by the MVP scaffold.",
            "FlowDisabled" => "Credential request is disabled by the current MVP policy.",
            "UnsupportedFlow" =>
                "Requested identity flow is not supported by the current MVP policy.",
            "InteractionBlocked" =>
                "Credential request requires interaction, but interaction is blocked by "
                + "policy.",
            _ => CredentialCoreGenericMessage,
        };
    }

    private static string GetGenericMessage(SafeDiagnosticFallbackScope fallbackScope)
    {
        return fallbackScope == SafeDiagnosticFallbackScope.CredentialCore
            ? CredentialCoreGenericMessage
            : GenericMessage;
    }
}
