#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowsResultValidation;

// This result contains only allowlisted conclusions, never input text or identifiers.
public sealed class ResultChecks
{
    public Outcome Outcome { get; internal set; }
    public Route Route { get; internal set; }
    public bool Passed { get; internal set; }
    public bool ProtocolValid { get; internal set; }
    public bool MetadataValid { get; internal set; }
    public bool PersistenceUnconfirmed { get; internal set; }
    public bool PersistenceFailed { get; internal set; }
}

public static class ProtocolResult
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] SuccessNames = ["accessToken", "tokenType", "expiresOn", "accountEmail",
        "tenantId", "authority", "scopes", "mechanism", "interaction", "warnings", "correlationId"];
    private static readonly string[] ForbiddenMaterial = ["refreshToken", "idToken", "authorizationCode", "claims"];

    public static ResultChecks Validate(ReadOnlyMemory<byte> bytes, uint exit, ResultExpectation request, DateTimeOffset received)
    {
        try
        {
            request.Validate();
            ReadOnlySpan<byte> span = bytes.Span;
            Need(span.Length is >= 3 and <= 1048576 && span[0] == (byte)'{' && span[^2] == (byte)'}' && span[^1] == 10);
            Need(!span[..^1].Contains((byte)10) && !span.Contains((byte)13));
            StrictUtf8.GetCharCount(span); // Validate all bytes, including discarded additive values.
            using JsonDocument document = JsonDocument.Parse(bytes[..^1], new JsonDocumentOptions { MaxDepth = 32 });
            JsonElement root = document.RootElement;
            Need(root.ValueKind == JsonValueKind.Object);
            CheckMembers(root);
            Need(root.TryGetProperty("protocol", out JsonElement protocol) && protocol.TryGetInt32(out int major) && major == 1);
            Outcome outcome = ParseOutcome(Text(root, "outcome"));
            Need(outcome != Outcome.Unknown && exit == (outcome == Outcome.Success ? 0u : 1u));
            foreach (string field in ForbiddenMaterial) Need(!root.TryGetProperty(field, out _));
            var result = new ResultChecks { Outcome = outcome, ProtocolValid = true };
            if (outcome != Outcome.Success)
            {
                foreach (string field in SuccessNames) Need(!root.TryGetProperty(field, out _));
                string reason = Text(root, "reason");
                Need(Regex.IsMatch(reason, "\\A[a-z][a-z0-9_]{0,63}\\z", RegexOptions.CultureInvariant));
                // A syntactically valid unknown reason is deliberately omitted from all outputs.
            }
            else
            {
                Need(Text(root, "accessToken").Length > 0); // Opaque; never decoded, compared, hashed or emitted.
                Need(Text(root, "tokenType").Length > 0); // The accepted contract does not restrict this to Bearer.
                string email = Text(root, "accountEmail");
                Need(ResultExpectation.HasCharacterLength(email, 3, 320) &&
                    string.Equals(email, request.Email, StringComparison.OrdinalIgnoreCase));
                Need(ResultExpectation.TryExactGuid(Text(root, "tenantId"), out Guid tenant) && tenant != Guid.Empty);
                Need(request.ExactResultTenant is null || tenant == request.ExactResultTenant);
                Need(Text(root, "authority") == "https://login.microsoftonline.com/" + tenant.ToString("D"));
                string expiration = Text(root, "expiresOn");
                Need(Regex.IsMatch(expiration, "\\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]+)?(?:Z|[+-][0-9]{2}:[0-9]{2})\\z"));
                Need(root.GetProperty("expiresOn").TryGetDateTimeOffset(out DateTimeOffset expiry) && expiry > received);
                Need(Text(root, "mechanism") == "wam");
                result.Route = Text(root, "interaction") switch
                { "silent" => Route.Silent, "interactive" => Route.Interactive, _ => throw new InvalidResultException() };
                Need(request.InteractionAllowed || result.Route == Route.Silent);
                var scopes = StringArray(root, "scopes");
                if (request.Scopes.Length == 1 && request.Scopes[0].EndsWith("/.default", StringComparison.Ordinal))
                    Need(request.DefaultAssociationIndependentlyAccepted); // No literal /.default or invented operation field.
                else foreach (string scope in request.Scopes) Need(scopes.Contains(scope, StringComparer.Ordinal));
                var warnings = StringArray(root, "warnings");
                Need(warnings.Distinct(StringComparer.Ordinal).Count() == warnings.Count);
                foreach (string warning in warnings)
                {
                    Need(warning is "persistence_unconfirmed" or "persistence_failed");
                    result.PersistenceUnconfirmed |= warning == "persistence_unconfirmed";
                    result.PersistenceFailed |= warning == "persistence_failed";
                }
                if (root.TryGetProperty("correlationId", out _))
                    Need(ResultExpectation.TryExactGuid(Text(root, "correlationId"), out _));
                result.MetadataValid = true;
            }
            result.Passed = outcome == request.ExpectedOutcome &&
                (request.ExpectedRoute == Route.None || result.Route == request.ExpectedRoute) &&
                (!request.RequirePersistenceUnconfirmed || result.PersistenceUnconfirmed);
            return result;
        }
        catch (InvalidResultException) { throw; }
        catch { throw new InvalidResultException(); } // No source exception, text or raw-byte fallback.
    }

    private static void CheckMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            { Need(names.Add(property.Name)); CheckMembers(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray()) CheckMembers(item);
        else if (element.ValueKind == JsonValueKind.String)
            _ = element.GetString(); // Also validate escaped Unicode in otherwise discarded values.
    }
    private static string Text(JsonElement root, string name)
    { Need(root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String); return value.GetString()!; }
    private static List<string> StringArray(JsonElement root, string name)
    {
        Need(root.TryGetProperty(name, out JsonElement array) && array.ValueKind == JsonValueKind.Array);
        var values = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        { Need(item.ValueKind == JsonValueKind.String); string s = item.GetString()!; Need(s.Length > 0); values.Add(s); }
        return values;
    }
    private static void Need(bool condition) { if (!condition) throw new InvalidResultException(); }
    private static Outcome ParseOutcome(string value) => value switch
    {
        "success" => Outcome.Success, "invalid_request" => Outcome.InvalidRequest,
        "interaction_required" => Outcome.InteractionRequired, "account_ambiguous" => Outcome.AccountAmbiguous,
        "identity_validation_failed" => Outcome.IdentityValidationFailed, "cancelled" => Outcome.Cancelled,
        "denied" => Outcome.Denied, "mechanism_unavailable" => Outcome.MechanismUnavailable,
        "temporarily_unavailable" => Outcome.TemporarilyUnavailable, "timeout" => Outcome.Timeout,
        "internal_failure" => Outcome.InternalFailure, _ => Outcome.Unknown,
    };
}
