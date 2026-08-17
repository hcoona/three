using System.Text.Json;

namespace Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

public static class JwtExpiryMetadataReader
{
    public static bool TryReadExpiration(string token, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string[] segments = token.Split('.');
        if (segments.Length != 3)
        {
            return false;
        }

        try
        {
            byte[] payload = DecodeBase64Url(segments[1]);
            using JsonDocument document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("exp", out JsonElement expiration)
                && expiration.TryGetInt64(out long seconds)
                && TryFromUnixTimeSeconds(seconds, out expiresAt);
        }
        catch (Exception exception)
            when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = (base64.Length % 4) switch
        {
            0 => base64,
            2 => base64 + "==",
            3 => base64 + "=",
            _ => throw new FormatException("Invalid base64url length."),
        };
        return Convert.FromBase64String(base64);
    }

    private static bool TryFromUnixTimeSeconds(long seconds, out DateTimeOffset value)
    {
        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }
}
