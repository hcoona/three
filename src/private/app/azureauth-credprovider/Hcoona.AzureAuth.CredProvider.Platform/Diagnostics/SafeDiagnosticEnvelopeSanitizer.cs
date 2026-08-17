using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

internal static class SafeDiagnosticEnvelopeSanitizer
{
    internal const string CodePropertyName = "code";
    internal const int MaxCodeLength = 64;
    internal const int MaxMessageLength = 256;
    internal const int MaxPropertyKeyLength = 64;
    internal const int MaxPropertyValueLength = 256;

    internal static bool IsCanonicalCodePropertyKey(string key) =>
        string.Equals(key, CodePropertyName, StringComparison.Ordinal);

    internal static string? SanitizeCode(string? value)
    {
        string sanitized = SanitizeText(value, MaxCodeLength, '_', disallowEquals: true);
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    internal static string SanitizeMessage(string? value) =>
        SanitizeText(value, MaxMessageLength, ' ');

    internal static string SanitizePropertyKey(string? value) =>
        SanitizeText(value, MaxPropertyKeyLength, '_', disallowEquals: true).Trim('_');

    internal static string? SanitizePropertyValue(string? value) =>
        value is null ? null : SanitizeText(value, MaxPropertyValueLength, ' ');

    private static string SanitizeText(
        string? value,
        int maxLength,
        char replacement,
        bool disallowEquals = false
    )
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        bool previousWasReplacement = false;
        foreach (char character in value)
        {
            if (builder.Length == maxLength)
            {
                break;
            }

            bool replace =
                char.IsControl(character)
                || char.IsWhiteSpace(character)
                || (disallowEquals && character == '=');
            if (replace)
            {
                if (builder.Length == 0 || previousWasReplacement)
                {
                    continue;
                }

                builder.Append(replacement);
                previousWasReplacement = true;
                continue;
            }

            builder.Append(character);
            previousWasReplacement = false;
        }

        return builder.ToString().TrimEnd(replacement);
    }
}
