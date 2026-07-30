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
    private const string PhysicalTargetManifestPreclaimMetadataKey =
        "hcoona.azureAuthCredProvider.physicalTargetManifestState";
    private const string PhysicalTargetManifestPreclaimMetadataValue = "prepared";
    private const string GitConfigDevAzureComUseHttpPathKey =
        "credential.https://dev.azure.com.useHttpPath";
    private static readonly string GitConfigDevAzureComUseHttpPathTrueSha256 =
        ComputeSha256("true");
    private static readonly SemaphoreSlim ExecutionLock = new(1, 1);
    private static readonly AsyncLocal<bool> ExecutionLockHeldByCurrentAsyncFlow = new();
    private readonly IFileSystem? fileSystem;
    private readonly string? ownershipManifestPath;
    private readonly IConfigurationPhysicalTargetWriterDispatcher?
        physicalTargetWriterDispatcher;

    public ConfigurationManager() { }

    internal ConfigurationManager(
        IFileSystem fileSystem,
        string ownershipManifestPath,
        IConfigurationPhysicalTargetWriterDispatcher? physicalTargetWriterDispatcher = null
    )
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownershipManifestPath);
        string normalizedOwnershipManifestPath = GetNormalizableOwnershipManifestPath(
            this.fileSystem,
            ownershipManifestPath
        );
        EnsureOwnershipManifestPathIsNotReservedInternalFileSystemArtifact(
            normalizedOwnershipManifestPath
        );
        this.ownershipManifestPath = normalizedOwnershipManifestPath;
        this.physicalTargetWriterDispatcher = physicalTargetWriterDispatcher;
    }

    public ConfigurationPlanValidationResult ValidatePlan(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetValidatePlanValidationViolation(plan);
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
        ArgumentNullException.ThrowIfNull(plan);
        EnsureValidContract(plan);
        bool filesystemBacked = fileSystem is not null && ownershipManifestPath is not null;
        bool containsProjectionOnlyPhysicalTarget = ContainsProjectionOnlyPhysicalTarget(plan);
        if (filesystemBacked && containsProjectionOnlyPhysicalTarget)
        {
            EnsureValidForPhysicalTargetDryRunBeforeProjection(plan);
        }
        else if (filesystemBacked)
        {
            EnsureValidForPlanning(plan);
        }
        else
        {
            EnsureValidForNoFilesystemPlanning(plan);
        }

        EnsureNoOwnershipManifestPathCollisionWithPhysicalTargets(plan);
        EnsureNoFilesystemBackedPhysicalTargetKindSamePathConflicts(plan);

        if (filesystemBacked && containsProjectionOnlyPhysicalTarget)
        {
            EnsureNoReservedInternalNonCiPhysicalTargetPaths(plan);
            EnsurePhysicalTargetDryRunDispatchPlanShapeSupported(fileSystem, plan);
            EnsureProjectedOwnershipManifestValid(plan);
        }

        ConfigurationChangePlan projectedPlan = containsProjectionOnlyPhysicalTarget
            ? CanonicalizePhysicalTargetPlanForProjection(plan, filesystemBacked)
            : plan;
        ConfigurationPlanResult plannedResult = CreatePlannedResult(
            projectedPlan,
            ConfigurationPlanOperation.DryRun
        );
        if (!filesystemBacked)
        {
            ValidateProjectedPhysicalTargetManifestForReturn(plannedResult.OwnershipManifest);
            return ValueTask.FromResult(plannedResult);
        }

        EnsureFilesystemBackedDryRunOperationSupported(plan);
        return ValueTask.FromResult(
            SimulateFilesystemBackedDryRun(plannedResult, projectedPlan, cancellationToken)
        );
    }

    public ValueTask<ConfigurationPlanResult> ApplyAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureValidForExecution(plan, ConfigurationPlanOperation.Apply);
        return ExecuteAsync(plan, ConfigurationPlanOperation.Apply, cancellationToken);
    }

    public ValueTask<ConfigurationPlanResult> RemoveAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureValidForExecution(plan, ConfigurationPlanOperation.Remove);
        return ExecuteAsync(plan, ConfigurationPlanOperation.Remove, cancellationToken);
    }

    private async ValueTask<ConfigurationPlanResult> ExecuteAsync(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        if (fileSystem is null || ownershipManifestPath is null)
        {
            return await ValueTask.FromException<ConfigurationPlanResult>(
                new InvalidOperationException(
                    "Configuration apply/remove execution requires a filesystem-backed "
                        + "configuration manager with an ownership manifest path."
                )
            );
        }

        EnsureNoOwnershipManifestPathCollisionWithPhysicalTargets(plan);
        EnsureNoFilesystemBackedPhysicalTargetKindSamePathConflicts(plan);
        if (ContainsProjectionOnlyPhysicalTarget(plan))
        {
            EnsureNoReservedInternalNonCiPhysicalTargetPaths(plan);
            EnsurePhysicalTargetDispatchPlanShapeSupported(fileSystem, plan, operation);
            EnsureConditionalFileMutationsSupported(fileSystem);
            ConfigurationChangePlan physicalPlan = CanonicalizePhysicalTargetPlan(plan);
            EnsureGitConfigPhysicalWriterPreclaimValidationSupported(physicalPlan);
            ConfigurationPlanResult physicalPlannedResult = CreatePlannedResult(
                physicalPlan,
                operation
            );
            return await ExecutePhysicalTargetPlan(
                physicalPlannedResult,
                physicalPlan,
                operation,
                cancellationToken
            );
        }

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
            return plannedResult with
            {
                State = ConfigurationPlanState.Applied,
                OwnershipManifest = appliedOwnershipManifest,
            };
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            return await ValueTask.FromException<ConfigurationPlanResult>(exception);
        }
    }

    private async ValueTask<ConfigurationPlanResult> ExecutePhysicalTargetPlan(
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        if (physicalTargetWriterDispatcher is null)
        {
            return await ValueTask.FromException<ConfigurationPlanResult>(
                new NotSupportedException(
                    "Configuration apply/remove has no registered writer for 4D physical "
                        + "configuration targets."
                )
            );
        }

        try
        {
            ConfigurationOwnershipManifest? appliedOwnershipManifest =
                await ExecutePhysicalTargetPlanWithManifest(
                    plannedResult,
                    plan,
                    operation,
                    cancellationToken
                );
            return plannedResult with
            {
                State = ConfigurationPlanState.Applied,
                OwnershipManifest = appliedOwnershipManifest,
            };
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            return await ValueTask.FromException<ConfigurationPlanResult>(exception);
        }
    }

    private async ValueTask<ConfigurationOwnershipManifest?> ExecutePhysicalTargetPlanWithManifest(
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        EnsureNoConfigurationExecutionAlreadyInProgress();
        if (!ExecutionLock.Wait(0, cancellationToken))
        {
            throw new InvalidOperationException(
                "Configuration apply/remove execution is already in progress; Phase 4D physical "
                    + "target dispatch does not allow concurrent or reentrant execution."
            );
        }

        ExecutionLockHeldByCurrentAsyncFlow.Value = true;
        try
        {
            IFileSystem executionFileSystem = fileSystem!;
            string manifestPath = ownershipManifestPath!;
            IDisposable? crossProcessExecutionLock = null;
            try
            {
                if (plan.TemporaryContainer is not null)
                {
                    crossProcessExecutionLock = AcquireConfigurationExecutionLock(
                        executionFileSystem,
                        plan,
                        manifestPath
                    );
                }

                PhysicalTargetManifestDispatchPreparation preparation =
                    PreparePhysicalTargetManifestDispatch(
                        plannedResult,
                        plan,
                        operation,
                        cancellationToken
                    );
                ContainerRollbackSnapshot? containerSnapshot =
                    plan.TemporaryContainer is null
                        ? null
                        : CaptureContainerRollbackSnapshot(executionFileSystem, plan);
                var completedWrites = new Stack<FileRollbackSnapshot>();
                if (plan.TemporaryContainer is null)
                {
                    crossProcessExecutionLock = AcquireConfigurationExecutionLock(
                        executionFileSystem,
                        plan,
                        manifestPath
                    );
                    _ = ValidateCurrentManifestBeforePhysicalTargetManifestCommit(
                        executionFileSystem,
                        manifestPath,
                        plan,
                        operation,
                        preparation.ManifestRollbackSnapshot
                    );
                }

                FileRollbackSnapshot manifestDispatchSnapshot =
                    preparation.ManifestRollbackSnapshot;
                var dispatchRequest = new ConfigurationPhysicalTargetWriterRequest(
                    operation,
                    plan.Changes[0].TargetKind,
                    plan.Changes,
                    preparation.OwnershipProofs
                )
                {
                    ResourceIdentity = plan.Manifest.ResourceIdentity,
                };
                ValidatePhysicalTargetDispatchBeforeManifestPreclaim(
                    plan,
                    operation,
                    preparation.OwnershipProofs,
                    cancellationToken
                );
                bool physicalTargetRollbackSafetyUnproven = false;
                bool finalManifestRollbackUnsafeDueToStaleRetainedProof = false;
                try
                {
                    manifestDispatchSnapshot = WritePreparedPhysicalTargetManifestPreclaim(
                        executionFileSystem,
                        manifestPath,
                        preparation,
                        completedWrites
                    );
                    cancellationToken.ThrowIfCancellationRequested();
                    await physicalTargetWriterDispatcher!.Dispatch(
                        dispatchRequest,
                        cancellationToken
                    );

                    try
                    {
                        ValidateAndRegisterCompletedPhysicalTargetFileMutations(
                            executionFileSystem,
                            plan,
                            dispatchRequest.CompletedFileMutations,
                            completedWrites
                        );
                    }
                    catch (Exception completedMutationException)
                        when (completedMutationException is not OperationCanceledException)
                    {
                        physicalTargetRollbackSafetyUnproven = true;
                        throw;
                    }

                    ConfigurationPhysicalTargetOwnershipProof[] retainedOwnershipProofs =
                        CreatePhysicalTargetOwnershipProofs(
                            preparation.PreparedOwnershipManifest
                        );
                    ValidateRetainedOwnershipProofsAfterManifestPreclaim(
                        retainedOwnershipProofs,
                        ref finalManifestRollbackUnsafeDueToStaleRetainedProof,
                        cancellationToken
                    );
                    CommitPreparedPhysicalTargetManifestDispatch(
                        executionFileSystem,
                        manifestPath,
                        preparation,
                        manifestDispatchSnapshot,
                        completedWrites
                    );
                    try
                    {
                        ValidateAndRegisterCompletedPhysicalTargetFileMutations(
                            executionFileSystem,
                            plan,
                            dispatchRequest.CompletedFileMutations,
                            completedWrites
                        );
                    }
                    catch (Exception completedMutationException)
                        when (completedMutationException is not OperationCanceledException)
                    {
                        physicalTargetRollbackSafetyUnproven = true;
                        throw;
                    }

                    ValidateRetainedOwnershipProofsAfterManifestPreclaim(
                        retainedOwnershipProofs,
                        ref finalManifestRollbackUnsafeDueToStaleRetainedProof,
                        cancellationToken
                    );
                    VerifyCurrentPhysicalTargetManifestMatchesPreparedFinalState(
                        executionFileSystem,
                        manifestPath,
                        preparation.PreparedOwnershipManifest
                    );
                    if (
                        operation == ConfigurationPlanOperation.Remove
                        && preparation.DeleteManifest
                        && containerSnapshot is not null
                    )
                    {
                        DeleteTemporaryContainerAfterFullRemove(
                            executionFileSystem,
                            plan,
                            manifestPath,
                            containerSnapshot
                        );
                    }

                    return preparation.PreparedOwnershipManifest;
                }
                catch (Exception exception)
                {
                    if (exception is PhysicalTargetManifestCommitIndeterminateException)
                    {
                        if (plan.ContainsCredentialMaterial)
                        {
                            ThrowSanitizedPhysicalTargetIndeterminateFailure(exception);
                        }

                        throw;
                    }
                    Exception failure = exception;
                    if (dispatchRequest.CompletedFileMutations.Count > 0)
                    {
                        try
                        {
                            ValidateAndRegisterCompletedPhysicalTargetFileMutations(
                                executionFileSystem,
                                plan,
                                dispatchRequest.CompletedFileMutations,
                                completedWrites
                            );
                        }
                        catch (Exception completedMutationException)
                            when (completedMutationException is not OperationCanceledException)
                        {
                            failure = completedMutationException;
                            physicalTargetRollbackSafetyUnproven = true;
                        }
                    }
                    try
                    {
                        RollBackPhysicalTargetDispatchWithoutMaskingConflict(
                            executionFileSystem,
                            manifestPath,
                            plan,
                            manifestDispatchSnapshot,
                            preparation.PreparedOwnershipManifest,
                            completedWrites,
                            physicalTargetRollbackSafetyUnproven,
                            finalManifestRollbackUnsafeDueToStaleRetainedProof,
                            failure
                        );
                        if (containerSnapshot is not null)
                        {
                            DeleteTemporaryContainerAfterRollback(
                                executionFileSystem,
                                plan,
                                containerSnapshot
                            );
                        }
                    }
                    catch (Exception rollbackException)
                        when (
                            plan.ContainsCredentialMaterial
                            && rollbackException is not OperationCanceledException
                        )
                    {
                        ThrowSanitizedPhysicalTargetRollbackFailure(
                            failure,
                            rollbackException
                        );
                    }

                    if (failure is OperationCanceledException)
                    {
                        throw;
                    }

                    if (plan.ContainsCredentialMaterial)
                    {
                        ThrowSanitizedPhysicalTargetDispatchFailure(failure);
                    }

                    ExceptionDispatchInfo.Capture(failure).Throw();
                    throw;
                }
            }
            finally
            {
                crossProcessExecutionLock?.Dispose();
            }
        }
        finally
        {
            ExecutionLockHeldByCurrentAsyncFlow.Value = false;
            ExecutionLock.Release();
        }
    }

    private static void ThrowSanitizedPhysicalTargetDispatchFailure(
        Exception exception,
        string message = "Configuration physical target dispatch failure was rolled back."
    )
    {
        var sanitizedException = new InvalidOperationException(message);
        sanitizedException.Data["ConfigurationDispatchExceptionType"] =
            exception.GetType().FullName ?? exception.GetType().Name;
        sanitizedException.Data["ConfigurationDispatchExceptionHResult"] = exception.HResult;
        throw sanitizedException;
    }

    private static void ThrowSanitizedPhysicalTargetRollbackFailure(
        Exception dispatchException,
        Exception rollbackException
    )
    {
        var sanitizedException = new InvalidOperationException(
            "Configuration physical target dispatch failure rollback failed."
        );
        sanitizedException.Data["ConfigurationDispatchExceptionType"] =
            dispatchException.GetType().FullName ?? dispatchException.GetType().Name;
        sanitizedException.Data["ConfigurationDispatchExceptionHResult"] =
            dispatchException.HResult;
        sanitizedException.Data["ConfigurationRollbackExceptionType"] =
            rollbackException.GetType().FullName ?? rollbackException.GetType().Name;
        sanitizedException.Data["ConfigurationRollbackExceptionHResult"] =
            rollbackException.HResult;
        throw sanitizedException;
    }

    private static void ThrowSanitizedPhysicalTargetIndeterminateFailure(Exception exception) =>
        ThrowSanitizedPhysicalTargetDispatchFailure(
            exception,
            "Configuration physical target dispatch final manifest commit is indeterminate."
        );

    private PhysicalTargetManifestDispatchPreparation PreparePhysicalTargetManifestDispatch(
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        IFileSystem executionFileSystem = fileSystem!;
        string manifestPath = ownershipManifestPath!;
        EnsureManifestParentChainIsUsable(executionFileSystem, manifestPath);
        EnsurePathIsNotUnsupportedReparsePoint(
            executionFileSystem,
            manifestPath,
            "configuration ownership manifest"
        );
        FileRollbackSnapshot manifestSnapshot = CaptureRollbackSnapshot(
            executionFileSystem,
            manifestPath
        );
        ValidateExistingManifest(executionFileSystem, plan, manifestSnapshot, operation);
        ConfigurationOwnershipManifest ownershipManifest =
            plannedResult.OwnershipManifest
            ?? throw new InvalidOperationException(
                "Configuration execution requires a projected ownership manifest."
            );
        ConfigurationOwnershipManifest? manifestToWrite = null;
        bool deleteManifest = false;
        ConfigurationOwnershipManifest? existingManifest = manifestSnapshot.Existed
            ? ConfigurationOwnershipManifestSerializer.Deserialize(manifestSnapshot.Contents!)
            : null;
        if (operation == ConfigurationPlanOperation.Remove)
        {
            manifestToWrite = CreateRemainingManifestAfterRemove(
                executionFileSystem,
                existingManifest
                    ?? throw new InvalidOperationException(
                        "Configuration remove requires an existing ownership manifest."
                    ),
                ownershipManifest,
                plan
            );
            deleteManifest = manifestToWrite is null;
        }
        else
        {
            manifestToWrite = CreateMergedManifestForApply(
                executionFileSystem,
                manifestSnapshot,
                ownershipManifest,
                plan
            );
        }

        return new PhysicalTargetManifestDispatchPreparation(
            manifestToWrite,
            deleteManifest,
            manifestSnapshot,
            CreatePhysicalTargetOwnershipProofs(existingManifest)
        );
    }

    private void ValidatePhysicalTargetDispatchBeforeManifestPreclaim(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        CancellationToken cancellationToken
    )
    {
        IFileSystem executionFileSystem = fileSystem
            ?? throw new InvalidOperationException(
                "Configuration physical target dispatch validation requires a filesystem-backed "
                    + "configuration manager."
            );
        ConfigurationPhysicalTargetWriterDispatcher? fallbackDispatcher = null;
        IConfigurationPhysicalTargetWriterDispatcherValidator validator =
            physicalTargetWriterDispatcher as IConfigurationPhysicalTargetWriterDispatcherValidator
            ?? (fallbackDispatcher ??= new ConfigurationPhysicalTargetWriterDispatcher(
                executionFileSystem
            ));
        validator.Validate(
            new ConfigurationPhysicalTargetWriterRequest(
                operation,
                plan.Changes[0].TargetKind,
                plan.Changes,
                ownershipProofs
            )
            {
                ResourceIdentity = plan.Manifest.ResourceIdentity,
            },
            cancellationToken
        );

        IConfigurationPhysicalTargetRetainedOwnershipProofValidator retainedProofValidator =
            physicalTargetWriterDispatcher
            as IConfigurationPhysicalTargetRetainedOwnershipProofValidator
            ?? (fallbackDispatcher ??= new ConfigurationPhysicalTargetWriterDispatcher(
                executionFileSystem
            ));
        retainedProofValidator.ValidateRetainedOwnershipProofs(
            ownershipProofs,
            cancellationToken
        );
    }

    private void ValidateRetainedOwnershipProofsBeforePhysicalTargetManifestCommit(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        CancellationToken cancellationToken
    )
    {
        IFileSystem executionFileSystem = fileSystem
            ?? throw new InvalidOperationException(
                "Configuration physical target retained-proof validation requires a "
                    + "filesystem-backed configuration manager."
            );
        IConfigurationPhysicalTargetRetainedOwnershipProofValidator retainedProofValidator =
            physicalTargetWriterDispatcher
            as IConfigurationPhysicalTargetRetainedOwnershipProofValidator
            ?? new ConfigurationPhysicalTargetWriterDispatcher(executionFileSystem);
        retainedProofValidator.ValidateRetainedOwnershipProofs(
            ownershipProofs,
            cancellationToken
        );
    }

    private void ValidateRetainedOwnershipProofsAfterManifestPreclaim(
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> ownershipProofs,
        ref bool finalManifestRollbackUnsafeDueToStaleRetainedProof,
        CancellationToken cancellationToken
    )
    {
        try
        {
            ValidateRetainedOwnershipProofsBeforePhysicalTargetManifestCommit(
                ownershipProofs,
                cancellationToken
            );
            finalManifestRollbackUnsafeDueToStaleRetainedProof = false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            finalManifestRollbackUnsafeDueToStaleRetainedProof = true;
            throw;
        }
    }

    private static void ValidateGitConfigRetainedUseHttpPathOwnershipProofs(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest? manifest,
        CancellationToken cancellationToken
    )
    {
        ConfigurationPhysicalTargetOwnershipProof[] retainedProofs =
            CreatePhysicalTargetOwnershipProofs(manifest);
        if (retainedProofs.Length == 0)
        {
            return;
        }

        new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
            .ValidateRetainedOwnershipProofs(retainedProofs, cancellationToken);
    }

    private static void ValidateGitConfigRetainedUseHttpPathOwnershipProofsAfterGenericMutation(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest? manifest,
        CancellationToken cancellationToken
    )
    {
        if (CreatePhysicalTargetOwnershipProofs(manifest).Length == 0)
        {
            return;
        }

        ValidateGitConfigRetainedUseHttpPathOwnershipProofs(
            fileSystem,
            manifest,
            cancellationToken
        );
    }

    private static FileRollbackSnapshot WritePreparedPhysicalTargetManifestPreclaim(
        IFileSystem executionFileSystem,
        string manifestPath,
        PhysicalTargetManifestDispatchPreparation preparation,
        Stack<FileRollbackSnapshot> completedWrites
    )
    {
        ConfigurationOwnershipManifest preclaimManifest =
            CreatePhysicalTargetManifestPreclaim(executionFileSystem, preparation);
        string preclaimManifestContents = ConfigurationOwnershipManifestSerializer.Serialize(
            preclaimManifest
        );
        ExecuteAtomicWriteWithRollbackRegistration(
            executionFileSystem,
            manifestPath,
            preclaimManifestContents,
            options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
            snapshot: preparation.ManifestRollbackSnapshot,
            expectedCurrentHashForRollback: ComputeSha256(preclaimManifestContents),
            completedWrites: completedWrites
        );
        FileRollbackSnapshot preclaimSnapshot = CaptureRollbackSnapshot(
            executionFileSystem,
            manifestPath
        );
        ValidateFileSnapshotIsRegularFile(
            preclaimSnapshot,
            "prepared configuration ownership manifest preclaim"
        );
        if (
            !string.Equals(
                preclaimSnapshot.ContentsSha256Hash,
                ComputeSha256(preclaimManifestContents),
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: prepared manifest preclaim changed "
                    + "before physical target dispatch."
            );
        }

        return preclaimSnapshot;
    }

    private static ConfigurationOwnershipManifest CreatePhysicalTargetManifestPreclaim(
        IFileSystem fileSystem,
        PhysicalTargetManifestDispatchPreparation preparation
    )
    {
        ConfigurationOwnershipManifest manifest = CanonicalizePhysicalTargetManifestForWrite(
            fileSystem,
            preparation.PreparedOwnershipManifest
                ?? (
                    preparation.ManifestRollbackSnapshot.Existed
                        ? ConfigurationOwnershipManifestSerializer.Deserialize(
                            preparation.ManifestRollbackSnapshot.Contents!
                        )
                        : throw new InvalidOperationException(
                            "Configuration physical target dispatch requires a manifest preclaim."
                        )
                )
        );
        var safeMetadata = new Dictionary<string, string>(
            manifest.SafeMetadata,
            StringComparer.Ordinal
        )
        {
            [PhysicalTargetManifestPreclaimMetadataKey] =
                PhysicalTargetManifestPreclaimMetadataValue,
        };
        return manifest with { SafeMetadata = safeMetadata };
    }

    private static ConfigurationOwnershipManifest
        EnsureFinalPhysicalTargetManifestHasNoPreclaimMetadata(
        ConfigurationOwnershipManifest manifest
    )
    {
        if (manifest.SafeMetadata.ContainsKey(PhysicalTargetManifestPreclaimMetadataKey))
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: final manifest contains reserved "
                    + "physical target preclaim metadata."
            );
        }

        return manifest;
    }

    private static void CommitPreparedPhysicalTargetManifestDispatch(
        IFileSystem executionFileSystem,
        string manifestPath,
        PhysicalTargetManifestDispatchPreparation preparation,
        FileRollbackSnapshot manifestDispatchSnapshot,
        Stack<FileRollbackSnapshot> completedWrites
    )
    {
        FileRollbackSnapshot manifestSnapshot = ValidatePreparedManifestPreclaimStillCurrent(
            executionFileSystem,
            manifestPath,
            manifestDispatchSnapshot
        );
        if (preparation.DeleteManifest)
        {
            try
            {
                ExecuteDeleteWithRollbackRegistration(
                    executionFileSystem,
                    manifestPath,
                    manifestSnapshot,
                    expectedCurrentHashForRollback: null,
                    completedWrites
                );
            }
            catch (FileMutationException exception)
                when (exception.MutationMayHaveReachedDurableState)
            {
                if (
                    !CurrentManifestMatchesPreparedFinalState(
                        executionFileSystem,
                        manifestPath,
                        preparation.PreparedOwnershipManifest
                    )
                )
                {
                    throw new PhysicalTargetManifestCommitIndeterminateException(exception);
                }
            }

            VerifyCurrentPhysicalTargetManifestMatchesPreparedFinalState(
                executionFileSystem,
                manifestPath,
                preparation.PreparedOwnershipManifest
            );
            return;
        }

        string manifestContents = ConfigurationOwnershipManifestSerializer.Serialize(
            CanonicalizePhysicalTargetManifestForWrite(
                executionFileSystem,
                EnsureFinalPhysicalTargetManifestHasNoPreclaimMetadata(
                    preparation.PreparedOwnershipManifest
                        ?? throw new InvalidOperationException(
                            "Configuration execution requires a prepared ownership manifest."
                        )
                )
            )
        );
        try
        {
            ExecuteAtomicWriteWithRollbackRegistration(
                executionFileSystem,
                manifestPath,
                manifestContents,
                options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
                snapshot: manifestSnapshot,
                expectedCurrentHashForRollback: ComputeSha256(manifestContents),
                completedWrites: completedWrites
            );
        }
        catch (FileMutationException exception)
            when (exception.MutationMayHaveReachedDurableState)
        {
            if (
                !CurrentManifestMatchesPreparedFinalState(
                    executionFileSystem,
                    manifestPath,
                    preparation.PreparedOwnershipManifest
                )
            )
            {
                throw new PhysicalTargetManifestCommitIndeterminateException(exception);
            }
        }

        VerifyCurrentPhysicalTargetManifestMatchesPreparedFinalState(
            executionFileSystem,
            manifestPath,
            preparation.PreparedOwnershipManifest
        );
    }

    private void EnsureValidForExecution(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureValidContract(plan);

        if (ContainsProjectionOnlyPhysicalTarget(plan))
        {
            EnsureValidForPhysicalTargetExecutionBeforeProjection(plan, operation);
        }

        EnsureProjectedOwnershipManifestValid(plan);
    }

    private void EnsureValidForPhysicalTargetExecutionBeforeProjection(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        string? violation = GetNuGetPluginLayoutPlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        violation = GetPhase4DPhysicalScaffoldPrecedencePlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        EnsureNoOwnershipManifestPathCollisionWithPhysicalTargets(plan);
        EnsureNoFilesystemBackedPhysicalTargetKindSamePathConflicts(plan);
        EnsureGitConfigGoldenSliceSupported(plan);
        EnsureNoReservedInternalNonCiPhysicalTargetPaths(plan);
        EnsurePhysicalTargetDispatchPlanShapeSupported(fileSystem, plan, operation);
    }

    private void EnsureProjectedOwnershipManifestValid(ConfigurationChangePlan plan)
    {
        string? violation = GetProjectionValidationViolation(plan, fileSystem);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        EnsureGitConfigGoldenSliceSupported(plan);
    }

    private string? GetValidatePlanValidationViolation(ConfigurationChangePlan plan)
    {
        string? violation = GetContractValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        violation = GetReservedPreclaimMetadataValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        violation = GetNuGetPluginLayoutPlanningValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        if (ContainsProjectionOnlyPhysicalTarget(plan))
        {
            violation = GetPhase4DPhysicalScaffoldPrecedencePlanningValidationViolation(plan);
            violation ??= GetOwnershipManifestPathCollisionWithPhysicalTargetsViolation(plan);
            violation ??= GetFilesystemBackedPhysicalTargetKindSamePathConflictViolation(plan);
            violation ??= GetReservedInternalPlanningPhysicalTargetPathViolation(plan);
            violation ??= GetNpmrcStaticWriterPlanningValidationViolation(plan, fileSystem);
            violation ??= GetYarnrcStaticWriterPlanningValidationViolation(plan, fileSystem);
            violation ??= GetPhase4DPhysicalScaffoldPlanningViolation(fileSystem, plan);
            violation ??= GetGitConfigStaticWriterPlanningValidationViolation(
                plan,
                GetRejectSecretGitConfigValueWritesBeforeManifestPreclaim()
            );
            return violation ?? GetProjectionValidationViolation(plan, fileSystem);
        }

        violation = GetProjectionValidationViolation(plan, fileSystem);
        violation ??= GetAdditionalPlanningValidationViolation(
            plan,
            includeCiTemporaryFileReservedPaths: true
        );
        violation ??= GetOwnershipManifestPathCollisionWithPhysicalTargetsViolation(plan);
        violation ??= GetFilesystemBackedPhysicalTargetKindSamePathConflictViolation(plan);
        return violation;
    }

    private void EnsureValidForPhysicalTargetDryRunBeforeProjection(
        ConfigurationChangePlan plan
    )
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetNuGetPluginLayoutPlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        violation = GetPhase4DPhysicalScaffoldPrecedencePlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private void
        EnsureNoReservedInternalNonCiPhysicalTargetPaths(ConfigurationChangePlan plan)
    {
        string? violation = GetReservedInternalNonCiPhysicalTargetPathViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private static void EnsureValidContract(ConfigurationChangePlan plan)
    {
        string? violation = GetContractValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        violation = GetReservedPreclaimMetadataValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private void EnsureValidForPlanning(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetPlanningValidationViolation(
            plan,
            GetRejectSecretGitConfigValueWritesBeforeManifestPreclaim()
        );
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private void EnsureValidForNoFilesystemPlanning(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetNuGetPluginLayoutPlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        violation = GetPhase4DPhysicalScaffoldFirstPlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        violation = GetPythonKeyringPlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        violation = GetNpmrcStaticWriterPlanningValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        violation = GetGitConfigStaticWriterPlanningValidationViolation(
            plan,
            GetRejectSecretGitConfigValueWritesBeforeManifestPreclaim()
        );
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }

        violation = GetProjectionValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private static string? GetContractValidationViolation(ConfigurationChangePlan plan) =>
        ConfigurationChangePlanPolicy.GetViolation(plan);

    private static string? GetReservedPreclaimMetadataValidationViolation(
        ConfigurationChangePlan plan
    ) =>
        plan.Manifest?.SafeMetadata?.ContainsKey(PhysicalTargetManifestPreclaimMetadataKey) == true
            ? "Configuration manifest metadata uses a reserved physical target preclaim key."
            : null;

    private static string? GetProjectionValidationViolation(
        ConfigurationChangePlan plan,
        IFileSystem? fileSystem = null
    )
    {
        ConfigurationPlannedOperation[] plannedOperations =
            ConfigurationPlanProjector.CreatePlannedOperations(plan);
        ConfigurationOwnershipManifest manifest =
            ConfigurationPlanProjector.CreateOwnershipManifest(plan, plannedOperations);
        string? violation = ConfigurationOwnershipManifestPolicy.GetViolation(manifest);
        if (violation is not null)
        {
            return violation;
        }

        if (fileSystem is null)
        {
            violation = GetGitConfigPhysicalTargetEntriesPathViolation(
                manifest
                    .Entries.Where(entry => entry.TargetKind == ConfigurationTargetKind.GitConfig),
                LooksLikeRelativePhysicalPath
            );
            if (violation is not null)
            {
                return violation;
            }
        }

        Func<string, bool> isPathFullyQualified = fileSystem is null
            ? Path.IsPathFullyQualified
            : fileSystem.IsPathFullyQualified;
        return GetNpmrcPhysicalTargetEntriesPathViolation(
            manifest.Entries.Where(entry =>
                entry.TargetKind is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc
            ),
            isPathFullyQualified
        );
    }

    private static string? GetPlanningValidationViolation(
        ConfigurationChangePlan plan,
        bool rejectSecretValueWrites
    )
    {
        string? violation = GetContractValidationViolation(plan);
        violation ??= GetReservedPreclaimMetadataValidationViolation(plan);
        violation ??= GetProjectionValidationViolation(plan);
        violation ??= GetGitConfigStaticWriterPlanningValidationViolation(
            plan,
            rejectSecretValueWrites
        );
        if (violation is not null)
        {
            return violation;
        }

        return GetAdditionalPlanningValidationViolation(plan);
    }

    private static string? GetPhase4DPhysicalScaffoldFirstPlanningValidationViolation(
        ConfigurationChangePlan plan
    )
    {
        string? violation = GetContractValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        if (!ContainsProjectionOnlyPhysicalTarget(plan))
        {
            return GetProjectionValidationViolation(plan)
                ?? GetAdditionalPlanningValidationViolation(
                    plan,
                    includeCiTemporaryFileReservedPaths: true
                );
        }

        violation = GetPhase4DPhysicalScaffoldPrecedencePlanningValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        violation = GetLexicalReservedInternalPlanningPhysicalTargetPathViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        violation = GetNpmrcStaticWriterPlanningValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        violation = GetYarnrcStaticWriterPlanningValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        violation = GetPhase4DPhysicalScaffoldPlanningViolation(null, plan);
        if (violation is not null)
        {
            return violation;
        }

        violation = GetGitConfigStaticWriterPlanningValidationViolation(
            plan,
            rejectSecretValueWrites: true
        );
        if (violation is not null)
        {
            return violation;
        }

        violation = GetProjectionValidationViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        return null;
    }

    private static string? GetAdditionalPlanningValidationViolation(ConfigurationChangePlan plan) =>
        GetAdditionalPlanningValidationViolation(
            plan,
            includeCiTemporaryFileReservedPaths: false
        );

    private static string? GetAdditionalPlanningValidationViolation(
        ConfigurationChangePlan plan,
        bool includeCiTemporaryFileReservedPaths
    ) =>
        GetPlanningValidationViolationBeforeReservedPath(plan)
            ?? (
                includeCiTemporaryFileReservedPaths
                    ? GetLexicalReservedInternalPlanningPhysicalTargetPathViolation(plan)
                    : GetLexicalReservedInternalNonCiPhysicalTargetPathViolation(plan)
            )
            ?? GetCiTemporaryFileUnsupportedOperationViolation(plan);

    private static string? GetPhase4DPhysicalScaffoldPrecedencePlanningValidationViolation(
        ConfigurationChangePlan plan
    ) =>
        GetPlanningValidationViolationBeforeReservedPath(plan)
            ?? GetCiTemporaryFileUnsupportedOperationViolation(plan);

    private static string? GetPlanningValidationViolationBeforeReservedPath(
        ConfigurationChangePlan plan
    ) =>
        GetCiTemporaryFilePlanWholeFileOwnershipViolation(plan)
            ?? GetPhysicalTargetKindSamePathConflictViolation(plan);

    private static string? GetPhase4DPhysicalScaffoldPlanningViolation(
        IFileSystem? fileSystem,
        ConfigurationChangePlan plan
    )
    {
        if (!ContainsProjectionOnlyPhysicalTarget(plan))
        {
            return null;
        }

        try
        {
            EnsurePhysicalTargetDryRunDispatchPlanShapeSupported(fileSystem, plan);
            return null;
        }
        catch (NotSupportedException exception)
        {
            return exception.Message;
        }
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

    private static void EnsurePythonKeyringTargetPathsAreCanonical(ConfigurationChangePlan plan)
    {
        foreach (
            ConfigurationChange change in plan.Changes.Where(change =>
                change.TargetKind
                    is ConfigurationTargetKind.PythonKeyringBackend
                        or ConfigurationTargetKind.KeyringShim
            )
        )
        {
            string? violation = PythonKeyringPhysicalTargetWriter.GetPlanningValidationViolation(
                change
            );
            if (violation is not null)
            {
                throw new NotSupportedException(violation);
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

    private static string GetNormalizableOwnershipManifestPath(
        IFileSystem fileSystem,
        string ownershipManifestPath
    )
    {
        try
        {
            return fileSystem.GetFullPath(ownershipManifestPath);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or IOException)
        {
            throw new ArgumentException(
                "Configuration ownership manifest path must be a normalizable physical path.",
                nameof(ownershipManifestPath),
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

    private string? GetReservedInternalPlanningPhysicalTargetPathViolation(
        ConfigurationChangePlan plan
    ) =>
        GetReservedInternalPhysicalTargetPathViolation(
            plan,
            includeCiTemporaryFileTargets: true
        );

    private string? GetReservedInternalNonCiPhysicalTargetPathViolation(
        ConfigurationChangePlan plan
    ) =>
        GetReservedInternalPhysicalTargetPathViolation(
            plan,
            includeCiTemporaryFileTargets: false
        );

    private string? GetReservedInternalPhysicalTargetPathViolation(
        ConfigurationChangePlan plan,
        bool includeCiTemporaryFileTargets
    )
    {
        return plan.Changes.Any(change =>
            (
                includeCiTemporaryFileTargets
                    ? IsPhysicalFileSystemTarget(change.TargetKind)
                    : IsNonCiPhysicalFileSystemTarget(change.TargetKind)
            )
            && IsReservedInternalPhysicalTargetPath(change)
        )
            ? "Protocol violation: physical configuration targets must not use reserved "
                + "internal filesystem artifact paths."
            : null;
    }

    private bool IsReservedInternalPhysicalTargetPath(ConfigurationChange change)
    {
        if (fileSystem is null || change.TargetKind == ConfigurationTargetKind.CiTemporaryFile)
        {
            return IsReservedInternalConfigurationPathArtifact(change.TargetPathOrName);
        }

        string targetPath = GetNormalizablePhysicalPath(
            fileSystem,
            change.TargetPathOrName,
            $"{change.TargetKind} target"
        );
        return IsReservedInternalFileSystemArtifact(targetPath);
    }

    private static string? GetLexicalReservedInternalPlanningPhysicalTargetPathViolation(
        ConfigurationChangePlan plan
    ) =>
        GetLexicalReservedInternalPhysicalTargetPathViolation(
            plan,
            includeCiTemporaryFileTargets: true
        );

    private static string? GetLexicalReservedInternalNonCiPhysicalTargetPathViolation(
        ConfigurationChangePlan plan
    ) =>
        GetLexicalReservedInternalPhysicalTargetPathViolation(
            plan,
            includeCiTemporaryFileTargets: false
        );

    private static string? GetLexicalReservedInternalPhysicalTargetPathViolation(
        ConfigurationChangePlan plan,
        bool includeCiTemporaryFileTargets
    ) =>
        plan.Changes.Any(change =>
            (
                includeCiTemporaryFileTargets
                    ? IsPhysicalFileSystemTarget(change.TargetKind)
                    : IsNonCiPhysicalFileSystemTarget(change.TargetKind)
            )
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

    private static void ValidatePythonKeyringManifestEntriesAreVerifiableNonSecretValueWrites(
        IEnumerable<ConfigurationOwnershipManifestEntry> entries
    )
    {
        foreach (
            ConfigurationOwnershipManifestEntry entry in entries.Where(entry =>
                entry.TargetKind
                    is ConfigurationTargetKind.PythonKeyringBackend
                        or ConfigurationTargetKind.KeyringShim
            )
        )
        {
            string? pathViolation =
                PythonKeyringPhysicalTargetWriter.GetTargetPathValidationViolation(
                    entry.TargetPathOrName,
                    entry.TargetKind
                );
            if (pathViolation is not null)
            {
                throw new InvalidOperationException(pathViolation);
            }

            if (
                !string.Equals(entry.Key, "physical-target", StringComparison.Ordinal)
                || !IsValueWritingOperation(entry.Operation)
                || !entry.HasPlannedValue
                || entry.IsSecretValue
                || !IsLowercaseSha256Hex(entry.PlannedValueSha256)
            )
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: Python keyring physical target "
                        + "entries must be non-secret value-writing entries with verifiable "
                        + "planned value SHA-256 hashes."
                );
            }
        }
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

    private ConfigurationChangePlan CanonicalizePhysicalTargetPlan(ConfigurationChangePlan plan)
    {
        if (
            !plan.Changes.Any(change =>
                change.TargetKind is ConfigurationTargetKind.GitConfig
                    or ConfigurationTargetKind.Npmrc
                    or ConfigurationTargetKind.Yarnrc
                    or ConfigurationTargetKind.NuGetPluginLayout
            )
        )
        {
            return plan;
        }

        return plan with
        {
            Changes = plan.Changes.Select(CanonicalizePhysicalTargetChange).ToArray(),
        };
    }

    private ConfigurationChange CanonicalizePhysicalTargetChange(ConfigurationChange change)
    {
        if (
            change.TargetKind is ConfigurationTargetKind.GitConfig
                or ConfigurationTargetKind.Npmrc
                or ConfigurationTargetKind.Yarnrc
                or ConfigurationTargetKind.NuGetPluginLayout
        )
        {
            ConfigurationChange canonicalizedChange = change with
            {
                TargetPathOrName = CreatePhysicalPathIdentity(
                    fileSystem: fileSystem!,
                    change.TargetPathOrName
                ),
            };

            if (change.TargetKind != ConfigurationTargetKind.GitConfig)
            {
                return change.TargetKind
                    is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc
                    && !IsValueWritingOperation(change.Operation)
                    ? canonicalizedChange with { IsSecretValue = false }
                    : canonicalizedChange;
            }

            return canonicalizedChange with
            {
                Key = GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(
                    change.Key
                ),
            };
        }

        return change;
    }

    private ConfigurationChangePlan CanonicalizePhysicalTargetPlanForProjection(
        ConfigurationChangePlan plan,
        bool filesystemBacked
    ) =>
        filesystemBacked
            ? CanonicalizePhysicalTargetPlan(plan)
            : CanonicalizeNoFilesystemPhysicalTargetPlan(plan);

    private static ConfigurationChangePlan CanonicalizeNoFilesystemPhysicalTargetPlan(
        ConfigurationChangePlan plan
    )
    {
        if (
            !plan.Changes.Any(change =>
                change.TargetKind is ConfigurationTargetKind.GitConfig
                    or ConfigurationTargetKind.Npmrc
                    or ConfigurationTargetKind.Yarnrc
                    or ConfigurationTargetKind.NuGetPluginLayout
            )
        )
        {
            return plan;
        }

        return plan with
        {
            Changes = plan.Changes.Select(CanonicalizeNoFilesystemPhysicalTargetChange).ToArray(),
        };
    }

    private static ConfigurationChange CanonicalizeNoFilesystemPhysicalTargetChange(
        ConfigurationChange change
    )
    {
        if (
            change.TargetKind
                is not ConfigurationTargetKind.GitConfig
                and not ConfigurationTargetKind.Npmrc
                and not ConfigurationTargetKind.Yarnrc
                and not ConfigurationTargetKind.NuGetPluginLayout
        )
        {
            return change;
        }

        ConfigurationChange canonicalizedChange = change with
        {
            TargetPathOrName = change.TargetKind
                is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc
                ? CreateNoFilesystemPhysicalPathIdentity(change.TargetPathOrName)
                : CreatePlanningPhysicalPathIdentity(change),
        };

        if (change.TargetKind != ConfigurationTargetKind.GitConfig)
        {
            return change.TargetKind
                is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc
                && !IsValueWritingOperation(change.Operation)
                ? canonicalizedChange with { IsSecretValue = false }
                : canonicalizedChange;
        }

        return canonicalizedChange with
        {
            Key = GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(change.Key),
        };
    }

    private bool GetRejectSecretGitConfigValueWritesBeforeManifestPreclaim()
    {
        if (
            physicalTargetWriterDispatcher
            is not IConfigurationPhysicalTargetWriterDispatcherPreclaimPolicy preclaimPolicy
        )
        {
            return true;
        }

        if (
            !preclaimPolicy.RejectSecretGitConfigValueWritesBeforeManifestPreclaim
            && physicalTargetWriterDispatcher
                is not IConfigurationPhysicalTargetWriterDispatcherValidator
        )
        {
            return true;
        }

        return preclaimPolicy.RejectSecretGitConfigValueWritesBeforeManifestPreclaim;
    }

    private void EnsureGitConfigPhysicalWriterPreclaimValidationSupported(
        ConfigurationChangePlan plan
    )
    {
        string? violation = GetGitConfigStaticWriterPlanningValidationViolation(
            plan,
            GetRejectSecretGitConfigValueWritesBeforeManifestPreclaim()
        );
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private static string? GetGitConfigStaticWriterPlanningValidationViolation(
        ConfigurationChangePlan plan,
        bool rejectSecretValueWrites
    )
    {
        try
        {
            foreach (
                ConfigurationChange change in plan.Changes.Where(change =>
                    change.TargetKind == ConfigurationTargetKind.GitConfig
                )
            )
            {
                GitConfigPhysicalTargetWriter.ValidateChangeBeforeManifestPreclaim(
                    change,
                    rejectSecretValueWrites
                );
            }

            return null;
        }
        catch (NotSupportedException exception)
        {
            return exception.Message;
        }
    }

    private static string? GetNpmrcStaticWriterPlanningValidationViolation(
        ConfigurationChangePlan plan,
        IFileSystem? fileSystem = null
    )
    {
        foreach (
            ConfigurationChange change in plan.Changes.Where(change =>
                change.TargetKind == ConfigurationTargetKind.Npmrc
            )
        )
        {
            string? violation = NpmrcPhysicalTargetWriter.GetPlanningValidationViolation(
                change,
                plan.Manifest.ResourceIdentity
            );
            if (violation is not null)
            {
                return violation;
            }
        }

        string? batchViolation = GetNpmrcMixedOperationBatchViolation(plan);
        if (batchViolation is not null)
        {
            return batchViolation;
        }

        return GetNpmrcPhysicalTargetRequestShapeViolation(plan, fileSystem);
    }

    private static string? GetNpmrcMixedOperationBatchViolation(ConfigurationChangePlan plan)
    {
        ConfigurationChange[] npmrcChanges = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.Npmrc)
            .ToArray();
        if (npmrcChanges.Length <= 1)
        {
            return null;
        }

        bool hasValueWritingChanges = npmrcChanges.Any(change =>
            IsValueWritingOperation(change.Operation)
        );
        bool hasRemoveChanges = npmrcChanges.Any(
            change => change.Operation == ConfigurationChangeOperation.Remove
        );
        return hasValueWritingChanges && hasRemoveChanges
            ? "Protocol violation: Npmrc physical writer does not support mixed value-writing "
                + "and remove batches."
            : null;
    }

    private static string? GetNpmrcPhysicalTargetRequestShapeViolation(
        ConfigurationChangePlan plan,
        IFileSystem? fileSystem = null
    )
    {
        ConfigurationChange[] npmrcChanges = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.Npmrc)
            .ToArray();
        if (npmrcChanges.Length <= 1)
        {
            return null;
        }

        string firstPath = CreateDispatchPhysicalPathIdentity(fileSystem, npmrcChanges[0]);
        if (
            npmrcChanges
                .Skip(1)
                .Any(change =>
                    !ConfigurationPathIdentityComparer.Instance.Equals(
                        CreateDispatchPhysicalPathIdentity(fileSystem, change),
                        firstPath
                    )
                )
        )
        {
            return "Protocol violation: Npmrc physical writer supports only one normalized "
                + "physical file path per plan.";
        }

        if (
            npmrcChanges
                .GroupBy(change => change.Key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            return "Protocol violation: Npmrc physical writer supports only one change per "
                + "canonical key.";
        }

        return null;
    }

    private static string? GetYarnrcStaticWriterPlanningValidationViolation(
        ConfigurationChangePlan plan,
        IFileSystem? fileSystem = null
    )
    {
        foreach (
            ConfigurationChange change in plan.Changes.Where(change =>
                change.TargetKind == ConfigurationTargetKind.Yarnrc
            )
        )
        {
            string? violation = YarnrcPhysicalTargetWriter.GetPlanningValidationViolation(
                change,
                plan.Manifest.ResourceIdentity
            );
            if (violation is not null)
            {
                return violation;
            }
        }

        string? batchViolation = GetYarnrcMixedOperationBatchViolation(plan);
        if (batchViolation is not null)
        {
            return batchViolation;
        }

        return GetYarnrcPhysicalTargetRequestShapeViolation(plan, fileSystem);
    }

    private static string? GetYarnrcMixedOperationBatchViolation(ConfigurationChangePlan plan)
    {
        ConfigurationChange[] yarnrcChanges = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.Yarnrc)
            .ToArray();
        if (yarnrcChanges.Length <= 1)
        {
            return null;
        }

        bool hasValueWritingChanges = yarnrcChanges.Any(change =>
            IsValueWritingOperation(change.Operation)
        );
        bool hasRemoveChanges = yarnrcChanges.Any(
            change => change.Operation == ConfigurationChangeOperation.Remove
        );
        return hasValueWritingChanges && hasRemoveChanges
            ? "Protocol violation: Yarnrc physical writer does not support mixed value-writing "
                + "and remove batches."
            : null;
    }

    private static string? GetYarnrcPhysicalTargetRequestShapeViolation(
        ConfigurationChangePlan plan,
        IFileSystem? fileSystem = null
    )
    {
        ConfigurationChange[] yarnrcChanges = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.Yarnrc)
            .ToArray();
        if (yarnrcChanges.Length <= 1)
        {
            return null;
        }

        string firstPath = CreateDispatchPhysicalPathIdentity(fileSystem, yarnrcChanges[0]);
        if (
            yarnrcChanges
                .Skip(1)
                .Any(change =>
                    !ConfigurationPathIdentityComparer.Instance.Equals(
                        CreateDispatchPhysicalPathIdentity(fileSystem, change),
                        firstPath
                    )
                )
        )
        {
            return "Protocol violation: Yarnrc physical writer supports only one normalized "
                + "physical file path per plan.";
        }

        if (
            yarnrcChanges
                .GroupBy(change => change.Key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            return "Protocol violation: Yarnrc physical writer supports only one change per "
                + "canonical key.";
        }

        return null;
    }

    private static void ValidateProjectedPhysicalTargetManifestForReturn(
        ConfigurationOwnershipManifest? manifest
    )
    {
        if (manifest is null)
        {
            return;
        }

        string? gitConfigPathViolation = GetGitConfigPhysicalTargetEntriesPathViolation(
            manifest.Entries.Where(entry => entry.TargetKind == ConfigurationTargetKind.GitConfig),
            LooksLikeRelativePhysicalPath
        );
        if (gitConfigPathViolation is not null)
        {
            throw new InvalidOperationException(gitConfigPathViolation);
        }

        ValidateGitConfigManifestEntriesAreVerifiableNonSecretValueWrites(
            manifest.Entries.Where(entry => IsValueWritingOperation(entry.Operation))
        );
        ValidateGitConfigUseHttpPathManifestEntriesRetainCanonicalTrue(
            manifest.Entries.Where(entry => IsValueWritingOperation(entry.Operation))
        );
        ValidatePythonKeyringManifestEntriesAreVerifiableNonSecretValueWrites(
            manifest.Entries.Where(entry => IsValueWritingOperation(entry.Operation))
        );
        ValidateNuGetPluginLayoutManifestEntriesAreVerifiableNonSecretValueWrites(
            manifest.Entries.Where(entry => IsValueWritingOperation(entry.Operation))
        );
        string? npmrcPathViolation = GetNpmrcPhysicalTargetEntriesPathViolation(
            manifest.Entries.Where(entry =>
                entry.TargetKind is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc
            ),
            Path.IsPathFullyQualified
        );
        if (npmrcPathViolation is not null)
        {
            throw new InvalidOperationException(npmrcPathViolation);
        }
    }

    private static string? GetNpmrcPhysicalTargetEntriesPathViolation(
        IEnumerable<ConfigurationOwnershipManifestEntry> entries,
        Func<string, bool> isPathFullyQualified
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(isPathFullyQualified);

        return entries.Any(entry => !isPathFullyQualified(entry.TargetPathOrName))
            ? "Configuration ownership manifest conflict: Npmrc/Yarnrc physical target entries "
                + "must use fully qualified target paths."
            : null;
    }

    private static string? GetGitConfigPhysicalTargetEntriesPathViolation(
        IEnumerable<ConfigurationOwnershipManifestEntry> entries,
        Func<string, bool> looksLikeRelativePhysicalPath
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(looksLikeRelativePhysicalPath);

        return entries.Any(entry => looksLikeRelativePhysicalPath(entry.TargetPathOrName))
            ? "Configuration ownership manifest conflict: Git config physical target entries "
                + "must use fully qualified target paths."
            : null;
    }

    private static bool LooksLikeRelativePhysicalPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (Path.IsPathFullyQualified(path))
        {
            return false;
        }

        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return true;
        }

        return path is "." or ".." || path.Contains('/') || path.Contains('\\');
    }

    private static string? GetNpmrcRetainedOwnershipProofViolation(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest? manifest
    )
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (manifest is null)
        {
            return null;
        }

        return manifest
            .Entries.Where(entry =>
                entry.TargetKind is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc
            )
            .Select(entry =>
                CreateEntryKey(
                    entry.TargetKind,
                    CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName),
                    CanonicalizePhysicalTargetManifestKey(entry.TargetKind, entry.Key)
                )
            )
            .GroupBy(key => key, GetEntryMergeKeyComparer())
            .Any(group => group.Count() > 1)
            ? "Configuration ownership manifest conflict: Npmrc/Yarnrc retained ownership proofs "
                + "must be unique per canonical physical key."
            : null;
    }

    private static ConfigurationOwnershipManifest CanonicalizePhysicalTargetManifestForWrite(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest manifest
    )
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(manifest);

        ConfigurationOwnershipManifest canonicalManifest = manifest with
        {
            Entries = manifest
                .Entries.Select((entry, index) =>
                    CanonicalizePhysicalTargetManifestEntryForWrite(fileSystem, entry) with
                    {
                        Sequence = index + 1,
                    }
                )
                .ToArray(),
        };
        ConfigurationOwnershipManifestPolicy.EnsureValid(canonicalManifest);
        ValidatePhysicalTargetManifestEntries(fileSystem, canonicalManifest);
        return canonicalManifest;
    }

    private static ConfigurationOwnershipManifestEntry
        CanonicalizePhysicalTargetManifestEntryForWrite(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifestEntry entry
    )
    {
        if (
            entry.TargetKind != ConfigurationTargetKind.GitConfig
            && entry.TargetKind != ConfigurationTargetKind.Npmrc
            && entry.TargetKind != ConfigurationTargetKind.Yarnrc
        )
        {
            return entry;
        }

        ConfigurationOwnershipManifestEntry canonicalizedEntry = entry with
        {
            TargetPathOrName = CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName),
        };

        return entry.TargetKind == ConfigurationTargetKind.GitConfig
            ? canonicalizedEntry with
            {
                Key = GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(
                    entry.Key
                ),
            }
            : canonicalizedEntry;
    }

    private ConfigurationOwnershipManifest? Execute(
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        EnsureNoConfigurationExecutionAlreadyInProgress();
        ExecutionLock.Wait(cancellationToken);
        ExecutionLockHeldByCurrentAsyncFlow.Value = true;
        try
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
            ValidateGitConfigRetainedUseHttpPathOwnershipProofs(
                executionFileSystem,
                manifestSnapshot.Existed
                    ? ConfigurationOwnershipManifestSerializer.Deserialize(
                        manifestSnapshot.Contents!
                    )
                    : null,
                cancellationToken
            );
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
            bool finalManifestMayExistForRollbackHandling = false;
            string? finalManifestSha256HashForRollbackHandling = null;
            FileRollbackSnapshot? preFinalManifestSnapshotForRollbackHandling = null;
            ConfigurationOwnershipManifest? finalManifestForRollbackHandling = null;
            bool finalManifestRetainedProofValidationFailedForRollbackHandling = false;
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
                    ConfigurationOwnershipManifest existingManifest =
                        ConfigurationOwnershipManifestSerializer.Deserialize(
                            manifestSnapshot.Contents!
                        );
                    ConfigurationOwnershipManifest? remainingManifestBeforeCommit =
                        CreateRemainingManifestAfterRemove(
                            executionFileSystem,
                            existingManifest,
                            ownershipManifest,
                            plan
                        );
                    string? remainingManifestContents = null;
                    ValidateGitConfigRetainedUseHttpPathOwnershipProofsAfterGenericMutation(
                        executionFileSystem,
                        remainingManifestBeforeCommit,
                        cancellationToken
                    );
                    if (
                        CreatePhysicalTargetOwnershipProofs(remainingManifestBeforeCommit)
                            .Length > 0
                    )
                    {
                        remainingManifestContents =
                            ConfigurationOwnershipManifestSerializer.Serialize(
                                remainingManifestBeforeCommit!
                            );
                        finalManifestSha256HashForRollbackHandling =
                            ComputeSha256(remainingManifestContents);
                        preFinalManifestSnapshotForRollbackHandling = manifestSnapshot;
                        finalManifestForRollbackHandling = remainingManifestBeforeCommit;
                    }

                    ConfigurationOwnershipManifest? remainingManifest = CommitManifestRemove(
                        executionFileSystem,
                        manifestPath,
                        manifestSnapshot,
                        remainingManifestBeforeCommit,
                        remainingManifestContents,
                        completedWrites,
                        ref finalManifestMayExistForRollbackHandling,
                        ref finalManifestSha256HashForRollbackHandling
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

                    try
                    {
                        ValidateGitConfigRetainedUseHttpPathOwnershipProofsAfterGenericMutation(
                            executionFileSystem,
                            remainingManifest,
                            cancellationToken
                        );
                    }
                    catch (Exception retainedProofValidationException)
                        when (retainedProofValidationException is not OperationCanceledException)
                    {
                        finalManifestRetainedProofValidationFailedForRollbackHandling = true;
                        throw;
                    }

                    finalManifestMayExistForRollbackHandling = false;
                    finalManifestSha256HashForRollbackHandling = null;
                    preFinalManifestSnapshotForRollbackHandling = null;
                    finalManifestForRollbackHandling = null;
                    finalManifestRetainedProofValidationFailedForRollbackHandling = false;
                    VerifyCurrentGenericManifestMatchesPreparedFinalState(
                        executionFileSystem,
                        manifestPath,
                        remainingManifest
                    );
                    return remainingManifest;
                }
                else
                {
                    ConfigurationOwnershipManifest mergedOwnershipManifest =
                        mergedOwnershipManifestForApply
                        ?? throw new InvalidOperationException(
                            "Configuration apply execution requires a precomputed merged manifest."
                        );
                    ValidateGitConfigRetainedUseHttpPathOwnershipProofsAfterGenericMutation(
                        executionFileSystem,
                        mergedOwnershipManifest,
                        cancellationToken
                    );
                    string manifestContents = ConfigurationOwnershipManifestSerializer.Serialize(
                        mergedOwnershipManifest
                    );
                    string manifestContentsSha256Hash = ComputeSha256(manifestContents);
                    if (CreatePhysicalTargetOwnershipProofs(mergedOwnershipManifest).Length > 0)
                    {
                        finalManifestSha256HashForRollbackHandling =
                            manifestContentsSha256Hash;
                        preFinalManifestSnapshotForRollbackHandling = manifestSnapshot;
                        finalManifestForRollbackHandling = mergedOwnershipManifest;
                    }

                    ExecuteAtomicWriteWithRollbackRegistration(
                        executionFileSystem,
                        manifestPath,
                        manifestContents,
                        options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
                        snapshot: manifestSnapshot,
                        expectedCurrentHashForRollback: manifestContentsSha256Hash,
                        completedWrites: completedWrites,
                        unsafeFinalManifestMayExistForRollbackDeletion:
                            ref finalManifestMayExistForRollbackHandling,
                        unsafeFinalManifestSha256HashForRollbackDeletion:
                            ref finalManifestSha256HashForRollbackHandling
                    );
                    try
                    {
                        ValidateGitConfigRetainedUseHttpPathOwnershipProofsAfterGenericMutation(
                            executionFileSystem,
                            mergedOwnershipManifest,
                            cancellationToken
                        );
                    }
                    catch (Exception retainedProofValidationException)
                        when (retainedProofValidationException is not OperationCanceledException)
                    {
                        finalManifestRetainedProofValidationFailedForRollbackHandling = true;
                        throw;
                    }

                    finalManifestMayExistForRollbackHandling = false;
                    finalManifestSha256HashForRollbackHandling = null;
                    preFinalManifestSnapshotForRollbackHandling = null;
                    finalManifestForRollbackHandling = null;
                    finalManifestRetainedProofValidationFailedForRollbackHandling = false;
                    VerifyCurrentGenericManifestMatchesPreparedFinalState(
                        executionFileSystem,
                        manifestPath,
                        mergedOwnershipManifest
                    );
                    return mergedOwnershipManifest;
                }
            }

            catch (Exception exception)
            {
                if (
                    finalManifestMayExistForRollbackHandling
                    && !string.IsNullOrWhiteSpace(finalManifestSha256HashForRollbackHandling)
                    && preFinalManifestSnapshotForRollbackHandling is not null
                )
                {
                    RollBackGenericMutationWithFinalManifestHandling(
                        executionFileSystem,
                        manifestPath,
                        plan,
                        completedWrites,
                        finalManifestForRollbackHandling,
                        preFinalManifestSnapshotForRollbackHandling,
                        finalManifestSha256HashForRollbackHandling,
                        finalManifestRetainedProofValidationFailedForRollbackHandling,
                        exception
                    );
                }
                else
                {
                    RollBackWithoutMaskingConflict(
                        executionFileSystem,
                        completedWrites,
                        exception
                    );
                }

                DeleteTemporaryContainerAfterRollback(executionFileSystem, plan, containerSnapshot);
                throw;
            }
        }
        finally
        {
            ExecutionLockHeldByCurrentAsyncFlow.Value = false;
            ExecutionLock.Release();
        }
    }

    private static void EnsureNoConfigurationExecutionAlreadyInProgress()
    {
        if (ExecutionLockHeldByCurrentAsyncFlow.Value)
        {
            throw new InvalidOperationException(
                "Configuration apply/remove execution is already in progress and does not allow "
                    + "reentrant execution."
            );
        }
    }

    private ConfigurationPlanResult SimulateFilesystemBackedDryRun(
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken
    )
    {
        bool failFastOnLockedExecution = IsProjectionOnlyPhysicalTargetPlan(plan);
        if (failFastOnLockedExecution)
        {
            EnsureNoConfigurationExecutionAlreadyInProgress();
            if (!ExecutionLock.Wait(0, cancellationToken))
            {
                throw new InvalidOperationException(
                    "Configuration dry-run execution is already in progress; filesystem-backed "
                        + "4D physical target dry-run does not allow concurrent or reentrant "
                        + "execution."
                );
            }
        }
        else
        {
            EnsureNoConfigurationExecutionAlreadyInProgress();
            ExecutionLock.Wait(cancellationToken);
        }

        bool executionLockHeldByCurrentAsyncFlow =
            ExecutionLockHeldByCurrentAsyncFlow.Value;
        ExecutionLockHeldByCurrentAsyncFlow.Value = true;
        try
        {
            IFileSystem dryRunFileSystem = fileSystem!;
            string manifestPath = ownershipManifestPath!;
            if (IsProjectionOnlyPhysicalTargetPlan(plan))
            {
                EnsurePhysicalTargetDryRunDispatchPlanShapeSupported(fileSystem, plan);
                EnsureConditionalFileMutationsSupported(dryRunFileSystem);
                return SimulateProjectionOnlyPhysicalTargetDryRun(
                    dryRunFileSystem,
                    manifestPath,
                    plannedResult,
                    plan,
                    cancellationToken
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

            if (hasValueWritingChanges || !hasRemoveChanges)
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
            string? npmrcRetainedOwnershipProofViolation =
                GetNpmrcRetainedOwnershipProofViolation(dryRunFileSystem, baseManifest);
            if (npmrcRetainedOwnershipProofViolation is not null)
            {
                throw new InvalidOperationException(npmrcRetainedOwnershipProofViolation);
            }
            ValidateGitConfigRetainedUseHttpPathOwnershipProofs(
                dryRunFileSystem,
                baseManifest,
                cancellationToken
            );
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
        finally
        {
            ExecutionLockHeldByCurrentAsyncFlow.Value =
                executionLockHeldByCurrentAsyncFlow;
            ExecutionLock.Release();
        }
    }

    private ConfigurationPlanResult SimulateProjectionOnlyPhysicalTargetDryRun(
        IFileSystem dryRunFileSystem,
        string manifestPath,
        ConfigurationPlanResult plannedResult,
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken
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
        string? npmrcRetainedOwnershipProofViolation =
            GetNpmrcRetainedOwnershipProofViolation(dryRunFileSystem, baseManifest);
        if (npmrcRetainedOwnershipProofViolation is not null)
        {
            throw new InvalidOperationException(npmrcRetainedOwnershipProofViolation);
        }
        ValidateProjectionOnlyPhysicalTargetDryRunPhysicalState(
            dryRunFileSystem,
            plan,
            hasRemoveChanges
                ? ConfigurationPlanOperation.Remove
                : ConfigurationPlanOperation.Apply,
            baseManifest,
            cancellationToken
        );
        ConfigurationPhysicalTargetOwnershipProof[] nonActiveRetainedOwnershipProofs =
            CreatePhysicalTargetOwnershipProofs(baseManifest)
                .Where(proof => proof.TargetKind != plan.Changes[0].TargetKind)
                .ToArray();
        if (nonActiveRetainedOwnershipProofs.Length > 0)
        {
            ValidateRetainedOwnershipProofsBeforePhysicalTargetManifestCommit(
                nonActiveRetainedOwnershipProofs,
                cancellationToken
            );
        }
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

    private void ValidateProjectionOnlyPhysicalTargetDryRunPhysicalState(
        IFileSystem dryRunFileSystem,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        ConfigurationOwnershipManifest? existingManifest,
        CancellationToken cancellationToken
    )
    {
        if (!plan.Changes.All(change => IsSupportedProjectionOnlyPhysicalTarget(change.TargetKind)))
        {
            return;
        }

        EnsureGitConfigPhysicalWriterPreclaimValidationSupported(plan);
        ConfigurationPhysicalTargetWriterDispatcher? fallbackDispatcher = null;
        IConfigurationPhysicalTargetWriterDispatcherValidator validator =
            physicalTargetWriterDispatcher as IConfigurationPhysicalTargetWriterDispatcherValidator
            ?? (fallbackDispatcher ??= new ConfigurationPhysicalTargetWriterDispatcher(
                dryRunFileSystem
            ));
        validator
            .Validate(
                new ConfigurationPhysicalTargetWriterRequest(
                    operation,
                    plan.Changes[0].TargetKind,
                    plan.Changes,
                    CreatePhysicalTargetOwnershipProofs(existingManifest)
                )
                {
                    ResourceIdentity = plan.Manifest.ResourceIdentity,
                },
                cancellationToken
            );
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

        string lockScopeRoot = fileSystem.GetFullPath(
            plan.TemporaryContainer?.ProductOwnedPath ?? manifestPath
        );
        string lockDirectory = CreateConfigurationExecutionLockDirectory(
            fileSystem,
            lockScopeRoot,
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
        ConfigurationOwnershipManifest? remainingManifest,
        string? remainingManifestContents,
        Stack<FileRollbackSnapshot> completedWrites,
        ref bool unsafeFinalManifestMayExistForRollbackDeletion,
        ref string? unsafeFinalManifestSha256HashForRollbackDeletion
    )
    {
        if (remainingManifest is null)
        {
            ExecuteDeleteWithRollbackRegistration(
                fileSystem,
                manifestPath,
                manifestSnapshot,
                expectedCurrentHashForRollback: null,
                completedWrites
            );
            unsafeFinalManifestMayExistForRollbackDeletion = false;
            unsafeFinalManifestSha256HashForRollbackDeletion = null;
            return null;
        }

        remainingManifestContents ??= ConfigurationOwnershipManifestSerializer.Serialize(
            remainingManifest
        );
        string remainingManifestContentsSha256Hash = ComputeSha256(remainingManifestContents);
        ExecuteAtomicWriteWithRollbackRegistration(
            fileSystem,
            manifestPath,
            remainingManifestContents,
            options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
            snapshot: manifestSnapshot,
            expectedCurrentHashForRollback: remainingManifestContentsSha256Hash,
            completedWrites: completedWrites,
            unsafeFinalManifestMayExistForRollbackDeletion:
                ref unsafeFinalManifestMayExistForRollbackDeletion,
            unsafeFinalManifestSha256HashForRollbackDeletion:
                ref unsafeFinalManifestSha256HashForRollbackDeletion
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
            valueOnlyManifest = CanonicalizePhysicalTargetManifestForWrite(
                fileSystem,
                valueOnlyManifest
            );
            ValidateMergedManifestForApply(fileSystem, plan, valueOnlyManifest);
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
            string key = CreateEntryMergeKey(fileSystem, plan, existingEntry);
            if (replacements.TryGetValue(key, out ConfigurationOwnershipManifestEntry? replacement))
            {
                mergedEntries.Add(replacement);
                replacedKeys.Add(key);
                continue;
            }

            if (!PlanTargetsEntry(fileSystem, plan, existingEntry))
            {
                mergedEntries.Add(existingEntry);
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
        mergedManifest = CanonicalizePhysicalTargetManifestForWrite(fileSystem, mergedManifest);
        ValidateMergedManifestForApply(fileSystem, plan, mergedManifest);
        return mergedManifest;
    }

    private static void ValidateMergedManifestForApply(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest manifest
    )
    {
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        ValidatePhysicalTargetManifestEntries(fileSystem, manifest);
        ValidateCiTemporaryFileManifestWholeFileOwnership(fileSystem, plan, manifest);
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
            ConfigurationOwnershipManifest canonicalProjectedManifest =
                CanonicalizePhysicalTargetManifestForWrite(fileSystem, projectedManifest);
            ValidateMergedManifestForProjectionOnlyDryRun(
                fileSystem,
                plan,
                canonicalProjectedManifest
            );
            return canonicalProjectedManifest;
        }

        var replacements = projectedManifest.Entries.ToDictionary(
            entry => CreateEntryMergeKey(fileSystem, plan, entry),
            GetEntryMergeKeyComparer()
        );
        var replacedKeys = new HashSet<string>(GetEntryMergeKeyComparer());
        var mergedEntries = new List<ConfigurationOwnershipManifestEntry>();

        foreach (ConfigurationOwnershipManifestEntry existingEntry in existingManifest.Entries)
        {
            string key = CreateEntryMergeKey(fileSystem, plan, existingEntry);
            if (replacements.TryGetValue(key, out ConfigurationOwnershipManifestEntry? replacement))
            {
                mergedEntries.Add(replacement);
                replacedKeys.Add(key);
                continue;
            }

            if (!PlanTargetsEntry(fileSystem, plan, existingEntry))
            {
                mergedEntries.Add(existingEntry);
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
        mergedManifest = CanonicalizePhysicalTargetManifestForWrite(fileSystem, mergedManifest);
        ValidateMergedManifestForProjectionOnlyDryRun(fileSystem, plan, mergedManifest);
        return mergedManifest;
    }

    private static void ValidateMergedManifestForProjectionOnlyDryRun(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest manifest
    )
    {
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        ValidatePhysicalTargetManifestEntries(fileSystem, manifest);
        ValidateCiTemporaryFileManifestWholeFileOwnership(fileSystem, plan, manifest);
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
        remainingManifest =
            CanonicalizePhysicalTargetManifestForWrite(fileSystem, remainingManifest);
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

        bool comparePhysicalTargetPath = HasCollisionCheckedPhysicalTargetPath(change.TargetKind);
        string changeTargetPathOrName = comparePhysicalTargetPath
            ? CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName)
            : change.TargetPathOrName;
        string entryTargetPathOrName = comparePhysicalTargetPath
            ? CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName)
            : entry.TargetPathOrName;
        StringComparison targetPathComparison = comparePhysicalTargetPath
            ? GetPathIdentityComparison()
            : StringComparison.Ordinal;
        return string.Equals(changeTargetPathOrName, entryTargetPathOrName, targetPathComparison)
            && string.Equals(
                CanonicalizePhysicalTargetManifestKey(change.TargetKind, change.Key),
                CanonicalizePhysicalTargetManifestKey(entry.TargetKind, entry.Key),
                StringComparison.Ordinal
            );
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

        string mergeTargetIdentity = HasCollisionCheckedPhysicalTargetPath(entry.TargetKind)
            ? CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName)
            : entry.TargetPathOrName;
        return CreateEntryKey(
            entry.TargetKind,
            mergeTargetIdentity,
            CanonicalizePhysicalTargetManifestKey(entry.TargetKind, entry.Key)
        );
    }

    private static EntryMergeKeyComparer GetEntryMergeKeyComparer() =>
        EntryMergeKeyComparer.Instance;

    private static string CreateEntryKey(
        ConfigurationTargetKind targetKind,
        string targetPathOrName,
        string key
    ) => $"{targetKind}\n{targetPathOrName}\n{key}";

    private static string CanonicalizePhysicalTargetManifestKey(
        ConfigurationTargetKind targetKind,
        string key
    ) =>
        targetKind == ConfigurationTargetKind.GitConfig
            ? GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(key)
            : key;

    private sealed class EntryMergeKeyComparer : IEqualityComparer<string>
    {
        public static readonly EntryMergeKeyComparer Instance = new();

        private EntryMergeKeyComparer() { }

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

            EntryMergeKeyParts xParts = ParseEntryMergeKey(x);
            EntryMergeKeyParts yParts = ParseEntryMergeKey(y);
            return string.Equals(xParts.TargetKind, yParts.TargetKind, StringComparison.Ordinal)
                && EntryMergeKeyPathsEqual(xParts, yParts)
                && string.Equals(xParts.Key, yParts.Key, StringComparison.Ordinal);
        }

        public int GetHashCode(string obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            EntryMergeKeyParts parts = ParseEntryMergeKey(obj);
            var hashCode = new HashCode();
            hashCode.Add(parts.TargetKind, StringComparer.Ordinal);
            hashCode.Add(
                parts.TargetPathOrName,
                EntryMergeKeyUsesPhysicalPathComparison(parts.TargetKind)
                    ? GetPathIdentityComparer()
                    : StringComparer.Ordinal
            );
            hashCode.Add(parts.Key is null);
            if (parts.Key is not null)
            {
                hashCode.Add(parts.Key, StringComparer.Ordinal);
            }

            return hashCode.ToHashCode();
        }
    }

    private readonly record struct EntryMergeKeyParts(
        string TargetKind,
        string TargetPathOrName,
        string? Key
    );

    private static EntryMergeKeyParts ParseEntryMergeKey(string mergeKey)
    {
        int firstSeparatorIndex = mergeKey.IndexOf('\n', StringComparison.Ordinal);
        if (firstSeparatorIndex < 0)
        {
            return new EntryMergeKeyParts(mergeKey, string.Empty, null);
        }

        int secondSeparatorIndex = mergeKey.IndexOf('\n', firstSeparatorIndex + 1);
        if (secondSeparatorIndex < 0)
        {
            return new EntryMergeKeyParts(
                mergeKey[..firstSeparatorIndex],
                mergeKey[(firstSeparatorIndex + 1)..],
                null
            );
        }

        return new EntryMergeKeyParts(
            mergeKey[..firstSeparatorIndex],
            mergeKey[(firstSeparatorIndex + 1)..secondSeparatorIndex],
            mergeKey[(secondSeparatorIndex + 1)..]
        );
    }

    private static bool EntryMergeKeyPathsEqual(
        EntryMergeKeyParts xParts,
        EntryMergeKeyParts yParts
    )
    {
        StringComparison comparison =
            EntryMergeKeyUsesPhysicalPathComparison(xParts.TargetKind)
            || EntryMergeKeyUsesPhysicalPathComparison(yParts.TargetKind)
                ? GetPathIdentityComparison()
                : StringComparison.Ordinal;
        return string.Equals(xParts.TargetPathOrName, yParts.TargetPathOrName, comparison);
    }

    private static bool EntryMergeKeyUsesPhysicalPathComparison(string targetKind) =>
        Enum.TryParse(targetKind, out ConfigurationTargetKind parsedTargetKind)
        && HasCollisionCheckedPhysicalTargetPath(parsedTargetKind);

    private static void ValidateCiTemporaryFileManifestWholeFileOwnership(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest manifest
    )
    {
        if (
            manifest.Entries.Any(entry =>
                entry.TargetKind == ConfigurationTargetKind.CiTemporaryFile
                && !fileSystem.IsPathFullyQualified(entry.TargetPathOrName)
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: CI temporary file entries must use "
                    + "fully qualified target paths."
            );
        }

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

    private static void EnsurePhysicalFileParentChainIsUsable(
        IFileSystem fileSystem,
        string path,
        string entryKind
    )
    {
        string fullPath = fileSystem.GetFullPath(path);
        string? parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent))
        {
            parent = Directory.GetCurrentDirectory();
        }

        foreach (string directory in EnumerateDirectoryChain(parent))
        {
            try
            {
                if (IsUnsupportedLinkOrReparsePoint(fileSystem, directory))
                {
                    throw new NotSupportedException(
                        "Filesystem-backed configuration execution rejects symbolic-link or "
                            + $"reparse-point directories in {entryKind} parent paths."
                    );
                }

                if (!fileSystem.DirectoryExists(directory) && fileSystem.FileExists(directory))
                {
                    throw new NotSupportedException(
                        "Filesystem-backed configuration execution rejects non-directory entries "
                            + $"in {entryKind} parent paths."
                    );
                }
            }
            catch (FileNotFoundException)
            {
                // Missing parent directories are tolerated for final-state reads and rollback
                // expectation checks. The subsequent conditional mutation or snapshot read fails
                // closed if the path cannot be proven to match the expected state.
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

    private static void RegisterPhysicalTargetFileMutationForRollback(
        string normalizedMutationPath,
        ConfigurationPhysicalTargetFileMutation mutation,
        Stack<FileRollbackSnapshot> completedWrites
    )
    {
        if (!CompletedFileMutationRequiresRollbackRegistration(mutation))
        {
            return;
        }

        if (RollbackSnapshotsContainPath(completedWrites, normalizedMutationPath))
        {
            return;
        }

        if (mutation.PreviouslyExisted != (mutation.PreviousContentsBytes is not null))
        {
            throw new InvalidOperationException(
                "Configuration physical target writer reported an invalid completed file "
                    + "mutation."
            );
        }

        RegisterRollbackSnapshot(
            completedWrites,
            new FileRollbackSnapshot(
                normalizedMutationPath,
                mutation.PreviouslyExisted
                    ? FileRollbackSnapshotEntryKind.RegularFile
                    : FileRollbackSnapshotEntryKind.Missing,
                mutation.PreviousContentsBytes is null
                    ? null
                    : DecodeUtf8TextWithoutLeadingBom(mutation.PreviousContentsBytes),
                mutation.PreviousContentsBytes,
                mutation.PreviousContentsBytes is null
                    ? null
                    : ComputeSha256(mutation.PreviousContentsBytes),
                UnixFileMode: mutation.PreviousUnixFileMode
            ),
            mutation.ExpectedCurrentSha256Hash
        );
    }

    private static bool IsProvenNoRollbackCompletedFileMutationObservation(
        ConfigurationPhysicalTargetFileMutation mutation
    ) =>
        mutation.PreviouslyExisted
        && mutation.PreviousContentsBytes is not null
        && !string.IsNullOrWhiteSpace(mutation.ExpectedCurrentSha256Hash)
        && string.Equals(
            mutation.ExpectedCurrentSha256Hash,
            ComputeSha256(mutation.PreviousContentsBytes),
            StringComparison.Ordinal
        );

    private static bool CompletedFileMutationRequiresRollbackRegistration(
        ConfigurationPhysicalTargetFileMutation mutation
    ) =>
        mutation.RequiresRollback
        || !IsProvenNoRollbackCompletedFileMutationObservation(mutation);

    private static bool RollbackSnapshotsContainPath(
        IEnumerable<FileRollbackSnapshot> snapshots,
        string path
    ) =>
        snapshots.Any(snapshot =>
            string.Equals(snapshot.Path, path, GetPathIdentityComparison())
        );

    private static void ValidateAndRegisterCompletedPhysicalTargetFileMutations(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        IEnumerable<ConfigurationPhysicalTargetFileMutation> mutations,
        Stack<FileRollbackSnapshot> completedWrites
    )
    {
        ConfigurationPhysicalTargetFileMutation[] reportedMutations = mutations.ToArray();
        string[] expectedGitConfigTargetPaths = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.GitConfig)
            .Select(change => CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName))
            .Distinct(GetPathIdentityComparer())
            .ToArray();
        string[] expectedNpmrcTargetPaths = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.Npmrc)
            .Select(change => CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName))
            .Distinct(ConfigurationPathIdentityComparer.Instance)
            .ToArray();
        string[] expectedYarnrcTargetPaths = plan
            .Changes.Where(change => change.TargetKind == ConfigurationTargetKind.Yarnrc)
            .Select(change => CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName))
            .Distinct(ConfigurationPathIdentityComparer.Instance)
            .ToArray();
        string[] expectedNuGetPluginLayoutTargetRootPaths = plan
            .Changes.Where(change =>
                change.TargetKind == ConfigurationTargetKind.NuGetPluginLayout
            )
            .Select(change => CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName))
            .Distinct(GetPathIdentityComparer())
            .ToArray();
        string[] expectedPythonKeyringTargetPaths = plan
            .Changes.Where(change =>
                change.TargetKind is ConfigurationTargetKind.PythonKeyringBackend
                    or ConfigurationTargetKind.KeyringShim
            )
            .Select(change => CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName))
            .Distinct(GetPathIdentityComparer())
            .ToArray();
        var expectedGitConfigTargetPathSet = new HashSet<string>(
            expectedGitConfigTargetPaths,
            GetPathIdentityComparer()
        );
        var expectedNpmrcTargetPathSet = new HashSet<string>(
            expectedNpmrcTargetPaths,
            ConfigurationPathIdentityComparer.Instance
        );
        var expectedYarnrcTargetPathSet = new HashSet<string>(
            expectedYarnrcTargetPaths,
            ConfigurationPathIdentityComparer.Instance
        );
        var expectedNuGetPluginLayoutTargetRootPathSet = new HashSet<string>(
            expectedNuGetPluginLayoutTargetRootPaths,
            GetPathIdentityComparer()
        );
        var expectedPythonKeyringTargetPathSet = new HashSet<string>(
            expectedPythonKeyringTargetPaths,
            GetPathIdentityComparer()
        );
        var mutationsByNormalizedPath =
            new Dictionary<string, List<ConfigurationPhysicalTargetFileMutation>>(
                GetPathIdentityComparer()
            );
        var pathsWithPreviousSnapshotInconsistency = new HashSet<string>(
            GetPathIdentityComparer()
        );
        var mutationsWithPreviousSnapshotInconsistency =
            new HashSet<ConfigurationPhysicalTargetFileMutation>();
        InvalidOperationException? reportedMutationException = null;

        foreach (ConfigurationPhysicalTargetFileMutation mutation in reportedMutations)
        {
            if (string.IsNullOrWhiteSpace(mutation.Path))
            {
                reportedMutationException ??= new InvalidOperationException(
                    "Configuration physical target writer reported a completed file mutation "
                        + "with an empty path."
                );
                continue;
            }

            string normalizedMutationPath;
            try
            {
                normalizedMutationPath = CreatePhysicalPathIdentity(fileSystem, mutation.Path);
            }
            catch (Exception exception)
                when (exception is ArgumentException or NotSupportedException or IOException)
            {
                reportedMutationException ??= new InvalidOperationException(
                    "Configuration physical target writer reported a completed file mutation "
                        + "with an invalid reported path.",
                    exception
                );
                continue;
            }

            if (expectedGitConfigTargetPaths.Length > 0)
            {
                if (!expectedGitConfigTargetPathSet.Contains(normalizedMutationPath))
                {
                    reportedMutationException ??= new InvalidOperationException(
                        "Configuration physical target writer reported a completed file mutation "
                            + "for an unrelated Git config target path."
                    );
                    continue;
                }
            }

            if (expectedNpmrcTargetPaths.Length > 0)
            {
                if (!expectedNpmrcTargetPathSet.Contains(normalizedMutationPath))
                {
                    reportedMutationException ??= new InvalidOperationException(
                        "Configuration physical target writer reported a completed file mutation "
                            + "for an unrelated Npmrc target path."
                    );
                    continue;
                }
            }

            if (expectedYarnrcTargetPaths.Length > 0)
            {
                if (!expectedYarnrcTargetPathSet.Contains(normalizedMutationPath))
                {
                    reportedMutationException ??= new InvalidOperationException(
                        "Configuration physical target writer reported a completed file mutation "
                            + "for an unrelated Yarnrc target path."
                    );
                    continue;
                }
            }

            if (expectedNuGetPluginLayoutTargetRootPaths.Length > 0)
            {
                if (
                    !expectedNuGetPluginLayoutTargetRootPathSet.Any(expectedRootPath =>
                        IsPathUnderDirectory(expectedRootPath, normalizedMutationPath)
                    )
                )
                {
                    reportedMutationException ??= new InvalidOperationException(
                        "Configuration physical target writer reported a completed file mutation "
                            + "for an unrelated NuGet plugin layout target path."
                    );
                    continue;
                }
            }

            if (expectedPythonKeyringTargetPaths.Length > 0)
            {
                if (!expectedPythonKeyringTargetPathSet.Contains(normalizedMutationPath))
                {
                    reportedMutationException ??= new InvalidOperationException(
                        "Configuration physical target writer reported a completed file mutation "
                            + "for an unrelated Python keyring target path."
                    );
                    continue;
                }
            }

            if (
                !mutationsByNormalizedPath.TryGetValue(
                    normalizedMutationPath,
                    out List<ConfigurationPhysicalTargetFileMutation>? pathMutations
                )
            )
            {
                pathMutations = [];
                mutationsByNormalizedPath.Add(normalizedMutationPath, pathMutations);
            }
            else
            {
                reportedMutationException ??= new InvalidOperationException(
                    "Configuration physical target writer reported duplicate completed file "
                        + "mutations for the same Git config target path."
                );
            }

            try
            {
                ValidateCompletedPhysicalTargetFileMutationPreviousSnapshot(mutation);
            }
            catch (InvalidOperationException exception)
            {
                reportedMutationException ??= exception;
                pathsWithPreviousSnapshotInconsistency.Add(normalizedMutationPath);
                mutationsWithPreviousSnapshotInconsistency.Add(mutation);
            }

            pathMutations.Add(mutation);
        }

        foreach (
            KeyValuePair<string, List<ConfigurationPhysicalTargetFileMutation>> mutationsByPath in
                mutationsByNormalizedPath
        )
        {
            ConfigurationPhysicalTargetFileMutation[] candidateMutations = mutationsByPath
                .Value.Where(mutation =>
                    !mutationsWithPreviousSnapshotInconsistency.Contains(mutation)
                    && (
                        !pathsWithPreviousSnapshotInconsistency.Contains(mutationsByPath.Key)
                        || mutation.PreviouslyExisted
                    )
                )
                .ToArray();
            InvalidOperationException? candidateException =
                ValidateAndRegisterBestCompletedPhysicalTargetFileMutation(
                    fileSystem,
                    mutationsByPath.Key,
                    candidateMutations,
                    completedWrites
                );
            reportedMutationException ??= candidateException;
        }

        if (reportedMutationException is not null)
        {
            throw reportedMutationException;
        }

        foreach (string expectedPath in expectedGitConfigTargetPaths)
        {
            if (!mutationsByNormalizedPath.ContainsKey(expectedPath))
            {
                throw new InvalidOperationException(
                    "Configuration physical target writer did not report a completed file "
                        + "mutation or observation for every Git config target path."
                );
            }
        }

        foreach (string expectedPath in expectedNpmrcTargetPaths)
        {
            if (!mutationsByNormalizedPath.ContainsKey(expectedPath))
            {
                throw new InvalidOperationException(
                    "Configuration physical target writer did not report a completed file "
                        + "mutation or observation for every Npmrc target path."
                );
            }
        }

        foreach (string expectedPath in expectedYarnrcTargetPaths)
        {
            if (!mutationsByNormalizedPath.ContainsKey(expectedPath))
            {
                throw new InvalidOperationException(
                    "Configuration physical target writer did not report a completed file "
                        + "mutation or observation for every Yarnrc target path."
                );
            }
        }

        foreach (string expectedRootPath in expectedNuGetPluginLayoutTargetRootPaths)
        {
            if (
                !mutationsByNormalizedPath.Keys.Any(candidatePath =>
                    IsPathUnderDirectory(expectedRootPath, candidatePath)
                )
            )
            {
                throw new InvalidOperationException(
                    "Configuration physical target writer did not report a completed file "
                        + "mutation or observation for every NuGet plugin layout target path."
                );
            }
        }

        foreach (string expectedPath in expectedPythonKeyringTargetPaths)
        {
            if (!mutationsByNormalizedPath.ContainsKey(expectedPath))
            {
                throw new InvalidOperationException(
                    "Configuration physical target writer did not report a completed file "
                        + "mutation or observation for every Python keyring target path."
                );
            }
        }
    }

    private static InvalidOperationException?
        ValidateAndRegisterBestCompletedPhysicalTargetFileMutation(
            IFileSystem fileSystem,
            string normalizedMutationPath,
            IReadOnlyList<ConfigurationPhysicalTargetFileMutation> mutations,
            Stack<FileRollbackSnapshot> completedWrites
        )
    {
        InvalidOperationException? candidateException = null;

        foreach (
            ConfigurationPhysicalTargetFileMutation mutation in mutations.Where(
                CompletedFileMutationRequiresRollbackRegistration
            )
        )
        {
            if (
                TryValidateAndRegisterCompletedPhysicalTargetFileMutation(
                    fileSystem,
                    normalizedMutationPath,
                    mutation,
                    completedWrites,
                    ref candidateException
                )
            )
            {
                return null;
            }
        }

        foreach (
            ConfigurationPhysicalTargetFileMutation mutation in mutations.Where(mutation =>
                !CompletedFileMutationRequiresRollbackRegistration(mutation)
            )
        )
        {
            if (
                TryValidateAndRegisterCompletedPhysicalTargetFileMutation(
                    fileSystem,
                    normalizedMutationPath,
                    mutation,
                    completedWrites,
                    ref candidateException
                )
            )
            {
                return null;
            }
        }

        return candidateException;
    }

    private static bool TryValidateAndRegisterCompletedPhysicalTargetFileMutation(
        IFileSystem fileSystem,
        string normalizedMutationPath,
        ConfigurationPhysicalTargetFileMutation mutation,
        Stack<FileRollbackSnapshot> completedWrites,
        ref InvalidOperationException? candidateException
    )
    {
        try
        {
            ValidateCompletedPhysicalTargetFileMutation(
                fileSystem,
                normalizedMutationPath,
                mutation
            );
            RegisterPhysicalTargetFileMutationForRollback(
                normalizedMutationPath,
                mutation,
                completedWrites
            );
            return true;
        }
        catch (InvalidOperationException exception)
        {
            candidateException ??= exception;
            return false;
        }
    }

    private static void ValidateCompletedPhysicalTargetFileMutation(
        IFileSystem fileSystem,
        string normalizedMutationPath,
        ConfigurationPhysicalTargetFileMutation mutation
    )
    {
        ValidateCompletedPhysicalTargetFileMutationPreviousSnapshot(mutation);
        if (string.IsNullOrWhiteSpace(mutation.ExpectedCurrentSha256Hash))
        {
            throw new InvalidOperationException(
                "Configuration physical target writer reported a completed file mutation "
                    + "without an expected current hash."
            );
        }

        EnsurePhysicalFileParentChainIsUsable(
            fileSystem,
            normalizedMutationPath,
            "completed physical target mutation"
        );
        EnsurePathIsNotUnsupportedReparsePoint(
            fileSystem,
            normalizedMutationPath,
            "completed physical target mutation"
        );
        FileRollbackSnapshot currentSnapshot = CaptureRollbackSnapshot(
            fileSystem,
            normalizedMutationPath
        );
        ValidateFileSnapshotIsRegularFile(currentSnapshot, "completed physical target mutation");
        if (
            !currentSnapshot.Existed
            || !string.Equals(
                currentSnapshot.ContentsSha256Hash,
                mutation.ExpectedCurrentSha256Hash,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration conflict: completed physical target mutation current hash "
                    + "does not match."
            );
        }
    }

    private static void ValidateCompletedPhysicalTargetFileMutationPreviousSnapshot(
        ConfigurationPhysicalTargetFileMutation mutation
    )
    {
        if (mutation.PreviouslyExisted == (mutation.PreviousContentsBytes is not null))
        {
            return;
        }

        throw new InvalidOperationException(
            "Configuration physical target writer reported an invalid completed file mutation."
        );
    }

    private static ConfigurationPhysicalTargetOwnershipProof[] CreatePhysicalTargetOwnershipProofs(
        ConfigurationOwnershipManifest? existingManifest
    )
    {
        if (existingManifest is null)
        {
            return [];
        }

        if (ContainsPhysicalTargetManifestPreclaimMetadataKey(existingManifest))
        {
            return [];
        }

        ConfigurationOwnershipManifestPolicy.EnsureValid(existingManifest);

        return existingManifest
            .Entries.Where(entry =>
                entry.TargetKind is ConfigurationTargetKind.GitConfig
                    or ConfigurationTargetKind.Npmrc
                    or ConfigurationTargetKind.Yarnrc
                    or ConfigurationTargetKind.NuGetPluginLayout
                    or ConfigurationTargetKind.PythonKeyringBackend
                    or ConfigurationTargetKind.KeyringShim
            )
            .Select(entry => new ConfigurationPhysicalTargetOwnershipProof(
                entry.TargetKind,
                entry.TargetPathOrName,
                CanonicalizePhysicalTargetManifestKey(entry.TargetKind, entry.Key),
                entry.PlannedValueSha256
            ))
            .ToArray();
    }

    private static bool IsPhysicalTargetManifestPreclaim(
        ConfigurationOwnershipManifest manifest
    ) =>
        manifest.SafeMetadata.TryGetValue(
            PhysicalTargetManifestPreclaimMetadataKey,
            out string? state
        )
        && string.Equals(
            state,
            PhysicalTargetManifestPreclaimMetadataValue,
            StringComparison.Ordinal
        );

    private static bool ContainsPhysicalTargetManifestPreclaimMetadataKey(
        ConfigurationOwnershipManifest manifest
    ) =>
        manifest.SafeMetadata.ContainsKey(PhysicalTargetManifestPreclaimMetadataKey);

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

    private static void EnsurePhysicalTargetDispatchPlanShapeSupported(
        IFileSystem? fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        EnsurePhysicalTargetDispatchTargetShapeSupported(plan, "apply/remove");
        EnsureNuGetPluginLayoutTargetRootsAreCanonical(plan);
        EnsurePythonKeyringTargetPathsAreCanonical(plan);
        EnsurePhysicalTargetDispatchBatchShapeSupported(fileSystem, plan, "apply/remove");

        if (
            operation == ConfigurationPlanOperation.Apply
            && plan.Changes.Any(change => !IsValueWritingOperation(change.Operation))
        )
        {
            throw new NotSupportedException(
                "Configuration apply currently supports only value-writing 4D physical "
                    + "target changes."
            );
        }

        if (
            operation == ConfigurationPlanOperation.Remove
            && plan.Changes.Any(change => !IsOwnershipRemoveOperation(change.Operation))
        )
        {
            throw new NotSupportedException(
                "Configuration remove currently supports only ownership-removing 4D physical "
                    + "target changes."
            );
        }

        EnsureSupportedProjectionOnlyPhysicalTargetKinds(plan, "apply/remove");
    }

    private static void EnsurePhysicalTargetDryRunDispatchPlanShapeSupported(
        IFileSystem? fileSystem,
        ConfigurationChangePlan plan
    )
    {
        EnsurePhysicalTargetDispatchTargetShapeSupported(plan, "dry-run");
        EnsureNuGetPluginLayoutTargetRootsAreCanonical(plan);
        EnsurePythonKeyringTargetPathsAreCanonical(plan);
        EnsurePhysicalTargetDispatchBatchShapeSupported(fileSystem, plan, "dry-run");

        bool allValueWriting = plan.Changes.All(change =>
            IsValueWritingOperation(change.Operation)
        );
        bool allOwnershipRemoving = plan.Changes.All(change =>
            IsOwnershipRemoveOperation(change.Operation)
        );
        if (!allValueWriting && !allOwnershipRemoving)
        {
            throw new NotSupportedException(
                "Configuration dry-run currently supports only 4D physical target plans that "
                    + "can be executed by apply or remove."
            );
        }

        EnsureSupportedProjectionOnlyPhysicalTargetKinds(plan, "dry-run");
    }

    private static void EnsurePhysicalTargetDispatchBatchShapeSupported(
        IFileSystem? fileSystem,
        ConfigurationChangePlan plan,
        string operationDescription
    )
    {
        if (plan.Changes.Count != 1)
        {
            bool supportedGitConfigBatch =
                plan.Changes.All(change => change.TargetKind == ConfigurationTargetKind.GitConfig)
                && AllChangesTargetSameNormalizedPhysicalPath(
                    fileSystem,
                    plan.Changes,
                    GetPathIdentityComparer()
                );
            bool supportedNpmrcBatch =
                plan.Changes.All(change => change.TargetKind == ConfigurationTargetKind.Npmrc)
                && AllChangesTargetSameNormalizedPhysicalPath(
                    fileSystem,
                    plan.Changes,
                    ConfigurationPathIdentityComparer.Instance
                );
            bool supportedYarnrcBatch =
                plan.Changes.All(change => change.TargetKind == ConfigurationTargetKind.Yarnrc)
                && AllChangesTargetSameNormalizedPhysicalPath(
                    fileSystem,
                    plan.Changes,
                    ConfigurationPathIdentityComparer.Instance
                );
            if (!supportedGitConfigBatch && !supportedNpmrcBatch && !supportedYarnrcBatch)
            {
                throw new NotSupportedException(
                    $"Configuration {operationDescription} currently supports dispatching only "
                        + "one 4D physical target change per plan, except for GitConfig, Npmrc, "
                        + "or Yarnrc batches that target the same normalized physical path."
                );
            }
        }

        if (plan.Changes.All(change => change.TargetKind == ConfigurationTargetKind.GitConfig))
        {
            EnsureGitConfigGoldenSliceSupported(plan);
            EnsureNoDuplicateGitConfigPhysicalTargetKeys(plan, operationDescription);
        }
    }

    private static void EnsureNuGetPluginLayoutTargetRootsAreCanonical(ConfigurationChangePlan plan)
    {
        foreach (
            ConfigurationChange change in plan.Changes.Where(change =>
                change.TargetKind == ConfigurationTargetKind.NuGetPluginLayout
            )
        )
        {
            string? violation =
                NuGetPluginLayoutPhysicalTargetWriter.GetTargetRootPathValidationViolation(
                    change.TargetPathOrName
                );
            if (violation is not null)
            {
                throw new NotSupportedException(violation);
            }
        }
    }

    private string? GetNuGetPluginLayoutPlanningValidationViolation(
        ConfigurationChangePlan plan
    )
    {
        string? violation = GetReservedInternalPlanningPhysicalTargetPathViolation(plan);
        if (violation is not null)
        {
            return violation;
        }

        foreach (
            ConfigurationChange change in plan.Changes.Where(change =>
                change.TargetKind == ConfigurationTargetKind.NuGetPluginLayout
            )
        )
        {
            string? changeViolation =
                NuGetPluginLayoutPhysicalTargetWriter.GetPlanningValidationViolation(
                change
            );
            if (changeViolation is not null)
            {
                return changeViolation;
            }
        }

        return null;
    }

    private static string? GetPythonKeyringPlanningValidationViolation(
        ConfigurationChangePlan plan
    )
    {
        try
        {
            EnsurePythonKeyringTargetPathsAreCanonical(plan);
            return null;
        }
        catch (NotSupportedException exception)
        {
            return exception.Message;
        }
    }

    private static void EnsureGitConfigGoldenSliceSupported(ConfigurationChangePlan plan)
    {
        foreach (
            ConfigurationChange change in plan.Changes.Where(change =>
                change.TargetKind == ConfigurationTargetKind.GitConfig
            )
        )
        {
            string canonicalKey =
                GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(change.Key);
            if (
                string.Equals(
                    canonicalKey,
                    "credential.https://dev.azure.com.useHttpPath",
                    StringComparison.Ordinal
                )
                && IsValueWritingOperation(change.Operation)
                && !string.Equals(change.Value, "true", StringComparison.Ordinal)
            )
            {
                throw new NotSupportedException(
                    "GitConfig golden-slice support requires credential "
                        + "\"https://dev.azure.com\".useHttpPath to have canonical value true."
                );
            }
        }
    }

    private static void EnsureNoDuplicateGitConfigPhysicalTargetKeys(
        ConfigurationChangePlan plan,
        string operationDescription
    )
    {
        if (
            plan
                .Changes.Select(change =>
                    GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(
                        change.Key
                    )
                )
                .GroupBy(key => key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            throw new NotSupportedException(
                $"Configuration {operationDescription} does not support multiple Git config "
                    + "changes for the same canonical physical key."
            );
        }
    }

    private static bool AllChangesTargetSameNormalizedPhysicalPath(
        IFileSystem? fileSystem,
        IReadOnlyList<ConfigurationChange> changes,
        IEqualityComparer<string> pathIdentityComparer
    )
    {
        string firstPath = CreateDispatchPhysicalPathIdentity(fileSystem, changes[0]);
        return changes
            .Skip(1)
            .All(change =>
                pathIdentityComparer.Equals(
                    CreateDispatchPhysicalPathIdentity(fileSystem, change),
                    firstPath
                )
            );
    }

    private static string CreateDispatchPhysicalPathIdentity(
        IFileSystem? fileSystem,
        ConfigurationChange change
    )
    {
        if (fileSystem is null)
        {
            return CreatePlanningPhysicalPathIdentity(change);
        }

        return CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName);
    }

    private static void EnsurePhysicalTargetDispatchTargetShapeSupported(
        ConfigurationChangePlan plan,
        string operationDescription
    )
    {
        if (!plan.Changes.All(change => IsProjectionOnlyPhysicalTarget(change.TargetKind)))
        {
            throw new NotSupportedException(
                $"Configuration {operationDescription} does not support mixing 4D physical "
                    + "configuration targets with other target kinds."
            );
        }

        if (
            plan
                .Changes.Select(change => change.TargetKind)
                .Distinct()
                .Skip(1)
                .Any()
        )
        {
            throw new NotSupportedException(
                $"Configuration {operationDescription} currently supports dispatching only one "
                    + "4D physical target kind per plan."
            );
        }

    }

    private static void EnsureSupportedProjectionOnlyPhysicalTargetKinds(
        ConfigurationChangePlan plan,
        string operationDescription
    )
    {
        if (
            plan.Changes.All(change =>
                IsSupportedProjectionOnlyPhysicalTarget(change.TargetKind)
            )
        )
        {
            return;
        }

        throw new NotSupportedException(
            $"Configuration {operationDescription} has no registered writer for this 4D "
                + "physical configuration target kind. Phase 4D.2 currently supports only "
                + "GitConfig, Npmrc, NuGetPluginLayout, PythonKeyringBackend, and KeyringShim "
                + "physical targets."
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
        ConfigurationOwnershipManifestPolicy.EnsureValid(existingManifest);
        if (ContainsPhysicalTargetManifestPreclaimMetadataKey(existingManifest))
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: existing manifest contains reserved "
                    + "physical target dispatch preclaim metadata."
            );
        }

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
            nonCiPhysicalEntries.Any(entry =>
                entry.TargetKind == ConfigurationTargetKind.GitConfig
                && !fileSystem.IsPathFullyQualified(entry.TargetPathOrName)
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: Git config physical target entries "
                    + "must use fully qualified target paths."
            );
        }

        string? npmrcPathViolation = GetNpmrcPhysicalTargetEntriesPathViolation(
            nonCiPhysicalEntries.Where(entry =>
                entry.TargetKind is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc
            ),
            fileSystem.IsPathFullyQualified
        );
        if (npmrcPathViolation is not null)
        {
            throw new InvalidOperationException(npmrcPathViolation);
        }

        ValidateGitConfigManifestEntriesAreVerifiableNonSecretValueWrites(nonCiPhysicalEntries);
        ValidateGitConfigUseHttpPathManifestEntriesRetainCanonicalTrue(nonCiPhysicalEntries);
        ValidatePythonKeyringManifestEntriesAreVerifiableNonSecretValueWrites(
            nonCiPhysicalEntries
        );
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

        ValidateNuGetPluginLayoutManifestEntries(fileSystem, nonCiPhysicalEntries);
        if (
            nonCiPhysicalEntries
                .Where(entry => entry.TargetKind == ConfigurationTargetKind.GitConfig)
                .GroupBy(
                    entry =>
                        CreateEntryKey(
                            entry.TargetKind,
                            CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName),
                            CanonicalizePhysicalTargetManifestKey(entry.TargetKind, entry.Key)
                        ),
                    GetEntryMergeKeyComparer()
                )
                .Any(group => group.Count() > 1)
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: Git config entries must not contain "
                    + "multiple ownership records for the same canonical physical key."
            );
        }

        ValidateNonCiPhysicalManifestEntriesHaveRegisteredRetainedProofSupport(
            nonCiPhysicalEntries
        );
    }

    private static void ValidateNonCiPhysicalManifestEntriesHaveRegisteredRetainedProofSupport(
        IEnumerable<ConfigurationOwnershipManifestEntry> nonCiPhysicalEntries
    )
    {
        if (
            nonCiPhysicalEntries.Any(entry =>
                !HasRegisteredRetainedProofSupport(entry.TargetKind)
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: non-CI physical target entries must "
                    + "have a registered retained-proof validator and writer in this phase."
            );
        }
    }

    private static bool HasRegisteredRetainedProofSupport(
        ConfigurationTargetKind targetKind
    ) =>
        targetKind is
            ConfigurationTargetKind.GitConfig
                or ConfigurationTargetKind.Npmrc
                or ConfigurationTargetKind.Yarnrc
                or ConfigurationTargetKind.NuGetPluginLayout
                or ConfigurationTargetKind.PythonKeyringBackend
                or ConfigurationTargetKind.KeyringShim;

    private static void ValidateGitConfigManifestEntriesAreVerifiableNonSecretValueWrites(
        IEnumerable<ConfigurationOwnershipManifestEntry> entries
    )
    {
        foreach (
            ConfigurationOwnershipManifestEntry entry in entries.Where(entry =>
                entry.TargetKind == ConfigurationTargetKind.GitConfig
            )
        )
        {
            if (
                IsValueWritingOperation(entry.Operation)
                && entry.HasPlannedValue
                && !entry.IsSecretValue
                && IsLowercaseSha256Hex(entry.PlannedValueSha256)
            )
            {
                continue;
            }

            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: Git config physical target entries "
                    + "must be non-secret value-writing entries with verifiable planned value "
                    + "SHA-256 hashes."
            );
        }
    }

    private static void ValidateNuGetPluginLayoutManifestEntries(
        IFileSystem fileSystem,
        IEnumerable<ConfigurationOwnershipManifestEntry> entries
    )
    {
        foreach (
            ConfigurationOwnershipManifestEntry entry in entries.Where(entry =>
                entry.TargetKind == ConfigurationTargetKind.NuGetPluginLayout
            )
        )
        {
            if (!fileSystem.IsPathFullyQualified(entry.TargetPathOrName))
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: NuGet plugin layout "
                        + "physical target entries must use fully qualified target paths."
                );
            }

            if (!IsCanonicalNuGetPluginLayoutTargetRootPath(entry.TargetPathOrName))
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: NuGet plugin layout "
                        + "physical target entries must use the official per-user plugin "
                        + "convention root."
                );
            }

            if (
                !string.Equals(entry.Key, "physical-target", StringComparison.Ordinal)
                || !IsValueWritingOperation(entry.Operation)
                || !entry.HasPlannedValue
                || entry.IsSecretValue
                || !IsLowercaseSha256Hex(entry.PlannedValueSha256)
            )
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: NuGet plugin layout "
                        + "physical target entries must be non-secret value-writing entries "
                        + "with verifiable planned value SHA-256 hashes."
                );
            }
        }
    }

    private static void ValidateNuGetPluginLayoutManifestEntriesAreVerifiableNonSecretValueWrites(
        IEnumerable<ConfigurationOwnershipManifestEntry> entries
    )
    {
        foreach (
            ConfigurationOwnershipManifestEntry entry in entries.Where(entry =>
                entry.TargetKind == ConfigurationTargetKind.NuGetPluginLayout
            )
        )
        {
            if (!IsCanonicalNuGetPluginLayoutTargetRootPath(entry.TargetPathOrName))
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: NuGet plugin layout "
                        + "physical target entries must use the official per-user plugin "
                        + "convention root."
                );
            }

            if (
                !string.Equals(entry.Key, "physical-target", StringComparison.Ordinal)
                || !IsValueWritingOperation(entry.Operation)
                || !entry.HasPlannedValue
                || entry.IsSecretValue
                || !IsLowercaseSha256Hex(entry.PlannedValueSha256)
            )
            {
                throw new InvalidOperationException(
                    "Configuration ownership manifest conflict: NuGet plugin layout "
                        + "physical target entries must be non-secret value-writing entries "
                        + "with verifiable planned value SHA-256 hashes."
                );
            }
        }
    }

    private static void ValidateGitConfigUseHttpPathManifestEntriesRetainCanonicalTrue(
        IEnumerable<ConfigurationOwnershipManifestEntry> entries
    )
    {
        foreach (
            ConfigurationOwnershipManifestEntry entry in entries.Where(entry =>
                entry.TargetKind == ConfigurationTargetKind.GitConfig
            )
        )
        {
            string canonicalKey = CanonicalizePhysicalTargetManifestKey(
                entry.TargetKind,
                entry.Key
            );
            if (
                !string.Equals(
                    canonicalKey,
                    GitConfigDevAzureComUseHttpPathKey,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (
                string.Equals(
                    entry.PlannedValueSha256,
                    GitConfigDevAzureComUseHttpPathTrueSha256,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: retained Git config credential "
                    + "\"https://dev.azure.com\".useHttpPath entries must use canonical value "
                    + "true."
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

    private static FileRollbackSnapshot ValidateCurrentManifestBeforePhysicalTargetManifestCommit(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        FileRollbackSnapshot preparedManifestSnapshot
    )
    {
        EnsureManifestParentChainIsUsable(fileSystem, manifestPath);
        EnsurePathIsNotUnsupportedReparsePoint(
            fileSystem,
            manifestPath,
            "configuration ownership manifest"
        );
        FileRollbackSnapshot snapshot = CaptureRollbackSnapshot(fileSystem, manifestPath);
        if (!FileRollbackSnapshotsRepresentSameState(snapshot, preparedManifestSnapshot))
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: manifest changed during physical "
                    + "target dispatch."
            );
        }

        ValidateExistingManifest(fileSystem, plan, snapshot, operation);
        return snapshot;
    }

    private static FileRollbackSnapshot ValidatePreparedManifestPreclaimStillCurrent(
        IFileSystem fileSystem,
        string manifestPath,
        FileRollbackSnapshot preparedManifestSnapshot
    )
    {
        EnsureManifestParentChainIsUsable(fileSystem, manifestPath);
        EnsurePathIsNotUnsupportedReparsePoint(
            fileSystem,
            manifestPath,
            "configuration ownership manifest"
        );
        FileRollbackSnapshot snapshot = CaptureRollbackSnapshot(fileSystem, manifestPath);
        if (!FileRollbackSnapshotsRepresentSameState(snapshot, preparedManifestSnapshot))
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: manifest changed during physical "
                    + "target dispatch."
            );
        }

        return snapshot;
    }

    private static bool FileRollbackSnapshotsRepresentSameState(
        FileRollbackSnapshot currentSnapshot,
        FileRollbackSnapshot expectedSnapshot
    )
    {
        if (currentSnapshot.EntryKind != expectedSnapshot.EntryKind)
        {
            return false;
        }

        return currentSnapshot.EntryKind != FileRollbackSnapshotEntryKind.RegularFile
            || string.Equals(
                currentSnapshot.ContentsSha256Hash,
                expectedSnapshot.ContentsSha256Hash,
                StringComparison.Ordinal
            );
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

    private static void ExecuteAtomicWriteWithRollbackRegistration(
        IFileSystem fileSystem,
        string path,
        string contents,
        AtomicWriteOptions options,
        FileRollbackSnapshot snapshot,
        string? expectedCurrentHashForRollback,
        Stack<FileRollbackSnapshot> completedWrites,
        ref bool unsafeFinalManifestMayExistForRollbackDeletion,
        ref string? unsafeFinalManifestSha256HashForRollbackDeletion
    )
    {
        try
        {
            ExecuteAtomicWriteWithRollbackRegistration(
                fileSystem,
                path,
                contents,
                options,
                snapshot,
                expectedCurrentHashForRollback,
                completedWrites
            );
            unsafeFinalManifestMayExistForRollbackDeletion =
                !string.IsNullOrWhiteSpace(
                    unsafeFinalManifestSha256HashForRollbackDeletion
                );
        }
        catch (FileMutationException exception)
            when (exception.MutationMayHaveReachedDurableState)
        {
            unsafeFinalManifestMayExistForRollbackDeletion =
                !string.IsNullOrWhiteSpace(
                    unsafeFinalManifestSha256HashForRollbackDeletion
                );
            throw;
        }
        catch (FileMutationException exception)
            when (!exception.MutationMayHaveReachedDurableState)
        {
            unsafeFinalManifestMayExistForRollbackDeletion = false;
            unsafeFinalManifestSha256HashForRollbackDeletion = null;
            throw;
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException and not FileMutationException)
        {
            unsafeFinalManifestMayExistForRollbackDeletion = false;
            unsafeFinalManifestSha256HashForRollbackDeletion = null;
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
                RollBackSnapshot(fileSystem, snapshot);
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

    private static void RollBackSnapshot(IFileSystem fileSystem, FileRollbackSnapshot snapshot)
    {
        EnsurePhysicalFileParentChainIsUsable(fileSystem, snapshot.Path, "rollback target");
        EnsurePathIsNotUnsupportedReparsePoint(fileSystem, snapshot.Path, "rollback target");
        if (snapshot.Existed)
        {
            fileSystem.AtomicWriteAllBytes(
                snapshot.Path,
                snapshot.ContentsBytes!,
                options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly,
                expectation: CreateRollbackCurrentExpectation(snapshot)
            );
            if (snapshot.UnixFileMode is { } unixFileMode)
            {
                fileSystem.SetUnixFileMode(snapshot.Path, unixFileMode);
            }
            return;
        }

        if (!fileSystem.FileExists(snapshot.Path))
        {
            return;
        }

        fileSystem.DeleteFile(snapshot.Path, CreateRollbackCurrentExpectation(snapshot));
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

    private static void RollBackGenericMutationWithFinalManifestHandling(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        Stack<FileRollbackSnapshot> snapshots,
        ConfigurationOwnershipManifest? expectedFinalManifest,
        FileRollbackSnapshot preFinalManifestSnapshot,
        string? expectedFinalManifestSha256Hash,
        bool retainedProofValidationFailed,
        Exception originalException
    )
    {
        try
        {
            if (
                ShouldDeleteUnsafeFinalManifestAfterGenericRollback(
                    fileSystem,
                    manifestPath,
                    plan,
                    snapshots,
                    expectedFinalManifest,
                    preFinalManifestSnapshot,
                    retainedProofValidationFailed
                )
            )
            {
                RollBackGenericMutationAndDeleteUnsafeFinalManifest(
                    fileSystem,
                    manifestPath,
                    plan,
                    snapshots,
                    expectedFinalManifestSha256Hash
                );
            }
            else
            {
                RollBack(fileSystem, snapshots);
            }
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

    private static bool ShouldDeleteUnsafeFinalManifestAfterGenericRollback(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        IEnumerable<FileRollbackSnapshot> snapshots,
        ConfigurationOwnershipManifest? expectedFinalManifest,
        FileRollbackSnapshot preFinalManifestSnapshot,
        bool retainedProofValidationFailed
    )
    {
        if (
            expectedFinalManifest is null
            || CreatePhysicalTargetOwnershipProofs(expectedFinalManifest).Length == 0
        )
        {
            return false;
        }

        if (
            CurrentManifestMatchesValidPreExistingSnapshot(
                fileSystem,
                manifestPath,
                plan,
                preFinalManifestSnapshot
            )
        )
        {
            return false;
        }

        return retainedProofValidationFailed
            || !OwnershipManifestGitConfigProofsMatchCurrentFiles(
                fileSystem,
                expectedFinalManifest
            )
            || GenericRollbackWouldInvalidateFinalManifestEntries(
                fileSystem,
                manifestPath,
                plan,
                expectedFinalManifest,
                snapshots
            );
    }

    private static void RollBackGenericMutationAndDeleteUnsafeFinalManifest(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        Stack<FileRollbackSnapshot> snapshots,
        string? unsafeFinalManifestSha256HashForRollbackDeletion
    )
    {
        Exception? rollbackException = null;
        while (snapshots.Count > 0)
        {
            FileRollbackSnapshot snapshot = snapshots.Pop();
            if (string.Equals(snapshot.Path, manifestPath, GetPathIdentityComparison()))
            {
                continue;
            }

            try
            {
                RollBackSnapshot(fileSystem, snapshot);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                rollbackException ??= exception;
            }
        }

        try
        {
            DeleteUnsafeFinalManifestRetainingGitConfigOwnership(
                fileSystem,
                manifestPath,
                plan,
                unsafeFinalManifestSha256HashForRollbackDeletion
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            rollbackException ??= exception;
        }

        if (rollbackException is not null)
        {
            throw new InvalidOperationException(
                "Configuration rollback failed after an apply/remove error.",
                rollbackException
            );
        }
    }

    private static void DeleteUnsafeFinalManifestRetainingGitConfigOwnership(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        string? expectedUnsafeFinalManifestSha256Hash
    )
    {
        EnsureManifestParentChainIsUsable(fileSystem, manifestPath);
        EnsurePathIsNotUnsupportedReparsePoint(
            fileSystem,
            manifestPath,
            "configuration ownership manifest"
        );
        FileRollbackSnapshot snapshot = CaptureRollbackSnapshot(fileSystem, manifestPath);
        if (!snapshot.Existed)
        {
            return;
        }

        ValidateFileSnapshotIsRegularFile(snapshot, "configuration ownership manifest");
        ConfigurationOwnershipManifest currentManifest;
        try
        {
            currentManifest = ConfigurationOwnershipManifestSerializer.Deserialize(
                snapshot.Contents!
            );
        }
        catch (Exception exception)
            when (
                exception is InvalidOperationException
                    or System.Text.Json.JsonException
                    or ArgumentException
            )
        {
            return;
        }

        if (
            ContainsPhysicalTargetManifestPreclaimMetadataKey(currentManifest)
            || !ManifestIdentityMatches(currentManifest, plan)
            || CreatePhysicalTargetOwnershipProofs(currentManifest).Length == 0
        )
        {
            return;
        }

        if (
            string.IsNullOrWhiteSpace(expectedUnsafeFinalManifestSha256Hash)
            || !string.Equals(
                snapshot.ContentsSha256Hash,
                expectedUnsafeFinalManifestSha256Hash,
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "Configuration ownership manifest conflict: unsafe final manifest changed before "
                    + "rollback deletion."
            );
        }

        fileSystem.DeleteFile(manifestPath, CreateMutationExpectation(snapshot));
    }

    private static bool CurrentManifestMatchesValidPreExistingSnapshot(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        FileRollbackSnapshot preFinalManifestSnapshot
    )
    {
        if (!preFinalManifestSnapshot.Existed)
        {
            return false;
        }

        try
        {
            EnsureManifestParentChainIsUsable(fileSystem, manifestPath);
            EnsurePathIsNotUnsupportedReparsePoint(
                fileSystem,
                manifestPath,
                "configuration ownership manifest"
            );
            FileRollbackSnapshot currentSnapshot = CaptureRollbackSnapshot(
                fileSystem,
                manifestPath
            );
            if (
                !OwnershipManifestSnapshotsEquivalent(
                    currentSnapshot,
                    preFinalManifestSnapshot
                )
            )
            {
                return false;
            }

            ConfigurationOwnershipManifest preFinalManifest =
                ConfigurationOwnershipManifestSerializer.Deserialize(
                    preFinalManifestSnapshot.Contents!
                );
            ValidatePhysicalTargetManifestEntries(fileSystem, preFinalManifest);
            ValidateCiTemporaryFileManifestWholeFileOwnership(fileSystem, plan, preFinalManifest);
            return OwnershipManifestGitConfigProofsMatchCurrentFiles(
                fileSystem,
                preFinalManifest
            );
        }
        catch (Exception exception)
            when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or InvalidOperationException
                    or System.Text.Json.JsonException
                    or ArgumentException
            )
        {
            return false;
        }
    }

    private static bool OwnershipManifestSnapshotsEquivalent(
        FileRollbackSnapshot currentSnapshot,
        FileRollbackSnapshot expectedSnapshot
    )
    {
        if (FileRollbackSnapshotsRepresentSameState(currentSnapshot, expectedSnapshot))
        {
            return true;
        }

        if (!currentSnapshot.Existed || !expectedSnapshot.Existed)
        {
            return false;
        }

        ValidateFileSnapshotIsRegularFile(currentSnapshot, "configuration ownership manifest");
        ValidateFileSnapshotIsRegularFile(expectedSnapshot, "configuration ownership manifest");
        ConfigurationOwnershipManifest currentManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(currentSnapshot.Contents!);
        ConfigurationOwnershipManifest expectedManifest =
            ConfigurationOwnershipManifestSerializer.Deserialize(expectedSnapshot.Contents!);
        return string.Equals(
            ConfigurationOwnershipManifestSerializer.Serialize(currentManifest),
            ConfigurationOwnershipManifestSerializer.Serialize(expectedManifest),
            StringComparison.Ordinal
        );
    }

    private static bool OwnershipManifestGitConfigProofsMatchCurrentFiles(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifest manifest
    )
    {
        try
        {
            ConfigurationPhysicalTargetOwnershipProof[] ownershipProofs =
                CreatePhysicalTargetOwnershipProofs(manifest);
            if (ownershipProofs.Length == 0)
            {
                return true;
            }

            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
                .ValidateRetainedOwnershipProofs(ownershipProofs, CancellationToken.None);
            return true;
        }
        catch (Exception exception)
            when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or InvalidOperationException
                    or System.Text.Json.JsonException
                    or ArgumentException
            )
        {
            return false;
        }
    }

    private static bool GenericRollbackWouldInvalidateFinalManifestEntries(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest finalManifest,
        IEnumerable<FileRollbackSnapshot> snapshots
    )
    {
        foreach (FileRollbackSnapshot snapshot in snapshots)
        {
            if (string.Equals(snapshot.Path, manifestPath, GetPathIdentityComparison()))
            {
                continue;
            }

            foreach (
                ConfigurationChange change in plan.Changes.Where(change =>
                    IsGenericFileTarget(change.TargetKind)
                    && string.Equals(
                        CreateCiTemporaryFileWholeFileIdentity(
                            fileSystem,
                            plan,
                            change.TargetPathOrName
                        ),
                        Path.TrimEndingDirectorySeparator(
                            CreatePhysicalPathIdentity(fileSystem, snapshot.Path)
                        ),
                        GetPathIdentityComparison()
                    )
                )
            )
            {
                if (
                    !FinalManifestEntryMatchesRolledBackGenericSnapshot(
                        fileSystem,
                        plan,
                        finalManifest,
                        change,
                        snapshot
                    )
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool FinalManifestEntryMatchesRolledBackGenericSnapshot(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest finalManifest,
        ConfigurationChange change,
        FileRollbackSnapshot snapshot
    )
    {
        ConfigurationOwnershipManifestEntry[] matchingEntries = finalManifest
            .Entries.Where(entry => PlanChangeMatchesEntry(fileSystem, plan, change, entry))
            .ToArray();
        if (!snapshot.Existed)
        {
            return matchingEntries.Length == 0;
        }

        return matchingEntries.Length == 1
            && string.Equals(
                matchingEntries[0].PlannedValueSha256,
                snapshot.ContentsSha256Hash,
                StringComparison.Ordinal
            );
    }

    private static void RollBackPhysicalTargetDispatchWithoutMaskingConflict(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        FileRollbackSnapshot preparedManifestSnapshot,
        ConfigurationOwnershipManifest? preparedOwnershipManifest,
        Stack<FileRollbackSnapshot> snapshots,
        bool physicalTargetRollbackSafetyUnproven,
        bool finalManifestRollbackUnsafeDueToStaleRetainedProof,
        Exception originalException
    )
    {
        try
        {
            RollBackPhysicalTargetDispatch(
                fileSystem,
                manifestPath,
                plan,
                preparedManifestSnapshot,
                preparedOwnershipManifest,
                snapshots,
                physicalTargetRollbackSafetyUnproven,
                finalManifestRollbackUnsafeDueToStaleRetainedProof
            );
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

    private static void RollBackPhysicalTargetDispatch(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        FileRollbackSnapshot preparedManifestSnapshot,
        ConfigurationOwnershipManifest? preparedOwnershipManifest,
        Stack<FileRollbackSnapshot> snapshots,
        bool physicalTargetRollbackSafetyUnproven,
        bool finalManifestRollbackUnsafeDueToStaleRetainedProof
    )
    {
        Exception? rollbackException = null;
        bool physicalTargetRollbackFailedOrDeferred = physicalTargetRollbackSafetyUnproven;
        bool finalOwnershipManifestRollbackUnsafe =
            physicalTargetRollbackSafetyUnproven
            || finalManifestRollbackUnsafeDueToStaleRetainedProof;
        bool manifestMatchesPreparedSnapshot = ManifestMatchesSnapshot(
            fileSystem,
            manifestPath,
            preparedManifestSnapshot
        );
        while (snapshots.Count > 0)
        {
            FileRollbackSnapshot snapshot = snapshots.Pop();
            bool isManifestSnapshot = string.Equals(
                snapshot.Path,
                manifestPath,
                GetPathIdentityComparison()
            );
            if (!isManifestSnapshot)
            {
                if (!manifestMatchesPreparedSnapshot)
                {
                    if (
                        CurrentManifestAdoptsPreparedPhysicalTargetEntriesForPhysicalSnapshot(
                            fileSystem,
                            manifestPath,
                            plan,
                            preparedOwnershipManifest,
                            snapshot
                        )
                    )
                    {
                        physicalTargetRollbackFailedOrDeferred = true;
                        finalOwnershipManifestRollbackUnsafe = true;
                        continue;
                    }
                }
                else
                {
                    manifestMatchesPreparedSnapshot = ManifestMatchesSnapshot(
                        fileSystem,
                        manifestPath,
                        preparedManifestSnapshot
                    );
                    if (
                        !manifestMatchesPreparedSnapshot
                        && CurrentManifestAdoptsPreparedPhysicalTargetEntriesForPhysicalSnapshot(
                            fileSystem,
                            manifestPath,
                            plan,
                            preparedOwnershipManifest,
                            snapshot
                        )
                    )
                    {
                        physicalTargetRollbackFailedOrDeferred = true;
                        finalOwnershipManifestRollbackUnsafe = true;
                        continue;
                    }
                }
            }
            else
            {
                // Once final manifest rollback is unsafe, do not reinstate an older final
                // ownership manifest. Rolling the current final manifest back to the prepared
                // preclaim remains safe because preclaims are not accepted as final ownership.
                bool manifestRollbackSnapshotMayReinstateOwnership =
                    ManifestRollbackSnapshotMayReinstateOwnership(
                        snapshot,
                        preparedManifestSnapshot
                    );
                bool manifestRollbackSnapshotProofsMatchCurrentFiles =
                    !manifestRollbackSnapshotMayReinstateOwnership
                    || ManifestRollbackSnapshotGitConfigProofsMatchCurrentFiles(
                        fileSystem,
                        snapshot
                    );
                if (
                    manifestRollbackSnapshotMayReinstateOwnership
                    && (
                        finalOwnershipManifestRollbackUnsafe
                        || !manifestRollbackSnapshotProofsMatchCurrentFiles
                    )
                )
                {
                    manifestMatchesPreparedSnapshot = false;
                    continue;
                }

                if (!SnapshotMatchesExpectedCurrentForRollback(fileSystem, snapshot))
                {
                    manifestMatchesPreparedSnapshot = false;
                    continue;
                }
            }

            try
            {
                RollBackSnapshot(fileSystem, snapshot);
                if (isManifestSnapshot)
                {
                    manifestMatchesPreparedSnapshot = ManifestMatchesSnapshot(
                        fileSystem,
                        manifestPath,
                        preparedManifestSnapshot
                    );
                }
            }

            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                rollbackException ??= exception;
                if (isManifestSnapshot)
                {
                    manifestMatchesPreparedSnapshot = false;
                }
                else
                {
                    physicalTargetRollbackFailedOrDeferred = true;
                    finalOwnershipManifestRollbackUnsafe = true;
                }
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

    private static bool ManifestRollbackSnapshotMayReinstateOwnership(
        FileRollbackSnapshot snapshot,
        FileRollbackSnapshot preparedManifestSnapshot
    ) =>
        snapshot.Existed
        && !FileRollbackSnapshotsRepresentSameState(snapshot, preparedManifestSnapshot);

    private static bool ManifestRollbackSnapshotGitConfigProofsMatchCurrentFiles(
        IFileSystem fileSystem,
        FileRollbackSnapshot snapshot
    )
    {
        if (!snapshot.Existed)
        {
            return true;
        }

        try
        {
            ValidateFileSnapshotIsRegularFile(
                snapshot,
                "configuration ownership manifest rollback snapshot"
            );
            ConfigurationOwnershipManifest manifest =
                ConfigurationOwnershipManifestSerializer.Deserialize(snapshot.Contents!);
            ConfigurationPhysicalTargetOwnershipProof[] ownershipProofs =
                CreatePhysicalTargetOwnershipProofs(manifest);
            if (ownershipProofs.Length == 0)
            {
                return true;
            }

            new ConfigurationPhysicalTargetWriterDispatcher(fileSystem)
                .ValidateRetainedOwnershipProofs(ownershipProofs, CancellationToken.None);
            return true;
        }
        catch (Exception exception)
            when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or InvalidOperationException
                    or System.Text.Json.JsonException
                    or ArgumentException
            )
        {
            return false;
        }
    }

    private static bool CurrentManifestAdoptsPreparedPhysicalTargetEntriesForPhysicalSnapshot(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        ConfigurationOwnershipManifest? preparedOwnershipManifest,
        FileRollbackSnapshot physicalSnapshot
    )
    {
        if (!SnapshotMatchesExpectedCurrentForRollback(fileSystem, physicalSnapshot))
        {
            return false;
        }

        if (preparedOwnershipManifest is null)
        {
            return CurrentFinalManifestAdoptsPreparedPhysicalTargetRemovals(
                fileSystem,
                manifestPath,
                plan,
                physicalSnapshot
            );
        }

        ConfigurationOwnershipManifest? currentManifest = TryReadFinalOwnershipManifest(
            fileSystem,
            manifestPath,
            plan
        );
        if (currentManifest is null)
        {
            return false;
        }

        if (!ManifestIdentityMatches(currentManifest, preparedOwnershipManifest))
        {
            return false;
        }

        ConfigurationChange[] affectedChanges = GetPhysicalTargetChangesAffectedByPhysicalSnapshot(
            fileSystem,
            plan,
            physicalSnapshot
        );
        if (affectedChanges.Length == 0)
        {
            return false;
        }

        ConfigurationOwnershipManifestEntry[] currentPathEntries = currentManifest
            .Entries.Where(entry =>
                IsRollbackAdoptablePhysicalTargetKind(entry.TargetKind)
                && PhysicalTargetManifestEntryMatchesSnapshot(
                    fileSystem,
                    entry,
                    physicalSnapshot
                )
            )
            .ToArray();
        ConfigurationOwnershipManifestEntry[] preparedPathEntries = preparedOwnershipManifest
            .Entries.Where(entry =>
                IsRollbackAdoptablePhysicalTargetKind(entry.TargetKind)
                && PhysicalTargetManifestEntryMatchesSnapshot(
                    fileSystem,
                    entry,
                    physicalSnapshot
                )
            )
            .ToArray();
        return affectedChanges.All(change =>
                AffectedPhysicalTargetChangeIsAdoptedByCurrentManifest(change, currentPathEntries)
            )
            && preparedPathEntries.All(preparedEntry =>
            {
                ConfigurationChange? matchingChange = affectedChanges.FirstOrDefault(change =>
                    string.Equals(
                        CanonicalizePhysicalTargetManifestKey(change.TargetKind, change.Key),
                        CanonicalizePhysicalTargetManifestKey(
                            preparedEntry.TargetKind,
                            preparedEntry.Key
                        ),
                        StringComparison.Ordinal
                    )
                );

                return PreparedPhysicalTargetEntryIsAdoptedByCurrentManifest(
                    fileSystem,
                    matchingChange,
                    preparedEntry,
                    currentManifest
                );
            });
    }

    private static bool CurrentFinalManifestAdoptsPreparedPhysicalTargetRemovals(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan,
        FileRollbackSnapshot physicalSnapshot
    )
    {
        ConfigurationChange[] affectedChanges = GetPhysicalTargetChangesAffectedByPhysicalSnapshot(
            fileSystem,
            plan,
            physicalSnapshot
        );
        if (
            affectedChanges.Length == 0
            || affectedChanges.Any(change =>
                change.Operation != ConfigurationChangeOperation.Remove
            )
        )
        {
            return false;
        }

        ConfigurationOwnershipManifest? currentManifest = TryReadFinalOwnershipManifest(
            fileSystem,
            manifestPath,
            plan
        );
        if (currentManifest is null)
        {
            return CurrentManifestMatchesPreparedFinalState(
                fileSystem,
                manifestPath,
                preparedOwnershipManifest: null
            );
        }

        if (!ManifestIdentityMatches(currentManifest, plan))
        {
            return false;
        }

        ConfigurationOwnershipManifestEntry[] currentPathEntries = currentManifest
            .Entries.Where(entry =>
                IsRollbackAdoptablePhysicalTargetKind(entry.TargetKind)
                && PhysicalTargetManifestEntryMatchesSnapshot(
                    fileSystem,
                    entry,
                    physicalSnapshot
                )
            )
            .ToArray();
        return affectedChanges.All(change =>
            AffectedPhysicalTargetChangeIsAdoptedByCurrentManifest(change, currentPathEntries)
        );
    }

    private static bool IsRollbackAdoptablePhysicalTargetKind(
        ConfigurationTargetKind targetKind
    ) =>
        targetKind
            is ConfigurationTargetKind.GitConfig
                or ConfigurationTargetKind.Npmrc
                or ConfigurationTargetKind.Yarnrc;

    private static ConfigurationChange[] GetPhysicalTargetChangesAffectedByPhysicalSnapshot(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan,
        FileRollbackSnapshot physicalSnapshot
    ) =>
        plan
            .Changes.Where(change =>
                IsRollbackAdoptablePhysicalTargetKind(change.TargetKind)
                && string.Equals(
                    CreatePhysicalPathIdentity(fileSystem, change.TargetPathOrName),
                    CreatePhysicalPathIdentity(fileSystem, physicalSnapshot.Path),
                    GetPathIdentityComparison()
                )
            )
            .ToArray();

    private static bool ManifestIdentityMatches(
        ConfigurationOwnershipManifest currentManifest,
        ConfigurationOwnershipManifest preparedManifest
    ) =>
        string.Equals(
            currentManifest.ManifestId,
            preparedManifest.ManifestId,
            StringComparison.Ordinal
        )
        && string.Equals(
            currentManifest.OwnerProductId,
            preparedManifest.OwnerProductId,
            StringComparison.Ordinal
        )
        && string.Equals(
            currentManifest.EntrySelector,
            preparedManifest.EntrySelector,
            StringComparison.Ordinal
        );

    private static bool ManifestIdentityMatches(
        ConfigurationOwnershipManifest currentManifest,
        ConfigurationChangePlan plan
    ) =>
        string.Equals(
            currentManifest.ManifestId,
            plan.Manifest.ManifestId,
            StringComparison.Ordinal
        )
        && string.Equals(
            currentManifest.OwnerProductId,
            plan.OwnerProductId,
            StringComparison.Ordinal
        )
        && string.Equals(
            currentManifest.EntrySelector,
            plan.Manifest.EntrySelector,
            StringComparison.Ordinal
        );

    private static bool AffectedPhysicalTargetChangeIsAdoptedByCurrentManifest(
        ConfigurationChange change,
        IReadOnlyList<ConfigurationOwnershipManifestEntry> currentPathEntries
    )
    {
        string affectedKey = CanonicalizePhysicalTargetManifestKey(change.TargetKind, change.Key);
        ConfigurationOwnershipManifestEntry[] matchingCurrentEntries = currentPathEntries
            .Where(entry =>
                string.Equals(
                    CanonicalizePhysicalTargetManifestKey(entry.TargetKind, entry.Key),
                    affectedKey,
                    StringComparison.Ordinal
                )
            )
            .ToArray();
        if (change.Operation == ConfigurationChangeOperation.Remove)
        {
            return matchingCurrentEntries.Length == 0;
        }

        if (matchingCurrentEntries.Length != 1)
        {
            return false;
        }

        ConfigurationOwnershipManifestEntry matchingCurrentEntry = matchingCurrentEntries[0];
        if (
            change.IsSecretValue
            && change.TargetKind is ConfigurationTargetKind.Npmrc or ConfigurationTargetKind.Yarnrc
        )
        {
            return matchingCurrentEntry.IsSecretValue;
        }

        return PlannedValueSha256MatchesChange(change, matchingCurrentEntry.PlannedValueSha256);
    }

    private static ConfigurationOwnershipManifest? TryReadFinalOwnershipManifest(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationChangePlan plan
    )
    {
        try
        {
            EnsureManifestParentChainIsUsable(fileSystem, manifestPath);
            EnsurePathIsNotUnsupportedReparsePoint(
                fileSystem,
                manifestPath,
                "configuration ownership manifest"
            );
            FileRollbackSnapshot currentManifestSnapshot = CaptureRollbackSnapshot(
                fileSystem,
                manifestPath
            );
            if (!currentManifestSnapshot.Existed)
            {
                return null;
            }

            ValidateFileSnapshotIsRegularFile(
                currentManifestSnapshot,
                "configuration ownership manifest"
            );
            ConfigurationOwnershipManifest currentManifest =
                ConfigurationOwnershipManifestSerializer.Deserialize(
                    currentManifestSnapshot.Contents!
                );
            if (ContainsPhysicalTargetManifestPreclaimMetadataKey(currentManifest))
            {
                return null;
            }

            ConfigurationOwnershipManifestPolicy.EnsureValid(currentManifest);
            ValidateOwnershipManifestPathDoesNotCollideWithPhysicalTargetEntries(
                fileSystem,
                manifestPath,
                currentManifest
            );
            ValidatePhysicalTargetManifestEntries(fileSystem, currentManifest);
            ValidateCiTemporaryFileManifestWholeFileOwnership(fileSystem, plan, currentManifest);
            ValidateGitConfigRetainedUseHttpPathOwnershipProofs(
                fileSystem,
                currentManifest,
                CancellationToken.None
            );
            return currentManifest;
        }
        catch (Exception exception)
            when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or InvalidOperationException
                    or System.Text.Json.JsonException
                    or ArgumentException
            )
        {
            return null;
        }
    }

    private static void VerifyCurrentPhysicalTargetManifestMatchesPreparedFinalState(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationOwnershipManifest? preparedOwnershipManifest
    ) =>
        VerifyCurrentManifestMatchesPreparedFinalState(
            fileSystem,
            manifestPath,
            preparedOwnershipManifest,
            "Configuration ownership manifest conflict: final manifest changed during physical "
                + "target dispatch."
        );

    private static void VerifyCurrentGenericManifestMatchesPreparedFinalState(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationOwnershipManifest? preparedOwnershipManifest
    ) =>
        VerifyCurrentManifestMatchesPreparedFinalState(
            fileSystem,
            manifestPath,
            preparedOwnershipManifest,
            "Configuration ownership manifest conflict: final manifest changed during "
                + "configuration operation."
        );

    private static void VerifyCurrentManifestMatchesPreparedFinalState(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationOwnershipManifest? preparedOwnershipManifest,
        string conflictMessage
    )
    {
        if (
            CurrentManifestMatchesPreparedFinalState(
                fileSystem,
                manifestPath,
                preparedOwnershipManifest
            )
        )
        {
            return;
        }

        throw new InvalidOperationException(conflictMessage);
    }

    private static bool CurrentManifestMatchesPreparedFinalState(
        IFileSystem fileSystem,
        string manifestPath,
        ConfigurationOwnershipManifest? preparedOwnershipManifest
    )
    {
        try
        {
            EnsureManifestParentChainIsUsable(fileSystem, manifestPath);
            EnsurePathIsNotUnsupportedReparsePoint(
                fileSystem,
                manifestPath,
                "configuration ownership manifest"
            );
            FileRollbackSnapshot currentManifestSnapshot = CaptureRollbackSnapshot(
                fileSystem,
                manifestPath
            );
            if (preparedOwnershipManifest is null)
            {
                return !currentManifestSnapshot.Existed;
            }

            if (!currentManifestSnapshot.Existed)
            {
                return false;
            }

            ValidateFileSnapshotIsRegularFile(
                currentManifestSnapshot,
                "configuration ownership manifest"
            );
            ConfigurationOwnershipManifest currentManifest =
                ConfigurationOwnershipManifestSerializer.Deserialize(
                    currentManifestSnapshot.Contents!
                );
            string currentManifestContents = ConfigurationOwnershipManifestSerializer.Serialize(
                currentManifest
            );
            string preparedManifestContents = ConfigurationOwnershipManifestSerializer.Serialize(
                preparedOwnershipManifest
            );
            return string.Equals(
                currentManifestContents,
                preparedManifestContents,
                StringComparison.Ordinal
            );
        }
        catch (Exception exception)
            when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or InvalidOperationException
                    or System.Text.Json.JsonException
                    or ArgumentException
            )
        {
            return false;
        }
    }

    private static bool PhysicalTargetManifestEntryMatchesSnapshot(
        IFileSystem fileSystem,
        ConfigurationOwnershipManifestEntry entry,
        FileRollbackSnapshot physicalSnapshot
    ) =>
        string.Equals(
            CreatePhysicalPathIdentity(fileSystem, entry.TargetPathOrName),
            CreatePhysicalPathIdentity(fileSystem, physicalSnapshot.Path),
            GetPathIdentityComparison()
        );

    private static bool PreparedPhysicalTargetEntryIsAdoptedByCurrentManifest(
        IFileSystem fileSystem,
        ConfigurationChange? change,
        ConfigurationOwnershipManifestEntry preparedEntry,
        ConfigurationOwnershipManifest currentManifest
    )
    {
        if (
            !IsRollbackAdoptablePhysicalTargetKind(preparedEntry.TargetKind)
            || (!preparedEntry.IsSecretValue
                && string.IsNullOrWhiteSpace(preparedEntry.PlannedValueSha256)
            )
        )
        {
            return false;
        }

        string preparedTargetPath = CreatePhysicalPathIdentity(
            fileSystem,
            preparedEntry.TargetPathOrName
        );
        string preparedKey = CanonicalizePhysicalTargetManifestKey(
            preparedEntry.TargetKind,
            preparedEntry.Key
        );
        return currentManifest.Entries.Any(currentEntry =>
            IsRollbackAdoptablePhysicalTargetKind(currentEntry.TargetKind)
            && string.Equals(
                CreatePhysicalPathIdentity(fileSystem, currentEntry.TargetPathOrName),
                preparedTargetPath,
                GetPathIdentityComparison()
            )
            && string.Equals(
                CanonicalizePhysicalTargetManifestKey(currentEntry.TargetKind, currentEntry.Key),
                preparedKey,
                StringComparison.Ordinal
            )
            && (preparedEntry.IsSecretValue
                ? currentEntry.IsSecretValue
                : change is null
                    ? string.Equals(
                        currentEntry.PlannedValueSha256,
                        preparedEntry.PlannedValueSha256,
                        StringComparison.Ordinal
                    )
                    : PlannedValueSha256MatchesChange(change, preparedEntry.PlannedValueSha256)
                        && PlannedValueSha256MatchesChange(
                            change,
                            currentEntry.PlannedValueSha256
                        ))
        );
    }

    private static bool PlannedValueSha256MatchesChange(
        ConfigurationChange change,
        string? plannedValueSha256
    )
    {
        if (
            change.TargetKind == ConfigurationTargetKind.GitConfig
            && string.Equals(
                CanonicalizePhysicalTargetManifestKey(change.TargetKind, change.Key),
                "credential.helper",
                StringComparison.Ordinal
            )
            && change.Value is not null
        )
        {
            return CredentialHelperPlannedValueSha256Matches(plannedValueSha256, change.Value);
        }

        if (change.Value is null)
        {
            return false;
        }

        return string.Equals(
            plannedValueSha256,
            ComputeSha256(ConfigurationPlanProjector.GetPlannedValueForHash(change)),
            StringComparison.Ordinal
        );
    }

    private static bool CredentialHelperPlannedValueSha256Matches(
        string? plannedValueSha256,
        string helperValue
    )
    {
        if (string.IsNullOrWhiteSpace(plannedValueSha256))
        {
            return false;
        }

        string rawPlannedValueSha256 = ComputeSha256(helperValue);
        if (string.Equals(plannedValueSha256, rawPlannedValueSha256, StringComparison.Ordinal))
        {
            return true;
        }

        string escapedPlannedValueSha256 = ComputeSha256(
            GitConfigPhysicalTargetWriter.EscapeCredentialHelperPathForShell(helperValue)
        );
        return string.Equals(
            plannedValueSha256,
            escapedPlannedValueSha256,
            StringComparison.Ordinal
        );
    }

    private static bool SnapshotMatchesExpectedCurrentForRollback(
        IFileSystem fileSystem,
        FileRollbackSnapshot snapshot
    )
    {
        try
        {
            EnsurePhysicalFileParentChainIsUsable(fileSystem, snapshot.Path, "rollback target");
            EnsurePathIsNotUnsupportedReparsePoint(fileSystem, snapshot.Path, "rollback target");
            FileRollbackSnapshot currentSnapshot = CaptureRollbackSnapshot(
                fileSystem,
                snapshot.Path
            );
            if (snapshot.ExpectedCurrentHashForRollback is null)
            {
                return currentSnapshot.EntryKind == FileRollbackSnapshotEntryKind.Missing;
            }

            return currentSnapshot.EntryKind == FileRollbackSnapshotEntryKind.RegularFile
                && string.Equals(
                    currentSnapshot.ContentsSha256Hash,
                    snapshot.ExpectedCurrentHashForRollback,
                    StringComparison.Ordinal
                );
        }
        catch (Exception exception)
            when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or InvalidOperationException
            )
        {
            return false;
        }
    }

    private static bool ManifestMatchesSnapshot(
        IFileSystem fileSystem,
        string manifestPath,
        FileRollbackSnapshot preparedManifestSnapshot
    )
    {
        try
        {
            EnsureManifestParentChainIsUsable(fileSystem, manifestPath);
            EnsurePathIsNotUnsupportedReparsePoint(
                fileSystem,
                manifestPath,
                "configuration ownership manifest"
            );
            return FileRollbackSnapshotsRepresentSameState(
                CaptureRollbackSnapshot(fileSystem, manifestPath),
                preparedManifestSnapshot
            );
        }
        catch (Exception exception)
            when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or InvalidOperationException
            )
        {
            return false;
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
            DeleteExistingTemporaryContainerIfOnlyArtifactsRemain(
                fileSystem,
                containerSnapshot,
                CreateRemovedTargetFileSet(fileSystem, plan)
            );
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
            TryDeleteTemporaryContainerFile(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: true
            )
        )
        {
            return;
        }

        if (fileSystem.FileExists(containerSnapshot.Path))
        {
            return;
        }

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
            TryDeleteTemporaryContainerFile(
                fileSystem,
                containerSnapshot,
                throwIfUnsafe: false
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

    private static bool TryDeleteTemporaryContainerFile(
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

            if (
                fileSystem.DirectoryExists(containerSnapshot.Path)
                || !fileSystem.FileExists(containerSnapshot.Path)
            )
            {
                return false;
            }

            if (!IsSafeFileSystemArtifact(fileSystem, containerSnapshot.Path))
            {
                throw new NotSupportedException(
                    "Temporary container cleanup rejects unsafe file containers."
                );
            }

            if (
                fileSystem is not IFileSystemFileLength fileLength
                || fileLength.GetFileLength(containerSnapshot.Path) != 0
            )
            {
                return false;
            }

            fileSystem.DeleteFile(containerSnapshot.Path);
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

    private static void DeleteExistingTemporaryContainerIfOnlyArtifactsRemain(
        IFileSystem fileSystem,
        ContainerRollbackSnapshot containerSnapshot,
        IReadOnlySet<string>? emptyRemovedTargetFiles = null
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
                && !IsSafeEmptyRemovedTargetFile(fileSystem, file, emptyRemovedTargetFiles)
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

    private static HashSet<string> CreateRemovedTargetFileSet(
        IFileSystem fileSystem,
        ConfigurationChangePlan plan
    ) =>
        plan
            .Changes.Where(change => change.Operation == ConfigurationChangeOperation.Remove)
            .Select(change => fileSystem.GetFullPath(change.TargetPathOrName))
            .ToHashSet(GetPathIdentityComparer());

    private static bool IsSafeEmptyRemovedTargetFile(
        IFileSystem fileSystem,
        string path,
        IReadOnlySet<string>? emptyRemovedTargetFiles
    )
    {
        if (
            emptyRemovedTargetFiles is null
            || !emptyRemovedTargetFiles.Contains(path)
            || !IsSafeFileSystemArtifact(fileSystem, path)
        )
        {
            return false;
        }

        try
        {
            return fileSystem is IFileSystemFileLength fileLength
                && fileLength.GetFileLength(path) == 0;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
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
            ? ComputeSha256(ConfigurationPlanProjector.GetPlannedValueForHash(change))
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

    private static string CreateNoFilesystemPhysicalPathIdentity(string targetPathOrName) =>
        NormalizePhysicalTargetConfigurationPathSegments(
            Path.TrimEndingDirectorySeparator(targetPathOrName)
        );

    private static string CreatePhysicalPathIdentity(
        IFileSystem fileSystem,
        string targetPathOrName
    )
    {
        string targetPath = fileSystem.GetFullPath(targetPathOrName);
        return NormalizePhysicalTargetConfigurationPathSegments(
            Path.TrimEndingDirectorySeparator(targetPath)
        );
    }

    private static StringComparer GetPathIdentityComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathIdentityComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    internal sealed class ConfigurationPathIdentityComparer : IEqualityComparer<string>
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

    internal static bool IsCanonicalNuGetPluginLayoutTargetRootPath(string targetPath)
    {
        if (
            string.IsNullOrWhiteSpace(targetPath)
            || ContainsPhysicalPathTraversalSegments(targetPath)
        )
        {
            return false;
        }

        string normalizedTargetPath = NormalizePhysicalTargetConfigurationPathSegments(targetPath);
        string canonicalTargetPath = GetCanonicalNuGetPluginLayoutTargetRootPath();
        return !string.IsNullOrWhiteSpace(canonicalTargetPath)
            && string.Equals(
                normalizedTargetPath,
                canonicalTargetPath,
                GetPathIdentityComparison()
            );
    }

    private static bool ContainsPhysicalPathTraversalSegments(string path)
    {
        string normalizedPath = NormalizeRelativeConfigurationPathSegments(path);
        return normalizedPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static string GetCanonicalNuGetPluginLayoutTargetRootPath()
    {
        string homeDirectory = GetCurrentUserProfileDirectory();
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return string.Empty;
        }

        ConfigurationLayoutProjectionContext context = new()
        {
            Platform = GetCurrentLayoutPlatform(),
            HomeDirectory = homeDirectory,
        };

        return NormalizePhysicalTargetConfigurationPathSegments(
            ConfigurationLayoutProjector.ProjectNuGetPluginLayout(context).TargetPath
        );
    }

    private static ConfigurationLayoutPlatform GetCurrentLayoutPlatform() =>
        OperatingSystem.IsWindows()
            ? ConfigurationLayoutPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? ConfigurationLayoutPlatform.MacOs
                : ConfigurationLayoutPlatform.Linux;

    private static string GetCurrentUserProfileDirectory()
    {
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.TrimEndingDirectorySeparator(userProfile);
        }

        if (OperatingSystem.IsWindows())
        {
            string? windowsUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(windowsUserProfile))
            {
                return Path.TrimEndingDirectorySeparator(windowsUserProfile);
            }

            string? homeDrive = Environment.GetEnvironmentVariable("HOMEDRIVE");
            string? homePath = Environment.GetEnvironmentVariable("HOMEPATH");
            if (!string.IsNullOrWhiteSpace(homeDrive) && !string.IsNullOrWhiteSpace(homePath))
            {
                return Path.TrimEndingDirectorySeparator(homeDrive + homePath);
            }
        }
        else
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                return Path.TrimEndingDirectorySeparator(home);
            }
        }

        return string.Empty;
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

    private static bool ContainsProjectionOnlyPhysicalTarget(ConfigurationChangePlan plan) =>
        plan.Changes.Any(change => IsProjectionOnlyPhysicalTarget(change.TargetKind));

    private static bool IsProjectionOnlyPhysicalTarget(ConfigurationTargetKind targetKind) =>
        targetKind
            is ConfigurationTargetKind.GitConfig
                or ConfigurationTargetKind.Npmrc
                or ConfigurationTargetKind.Yarnrc
                or ConfigurationTargetKind.NuGetPluginLayout
                or ConfigurationTargetKind.PythonKeyringBackend
                or ConfigurationTargetKind.KeyringShim;

    private static bool IsSupportedProjectionOnlyPhysicalTarget(
        ConfigurationTargetKind targetKind
    ) =>
        targetKind is ConfigurationTargetKind.GitConfig
            or ConfigurationTargetKind.Npmrc
            or ConfigurationTargetKind.Yarnrc
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

    private static bool IsLowercaseSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(IsLowercaseHex);

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
        string? ExpectedCurrentHashForRollback = null,
        UnixFileMode? UnixFileMode = null
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

    private sealed record PhysicalTargetManifestDispatchPreparation(
        ConfigurationOwnershipManifest? PreparedOwnershipManifest,
        bool DeleteManifest,
        FileRollbackSnapshot ManifestRollbackSnapshot,
        IReadOnlyList<ConfigurationPhysicalTargetOwnershipProof> OwnershipProofs
    );

    private sealed class PhysicalTargetManifestCommitIndeterminateException(
        Exception innerException
    )
        : InvalidOperationException(
            "Configuration ownership manifest final commit may have reached durable state and "
                + "could not be verified; rollback was skipped to avoid clobbering committed "
                + "physical target state.",
            innerException
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CanonicalResourceIdentity? ResourceIdentity { get; init; }
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
