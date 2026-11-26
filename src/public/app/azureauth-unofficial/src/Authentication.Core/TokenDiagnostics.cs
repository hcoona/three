using System.Text;
using System.Text.Json;

namespace Authentication.Core;

public enum TokenDiagnosticFormat { Unavailable, Unreadable, LimitExceeded, DecodedUnverified }

public readonly record struct TokenDiagnostic(
    TokenDiagnosticFormat Format, bool HasExpiration, bool HasAudience, bool HasScopes);

// Diagnostic-only interpretation of this request's token. Never validate identity,
// signature, authorization or scope satisfaction with these observations.
public static class TokenDiagnostics
{
    public static TokenDiagnostic Describe(string? token)
    {
        if (string.IsNullOrEmpty(token)) return new(TokenDiagnosticFormat.Unavailable, false, false, false);
        if (token.Length > 65536) return new(TokenDiagnosticFormat.LimitExceeded, false, false, false);
        try
        {
            var first = token.IndexOf('.');
            var second = first < 0 ? -1 : token.IndexOf('.', first + 1);
            if (first < 1 || second <= first + 1 || second == token.Length - 1
                || token.IndexOf('.', second + 1) >= 0) return Unreadable;
            foreach (var character in token)
                if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')) return Unreadable;
            var payload = token[(first + 1)..second];
            if (payload.Length > 43692) return new(TokenDiagnosticFormat.LimitExceeded, false, false, false);
            var padded = payload.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(padded);
            if (bytes.Length > 32768) return new(TokenDiagnosticFormat.LimitExceeded, false, false, false);
            _ = new UTF8Encoding(false, true).GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !UniqueMembers(root)) return Unreadable;
            var hasExpiration = root.TryGetProperty("exp", out var expiration)
                && expiration.ValueKind == JsonValueKind.Number && expiration.TryGetInt64(out _);
            var hasAudience = root.TryGetProperty("aud", out var audience)
                && (audience.ValueKind == JsonValueKind.String
                    || audience.ValueKind == JsonValueKind.Array
                    && audience.EnumerateArray().All(value => value.ValueKind == JsonValueKind.String));
            var hasScopes = root.TryGetProperty("scp", out var scopes) && scopes.ValueKind == JsonValueKind.String;
            return new(TokenDiagnosticFormat.DecodedUnverified, hasExpiration, hasAudience, hasScopes);
        }
        catch (Exception) { return Unreadable; }
    }

    private static TokenDiagnostic Unreadable => new(TokenDiagnosticFormat.Unreadable, false, false, false);

    private static bool UniqueMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || !UniqueMembers(property.Value)) return false;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (!UniqueMembers(item)) return false;
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            _ = element.GetString();
        }
        return true;
    }
}
