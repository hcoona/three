using System.Diagnostics.CodeAnalysis;
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

    public static string ExpandEnvironmentVariables(
        string value,
        Func<string, string?> environmentVariableReader,
        out bool unresolvedEnvironmentReference
    )
    {
        ArgumentNullException.ThrowIfNull(environmentVariableReader);
        unresolvedEnvironmentReference = false;
        var expanded = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length;)
        {
            int escapeStart = index;
            while (index < value.Length && value[index] == '\\')
            {
                index++;
            }

            int escapeCount = index - escapeStart;
            if (
                !TryParseEnvironmentReference(
                    value,
                    index,
                    out int referenceEnd,
                    out string? variableName,
                    out bool optional
                )
            )
            {
                expanded.Append(value, escapeStart, escapeCount);
                if (index < value.Length)
                {
                    expanded.Append(value[index]);
                    index++;
                }
                continue;
            }

            expanded.Append('\\', escapeCount / 2);
            if ((escapeCount & 1) != 0)
            {
                expanded.Append(value, index, referenceEnd - index);
            }
            else
            {
                string? environmentValue = environmentVariableReader(variableName);
                if (environmentValue is not null)
                {
                    expanded.Append(environmentValue);
                }
                else if (!optional)
                {
                    unresolvedEnvironmentReference = true;
                    expanded.Append("${").Append(variableName).Append('}');
                }
            }

            index = referenceEnd;
        }

        return expanded.ToString();
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

    private static bool TryParseEnvironmentReference(
        string value,
        int startIndex,
        out int endIndex,
        [NotNullWhen(true)] out string? variableName,
        out bool optional
    )
    {
        endIndex = startIndex;
        variableName = null;
        optional = false;
        if (
            startIndex + 3 > value.Length
            || value[startIndex] != '$'
            || value[startIndex + 1] != '{'
        )
        {
            return false;
        }

        int closingBraceIndex = value.IndexOf('}', startIndex + 2);
        if (closingBraceIndex < 0)
        {
            return false;
        }

        ReadOnlySpan<char> body = value.AsSpan(
            startIndex + 2,
            closingBraceIndex - startIndex - 2
        );
        if (!body.IsEmpty && body[^1] == '?')
        {
            optional = true;
            body = body[..^1];
        }

        if (body.IsEmpty || body.IndexOfAny("${}?".AsSpan()) >= 0)
        {
            return false;
        }

        variableName = body.ToString();
        endIndex = closingBraceIndex + 1;
        return true;
    }
}
