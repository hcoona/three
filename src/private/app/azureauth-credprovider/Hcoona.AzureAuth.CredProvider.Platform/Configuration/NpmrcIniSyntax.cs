using System.Text;
using System.Text.Json;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal static class NpmrcIniSyntax
{
    public static string DecodeField(string rawValue)
    {
        string value = rawValue.Trim();
        if (value.StartsWith('"') && value.EndsWith('"'))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(value);
                return document.RootElement.ValueKind == JsonValueKind.String
                    ? document.RootElement.GetString() ?? value
                    : value;
            }
            catch (JsonException)
            {
                return value;
            }
        }

        if (value.StartsWith('\'') && value.EndsWith('\''))
        {
            return value[1..^1];
        }

        var decoded = new StringBuilder(value.Length);
        bool escaped = false;
        foreach (char character in value)
        {
            if (escaped)
            {
                if (character is '\\' or '#' or ';')
                {
                    decoded.Append(character);
                }
                else
                {
                    decoded.Append('\\').Append(character);
                }

                escaped = false;
            }
            else if (character is '#' or ';')
            {
                break;
            }
            else if (character == '\\')
            {
                escaped = true;
            }
            else
            {
                decoded.Append(character);
            }
        }

        if (escaped)
        {
            decoded.Append('\\');
        }

        return decoded.ToString().TrimEnd();
    }

    public static string NormalizeArrayAssignmentKey(
        string key,
        out bool isArrayAssignment
    )
    {
        isArrayAssignment = key.Length > 2 && key.EndsWith("[]", StringComparison.Ordinal);
        return isArrayAssignment ? key[..^2] : key;
    }

    public static bool IsSectionHeader(string rawLine)
    {
        if (rawLine.Length < 2 || rawLine[0] != '[')
        {
            return false;
        }

        int closingBracketIndex = rawLine.IndexOf(']', 1);
        return closingBracketIndex >= 0
            && rawLine.AsSpan(closingBracketIndex + 1).Trim().IsEmpty;
    }
}
