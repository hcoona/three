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
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            Manifest = plan.Manifest,
            TemporaryContainer = plan.TemporaryContainer,
            DeclarationPreservation = plan.DeclarationPreservation,
            ExpiresAt = plan.ExpiresAt,
            ContainsCredentialMaterial = plan.ContainsCredentialMaterial,
            ExtensionData = plan.ExtensionData,
            Changes = plannedOperations.Select(operation => operation.Change).ToArray(),
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
            OwnerProductId = plan.OwnerProductId,
            Scope = plan.Scope,
            EntrySelector = plan.Manifest.EntrySelector,
            ResourceIdentity = plan.Manifest.ResourceIdentity,
            ProductVersion = plan.Manifest.ProductVersion,
            SafeMetadata = plan.Manifest.SafeMetadata,
            Entries = plannedOperations
                .Select(operation => operation.OwnershipEntry)
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
        bool hasValue = change.Value is not null;
        bool isSecret =
            change.IsSecretValue
            || ConfigurationChangePlanPolicy.IsIntrinsicallySecretNpmCompatibleAuthValue(change);
        var plannedChange = new ConfigurationPlannedChange
        {
            Sequence = sequence,
            Operation = change.Operation,
            TargetKind = change.TargetKind,
            TargetPathOrName = change.TargetPathOrName,
            Key = change.Key,
            RequiresOwnershipRecord = change.RequiresOwnershipRecord,
            PreserveDeclarationsAndComments = change.PreserveDeclarationsAndComments,
            HasPlannedValue = hasValue,
            IsSecretValue = isSecret,
        };
        return new ConfigurationPlannedOperation
        {
            Sequence = sequence,
            Change = plannedChange,
            OwnershipEntry = change.RequiresOwnershipRecord
                ? new ConfigurationOwnershipManifestEntry
                {
                    Sequence = sequence,
                    TargetKind = change.TargetKind,
                    TargetPathOrName = change.TargetPathOrName,
                    Key = change.Key,
                }
                : null,
        };
    }
}
