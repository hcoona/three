using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;

/// <summary>Describes one pinned AzureAuth.exe deployment.</summary>
[JsonConverter(typeof(AzureAuthDeploymentConfigDirectJsonConverter))]
public sealed record AzureAuthDeploymentConfig
{
    public required int SchemaVersion { get; init; }

    public required string ExecutablePath { get; init; }

    public required string ExecutableSha256 { get; init; }

    public required string SignerIdentity { get; init; }

    public required string PublisherName { get; init; }

    public required string ExecutableVersion { get; init; }

    public required string ProvenanceIdentifier { get; init; }
}

/// <summary>Validates the frozen v1 AzureAuth deployment contract.</summary>
public static class AzureAuthDeploymentConfigPolicy
{
    public static void EnsureValid(AzureAuthDeploymentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.SchemaVersion != ContractVersions.AzureAuthDeploymentConfigSchemaMajor)
        {
            throw new ArgumentException(
                "Deployment configuration schema version must be 1.",
                nameof(config)
            );
        }

        WindowsPathPolicy.ValidateExecutablePath(config.ExecutablePath);
        EnsureValidSha256(config.ExecutableSha256, nameof(config.ExecutableSha256));
        EnsureValidPrintableAsciiPin(config.SignerIdentity, nameof(config.SignerIdentity));
        EnsureValidPrintableAsciiPin(config.PublisherName, nameof(config.PublisherName));
        EnsureValidExactVersion(config.ExecutableVersion, nameof(config.ExecutableVersion));
        EnsureValidProvenanceIdentifier(
            config.ProvenanceIdentifier,
            nameof(config.ProvenanceIdentifier)
        );
    }

    internal static void EnsureValidSha256(string? value, string paramName)
    {
        if (value is not { Length: 64 } || value.Any(static character => !IsLowerHex(character)))
        {
            throw new ArgumentException(
                "Executable SHA-256 must be 64 lowercase hexadecimal characters.",
                paramName
            );
        }
    }

    internal static void EnsureValidPrintableAsciiPin(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Deployment pin is required.", paramName);
        }

        foreach (char character in value)
        {
            if (!IsPrintableAscii(character))
            {
                throw new ArgumentException(
                    "Deployment pin must use printable ASCII.",
                    paramName
                );
            }
        }
    }

    internal static void EnsureValidExactVersion(string? value, string paramName)
    {
        EnsureValidPrintableAsciiPin(value, paramName);
        string[] segments = value!.Split('.', StringSplitOptions.None);
        if (segments.Length is < 3 or > 4)
        {
            throw new ArgumentException(
                "Executable version must use an exact three- or four-part numeric form.",
                paramName
            );
        }

        if (
            segments.Any(static segment =>
                segment.Length == 0 || segment.Any(static character => !char.IsAsciiDigit(character))
            )
        )
        {
            throw new ArgumentException(
                "Executable version must use an exact three- or four-part numeric form.",
                paramName
            );
        }
    }

    internal static void EnsureValidProvenanceIdentifier(string? value, string paramName)
    {
        EnsureValidPrintableAsciiPin(value, paramName);

        if (!IsLowercaseLetterOrDigit(value![0]) || !IsLowercaseLetterOrDigit(value[^1]))
        {
            throw new ArgumentException(
                "Provenance identifier must start and end with lowercase ASCII or digits.",
                paramName
            );
        }

        foreach (char character in value)
        {
            if (
                !IsLowercaseLetterOrDigit(character)
                && character is not ('-' or '.' or '_' or ':' or '/')
            )
            {
                throw new ArgumentException(
                    "Provenance identifier must use canonical lowercase ASCII.",
                    paramName
                );
            }
        }
    }

    private static bool IsLowerHex(char value) =>
        (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f');

    private static bool IsPrintableAscii(char value) => value is >= ' ' and <= '~';

    private static bool IsLowercaseLetterOrDigit(char value) =>
        (value >= 'a' && value <= 'z') || char.IsAsciiDigit(value);
}

/// <summary>Applies the lexical Windows path policy frozen for WP2.</summary>
public static class WindowsPathPolicy
{
    private static readonly string[] ReservedDeviceNames =
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
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    ];

    public static void ValidateExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !string.Equals(path, path.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Executable path is not an allowed absolute Windows path.",
                nameof(path)
            );
        }

        if (path.Length < 4 || path[1] != ':' || path[2] != '\\' || path[0] is < 'A' or > 'Z')
        {
            throw new ArgumentException(
                "Executable path is not an allowed absolute Windows path.",
                nameof(path)
            );
        }

        if (
            path.Contains('/')
            || path.Contains('%')
            || path.IndexOf(':', 2) >= 0
            || path.Any(static character => !IsAllowedPathCharacter(character))
        )
        {
            throw new ArgumentException(
                "Executable path is not an allowed absolute Windows path.",
                nameof(path)
            );
        }

        string[] components = path[3..].Split('\\', StringSplitOptions.None);
        if (components.Length == 0 || components.Any(IsForbiddenComponent))
        {
            throw new ArgumentException(
                "Executable path contains a forbidden component.",
                nameof(path)
            );
        }

        if (!string.Equals(components[^1], "AzureAuth.exe", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Executable path must name AzureAuth.exe with exact casing.",
                nameof(path)
            );
        }
    }

    public static bool MatchesConfiguredCanonicalPath(string configuredPath, string? canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        return !string.IsNullOrWhiteSpace(canonicalPath)
            && string.Equals(configuredPath, canonicalPath, StringComparison.Ordinal);
    }

    private static bool IsAllowedPathCharacter(char value) => value is >= ' ' and <= '~';

    private static bool IsForbiddenComponent(string component)
    {
        return string.IsNullOrEmpty(component)
            || component is "." or ".."
            || component[0] == ' '
            || component[^1] == ' '
            || component[^1] == '.'
            || component.Any(static character => character is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            || ContainsShortNameAlias(component)
            || IsReservedDeviceName(component);
    }

    private static bool ContainsShortNameAlias(string component)
    {
        for (int index = 0; index < component.Length - 1; index++)
        {
            if (component[index] == '~' && char.IsAsciiDigit(component[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReservedDeviceName(string component)
    {
        int separatorIndex = component.IndexOf('.');
        string nameWithoutExtension = separatorIndex >= 0 ? component[..separatorIndex] : component;
        return ReservedDeviceNames.Contains(nameWithoutExtension, StringComparer.OrdinalIgnoreCase);
    }
}
