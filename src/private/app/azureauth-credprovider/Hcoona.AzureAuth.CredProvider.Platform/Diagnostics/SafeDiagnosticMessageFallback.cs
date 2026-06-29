namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

internal static class SafeDiagnosticMessageFallback
{
    internal const string GenericMessage = "Adapter host execution failed.";

    internal static string Create(
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
                GetDefaultMessage(safeCode));
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }

        return GenericMessage;
    }

    internal static string GetDefaultMessage(string? safeCode)
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
}
