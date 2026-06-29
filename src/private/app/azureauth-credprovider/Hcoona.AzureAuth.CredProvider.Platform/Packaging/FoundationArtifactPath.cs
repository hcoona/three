using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.Packaging;

public static class FoundationArtifactPath
{
    private static readonly string[] WindowsReservedDeviceNames =
    [
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "CONIN$",
        "CONOUT$",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "COM¹",
        "COM²",
        "COM³",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
        "LPT¹",
        "LPT²",
        "LPT³",
    ];

    public static void EnsureSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Artifact paths must not be empty.", nameof(path));
        }

        if (
            path[0] == '/'
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains(':', StringComparison.Ordinal)
            || path.Contains('\0', StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                $"Artifact path '{path}' must be a safe forward-slash relative path.",
                nameof(path)
            );
        }

        string[] segments = path.Split('/');
        foreach (string segment in segments)
        {
            if (!IsSafeWindowsPathSegment(segment))
            {
                throw new ArgumentException(
                    $"Artifact path '{path}' contains an unsafe path segment.",
                    nameof(path)
                );
            }
        }
    }

    public static void EnsureSafeTargetRid(string rid)
    {
        if (string.IsNullOrWhiteSpace(rid) || rid != rid.Trim())
        {
            throw new ArgumentException(
                "Target RID must be an explicit safe path segment.",
                nameof(rid)
            );
        }

        if (!IsSafeWindowsPathSegment(rid) || rid.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Target RID must be an explicit safe path segment.",
                nameof(rid)
            );
        }

        foreach (char character in rid)
        {
            if (!IsAllowedTargetRidCharacter(character))
            {
                throw new ArgumentException(
                    "Target RID must contain only letters, digits, dots, underscores, and hyphens.",
                    nameof(rid)
                );
            }
        }
    }

    internal static bool IsSafeWindowsPathSegment(string segment)
    {
        return segment.Length != 0
            && segment is not ("." or "..")
            && !segment.EndsWith(' ')
            && !segment.EndsWith('.')
            && !ContainsInvalidFileNameCharacter(segment)
            && !IsWindowsReservedDeviceName(segment);
    }

    private static bool ContainsInvalidFileNameCharacter(string segment)
    {
        for (int index = 0; index < segment.Length; index++)
        {
            if (
                IsWindowsInvalidFileNameCharacter(segment[index])
                || CharUnicodeInfo.GetUnicodeCategory(segment, index) == UnicodeCategory.Format
            )
            {
                return true;
            }

            if (
                char.IsHighSurrogate(segment[index])
                && index + 1 < segment.Length
                && char.IsLowSurrogate(segment[index + 1])
            )
            {
                index++;
            }
        }

        return false;
    }

    private static bool IsWindowsInvalidFileNameCharacter(char character)
    {
        return char.IsControl(character)
            || character is '<' or '>' or '"' or ':' or '\\' or '|' or '?' or '*' or '\0';
    }

    private static bool IsWindowsReservedDeviceName(string segment)
    {
        int extensionSeparatorIndex = segment.IndexOf('.');
        string nameWithoutExtension = extensionSeparatorIndex >= 0
            ? segment[..extensionSeparatorIndex].TrimEnd(' ')
            : segment;
        return WindowsReservedDeviceNames.Contains(
            nameWithoutExtension,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static bool IsAllowedTargetRidCharacter(char character)
    {
        return char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-';
    }
}
