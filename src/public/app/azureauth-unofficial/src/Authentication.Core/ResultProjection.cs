using System.Text.Json;

namespace Authentication.Core;

// This value describes a prepared result, not a successful write or process exit.
public sealed record SerializedResult(byte[] Utf8Json, int ExitCode)
{
    public override string ToString() => nameof(SerializedResult);
}

public static class ResultProjection
{
    // The host supplies only the authoritative committed outcome. Projection never
    // performs transport I/O or serializes a provider object or exception.
    public static SerializedResult Serialize(AuthenticationOutcome outcome)
    {
        using var buffer = new MemoryStream();
        var exitCode = 1;
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocol", 1);
            if (outcome is { Success: { Tenant: { } tenant } token, Failure: null, Reason: AuthenticationReason.None }
                && IsComplete(token))
            {
                writer.WriteString("outcome", "success");
                writer.WriteString("accessToken", token.AccessToken);
                writer.WriteString("tokenType", token.TokenType);
                writer.WriteString("expiresOn", token.ExpiresOn);
                writer.WriteString("accountEmail", token.Email);
                writer.WriteString("tenantId", tenant);
                writer.WriteString("authority", $"https://login.microsoftonline.com/{tenant:D}");
                writer.WriteStartArray("scopes");
                foreach (var scope in token.Scopes)
                {
                    writer.WriteStringValue(scope);
                }
                writer.WriteEndArray();
                writer.WriteString("mechanism", "wam");
                writer.WriteString("interaction", outcome.Interactive ? "interactive" : "silent");
                writer.WriteStartArray("warnings");
                if (outcome.PersistenceUnconfirmed)
                {
                    writer.WriteStringValue("persistence_unconfirmed");
                }
                writer.WriteEndArray();
                if (token.CorrelationId is { } correlation && correlation != Guid.Empty)
                {
                    writer.WriteString("correlationId", correlation);
                }
                exitCode = 0;
            }
            else
            {
                var (failure, reason) = FailureFields(outcome);
                writer.WriteString("outcome", failure);
                writer.WriteString("reason", reason);
            }
            writer.WriteEndObject();
            writer.Flush();
        }
        buffer.WriteByte((byte)'\n');
        return new(buffer.ToArray(), exitCode);
    }

    // The coordinator validates these fields before selection. Projection still fails
    // closed to internal_failure rather than emitting a success the schema rejects.
    private static bool IsComplete(TokenCandidate token) =>
        !string.IsNullOrEmpty(token.AccessToken) && !string.IsNullOrEmpty(token.TokenType)
        && !string.IsNullOrEmpty(token.Email) && token.Scopes is not null
        && token.Scopes.All(scope => !string.IsNullOrEmpty(scope));

    private static (string Outcome, string Reason) FailureFields(AuthenticationOutcome outcome)
    {
        if (outcome is not { Success: null, Failure: { } failure })
        {
            return ("internal_failure", "internal_failure");
        }

        var name = failure switch
        {
            AuthenticationFailure.InvalidRequest => "invalid_request",
            AuthenticationFailure.InteractionRequired => "interaction_required",
            AuthenticationFailure.AccountAmbiguous => "account_ambiguous",
            AuthenticationFailure.IdentityValidationFailed => "identity_validation_failed",
            AuthenticationFailure.Cancelled => "cancelled",
            AuthenticationFailure.Denied => "denied",
            AuthenticationFailure.MechanismUnavailable => "mechanism_unavailable",
            AuthenticationFailure.TemporarilyUnavailable => "temporarily_unavailable",
            AuthenticationFailure.Timeout => "timeout",
            AuthenticationFailure.InternalFailure => "internal_failure",
            _ => null,
        };
        var reason = (failure, outcome.Reason) switch
        {
            (_, AuthenticationReason.None) => name,
            (AuthenticationFailure.InvalidRequest, AuthenticationReason.InvalidConfiguration) => "invalid_configuration",
            (AuthenticationFailure.InvalidRequest, AuthenticationReason.TenantConflict) => "tenant_conflict",
            (AuthenticationFailure.InteractionRequired, AuthenticationReason.ConsentRequired) => "consent_required",
            (AuthenticationFailure.TemporarilyUnavailable, AuthenticationReason.ProviderTransient) => "provider_transient",
            (AuthenticationFailure.TemporarilyUnavailable, AuthenticationReason.NetworkTransient) => "network_transient",
            (AuthenticationFailure.TemporarilyUnavailable, AuthenticationReason.ServiceTransient) => "service_transient",
            _ => null,
        };
        return name is not null && reason is not null
            ? (name, reason)
            : ("internal_failure", "internal_failure");
    }
}
