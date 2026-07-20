using System.Text;
using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

namespace Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

public sealed record AzureDevOpsJwtClaimConsistency
{
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset NotBefore { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

public static class AzureDevOpsJwtClaimConsistencyValidator
{
    public const int MaxHeaderBytes = 8 * 1024;
    public const int MaxPayloadBytes = 64 * 1024;
    public static readonly TimeSpan DefaultClockSkew = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumAccessTokenLifetime = TimeSpan.FromHours(24);

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryValidate(
        string rawToken,
        string enforcedTenant,
        DateTimeOffset utcNow,
        out AzureDevOpsJwtClaimConsistency? consistency,
        TimeSpan? clockSkew = null)
    {
        consistency = null;
        if (string.IsNullOrEmpty(rawToken) || string.IsNullOrWhiteSpace(enforcedTenant))
        {
            return false;
        }

        string[] segments = rawToken.Split('.');
        JsonDocument? headerDocument = null;
        JsonDocument? payloadDocument = null;
        if (
            segments.Length != 3
            || segments.Any(static segment => segment.Length == 0)
            || !TryDecodeSegment(segments[0], MaxHeaderBytes, out byte[] header)
            || !TryDecodeSegment(segments[1], MaxPayloadBytes, out byte[] payload)
            || !TryDecodeSegment(segments[2], MaxHeaderBytes, out _)
            || !TryParseObject(header, out headerDocument)
            || !TryParseObject(payload, out payloadDocument)
        )
        {
            headerDocument?.Dispose();
            payloadDocument?.Dispose();
            return false;
        }

        using (headerDocument)
        using (payloadDocument)
        {
            JsonElement claims = payloadDocument!.RootElement;
            if (
                !TryGetRequiredString(claims, "aud", out string? audience)
                || !string.Equals(
                    audience,
                    AzureAuthIdentityProvider.AzureDevOpsResourceId,
                    StringComparison.Ordinal)
                || !TryGetRequiredString(claims, "tid", out string? tenant)
                || !string.Equals(tenant, enforcedTenant, StringComparison.OrdinalIgnoreCase)
                || !TryGetNumericDate(claims, "iat", out DateTimeOffset issuedAt)
                || !TryGetNumericDate(claims, "nbf", out DateTimeOffset notBefore)
                || !TryGetNumericDate(claims, "exp", out DateTimeOffset expiresAt)
            )
            {
                return false;
            }

            TimeSpan skew = clockSkew ?? DefaultClockSkew;
            if (
                skew < TimeSpan.Zero
                || issuedAt > utcNow + skew
                || notBefore > utcNow + skew
                || expiresAt <= utcNow - skew
                || issuedAt > expiresAt
                || notBefore > expiresAt
                || expiresAt - issuedAt > MaximumAccessTokenLifetime
                || expiresAt - utcNow > MaximumAccessTokenLifetime
            )
            {
                return false;
            }

            consistency = new AzureDevOpsJwtClaimConsistency
            {
                IssuedAt = issuedAt,
                NotBefore = notBefore,
                ExpiresAt = expiresAt,
            };
            return true;
        }
    }

    private static bool TryDecodeSegment(string segment, int maxBytes, out byte[] bytes)
    {
        bytes = [];
        if (
            segment.Length > ((maxBytes + 2) / 3 * 4)
            || segment.Any(
                static character =>
                    !(
                        character is >= 'A' and <= 'Z'
                        or >= 'a' and <= 'z'
                        or >= '0' and <= '9'
                        or '-'
                        or '_'
                    ))
            || segment.Length % 4 == 1
        )
        {
            return false;
        }

        string base64 = segment.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        try
        {
            bytes = Convert.FromBase64String(base64);
            string canonical = Convert
                .ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            return bytes.Length <= maxBytes
                && string.Equals(segment, canonical, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryParseObject(byte[] utf8, out JsonDocument? document)
    {
        document = null;
        try
        {
            _ = StrictUtf8.GetString(utf8);
            document = JsonDocument.Parse(
                utf8,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && HasUniquePropertyNames(document.RootElement);
        }
        catch (Exception exception)
            when (exception is JsonException or DecoderFallbackException)
        {
            document?.Dispose();
            document = null;
            return false;
        }
    }

    private static bool HasUniquePropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || !HasUniquePropertyNames(property.Value))
                {
                    return false;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (!HasUniquePropertyNames(item))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryGetRequiredString(
        JsonElement claims,
        string name,
        out string? value)
    {
        value = null;
        return claims.TryGetProperty(name, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = property.GetString());
    }

    private static bool TryGetNumericDate(
        JsonElement claims,
        string name,
        out DateTimeOffset value)
    {
        value = default;
        if (
            !claims.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out long seconds)
        )
        {
            return false;
        }

        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
