using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

public static class ConfigurationOwnershipManifestPolicy
{
    public static void EnsureValid(ConfigurationOwnershipManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string? violation = GetViolation(manifest);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(manifest));
        }
    }

    public static bool IsValid(ConfigurationOwnershipManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return GetViolation(manifest) is null;
    }

    public static string? GetViolation(ConfigurationOwnershipManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != ConfigurationOwnershipManifest.CurrentSchemaVersion)
        {
            return "Configuration ownership manifest schema version is unsupported.";
        }

        if (
            string.IsNullOrWhiteSpace(manifest.ManifestId)
            || string.IsNullOrWhiteSpace(manifest.OwnerProductId)
            || string.IsNullOrWhiteSpace(manifest.EntrySelector)
        )
        {
            return "Configuration ownership manifest identifiers are required.";
        }

        if (
            manifest.Scope
            is not (
                ConfigurationScope.User
                or ConfigurationScope.WorkspaceReadOnly
                or ConfigurationScope.ExplicitPath
                or ConfigurationScope.CiTemporary
                or ConfigurationScope.Global
            )
        )
        {
            return "Configuration ownership manifest scope is unsupported.";
        }

        if (
            manifest.SafeMetadata is null
            || manifest.SafeMetadata.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null
            )
        )
        {
            return "Configuration ownership manifest metadata is invalid.";
        }

        if (manifest.Entries is null || manifest.Entries.Any(entry => entry is null))
        {
            return "Configuration ownership manifest entries are required.";
        }

        var selectors = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < manifest.Entries.Count; index++)
        {
            ConfigurationOwnershipManifestEntry entry = manifest.Entries[index];
            if (
                entry.Sequence != index + 1
                || string.IsNullOrWhiteSpace(entry.TargetPathOrName)
                || entry.TargetPathOrName.Contains('\0')
                || string.IsNullOrWhiteSpace(entry.Key)
                || entry.Key.Contains('\r')
                || entry.Key.Contains('\n')
            )
            {
                return "Configuration ownership manifest entry identity is invalid.";
            }

            if (
                entry.TargetKind
                is not (
                    ConfigurationTargetKind.GitConfig
                    or ConfigurationTargetKind.NuGetPluginLayout
                    or ConfigurationTargetKind.PythonKeyringBackend
                    or ConfigurationTargetKind.KeyringShim
                    or ConfigurationTargetKind.Npmrc
                    or ConfigurationTargetKind.Yarnrc
                    or ConfigurationTargetKind.CiTemporaryFile
                )
            )
            {
                return "Configuration ownership manifest target kind is unsupported.";
            }

            string identity =
                ((int)entry.TargetKind).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "\n"
                + entry.TargetPathOrName
                + "\n"
                + entry.Key;
            if (!selectors.Add(identity))
            {
                return "Configuration ownership manifest contains duplicate selectors.";
            }
        }

        return null;
    }
}
