using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

public interface IConfigurationManager : IConfigurationChangePlanSink
{
    ConfigurationPlanValidationResult ValidatePlan(ConfigurationChangePlan plan);

    ValueTask<ConfigurationPlanResult> DryRunAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );

    ValueTask<ConfigurationPlanResult> ApplyAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );

    ValueTask<ConfigurationPlanResult> RemoveAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );
}

public interface IConfigurationChangePlanSink
{
    ValueTask<ConfigurationPlanValidationResult> AcceptPlanAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );
}

public interface IConfigurationChangePlanFactory<in TRequest>
{
    ConfigurationChangePlan CreatePlan(TRequest request);
}

public sealed class ConfigurationManager : IConfigurationManager
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);
    private readonly IFileSystem? fileSystem;
    private readonly string? ownershipManifestPath;
    private readonly IConfigurationPhysicalTargetWriterDispatcher? dispatcher;

    public ConfigurationManager() { }

    internal ConfigurationManager(
        IFileSystem fileSystem,
        string ownershipManifestPath,
        IConfigurationPhysicalTargetWriterDispatcher? physicalTargetWriterDispatcher = null
    )
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipManifestPath);
        if (!fileSystem.IsPathFullyQualified(ownershipManifestPath))
        {
            throw new ArgumentException(
                "The ownership manifest path must be fully qualified.",
                nameof(ownershipManifestPath)
            );
        }

        this.ownershipManifestPath = fileSystem.GetFullPath(ownershipManifestPath);
        dispatcher =
            physicalTargetWriterDispatcher
            ?? new ConfigurationPhysicalTargetWriterDispatcher(fileSystem);
    }

    public ConfigurationPlanValidationResult ValidatePlan(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string? violation =
            ConfigurationChangePlanPolicy.GetViolation(plan) ?? GetLocalValidationViolation(plan);
        return new ConfigurationPlanValidationResult
        {
            Plan = plan,
            IsValid = violation is null,
            Violation = violation,
        };
    }

    public ValueTask<ConfigurationPlanValidationResult> AcceptPlanAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ValidatePlan(plan));
    }

    public ValueTask<ConfigurationPlanResult> DryRunAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationChangePlan normalizedPlan = ValidateAndNormalize(plan);
        ConfigurationOwnershipManifest? existingManifest = fileSystem is null
            ? null
            : LoadManifest();
        ValidateManifestForPlan(
            existingManifest,
            normalizedPlan,
            ConfigurationPlanOperation.DryRun
        );
        ValidatePlanAgainstCurrentState(
            normalizedPlan,
            existingManifest,
            ConfigurationPlanOperation.DryRun,
            cancellationToken
        );
        return ValueTask.FromResult(
            CreateResult(
                normalizedPlan,
                ConfigurationPlanOperation.DryRun,
                ProjectManifest(normalizedPlan)
            )
        );
    }

    public ValueTask<ConfigurationPlanResult> ApplyAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ExecuteAsync(plan, ConfigurationPlanOperation.Apply, cancellationToken);

    public ValueTask<ConfigurationPlanResult> RemoveAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ExecuteAsync(plan, ConfigurationPlanOperation.Remove, cancellationToken);

    internal async ValueTask<IReadOnlyList<ConfigurationPlanResult>> ExecuteBatchAsync(
        IReadOnlyList<(
            ConfigurationChangePlan Plan,
            ConfigurationPlanOperation Operation
        )> operations,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();
        if (fileSystem is null || ownershipManifestPath is null)
        {
            throw new InvalidOperationException(
                "Configuration execution requires a filesystem-backed configuration manager."
            );
        }

        (ConfigurationChangePlan Plan, ConfigurationPlanOperation Operation)[] normalized =
            operations
                .Select(operation => (ValidateAndNormalize(operation.Plan), operation.Operation))
                .ToArray();
        using IDisposable ownershipLock = AcquireOwnershipGroupLock();
        ConfigurationOwnershipManifest? originalManifest = LoadManifest();
        ConfigurationOwnershipManifest? previewManifest = originalManifest;
        var resultManifests = new List<ConfigurationOwnershipManifest?>(normalized.Length);

        foreach ((ConfigurationChangePlan plan, ConfigurationPlanOperation operation) in normalized)
        {
            ValidateManifestForPlan(previewManifest, plan, operation);
            ValidatePlanAgainstCurrentState(
                plan,
                MergePrevalidationOwnership(previewManifest, originalManifest),
                operation,
                cancellationToken
            );
            previewManifest = PreviewManifestAfterOperation(previewManifest, plan, operation);
            resultManifests.Add(previewManifest);
        }

        ConfigurationOwnershipManifest? ownershipIntent = CreateOwnershipIntent(
            originalManifest,
            previewManifest
        );
        bool hasApply = normalized.Any(operation =>
            operation.Operation == ConfigurationPlanOperation.Apply
        );
        bool hasRemove = normalized.Any(operation =>
            operation.Operation == ConfigurationPlanOperation.Remove
        );
        ConfigurationOwnershipManifest? persistedManifest = originalManifest;
        if (hasRemove || hasApply)
        {
            persistedManifest = hasApply ? ownershipIntent : previewManifest;
            PersistManifest(persistedManifest);
        }

        try
        {
            foreach (
                (ConfigurationChangePlan plan, ConfigurationPlanOperation operation) in normalized
            )
            {
                if (operation != ConfigurationPlanOperation.Remove)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                await ApplyChanges(plan, originalManifest, operation, cancellationToken);
                DeleteTemporaryContainer(plan);
            }
        }
        catch
        {
            PersistManifest(originalManifest);
            throw;
        }

        foreach ((ConfigurationChangePlan plan, ConfigurationPlanOperation operation) in normalized)
        {
            if (operation == ConfigurationPlanOperation.Apply)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ApplyChanges(plan, ownershipIntent, operation, cancellationToken);
            }
        }

        if (!ManifestsEquivalent(persistedManifest, previewManifest))
        {
            PersistManifest(previewManifest);
        }

        return normalized
            .Select(
                (operation, index) =>
                    CreateResult(operation.Plan, operation.Operation, resultManifests[index])
            )
            .ToArray();
    }

    private void DeleteTemporaryContainer(ConfigurationChangePlan plan)
    {
        ConfigurationTemporaryContainer? container = plan.TemporaryContainer;
        if (container is null)
        {
            return;
        }

        string path = fileSystem!.GetFullPath(container.ProductOwnedPath);
        switch (container.Kind)
        {
            case ConfigurationTemporaryContainerKind.NpmrcFile:
            case ConfigurationTemporaryContainerKind.YarnRcFile:
                if (fileSystem.FileExists(path))
                {
                    fileSystem.DeleteFile(path);
                }
                break;
            case ConfigurationTemporaryContainerKind.TemporaryHome:
                if (fileSystem.DirectoryExists(path))
                {
                    fileSystem.DeleteDirectory(path, recursive: true);
                }
                else if (fileSystem.FileExists(path))
                {
                    fileSystem.DeleteFile(path);
                }
                break;
        }
    }

    internal bool IsAppliedStateCurrent(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (fileSystem is null || ownershipManifestPath is null)
        {
            return false;
        }

        ConfigurationChangePlan normalizedPlan = ValidateAndNormalize(plan);
        using IDisposable ownershipLock = AcquireOwnershipGroupLock();
        ConfigurationOwnershipManifest? manifest;
        try
        {
            manifest = LoadManifest();
            ValidateManifestForPlan(manifest, normalizedPlan, ConfigurationPlanOperation.DryRun);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or InvalidOperationException
                        or JsonException
            )
        {
            return false;
        }

        if (manifest is null || !ManifestContainsAllPlanEntries(manifest, normalizedPlan))
        {
            return false;
        }

        foreach (
            IGrouping<TargetGroupKey, ConfigurationChange> group in GroupChanges(normalizedPlan)
        )
        {
            if (group.Key.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
            {
                if (!GenericChangesSatisfied(group.ToArray()))
                {
                    return false;
                }
                continue;
            }

            var request = CreateWriterRequest(
                normalizedPlan,
                manifest,
                ConfigurationPlanOperation.DryRun,
                group
            );
            if (!dispatcher!.IsSatisfied(request, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private async ValueTask<ConfigurationPlanResult> ExecuteAsync(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<ConfigurationPlanResult> results = await ExecuteBatchAsync(
            [(plan, operation)],
            cancellationToken
        );
        return results[0];
    }

    private ConfigurationChangePlan ValidateAndNormalize(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ConfigurationPlanValidationResult validation = ValidatePlan(plan);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.Violation, nameof(plan));
        }

        if (fileSystem is null)
        {
            return plan;
        }

        return plan with
        {
            Changes = plan
                .Changes.Select(change =>
                    change with
                    {
                        TargetPathOrName = fileSystem.GetFullPath(change.TargetPathOrName),
                        Key = CanonicalizeKey(change),
                    }
                )
                .ToArray(),
        };
    }

    private string? GetLocalValidationViolation(ConfigurationChangePlan plan)
    {
        if (
            fileSystem is not null
            && plan.Changes.Any(change => !fileSystem.IsPathFullyQualified(change.TargetPathOrName))
        )
        {
            return "Filesystem-backed configuration target paths must be fully qualified.";
        }

        foreach (ConfigurationChange change in plan.Changes)
        {
            string? writerViolation = change.TargetKind switch
            {
                ConfigurationTargetKind.GitConfig =>
                    GitConfigPhysicalTargetWriter.GetPlanningValidationViolation(change),
                ConfigurationTargetKind.Npmrc =>
                    NpmrcPhysicalTargetWriter.GetPlanningValidationViolation(
                        change,
                        plan.Manifest.ResourceIdentity
                    ),
                ConfigurationTargetKind.Yarnrc =>
                    YarnrcPhysicalTargetWriter.GetPlanningValidationViolation(
                        change,
                        plan.Manifest.ResourceIdentity
                    ),
                ConfigurationTargetKind.NuGetPluginLayout =>
                    NuGetPluginLayoutPhysicalTargetWriter.GetPlanningValidationViolation(change),
                ConfigurationTargetKind.PythonKeyringBackend
                or ConfigurationTargetKind.KeyringShim =>
                    PythonKeyringPhysicalTargetWriter.GetPlanningValidationViolation(change),
                ConfigurationTargetKind.CiTemporaryFile => GetGenericValidationViolation(change),
                _ => "Unsupported filesystem-backed configuration target kind.",
            };
            if (writerViolation is not null)
            {
                return writerViolation;
            }
        }

        bool duplicate = plan
            .Changes.GroupBy(
                change => new TargetGroupKey(
                    change.TargetKind,
                    NormalizePathForComparison(change.TargetPathOrName),
                    CanonicalizeKey(change)
                ),
                new TargetGroupKeyComparer(GetPathComparer())
            )
            .Any(group => group.Count() > 1);
        return duplicate ? "A configuration plan contains duplicate physical selectors." : null;
    }

    private static string? GetGenericValidationViolation(ConfigurationChange change)
    {
        if (string.IsNullOrWhiteSpace(change.Key))
        {
            return "CI temporary file changes require a non-empty ownership key.";
        }

        return change.Operation switch
        {
            ConfigurationChangeOperation.Set
            or ConfigurationChangeOperation.Create
            or ConfigurationChangeOperation.Update
            or ConfigurationChangeOperation.Refresh when change.Value is null =>
                "CI temporary file value-writing changes require a value.",
            ConfigurationChangeOperation.Remove when change.Value is not null =>
                "CI temporary file removal changes must not carry a value.",
            ConfigurationChangeOperation.Set
            or ConfigurationChangeOperation.Create
            or ConfigurationChangeOperation.Update
            or ConfigurationChangeOperation.Refresh
            or ConfigurationChangeOperation.Remove => null,
            _ => "Unsupported CI temporary file operation.",
        };
    }

    private void ValidatePlanAgainstCurrentState(
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest? manifest,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        if (fileSystem is null)
        {
            return;
        }

        foreach (IGrouping<TargetGroupKey, ConfigurationChange> group in GroupChanges(plan))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Key.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
            {
                ValidateGenericChanges(group.ToArray(), manifest, operation);
                continue;
            }

            dispatcher!.Validate(
                CreateWriterRequest(plan, manifest, operation, group),
                cancellationToken
            );
        }
    }

    private async ValueTask ApplyChanges(
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest? manifest,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        foreach (IGrouping<TargetGroupKey, ConfigurationChange> group in GroupChanges(plan))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Key.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
            {
                ApplyGenericChanges(group.ToArray(), operation);
                continue;
            }

            await dispatcher!.Dispatch(
                CreateWriterRequest(plan, manifest, operation, group),
                cancellationToken
            );
        }
    }

    private static ConfigurationPhysicalTargetWriterRequest CreateWriterRequest(
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest? manifest,
        ConfigurationPlanOperation operation,
        IGrouping<TargetGroupKey, ConfigurationChange> group
    ) =>
        new(operation, group.Key.TargetKind, group.ToArray(), manifest?.Entries ?? [])
        {
            ResourceIdentity = plan.Manifest.ResourceIdentity,
        };

    private IEnumerable<IGrouping<TargetGroupKey, ConfigurationChange>> GroupChanges(
        ConfigurationChangePlan plan
    ) =>
        plan.Changes.GroupBy(
            change => new TargetGroupKey(
                change.TargetKind,
                NormalizePathForComparison(change.TargetPathOrName),
                string.Empty
            ),
            new TargetGroupKeyComparer(GetPathComparer())
        );

    private void ValidateGenericChanges(
        IReadOnlyList<ConfigurationChange> changes,
        ConfigurationOwnershipManifest? manifest,
        ConfigurationPlanOperation operation
    )
    {
        foreach (ConfigurationChange change in changes)
        {
            bool exists = fileSystem!.FileExists(change.TargetPathOrName);
            bool owned = IsOwned(manifest, change);
            if (operation == ConfigurationPlanOperation.Remove)
            {
                if (!owned)
                {
                    throw new InvalidOperationException(
                        "Configuration removal requires a recognized owned selector."
                    );
                }
                continue;
            }

            if (exists && !owned)
            {
                throw new InvalidOperationException(
                    "Configuration target already exists without recognized ownership."
                );
            }
        }
    }

    private void ApplyGenericChanges(
        IReadOnlyList<ConfigurationChange> changes,
        ConfigurationPlanOperation operation
    )
    {
        foreach (ConfigurationChange change in changes)
        {
            if (
                operation == ConfigurationPlanOperation.Remove
                || change.Operation == ConfigurationChangeOperation.Remove
            )
            {
                if (fileSystem!.FileExists(change.TargetPathOrName))
                {
                    fileSystem.DeleteFile(change.TargetPathOrName);
                }
                continue;
            }

            fileSystem!.AtomicWriteAllText(
                change.TargetPathOrName,
                change.Value!,
                Utf8NoBom,
                change.IsSecretValue
                    ? AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
                    : AtomicWriteOptions.None
            );
        }
    }

    private bool GenericChangesSatisfied(IReadOnlyList<ConfigurationChange> changes)
    {
        foreach (ConfigurationChange change in changes)
        {
            if (!fileSystem!.FileExists(change.TargetPathOrName))
            {
                return false;
            }

            if (
                !change.IsSecretValue
                && change.Value is not null
                && !string.Equals(
                    fileSystem.ReadAllText(change.TargetPathOrName),
                    change.Value,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private ConfigurationOwnershipManifest? LoadManifest()
    {
        if (fileSystem is null || ownershipManifestPath is null)
        {
            return null;
        }

        return fileSystem.FileExists(ownershipManifestPath)
            ? ConfigurationOwnershipManifestSerializer.Deserialize(
                fileSystem.ReadAllText(ownershipManifestPath)
            )
            : null;
    }

    private static void ValidateManifestForPlan(
        ConfigurationOwnershipManifest? manifest,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        if (manifest is null)
        {
            if (operation == ConfigurationPlanOperation.Remove)
            {
                throw new InvalidOperationException(
                    "Configuration removal requires an ownership manifest."
                );
            }
            return;
        }

        if (
            !string.Equals(manifest.OwnerProductId, plan.OwnerProductId, StringComparison.Ordinal)
            || manifest.Scope != plan.Scope
            || !string.Equals(
                manifest.ManifestId,
                plan.Manifest.ManifestId,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "The existing configuration ownership manifest is not recognized for this plan."
            );
        }
    }

    private ConfigurationOwnershipManifest? PreviewManifestAfterOperation(
        ConfigurationOwnershipManifest? existing,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        if (operation == ConfigurationPlanOperation.Remove)
        {
            if (existing is null)
            {
                return null;
            }

            ConfigurationOwnershipManifestEntry[] remaining = existing
                .Entries.Where(entry =>
                    !plan.Changes.Any(change => EntryMatchesChange(entry, change))
                )
                .Select((entry, index) => entry with { Sequence = index + 1 })
                .ToArray();
            return remaining.Length == 0 ? null : existing with { Entries = remaining };
        }

        ConfigurationOwnershipManifest projected = ProjectManifest(plan);
        var entries = new List<ConfigurationOwnershipManifestEntry>(existing?.Entries ?? []);
        foreach (ConfigurationOwnershipManifestEntry projectedEntry in projected.Entries)
        {
            entries.RemoveAll(entry => EntryMatchesEntry(entry, projectedEntry));
            entries.Add(projectedEntry);
        }

        return projected with
        {
            Entries = entries
                .Select((entry, index) => entry with { Sequence = index + 1 })
                .ToArray(),
        };
    }

    private ConfigurationOwnershipManifest? MergePrevalidationOwnership(
        ConfigurationOwnershipManifest? preview,
        ConfigurationOwnershipManifest? original
    )
    {
        if (preview is null)
        {
            return original;
        }
        if (original is null)
        {
            return preview;
        }

        var entries = new List<ConfigurationOwnershipManifestEntry>(preview.Entries);
        foreach (ConfigurationOwnershipManifestEntry originalEntry in original.Entries)
        {
            if (!entries.Any(entry => EntryMatchesEntry(entry, originalEntry)))
            {
                entries.Add(originalEntry);
            }
        }

        return preview with
        {
            Entries = entries,
        };
    }

    private ConfigurationOwnershipManifest? CreateOwnershipIntent(
        ConfigurationOwnershipManifest? original,
        ConfigurationOwnershipManifest? final
    )
    {
        if (final is null)
        {
            return original;
        }
        if (original is null)
        {
            return final;
        }

        var entries = new List<ConfigurationOwnershipManifestEntry>(original.Entries);
        foreach (ConfigurationOwnershipManifestEntry finalEntry in final.Entries)
        {
            if (!entries.Any(entry => EntryMatchesEntry(entry, finalEntry)))
            {
                entries.Add(finalEntry);
            }
        }

        return final with
        {
            Entries = entries
                .Select((entry, index) => entry with { Sequence = index + 1 })
                .ToArray(),
        };
    }

    private static bool ManifestsEquivalent(
        ConfigurationOwnershipManifest? left,
        ConfigurationOwnershipManifest? right
    )
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return string.Equals(
            ConfigurationOwnershipManifestSerializer.Serialize(left),
            ConfigurationOwnershipManifestSerializer.Serialize(right),
            StringComparison.Ordinal
        );
    }

    private void PersistManifest(ConfigurationOwnershipManifest? manifest)
    {
        if (fileSystem is null || ownershipManifestPath is null)
        {
            return;
        }

        if (manifest is null)
        {
            if (fileSystem.FileExists(ownershipManifestPath))
            {
                fileSystem.DeleteFile(ownershipManifestPath);
            }
            return;
        }

        fileSystem.AtomicWriteAllText(
            ownershipManifestPath,
            ConfigurationOwnershipManifestSerializer.Serialize(manifest),
            Utf8NoBom,
            AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
        );
    }

    private IDisposable AcquireOwnershipGroupLock()
    {
        if (fileSystem is not IFileSystemMutationLock mutationLock || ownershipManifestPath is null)
        {
            return NullDisposable.Instance;
        }

        string manifestDirectory =
            Path.GetDirectoryName(ownershipManifestPath)
            ?? throw new InvalidOperationException(
                "The ownership manifest path has no parent directory."
            );
        string lockDirectory = Path.Combine(
            manifestDirectory,
            ".locks",
            Path.GetFileName(ownershipManifestPath)
        );
        return mutationLock.AcquireMutationLock(lockDirectory);
    }

    private bool ManifestContainsAllPlanEntries(
        ConfigurationOwnershipManifest manifest,
        ConfigurationChangePlan plan
    ) =>
        plan
            .Changes.Where(change => change.RequiresOwnershipRecord)
            .All(change => manifest.Entries.Any(entry => EntryMatchesChange(entry, change)));

    private bool IsOwned(ConfigurationOwnershipManifest? manifest, ConfigurationChange change) =>
        manifest?.Entries.Any(entry => EntryMatchesChange(entry, change)) == true;

    private bool EntryMatchesChange(
        ConfigurationOwnershipManifestEntry entry,
        ConfigurationChange change
    ) =>
        entry.TargetKind == change.TargetKind
        && PathsEqual(entry.TargetPathOrName, change.TargetPathOrName)
        && string.Equals(entry.Key, CanonicalizeKey(change), StringComparison.Ordinal);

    private bool EntryMatchesEntry(
        ConfigurationOwnershipManifestEntry left,
        ConfigurationOwnershipManifestEntry right
    ) =>
        left.TargetKind == right.TargetKind
        && PathsEqual(left.TargetPathOrName, right.TargetPathOrName)
        && string.Equals(left.Key, right.Key, StringComparison.Ordinal);

    private bool PathsEqual(string left, string right) =>
        string.Equals(
            NormalizePathForComparison(left),
            NormalizePathForComparison(right),
            fileSystem is null
                ? (
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal
                )
                : FileSystemPathSemantics.GetComparison(fileSystem)
        );

    private string NormalizePathForComparison(string path) => fileSystem?.GetFullPath(path) ?? path;

    private StringComparer GetPathComparer() =>
        fileSystem is null
            ? (
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            )
            : FileSystemPathSemantics.GetComparer(fileSystem);

    private static string CanonicalizeKey(ConfigurationChange change) =>
        change.TargetKind == ConfigurationTargetKind.GitConfig
            ? GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(change.Key)
            : change.Key;

    private static ConfigurationOwnershipManifest ProjectManifest(ConfigurationChangePlan plan)
    {
        ConfigurationPlannedOperation[] operations =
            ConfigurationPlanProjector.CreatePlannedOperations(plan);
        return ConfigurationPlanProjector.CreateOwnershipManifest(plan, operations);
    }

    private static ConfigurationPlanResult CreateResult(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        ConfigurationOwnershipManifest? manifest
    )
    {
        ConfigurationPlannedOperation[] operations =
            ConfigurationPlanProjector.CreatePlannedOperations(plan);
        return new ConfigurationPlanResult
        {
            Plan = ConfigurationPlanProjector.CreateDryRunPlan(plan, operations),
            Operation = operation,
            Changes = operations.Select(item => item.Change).ToArray(),
            PlannedOperations = operations,
            OwnershipManifest = manifest,
        };
    }

    private sealed record TargetGroupKey(
        ConfigurationTargetKind TargetKind,
        string TargetPath,
        string Key
    );

    private sealed class TargetGroupKeyComparer(StringComparer pathComparer)
        : IEqualityComparer<TargetGroupKey>
    {
        public bool Equals(TargetGroupKey? left, TargetGroupKey? right) =>
            left is not null
            && right is not null
            && left.TargetKind == right.TargetKind
            && string.Equals(left.Key, right.Key, StringComparison.Ordinal)
            && pathComparer.Equals(left.TargetPath, right.TargetPath);

        public int GetHashCode(TargetGroupKey value) =>
            HashCode.Combine(
                value.TargetKind,
                pathComparer.GetHashCode(value.TargetPath),
                StringComparer.Ordinal.GetHashCode(value.Key)
            );
    }

    private sealed class NullDisposable : IDisposable
    {
        public static NullDisposable Instance { get; } = new();

        public void Dispose() { }
    }
}

public sealed record ConfigurationPlanValidationResult
{
    public required ConfigurationChangePlan Plan { get; init; }
    public required bool IsValid { get; init; }
    public string? Violation { get; init; }
}

public sealed record ConfigurationPlanResult
{
    public required ConfigurationDryRunPlan Plan { get; init; }

    [JsonConverter(typeof(ConfigurationPlanOperationJsonConverter))]
    public required ConfigurationPlanOperation Operation { get; init; }
    public IReadOnlyList<ConfigurationPlannedChange> Changes { get; init; } = [];
    public IReadOnlyList<ConfigurationPlannedOperation> PlannedOperations { get; init; } = [];
    public ConfigurationOwnershipManifest? OwnershipManifest { get; init; }
}

public sealed record ConfigurationDryRunPlan
{
    public required int ContractMajor { get; init; }
    public required string PlanId { get; init; }
    public required string OwnerProductId { get; init; }
    public required ConfigurationScope Scope { get; init; }
    public required ConfigurationManifestMetadata Manifest { get; init; }
    public ConfigurationTemporaryContainer? TemporaryContainer { get; init; }
    public required ConfigurationDeclarationPreservation DeclarationPreservation { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required bool ContainsCredentialMaterial { get; init; }
    public IReadOnlyDictionary<string, string> ExtensionData { get; init; } =
        ContractMetadata.Empty;
    public IReadOnlyList<ConfigurationPlannedChange> Changes { get; init; } = [];
}

public sealed record ConfigurationPlannedChange
{
    public required int Sequence { get; init; }
    public required ConfigurationChangeOperation Operation { get; init; }
    public required ConfigurationTargetKind TargetKind { get; init; }
    public required string TargetPathOrName { get; init; }
    public required string Key { get; init; }
    public required bool RequiresOwnershipRecord { get; init; }
    public required bool PreserveDeclarationsAndComments { get; init; }
    public required bool HasPlannedValue { get; init; }
    public required bool IsSecretValue { get; init; }
}

public enum ConfigurationPlanOperation
{
    DryRun,
    Apply,
    Remove,
}

public sealed class ConfigurationPlanOperationJsonConverter
    : JsonConverter<ConfigurationPlanOperation>
{
    public override ConfigurationPlanOperation Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (
            reader.TokenType != JsonTokenType.String
            || !Enum.TryParse(
                reader.GetString(),
                ignoreCase: true,
                out ConfigurationPlanOperation value
            )
        )
        {
            throw new JsonException("Unsupported configuration plan operation.");
        }

        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ConfigurationPlanOperation value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}

public sealed record ConfigurationPlannedOperation
{
    public required int Sequence { get; init; }
    public required ConfigurationPlannedChange Change { get; init; }
    public required ConfigurationOwnershipManifestEntry? OwnershipEntry { get; init; }
}

public sealed record ConfigurationOwnershipManifest
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string ManifestId { get; init; }
    public required string OwnerProductId { get; init; }
    public required ConfigurationScope Scope { get; init; }
    public required string EntrySelector { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CanonicalResourceIdentity? ResourceIdentity { get; init; }
    public string? ProductVersion { get; init; }
    public IReadOnlyDictionary<string, string> SafeMetadata { get; init; } = ContractMetadata.Empty;

    [JsonRequired]
    public IReadOnlyList<ConfigurationOwnershipManifestEntry> Entries { get; init; } = [];
}

public sealed record ConfigurationOwnershipManifestEntry
{
    public required int Sequence { get; init; }
    public required ConfigurationTargetKind TargetKind { get; init; }
    public required string TargetPathOrName { get; init; }
    public required string Key { get; init; }
}

public static class ConfigurationOwnershipManifestSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions =
        ConfigurationOwnershipManifestJson.CreateSerializerOptions();

    public static string Serialize(ConfigurationOwnershipManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        return JsonSerializer.Serialize(manifest, SerializerOptions);
    }

    public static ConfigurationOwnershipManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ConfigurationOwnershipManifest manifest =
            JsonSerializer.Deserialize<ConfigurationOwnershipManifest>(json, SerializerOptions)
            ?? throw new JsonException(
                "Configuration ownership manifest JSON did not contain a manifest."
            );
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        return manifest;
    }
}

public sealed class ConfigurationOwnershipManifestStore(IFileSystem fileSystem)
{
    public void Save(string path, ConfigurationOwnershipManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);
        fileSystem.AtomicWriteAllText(
            path,
            ConfigurationOwnershipManifestSerializer.Serialize(manifest),
            Utf8NoBom,
            AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
        );
    }

    public ConfigurationOwnershipManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ConfigurationOwnershipManifestSerializer.Deserialize(fileSystem.ReadAllText(path));
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);
}
