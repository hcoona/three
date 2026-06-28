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
            return "Protocol violation: configuration ownership manifest schema version must be 1.";
        }

        if (
            string.IsNullOrWhiteSpace(manifest.ManifestId)
            || string.IsNullOrWhiteSpace(manifest.PlanId)
            || string.IsNullOrWhiteSpace(manifest.ChangeSetId)
            || string.IsNullOrWhiteSpace(manifest.OwnerProductId)
            || string.IsNullOrWhiteSpace(manifest.EntrySelector)
        )
        {
            return "Protocol violation: configuration ownership manifest identifiers are required.";
        }

        if (!HasKnownManifestEnums(manifest))
        {
            return "Protocol violation: configuration ownership manifest enum values must use "
                + "supported v1 values.";
        }

        if (manifest.SafeMetadata is null)
        {
            return "Protocol violation: configuration ownership manifest metadata is required.";
        }

        if (
            manifest.SafeMetadata.Any(metadata =>
                string.IsNullOrWhiteSpace(metadata.Key) || metadata.Value is null
            )
        )
        {
            return "Protocol violation: configuration ownership manifest metadata keys and values "
                + "must be non-empty.";
        }

        if (manifest.Entries is null)
        {
            return "Protocol violation: configuration ownership manifest entries are required.";
        }

        if (manifest.Entries.Any(entry => entry is null))
        {
            return "Protocol violation: configuration ownership manifest entries must not contain "
                + "null entries.";
        }

        for (int index = 0; index < manifest.Entries.Count; index++)
        {
            ConfigurationOwnershipManifestEntry entry = manifest.Entries[index];
            int expectedSequence = index + 1;
            if (entry.Sequence != expectedSequence)
            {
                return "Protocol violation: configuration ownership manifest entry sequences must "
                    + "be contiguous and start at 1.";
            }

            string? entryViolation = GetEntryViolation(manifest, entry);
            if (entryViolation is not null)
            {
                return entryViolation;
            }
        }

        bool containsSecretEntry = manifest.Entries.Any(entry => entry.IsSecretValue);
        if (!manifest.ContainsCredentialMaterial && containsSecretEntry)
        {
            return "Protocol violation: configuration ownership manifests with secret entries must "
                + "advertise credential material.";
        }

        return null;
    }

    private static bool HasKnownManifestEnums(ConfigurationOwnershipManifest manifest) =>
        manifest.Scope
            is ConfigurationScope.User
                or ConfigurationScope.WorkspaceReadOnly
                or ConfigurationScope.ExplicitPath
                or ConfigurationScope.CiTemporary
                or ConfigurationScope.Global;

    private static string? GetEntryViolation(
        ConfigurationOwnershipManifest manifest,
        ConfigurationOwnershipManifestEntry entry
    )
    {
        if (!HasKnownEntryEnums(entry))
        {
            return "Protocol violation: configuration ownership manifest entry enum values must "
                + "use supported v1 values.";
        }

        if (
            string.IsNullOrWhiteSpace(entry.TargetPathOrName)
            || string.IsNullOrWhiteSpace(entry.Key)
        )
        {
            return "Protocol violation: configuration ownership manifest entry target and key are "
                + "required.";
        }

        if (ContainsLineBreak(entry.Key))
        {
            return "Protocol violation: configuration ownership manifest entry keys must not "
                + "contain CR or LF.";
        }

        if (entry.IsSecretValue && entry.PlannedValueSha256 is not null)
        {
            return "Protocol violation: configuration ownership manifest secret entries must not "
                + "include planned value hashes.";
        }

        if (
            entry.TargetKind == ConfigurationTargetKind.Yarnrc
            && IsYarnNpmAuthIdentKey(entry.Key)
        )
        {
            return "Protocol violation: Yarn npmAuthIdent is unsupported and must not be "
                + "emitted as a product-owned configuration ownership manifest entry.";
        }

        if (IsIntrinsicallySecretNpmCompatibleAuthEntry(entry) && !entry.IsSecretValue)
        {
            return "Protocol violation: configuration ownership manifest npm-compatible auth "
                + "entries must be marked as secret.";
        }

        if (entry.TargetKind == ConfigurationTargetKind.Npmrc)
        {
            string? npmrcViolation = GetNpmrcEntryViolation(manifest, entry);
            if (npmrcViolation is not null)
            {
                return npmrcViolation;
            }
        }

        if (RequiresValue(entry.Operation) && !entry.HasPlannedValue)
        {
            return "Protocol violation: value-writing configuration ownership manifest entries "
                + "require planned values.";
        }

        if (!RequiresValue(entry.Operation) && entry.HasPlannedValue)
        {
            return "Protocol violation: non-value configuration ownership manifest entries must "
                + "not advertise planned values.";
        }

        if (!entry.HasPlannedValue && entry.PlannedValueSha256 is not null)
        {
            return "Protocol violation: configuration ownership manifest entries without planned "
                + "values must not include planned value hashes.";
        }

        if (
            entry.HasPlannedValue
            && !entry.IsSecretValue
            && !IsLowercaseSha256Hex(entry.PlannedValueSha256)
        )
        {
            return "Protocol violation: non-secret configuration ownership manifest planned "
                + "values require SHA-256 hashes.";
        }

        if (
            RequiresPreviousOwnedEntryMetadata(entry.Operation)
            && string.IsNullOrWhiteSpace(entry.PreviousOwnedEntryMetadata)
        )
        {
            return "Protocol violation: update, refresh, remove, and remove-adapter "
                + "configuration ownership manifest entries require previous owned-entry "
                + "metadata.";
        }

        return null;
    }

    private static string? GetNpmrcEntryViolation(
        ConfigurationOwnershipManifest manifest,
        ConfigurationOwnershipManifestEntry entry
    )
    {
        string? operationViolation = GetNpmrcOperationViolation(entry.Operation);
        if (operationViolation is not null)
        {
            return operationViolation;
        }

        string? keyViolation = GetNpmrcKeyViolation(entry.Key);
        if (keyViolation is not null)
        {
            return keyViolation;
        }

        if (entry.IsSecretValue && !IsNpmAuthTokenKey(entry.Key))
        {
            return "Protocol violation: Npmrc secret entries are only supported for auth token "
                + "keys.";
        }

        if (
            entry.IsSecretValue
            && IsNpmAuthTokenKey(entry.Key)
        )
        {
            return GetNpmrcSecretAuthTokenSelectorViolation(manifest, entry.Key);
        }

        return null;
    }

    private static string? GetNpmrcSecretAuthTokenSelectorViolation(
        ConfigurationOwnershipManifest manifest,
        string selector
    )
    {
        if (manifest.ResourceIdentity is null)
        {
            return "Protocol violation: Npmrc secret auth token entries require a canonical "
                + "registry identity.";
        }

        string? resourceViolation = CanonicalResourceIdentityPolicy.GetViolation(
            manifest.ResourceIdentity
        );
        if (resourceViolation is not null)
        {
            return resourceViolation;
        }

        if (
            !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                manifest.ResourceIdentity.ServiceEndpoint,
                CredentialEcosystem.Npm
            )
        )
        {
            return "Protocol violation: Npmrc secret auth token entries require a canonical "
                + "npm registry identity.";
        }

        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(
            manifest.ResourceIdentity
        );
        if (
            !string.Equals(
                manifest.EntrySelector,
                selectors.NpmAuthTokenKey,
                StringComparison.Ordinal
            )
        )
        {
            return "Protocol violation: Npmrc secret auth token manifest selectors must match "
                + "the canonical registry identity.";
        }

        if (!string.Equals(selector, selectors.NpmAuthTokenKey, StringComparison.Ordinal))
        {
            return "Protocol violation: Npmrc secret auth token keys must match the canonical "
                + "registry identity.";
        }

        return null;
    }

    private static string? GetNpmrcOperationViolation(ConfigurationChangeOperation operation) =>
        operation switch
        {
            ConfigurationChangeOperation.EnsureFile =>
                "Protocol violation: Npmrc ensure-file manifest entries are unsupported.",
            ConfigurationChangeOperation.InstallAdapter =>
                "Protocol violation: Npmrc install-adapter manifest entries are unsupported.",
            ConfigurationChangeOperation.RemoveAdapter =>
                "Protocol violation: Npmrc remove-adapter manifest entries are unsupported.",
            _ => null,
        };

    private static string? GetNpmrcKeyViolation(string key)
    {
        if (HasLeadingOrTrailingWhiteSpace(key))
        {
            return "Protocol violation: Npmrc keys must not have surrounding whitespace.";
        }

        if (ContainsLineBreak(key))
        {
            return "Protocol violation: Npmrc keys must not contain CR or LF.";
        }

        if (key.Any(character => character < ' ' || character == '\u007f'))
        {
            return "Protocol violation: Npmrc keys must not contain control characters.";
        }

        if (key.Contains('='))
        {
            return "Protocol violation: Npmrc keys must not contain '='.";
        }

        if (IsQuoted(key))
        {
            return "Protocol violation: Npmrc keys must not be quoted.";
        }

        if (key.Contains('#') || key.Contains(';'))
        {
            return "Protocol violation: Npmrc keys must not contain comment markers.";
        }

        return null;
    }

    private static bool IsQuoted(string value) =>
        value.Length > 1
        && ((value[0] == '\'' && value[^1] == '\'') || (value[0] == '"' && value[^1] == '"'));

    private static bool HasKnownEntryEnums(ConfigurationOwnershipManifestEntry entry) =>
        entry.Operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Remove
                or ConfigurationChangeOperation.EnsureFile
                or ConfigurationChangeOperation.InstallAdapter
                or ConfigurationChangeOperation.RemoveAdapter
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
        && entry.TargetKind
            is ConfigurationTargetKind.GitConfig
                or ConfigurationTargetKind.NuGetPluginLayout
                or ConfigurationTargetKind.PythonKeyringBackend
                or ConfigurationTargetKind.KeyringShim
                or ConfigurationTargetKind.Npmrc
                or ConfigurationTargetKind.Yarnrc
                or ConfigurationTargetKind.CiTemporaryFile;

    private static bool RequiresValue(ConfigurationChangeOperation operation) =>
        operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;

    private static bool RequiresPreviousOwnedEntryMetadata(
        ConfigurationChangeOperation operation
    ) =>
        operation
            is ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
                or ConfigurationChangeOperation.Remove
                or ConfigurationChangeOperation.RemoveAdapter;

    private static bool IsIntrinsicallySecretNpmCompatibleAuthEntry(
        ConfigurationOwnershipManifestEntry entry
    ) =>
        (
            entry.TargetKind == ConfigurationTargetKind.Npmrc
            && entry.HasPlannedValue
            && IsNpmAuthTokenKey(entry.Key)
        )
        || (
            RequiresValue(entry.Operation)
            && entry.TargetKind == ConfigurationTargetKind.Yarnrc
            && IsYarnNpmAuthTokenKey(entry.Key)
        );

    private static bool IsNpmAuthTokenKey(string? key) =>
        key is not null
        && (
            string.Equals(key, "_authToken", StringComparison.Ordinal)
            || key.EndsWith(":_authToken", StringComparison.Ordinal)
        );

    private static bool IsYarnNpmAuthTokenKey(string? key) =>
        key is not null
        && (
            string.Equals(key, "npmAuthToken", StringComparison.Ordinal)
            || key.EndsWith(".npmAuthToken", StringComparison.Ordinal)
            || string.Equals(key, "npmAuthIdent", StringComparison.Ordinal)
            || key.EndsWith(".npmAuthIdent", StringComparison.Ordinal)
        );

    private static bool IsYarnNpmAuthIdentKey(string? key) =>
        key is not null
        && (
            string.Equals(key, "npmAuthIdent", StringComparison.Ordinal)
            || key.EndsWith(".npmAuthIdent", StringComparison.Ordinal)
        );

    private static bool HasLeadingOrTrailingWhiteSpace(string value) =>
        !string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static bool IsLowercaseSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(IsLowercaseHex);

    private static bool IsLowercaseHex(char value) =>
        (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f');

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\r') || value.Contains('\n');
}
