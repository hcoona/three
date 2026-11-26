using System.Text;
using System.Text.Json;
using Authentication.Core;
using Microsoft.Identity.Client;

namespace Authentication.Windows;

// Interpret public provider observations without exporting provider diagnostics.
internal static class MsalBoundary
{
    internal static ProviderFailureException MapFailure(Exception exception, CancellationToken cancellationToken) =>
        MapFailure(exception, cancellationToken, null);

    internal static ProviderFailureException MapFailure(Exception exception, CancellationToken cancellationToken,
        WindowsMechanismTrace? trace)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var code = (exception as MsalException)?.ErrorCode;

        if (code == "authentication_canceled")
            return new(AuthenticationFailure.Cancelled);

        if (code == "access_denied"
            || exception is MsalServiceException service && HasStructuredDenial(service.ResponseBody))
            return new(AuthenticationFailure.Denied);

        if (code == "user_mismatch")
            return new(AuthenticationFailure.IdentityValidationFailed);

        if (exception is MsalUiRequiredException challenge)
            return new(AuthenticationFailure.InteractionRequired, challenge.Claims,
                challenge.Classification == UiRequiredExceptionClassification.ConsentRequired
                    ? AuthenticationReason.ConsentRequired : AuthenticationReason.None);

        if (code is "platform_not_supported" or "wam_runtime_init_failed"
            || exception is ProviderFailureException { Failure: AuthenticationFailure.MechanismUnavailable })
        {
            if (code == "platform_not_supported") trace?.Record(WindowsMechanismFailure.BrokerPlatform);
            if (code == "wam_runtime_init_failed") trace?.Record(WindowsMechanismFailure.BrokerInitialization);
            return new(AuthenticationFailure.MechanismUnavailable);
        }

        if (exception is MsalException { IsRetryable: true })
            return new(AuthenticationFailure.TemporarilyUnavailable, reason: AuthenticationReason.ProviderTransient);

        if (code is "service_not_available" or "temporarily_unavailable")
            return new(AuthenticationFailure.TemporarilyUnavailable, reason: AuthenticationReason.ServiceTransient);

        if (code == "network_not_available"
            || exception is HttpRequestException
                { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError }
            || exception is OperationCanceledException { InnerException: TimeoutException })
            return new(AuthenticationFailure.TemporarilyUnavailable, reason: AuthenticationReason.NetworkTransient);

        return new(AuthenticationFailure.InternalFailure, diagnosticError: ClassifyInternalFailure(exception));
    }

    private static ProviderDiagnosticError ClassifyInternalFailure(Exception exception)
    {
        if (exception is MsalException { ErrorCode: MsalError.DuplicateQueryParameterError })
            return ProviderDiagnosticError.QueryParameterConflict;

        // Pinned WamAdapters decorates structured native status, not provider text.
        // Unknown/malformed bags cannot alter authentication or become output.
        try
        {
            if (exception is MsalServiceException service
                && service.ErrorCode.StartsWith("WAM_provider_error_", StringComparison.Ordinal)
                && service.AdditionalExceptionData?.TryGetValue(MsalException.BrokerErrorStatus, out var status) == true)
            {
                if (status == "ApiContractViolation") return ProviderDiagnosticError.BrokerContract;
                if (status == "IncorrectConfiguration") return ProviderDiagnosticError.BrokerConfiguration;
            }
        }
        catch (Exception) { }

        return exception switch
        {
            MsalClientException => ProviderDiagnosticError.MsalClient,
            MsalServiceException => ProviderDiagnosticError.MsalService,
            ArgumentException => ProviderDiagnosticError.Argument,
            InvalidOperationException => ProviderDiagnosticError.InvalidOperation,
            _ => ProviderDiagnosticError.Unexpected,
        };
    }

    // The operation binding is the provider-returned correlation ID, never the
    // caller's argument: the coordinator compares it with the ID it sent, so a
    // result from another operation (or with no correlation) fails validation.
    // Pinned MSAL 4.83.1 (tag commit d5d7de6) builds every AuthenticationResult
    // from RequestContext.CorrelationId, which WithCorrelationId sets: see
    // src/client/Microsoft.Identity.Client/Internal/Requests/RequestBase.cs lines
    // 353 (token response saved to cache, including broker responses) and 548
    // (retryable-failure cache fallback). That a real WAM/runtime-broker success
    // carries the sent ID is unverified until an authorized experiment; if it did
    // not, every success would fail closed as identity_validation_failed.
    internal static TokenCandidate Project(AuthenticationResult result) =>
        new(result.AccessToken, result.Account?.Username,
            // An all-zero tenant identifies no tenant; it remains missing like any other
            // invalid tenant metadata.
            Guid.TryParse(result.TenantId, out var tenant) && tenant != Guid.Empty ? tenant : null,
            // Missing scopes must reach the coordinator's metadata rejection. An empty
            // observed list has distinct /.default semantics and cannot replace null.
            result.Scopes?.ToArray()!, result.TokenType, result.ExpiresOn, result.CorrelationId,
            result.CorrelationId);

    private static bool HasStructuredDenial(string? body)
    {
        if (body is null || body.Length > 8192)
            return false;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                MaxDepth = 8,
                AllowDuplicateProperties = false,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException exception) when (exception.InnerException is EncoderFallbackException)
        {
            // Raw unpaired UTF-16 cannot supply structured denial evidence.
            return false;
        }
        catch (InvalidOperationException)
        {
            // Duplicate-property validation also decodes escaped property names.
            // Invalid surrogate escapes fail here before a document is returned.
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error_codes", out var codes)
                || codes.ValueKind != JsonValueKind.Array || codes.GetArrayLength() > 16)
                return false;

            var denied = false;
            foreach (var code in codes.EnumerateArray())
            {
                if (code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var value))
                    return false;
                denied |= value == 65004;
            }

            // Recognition follows validation of the complete document and array.
            return denied;
        }
    }
}
