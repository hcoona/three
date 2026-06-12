using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
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
    private const string FileSystemLockFileName = ".azureauth-credprovider.fs.lock";
    private const string LifecycleLockDirectoryName = ".azureauth-credprovider.lifecycle-locks";
    private const string Sha256MetadataPrefix = "sha256:";
    private static readonly object ExecutionLock = new();
    private readonly IFileSystem? fileSystem;
    private readonly string? ownershipManifestPath;

    public ConfigurationManager() { }

    internal ConfigurationManager(IFileSystem fileSystem, string ownershipManifestPath)
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipManifestPath);
        EnsureOwnershipManifestPathIsNotReservedInternalFileSystemArtifact(ownershipManifestPath);
        this.ownershipManifestPath = ownershipManifestPath;
    }

    public ConfigurationPlanValidationResult ValidatePlan(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetPlanningValidationViolation(plan);
        violation ??= GetOwnershipManifestPathCollisionWithPhysicalTargetsViolation(plan);
        violation ??= GetFilesystemBackedPhysicalTargetKindSamePathConflictViolation(plan);
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
        EnsureValidForPlanning(plan);
        EnsureNoOwnershipManifestPathCollisionWithPhysicalTargets(plan);
        EnsureNoFilesystemBackedPhysicalTargetKindSamePathConflicts(plan);
        ConfigurationPlanResult plannedResult = CreatePlannedResult(
            plan,
            ConfigurationPlanOperation.DryRun
        );
        if (fileSystem is null || ownershipManifestPath is null)
        {
            return ValueTask.FromResult(plannedResult);
        }

        EnsureFilesystemBackedDryRunOperationSupported(plan);
        return ValueTask.FromResult(SimulateFilesystemBackedDryRun(plannedResult, plan));
    }

    public ValueTask<ConfigurationPlanResult> ApplyAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ExecuteAsync(plan, ConfigurationPlanOperation.Apply, cancellationToken);

    public ValueTask<ConfigurationPlanResult> RemoveAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ExecuteAsync(plan, ConfigurationPlanOperation.Remove, cancellationToken);

    private ValueTask<ConfigurationPlanResult> ExecuteAsync(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureValid(plan);
        if (fileSystem is null || ownershipManifestPath is null)
        {
            return ValueTask.FromException<ConfigurationPlanResult>(
                new InvalidOperationException(
                    "Configuration apply/remove execution requires a filesystem-backed "
                        + "configuration manager with an ownership manifest path."
                )
            );
        }

        EnsureNoOwnershipManifestPathCollisionWithPhysicalTargets(plan);
        EnsureNoFilesystemBackedPhysicalTargetKindSamePathConflicts(plan);
        EnsureOperationSupported(plan, operation);
        EnsureExecutableGenericTargetPlanShapeSupported(fileSystem, plan);
        ConfigurationPlanResult plannedResult = CreatePlannedResult(plan, operation);

        try
        {
            ConfigurationOwnershipManifest? appliedOwnershipManifest = Execute(
                plannedResult,
                plan,
                operation,
                cancellationToken
            );
            return ValueTask.FromResult(
                plannedResult with
                {
                    State = ConfigurationPlanState.Applied,
                    OwnershipManifest = appliedOwnershipManifest,
                }
            );
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            return ValueTask.FromException<ConfigurationPlanResult>(exception);
        }
    }

    private static void EnsureValid(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private static void EnsureValidForPlanning(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetPlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private static string? GetValidationViolation(ConfigurationChangePlan plan)
    {
        string? contractViolation = ConfigurationChangePlanPolicy.GetViolation(plan);
        if (contractViolation is not null)
        {
            return contractViolation;
        }

        ConfigurationPlannedOperation[] plannedOperations =
            ConfigurationPlanProjector.CreatePlannedOperations(plan);
        ConfigurationOwnershipManifest manifest =
            ConfigurationPlanProjector.CreateOwnershipManifest(plan, plannedOperations);
        return ConfigurationOwnershipManifestPolicy.GetViolation(manifest);
    }

    private static string? GetPlanningValidationViolation(ConfigurationChangePlan plan)
    {
        string? violation = GetValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        return GetCiTemporaryFilePlanWholeFileOwnershipViolation(plan)
            ?? GetPhysicalTargetKindSamePathConflictViolation(plan)
            ?? GetReservedInternalPhysicalTargetPathViolation(plan)
            ?? GetCiTemporaryFileUnsupportedOperationViolation(plan);
    }

    private void EnsureNoOwnershipManifestPathCollisionWithPhysicalTargets(
        ConfigurationChangePlan plan
    )
    {
        if (fileSystem is null || ownershipManifestPath is null)
        {
            return;
        }

        string manifestPath = GetNormalizablePhysicalPath(
            fileSystem,
            ownershipManifestPath,
            "ownership manifest"
        );
        foreach (
            ConfigurationChange change in plan.Changes.Where(HasCollisionCheckedPhysicalTargetPath)
        )
        {
            string targetPath = GetNormalizablePhysicalPath(
                fileSystem,
                change.TargetPathOrName,
                $"{change.TargetKind} target"
            );
            if (PathsAreSameOrParentChild(targetPath, manifestPath))
            {
                throw new ArgumentException(
                    "Protocol violation: physical configuration targets must not equal, contain, "
                        + "or be contained by the ownership manifest path.",
                    nameof(plan)
                );
            }
        }
    }

    private string? GetOwnershipManifestPathCollisionWithPhysicalTargetsViolation(
        ConfigurationChangePlan plan
    )
    {
        if (fileSystem is null || ownershipManifestPath is null)
        {
            return null;
        }

        try
        {
            EnsureNoOwnershipManifestPathCollisionWithPhysicalTargets(plan);
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    private void EnsureNoFilesystemBackedPhysicalTargetKindSamePathConflicts(
        ConfigurationChangePlan plan
    )
    {
        if (fileSystem is null)
        {
            return;
        }

        if (
            plan
                .Changes.Where(HasCollisionCheckedPhysicalTargetPath)
                .Select(change =>
                    (
                        TargetPath: Path.TrimEndingDirectorySeparator(
                            GetNormalizablePhysicalPath(
                                fileSystem,
                                change.TargetPathOrName,
                                $"{change.TargetKind} target"
                            )
                        ),
                        change.TargetKind
                    )
                )
                .GroupBy(target => target.TargetPath, GetPathIdentityComparer())
                .Any(group => group.Select(target => target.TargetKind).Distinct().Skip(1).Any())
        )
        {
            throw new ArgumentException(
                "Protocol violation: physical configuration targets must not share the same "
                    + "physical target path with another target kind.",
                nameof(plan)
            );
        }
    }

    private string? GetFilesystemBackedPhysicalTargetKindSamePathConflictViolation(
        ConfigurationChangePlan plan
    )
    {
        try
        {
            EnsureNoFilesystemBackedPhysicalTargetKindSamePathConflicts(plan);
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    private static string GetNormalizablePhysicalPath(
        IFileSystem fileSystem,
        string path,
        string description
    )
    {
        try
        {
            return fileSystem.GetFullPath(path);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or IOException)
        {
            throw new ArgumentException(
                $"Protocol violation: {description} path must be a normalizable physical path.",
                nameof(path),
                exception
            );
        }
    }

    private static string? GetCiTemporaryFileUnsupportedOperationViolation(
        ConfigurationChangePlan plan
    ) =>
        plan.Changes.Any(change =>
            change.TargetKind == ConfigurationTargetKind.CiTemporaryFile
            && !IsSupportedCiTemporaryFileOperation(change.Operation)
        )
            ? "Protocol violation: CI temporary file targets currently support only create, "
                + "update, refresh, set, and remove operations."
            : null;

    private static string? GetReservedInternalPhysicalTargetPathViolation(
        ConfigurationChangePlan plan
    ) =>
        plan.Changes.Any(change =>
            IsPhysicalFileSystemTarget(change.TargetKind)
            && IsReservedInternalConfigurationPathArtifact(change.TargetPathOrName)
        )
            ? "Protocol violation: physical configuration targets must not use reserved "
                + "internal filesystem artifact paths."
            : null;

    private static string? GetCiTemporaryFilePlanWholeFileOwnershipViolation(
        ConfigurationChangePlan plan
    )
    {
        if (plan.Scope != ConfigurationScope.CiTemporary || plan.TemporaryContainer is null)
        {
            return null;
        }

        if (
            plan
                .Changes.Where(change =>
                    change.TargetKind == ConfigurationTargetKind.CiTemporaryFile
                )
                .GroupBy(
                    change => CreateCiTemporaryFileWholeFileIdentity(change.TargetPathOrName),
                    ConfigurationPathIdentityComparer.Instance
                )
                .Any(group => group.Count() > 1)
        )
        {
            return "Protocol violation: CI temporary file targets are whole-file ownership records "
                + "and support at most one change per target.";
        }

        string[] ciTemporaryFileTargets = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
            .Select(CreatePlanningPhysicalPathIdentity)
            .ToArray();
        for (var ancestorIndex = 0; ancestorIndex < ciTemporaryFileTargets.Length; ancestorIndex++)
        {
            for (var childIndex = 0; childIndex < ciTemporaryFileTargets.Length; childIndex++)
            {
                if (ancestorIndex == childIndex)
                {
                    continue;
                }

                if (
                    IsConfigurationPathUnderDirectory(
                        ciTemporaryFileTargets[ancestorIndex],
                        ciTemporaryFileTargets[childIndex]
                    )
                )
                {
                    return "Protocol violation: CI temporary file targets are whole-file "
                        + "ownership records and must not contain target paths that are parent "
                        + "paths of other CI temporary file targets.";
                }
            }
        }

        return plan
            .Changes.Select(change =>
                (
                    TargetPath: CreatePlanningPhysicalPathIdentity(change),
                    change.TargetKind
                )
            )
            .GroupBy(
                target => target.TargetPath,
                ConfigurationPathIdentityComparer.Instance
            )
            .Any(group =>
                group.Any(target => target.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
                && group.Select(target => target.TargetKind).Distinct().Skip(1).Any()
            )
            ? "Protocol violation: CI temporary file targets are whole-file ownership records "
                + "and must not share the same physical target path with another target kind."
            : null;
    }

    private static string? GetPhysicalTargetKindSamePathConflictViolation(
        ConfigurationChangePlan plan
    ) =>
        plan
            .Changes.Where(HasCollisionCheckedPhysicalTargetPath)
            .Select(change =>
                (
                    TargetPath: CreatePlanningPhysicalPathIdentity(change),
                    change.TargetKind
                )
            )
            .GroupBy(
                target => target.TargetPath,
                ConfigurationPathIdentityComparer.Instance
            )
            .Any(group => group.Select(target => target.TargetKind).Distinct().Skip(1).Any())
            ? "Protocol violation: physical configuration targets must not share the same "
                + "physical target path with another target kind."
            : null;

    private static ConfigurationPlanResult CreatePlannedResult(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        ConfigurationPlannedOperation[] plannedOperations =
            ConfigurationPlanProjector.CreatePlannedOperations(plan);

        ConfigurationOwnershipManifest ownershipManifest =
            ConfigurationPlanProjector.CreateOwnershipManifest(plan, plannedOperations);
        ConfigurationOwnershipManifestPolicy.EnsureValid(ownershipManifest);

        return new ConfigurationPlanResult
        {
            Plan = ConfigurationPlanProjector.CreateDryRunPlan(plan, plannedOperations),
            Operation = operation,
            State = ConfigurationPlanState.Planned,
            Changes = plannedOperations
                .Select(plannedOperation => plannedOperation.Change)
                .ToArray(),
            PlannedOperations = plannedOperations,
            OwnershipManifest = ownershipManifest,
        };
    }

    private ConfigurationOwnershipManifest? Execute(
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        lock (ExecutionLock)
        {
            IFileSystem executionFileSystem = fileSystem!;
            string manifestPath = ownershipManifestPath!;
            using IDisposable crossProcessExecutionLock = AcquireConfigurationExecutionLock(
                executionFileSystem,
                plan,
                manifestPath
            );
            EnsureManifestParentChainIsUsable(executionFileSystem, manifestPath);
            EnsurePathIsNotUnsupportedReparsePoint(
                executionFileSystem,
                manifestPath,
                "configuration ownership manifest"
            );
            ConfigurationOwnershipManifest ownershipManifest =
                plannedResult.OwnershipManifest
                ?? throw new InvalidOperationException(
                    "Configuration execution requires a projected ownership manifest."
                );
            FileRollbackSnapshot manifestSnapshot = CaptureRollbackSnapshot(
                executionFileSystem,
                manifestPath
            );
            ValidateExistingManifest(executionFileSystem, plan, manifestSnapshot, operation);
            Dictionary<string, FileRollbackSnapshot> targetSnapshots = CaptureTargetSnapshots(
                executionFileSystem,
                plan
            );
            ConfigurationOwnershipManifest? mergedOwnershipManifestForApply = null;

            ValidateBeforeState(executionFileSystem, targetSnapshots, plan);
            if (operation == ConfigurationPlanOperation.Apply)
            {
                mergedOwnershipManifestForApply = CreateMergedManifestForApply(
                    executionFileSystem,
                    manifestSnapshot,
                    ownershipManifest,
                    plan
                );
            }

            ContainerRollbackSnapshot containerSnapshot = CaptureContainerRollbackSnapshot(
                executionFileSystem,
                plan
            );

            var completedWrites = new Stack<FileRollbackSnapshot>();
            try
            {
                foreach (ConfigurationChange change in plan.Changes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureTargetParentChainIsUsable(executionFileSystem, plan, change);
                    EnsureTargetIsNotSymbolicLink(executionFileSystem, change.TargetPathOrName);
                    FileRollbackSnapshot currentSnapshot = ValidateCurrentTargetBeforeMutation(
                        executionFileSystem,
                        change
                    );
                    ExecuteChangeWithRollbackRegistration(
                        executionFileSystem,
                        change,
                        currentSnapshot,
                        completedWrites
                    );
                }

                manifestSnapshot = ValidateCurrentManifestBeforeMutation(
                    executionFileSystem,
                    manifestPath,
                    plan,
                    operation
                );
                if (operation == ConfigurationPlanOperation.Remove)
                {
                    ConfigurationOwnershipManifest? remainingManifest = CommitManifestRemove(
                        executionFileSystem,
                        manifestPath,
                        manifestSnapshot,
                        ownershipManifest,
                        plan,
                        completedWrites
                    );
                    if (remainingManifest is null)
                    {
                        DeleteTemporaryContainerAfterFullRemove(
                            executionFileSystem,
                            plan,
                            manifestPath,
                            containerSnapshot
                        );
                    }

                    return remainingManifest;
                }
                else
                {
                    ConfigurationOwnershipManifest mergedOwnershipManifest =
                        mergedOwnershipManifestForApply
                        ?? throw new InvalidOperationException(
                            "Configuration apply execution requires a precomputed merged manifest."
                        );
                    string manifestContents = ConfigurationOwnershipManifestSerializer.Serialize(
                        mergedOwnershipManifest
                    );
                    ExecuteAtomicWriteWithRollbackRegistration(
                        executionFileSystem,
                        manifestPath,
                        manifestContents,
                        options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
                        snapshot: manifestSnapshot,
                        expectedCurrentHashForRollback: ComputeSha256(manifestContents),
                        completedWrites: completedWrites
                    );
                    return mergedOwnershipManifest;
                }
            }

            catch (Exception exception)
            {
                RollBackWithoutMaskingConflict(executionFileSystem, completedWrites, exception);
                DeleteTemporaryContainerAfterRollback(executionFileSystem, plan, containerSnapshot);
                throw;
            }
        }
    }

    private ConfigurationPlanResult SimulateFilesystemBackedDryRun(
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan
    )
    {
        lock (ExecutionLock)
        {
            IFileSystem dryRunFileSystem = fileSystem!;
            string manifestPath = ownershipManifestPath!;
            if (IsProjectionOnlyPhysicalTargetPlan(plan))
            {
                return SimulateProjectionOnlyPhysicalTargetDryRun(
                    dryRunFileSystem,
                    manifestPath,
                    plannedResult,
                    plan
                );
            }

            EnsureExecutableGenericTargetPlanShapeSupported(dryRunFileSystem, plan);
            EnsureSupportedChangeOperations(plan);

            EnsureManifestParentChainIsUsable(dryRunFileSystem, manifestPath);
            EnsurePathIsNotUnsupportedReparsePoint(
                dryRunFileSystem,
                manifestPath,
                "configuration ownership manifest"
            );
            FileRollbackSnapshot manifestSnapshot = CaptureRollbackSnapshot(
                dryRunFileSystem,
                manifestPath
            );
            bool hasRemoveChanges = PlanHasOwnershipRemoveChanges(plan);
            bool hasValueWritingChanges = PlanHasValueWritingChanges(plan);
            if (hasRemoveChanges)
            {
                ValidateExistingManifest(
                    dryRunFileSystem,
                    plan,
                    manifestSnapshot,
                    ConfigurationPlanOperation.Remove
                );
            }

            if (hasValueWritingChanges)
            {
                ValidateExistingManifest(
                    dryRunFileSystem,
                    plan,
                    manifestSnapshot,
                    ConfigurationPlanOperation.Apply
                );
            }

            Dictionary<string, FileRollbackSnapshot> targetSnapshots =
                CaptureTargetSnapshots(dryRunFileSystem, plan);
            ValidateBeforeState(dryRunFileSystem, targetSnapshots, plan);

            ConfigurationOwnershipManifest projectedManifest =
                plannedResult.OwnershipManifest
                ?? throw new InvalidOperationException(
                    "Configuration dry-run requires a projected ownership manifest."
                );
            ConfigurationOwnershipManifest? baseManifest = manifestSnapshot.Existed
                ? ConfigurationOwnershipManifestSerializer.Deserialize(manifestSnapshot.Contents!)
                : null;
            if (hasRemoveChanges)
            {
                baseManifest = CreateRemainingManifestAfterRemove(
                    dryRunFileSystem,
                    baseManifest!,
                    projectedManifest,
                    plan
                );
            }

            ConfigurationOwnershipManifest? simulatedOwnershipManifest = hasValueWritingChanges
                ? CreateMergedManifestForApply(
                    dryRunFileSystem,
                    baseManifest,
                    projectedManifest,
                    plan
                )
                : baseManifest;

            return plannedResult with
            {
                OwnershipManifest = simulatedOwnershipManifest,
            };
        }
    }

    private static ConfigurationPlanResult SimulateProjectionOnlyPhysicalTargetDryRun(
        IFileSystem dryRunFileSystem,
        string manifestPath,
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan
    )
    {
        EnsureManifestParentChainIsUsable(dryRunFileSystem, manifestPath);
        EnsurePathIsNotUnsupportedReparsePoint(
            dryRunFileSystem,
            manifestPath,
            "configuration ownership manifest"
        );
        FileRollbackSnapshot manifestSnapshot = CaptureRollbackSnapshot(
            dryRunFileSystem,
            manifestPath
        );
        bool hasRemoveChanges = PlanHasOwnershipRemoveChanges(plan);
        bool hasValueWritingChanges = PlanHasValueWritingChanges(plan);
        if (hasRemoveChanges)
        {
            ValidateExistingManifest(
                dryRunFileSystem,
                plan,
                manifestSnapshot,
                ConfigurationPlanOperation.Remove
            );
        }

        if (hasValueWritingChanges || !hasRemoveChanges)
        {
            ValidateExistingManifest(
                dryRunFileSystem,
                plan,
                manifestSnapshot,
                ConfigurationPlanOperation.Apply
            );
        }

        ConfigurationOwnershipManifest projectedManifest =
            plannedResult.OwnershipManifest
            ?? throw new InvalidOperationException(
                "Configuration dry-run requires a projected ownership manifest."
            );
        ConfigurationOwnershipManifest? baseManifest = manifestSnapshot.Existed
            ? ConfigurationOwnershipManifestSerializer.Deserialize(manifestSnapshot.Contents!)
            : null;
        if (hasRemoveChanges)
        {
            baseManifest = CreateRemainingManifestAfterRemove(
                dryRunFileSystem,
                baseManifest!,
                projectedManifest,
                plan
            );
        }

        ConfigurationOwnershipManifest projectedNonRemoveManifest =
            CreateProjectionOnlyNonRemoveManifest(projectedManifest);
        ConfigurationOwnershipManifest? simulatedOwnershipManifest =
            projectedNonRemoveManifest.Entries.Count > 0
                ? CreateMergedManifestForProjectionOnlyDryRun(
                    dryRunFileSystem,
                    baseManifest,
                    projectedNonRemoveManifest,
                    plan
                )
                : baseManifest;

        return plannedResult with
        {
            OwnershipManifest = simulatedOwnershipManifest,
        };
    }

    private static IDisposable AcquireConfigurationExecutionLock(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        string manifestPath
    )
    {
        if (fileSystem is not IFileSystemMutationLock mutationLock)
        {
            return NullDisposable.Instance;
        }

        string productOwnedRoot = fileSystem.GetFullPath(plan.TemporaryContainer!.ProductOwnedPath);
        string lockDirectory = CreateConfigurationExecutionLockDirectory(
            fileSystem,
            productOwnedRoot,
            manifestPath
        );

        return mutationLock.AcquireMutationLock(lockDirectory);
    }

    internal static string CreateConfigurationExecutionLockDirectory(
        IFileSystem fileSystem,
        string productOwnedRoot,
        string manifestPath
    )
    {
        string manifestFullPath = fileSystem.GetFullPath(manifestPath);
        char directorySeparator = GetLifecyclePathDirectorySeparator(
            manifestFullPath,
            productOwnedRoot
        );
        string? manifestDirectory = GetLifecyclePathDirectoryName(
            manifestFullPath,
            directorySeparator
        );
        if (string.IsNullOrEmpty(manifestDirectory))
        {
            manifestDirectory = Directory.GetCurrentDirectory();
        }

        string lockName = CreateLifecycleLockName(manifestFullPath, productOwnedRoot);
        string lockRoot = fileSystem.GetFullPath(
            CombineLifecyclePath(
                manifestDirectory,
                LifecycleLockDirectoryName,
                directorySeparator
            )
        );
        if (IsLifecyclePathSameOrUnderDirectory(productOwnedRoot, lockRoot, directorySeparator))
        {
            string? productOwnedParent = GetLifecyclePathDirectoryName(
                TrimEndingLifecyclePathDirectorySeparators(productOwnedRoot, directorySeparator),
                directorySeparator
            );
            if (string.IsNullOrEmpty(productOwnedParent))
            {
                throw new NotSupportedException(
                    "Filesystem-backed configuration execution requires a lifecycle lock location "
                        + "outside the product-owned temporary container."
                );
            }

            lockRoot = fileSystem.GetFullPath(
                CombineLifecyclePath(
                    productOwnedParent,
                    LifecycleLockDirectoryName,
                    directorySeparator
                )
            );
            if (IsLifecyclePathSameOrUnderDirectory(productOwnedRoot, lockRoot, directorySeparator))
            {
                throw new NotSupportedException(
                    "Filesystem-backed configuration execution requires a lifecycle lock location "
                        + "outside the product-owned temporary container."
                );
            }

        }

        return CombineLifecyclePath(lockRoot, lockName, directorySeparator);
    }

    private static ConfigurationOwnershipManifest? CommitManifestRemove(
        IFileSystem fileSystem,
        string manifestPath,
        FileRollbackSnapshot manifestSnapshot,
        ConfigurationOwnershipManifest projectedManifest,
        ConfigurationChangePlan plan,
        Stack<FileRollbackSnapshot> completedWrites
    )
    {
        ConfigurationOwnershipManifest existingManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestSnapshot.Contents!);
        ConfigurationOwnershipManifest? remainingManifest = CreateRemainingManifestAfterRemove(
            fileSystem,
            existingManifest,
            projectedManifest,
            plan
        );
        if (remainingManifest is null)
        {
            ExecuteDeleteWithRollbackRegistration(
                fileSystem,
                manifestPath,
                manifestSnapshot,
                expectedCurrentHashForRollback: null,
                completedWrites
            );
            return null;
        }

        string remainingManifestContents = ConfigurationOwnershipManifestSerializer.Serialize(
            remainingManifest
        );
        ExecuteAtomicWriteWithRollbackRegistration(
            fileSystem,
            manifestPath,
            remainingManifestContents,
            options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
            snapshot: manifestSnapshot,
            expectedCurrentHashForRollback: ComputeSha256(remainingManifestContents),
            completedWrites: completedWrites
        );
        return remainingManifest;
    }

    private static ConfigurationOwnershipManifest CreateMergedManifestForApply(
        IFileSystem fileSystem,
        FileRollbackSnapshot manifestSnapshot,
        ConfigurationOwnershipManifest projectedManifest,
        ConfigurationChangePlan plan
    )
    {
        ConfigurationOwnershipManifest? existingManifest = manifestSnapshot.Existed
            ? ConfigurationOwnershipManifestSerializer.Deserialize(manifestSnapshot.Contents!)
            : null;
        return CreateMergedManifestForApply(fileSystem, existingManifest, projectedManifest, plan);
    }

    private static ConfigurationOwnershipManifest CreateMergedManifestForApply(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest? existingManifest,
        ConfigurationOwnershipManifest projectedManifest,
        ConfigurationChangePlan plan
    )
    {
        ConfigurationOwnershipManifestEntry[] projectedValueEntries = projectedManifest
            .Entries.Where(entry => IsValueWritingOperation(entry.Operation))
            .ToArray();
        if (existingManifest is null)
        {
            ConfigurationOwnershipManifest valueOnlyManifest = projectedManifest with
            {
                Entries = projectedValueEntries
                    .Select((entry, index) => entry with { Sequence = index + 1 })
                    .ToArray(),
            };
            ValidateCiTemporaryFileManifestWholeFileOwnership(fileSystem, plan, valueOnlyManifest);
            return valueOnlyManifest;
        }

        var replacements = projectedValueEntries.ToDictionary(
            entry => CreateEntryMergeKey(fileSystem, plan, entry),
            GetEntryMergeKeyComparer()
        );
        var replacedKeys = new HashSet<string>(GetEntryMergeKeyComparer());
        var mergedEntries = new List<ConfigurationOwnershipManifestEntry>();

        foreach (ConfigurationOwnershipManifestEntry existingEntry in existingManifest.Entries)
        {
            if (!PlanTargetsEntry(fileSystem, plan, existingEntry))
            {
                mergedEntries.Add(existingEntry);
                continue;
            }

            string key = CreateEntryMergeKey(fileSystem, plan, existingEntry);
            if (replacements.TryGetValue(key, out ConfigurationOwnershipManifestEntry? replacement))
            {
                mergedEntries.Add(replacement);
                replacedKeys.Add(key);
            }
        }

        mergedEntries.AddRange(
            projectedValueEntries.Where(entry =>
                !replacedKeys.Contains(CreateEntryMergeKey(fileSystem, plan, entry))
            )
        );

        ConfigurationOwnershipManifest mergedManifest = projectedManifest with
        {
            ContainsCredentialMaterial =
                projectedManifest.ContainsCredentialMaterial
                || mergedEntries.Any(entry => entry.IsSecretValue),
            Entries = mergedEntries.Select((entry, index) => entry with { Sequence = index + 1 })
                .ToArray(),
        };
        ConfigurationOwnershipManifestPolicy.EnsureValid(mergedManifest);
        ValidatePhysicalTargetManifestEntries(fileSystem, mergedManifest);
        ValidateCiTemporaryFileManifestWholeFileOwnership(fileSystem, plan, mergedManifest);
        return mergedManifest;
    }

    private static ConfigurationOwnershipManifest CreateMergedManifestForProjectionOnlyDryRun(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest? existingManifest,
        ConfigurationOwnershipManifest projectedManifest,
        ConfigurationChangePlan plan
    )
    {
        if (existingManifest is null)
        {
            return projectedManifest;
        }

        var replacements = projectedManifest.Entries.ToDictionary(
            entry => CreateEntryMergeKey(fileSystem, plan, entry),
            GetEntryMergeKeyComparer()
        );
        var replacedKeys = new HashSet<string>(GetEntryMergeKeyComparer());
        var mergedEntries = new List<ConfigurationOwnershipManifestEntry>();

        foreach (ConfigurationOwnershipManifestEntry existingEntry in existingManifest.Entries)
        {
            if (!PlanTargetsEntry(fileSystem, plan, existingEntry))
            {
                mergedEntries.Add(existingEntry);
                continue;
            }

            string key = CreateEntryMergeKey(fileSystem, plan, existingEntry);
            if (replacements.TryGetValue(key, out ConfigurationOwnershipManifestEntry? replacement))
            {
                mergedEntries.Add(replacement);
                replacedKeys.Add(key);
            }
        }

        mergedEntries.AddRange(
            projectedManifest.Entries.Where(entry =>
                !replacedKeys.Contains(CreateEntryMergeKey(fileSystem, plan, entry))
            )
        );

        ConfigurationOwnershipManifest mergedManifest = projectedManifest with
        {
            ContainsCredentialMaterial =
                projectedManifest.ContainsCredentialMaterial
                || mergedEntries.Any(entry => entry.IsSecretValue),
            Entries = mergedEntries.Select((entry, index) => entry with { Sequence = index + 1 })
                .ToArray(),
        };
        ConfigurationOwnershipManifestPolicy.EnsureValid(mergedManifest);
        ValidatePhysicalTargetManifestEntries(fileSystem, mergedManifest);
        ValidateCiTemporaryFileManifestWholeFileOwnership(fileSystem, plan, mergedManifest);
        return mergedManifest;
    }

    private static ConfigurationOwnershipManifest CreateProjectionOnlyNonRemoveManifest(
        ConfigurationOwnershipManifest projectedManifest
    ) =>
        projectedManifest with
        {
            Entries = projectedManifest
                .Entries.Where(entry => !IsOwnershipRemoveOperation(entry.Operation))
                .Select((entry, index) => entry with { Sequence = index + 1 })
                .ToArray(),
        };

    private static ConfigurationOwnershipManifest? CreateRemainingManifestAfterRemove(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest existingManifest,
        ConfigurationOwnershipManifest projectedManifest,
        ConfigurationChangePlan plan
    )
    {
        ConfigurationOwnershipManifestEntry[] remainingEntries = existingManifest
            .Entries.Where(entry => !PlanRemovesEntry(fileSystem, plan, entry))
            .Select((entry, index) => entry with { Sequence = index + 1 })
            .ToArray();
        if (remainingEntries.Length == 0)
        {
            return null;
        }

        ConfigurationOwnershipManifest remainingManifest = projectedManifest with
        {
            ContainsCredentialMaterial =
                projectedManifest.ContainsCredentialMaterial
                || remainingEntries.Any(entry => entry.IsSecretValue),
            Entries = remainingEntries,
        };
        ConfigurationOwnershipManifestPolicy.EnsureValid(remainingManifest);
        return remainingManifest;
    }

    private static bool PlanRemovesEntry(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifestEntry entry
    ) =>
        plan.Changes.Any(change =>
            IsOwnershipRemoveOperation(change.Operation)
            && PlanChangeMatchesEntry(fileSystem, plan, change, entry)
        );

    private static bool PlanTargetsEntry(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifestEntry entry
    ) =>
        plan.Changes.Any(change => PlanChangeMatchesEntry(fileSystem, plan, change, entry));

    private static bool PlanChangeMatchesEntry(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationChange change,
        ConfigurationOwnershipManifestEntry entry
    )
    {
        if (change.TargetKind != entry.TargetKind)
        {
            return false;
        }

        if (change.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
        {
            return string.Equals(
                CreateCiTemporaryFileWholeFileIdentity(
                    fileSystem,
                    plan,
                    change.TargetPathOrName
                ),
                CreateCiTemporaryFileWholeFileIdentity(
                    fileSystem,
                    plan,
                    entry.TargetPathOrName
                ),
                GetPathIdentityComparison()
            );
        }

        StringComparison targetPathComparison =
            HasCollisionCheckedPhysicalTargetPath(change.TargetKind)
            && HasCollisionCheckedPhysicalTargetPath(entry.TargetKind)
                ? GetPathIdentityComparison()
                : StringComparison.Ordinal;
        string changeTargetPathOrName =
            targetPathComparison == StringComparison.Ordinal
                ? change.TargetPathOrName
                : CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName);
        string entryTargetPathOrName =
            targetPathComparison == StringComparison.Ordinal
                ? entry.TargetPathOrName
                : CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName);
        return string.Equals(changeTargetPathOrName, entryTargetPathOrName, targetPathComparison)
            && string.Equals(change.Key, entry.Key, StringComparison.Ordinal);
    }

    private static string CreateEntryMergeKey(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifestEntry entry
    )
    {
        if (entry.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
        {
            string targetIdentity = CreateCiTemporaryFileManifestEntryIdentity(
                fileSystem,
                plan,
                entry.TargetPathOrName
            );
            return $"{entry.TargetKind}\n{targetIdentity}";
        }

        return CreateEntryKey(entry);
    }

    private static StringComparer GetEntryMergeKeyComparer() => GetPathIdentityComparer();

    private static string CreateEntryKey(ConfigurationOwnershipManifestEntry entry) =>
        $"{entry.TargetKind}\n{entry.TargetPathOrName}\n{entry.Key}";

    private static void ValidateCiTemporaryFileManifestWholeFileOwnership(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest manifest
    )
    {
        if (
            manifest
                .Entries.Where(entry =>
                    entry.TargetKind == ConfigurationTargetKind.CiTemporaryFile
                )
                .GroupBy(
                    entry => CreateCiTemporaryFileManifestEntryIdentity(
                        fileSystem,
                        plan,
                        entry.TargetPathOrName
                    ),
                    GetPathIdentityComparer()
                )
                .Any(group => group.Count() > 1)
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: CI temporary file entries are "
                    + "whole-file ownership records and must not contain multiple entries for "
                    + "the same target."
            );
        }

        ValidateCiTemporaryFileManifestHasNoParentChildTargetConflicts(fileSystem, plan, manifest);

        IEnumerable<(string TargetPath, ConfigurationTargetKind TargetKind)> manifestTargets =
            manifest.Entries.Select(entry =>
                (
                    entry.TargetKind == ConfigurationTargetKind.CiTemporaryFile
                        ? CreateCiTemporaryFileManifestEntryIdentity(
                            fileSystem,
                            plan,
                            entry.TargetPathOrName
                        )
                        : CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName),
                    entry.TargetKind
                )
            );
        IEnumerable<(string TargetPath, ConfigurationTargetKind TargetKind)> planTargets = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
            .Select(change =>
                (
                    CreateCiTemporaryFileWholeFileIdentity(
                        fileSystem,
                        plan,
                        change.TargetPathOrName
                    ),
                    change.TargetKind
                )
            );
        if (
            manifestTargets
                .Concat(planTargets)
                .GroupBy(target => target.TargetPath, GetPathIdentityComparer())
                .Any(group =>
                    group.Any(target =>
                        target.TargetKind == ConfigurationTargetKind.CiTemporaryFile
                    )
                    && group
                        .Select(target => target.TargetKind)
                        .Distinct()
                        .Skip(1)
                        .Any()
                )
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: CI temporary file entries are "
                    + "whole-file ownership records and must not share the same physical target "
                    + "path with entries of another target kind."
            );
        }
    }

    private static void ValidateCiTemporaryFileManifestHasNoParentChildTargetConflicts(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest manifest
    )
    {
        string[] targetPaths = manifest
            .Entries.Where(entry => entry.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
            .Select(entry =>
                CreateCiTemporaryFileManifestEntryIdentity(fileSystem, plan, entry.TargetPathOrName)
            )
            .ToArray();

        for (var ancestorIndex = 0; ancestorIndex < targetPaths.Length; ancestorIndex++)
        {
            for (var childIndex = 0; childIndex < targetPaths.Length; childIndex++)
            {
                if (ancestorIndex == childIndex)
                {
                    continue;
                }

                if (IsPathUnderDirectory(targetPaths[ancestorIndex], targetPaths[childIndex]))
                {
                    throw new InvalidOperationException(
                        "Configuration ownership manifest conflict: CI temporary file entries are "
                            + "whole-file ownership records and must not contain target paths "
                            + "that are parent paths of other CI temporary file targets."
                    );
                }
            }
        }
    }

    private static void EnsureExecutableGenericTargetPlanSupported(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan
    )
    {
        EnsureExecutableGenericTargetPlanShapeSupported(fileSystem, plan);
        EnsureExecutableGenericTargetPlanFilesystemStateSupported(fileSystem, plan);
    }

    private static void EnsureExecutableGenericTargetPlanShapeSupported(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan
    )
    {
        if (plan.Scope != ConfigurationScope.CiTemporary)
        {
            throw new NotSupportedException(
                "Filesystem-backed configuration execution supports generic file targets only "
                    + "for CI temporary plans."
            );
        }

        ConfigurationTemporaryContainer temporaryContainer =
            plan.TemporaryContainer
            ?? throw new NotSupportedException(
                "Filesystem-backed configuration execution requires a declared CI temporary "
                    + "container."
            );
        if (string.IsNullOrWhiteSpace(temporaryContainer.ProductOwnedPath))
        {
            throw new NotSupportedException(
                "Filesystem-backed configuration execution requires a valid product-owned "
                    + "temporary container."
            );
        }

        string productOwnedRoot = fileSystem.GetFullPath(temporaryContainer.ProductOwnedPath);
        if (!fileSystem.IsPathFullyQualified(productOwnedRoot))
        {
            throw new NotSupportedException(
                "Filesystem-backed configuration execution requires a fully qualified "
                    + "product-owned temporary container."
            );
        }

        EnsureConditionalFileMutationsSupported(fileSystem);
        EnsureCiTemporaryFilePlanHasWholeFileOwnership(fileSystem, plan);

        foreach (ConfigurationChange change in plan.Changes)
        {
            if (change.TargetKind != ConfigurationTargetKind.CiTemporaryFile)
            {
                throw new NotSupportedException(
                    "Filesystem-backed configuration execution supports only CI temporary file "
                        + "targets."
                );
            }

            string targetPath = fileSystem.GetFullPath(change.TargetPathOrName);
            if (IsReservedInternalFileSystemArtifact(targetPath))
            {
                throw new NotSupportedException(
                    "Filesystem-backed configuration execution rejects reserved internal "
                        + "filesystem artifact targets."
                );
            }

            if (!IsPathUnderDirectory(productOwnedRoot, targetPath))
            {
                throw new NotSupportedException(
                    "Filesystem-backed configuration execution supports only targets under the "
                        + "declared product-owned temporary container."
                );
            }
        }

        EnsureCiTemporaryFilePlanHasNoParentChildTargetConflicts(fileSystem, plan);
    }

    private static void EnsureExecutableGenericTargetPlanFilesystemStateSupported(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan
    )
    {
        foreach (ConfigurationChange change in plan.Changes)
        {
            EnsureTargetParentChainIsUsable(fileSystem, plan, change);
            EnsureTargetIsNotSymbolicLink(fileSystem, change.TargetPathOrName);
        }
    }

    private static void EnsureCiTemporaryFilePlanHasWholeFileOwnership(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan
    )
    {
        if (
            plan
                .Changes.Where(change =>
                    change.TargetKind == ConfigurationTargetKind.CiTemporaryFile
                )
                .GroupBy(
                    change => CreateCiTemporaryFileWholeFileIdentity(
                        fileSystem,
                        plan,
                        change.TargetPathOrName
                    ),
                    GetPathIdentityComparer()
                )
                .Any(group => group.Count() > 1)
        )
        {
            throw new NotSupportedException(
                "Filesystem-backed configuration execution treats CI temporary file targets as "
                    + "whole-file ownership records and supports at most one change per target."
            );
        }
    }

    private static void EnsureCiTemporaryFilePlanHasNoParentChildTargetConflicts(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan
    )
    {
        string[] targetPaths = plan
            .Changes.Select(change =>
                Path.TrimEndingDirectorySeparator(fileSystem.GetFullPath(change.TargetPathOrName))
            )
            .ToArray();

        for (var ancestorIndex = 0; ancestorIndex < targetPaths.Length; ancestorIndex++)
        {
            for (var childIndex = 0; childIndex < targetPaths.Length; childIndex++)
            {
                if (ancestorIndex == childIndex)
                {
                    continue;
                }

                if (IsPathUnderDirectory(targetPaths[ancestorIndex], targetPaths[childIndex]))
                {
                    throw new NotSupportedException(
                        "Filesystem-backed configuration execution rejects CI temporary file "
                            + "targets that are parent paths of other CI temporary file targets."
                    );
                }
            }
        }
    }

    private static void EnsureTargetParentChainIsUsable(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationChange change
    )
    {
        string productOwnedRoot = fileSystem.GetFullPath(plan.TemporaryContainer!.ProductOwnedPath);
        string targetPath = fileSystem.GetFullPath(change.TargetPathOrName);
        if (!IsPathUnderDirectory(productOwnedRoot, targetPath))
        {
            throw new NotSupportedException(
                "Filesystem-backed configuration execution supports only targets under the "
                    + "declared product-owned temporary container."
            );
        }

        string? targetParent = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(targetParent))
        {
            targetParent = Directory.GetCurrentDirectory();
        }

        // Check the full lexical parent chain, including ancestors above the declared container,
        // because a symlinked ancestor can redirect the container itself outside the intended tree.
        foreach (string directory in EnumerateDirectoryChain(targetParent))
        {
            try
            {
                if (IsUnsupportedLinkOrReparsePoint(fileSystem, directory))
                {
                    throw new NotSupportedException(
                        "Filesystem-backed configuration execution rejects symbolic-link or "
                            + "reparse-point "
                            + "directories in CI temporary target parent paths."
                    );
                }

                if (!fileSystem.DirectoryExists(directory) && fileSystem.FileExists(directory))
                {
                    throw new NotSupportedException(
                        "Filesystem-backed configuration execution rejects non-directory "
                            + "entries in CI temporary target parent paths."
                    );
                }
            }
            catch (FileNotFoundException)
            {
                // A concurrent remover can make a path disappear during the link check.
                // Treat the disappeared component as not currently usable; the conditional
                // mutation will fail closed if the before-state changed.
            }
            catch (DirectoryNotFoundException)
            {
                // See FileNotFoundException handling above.
            }
        }
    }

    private static void EnsureManifestParentChainIsUsable(
        IFileSystem fileSystem,
        string manifestPath
    )
    {
        string fullManifestPath = fileSystem.GetFullPath(manifestPath);
        string? manifestParent = Path.GetDirectoryName(fullManifestPath);
        if (string.IsNullOrEmpty(manifestParent))
        {
            manifestParent = Directory.GetCurrentDirectory();
        }

        foreach (string directory in EnumerateDirectoryChain(manifestParent))
        {
            try
            {
                if (IsUnsupportedLinkOrReparsePoint(fileSystem, directory))
                {
                    throw new NotSupportedException(
                        "Filesystem-backed configuration execution rejects symbolic-link or "
                            + "reparse-point directories in ownership manifest parent paths."
                    );
                }

                if (!fileSystem.DirectoryExists(directory) && fileSystem.FileExists(directory))
                {
                    throw new NotSupportedException(
                        "Filesystem-backed configuration execution rejects non-directory entries "
                            + "in ownership manifest parent paths."
                    );
                }
            }
            catch (FileNotFoundException)
            {
                // Missing manifest parent directories are valid for first apply. They will be
                // created through conditional file mutation after the existing chain is proven
                // safe.
            }
            catch (DirectoryNotFoundException)
            {
                // See FileNotFoundException handling above.
            }
        }
    }

    private static void EnsureConditionalFileMutationsSupported(IFileSystem fileSystem)
    {
        if (!fileSystem.SupportsConditionalFileMutations)
        {
            throw new PlatformNotSupportedException(
                "Filesystem-backed configuration execution requires conditional file mutation "
                    + "support for dry-run/apply equivalence."
            );
        }
    }

    private static void EnsureTargetIsNotSymbolicLink(IFileSystem fileSystem, string targetPath)
    {
        EnsurePathIsNotUnsupportedReparsePoint(
            fileSystem,
            targetPath,
            "CI temporary file target"
        );
    }

    private static void EnsurePathIsNotUnsupportedReparsePoint(
        IFileSystem fileSystem,
        string path,
        string entryKind
    )
    {
        try
        {
            if (IsUnsupportedLinkOrReparsePoint(fileSystem, path))
            {
                throw new NotSupportedException(
                    "Filesystem-backed configuration execution rejects symbolic-link or "
                        + $"reparse-point {entryKind}s."
                );
            }
        }
        catch (FileNotFoundException)
        {
            // Missing entries are valid for create/set planning and for first apply manifest
            // creation. Before-state validation handles operations that require an existing file.
        }
        catch (DirectoryNotFoundException)
        {
            // See FileNotFoundException handling above.
        }
    }

    private static bool IsUnsupportedLinkOrReparsePoint(IFileSystem fileSystem, string path)
    {
        if (
            fileSystem is IFileSystemReparsePointSafety reparsePointSafety
            && reparsePointSafety.IsReparsePoint(path)
        )
        {
            return true;
        }

        return fileSystem.IsSymbolicLink(path);
    }

    private static Stack<string> EnumerateDirectoryChain(string path)
    {
        var directories = new Stack<string>();
        string? current = Path.TrimEndingDirectorySeparator(path);
        while (!string.IsNullOrEmpty(current))
        {
            directories.Push(current);
            string? parent = Path.GetDirectoryName(current);
            if (
                string.IsNullOrEmpty(parent)
                || string.Equals(parent, current, StringComparison.Ordinal)
            )
            {
                break;
            }

            current = parent;
        }

        return directories;
    }

    private static Dictionary<string, FileRollbackSnapshot> CaptureTargetSnapshots(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan
    )
    {
        var snapshots = new Dictionary<string, FileRollbackSnapshot>(GetPathIdentityComparer());
        foreach (ConfigurationChange change in plan.Changes)
        {
            if (!IsGenericFileTarget(change.TargetKind))
            {
                throw new NotSupportedException(
                    "Configuration apply/remove currently supports only generic file targets."
                );
            }

            snapshots.TryAdd(
                CreateCiTemporaryFileWholeFileIdentity(
                    fileSystem,
                    plan,
                    change.TargetPathOrName
                ),
                CaptureValidatedTargetRollbackSnapshot(fileSystem, plan, change)
            );
        }

        return snapshots;
    }

    private static FileRollbackSnapshot CaptureValidatedTargetRollbackSnapshot(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationChange change
    )
    {
        EnsureTargetParentChainIsUsable(fileSystem, plan, change);
        EnsureTargetIsNotSymbolicLink(fileSystem, change.TargetPathOrName);
        return CaptureRollbackSnapshot(fileSystem, change.TargetPathOrName);
    }

    private static ContainerRollbackSnapshot CaptureContainerRollbackSnapshot(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan
    )
    {
        string productOwnedPath = fileSystem.GetFullPath(plan.TemporaryContainer!.ProductOwnedPath);
        EnsurePathIsNotUnsupportedReparsePoint(fileSystem, productOwnedPath, "temporary container");
        if (!fileSystem.DirectoryExists(productOwnedPath))
        {
            return new ContainerRollbackSnapshot(productOwnedPath, Existed: false, [], []);
        }

        EnsureDirectoryChainHasNoUnsupportedLinkOrReparsePoint(
            fileSystem,
            productOwnedPath,
            "Temporary container rollback snapshot rejects symbolic-link or reparse-point "
                + "directories in container paths."
        );

        string[] existingEntries = EnumerateExistingTemporaryContainerEntriesNoFollow(
            fileSystem,
            productOwnedPath
        );
        string[] existingFiles = existingEntries
            .Append(Path.Combine(productOwnedPath, FileSystemLockFileName))
            .Where(path => IsExistingSafeSnapshotFile(fileSystem, path))
            .Distinct(GetPathIdentityComparer())
            .ToArray();
        string[] existingDirectories = existingEntries
            .Append(productOwnedPath)
            .Where(path => IsExistingSafeSnapshotDirectory(fileSystem, path))
            .Distinct(GetPathIdentityComparer())
            .ToArray();
        return new ContainerRollbackSnapshot(
            productOwnedPath,
            Existed: true,
            existingFiles,
            existingDirectories
        );
    }

    private static string[] EnumerateExistingTemporaryContainerEntriesNoFollow(
        IFileSystem fileSystem,
        string productOwnedPath
    )
    {
        if (fileSystem is not IFileSystemNoFollowEnumeration noFollowEnumeration)
        {
            throw new NotSupportedException(
                "Temporary container rollback snapshot requires no-follow filesystem enumeration."
            );
        }

        return noFollowEnumeration
            .EnumerateFileSystemEntriesNoFollow(productOwnedPath, "*", SearchOption.AllDirectories)
            .Select(fileSystem.GetFullPath)
            .Where(entry => IsPathUnderDirectory(productOwnedPath, entry))
            .Distinct(GetPathIdentityComparer())
            .ToArray();
    }

    private static bool IsExistingSafeSnapshotFile(IFileSystem fileSystem, string path)
    {
        try
        {
            if (IsReservedInternalFileSystemArtifact(path))
            {
                return fileSystem.FileExists(path);
            }

            if (IsUnsupportedLinkOrReparsePoint(fileSystem, path))
            {
                ThrowUnsupportedSnapshotEntry();
            }

            return fileSystem.FileExists(path);
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception)
            when (
                IsReservedInternalFileSystemArtifact(path)
                && exception is IOException or UnauthorizedAccessException or NotSupportedException
            )
        {
            return true;
        }
    }

    private static bool IsExistingSafeSnapshotDirectory(IFileSystem fileSystem, string path)
    {
        try
        {
            if (IsReservedInternalFileSystemArtifact(path))
            {
                return fileSystem.DirectoryExists(path);
            }

            if (IsUnsupportedLinkOrReparsePoint(fileSystem, path))
            {
                ThrowUnsupportedSnapshotEntry();
            }

            return fileSystem.DirectoryExists(path);
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception)
            when (
                IsReservedInternalFileSystemArtifact(path)
                && exception is IOException or UnauthorizedAccessException or NotSupportedException
            )
        {
            return false;
        }
    }

    private static void ThrowUnsupportedSnapshotEntry()
    {
        throw new NotSupportedException(
            "Temporary container rollback snapshot rejects symbolic-link or reparse-point "
                + "descendants."
        );
    }

    private static void EnsureOperationSupported(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        if (
            operation == ConfigurationPlanOperation.Apply
            && plan.Changes.Any(change => change.Operation == ConfigurationChangeOperation.Remove)
        )
        {
            throw new NotSupportedException(
                "Configuration apply execution does not support remove changes."
            );
        }

        if (
            operation == ConfigurationPlanOperation.Remove
            && plan.Changes.Any(change => IsValueWritingOperation(change.Operation))
        )
        {
            throw new NotSupportedException(
                "Configuration remove execution does not support value-writing changes."
            );
        }

        EnsureSupportedChangeOperations(plan);
    }

    private static void EnsureFilesystemBackedDryRunOperationSupported(
        ConfigurationChangePlan plan
    )
    {
        bool applySupported = !plan.Changes.Any(change =>
            IsOwnershipRemoveOperation(change.Operation)
        );
        bool removeSupported = !plan.Changes.Any(change =>
            IsValueWritingOperation(change.Operation)
        );
        if (applySupported || removeSupported)
        {
            return;
        }

        throw new NotSupportedException(
            "Filesystem-backed configuration dry-run does not support plans that cannot be "
                + "executed by apply or remove."
        );
    }

    private static void EnsureSupportedChangeOperations(ConfigurationChangePlan plan)
    {
        if (
            plan.Changes.Any(change =>
                change.TargetKind == ConfigurationTargetKind.CiTemporaryFile
                && !IsSupportedCiTemporaryFileOperation(change.Operation)
            )
        )
        {
            throw new NotSupportedException(
                "Configuration apply/remove currently supports CI temporary file create, update, "
                    + "refresh, set, and remove operations only."
            );
        }
    }

    private static bool IsSupportedCiTemporaryFileOperation(
        ConfigurationChangeOperation operation
    ) =>
        operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
                or ConfigurationChangeOperation.Remove;

    private static bool PlanHasOwnershipRemoveChanges(ConfigurationChangePlan plan) =>
        plan.Changes.Any(change => IsOwnershipRemoveOperation(change.Operation));

    private static bool IsOwnershipRemoveOperation(ConfigurationChangeOperation operation) =>
        operation
            is ConfigurationChangeOperation.Remove or ConfigurationChangeOperation.RemoveAdapter;

    private static bool PlanHasValueWritingChanges(ConfigurationChangePlan plan) =>
        plan.Changes.Any(change => IsValueWritingOperation(change.Operation));

    private static FileRollbackSnapshot CaptureRollbackSnapshot(IFileSystem fileSystem, string path)
    {
        bool fileExists = fileSystem.FileExists(path);
        bool directoryExists = !fileExists && fileSystem.DirectoryExists(path);
        FileRollbackSnapshotEntryKind entryKind = FileRollbackSnapshotEntryKind.Missing;
        if (fileExists || directoryExists)
        {
            entryKind = fileSystem.IsSymbolicLink(path)
                ? FileRollbackSnapshotEntryKind.SymbolicLink
                : fileExists
                    ? FileRollbackSnapshotEntryKind.RegularFile
                    : FileRollbackSnapshotEntryKind.Directory;
        }

        byte[]? contentsBytes =
            entryKind == FileRollbackSnapshotEntryKind.RegularFile
                ? fileSystem.ReadAllBytes(path)
                : null;
        return new FileRollbackSnapshot(
            path,
            entryKind,
            contentsBytes is null ? null : DecodeUtf8TextWithoutLeadingBom(contentsBytes),
            contentsBytes,
            contentsBytes is null ? null : ComputeSha256(contentsBytes)
        );
    }

    private static void ValidateExistingManifest(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        FileRollbackSnapshot manifestSnapshot,
        ConfigurationPlanOperation operation
    )
    {
        if (manifestSnapshot.EntryKind == FileRollbackSnapshotEntryKind.Missing)
        {
            if (operation == ConfigurationPlanOperation.Remove)
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: remove requires an existing "
                        + "ownership manifest."
                );
            }

            if (!string.IsNullOrWhiteSpace(plan.Manifest.PreviousOwnedEntryHash))
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: expected manifest before-state "
                        + "hash was provided, but the manifest does not exist."
                );
            }

            ValidateManifestOwnsPriorOwnedApplyTargets(fileSystem, null, plan, operation);

            return;
        }

        ValidateFileSnapshotIsRegularFile(manifestSnapshot, "configuration ownership manifest");
        ConfigurationOwnershipManifest existingManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(manifestSnapshot.Contents!);
        ValidateOwnershipManifestPathDoesNotCollideWithPhysicalTargetEntries(
            fileSystem,
            manifestSnapshot.Path,
            existingManifest
        );
        ValidatePhysicalTargetManifestEntries(fileSystem, existingManifest);
        ValidateCiTemporaryFileManifestWholeFileOwnership(fileSystem, plan, existingManifest);
        if (
            !string.Equals(
                existingManifest.ManifestId,
                plan.Manifest.ManifestId,
                StringComparison.Ordinal
            )
            || !string.Equals(
                existingManifest.OwnerProductId,
                plan.OwnerProductId,
                StringComparison.Ordinal
            )
            || !string.Equals(
                existingManifest.EntrySelector,
                plan.Manifest.EntrySelector,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: existing manifest identity does not "
                    + "match the plan."
            );
        }

        ValidateExpectedHash(
            plan.Manifest.PreviousOwnedEntryHash,
            manifestSnapshot.ContentsSha256Hash!,
            "configuration ownership manifest"
        );

        if (operation == ConfigurationPlanOperation.Remove)
        {
            ValidateManifestOwnsRemovedTargets(fileSystem, existingManifest, plan);
        }
        else
        {
            ValidateManifestOwnsPriorOwnedApplyTargets(
                fileSystem,
                existingManifest,
                plan,
                operation
            );
        }
    }

    private static void ValidateOwnershipManifestPathDoesNotCollideWithPhysicalTargetEntries(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationOwnershipManifest manifest
    )
    {
        string manifestIdentity = CreatePhysicalPathIdentity(fileSystem, manifestPath);
        if (
            manifest
                .Entries.Where(entry =>
                    HasCollisionCheckedPhysicalTargetPath(entry.TargetKind)
                )
                .Select(entry => CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName))
                .Any(targetPath => PathsAreSameOrParentChild(targetPath, manifestIdentity))
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: physical target entries must not "
                    + "equal, contain, or be contained by the ownership manifest path."
            );
        }
    }

    private static void ValidatePhysicalTargetManifestEntries(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest manifest
    )
    {
        ConfigurationOwnershipManifestEntry[] physicalEntries = manifest
            .Entries.Where(entry => IsPhysicalFileSystemTarget(entry.TargetKind))
            .ToArray();
        ConfigurationOwnershipManifestEntry[] nonCiPhysicalEntries = manifest
            .Entries.Where(entry => IsNonCiPhysicalFileSystemTarget(entry.TargetKind))
            .ToArray();
        if (
            physicalEntries.Any(entry =>
                IsReservedInternalFileSystemArtifact(
                    CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName)
                )
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: physical target entries must not use "
                    + "reserved internal filesystem artifact paths."
            );
        }

        if (
            nonCiPhysicalEntries
                .Select(entry =>
                    (
                        TargetPath: CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName),
                        entry.TargetKind
                    )
                )
                .GroupBy(target => target.TargetPath, GetPathIdentityComparer())
                .Any(group => group.Select(target => target.TargetKind).Distinct().Skip(1).Any())
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: physical target entries must not share "
                    + "the same physical target path with entries of another target kind."
            );
        }
    }

    private static void ValidateManifestOwnsPriorOwnedApplyTargets(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest? existingManifest,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        if (operation != ConfigurationPlanOperation.Apply)
        {
            return;
        }

        foreach (
            ConfigurationChange change in plan.Changes.Where(change =>
                change.Operation
                    is ConfigurationChangeOperation.Update or ConfigurationChangeOperation.Refresh
            )
        )
        {
            if (
                existingManifest is not null
                && existingManifest.Entries.Any(entry =>
                    PlanChangeMatchesEntry(fileSystem, plan, change, entry)
                )
            )
            {
                continue;
            }

            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: update and refresh targets must be "
                    + "owned by the existing manifest."
            );
        }
    }

    private static void ValidateManifestOwnsRemovedTargets(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest existingManifest,
        ConfigurationChangePlan plan
    )
    {
        foreach (
            ConfigurationChange change in plan.Changes.Where(change =>
                IsOwnershipRemoveOperation(change.Operation)
            )
        )
        {
            if (
                !existingManifest.Entries.Any(entry =>
                    PlanChangeMatchesEntry(fileSystem, plan, change, entry)
                )
            )
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: remove target is not owned by "
                        + "the existing manifest."
                );
            }
        }
    }

    private static void ValidateBeforeState(
        IFileSystem fileSystem,
        IReadOnlyDictionary<string, FileRollbackSnapshot> targetSnapshots,
        ConfigurationChangePlan plan
    )
    {
        foreach (ConfigurationChange change in plan.Changes)
        {
            ValidateChangeBeforeState(
                targetSnapshots[
                    CreateCiTemporaryFileWholeFileIdentity(
                        fileSystem,
                        plan,
                        change.TargetPathOrName
                    )
                ],
                change
            );
        }
    }

    private static FileRollbackSnapshot ValidateCurrentTargetBeforeMutation(
        IFileSystem fileSystem,
        ConfigurationChange change
    )
    {
        FileRollbackSnapshot snapshot = CaptureRollbackSnapshot(
            fileSystem,
            change.TargetPathOrName
        );
        ValidateChangeBeforeState(snapshot, change);
        return snapshot;
    }

    private static void ValidateChangeBeforeState(
        FileRollbackSnapshot snapshot,
        ConfigurationChange change
    )
    {
        ValidateFileSnapshotIsRegularFile(snapshot, "configuration target");
        switch (change.Operation)
        {
            case ConfigurationChangeOperation.Set:
                if (
                    snapshot.Existed
                    && string.IsNullOrWhiteSpace(change.PreviousOwnedEntryMetadata)
                )
                {
                    throw new InvalidOperationException(
                        "Configuration conflict: target already exists and no before-state "
                            + "metadata was provided."
                    );
                }

                if (snapshot.Existed)
                {
                    ValidateExpectedHash(
                        change.PreviousOwnedEntryMetadata,
                        snapshot.ContentsSha256Hash!,
                        "configuration target"
                    );
                }

                break;

            case ConfigurationChangeOperation.Create:
                if (snapshot.Existed)
                {
                    throw new InvalidOperationException(
                        "Configuration conflict: create target already exists."
                    );
                }

                break;

            case ConfigurationChangeOperation.Update:
            case ConfigurationChangeOperation.Refresh:
            case ConfigurationChangeOperation.Remove:
                if (!snapshot.Existed)
                {
                    throw new InvalidOperationException(
                        "Configuration conflict: target does not exist."
                    );
                }

                ValidateExpectedHash(
                    change.PreviousOwnedEntryMetadata,
                    snapshot.ContentsSha256Hash!,
                    "configuration target"
                );
                break;

            default:
                throw new NotSupportedException(
                    "Configuration apply/remove currently supports only generic file "
                        + "create, update, refresh, set, and remove operations."
                );
        }
    }

    private static void ValidateFileSnapshotIsRegularFile(
        FileRollbackSnapshot snapshot,
        string entryKind
    )
    {
        switch (snapshot.EntryKind)
        {
            case FileRollbackSnapshotEntryKind.Missing:
            case FileRollbackSnapshotEntryKind.RegularFile:
                return;

            case FileRollbackSnapshotEntryKind.Directory:
                throw new InvalidOperationException(
                    $"Configuration conflict: {entryKind} path exists as a directory."
                );

            case FileRollbackSnapshotEntryKind.SymbolicLink:
                throw new NotSupportedException(
                    $"Configuration conflict: {entryKind} path is a symbolic-link and is not "
                        + "supported."
                );

            default:
                throw new NotSupportedException(
                    $"Configuration conflict: {entryKind} path exists as an unsupported "
                        + "filesystem entry."
                );
        }
    }

    private static FileRollbackSnapshot ValidateCurrentManifestBeforeMutation(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        EnsureManifestParentChainIsUsable(fileSystem, manifestPath);
        EnsurePathIsNotUnsupportedReparsePoint(
            fileSystem,
            manifestPath,
            "configuration ownership manifest"
        );
        FileRollbackSnapshot snapshot = CaptureRollbackSnapshot(fileSystem, manifestPath);
        ValidateExistingManifest(
            fileSystem,
            plan,
            snapshot,
            operation
        );
        return snapshot;
    }

    private static void ValidateExpectedHash(
        string? expectedMetadata,
        string actualHash,
        string entryKind
    )
    {
        string? expectedHash = NormalizeExpectedHash(expectedMetadata);
        if (expectedHash is null)
        {
            throw new InvalidOperationException(
                $"Configuration conflict: {entryKind} before-state hash is required."
            );
        }

        if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Configuration conflict: {entryKind} before-state hash does not match."
            );
        }
    }

    private static string? NormalizeExpectedHash(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        string value = metadata.Trim();
        if (value.StartsWith(Sha256MetadataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[Sha256MetadataPrefix.Length..];
        }

        return value.Length == 64 && value.All(IsLowercaseHex) ? value : null;
    }

    private static bool IsLowercaseHex(char value) =>
        (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f');

    private static void ExecuteChangeWithRollbackRegistration(
        IFileSystem fileSystem,
        ConfigurationChange change,
        FileRollbackSnapshot snapshot,
        Stack<FileRollbackSnapshot> completedWrites
    )
    {
        string? expectedCurrentHashForRollback = GetExpectedCurrentHashAfterMutation(change);
        switch (change.Operation)
        {
            case ConfigurationChangeOperation.Set:
            case ConfigurationChangeOperation.Create:
            case ConfigurationChangeOperation.Update:
            case ConfigurationChangeOperation.Refresh:
                ExecuteAtomicWriteWithRollbackRegistration(
                    fileSystem,
                    change.TargetPathOrName,
                    change.Value!,
                    AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
                    snapshot,
                    expectedCurrentHashForRollback,
                    completedWrites
                );
                break;

            case ConfigurationChangeOperation.Remove:
                ExecuteDeleteWithRollbackRegistration(
                    fileSystem,
                    change.TargetPathOrName,
                    snapshot,
                    expectedCurrentHashForRollback,
                    completedWrites
                );
                break;

            default:
                throw new NotSupportedException(
                    "Configuration apply/remove currently supports only generic file "
                        + "create, update, refresh, set, and remove operations."
                );
        }
    }

    private static void ExecuteAtomicWriteWithRollbackRegistration(
        IFileSystem fileSystem,
        string path,
        string contents,
        AtomicWriteOptions options,
        FileRollbackSnapshot snapshot,
        string? expectedCurrentHashForRollback,
        Stack<FileRollbackSnapshot> completedWrites
    )
    {
        try
        {
            fileSystem.AtomicWriteAllText(
                path,
                contents,
                options: options,
                expectation: CreateMutationExpectation(snapshot)
            );
            RegisterRollbackSnapshot(completedWrites, snapshot, expectedCurrentHashForRollback);
        }
        catch (FileMutationException exception)
            when (exception.MutationMayHaveReachedDurableState)
        {
            RegisterRollbackSnapshot(completedWrites, snapshot, expectedCurrentHashForRollback);
            throw;
        }
    }

    private static void ExecuteDeleteWithRollbackRegistration(
        IFileSystem fileSystem,
        string path,
        FileRollbackSnapshot snapshot,
        string? expectedCurrentHashForRollback,
        Stack<FileRollbackSnapshot> completedWrites
    )
    {
        try
        {
            fileSystem.DeleteFile(path, CreateMutationExpectation(snapshot));
            RegisterRollbackSnapshot(completedWrites, snapshot, expectedCurrentHashForRollback);
        }
        catch (FileMutationException exception)
            when (exception.MutationMayHaveReachedDurableState)
        {
            RegisterRollbackSnapshot(completedWrites, snapshot, expectedCurrentHashForRollback);
            throw;
        }
    }

    private static void RegisterRollbackSnapshot(
        Stack<FileRollbackSnapshot> completedWrites,
        FileRollbackSnapshot snapshot,
        string? expectedCurrentHashForRollback
    ) =>
        completedWrites.Push(
            snapshot with
            {
                ExpectedCurrentHashForRollback = expectedCurrentHashForRollback,
            }
        );

    private static void RollBack(IFileSystem fileSystem, Stack<FileRollbackSnapshot> snapshots)
    {
        Exception? rollbackException = null;
        while (snapshots.Count > 0)
        {
            FileRollbackSnapshot snapshot = snapshots.Pop();
            try
            {
                if (snapshot.Existed)
                {
                    fileSystem.AtomicWriteAllBytes(
                        snapshot.Path,
                        snapshot.ContentsBytes!,
                        options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
                        expectation: CreateRollbackCurrentExpectation(snapshot)
                    );
                }
                else
                {
                    if (!fileSystem.FileExists(snapshot.Path))
                    {
                        continue;
                    }

                    fileSystem.DeleteFile(
                        snapshot.Path,
                        CreateRollbackCurrentExpectation(snapshot)
                    );
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                rollbackException ??= exception;
            }
        }

        if (rollbackException is not null)
        {
            throw new InvalidOperationException(
                "Configuration rollback failed after an apply/remove error.",
                rollbackException
            );
        }
    }

    private static void RollBackWithoutMaskingConflict(
        IFileSystem fileSystem,
        Stack<FileRollbackSnapshot> snapshots,
        Exception originalException
    )
    {
        try
        {
            RollBack(fileSystem, snapshots);
        }
        catch (Exception rollbackException)
            when (rollbackException is not OperationCanceledException)
        {
            if (IsConfigurationConflict(originalException))
            {
                originalException.Data["ConfigurationRollbackFailure"] = rollbackException.Message;
                ExceptionDispatchInfo.Capture(originalException).Throw();
            }

            throw;
        }
    }

    private static void DeleteTemporaryContainerAfterFullRemove(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        string manifestPath,
        ContainerRollbackSnapshot containerSnapshot
    )
    {
        if (plan.TemporaryContainer?.DeleteContainerOnRemoval != true)
        {
            return;
        }

        if (!containerSnapshot.Existed)
        {
            DeleteTemporaryContainer(fileSystem, containerSnapshot);
            return;
        }

        if (
            ContainerSnapshotContainsOnlyPlanRemovedTargets(
                fileSystem,
                plan,
                manifestPath,
                containerSnapshot
            )
        )
        {
            DeleteExistingTemporaryContainerIfOnlyArtifactsRemain(fileSystem, containerSnapshot);
            return;
        }

        DeleteRollbackCreatedContainerContents(fileSystem, containerSnapshot);
    }

    private static void DeleteTemporaryContainerAfterRollback(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ContainerRollbackSnapshot containerSnapshot
    )
    {
        if (plan.TemporaryContainer?.DeleteContainerOnRollback != true)
        {
            return;
        }

        if (!containerSnapshot.Existed)
        {
            DeleteNewTemporaryContainerIfOnlyArtifactsRemain(fileSystem, containerSnapshot);
            return;
        }

        DeleteRollbackCreatedContainerContents(fileSystem, containerSnapshot);
    }

    private static void DeleteTemporaryContainer(
        IFileSystem fileSystem,
        ContainerRollbackSnapshot containerSnapshot
    )
    {
        if (
            !EnsureSafeTemporaryContainerForCleanup(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: true
            )
        )
        {
            return;
        }

        EnsureTemporaryContainerDescendantEntriesHaveNoUnsupportedLinkOrReparsePoint(
            fileSystem,
            containerSnapshot.Path,
            "Temporary container cleanup rejects symbolic-link or reparse-point descendants "
                + "before recursive container deletion."
        );
        EnsureTemporaryContainerDescendantDirectoriesHaveNoReservedInternalArtifacts(
            fileSystem,
            containerSnapshot.Path,
            "Temporary container cleanup rejects reserved internal filesystem artifact "
                + "directories before recursive container deletion."
        );
        fileSystem.DeleteDirectory(containerSnapshot.Path, recursive: true);
    }

    private static void DeleteNewTemporaryContainerIfOnlyArtifactsRemain(
        IFileSystem fileSystem,
        ContainerRollbackSnapshot containerSnapshot
    )
    {
        if (
            !EnsureSafeTemporaryContainerForCleanup(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: false
            )
        )
        {
            return;
        }

        if (
            !EnsureTemporaryContainerDescendantEntriesAreSafeForCleanup(
                fileSystem,
                containerSnapshot.Path
            )
        )
        {
            return;
        }

        string[] files = fileSystem
            .EnumerateFiles(containerSnapshot.Path, "*", SearchOption.AllDirectories)
            .Select(fileSystem.GetFullPath)
            .Where(file => IsPathUnderDirectory(containerSnapshot.Path, file))
            .ToArray();
        if (
            files.Any(file =>
                !IsKnownTemporaryContainerArtifact(file)
                && !IsSafeReservedInternalFileSystemArtifact(fileSystem, file)
            )
        )
        {
            return;
        }

        if (
            !EnsureSafeTemporaryContainerForCleanup(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: false
            )
        )
        {
            return;
        }

        if (
            !EnsureTemporaryContainerDescendantEntriesAreSafeForCleanup(
                fileSystem,
                containerSnapshot.Path
            )
        )
        {
            return;
        }

        if (
            !EnsureTemporaryContainerDescendantDirectoriesAreSafeForCleanup(
                fileSystem,
                containerSnapshot.Path
            )
        )
        {
            return;
        }

        fileSystem.DeleteDirectory(containerSnapshot.Path, recursive: true);
    }

    private static void DeleteExistingTemporaryContainerIfOnlyArtifactsRemain(
        IFileSystem fileSystem,
        ContainerRollbackSnapshot containerSnapshot
    )
    {
        if (
            !EnsureSafeTemporaryContainerForCleanup(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: false
            )
        )
        {
            return;
        }

        if (
            !EnsureTemporaryContainerDescendantEntriesAreSafeForCleanup(
                fileSystem,
                containerSnapshot.Path
            )
        )
        {
            return;
        }

        string[] files = fileSystem
            .EnumerateFiles(containerSnapshot.Path, "*", SearchOption.AllDirectories)
            .Select(fileSystem.GetFullPath)
            .Where(file => IsPathUnderDirectory(containerSnapshot.Path, file))
            .ToArray();
        if (
            files.Any(file =>
                !IsKnownTemporaryContainerArtifact(file)
                && !IsIgnorableInternalFileSystemArtifact(fileSystem, file)
            )
        )
        {
            return;
        }

        if (
            !EnsureSafeTemporaryContainerForCleanup(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: false
            )
        )
        {
            return;
        }

        if (
            !EnsureTemporaryContainerDescendantEntriesAreSafeForCleanup(
                fileSystem,
                containerSnapshot.Path
            )
        )
        {
            return;
        }

        if (
            !EnsureTemporaryContainerDescendantDirectoriesAreSafeForCleanup(
                fileSystem,
                containerSnapshot.Path
            )
        )
        {
            return;
        }

        fileSystem.DeleteDirectory(containerSnapshot.Path, recursive: true);
    }

    private static void DeleteRollbackCreatedContainerContents(
        IFileSystem fileSystem,
        ContainerRollbackSnapshot containerSnapshot
    )
    {
        if (
            !EnsureSafeTemporaryContainerForCleanup(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: false
            )
        )
        {
            return;
        }

        if (
            !EnsureTemporaryContainerDescendantEntriesAreSafeForCleanup(
                fileSystem,
                containerSnapshot.Path
            )
        )
        {
            return;
        }

        var existingFiles = new HashSet<string>(
            containerSnapshot.ExistingFiles,
            GetPathIdentityComparer()
        );
        var existingDirectories = new HashSet<string>(
            containerSnapshot.ExistingDirectories,
            GetPathIdentityComparer()
        );

        if (
            !EnsureSafeTemporaryContainerForCleanup(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: false
            )
        )
        {
            return;
        }

        foreach (
            string file in fileSystem
                .EnumerateFiles(containerSnapshot.Path, "*", SearchOption.AllDirectories)
                .Select(fileSystem.GetFullPath)
                .Where(file =>
                    IsPathUnderDirectory(containerSnapshot.Path, file)
                    && !existingFiles.Contains(file)
                    && (
                        IsSafeKnownTemporaryContainerArtifact(fileSystem, file)
                        || IsSafeReservedInternalFileSystemArtifact(fileSystem, file)
                    )
                )
                .ToArray()
        )
        {
            if (
                !EnsureSafeTemporaryContainerForCleanup(
                    fileSystem,
                    containerSnapshot,
                    throwIfUnsafe: false
                )
            )
            {
                return;
            }

            fileSystem.DeleteFile(file);
        }

        if (
            !EnsureSafeTemporaryContainerForCleanup(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: false
            )
        )
        {
            return;
        }

        foreach (
            string directory in fileSystem
                .EnumerateDirectories(containerSnapshot.Path, "*", SearchOption.AllDirectories)
                .Select(fileSystem.GetFullPath)
                .Where(directory =>
                    IsPathUnderDirectory(containerSnapshot.Path, directory)
                    && !existingDirectories.Contains(directory)
                )
                .OrderByDescending(directory => directory.Length)
                .ToArray()
        )
        {
            if (
                !EnsureSafeTemporaryContainerForCleanup(
                    fileSystem,
                    containerSnapshot,
                    throwIfUnsafe: false
                )
            )
            {
                return;
            }

            TryDeleteEmptyDirectory(fileSystem, directory);
        }
    }

    private static bool EnsureSafeTemporaryContainerForCleanup(
        IFileSystem fileSystem,
        ContainerRollbackSnapshot containerSnapshot,
        bool throwIfUnsafe
    )
    {
        try
        {
            string currentPath = fileSystem.GetFullPath(containerSnapshot.Path);
            if (
                !string.Equals(
                    Path.TrimEndingDirectorySeparator(containerSnapshot.Path),
                    Path.TrimEndingDirectorySeparator(currentPath),
                    GetPathIdentityComparison()
                )
            )
            {
                throw new NotSupportedException(
                    "Temporary container cleanup rejects container paths that no longer resolve "
                        + "to the expected product-owned root."
                );
            }
            if (!fileSystem.DirectoryExists(containerSnapshot.Path))
            {
                if (throwIfUnsafe)
                {
                    throw new NotSupportedException(
                        "Temporary container cleanup rejects container paths that no longer exist."
                    );
                }

                return false;
            }

            EnsureDirectoryChainHasNoUnsupportedLinkOrReparsePoint(
                fileSystem,
                currentPath,
                "Temporary container cleanup rejects symbolic-link or reparse-point directories "
                    + "in container paths."
            );

            return true;
        }
        catch (Exception exception)
            when (!throwIfUnsafe
                && exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
        {
            return false;
        }
    }

    private static void EnsureDirectoryChainHasNoUnsupportedLinkOrReparsePoint(
        IFileSystem fileSystem,
        string path,
        string message
    )
    {
        foreach (string directory in EnumerateDirectoryChain(path))
        {
            if (fileSystem.IsSymbolicLink(directory))
            {
                throw new NotSupportedException(message);
            }

            if (
                fileSystem is IFileSystemReparsePointSafety reparsePointSafety
                && reparsePointSafety.IsReparsePoint(directory)
            )
            {
                throw new NotSupportedException(message);
            }
        }
    }

    private static bool EnsureTemporaryContainerDescendantDirectoriesAreSafeForCleanup(
        IFileSystem fileSystem,
        string containerPath
    )
    {
        try
        {
            EnsureTemporaryContainerDescendantDirectoriesHaveNoUnsupportedLinkOrReparsePoint(
                fileSystem,
                containerPath,
                "Temporary container cleanup rejects symbolic-link or reparse-point descendant "
                    + "directories before container cleanup deletion."
            );
            EnsureTemporaryContainerDescendantDirectoriesHaveNoReservedInternalArtifacts(
                fileSystem,
                containerPath,
                "Temporary container cleanup rejects reserved internal filesystem artifact "
                    + "directories before container cleanup deletion."
            );
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool EnsureTemporaryContainerDescendantEntriesAreSafeForCleanup(
        IFileSystem fileSystem,
        string containerPath
    )
    {
        try
        {
            EnsureTemporaryContainerDescendantEntriesHaveNoUnsupportedLinkOrReparsePoint(
                fileSystem,
                containerPath,
                "Temporary container cleanup rejects symbolic-link or reparse-point descendants "
                    + "before container cleanup deletion."
            );
            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static void
        EnsureTemporaryContainerDescendantEntriesHaveNoUnsupportedLinkOrReparsePoint(
        IFileSystem fileSystem,
        string containerPath,
        string message
    )
    {
        if (fileSystem is not IFileSystemNoFollowEnumeration noFollowEnumeration)
        {
            throw new NotSupportedException(
                "Temporary container cleanup requires no-follow filesystem enumeration."
            );
        }

        foreach (
            string entry in noFollowEnumeration
                .EnumerateFileSystemEntriesNoFollow(
                    containerPath,
                    "*",
                    SearchOption.AllDirectories
                )
                .Select(fileSystem.GetFullPath)
                .Where(entry => IsPathUnderDirectory(containerPath, entry))
        )
        {
            if (IsUnsupportedLinkOrReparsePoint(fileSystem, entry))
            {
                throw new NotSupportedException(message);
            }
        }
    }

    private static void
        EnsureTemporaryContainerDescendantDirectoriesHaveNoUnsupportedLinkOrReparsePoint(
        IFileSystem fileSystem,
        string containerPath,
        string message
    )
    {
        // This preflight prevents deleting a tree that already contains unsafe directory links.
        // Recursive deletion still relies on IFileSystem implementations treating any directory
        // links introduced after this check as leaf entries rather than following them.
        foreach (
            string directory in fileSystem
                .EnumerateDirectories(containerPath, "*", SearchOption.AllDirectories)
                .Select(fileSystem.GetFullPath)
                .Where(directory => IsPathUnderDirectory(containerPath, directory))
        )
        {
            if (IsUnsupportedLinkOrReparsePoint(fileSystem, directory))
            {
                throw new NotSupportedException(message);
            }
        }
    }

    private static void
        EnsureTemporaryContainerDescendantDirectoriesHaveNoReservedInternalArtifacts(
        IFileSystem fileSystem,
        string containerPath,
        string message
    )
    {
        foreach (
            string directory in fileSystem
                .EnumerateDirectories(containerPath, "*", SearchOption.AllDirectories)
                .Select(fileSystem.GetFullPath)
                .Where(directory => IsPathUnderDirectory(containerPath, directory))
        )
        {
            if (IsReservedInternalFileSystemArtifact(directory))
            {
                throw new NotSupportedException(message);
            }
        }
    }

    private static bool IsKnownTemporaryContainerArtifact(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Length > 0
            && fileName[0] == '.'
            && fileName.EndsWith(".tmp", StringComparison.Ordinal);
    }

    private static bool IsReservedInternalFileSystemArtifact(string path) =>
        IsReservedInternalFileSystemArtifact(path, GetPathIdentityComparison());

    private static void EnsureOwnershipManifestPathIsNotReservedInternalFileSystemArtifact(
        string ownershipManifestPath
    )
    {
        if (!IsReservedInternalFileSystemArtifact(ownershipManifestPath))
        {
            return;
        }

        throw new ArgumentException(
            "Configuration ownership manifest path must not use reserved internal filesystem "
                + "artifact paths.",
            nameof(ownershipManifestPath)
        );
    }

    private static bool IsReservedInternalConfigurationPathArtifact(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        string normalizedPath =
            kind == ConfigurationPathKind.Invalid
                ? NormalizePhysicalTargetConfigurationPathSegments(path)
                : NormalizeAbsoluteConfigurationPathSegments(path);
        return ContainsReservedInternalPathSegment(
            normalizedPath,
            GetConfigurationPathComparison(kind)
        );
    }

    internal static bool IsReservedInternalFileSystemArtifact(
        string path,
        StringComparison pathIdentityComparison
    ) => ContainsReservedInternalPathSegment(path, pathIdentityComparison);

    private static bool ContainsReservedInternalPathSegment(
        string path,
        StringComparison pathIdentityComparison
    ) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment =>
                string.Equals(segment, FileSystemLockFileName, pathIdentityComparison)
                || string.Equals(segment, LifecycleLockDirectoryName, pathIdentityComparison)
            );

    private static bool IsIgnorableInternalFileSystemArtifact(IFileSystem fileSystem, string path)
    {
        if (!IsReservedInternalFileSystemArtifact(path))
        {
            return false;
        }

        try
        {
            if (fileSystem.IsSymbolicLink(path))
            {
                return false;
            }

            if (
                fileSystem is IFileSystemReparsePointSafety reparsePointSafety
                && reparsePointSafety.IsReparsePoint(path)
            )
            {
                return false;
            }

            return fileSystem is IFileSystemFileLength fileLength
                && fileLength.GetFileLength(path) == 0;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSafeReservedInternalFileSystemArtifact(
        IFileSystem fileSystem,
        string path
    )
    {
        return IsIgnorableInternalFileSystemArtifact(fileSystem, path);
    }

    private static bool IsSafeKnownTemporaryContainerArtifact(IFileSystem fileSystem, string path)
    {
        return IsKnownTemporaryContainerArtifact(path)
            && IsSafeFileSystemArtifact(fileSystem, path);
    }

    private static bool IsSafeFileSystemArtifact(IFileSystem fileSystem, string path)
    {
        try
        {
            if (fileSystem.IsSymbolicLink(path))
            {
                return false;
            }

            if (
                fileSystem is IFileSystemReparsePointSafety reparsePointSafety
                && reparsePointSafety.IsReparsePoint(path)
            )
            {
                return false;
            }

            return true;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static void TryDeleteEmptyDirectory(IFileSystem fileSystem, string directory)
    {
        try
        {
            if (IsUnsupportedLinkOrReparsePoint(fileSystem, directory))
            {
                return;
            }

            EnsureTemporaryContainerDescendantEntriesHaveNoUnsupportedLinkOrReparsePoint(
                fileSystem,
                directory,
                "Temporary container cleanup rejects symbolic-link or reparse-point descendants "
                    + "before empty directory deletion."
            );
            EnsureTemporaryContainerDescendantDirectoriesHaveNoUnsupportedLinkOrReparsePoint(
                fileSystem,
                directory,
                "Temporary container cleanup rejects symbolic-link or reparse-point descendant "
                    + "directories before empty directory deletion."
            );
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (
            fileSystem.EnumerateFiles(directory).Any()
            || fileSystem.EnumerateDirectories(directory).Any()
        )
        {
            return;
        }

        fileSystem.DeleteDirectory(directory);
    }

    private static bool IsConfigurationConflict(Exception exception) =>
        exception is InvalidOperationException
        && exception.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase);

    private static bool ContainerSnapshotContainsOnlyPlanRemovedTargets(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        string manifestPath,
        ContainerRollbackSnapshot containerSnapshot
    )
    {
        string[] removedTargets = plan
            .Changes.Where(change => change.Operation == ConfigurationChangeOperation.Remove)
            .Select(change => fileSystem.GetFullPath(change.TargetPathOrName))
            .ToArray();
        var removedTargetFiles = new HashSet<string>(removedTargets, GetPathIdentityComparer());
        string manifestFullPath = fileSystem.GetFullPath(manifestPath);

        if (
            containerSnapshot.ExistingFiles.Any(file =>
                !removedTargetFiles.Contains(file)
                && !IsOwnedManifestInsideContainer(containerSnapshot.Path, manifestFullPath, file)
                && !IsIgnorableInternalFileSystemArtifact(fileSystem, file)
            )
        )
        {
            return false;
        }

        return containerSnapshot.ExistingDirectories.All(directory =>
            IsExpectedRemovedTargetContainerDirectory(
                containerSnapshot.Path,
                directory,
                removedTargets,
                manifestFullPath
            )
        );
    }

    private static bool IsOwnedManifestInsideContainer(
        string containerPath,
        string manifestPath,
        string candidatePath
    ) =>
        string.Equals(candidatePath, manifestPath, GetPathIdentityComparison())
        && IsPathUnderDirectory(containerPath, manifestPath);

    private static bool IsExpectedRemovedTargetContainerDirectory(
        string containerPath,
        string directoryPath,
        IReadOnlyList<string> removedTargets,
        string manifestPath
    )
    {
        string normalizedContainer = Path.TrimEndingDirectorySeparator(containerPath);
        string normalizedDirectory = Path.TrimEndingDirectorySeparator(directoryPath);
        if (
            string.Equals(
                normalizedDirectory,
                normalizedContainer,
                GetPathIdentityComparison()
            )
        )
        {
            return true;
        }

        return IsPathUnderDirectory(normalizedContainer, normalizedDirectory)
            && (
                removedTargets.Any(target => IsPathUnderDirectory(normalizedDirectory, target))
                || IsPathUnderDirectory(normalizedDirectory, manifestPath)
            );
    }

    private static FileMutationExpectation CreateMutationExpectation(
        FileRollbackSnapshot snapshot
    ) =>
        snapshot.Existed
            ? FileMutationExpectation.Existing(snapshot.ContentsSha256Hash!)
            : FileMutationExpectation.Missing;

    private static FileMutationExpectation CreateRollbackCurrentExpectation(
        FileRollbackSnapshot snapshot
    ) =>
        snapshot.ExpectedCurrentHashForRollback is { } expectedHash
            ? FileMutationExpectation.Existing(expectedHash)
            : FileMutationExpectation.Missing;

    private static string? GetExpectedCurrentHashAfterMutation(ConfigurationChange change) =>
        IsValueWritingOperation(change.Operation)
            ? ComputeSha256(Encoding.UTF8.GetBytes(change.Value!))
            : null;

    private static bool IsPathUnderDirectory(string directoryPath, string candidatePath)
    {
        string normalizedDirectory = Path.TrimEndingDirectorySeparator(directoryPath);
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(candidatePath);
        return normalizedCandidate.Length > normalizedDirectory.Length
            && normalizedCandidate.StartsWith(normalizedDirectory, GetPathIdentityComparison())
            && IsDirectorySeparator(normalizedCandidate[normalizedDirectory.Length]);
    }

    private static bool PathsAreSameOrParentChild(string firstPath, string secondPath)
    {
        string normalizedFirst = Path.TrimEndingDirectorySeparator(firstPath);
        string normalizedSecond = Path.TrimEndingDirectorySeparator(secondPath);
        return string.Equals(normalizedFirst, normalizedSecond, GetPathIdentityComparison())
            || IsPathUnderDirectory(normalizedFirst, normalizedSecond)
            || IsPathUnderDirectory(normalizedSecond, normalizedFirst);
    }

    private static bool IsConfigurationPathUnderDirectory(
        string directoryPath,
        string candidatePath
    )
    {
        ConfigurationPathKind directoryKind = GetConfigurationPathKind(directoryPath);
        if (directoryKind != GetConfigurationPathKind(candidatePath))
        {
            return false;
        }

        string normalizedDirectory = NormalizeConfigurationPath(directoryPath);
        string normalizedCandidate = NormalizeConfigurationPath(candidatePath);
        return normalizedCandidate.Length > normalizedDirectory.Length
            && normalizedCandidate.StartsWith(
                normalizedDirectory,
                GetConfigurationPathComparison(directoryKind)
            )
            && normalizedCandidate[normalizedDirectory.Length] == '/';
    }

    private static string CreateCiTemporaryFileWholeFileIdentity(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        string targetPathOrName
    )
    {
        string productOwnedRoot = fileSystem.GetFullPath(
            plan.TemporaryContainer!.ProductOwnedPath
        );
        string targetPath = fileSystem.GetFullPath(targetPathOrName);
        if (!IsPathUnderDirectory(productOwnedRoot, targetPath))
        {
            throw new NotSupportedException(
                "Filesystem-backed configuration execution supports only targets under the "
                    + "declared product-owned temporary container."
            );
        }

        return Path.TrimEndingDirectorySeparator(targetPath);
    }

    private static string CreateCiTemporaryFileManifestEntryIdentity(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        string targetPathOrName
    ) =>
        plan.TemporaryContainer is null
            ? CreatePhysicalPathIdentity(fileSystem, targetPathOrName)
            : CreateCiTemporaryFileWholeFileIdentity(fileSystem, plan, targetPathOrName);

    private static string CreateCiTemporaryFileWholeFileIdentity(string targetPathOrName) =>
        NormalizeAbsoluteConfigurationPathSegments(
            Path.TrimEndingDirectorySeparator(targetPathOrName)
        );

    private static string CreatePlanningPhysicalPathIdentity(ConfigurationChange change) =>
        change.TargetKind == ConfigurationTargetKind.CiTemporaryFile
            ? CreateCiTemporaryFileWholeFileIdentity(change.TargetPathOrName)
            : NormalizePhysicalTargetConfigurationPathSegments(
                Path.TrimEndingDirectorySeparator(change.TargetPathOrName)
            );

    private static string CreatePhysicalPathIdentity(
        IFileSystem fileSystem,
        string targetPathOrName
    )
    {
        string targetPath = fileSystem.GetFullPath(targetPathOrName);
        return Path.TrimEndingDirectorySeparator(targetPath);
    }

    private static StringComparer GetPathIdentityComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathIdentityComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private sealed class ConfigurationPathIdentityComparer : IEqualityComparer<string>
    {
        public static readonly ConfigurationPathIdentityComparer Instance = new();

        private ConfigurationPathIdentityComparer() { }

        public bool Equals(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            ConfigurationPathKind xKind = GetConfigurationPathKind(x);
            return xKind == GetConfigurationPathKind(y)
                && string.Equals(
                    NormalizeConfigurationPath(x),
                    NormalizeConfigurationPath(y),
                    GetConfigurationPathComparison(xKind)
                );
        }

        public int GetHashCode(string obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            ConfigurationPathKind kind = GetConfigurationPathKind(obj);
            var hashCode = new HashCode();
            hashCode.Add(kind);
            hashCode.Add(
                NormalizeConfigurationPath(obj),
                IsWindowsConfigurationPathKind(kind)
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            );
            return hashCode.ToHashCode();
        }
    }

    private static StringComparison GetConfigurationPathComparison(ConfigurationPathKind kind) =>
        IsWindowsConfigurationPathKind(kind)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool IsWindowsConfigurationPathKind(ConfigurationPathKind kind) =>
        kind is ConfigurationPathKind.WindowsDrive or ConfigurationPathKind.WindowsUnc;

    private static string NormalizeConfigurationPath(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        string normalized = IsWindowsConfigurationPathKind(kind)
            ? path.Replace('\\', '/')
            : path;
        int rootLength = kind == ConfigurationPathKind.WindowsUnc ? 2 : 0;
        while (normalized.IndexOf("//", rootLength, StringComparison.Ordinal) >= 0)
        {
            normalized =
                normalized[..rootLength]
                + normalized[rootLength..].Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.TrimEnd('/');
    }

    private static string NormalizeAbsoluteConfigurationPathSegments(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        if (kind == ConfigurationPathKind.Invalid)
        {
            return path;
        }

        string normalized = IsWindowsConfigurationPathKind(kind)
            ? path.Replace('\\', '/')
            : path;
        int duplicateSlashRootLength = kind == ConfigurationPathKind.WindowsUnc ? 2 : 0;
        while (normalized.IndexOf("//", duplicateSlashRootLength, StringComparison.Ordinal) >= 0)
        {
            normalized =
                normalized[..duplicateSlashRootLength]
                + normalized[duplicateSlashRootLength..].Replace(
                    "//",
                    "/",
                    StringComparison.Ordinal
                );
        }

        int rootLength = GetAbsoluteConfigurationPathRootLength(normalized, kind);
        while (normalized.Length > rootLength && normalized.EndsWith('/'))
        {
            normalized = normalized[..^1];
        }

        string root = normalized[..rootLength];
        string remainder = normalized[rootLength..];
        var segments = new List<string>();
        foreach (string segment in remainder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return root;
        }

        string joinedSegments = string.Join('/', segments);
        return root.EndsWith('/') ? root + joinedSegments : root + "/" + joinedSegments;
    }

    private static string NormalizePhysicalTargetConfigurationPathSegments(string path)
    {
        ConfigurationPathKind kind = GetConfigurationPathKind(path);
        return kind == ConfigurationPathKind.Invalid
            ? NormalizeRelativeConfigurationPathSegments(path)
            : NormalizeAbsoluteConfigurationPathSegments(path);
    }

    private static string NormalizeRelativeConfigurationPathSegments(string path)
    {
        if (IsRootedInvalidConfigurationPath(path))
        {
            return NormalizeConfigurationPath(path);
        }

        string normalized = path.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        var segments = new List<string>();
        foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count > 0 && segments[^1] != "..")
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    segments.Add(segment);
                }

                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static bool IsRootedInvalidConfigurationPath(string path) =>
        path.Length > 0 && (path[0] == '/' || path[0] == '\\');

    private static int GetAbsoluteConfigurationPathRootLength(
        string normalizedPath,
        ConfigurationPathKind kind
    )
    {
        if (kind == ConfigurationPathKind.PosixAbsolute)
        {
            return 1;
        }

        if (kind == ConfigurationPathKind.WindowsDrive)
        {
            return Math.Min(3, normalizedPath.Length);
        }

        int serverEnd = normalizedPath.IndexOf('/', 2);
        if (serverEnd < 0)
        {
            return normalizedPath.Length;
        }

        int shareEnd = normalizedPath.IndexOf('/', serverEnd + 1);
        return shareEnd < 0 ? normalizedPath.Length : shareEnd;
    }

    private static ConfigurationPathKind GetConfigurationPathKind(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return ConfigurationPathKind.Invalid;
        }

        if (
            path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
        )
        {
            return ConfigurationPathKind.WindowsUnc;
        }

        if (
            path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && (path[2] == '\\' || path[2] == '/')
        )
        {
            return ConfigurationPathKind.WindowsDrive;
        }

        if (path[0] == '/')
        {
            return path.Contains('\\', StringComparison.Ordinal)
                ? ConfigurationPathKind.Invalid
                : ConfigurationPathKind.PosixAbsolute;
        }

        return ConfigurationPathKind.Invalid;
    }

    private enum ConfigurationPathKind
    {
        Invalid,
        WindowsDrive,
        WindowsUnc,
        PosixAbsolute,
    }

    private static string CreateLifecycleLockName(
        string manifestFullPath,
        string productOwnedRoot
    ) =>
        CreateLifecycleLockName(manifestFullPath, productOwnedRoot, GetPathIdentityComparison());

    internal static string CreateLifecycleLockName(
        string manifestFullPath,
        string productOwnedRoot,
        StringComparison pathIdentityComparison
    )
    {
        string normalizedManifestPath = NormalizeLifecycleLockKeyPath(
            manifestFullPath,
            pathIdentityComparison
        );
        string normalizedProductOwnedRoot = NormalizeLifecycleLockKeyPath(
            productOwnedRoot,
            pathIdentityComparison
        );
        return ComputeSha256(normalizedManifestPath + "\n" + normalizedProductOwnedRoot);
    }

    private static string NormalizeLifecycleLockKeyPath(
        string path,
        StringComparison pathIdentityComparison
    )
    {
        string normalizedPath = TrimEndingLifecyclePathDirectorySeparators(
            path,
            GetLifecyclePathDirectorySeparator(path)
        );
        return pathIdentityComparison == StringComparison.OrdinalIgnoreCase
            ? normalizedPath.ToUpperInvariant()
            : normalizedPath;
    }

    private static string CombineLifecyclePath(
        string directory,
        string childName,
        char directorySeparator
    )
    {
        string normalizedDirectory = TrimEndingLifecyclePathDirectorySeparators(
            directory,
            directorySeparator
        );
        return IsLifecyclePathRoot(normalizedDirectory, directorySeparator)
            ? normalizedDirectory + childName
            : normalizedDirectory + directorySeparator + childName;
    }

    private static string? GetLifecyclePathDirectoryName(string path, char directorySeparator)
    {
        string normalizedPath = TrimEndingLifecyclePathDirectorySeparators(
            path,
            directorySeparator
        );
        int rootLength = GetLifecyclePathRootLength(normalizedPath, directorySeparator);
        if (normalizedPath.Length == rootLength)
        {
            return null;
        }

        int separatorIndex = normalizedPath.LastIndexOf(directorySeparator);
        if (separatorIndex < rootLength)
        {
            return rootLength == 0 ? null : normalizedPath[..rootLength];
        }

        return separatorIndex + 1 == rootLength
            ? normalizedPath[..rootLength]
            : normalizedPath[..separatorIndex];
    }

    private static string TrimEndingLifecyclePathDirectorySeparators(
        string path,
        char directorySeparator
    )
    {
        int rootLength = GetLifecyclePathRootLength(path, directorySeparator);
        int length = path.Length;
        while (length > rootLength && path[length - 1] == directorySeparator)
        {
            length--;
        }

        return length == path.Length ? path : path[..length];
    }

    private static bool IsLifecyclePathRoot(string path, char directorySeparator) =>
        path.Length == GetLifecyclePathRootLength(path, directorySeparator);

    private static int GetLifecyclePathRootLength(string path, char directorySeparator)
    {
        if (directorySeparator == '/')
        {
            return path.Length > 0 && path[0] == '/' ? 1 : 0;
        }

        if (
            path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && path[2] == directorySeparator
        )
        {
            return 3;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            int serverSeparatorIndex = path.IndexOf(directorySeparator, 2);
            if (serverSeparatorIndex < 0)
            {
                return path.Length;
            }

            int shareSeparatorIndex = path.IndexOf(
                directorySeparator,
                serverSeparatorIndex + 1
            );
            return shareSeparatorIndex < 0 ? path.Length : shareSeparatorIndex + 1;
        }

        return path.Length > 0 && path[0] == directorySeparator ? 1 : 0;
    }

    private static char GetLifecyclePathDirectorySeparator(params string[] paths)
    {
        foreach (string path in paths)
        {
            int slashIndex = path.IndexOf('/');
            int backslashIndex = path.IndexOf('\\');
            if (slashIndex >= 0 && (backslashIndex < 0 || slashIndex < backslashIndex))
            {
                return '/';
            }

            if (backslashIndex >= 0)
            {
                return '\\';
            }
        }

        return Path.DirectorySeparatorChar;
    }

    private static bool IsDirectorySeparator(char value) =>
        value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;

    private static bool IsLifecyclePathSameOrUnderDirectory(
        string directoryPath,
        string candidatePath,
        char directorySeparator
    )
    {
        string normalizedDirectory = TrimEndingLifecyclePathDirectorySeparators(
            directoryPath,
            directorySeparator
        );
        string normalizedCandidate = TrimEndingLifecyclePathDirectorySeparators(
            candidatePath,
            directorySeparator
        );
        StringComparison comparison = GetPathIdentityComparison();
        if (string.Equals(normalizedDirectory, normalizedCandidate, comparison))
        {
            return true;
        }

        if (
            normalizedCandidate.Length <= normalizedDirectory.Length
            || !normalizedCandidate.StartsWith(normalizedDirectory, comparison)
        )
        {
            return false;
        }

        return IsLifecyclePathRoot(normalizedDirectory, directorySeparator)
            || normalizedCandidate[normalizedDirectory.Length] == directorySeparator;
    }

    private static bool IsGenericFileTarget(ConfigurationTargetKind targetKind) =>
        targetKind == ConfigurationTargetKind.CiTemporaryFile;

    private static bool IsProjectionOnlyPhysicalTargetPlan(ConfigurationChangePlan plan) =>
        plan.Changes.Count > 0
        && plan.Changes.All(change => IsProjectionOnlyPhysicalTarget(change.TargetKind));

    private static bool IsProjectionOnlyPhysicalTarget(ConfigurationTargetKind targetKind) =>
        targetKind
            is ConfigurationTargetKind.GitConfig
                or ConfigurationTargetKind.NuGetPluginLayout
                or ConfigurationTargetKind.PythonKeyringBackend
                or ConfigurationTargetKind.KeyringShim;

    private static bool IsPhysicalFileSystemTarget(ConfigurationTargetKind targetKind) =>
        targetKind == ConfigurationTargetKind.CiTemporaryFile
        || IsProjectionOnlyPhysicalTarget(targetKind)
        || targetKind is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc;

    private static bool IsNonCiPhysicalFileSystemTarget(ConfigurationTargetKind targetKind) =>
        targetKind != ConfigurationTargetKind.CiTemporaryFile
        && IsPhysicalFileSystemTarget(targetKind);

    private static bool HasCollisionCheckedPhysicalTargetPath(ConfigurationChange change) =>
        HasCollisionCheckedPhysicalTargetPath(change.TargetKind);

    private static bool HasCollisionCheckedPhysicalTargetPath(ConfigurationTargetKind targetKind) =>
        IsPhysicalFileSystemTarget(targetKind);

    private static bool IsValueWritingOperation(ConfigurationChangeOperation operation) =>
        operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;

    private static string ComputeSha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private static string ComputeSha256(byte[] value)
    {
        byte[] hash = SHA256.HashData(value);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    private static string DecodeUtf8TextWithoutLeadingBom(byte[] value)
    {
        ReadOnlySpan<byte> bytes = value;
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            bytes = bytes[3..];
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private sealed record FileRollbackSnapshot(
        string Path,
        FileRollbackSnapshotEntryKind EntryKind,
        string? Contents,
        byte[]? ContentsBytes,
        string? ContentsSha256Hash,
        string? ExpectedCurrentHashForRollback = null
    )
    {
        public bool Existed => EntryKind != FileRollbackSnapshotEntryKind.Missing;
    }

    private enum FileRollbackSnapshotEntryKind
    {
        Missing,
        RegularFile,
        Directory,
        SymbolicLink,
    }

    private sealed record ContainerRollbackSnapshot(
        string Path,
        bool Existed,
        IReadOnlyList<string> ExistingFiles,
        IReadOnlyList<string> ExistingDirectories
    );

    private sealed class NullDisposable : IDisposable
    {
        public static NullDisposable Instance { get; } = new();

        private NullDisposable() { }

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
    public required ConfigurationPlanState State { get; init; }
    public IReadOnlyList<ConfigurationPlannedChange> Changes { get; init; } =
        Array.Empty<ConfigurationPlannedChange>();
    public IReadOnlyList<ConfigurationPlannedOperation> PlannedOperations { get; init; } =
        Array.Empty<ConfigurationPlannedOperation>();
    public ConfigurationOwnershipManifest? OwnershipManifest { get; init; }
}

public sealed record ConfigurationDryRunPlan
{
    public required int ContractMajor { get; init; }
    public required string PlanId { get; init; }
    public required string ChangeSetId { get; init; }
    public required string OwnerProductId { get; init; }
    public required ConfigurationScope Scope { get; init; }
    public required ConfigurationAtomicityPolicy AtomicityPolicy { get; init; }
    public required ConfigurationRollbackPolicy RollbackPolicy { get; init; }
    public required ConfigurationPlanState State { get; init; }
    public required ConfigurationManifestCommitPolicy ManifestCommitPolicy { get; init; }
    public required ConfigurationManifestMetadata Manifest { get; init; }
    public ConfigurationTemporaryContainer? TemporaryContainer { get; init; }
    public required ConfigurationDeclarationPreservation DeclarationPreservation { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required bool ContainsCredentialMaterial { get; init; }
    public IReadOnlyDictionary<string, string> ExtensionData { get; init; } =
        ContractMetadata.Empty;
    public IReadOnlyList<ConfigurationPlannedChange> Changes { get; init; } =
        Array.Empty<ConfigurationPlannedChange>();
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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlannedValueSha256 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousOwnedEntryMetadata { get; init; }
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
    private static readonly Dictionary<string, ConfigurationPlanOperation> ValuesByWireName =
        Enum.GetValues<ConfigurationPlanOperation>()
            .ToDictionary(
                value =>
                    JsonNamingPolicy.CamelCase.ConvertName(
                        Enum.GetName(value)
                            ?? throw new JsonException(
                                "Unnamed enum value in ConfigurationPlanOperation."
                            )
                    ),
                value => value,
                StringComparer.Ordinal
            );

    private static readonly Dictionary<ConfigurationPlanOperation, string> WireNamesByValue =
        ValuesByWireName.ToDictionary(pair => pair.Value, pair => pair.Key);

    public override ConfigurationPlanOperation Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string enum value for ConfigurationPlanOperation.");
        }

        string? value = reader.GetString();
        if (
            value is null
            || !ValuesByWireName.TryGetValue(value, out ConfigurationPlanOperation enumValue)
        )
        {
            throw new JsonException(
                $"Unsupported enum value '{value}' for ConfigurationPlanOperation."
            );
        }

        return enumValue;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ConfigurationPlanOperation value,
        JsonSerializerOptions options
    )
    {
        if (!WireNamesByValue.TryGetValue(value, out string? wireName))
        {
            long numericValue = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            throw new JsonException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unsupported enum value '{numericValue}' for ConfigurationPlanOperation."
                )
            );
        }

        writer.WriteStringValue(wireName);
    }
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
    public required string PlanId { get; init; }
    public required string ChangeSetId { get; init; }
    public required string OwnerProductId { get; init; }

    public required ConfigurationScope Scope { get; init; }
    public required string EntrySelector { get; init; }
    public string? ProductVersion { get; init; }
    public string? PreviousOwnedEntryHash { get; init; }
    public bool ContainsCredentialMaterial { get; init; }
    public IReadOnlyDictionary<string, string> SafeMetadata { get; init; } =
        ContractMetadata.Empty;

    [JsonRequired]
    public IReadOnlyList<ConfigurationOwnershipManifestEntry> Entries { get; init; } =
        Array.Empty<ConfigurationOwnershipManifestEntry>();
}

public sealed record ConfigurationOwnershipManifestEntry
{
    public required int Sequence { get; init; }

    public required ConfigurationChangeOperation Operation { get; init; }

    public required ConfigurationTargetKind TargetKind { get; init; }
    public required string TargetPathOrName { get; init; }
    public required string Key { get; init; }
    public required bool PreserveDeclarationsAndComments { get; init; }
    public bool HasPlannedValue { get; init; }
    public bool IsSecretValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlannedValueSha256 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousOwnedEntryMetadata { get; init; }
}

public static class ConfigurationOwnershipManifestSerializer
{
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions =
        ConfigurationOwnershipManifestJson.CreateSerializerOptions();

    public static string Serialize(ConfigurationOwnershipManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        return System.Text.Json.JsonSerializer.Serialize(manifest, SerializerOptions);
    }

    public static ConfigurationOwnershipManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ConfigurationOwnershipManifest manifest =
            System.Text.Json.JsonSerializer.Deserialize<ConfigurationOwnershipManifest>(
                json,
                SerializerOptions
            )
            ?? throw new System.Text.Json.JsonException(
                "Configuration ownership manifest JSON did not contain a manifest."
            );
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        return manifest;
    }
}

public sealed class ConfigurationOwnershipManifestStore
{
    private readonly IFileSystem fileSystem;

    public ConfigurationOwnershipManifestStore(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public void Save(string path, ConfigurationOwnershipManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);
        fileSystem.AtomicWriteAllText(
            path,
            ConfigurationOwnershipManifestSerializer.Serialize(manifest)
        );
    }

    public ConfigurationOwnershipManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ConfigurationOwnershipManifestSerializer.Deserialize(fileSystem.ReadAllText(path));
    }
}
