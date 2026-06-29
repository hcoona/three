using System.Globalization;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

internal static class SafeDiagnosticEnvelopeSanitizer
{
    internal const string CodePropertyName = "code";
    internal const int InspectionMultiplier = 4;
    internal const int MaxCodeLength = 64;
    internal const int MaxMessageLength = 256;
    internal const int MaxPropertyKeyLength = 64;
    internal const int MaxPropertyValueLength = 256;

    internal static bool IsCanonicalCodePropertyKey(string key)
    {
        return string.Equals(key, CodePropertyName, StringComparison.Ordinal);
    }

    internal static string? SanitizeCode(string? safeCode)
    {
        if (string.IsNullOrEmpty(safeCode))
        {
            return null;
        }

        string sanitizedCode = SanitizeToken(
            safeCode,
            MaxCodeLength,
            disallowEquals: true);
        return string.IsNullOrEmpty(sanitizedCode)
            ? null
            : sanitizedCode;
    }

    internal static string SanitizeMessage(string? safeMessage)
    {
        string sanitizedMessage = string.IsNullOrEmpty(safeMessage)
            ? string.Empty
            : SanitizeText(
                safeMessage,
                MaxMessageLength,
                separator: ' ');

        return EscapeReservedCodeToken(sanitizedMessage);
    }

    internal static string SanitizePropertyKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(MaxPropertyKeyLength);
        var previousWasSeparator = false;
        int maxInspectedCharacterCount = GetMaxInputInspectionLength(
            MaxPropertyKeyLength);
        for (int index = 0;
             index < key.Length && index < maxInspectedCharacterCount;
             index++)
        {
            if (builder.Length == MaxPropertyKeyLength)
            {
                break;
            }

            char character = key[index];
            char sanitizedCharacter = IsPropertyKeyCharacter(character)
                ? character
                : '_';
            if (sanitizedCharacter == '_')
            {
                if (previousWasSeparator)
                {
                    continue;
                }

                previousWasSeparator = true;
            }
            else
            {
                previousWasSeparator = false;
            }

            builder.Append(sanitizedCharacter);
        }

        return builder.ToString().Trim('_');
    }

    internal static bool IsReservedPropertyKey(string key)
    {
        return key.EndsWith(CodePropertyName, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsPropertyKeyCharacter(char character)
    {
        return char.IsLetterOrDigit(character)
               || character is '-' or '_' or '.' or ':';
    }

    internal static string? SanitizePropertyValue(string? value)
    {
        return value is null
            ? null
            : EscapeReservedCodeToken(
                SanitizeToken(value, MaxPropertyValueLength));
    }

    internal static string SanitizeToken(
        string value,
        int maxLength,
        bool disallowEquals = false)
    {
        return SanitizeText(
            value,
            maxLength,
            separator: '_',
            disallowEquals);
    }

    internal static string SanitizeText(
        string value,
        int maxLength,
        char separator,
        bool disallowEquals = false)
    {
        var builder = new StringBuilder(maxLength);
        bool hasPendingSeparator = false;
        bool isTruncated = false;
        int maxInspectedCharacterCount = GetMaxInputInspectionLength(maxLength);

        for (int index = 0; index < value.Length; index++)
        {
            if (index == maxInspectedCharacterCount)
            {
                isTruncated = true;
                break;
            }

            char character = value[index];
            char sanitizedCharacter = IsUnsafeCharacter(character)
                || char.IsWhiteSpace(character)
                || (disallowEquals && character == '=')
                ? separator
                : character;
            if (sanitizedCharacter == separator)
            {
                if (builder.Length == 0 || hasPendingSeparator)
                {
                    continue;
                }

                hasPendingSeparator = true;
                continue;
            }

            if (hasPendingSeparator)
            {
                if (builder.Length == maxLength)
                {
                    isTruncated = true;
                    break;
                }

                builder.Append(separator);
                hasPendingSeparator = false;
                if (builder.Length == maxLength)
                {
                    isTruncated = true;
                    break;
                }
            }

            if (builder.Length == maxLength)
            {
                isTruncated = true;
                break;
            }

            builder.Append(sanitizedCharacter);
        }

        string sanitized = builder.ToString();
        if (!isTruncated)
        {
            return sanitized;
        }

        if (sanitized.Length == 0)
        {
            return string.Empty;
        }

        if (maxLength <= 3)
        {
            return sanitized.Length <= maxLength
                ? sanitized
                : sanitized[..maxLength];
        }

        int truncatedLength = Math.Min(sanitized.Length, maxLength - 3);
        string truncated = sanitized[..truncatedLength].TrimEnd(separator);
        return string.IsNullOrEmpty(truncated)
            ? sanitized[..Math.Min(sanitized.Length, maxLength)]
            : string.Concat(truncated, "...");
    }

    internal static int GetMaxInputInspectionLength(int maxLength)
    {
        return checked(maxLength * InspectionMultiplier);
    }

    internal static string? TryGetPassthroughCode(string? safeCode)
    {
        if (safeCode is null
            || safeCode.Length == 0
            || safeCode.Length > MaxCodeLength)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(safeCode))
        {
            return null;
        }

        bool sawNonSeparator = false;
        bool previousWasSeparator = false;
        foreach (char character in safeCode)
        {
            if (IsUnsafeCharacter(character)
                || char.IsWhiteSpace(character)
                || character == '=')
            {
                return null;
            }

            if (character == '_')
            {
                if (!sawNonSeparator || previousWasSeparator)
                {
                    return null;
                }

                previousWasSeparator = true;
                continue;
            }

            sawNonSeparator = true;
            previousWasSeparator = false;
        }

        return sawNonSeparator && !previousWasSeparator
            ? safeCode
            : null;
    }

    internal static string EscapeReservedCodeToken(string value)
    {
        const string ReservedCodeToken = CodePropertyName + "=";
        int reservedCodeTokenIndex = value.IndexOf(
            ReservedCodeToken,
            StringComparison.OrdinalIgnoreCase);
        if (reservedCodeTokenIndex < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        int currentIndex = 0;
        while (reservedCodeTokenIndex >= 0)
        {
            builder.Append(value, currentIndex, reservedCodeTokenIndex - currentIndex);
            builder.Append(value, reservedCodeTokenIndex, CodePropertyName.Length);
            builder.Append(':');
            currentIndex = reservedCodeTokenIndex + ReservedCodeToken.Length;
            reservedCodeTokenIndex = value.IndexOf(
                ReservedCodeToken,
                currentIndex,
                StringComparison.OrdinalIgnoreCase);
        }

        builder.Append(value, currentIndex, value.Length - currentIndex);
        return builder.ToString();
    }

    internal static bool IsUnsafeCharacter(char character)
    {
        UnicodeCategory category = char.GetUnicodeCategory(character);
        return category is UnicodeCategory.Control
               or UnicodeCategory.Format
               or UnicodeCategory.Surrogate
               or UnicodeCategory.PrivateUse
               or UnicodeCategory.OtherNotAssigned;
    }
}
