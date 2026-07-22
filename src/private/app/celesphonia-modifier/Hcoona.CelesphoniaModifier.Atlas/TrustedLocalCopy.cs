using System.Buffers;
using System.Security.Cryptography;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class TrustedLocalCopy
{
    public static ValueTask CopyAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        CopyAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    internal static async ValueTask CopyAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);

        AtlasLoadedDocument<AtlasIntakeCopyRequest> loadedRequest =
            await AtlasIntakeContracts.ReadCopyRequestAsync(
                    requestPath,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasIntakeCopyRequest request = loadedRequest.Document;
        AtlasWorkspaceLayout layout = AtlasIntakeContracts.CreateWorkspaceLayout(
            request.ProjectRoot,
            request.WorkspaceRoot,
            request.SurveyAlias);
        if (AtlasIntakeContracts.PathEquals(
                request.ApprovedStatePath,
                layout.CanonicalApprovedStatePath)
            && AtlasDiscovery.IsRequiredFileAbsent(layout.CanonicalApprovedStatePath, io))
        {
            if (!AtlasDiscovery.IsRequiredFileAbsent(layout.CanonicalQualifiedStatePath, io)
                || !AtlasDiscovery.IsRequiredFileAbsent(
                    layout.CanonicalPreflightedStatePath,
                    io))
            {
                throw new AtlasSafetyException(
                    "A completed state lacks its approved predecessor.");
            }

            throw new AtlasApprovalException("The approved state is required.");
        }

        ValidateCopyCanonicalPaths(loadedRequest.AbsolutePath, request, layout, io);
        AtlasDiscovery.ValidateCommandWorkspaceCensus(
            layout,
            AtlasIntakeContracts.QualifiedStateRevision,
            io);

        if (await AtlasDiscovery.TryReturnValidatedCopyAsync(
                loadedRequest,
                layout,
                io,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        AtlasLoadedDocument<AtlasIntakeStateDocument> approvedState =
            await AtlasIntakeContracts.ReadStateAsync(
                    request.ApprovedStatePath,
                    cancellationToken)
                .ConfigureAwait(false);
        if (approvedState.Document.StateRevision != AtlasIntakeContracts.ApprovedStateRevision)
        {
            throw new AtlasSafetyException("The approved state revision is invalid.");
        }

        AtlasDiscovery.EnsureDigestMatches(
            request.ExpectedApprovedStateSha256,
            approvedState.Sha256,
            static () => new AtlasSafetyException("The approved state digest is invalid."));
        if (!StringComparer.Ordinal.Equals(
                approvedState.Document.DecisionCommit,
                request.DecisionCommit))
        {
            throw new AtlasSafetyException(
                "The decision commit does not match state revision 2.");
        }

        PhaseInventoryContext inventoryContext = await LoadPhaseInventoryAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalQualifiedInventoryBackupPath,
                request.ExpectedInventorySha256,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        await AtlasDiscovery.ValidateStateChainAsync(
                layout,
                approvedState,
                inventoryContext.PriorInventory,
                new AtlasDiscovery.StateValidationExpectations(),
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasDocumentBinding approvedManifestBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.ApprovedManifestRole);
        AtlasDocumentBinding sourceRootMapBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.SourceRootMapRole);
        AtlasDocumentBinding copyPlanBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.CopyPlanRole);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                    request.ApprovedManifestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap =
            await AtlasIntakeContracts.ReadSourceRootMapAsync(
                    request.SourceRootMapPath,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                    request.CopyPlanPath,
                    cancellationToken)
                .ConfigureAwait(false);

        AtlasDiscovery.EnsureStateDocumentMatchesBinding(approvedManifest, approvedManifestBinding);
        AtlasDiscovery.EnsureStateDocumentMatchesBinding(sourceRootMap, sourceRootMapBinding);
        AtlasDiscovery.EnsureStateDocumentMatchesBinding(copyPlan, copyPlanBinding);

        if (approvedManifest.Document.ManifestRevision
                != AtlasIntakeContracts.ApprovedManifestRevision
            || !StringComparer.Ordinal.Equals(
                approvedManifest.Document.Confirmation.Status,
                AtlasIntakeContracts.ApprovedConfirmationStatus)
            || !StringComparer.Ordinal.Equals(
                approvedManifest.Document.Confirmation.DecisionReference,
                AtlasIntakeContracts.ApprovalDecisionReferencePrefix + request.DecisionCommit))
        {
            throw new AtlasApprovalException("The approved manifest is invalid.");
        }

        CopyPhaseAliases aliases = ResolveCopyAliases(
            inventoryContext,
            copyPlan.Document);

        string finalReceiptPath = layout.CanonicalCopyReceiptPath;
        string finalReceiptStagingPath = AtlasDiscovery.GetStagingPath(
            finalReceiptPath,
            AtlasIntakeContracts.QualifiedPhase);
        string incompleteReceiptStagingPath = Path.Combine(
            layout.CanonicalIncompleteCopyPath,
            Path.GetFileName(finalReceiptStagingPath));
        await PromoteValidatedInnerReceiptStagingAsync(
                loadedRequest.Sha256,
                request,
                layout,
                approvedState,
                approvedManifest,
                sourceRootMap,
                copyPlan,
                aliases,
                incompleteReceiptStagingPath,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        ValidateCopyOutputCensus(layout, copyPlan.Document, io);
        RequireRecoverableCopyStateBeforeSourceAccess(layout, inventoryContext, io);

        if (io.DirectoryExists(layout.CanonicalFinalCopyPath)
            || io.DirectoryExists(layout.CanonicalIncompleteCopyPath))
        {
            if (await TryRecoverCopyFinalizationAsync(
                    loadedRequest,
                    request,
                    layout,
                    approvedState,
                    approvedManifest,
                    sourceRootMap,
                    copyPlan,
                    inventoryContext,
                    aliases,
                    finalReceiptPath,
                    finalReceiptStagingPath,
                    incompleteReceiptStagingPath,
                    io,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return;
            }
        }

        if (io.DirectoryExists(layout.CanonicalFinalCopyPath)
            || io.DirectoryExists(layout.CanonicalIncompleteCopyPath))
        {
            throw new AtlasSafetyException(
                "An unfinished copy directory requires human inspection.");
        }

        ValidateFreshCopyOutputAbsence(layout, io);
        CopyValidationContext validation = ValidateCurrentSourcesAgainstManifest(
            approvedManifest.Document,
            sourceRootMap.Document,
            copyPlan.Document,
            io);
        io.CreateDirectory(Path.GetDirectoryName(layout.CanonicalIncompleteCopyPath)!);
        io.CreateDirectory(layout.CanonicalIncompleteCopyPath);

        bool renamedToFinal = false;
        try
        {
            List<AtlasCopyReceiptEntry> receiptEntries = [];
            foreach (ResolvedCopySource source in validation.IncludedSources
                         .OrderBy(
                             static source => source.CopyPlanEntry.SourceAlias,
                             StringComparer.Ordinal))
            {
                string destinationPath = Path.Combine(
                    layout.CanonicalIncompleteCopyPath,
                    source.CopyPlanEntry.DestinationRelativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                io.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                receiptEntries.Add(
                    await CopySourceFileAsync(
                            source,
                            destinationPath,
                            io,
                            cancellationToken)
                        .ConfigureAwait(false));
            }

            string gameExecutableSha256 = await HashTrackedSourceAsync(
                    sourceRootMap.Document.GameExecutablePath,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            _ = ValidateCurrentSourcesAgainstManifest(
                approvedManifest.Document,
                sourceRootMap.Document,
                copyPlan.Document,
                io);

            AtlasCopyReceiptDocument stagedReceipt = CreateCopyReceipt(
                loadedRequest.Sha256,
                request,
                approvedState,
                approvedManifest,
                sourceRootMap,
                copyPlan,
                aliases,
                gameExecutableSha256,
                receiptEntries);
            byte[] stagedReceiptBytes = AtlasIntakeContracts.SerializeCopyReceipt(stagedReceipt);
            _ = await AtlasDiscovery.EnsureDeterministicFileAsync(
                    incompleteReceiptStagingPath,
                    AtlasIntakeContracts.QualifiedPhase,
                    stagedReceiptBytes,
                    AtlasDiscovery.ReadCopyReceiptShaAsync,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            AtlasLoadedDocument<AtlasCopyReceiptDocument> stagedReceiptEvidence =
                await LoadReceiptAsync(incompleteReceiptStagingPath, cancellationToken)
                    .ConfigureAwait(false);
            ValidateReceiptAgainstBindings(
                loadedRequest.Sha256,
                request,
                approvedState,
                approvedManifest,
                sourceRootMap,
                copyPlan,
                aliases,
                stagedReceiptEvidence.Document);
            if (!HasCompleteCopySet(
                    layout.CanonicalIncompleteCopyPath,
                    copyPlan.Document,
                    incompleteReceiptStagingPath,
                    io))
            {
                throw new AtlasSafetyException("The incomplete copy directory is unusable.");
            }

            await ValidateCopiedFilesAgainstReceiptAsync(
                layout.CanonicalIncompleteCopyPath,
                stagedReceiptEvidence.Document,
                io,
                cancellationToken);

            if (io.DirectoryExists(layout.CanonicalFinalCopyPath))
            {
                throw new AtlasSafetyException("The final copy path must not exist before rename.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            io.MoveDirectory(layout.CanonicalIncompleteCopyPath, layout.CanonicalFinalCopyPath);
            renamedToFinal = true;
            finalReceiptStagingPath = Path.Combine(
                layout.CanonicalFinalCopyPath,
                Path.GetFileName(incompleteReceiptStagingPath));
            if (!HasCompleteCopySet(
                    layout.CanonicalFinalCopyPath,
                    copyPlan.Document,
                    finalReceiptStagingPath,
                    io))
            {
                throw new AtlasSafetyException("The final copy directory is unusable.");
            }

            await ValidateCopiedFilesAgainstReceiptAsync(
                layout.CanonicalFinalCopyPath,
                stagedReceiptEvidence.Document,
                io,
                cancellationToken);

            AtlasPrivateArtifactInventoryDocument replacementInventory = CreateQualifiedInventory(
                inventoryContext.PriorInventory.Document,
                approvedManifestBinding.ArtifactAlias,
                copyPlan.Document,
                aliases,
                finalReceiptPath);
            byte[] replacementInventoryBytes =
                AtlasIntakeContracts.SerializeInventory(replacementInventory);
            InventoryReplaceResult inventoryReplace =
                await AtlasDiscovery.EnsureInventoryReplaceAsync(
                        layout.CanonicalInventoryPath,
                        layout.CanonicalQualifiedInventoryBackupPath,
                        AtlasIntakeContracts.QualifiedPhase,
                        inventoryContext.PriorInventory.Bytes,
                        replacementInventoryBytes,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);

            await AtlasDiscovery.MoveValidatedFileAsync(
                    finalReceiptStagingPath,
                    finalReceiptPath,
                    AtlasIntakeContracts.ComputeSha256Hex(stagedReceiptBytes),
                    AtlasDiscovery.ReadCopyReceiptShaAsync,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            AtlasIntakeStateDocument state3 = CreateQualifiedState(
                request,
                layout,
                approvedState,
                approvedManifestBinding,
                sourceRootMapBinding,
                copyPlanBinding,
                aliases,
                loadedRequest.Sha256,
                inventoryReplace.BackupSha256,
                inventoryReplace.ReplacementSha256,
                AtlasIntakeContracts.ComputeSha256Hex(stagedReceiptBytes));
            byte[] stateBytes = AtlasIntakeContracts.SerializeState(state3);
            _ = await AtlasDiscovery.EnsureDeterministicFileAsync(
                    layout.CanonicalQualifiedStatePath,
                    AtlasIntakeContracts.QualifiedPhase,
                    stateBytes,
                    AtlasDiscovery.ReadStateShaAsync,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (!renamedToFinal && io.DirectoryExists(layout.CanonicalIncompleteCopyPath))
            {
                CancellationToken recoveryToken = cancellationToken.IsCancellationRequested
                    ? CancellationToken.None
                    : cancellationToken;
                IncompleteCopyEvidenceState incompleteState =
                    await ClassifyIncompleteCopyDirectoryAsync(
                            loadedRequest.Sha256,
                            request,
                            layout,
                            approvedState,
                            approvedManifest,
                            sourceRootMap,
                            copyPlan,
                            aliases,
                            incompleteReceiptStagingPath,
                            io,
                            recoveryToken)
                        .ConfigureAwait(false);
                if (incompleteState == IncompleteCopyEvidenceState.PartialOwned)
                {
                    DeleteOwnedIncompleteDirectory(
                        layout.CanonicalIncompleteCopyPath,
                        copyPlan.Document,
                        io);
                }
            }

            throw;
        }
    }

    internal static void ValidateCopyCanonicalPaths(
        string requestPath,
        AtlasIntakeCopyRequest request,
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io)
    {
        AtlasDiscovery.ValidateCanonicalRequestFile(
            requestPath,
            layout.CanonicalCopyRequestPath,
            layout.WorkspaceRoot,
            io);
        AtlasDiscovery.ValidateExistingOrdinaryFile(request.ApprovedStatePath, io);
        AtlasDiscovery.ValidateExistingOrdinaryFile(request.ApprovedManifestPath, io);
        AtlasDiscovery.ValidateExistingOrdinaryFile(request.SourceRootMapPath, io);
        AtlasDiscovery.ValidateExistingOrdinaryFile(request.CopyPlanPath, io);
        AtlasDiscovery.ValidateExistingOrdinaryFile(request.InventoryPath, io);
        AtlasDiscovery.ValidateCanonicalOutputDirectory(
            request.StateRevisionDirectory,
            layout.StatesDirectory,
            layout.WorkspaceRoot,
            io);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.ApprovedStatePath,
            layout.CanonicalApprovedStatePath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.ApprovedManifestPath,
            layout.CanonicalApprovedManifestPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.SourceRootMapPath,
            layout.CanonicalSourceRootMapPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.CopyPlanPath,
            layout.CanonicalCopyPlanPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.InventoryPath,
            layout.CanonicalInventoryPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        if (!AtlasIntakeContracts.PathEquals(
                request.IncompleteCopyPath,
                layout.CanonicalIncompleteCopyPath)
            || !AtlasIntakeContracts.PathEquals(
                request.FinalCopyPath,
                layout.CanonicalFinalCopyPath))
        {
            throw new AtlasSafetyException("The copy directory paths are invalid.");
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetDirectoryName(request.IncompleteCopyPath),
                Path.GetDirectoryName(request.FinalCopyPath))
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(request.IncompleteCopyPath),
                Path.GetFileName(request.FinalCopyPath) + ".incomplete"))
        {
            throw new AtlasSafetyException("The incomplete copy path is invalid.");
        }

        AtlasDiscovery.ValidateCreateNewOutputDirectory(
            layout.CopiesDirectory,
            layout.WorkspaceRoot,
            io);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.InventoryBackupPath,
            layout.CanonicalQualifiedInventoryBackupPath,
            layout.WorkspaceRoot,
            io,
            allowExistingOutput: true);
    }

    internal static void ValidateCopyOutputCensus(
        AtlasWorkspaceLayout layout,
        AtlasCopyPlanDocument copyPlan,
        AtlasIoSeams io)
    {
        bool hasIncompleteDirectory = io.DirectoryExists(layout.CanonicalIncompleteCopyPath);
        bool hasFinalDirectory = io.DirectoryExists(layout.CanonicalFinalCopyPath);
        if (io.FileExists(layout.CanonicalIncompleteCopyPath) && !hasIncompleteDirectory)
        {
            throw new AtlasSafetyException("The incomplete copy path is invalid.");
        }

        if (io.FileExists(layout.CanonicalFinalCopyPath) && !hasFinalDirectory)
        {
            throw new AtlasSafetyException("The final copy path is invalid.");
        }

        if (hasIncompleteDirectory && hasFinalDirectory)
        {
            throw new AtlasSafetyException("Unexpected copy directories require human inspection.");
        }

        string finalReceiptStagingPath = AtlasDiscovery.GetStagingPath(
            layout.CanonicalCopyReceiptPath,
            AtlasIntakeContracts.QualifiedPhase);
        string incompleteReceiptStagingPath = Path.Combine(
            layout.CanonicalIncompleteCopyPath,
            Path.GetFileName(finalReceiptStagingPath));
        if (hasIncompleteDirectory
            && !HasCompleteCopySet(
                layout.CanonicalIncompleteCopyPath,
                copyPlan,
                incompleteReceiptStagingPath,
                io))
        {
            throw new AtlasSafetyException("The incomplete copy directory is unusable.");
        }

        string finalReceiptPath = io.FileExists(finalReceiptStagingPath)
            ? finalReceiptStagingPath
            : layout.CanonicalCopyReceiptPath;
        if (hasFinalDirectory
            && !HasCompleteCopySet(
                layout.CanonicalFinalCopyPath,
                copyPlan,
                finalReceiptPath,
                io))
        {
            throw new AtlasSafetyException("The final copy directory is unusable.");
        }
    }

    internal static void ValidateFreshCopyOutputAbsence(
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io)
    {
        RejectExistingCopyOutput(
            layout.CanonicalIncompleteCopyPath,
            "The incomplete copy path exists.");
        RejectExistingCopyOutput(layout.CanonicalFinalCopyPath, "The final copy path exists.");
        return;

        void RejectExistingCopyOutput(string path, string message)
        {
            if (!io.FileExists(path) && !io.DirectoryExists(path))
            {
                return;
            }

            AtlasDiscovery.ValidatePathComponents(
                path,
                io,
                allowMissingLeaf: false,
                requireFileLeaf: false,
                requireDirectoryLeaf: false);
            throw new AtlasSafetyException(message);
        }
    }

    internal static async ValueTask<IncompleteCopyEvidenceState>
        ClassifyIncompleteCopyDirectoryAsync(
        string requestSha256,
        AtlasIntakeCopyRequest request,
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> approvedState,
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest,
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap,
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan,
        CopyPhaseAliases aliases,
        string incompleteReceiptStagingPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        try
        {
            string innerReceiptStagingPath = AtlasDiscovery.GetStagingPath(
                incompleteReceiptStagingPath,
                AtlasIntakeContracts.QualifiedPhase);
            bool hasOuterReceiptEvidence = io.FileExists(incompleteReceiptStagingPath)
                || io.DirectoryExists(incompleteReceiptStagingPath);
            bool hasInnerReceiptEvidence = io.FileExists(innerReceiptStagingPath)
                || io.DirectoryExists(innerReceiptStagingPath);
            if (!HasCompleteCopySet(
                    layout.CanonicalIncompleteCopyPath,
                    copyPlan.Document,
                    incompleteReceiptStagingPath,
                    io))
            {
                if (hasOuterReceiptEvidence || hasInnerReceiptEvidence)
                {
                    return IncompleteCopyEvidenceState.Complete;
                }

                _ = InspectOwnedPartialCopyContent(
                    layout.CanonicalIncompleteCopyPath,
                    copyPlan.Document,
                    io);
                return IncompleteCopyEvidenceState.PartialOwned;
            }
        }
        catch (OperationCanceledException)
        {
            return IncompleteCopyEvidenceState.Canceled;
        }
        catch (Exception exception) when (IsIncompleteEvidenceIoFailure(exception))
        {
            return IncompleteCopyEvidenceState.IoIndeterminate;
        }
        catch (AtlasSafetyException)
        {
            return IncompleteCopyEvidenceState.Complete;
        }

        try
        {
            AtlasLoadedDocument<AtlasCopyReceiptDocument> incompleteReceipt =
                await LoadReceiptAsync(incompleteReceiptStagingPath, cancellationToken)
                    .ConfigureAwait(false);
            ValidateReceiptAgainstBindings(
                requestSha256,
                request,
                approvedState,
                approvedManifest,
                sourceRootMap,
                copyPlan,
                aliases,
                incompleteReceipt.Document);
            await ValidateCopiedFilesAgainstReceiptAsync(
                layout.CanonicalIncompleteCopyPath,
                incompleteReceipt.Document,
                io,
                cancellationToken)
                .ConfigureAwait(false);
            return IncompleteCopyEvidenceState.Recoverable;
        }
        catch (OperationCanceledException)
        {
            return IncompleteCopyEvidenceState.Canceled;
        }
        catch (Exception exception) when (IsIncompleteEvidenceIoFailure(exception))
        {
            return IncompleteCopyEvidenceState.IoIndeterminate;
        }
        catch (AtlasSafetyException)
        {
            return IncompleteCopyEvidenceState.Complete;
        }
    }

    internal static async ValueTask PromoteValidatedInnerReceiptStagingAsync(
        string requestSha256,
        AtlasIntakeCopyRequest request,
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> approvedState,
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest,
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap,
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan,
        CopyPhaseAliases aliases,
        string incompleteReceiptStagingPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        string innerReceiptStagingPath = AtlasDiscovery.GetStagingPath(
            incompleteReceiptStagingPath,
            AtlasIntakeContracts.QualifiedPhase);
        bool hasOuterReceipt = io.FileExists(incompleteReceiptStagingPath);
        bool hasInnerReceipt = io.FileExists(innerReceiptStagingPath);
        if (io.DirectoryExists(incompleteReceiptStagingPath)
            || io.DirectoryExists(innerReceiptStagingPath)
            || (hasOuterReceipt && hasInnerReceipt))
        {
            throw new AtlasSafetyException("The staged copy receipt evidence is ambiguous.");
        }

        if (!hasInnerReceipt)
        {
            return;
        }

        if (!io.DirectoryExists(layout.CanonicalIncompleteCopyPath))
        {
            throw new AtlasSafetyException("The staged copy receipt is not request-owned.");
        }

        AtlasDiscovery.ValidateExistingOrdinaryFile(innerReceiptStagingPath, io);
        if (!HasCompleteCopySet(
                layout.CanonicalIncompleteCopyPath,
                copyPlan.Document,
                innerReceiptStagingPath,
                io))
        {
            throw new AtlasSafetyException("The staged copy receipt census is incomplete.");
        }

        AtlasLoadedDocument<AtlasCopyReceiptDocument> receipt =
            await LoadReceiptAsync(innerReceiptStagingPath, cancellationToken)
                .ConfigureAwait(false);
        ValidateReceiptAgainstBindings(
            requestSha256,
            request,
            approvedState,
            approvedManifest,
            sourceRootMap,
            copyPlan,
            aliases,
            receipt.Document);
        await ValidateCopiedFilesAgainstReceiptAsync(
                layout.CanonicalIncompleteCopyPath,
                receipt.Document,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        await AtlasDiscovery.MoveValidatedFileAsync(
                innerReceiptStagingPath,
                incompleteReceiptStagingPath,
                receipt.Sha256,
                AtlasDiscovery.ReadCopyReceiptShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask<bool> TryRecoverCopyFinalizationAsync(
        AtlasLoadedDocument<AtlasIntakeCopyRequest> loadedRequest,
        AtlasIntakeCopyRequest request,
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> approvedState,
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest,
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap,
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan,
        PhaseInventoryContext inventoryContext,
        CopyPhaseAliases aliases,
        string finalReceiptPath,
        string finalReceiptStagingPath,
        string incompleteReceiptStagingPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        bool hasIncomplete = io.DirectoryExists(layout.CanonicalIncompleteCopyPath);
        bool hasFinal = io.DirectoryExists(layout.CanonicalFinalCopyPath);
        if (hasIncomplete && hasFinal)
        {
            throw new AtlasSafetyException("Unexpected copy directories require human inspection.");
        }

        if (hasIncomplete)
        {
            if (!HasCompleteCopySet(
                    layout.CanonicalIncompleteCopyPath,
                    copyPlan.Document,
                    incompleteReceiptStagingPath,
                    io))
            {
                throw new AtlasSafetyException("The incomplete copy directory is unusable.");
            }

            AtlasLoadedDocument<AtlasCopyReceiptDocument> incompleteReceipt =
                await LoadReceiptAsync(
                        incompleteReceiptStagingPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            ValidateReceiptAgainstBindings(
                loadedRequest.Sha256,
                request,
                approvedState,
                approvedManifest,
                sourceRootMap,
                copyPlan,
                aliases,
                incompleteReceipt.Document);
            await ValidateCopiedFilesAgainstReceiptAsync(
                layout.CanonicalIncompleteCopyPath,
                incompleteReceipt.Document,
                io,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            io.MoveDirectory(layout.CanonicalIncompleteCopyPath, layout.CanonicalFinalCopyPath);
            hasIncomplete = false;
            hasFinal = true;
            finalReceiptStagingPath = Path.Combine(
                layout.CanonicalFinalCopyPath,
                Path.GetFileName(incompleteReceiptStagingPath));
        }

        if (!hasFinal)
        {
            return false;
        }

        bool inventoryAlreadyReplaced = !StringComparer.Ordinal.Equals(
            inventoryContext.CurrentInventory.Sha256,
            inventoryContext.PriorInventory.Sha256);
        string receiptStagingPathToUse;
        if (io.FileExists(finalReceiptStagingPath))
        {
            receiptStagingPathToUse = finalReceiptStagingPath;
        }
        else
        {
            if (!inventoryAlreadyReplaced && io.FileExists(finalReceiptPath))
            {
                throw new AtlasSafetyException("The final copy directory is unusable.");
            }

            receiptStagingPathToUse = finalReceiptPath;
        }

        if (!io.FileExists(receiptStagingPathToUse)
            || !HasCompleteCopySet(
                layout.CanonicalFinalCopyPath,
                copyPlan.Document,
                receiptStagingPathToUse,
                io))
        {
            throw new AtlasSafetyException("The final copy directory is unusable.");
        }

        AtlasLoadedDocument<AtlasCopyReceiptDocument> receipt = await LoadReceiptAsync(
                receiptStagingPathToUse,
                cancellationToken)
            .ConfigureAwait(false);
        ValidateReceiptAgainstBindings(
            loadedRequest.Sha256,
            request,
            approvedState,
            approvedManifest,
            sourceRootMap,
            copyPlan,
            aliases,
            receipt.Document);
        await ValidateCopiedFilesAgainstReceiptAsync(
            layout.CanonicalFinalCopyPath,
            receipt.Document,
            io,
            cancellationToken);

        AtlasPrivateArtifactInventoryDocument replacementInventory = CreateQualifiedInventory(
            inventoryContext.PriorInventory.Document,
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.ApprovedManifestRole).ArtifactAlias,
            copyPlan.Document,
            aliases,
            finalReceiptPath);
        byte[] replacementInventoryBytes =
            AtlasIntakeContracts.SerializeInventory(replacementInventory);
        InventoryReplaceResult inventoryReplace = await AtlasDiscovery.EnsureInventoryReplaceAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalQualifiedInventoryBackupPath,
                AtlasIntakeContracts.QualifiedPhase,
                inventoryContext.PriorInventory.Bytes,
                replacementInventoryBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        if (StringComparer.OrdinalIgnoreCase.Equals(
                receiptStagingPathToUse,
                finalReceiptStagingPath))
        {
            await AtlasDiscovery.MoveValidatedFileAsync(
                    finalReceiptStagingPath,
                    finalReceiptPath,
                    receipt.Sha256,
                    AtlasDiscovery.ReadCopyReceiptShaAsync,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        AtlasIntakeStateDocument state3 = CreateQualifiedState(
            request,
            layout,
            approvedState,
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.ApprovedManifestRole),
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.SourceRootMapRole),
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.CopyPlanRole),
            aliases,
            loadedRequest.Sha256,
            inventoryReplace.BackupSha256,
            inventoryReplace.ReplacementSha256,
            receipt.Sha256);
        byte[] stateBytes = AtlasIntakeContracts.SerializeState(state3);
        _ = await AtlasDiscovery.EnsureDeterministicFileAsync(
                layout.CanonicalQualifiedStatePath,
                AtlasIntakeContracts.QualifiedPhase,
                stateBytes,
                AtlasDiscovery.ReadStateShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    internal static AtlasCopyReceiptDocument CreateCopyReceipt(
        string requestSha256,
        AtlasIntakeCopyRequest request,
        AtlasLoadedDocument<AtlasIntakeStateDocument> approvedState,
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest,
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap,
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan,
        CopyPhaseAliases aliases,
        string gameExecutableSha256,
        IReadOnlyList<AtlasCopyReceiptEntry> receiptEntries) =>
        new()
        {
            SchemaVersion = AtlasIntakeContracts.CopyReceiptSchemaVersion,
            SurveyAlias = request.SurveyAlias,
            ReceiptArtifactAlias = aliases.ReceiptAlias,
            Profile = AtlasIntakeContracts.TrustedLocalFilesystemProfile,
            CopyRequestSha256 = requestSha256,
            ApprovedStateSha256 = approvedState.Sha256,
            ApprovedManifestSha256 = approvedManifest.Sha256,
            SourceRootMapSha256 = sourceRootMap.Sha256,
            CopyPlanSha256 = copyPlan.Sha256,
            DecisionReference = AtlasIntakeContracts.ApprovalDecisionReferencePrefix
                + request.DecisionCommit,
            ApprovedManifestArtifactAlias = AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.ApprovedManifestRole).ArtifactAlias,
            FinalCopyRootRelativePath = AtlasIntakeContracts.SaveSnapshotRelativeRoot,
            SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
            BuildId = AtlasIntakeContracts.ExactBuildId,
            GameExecutableSha256 = gameExecutableSha256,
            SaveCount = receiptEntries.Count(entry =>
                StringComparer.Ordinal.Equals(
                    entry.ArtifactClass,
                    AtlasIntakeContracts.SaveCopyArtifactClass)),
            DefinitionCount = receiptEntries.Count(entry =>
                StringComparer.Ordinal.Equals(
                    entry.ArtifactClass,
                    AtlasIntakeContracts.DefinitionCopyArtifactClass)),
            Entries = [.. receiptEntries.OrderBy(
                static entry => entry.SourceAlias,
                StringComparer.Ordinal)],
        };

    internal static AtlasIntakeStateDocument CreateQualifiedState(
        AtlasIntakeCopyRequest request,
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> approvedState,
        AtlasDocumentBinding approvedManifestBinding,
        AtlasDocumentBinding sourceRootMapBinding,
        AtlasDocumentBinding copyPlanBinding,
        CopyPhaseAliases aliases,
        string requestSha256,
        string backupSha256,
        string inventorySha256,
        string receiptSha256) =>
        new()
        {
            SchemaVersion = AtlasIntakeContracts.IntakeStateSchemaVersion,
            SurveyAlias = request.SurveyAlias,
            StateRevision = AtlasIntakeContracts.QualifiedStateRevision,
            Phase = AtlasIntakeContracts.QualifiedPhase,
            StateArtifactAlias = aliases.StateAlias,
            SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
            BuildId = AtlasIntakeContracts.ExactBuildId,
            InventorySha256 = inventorySha256,
            DecisionCommit = request.DecisionCommit,
            FinalCopyRootRelativePath = AtlasIntakeContracts.SaveSnapshotRelativeRoot,
            DocumentBindings =
            [
                AtlasDiscovery.CreateDocumentBinding(
                    AtlasIntakeContracts.PredecessorStateRole,
                    approvedState.Document.StateArtifactAlias,
                    approvedState.AbsolutePath,
                    layout,
                    approvedState.Sha256),
                approvedManifestBinding,
                sourceRootMapBinding,
                copyPlanBinding,
                new AtlasDocumentBinding
                {
                    Role = AtlasIntakeContracts.CopyReceiptRole,
                    ArtifactAlias = aliases.ReceiptAlias,
                    RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                        layout.WorkspaceRoot,
                        layout.CanonicalCopyReceiptPath),
                    Sha256 = receiptSha256,
                },
            ],
            ArtifactBindings =
            [
                new AtlasArtifactBinding
                {
                    Role = AtlasIntakeContracts.CopyRequestRole,
                    ArtifactAlias = aliases.RequestAlias,
                    RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                        layout.WorkspaceRoot,
                        layout.CanonicalCopyRequestPath),
                    Sha256 = requestSha256,
                },
                new AtlasArtifactBinding
                {
                    Role = AtlasIntakeContracts.QualifiedInventoryBackupRole,
                    ArtifactAlias = aliases.InventoryBackupAlias,
                    RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                        layout.WorkspaceRoot,
                        layout.CanonicalQualifiedInventoryBackupPath),
                    Sha256 = backupSha256,
                },
            ],
        };

    internal static AtlasPrivateArtifactInventoryDocument CreateQualifiedInventory(
        AtlasPrivateArtifactInventoryDocument priorInventory,
        string approvedManifestArtifactAlias,
        AtlasCopyPlanDocument copyPlan,
        CopyPhaseAliases aliases,
        string receiptPath)
    {
        string predecessorStateAlias = TryFindPhaseAlias(
                priorInventory,
                AtlasIntakeContracts.State2Purpose)
            ?? throw new AtlasSafetyException("The approved predecessor state is missing.");
        List<AtlasPrivateArtifactEntry> destinationEntries = [];
        foreach (AtlasCopyPlanEntry entry in copyPlan.Entries)
        {
            bool isSave = StringComparer.Ordinal.Equals(
                entry.ArtifactClass,
                AtlasIntakeContracts.SaveCopyArtifactClass);
            destinationEntries.Add(AtlasDiscovery.CreateArtifactEntry(
                entry.DestinationArtifactAlias,
                entry.ArtifactClass,
                $"snapshot-copy:{entry.SourceAlias}",
                [approvedManifestArtifactAlias],
                isSave ? "A8" : "A6",
                AtlasIntakeContracts.DeleteDisposition,
                $"{AtlasIntakeContracts.TrustedLocalFilesystemProfile};"
                + $"receipt:{aliases.ReceiptAlias}",
                qualification: isSave
                    ? AtlasIntakeContracts.A2QualifiedSaveQualification
                    : null));
        }

        return priorInventory with
        {
            Artifacts =
            [
                .. priorInventory.Artifacts,
                AtlasDiscovery.CreateArtifactEntry(
                    aliases.RequestAlias,
                    AtlasIntakeContracts.PrivateEvidenceArtifactClass,
                    AtlasIntakeContracts.CopyRequestPurpose,
                    [],
                    "A8",
                    AtlasIntakeContracts.DeleteDisposition,
                    "atlas-cli:intake-copy"),
                .. destinationEntries,
                AtlasDiscovery.CreateArtifactEntry(
                    aliases.ReceiptAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.CopyReceiptPurpose,
                    [approvedManifestArtifactAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.CopyReceiptSchemaVersion),
                AtlasDiscovery.CreateArtifactEntry(
                    aliases.StateAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.State3Purpose,
                    [predecessorStateAlias, aliases.ReceiptAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.IntakeStateSchemaVersion),
                AtlasDiscovery.CreateArtifactEntry(
                    aliases.InventoryBackupAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.QualifiedInventoryBackupPurpose,
                    [aliases.RequestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "inventory-backup:qualified"),
            ],
        };
    }

    internal static void ValidateReceiptAgainstBindings(
        string requestSha256,
        AtlasIntakeCopyRequest request,
        AtlasLoadedDocument<AtlasIntakeStateDocument> approvedState,
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest,
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap,
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan,
        CopyPhaseAliases aliases,
        AtlasCopyReceiptDocument receipt)
    {
        AtlasDocumentBinding approvedManifestBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                approvedState.Document,
                AtlasIntakeContracts.ApprovedManifestRole);
        AtlasDiscovery.ValidateSourceRootMapAgainstManifest(
            sourceRootMap.Document,
            approvedManifest.Document);
        AtlasDiscovery.ValidateCopyPlanAgainstManifest(
            copyPlan.Document,
            approvedManifest.Document);
        if (!StringComparer.Ordinal.Equals(
                receipt.SchemaVersion,
                AtlasIntakeContracts.CopyReceiptSchemaVersion)
            || !StringComparer.Ordinal.Equals(receipt.SurveyAlias, request.SurveyAlias)
            || !StringComparer.Ordinal.Equals(
                receipt.SurveyAlias,
                approvedState.Document.SurveyAlias)
            || !StringComparer.Ordinal.Equals(
                receipt.SurveyAlias,
                approvedManifest.Document.SurveyAlias)
            || !StringComparer.Ordinal.Equals(
                receipt.SurveyAlias,
                sourceRootMap.Document.SurveyAlias)
            || !StringComparer.Ordinal.Equals(receipt.SurveyAlias, copyPlan.Document.SurveyAlias)
            || !StringComparer.Ordinal.Equals(
                receipt.Profile,
                AtlasIntakeContracts.TrustedLocalFilesystemProfile)
            || !StringComparer.Ordinal.Equals(receipt.ReceiptArtifactAlias, aliases.ReceiptAlias)
            || !StringComparer.Ordinal.Equals(receipt.CopyRequestSha256, requestSha256)
            || !StringComparer.Ordinal.Equals(receipt.ApprovedStateSha256, approvedState.Sha256)
            || !StringComparer.Ordinal.Equals(
                receipt.ApprovedManifestSha256,
                approvedManifest.Sha256)
            || !StringComparer.Ordinal.Equals(receipt.SourceRootMapSha256, sourceRootMap.Sha256)
            || !StringComparer.Ordinal.Equals(receipt.CopyPlanSha256, copyPlan.Sha256)
            || !StringComparer.Ordinal.Equals(
                receipt.ApprovedManifestArtifactAlias,
                approvedManifestBinding.ArtifactAlias)
            || !StringComparer.Ordinal.Equals(
                receipt.DecisionReference,
                AtlasIntakeContracts.ApprovalDecisionReferencePrefix + request.DecisionCommit)
            || !StringComparer.Ordinal.Equals(
                receipt.FinalCopyRootRelativePath,
                AtlasIntakeContracts.SaveSnapshotRelativeRoot)
            || receipt.SteamAppId != AtlasIntakeContracts.ExactSteamAppId
            || receipt.BuildId != AtlasIntakeContracts.ExactBuildId)
        {
            throw new AtlasSafetyException(
                "The copy receipt does not match the approved bindings.");
        }

        Dictionary<string, AtlasCopyPlanEntry> planEntries = copyPlan.Document.Entries.ToDictionary(
            static entry => entry.SourceAlias,
            StringComparer.Ordinal);
        if (receipt.Entries.Length != planEntries.Count)
        {
            throw new AtlasSafetyException("The copy receipt entry set is incomplete.");
        }

        int saveCount = 0;
        int definitionCount = 0;
        foreach (AtlasCopyReceiptEntry entry in receipt.Entries)
        {
            if (!planEntries.TryGetValue(entry.SourceAlias, out AtlasCopyPlanEntry? planEntry)
                || !StringComparer.Ordinal.Equals(
                    entry.DestinationArtifactAlias,
                    planEntry.DestinationArtifactAlias)
                || !StringComparer.Ordinal.Equals(entry.ArtifactClass, planEntry.ArtifactClass)
                || !StringComparer.Ordinal.Equals(
                    entry.DestinationRelativePath,
                    planEntry.DestinationRelativePath))
            {
                throw new AtlasSafetyException("The copy receipt does not match the copy plan.");
            }

            if (StringComparer.Ordinal.Equals(
                    entry.ArtifactClass,
                    AtlasIntakeContracts.SaveCopyArtifactClass))
            {
                saveCount++;
            }
            else if (StringComparer.Ordinal.Equals(
                entry.ArtifactClass,
                AtlasIntakeContracts.DefinitionCopyArtifactClass))
            {
                definitionCount++;
            }
            else
            {
                throw new AtlasSafetyException("The copy receipt does not match the copy plan.");
            }
        }

        if (receipt.SaveCount != approvedManifest.Document.IncludedSaveCount
            || receipt.DefinitionCount != approvedManifest.Document.IncludedDefinitionCount
            || saveCount != receipt.SaveCount
            || definitionCount != receipt.DefinitionCount)
        {
            throw new AtlasSafetyException("The copy receipt counts are invalid.");
        }
    }

    internal static async ValueTask ValidateCopiedFilesAgainstReceiptAsync(
        string finalCopyPath,
        AtlasCopyReceiptDocument receipt,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        foreach (AtlasCopyReceiptEntry entry in receipt.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destinationPath = Path.Combine(
                finalCopyPath,
                entry.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
            AtlasDiscovery.ValidateExistingOrdinaryFile(destinationPath, io);

            string sha256 = await HashFileAsync(destinationPath, io, cancellationToken)
                .ConfigureAwait(false);
            long length = io.GetLength(destinationPath);
            FileAttributes attributes = io.GetAttributes(destinationPath);
            if (!StringComparer.Ordinal.Equals(entry.SourceSha256, sha256)
                || entry.SourceLength != length
                || (attributes & FileAttributes.ReadOnly) == 0)
            {
                throw new AtlasSafetyException("A staged copy does not validate.");
            }
        }
    }

    internal static bool HasCompleteCopySet(
        string copyRoot,
        AtlasCopyPlanDocument copyPlan,
        string receiptEvidencePath,
        AtlasIoSeams io)
    {
        AtlasDiscovery.ValidateExistingOrdinaryDirectory(copyRoot, io);
        if (!io.FileExists(receiptEvidencePath))
        {
            return false;
        }

        AtlasDiscovery.ValidateExistingOrdinaryFile(receiptEvidencePath, io);
        if (!AtlasDiscovery.ContainsPath(copyRoot, receiptEvidencePath))
        {
            throw new AtlasSafetyException("The receipt path is outside the copy root.");
        }

        HashSet<string> expectedFiles = copyPlan.Entries
            .Select(entry => Path.Combine(
                copyRoot,
                entry.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Append(receiptEvidencePath)
            .Select(AtlasIntakeContracts.NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedDirectories = new(StringComparer.OrdinalIgnoreCase);
        foreach (string expectedFile in expectedFiles)
        {
            string? directory = Path.GetDirectoryName(expectedFile);
            while (!string.IsNullOrEmpty(directory)
                   && !AtlasIntakeContracts.PathEquals(directory, copyRoot))
            {
                expectedDirectories.Add(AtlasIntakeContracts.NormalizePath(directory));
                directory = Path.GetDirectoryName(directory);
            }
        }

        HashSet<string> actualFiles = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> actualDirectories = new(StringComparer.OrdinalIgnoreCase);
        EnumerateRecoveredDirectory(copyRoot);

        return expectedFiles.SetEquals(actualFiles)
            && expectedDirectories.SetEquals(actualDirectories);

        void EnumerateRecoveredDirectory(string directoryPath)
        {
            string[] childEntries;
            try
            {
                childEntries =
                [
                    .. io.EnumerateFileSystemEntries(
                            directoryPath,
                            SearchOption.TopDirectoryOnly)
                        .OrderBy(
                            static path => Path.GetFileName(path),
                            StringComparer.OrdinalIgnoreCase),
                ];
            }
            catch (Exception exception) when (
                exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                throw new AtlasSafetyException("The recovered copy set is inaccessible.");
            }

            foreach (string childEntry in childEntries)
            {
                string normalizedEntry;
                try
                {
                    normalizedEntry = AtlasIntakeContracts.NormalizePath(childEntry);
                }
                catch (Exception exception) when (
                    exception is ArgumentException
                    or IOException
                    or NotSupportedException)
                {
                    throw new AtlasSafetyException("The recovered copy set is ambiguous.");
                }

                if (!AtlasDiscovery.ContainsPath(copyRoot, normalizedEntry)
                    || AtlasIntakeContracts.PathEquals(normalizedEntry, directoryPath))
                {
                    throw new AtlasSafetyException(
                        "The recovered copy set has unexpected content.");
                }

                FileAttributes attributes;
                try
                {
                    attributes = io.GetAttributes(normalizedEntry);
                }
                catch (Exception exception) when (
                    exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
                {
                    throw new AtlasSafetyException("The recovered copy set is inaccessible.");
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new AtlasSafetyException("A recovered copy path is reparse-backed.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!actualDirectories.Add(normalizedEntry))
                    {
                        throw new AtlasSafetyException(
                            "The recovered copy set is ambiguous.");
                    }

                    if (!expectedDirectories.Contains(normalizedEntry))
                    {
                        throw new AtlasSafetyException(
                            "The recovered copy set has unexpected content.");
                    }

                    EnumerateRecoveredDirectory(normalizedEntry);
                    continue;
                }

                if (!actualFiles.Add(normalizedEntry))
                {
                    throw new AtlasSafetyException("The recovered copy set is ambiguous.");
                }

                if (!expectedFiles.Contains(normalizedEntry))
                {
                    throw new AtlasSafetyException(
                        "The recovered copy set has unexpected content.");
                }
            }
        }
    }

    internal static CopyValidationContext ValidateCurrentSourcesAgainstManifest(
        AtlasCorpusIntakeManifest manifest,
        AtlasSourceRootMapDocument sourceRootMap,
        AtlasCopyPlanDocument copyPlan,
        AtlasIoSeams io)
    {
        foreach (AtlasSourceRootBinding saveRoot in sourceRootMap.SaveRoots)
        {
            AtlasDiscovery.ValidateExistingOrdinaryDirectory(saveRoot.AbsolutePath, io);
        }

        AtlasDiscovery.ValidateExistingOrdinaryDirectory(sourceRootMap.DefinitionRootPath, io);
        AtlasDiscovery.DiscoveredManifest discovered = AtlasDiscovery.DiscoverCurrentManifest(
            new AtlasIntakeDiscoveryRequest
            {
                SurveyAlias = manifest.SurveyAlias,
                SaveRoots =
                [
                    .. sourceRootMap.SaveRoots.Select(binding => new AtlasRequestSaveRoot
                    {
                        LocationRole = binding.LocationRole,
                        Path = binding.AbsolutePath,
                    }),
                ],
                DefinitionRoot = sourceRootMap.DefinitionRootPath,
            },
            manifest,
            io);
        if (!StringComparer.Ordinal.Equals(
                discovered.PendingManifest.SchemaVersion,
                manifest.SchemaVersion)
            || discovered.PendingManifest.SaveEntries.Length != manifest.SaveEntries.Length
            || discovered.PendingManifest.DefinitionEntries.Length
                != manifest.DefinitionEntries.Length)
        {
            throw new AtlasSafetyException("The source directories changed.");
        }

        Dictionary<string, AtlasManifestSaveEntry> saveEntries = manifest.SaveEntries.ToDictionary(
            static entry => entry.SourceAlias,
            StringComparer.Ordinal);
        Dictionary<string, AtlasManifestDefinitionEntry> definitionEntries =
            manifest.DefinitionEntries.ToDictionary(
                static entry => entry.SourceAlias,
                StringComparer.Ordinal);
        Dictionary<string, string> saveRootPaths = sourceRootMap.SaveRoots.ToDictionary(
            static binding => binding.RootAlias,
            static binding => binding.AbsolutePath,
            StringComparer.Ordinal);
        List<ResolvedCopySource> includedSources = [];
        foreach (AtlasCopyPlanEntry planEntry in copyPlan.Entries)
        {
            if (StringComparer.Ordinal.Equals(
                    planEntry.ArtifactClass,
                    AtlasIntakeContracts.SaveCopyArtifactClass))
            {
                AtlasManifestSaveEntry manifestEntry = saveEntries[planEntry.SourceAlias];
                string absolutePath = Path.Combine(
                    saveRootPaths[manifestEntry.RootAlias],
                    manifestEntry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                includedSources.Add(new ResolvedCopySource(planEntry, absolutePath));
            }
            else
            {
                AtlasManifestDefinitionEntry manifestEntry =
                    definitionEntries[planEntry.SourceAlias];
                string absolutePath = Path.Combine(
                    sourceRootMap.DefinitionRootPath,
                    manifestEntry.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                includedSources.Add(new ResolvedCopySource(planEntry, absolutePath));
            }
        }

        return new CopyValidationContext(includedSources);
    }

    private static void RequireRecoverableCopyStateBeforeSourceAccess(
        AtlasWorkspaceLayout layout,
        PhaseInventoryContext inventoryContext,
        AtlasIoSeams io)
    {
        if (StringComparer.Ordinal.Equals(
                inventoryContext.CurrentInventory.Sha256,
                inventoryContext.PriorInventory.Sha256))
        {
            return;
        }

        bool hasFinalDirectory = io.DirectoryExists(layout.CanonicalFinalCopyPath);
        bool hasIncompleteDirectory = io.DirectoryExists(layout.CanonicalIncompleteCopyPath);
        if (!hasFinalDirectory || hasIncompleteDirectory)
        {
            throw new AtlasSafetyException(
                "The replaced inventory requires an exact recoverable final copy.");
        }
    }

    internal static async ValueTask<AtlasCopyReceiptEntry> CopySourceFileAsync(
        ResolvedCopySource source,
        string destinationPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        AtlasDiscovery.ValidateExistingOrdinaryFile(source.AbsolutePath, io);
        using Stream sourceStream = io.OpenFile(
            source.AbsolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.SequentialScan);
        long sourceLength = sourceStream.Length;
        DateTimeOffset sourceLastWriteTimeUtc = io.GetLastWriteTimeUtc(source.AbsolutePath);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        long bytesCopied = 0;
        {
            using Stream destinationStream = io.OpenFile(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileOptions.None);

            try
            {
                while (true)
                {
                    int read = await sourceStream.ReadAsync(buffer, cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    bytesCopied += read;
                    hash.AppendData(buffer, 0, read);
                    await destinationStream.WriteAsync(
                            buffer.AsMemory(0, read),
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                await AtlasDiscovery.FlushAsync(destinationStream, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        string sourceSha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        string destinationSha256 = await HashFileAsync(destinationPath, io, cancellationToken)
            .ConfigureAwait(false);
        long destinationLength = io.GetLength(destinationPath);
        DateTimeOffset sourceLastWriteAfter = io.GetLastWriteTimeUtc(source.AbsolutePath);
        if (bytesCopied != sourceLength
            || destinationLength != sourceLength
            || !StringComparer.Ordinal.Equals(sourceSha256, destinationSha256)
            || sourceStream.Length != sourceLength
            || sourceLastWriteAfter != sourceLastWriteTimeUtc)
        {
            throw new AtlasSafetyException("The copied file did not remain stable.");
        }

        io.SetAttributes(
            destinationPath,
            io.GetAttributes(destinationPath) | FileAttributes.ReadOnly);
        return new AtlasCopyReceiptEntry
        {
            DestinationArtifactAlias = source.CopyPlanEntry.DestinationArtifactAlias,
            SourceAlias = source.CopyPlanEntry.SourceAlias,
            ArtifactClass = source.CopyPlanEntry.ArtifactClass,
            DestinationRelativePath = source.CopyPlanEntry.DestinationRelativePath,
            SourceLength = sourceLength,
            SourceLastWriteTimeUtc = sourceLastWriteTimeUtc,
            SourceSha256 = sourceSha256,
        };
    }

    internal static async ValueTask<string> HashTrackedSourceAsync(
        string absolutePath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        AtlasDiscovery.ValidateExistingOrdinaryFile(absolutePath, io);
        using Stream sourceStream = io.OpenFile(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.SequentialScan);
        long sourceLength = sourceStream.Length;
        DateTimeOffset sourceLastWriteTimeUtc = io.GetLastWriteTimeUtc(absolutePath);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        long bytesRead = 0;
        try
        {
            while (true)
            {
                int read = await sourceStream.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytesRead += read;
                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (bytesRead != sourceLength
            || sourceStream.Length != sourceLength
            || io.GetLastWriteTimeUtc(absolutePath) != sourceLastWriteTimeUtc)
        {
            throw new AtlasSafetyException("The tracked source changed while it was hashed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static async ValueTask<string> HashFileAsync(
        string absolutePath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        using Stream stream = io.OpenFile(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static async ValueTask<AtlasLoadedDocument<AtlasCopyReceiptDocument>> LoadReceiptAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return await AtlasIntakeContracts.ReadCopyReceiptAsync(path, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static PhaseInventoryContext ResolvePhaseInventoryFromCurrent(
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> priorInventory) =>
        new(priorInventory, priorInventory);

    internal static async ValueTask<PhaseInventoryContext> LoadPhaseInventoryAsync(
        string inventoryPath,
        string backupPath,
        string expectedPriorSha256,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> currentInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(inventoryPath, cancellationToken)
                .ConfigureAwait(false);
        if (StringComparer.Ordinal.Equals(currentInventory.Sha256, expectedPriorSha256))
        {
            if (io.FileExists(backupPath))
            {
                throw new AtlasSafetyException(
                    "The prior inventory must not retain a phase backup.");
            }

            return new PhaseInventoryContext(currentInventory, currentInventory);
        }

        if (!io.FileExists(backupPath))
        {
            throw new AtlasSafetyException(
                "The inventory digest does not match the expected state.");
        }

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> backupInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(backupPath, cancellationToken)
                .ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(backupInventory.Sha256, expectedPriorSha256))
        {
            throw new AtlasSafetyException("The inventory backup does not match the prior state.");
        }

        return new PhaseInventoryContext(backupInventory, currentInventory);
    }

    internal static CopyPhaseAliases ResolveCopyAliases(
        PhaseInventoryContext inventoryContext,
        AtlasCopyPlanDocument copyPlan)
    {
        string? requestAlias = TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.CopyRequestPurpose);
        string? receiptAlias = TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.CopyReceiptPurpose);
        string? stateAlias = TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.State3Purpose);
        string? backupAlias = TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.QualifiedInventoryBackupPurpose);
        if (requestAlias is not null
            || receiptAlias is not null
            || stateAlias is not null
            || backupAlias is not null
            || !StringComparer.Ordinal.Equals(
                inventoryContext.CurrentInventory.Sha256,
                inventoryContext.PriorInventory.Sha256))
        {
            if (requestAlias is null
                || receiptAlias is null
                || stateAlias is null
                || backupAlias is null)
            {
                throw new AtlasSafetyException("The qualified inventory is incomplete.");
            }

            CopyPhaseAliases aliases = new(requestAlias, receiptAlias, stateAlias, backupAlias);
            ValidateRecoveredCopyAliases(inventoryContext, copyPlan, aliases);
            return aliases;
        }

        return CreateCopyPhaseAliases(GetFirstCopyArtifactOrdinal(inventoryContext, copyPlan));
    }

    private static int GetFirstCopyArtifactOrdinal(
        PhaseInventoryContext inventoryContext,
        AtlasCopyPlanDocument copyPlan) =>
        Math.Max(
                AtlasDiscovery.GetMaximumArtifactOrdinal(inventoryContext.PriorInventory.Document),
                AtlasDiscovery.GetMaximumArtifactOrdinal(copyPlan))
            + 1;

    private static CopyPhaseAliases CreateCopyPhaseAliases(int firstOrdinal) =>
        new(
            AtlasIntakeContracts.FormatArtifactAlias(firstOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(firstOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(firstOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(firstOrdinal));

    private static void ValidateRecoveredCopyAliases(
        PhaseInventoryContext inventoryContext,
        AtlasCopyPlanDocument copyPlan,
        CopyPhaseAliases aliases)
    {
        CopyPhaseAliases expected = CreateCopyPhaseAliases(
            GetFirstCopyArtifactOrdinal(inventoryContext, copyPlan));
        List<RecoveredArtifactExpectation> expectedArtifacts =
        [
            new(
                expected.RequestAlias,
                AtlasIntakeContracts.CopyRequestPurpose,
                AtlasIntakeContracts.PrivateEvidenceArtifactClass),
        ];
        expectedArtifacts.AddRange(copyPlan.Entries.Select(entry =>
            new RecoveredArtifactExpectation(
                entry.DestinationArtifactAlias,
                $"snapshot-copy:{entry.SourceAlias}",
                entry.ArtifactClass)));
        expectedArtifacts.AddRange(
        [
            new(
                expected.ReceiptAlias,
                AtlasIntakeContracts.CopyReceiptPurpose,
                AtlasIntakeContracts.PrivateProvenanceArtifactClass),
            new(
                expected.StateAlias,
                AtlasIntakeContracts.State3Purpose,
                AtlasIntakeContracts.PrivateProvenanceArtifactClass),
            new(
                expected.InventoryBackupAlias,
                AtlasIntakeContracts.QualifiedInventoryBackupPurpose,
                AtlasIntakeContracts.PrivateProvenanceArtifactClass),
        ]);
        ValidateRecoveredPhaseArtifacts(
            inventoryContext,
            expectedArtifacts,
            "The qualified inventory aliases are invalid.");
        if (aliases != expected)
        {
            throw new AtlasSafetyException("The qualified inventory aliases are invalid.");
        }
    }

    internal static void ValidateRecoveredPhaseArtifacts(
        PhaseInventoryContext inventoryContext,
        IReadOnlyList<RecoveredArtifactExpectation> expectedArtifacts,
        string errorMessage)
    {
        AtlasPrivateArtifactEntry[] priorArtifacts =
            inventoryContext.PriorInventory.Document.Artifacts;
        AtlasPrivateArtifactEntry[] currentArtifacts =
            inventoryContext.CurrentInventory.Document.Artifacts;
        if (currentArtifacts.Length != priorArtifacts.Length + expectedArtifacts.Count)
        {
            throw new AtlasSafetyException(errorMessage);
        }

        AtlasPrivateArtifactInventoryDocument recoveredPrefix =
            inventoryContext.CurrentInventory.Document with
            {
                Artifacts = currentArtifacts[..priorArtifacts.Length],
            };
        byte[] recoveredPrefixBytes = AtlasIntakeContracts.SerializeInventory(recoveredPrefix);
        if (!recoveredPrefixBytes.AsSpan().SequenceEqual(inventoryContext.PriorInventory.Bytes))
        {
            throw new AtlasSafetyException(errorMessage);
        }

        for (int index = 0; index < expectedArtifacts.Count; index++)
        {
            AtlasPrivateArtifactEntry actual = currentArtifacts[priorArtifacts.Length + index];
            RecoveredArtifactExpectation expected = expectedArtifacts[index];
            if (!StringComparer.Ordinal.Equals(
                    actual.ArtifactAlias,
                    expected.ArtifactAlias)
                || !StringComparer.Ordinal.Equals(actual.Purpose, expected.Purpose)
                || !StringComparer.Ordinal.Equals(
                    actual.ArtifactClass,
                    expected.ArtifactClass))
            {
                throw new AtlasSafetyException(errorMessage);
            }
        }
    }

    internal static string? TryFindPhaseAlias(
        AtlasPrivateArtifactInventoryDocument inventory,
        string purpose)
    {
        string[] matches = inventory.Artifacts
            .Where(artifact => StringComparer.Ordinal.Equals(artifact.Purpose, purpose))
            .Select(static artifact => artifact.ArtifactAlias)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new AtlasSafetyException("The phase alias is ambiguous."),
        };
    }

    private static bool IsIncompleteEvidenceIoFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or NotSupportedException;

    internal static void DeleteOwnedIncompleteDirectory(
        string path,
        AtlasCopyPlanDocument copyPlan,
        AtlasIoSeams io)
    {
        OwnedPartialCopyContent content = InspectOwnedPartialCopyContent(path, copyPlan, io);
        foreach (string file in content.Files)
        {
            io.SetAttributes(file, io.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }

        foreach (string directory in content.Directories
                     .OrderByDescending(static value => value.Length))
        {
            io.SetAttributes(directory, io.GetAttributes(directory) & ~FileAttributes.ReadOnly);
        }

        io.SetAttributes(path, io.GetAttributes(path) & ~FileAttributes.ReadOnly);
        io.DeleteDirectory(path, true);
    }

    private static OwnedPartialCopyContent InspectOwnedPartialCopyContent(
        string copyRoot,
        AtlasCopyPlanDocument copyPlan,
        AtlasIoSeams io)
    {
        AtlasDiscovery.ValidateExistingOrdinaryDirectory(copyRoot, io);
        HashSet<string> expectedFiles = copyPlan.Entries
            .Select(entry => Path.Combine(
                copyRoot,
                entry.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Select(AtlasIntakeContracts.NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expectedDirectories = new(StringComparer.OrdinalIgnoreCase);
        foreach (string expectedFile in expectedFiles)
        {
            string? directory = Path.GetDirectoryName(expectedFile);
            while (!string.IsNullOrEmpty(directory)
                   && !AtlasIntakeContracts.PathEquals(directory, copyRoot))
            {
                expectedDirectories.Add(AtlasIntakeContracts.NormalizePath(directory));
                directory = Path.GetDirectoryName(directory);
            }
        }

        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
        InspectDirectory(copyRoot);
        return new OwnedPartialCopyContent([.. files], [.. directories]);

        void InspectDirectory(string directoryPath)
        {
            foreach (string childEntry in io.EnumerateFileSystemEntries(
                         directoryPath,
                         SearchOption.TopDirectoryOnly))
            {
                string normalizedEntry = AtlasIntakeContracts.NormalizePath(childEntry);
                if (!AtlasDiscovery.ContainsPath(copyRoot, normalizedEntry)
                    || AtlasIntakeContracts.PathEquals(normalizedEntry, directoryPath))
                {
                    throw new AtlasSafetyException(
                        "The partial copy set has ambiguous content.");
                }

                FileAttributes attributes = io.GetAttributes(normalizedEntry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new AtlasSafetyException("A partial copy path is reparse-backed.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!expectedDirectories.Contains(normalizedEntry)
                        || !directories.Add(normalizedEntry))
                    {
                        throw new AtlasSafetyException(
                            "The partial copy set has ambiguous content.");
                    }

                    InspectDirectory(normalizedEntry);
                    continue;
                }

                if (!expectedFiles.Contains(normalizedEntry)
                    || !files.Add(normalizedEntry))
                {
                    throw new AtlasSafetyException(
                        "The partial copy set has ambiguous content.");
                }
            }
        }
    }
}

internal sealed record ResolvedCopySource(AtlasCopyPlanEntry CopyPlanEntry, string AbsolutePath);

internal sealed record CopyValidationContext(IReadOnlyList<ResolvedCopySource> IncludedSources);

internal sealed record CopyPhaseAliases(
    string RequestAlias,
    string ReceiptAlias,
    string StateAlias,
    string InventoryBackupAlias);

internal sealed record RecoveredArtifactExpectation(
    string ArtifactAlias,
    string Purpose,
    string ArtifactClass);

internal sealed record OwnedPartialCopyContent(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Directories);

internal enum IncompleteCopyEvidenceState
{
    PartialOwned,
    Complete,
    Recoverable,
    Canceled,
    IoIndeterminate,
}

internal sealed record PhaseInventoryContext(
    AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> PriorInventory,
    AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> CurrentInventory);
