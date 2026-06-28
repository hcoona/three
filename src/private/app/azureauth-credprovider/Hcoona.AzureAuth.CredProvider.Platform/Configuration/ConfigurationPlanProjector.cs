using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal static class ConfigurationPlanProjector
{
    public static ConfigurationPlannedOperation[] CreatePlannedOperations(
        ConfigurationChangePlan plan
    )
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Changes.Select(CreatePlannedOperation).ToArray();
    }

    public static ConfigurationDryRunPlan CreateDryRunPlan(
        ConfigurationChangePlan plan,
        IReadOnlyList<ConfigurationPlannedOperation> plannedOperations
    )
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plannedOperations);

        return new ConfigurationDryRunPlan
        {
            ContractMajor = plan.ContractMajor,
            PlanId = plan.PlanId,
            ChangeSetId = plan.ChangeSetId,
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            AtomicityPolicy = plan.AtomicityPolicy,
            RollbackPolicy = plan.RollbackPolicy,
            State = plan.State,
            ManifestCommitPolicy = plan.ManifestCommitPolicy,
            Manifest = plan.Manifest,
            TemporaryContainer = plan.TemporaryContainer,
            DeclarationPreservation = plan.DeclarationPreservation,
            ExpiresAt = plan.ExpiresAt,
            ContainsCredentialMaterial = plan.ContainsCredentialMaterial,
            ExtensionData = plan.ExtensionData,
            Changes = plannedOperations
                .Select(plannedOperation => plannedOperation.Change)
                .ToArray(),
        };
    }

    public static ConfigurationOwnershipManifest CreateOwnershipManifest(
        ConfigurationChangePlan plan,
        IReadOnlyList<ConfigurationPlannedOperation> plannedOperations
    )
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plannedOperations);

        return new ConfigurationOwnershipManifest
        {
            ManifestId = plan.Manifest.ManifestId,
            PlanId = plan.PlanId,
            ChangeSetId = plan.ChangeSetId,
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ResourceIdentity = plan.Manifest.ResourceIdentity,
            ProductVersion = plan.Manifest.ProductVersion,
            PreviousOwnedEntryHash = plan.Manifest.PreviousOwnedEntryHash,
            ContainsCredentialMaterial = plan.ContainsCredentialMaterial,
            SafeMetadata = plan.Manifest.SafeMetadata,
            Entries = plannedOperations
                .Select(plannedOperation => plannedOperation.OwnershipEntry)
                .OfType<ConfigurationOwnershipManifestEntry>()
                .ToArray(),
        };
    }

    public static IReadOnlyList<ConfigurationTargetLayoutProjection> CreateTargetLayoutProjections(
        ConfigurationLayoutProjectionContext context
    ) => ConfigurationLayoutProjector.ProjectTargets(context);

    private static ConfigurationPlannedOperation CreatePlannedOperation(
        ConfigurationChange change,
        int index
    )
    {
        int sequence = index + 1;
        return new ConfigurationPlannedOperation
        {
            Sequence = sequence,
            Change = CreatePlannedChange(change, sequence),
            OwnershipEntry = change.RequiresOwnershipRecord
                ? CreateOwnershipManifestEntry(change, sequence)
                : null,
        };
    }

    private static ConfigurationPlannedChange CreatePlannedChange(
        ConfigurationChange change,
        int sequence
    )
    {
        bool hasPlannedValue = change.Value is not null;
        bool isSecretValue = change.TargetKind == ConfigurationTargetKind.Npmrc
            ? hasPlannedValue && change.IsSecretValue
            : change.IsSecretValue;
        return new ConfigurationPlannedChange
        {
            Sequence = sequence,
            Operation = change.Operation,
            TargetKind = change.TargetKind,
            TargetPathOrName = change.TargetPathOrName,
            Key = change.Key,
            RequiresOwnershipRecord = change.RequiresOwnershipRecord,
            PreserveDeclarationsAndComments = change.PreserveDeclarationsAndComments,
            HasPlannedValue = hasPlannedValue,
            IsSecretValue = isSecretValue,
            PlannedValueSha256 = GetPlannedValueSha256(change, hasPlannedValue, isSecretValue),
            PreviousOwnedEntryMetadata = change.PreviousOwnedEntryMetadata,
        };
    }

    private static ConfigurationOwnershipManifestEntry CreateOwnershipManifestEntry(
        ConfigurationChange change,
        int sequence
    )
    {
        bool hasPlannedValue = change.Value is not null;
        bool isSecretValue = change.TargetKind == ConfigurationTargetKind.Npmrc
            ? hasPlannedValue && change.IsSecretValue
            : change.IsSecretValue;
        return new ConfigurationOwnershipManifestEntry
        {
            Sequence = sequence,
            Operation = change.Operation,
            TargetKind = change.TargetKind,
            TargetPathOrName = change.TargetPathOrName,
            Key = change.Key,
            PreserveDeclarationsAndComments = change.PreserveDeclarationsAndComments,
            HasPlannedValue = hasPlannedValue,
            IsSecretValue = isSecretValue,
            PlannedValueSha256 = GetPlannedValueSha256(change, hasPlannedValue, isSecretValue),
            PreviousOwnedEntryMetadata = change.PreviousOwnedEntryMetadata,
        };
    }

    private static string? GetPlannedValueSha256(
        ConfigurationChange change,
        bool hasPlannedValue,
        bool isSecretValue
    )
    {
        if (!hasPlannedValue || isSecretValue)
        {
            return null;
        }

        return ComputeSha256(GetPlannedValueForHash(change));
    }

    internal static string GetPlannedValueForHash(ConfigurationChange change)
    {
        if (
            change.TargetKind == ConfigurationTargetKind.GitConfig
            && GitConfigPhysicalTargetWriter.TryCanonicalizeSupportedConfigurationKey(
                change.Key,
                out string canonicalKey
            )
            && string.Equals(canonicalKey, "credential.helper", StringComparison.Ordinal)
            && change.Value is not null
        )
        {
            return GitConfigPhysicalTargetWriter.EscapeCredentialHelperPathForShell(change.Value);
        }

        return change.Value!;
    }

    private static string ComputeSha256(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }
}
