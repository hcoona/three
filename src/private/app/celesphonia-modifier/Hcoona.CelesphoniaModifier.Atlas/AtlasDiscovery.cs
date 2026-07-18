using System.Security.Cryptography;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasDiscovery
{
    public static ValueTask DiscoverAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        DiscoverAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    public static ValueTask ConfirmAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        ConfirmAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    internal static async ValueTask DiscoverAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);

        AtlasLoadedDocument<AtlasIntakeDiscoveryRequest> loadedRequest =
            await AtlasIntakeContracts.ReadDiscoveryRequestAsync(
                    requestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasIntakeDiscoveryRequest request = loadedRequest.Document;
        AtlasWorkspaceLayout layout = AtlasIntakeContracts.CreateWorkspaceLayout(
            request.ProjectRoot,
            request.WorkspaceRoot,
            request.SurveyAlias);
        ValidatePrivateWorkspace(layout, io);
        ValidateDiscoveryCanonicalPaths(loadedRequest.AbsolutePath, request, layout, io);

        if (await TryReturnCompletedPhaseAsync(
                layout.CanonicalDiscoveredStatePath,
                AtlasIntakeContracts.DiscoveredStateRevision,
                io,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        if (await TryReturnCompletedPhaseAsync(
                layout.CanonicalApprovedStatePath,
                AtlasIntakeContracts.ApprovedStateRevision,
                io,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        if (await TryReturnCompletedPhaseAsync(
                layout.CanonicalQualifiedStatePath,
                AtlasIntakeContracts.QualifiedStateRevision,
                io,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        if (await TryReturnCompletedPhaseAsync(
                layout.CanonicalPreflightedStatePath,
                AtlasIntakeContracts.PreflightedStateRevision,
                io,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> baselineManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                    request.BaselineManifestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(
                baselineManifest.Document.Confirmation.Status,
                AtlasIntakeContracts.ApprovedConfirmationStatus))
        {
            throw new AtlasApprovalException("The baseline manifest is not approved.");
        }

        EnsureDigestMatches(
            request.ExpectedBaselineSha256,
            baselineManifest.Sha256,
            static () => new AtlasApprovalException("The baseline manifest digest is invalid."));
        if (baselineManifest.Document.ManifestRevision
            != AtlasIntakeContracts.BaselineManifestRevision)
        {
            throw new AtlasApprovalException("The baseline manifest revision is invalid.");
        }

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                    request.InventoryPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureDigestMatches(
            request.ExpectedInventorySha256,
            inventory.Sha256,
            static () => new AtlasSafetyException("The inventory digest is invalid."));

        string baselineManifestAlias = FindManifestArtifactAlias(
            inventory.Document,
            AtlasIntakeContracts.ManifestRevision3Purpose);
        DiscoveredManifest discovered = DiscoverCurrentManifest(
            request,
            baselineManifest.Document,
            io);

        int nextArtifactOrdinal = GetMaximumArtifactOrdinal(inventory.Document) + 1;
        string requestAlias = AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);
        string manifestRevision4Alias =
            AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);
        string sourceRootMapAlias =
            AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);
        string copyPlanAlias =
            AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);
        string state1Alias =
            AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);
        string inventoryBackupAlias =
            AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);

        AtlasCorpusIntakeManifest pendingManifest = discovered.PendingManifest with
        {
            ManifestRevision = AtlasIntakeContracts.PendingManifestRevision,
            Validation = discovered.PendingManifest.Validation with
            {
                Method = AtlasIntakeContracts.AtlasToolValidationMethod,
            },
            Confirmation = new AtlasManifestConfirmation
            {
                Status = AtlasIntakeContracts.PendingConfirmationStatus,
            },
        };
        byte[] pendingManifestBytes = AtlasIntakeContracts.SerializeManifest(pendingManifest);
        PublishedFile pendingManifestFile = await EnsureDeterministicFileAsync(
                layout.CanonicalPendingManifestPath,
                AtlasIntakeContracts.DiscoveredPhase,
                pendingManifestBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasSourceRootMapDocument sourceRootMap = CreateSourceRootMap(request, discovered);
        byte[] sourceRootMapBytes = AtlasIntakeContracts.SerializeSourceRootMap(sourceRootMap);
        PublishedFile sourceRootMapFile = await EnsureDeterministicFileAsync(
                layout.CanonicalSourceRootMapPath,
                AtlasIntakeContracts.DiscoveredPhase,
                sourceRootMapBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasCopyPlanDocument copyPlan = CreateCopyPlan(discovered, nextArtifactOrdinal);
        byte[] copyPlanBytes = AtlasIntakeContracts.SerializeCopyPlan(copyPlan);
        PublishedFile copyPlanFile = await EnsureDeterministicFileAsync(
                layout.CanonicalCopyPlanPath,
                AtlasIntakeContracts.DiscoveredPhase,
                copyPlanBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasPrivateArtifactInventoryDocument replacementInventory = inventory.Document with
        {
            Artifacts =
            [
                .. inventory.Document.Artifacts,
                CreateArtifactEntry(
                    requestAlias,
                    AtlasIntakeContracts.PrivateEvidenceArtifactClass,
                    AtlasIntakeContracts.DiscoverRequestPurpose,
                    [],
                    "A8",
                    AtlasIntakeContracts.DeleteDisposition,
                    "atlas-cli:intake-discover"),
                CreateArtifactEntry(
                    manifestRevision4Alias,
                    AtlasIntakeContracts.LiveDiscoveryArtifactClass,
                    AtlasIntakeContracts.ManifestRevision4Purpose,
                    [baselineManifestAlias],
                    "A2",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "atlas-intake/v2;r000004"),
                CreateArtifactEntry(
                    sourceRootMapAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.SourceRootMapPurpose,
                    [manifestRevision4Alias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.SourceRootMapSchemaVersion),
                CreateArtifactEntry(
                    copyPlanAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.CopyPlanPurpose,
                    [manifestRevision4Alias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.CopyPlanSchemaVersion),
                CreateArtifactEntry(
                    state1Alias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.State1Purpose,
                    [manifestRevision4Alias, sourceRootMapAlias, copyPlanAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.IntakeStateSchemaVersion),
                CreateArtifactEntry(
                    inventoryBackupAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.DiscoveryInventoryBackupPurpose,
                    [requestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "inventory-backup:discovered"),
            ],
        };
        byte[] replacementInventoryBytes =
            AtlasIntakeContracts.SerializeInventory(replacementInventory);
        InventoryReplaceResult inventoryReplace = await EnsureInventoryReplaceAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalDiscoveredInventoryBackupPath,
                AtlasIntakeContracts.DiscoveredPhase,
                inventory.Bytes,
                replacementInventoryBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasIntakeStateDocument state1 = new()
        {
            SchemaVersion = AtlasIntakeContracts.IntakeStateSchemaVersion,
            SurveyAlias = request.SurveyAlias,
            StateRevision = AtlasIntakeContracts.DiscoveredStateRevision,
            Phase = AtlasIntakeContracts.DiscoveredPhase,
            StateArtifactAlias = state1Alias,
            SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
            BuildId = AtlasIntakeContracts.ExactBuildId,
            InventorySha256 = inventoryReplace.ReplacementSha256,
            DocumentBindings =
            [
                CreateDocumentBinding(
                    AtlasIntakeContracts.BaselineManifestRole,
                    baselineManifestAlias,
                    layout.CanonicalBaselineManifestPath,
                    layout),
                CreateDocumentBinding(
                    AtlasIntakeContracts.PendingManifestRole,
                    manifestRevision4Alias,
                    pendingManifestFile.FinalPath,
                    layout,
                    pendingManifestFile.Sha256),
                CreateDocumentBinding(
                    AtlasIntakeContracts.SourceRootMapRole,
                    sourceRootMapAlias,
                    sourceRootMapFile.FinalPath,
                    layout,
                    sourceRootMapFile.Sha256),
                CreateDocumentBinding(
                    AtlasIntakeContracts.CopyPlanRole,
                    copyPlanAlias,
                    copyPlanFile.FinalPath,
                    layout,
                    copyPlanFile.Sha256),
            ],
            ArtifactBindings =
            [
                CreateArtifactBinding(
                    AtlasIntakeContracts.DiscoveredRequestRole,
                    requestAlias,
                    loadedRequest.AbsolutePath,
                    layout,
                    loadedRequest.Sha256),
                CreateArtifactBinding(
                    AtlasIntakeContracts.DiscoveredInventoryBackupRole,
                    inventoryBackupAlias,
                    inventoryReplace.BackupPath,
                    layout,
                    inventoryReplace.BackupSha256),
            ],
        };
        byte[] stateBytes = AtlasIntakeContracts.SerializeState(state1);
        _ = await EnsureDeterministicFileAsync(
                layout.CanonicalDiscoveredStatePath,
                AtlasIntakeContracts.DiscoveredPhase,
                stateBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask ConfirmAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);

        AtlasLoadedDocument<AtlasIntakeConfirmationRequest> loadedRequest =
            await AtlasIntakeContracts.ReadConfirmationRequestAsync(
                    requestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasIntakeConfirmationRequest request = loadedRequest.Document;
        AtlasWorkspaceLayout layout = AtlasIntakeContracts.CreateWorkspaceLayout(
            request.ProjectRoot,
            request.WorkspaceRoot,
            request.SurveyAlias);
        ValidatePrivateWorkspace(layout, io);
        ValidateConfirmationCanonicalPaths(loadedRequest.AbsolutePath, request, layout, io);

        if (await TryReturnCompletedPhaseAsync(
                layout.CanonicalApprovedStatePath,
                AtlasIntakeContracts.ApprovedStateRevision,
                io,
                cancellationToken)
            .ConfigureAwait(false)
            || await TryReturnCompletedPhaseAsync(
                layout.CanonicalQualifiedStatePath,
                AtlasIntakeContracts.QualifiedStateRevision,
                io,
                cancellationToken).ConfigureAwait(false)
            || await TryReturnCompletedPhaseAsync(
                layout.CanonicalPreflightedStatePath,
                AtlasIntakeContracts.PreflightedStateRevision,
                io,
                cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!io.FileExists(layout.CanonicalDiscoveredStatePath))
        {
            throw new AtlasApprovalException("The discovered state is required.");
        }

        AtlasLoadedDocument<AtlasIntakeStateDocument> discoveredState =
            await AtlasIntakeContracts.ReadStateAsync(
                    request.DiscoveredStatePath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureDigestMatches(
            request.ExpectedDiscoveredStateSha256,
            discoveredState.Sha256,
            static () => new AtlasApprovalException("The discovered state digest is invalid."));
        if (discoveredState.Document.StateRevision != AtlasIntakeContracts.DiscoveredStateRevision)
        {
            throw new AtlasApprovalException("The discovered state revision is invalid.");
        }

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> inventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                    request.InventoryPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureDigestMatches(
            request.ExpectedInventorySha256,
            inventory.Sha256,
            static () => new AtlasSafetyException("The inventory digest is invalid."));
        EnsureDigestMatches(
            discoveredState.Document.InventorySha256,
            inventory.Sha256,
            static () => new AtlasSafetyException("The discovered inventory digest is invalid."));

        AtlasDocumentBinding pendingManifestBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                discoveredState.Document,
                AtlasIntakeContracts.PendingManifestRole);
        AtlasDocumentBinding sourceRootMapBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                discoveredState.Document,
                AtlasIntakeContracts.SourceRootMapRole);
        AtlasDocumentBinding copyPlanBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                discoveredState.Document,
                AtlasIntakeContracts.CopyPlanRole);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> pendingManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                    request.PendingManifestPath,
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

        EnsureStateDocumentMatchesBinding(pendingManifest, pendingManifestBinding);
        EnsureStateDocumentMatchesBinding(sourceRootMap, sourceRootMapBinding);
        EnsureStateDocumentMatchesBinding(copyPlan, copyPlanBinding);

        if (pendingManifest.Document.ManifestRevision
                != AtlasIntakeContracts.PendingManifestRevision
            || !StringComparer.Ordinal.Equals(
                pendingManifest.Document.Confirmation.Status,
                AtlasIntakeContracts.PendingConfirmationStatus))
        {
            throw new AtlasApprovalException("The pending manifest is invalid.");
        }

        int nextArtifactOrdinal = Math.Max(
            GetMaximumArtifactOrdinal(inventory.Document),
            GetMaximumArtifactOrdinal(copyPlan.Document))
            + 1;
        string requestAlias = AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);
        string manifestRevision5Alias =
            AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);
        string state2Alias =
            AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);
        string inventoryBackupAlias =
            AtlasIntakeContracts.FormatArtifactAlias(nextArtifactOrdinal++);

        AtlasCorpusIntakeManifest approvedManifest = pendingManifest.Document with
        {
            ManifestRevision = AtlasIntakeContracts.ApprovedManifestRevision,
            Confirmation = new AtlasManifestConfirmation
            {
                Status = AtlasIntakeContracts.ApprovedConfirmationStatus,
                ConfirmedByRole = AtlasIntakeContracts.ProjectLeaderRole,
                DecisionReference = AtlasIntakeContracts.ApprovalDecisionReferencePrefix
                    + request.DecisionCommit,
            },
        };
        byte[] approvedManifestBytes = AtlasIntakeContracts.SerializeManifest(approvedManifest);
        PublishedFile approvedManifestFile = await EnsureDeterministicFileAsync(
                layout.CanonicalApprovedManifestPath,
                AtlasIntakeContracts.ApprovedPhase,
                approvedManifestBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasPrivateArtifactInventoryDocument replacementInventory = inventory.Document with
        {
            Artifacts =
            [
                .. inventory.Document.Artifacts,
                CreateArtifactEntry(
                    requestAlias,
                    AtlasIntakeContracts.PrivateEvidenceArtifactClass,
                    AtlasIntakeContracts.ConfirmRequestPurpose,
                    [],
                    "A8",
                    AtlasIntakeContracts.DeleteDisposition,
                    "atlas-cli:intake-confirm"),
                CreateArtifactEntry(
                    manifestRevision5Alias,
                    AtlasIntakeContracts.LiveDiscoveryArtifactClass,
                    AtlasIntakeContracts.ManifestRevision5Purpose,
                    [pendingManifestBinding.ArtifactAlias],
                    "A2",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "atlas-intake/v2;r000005"),
                CreateArtifactEntry(
                    state2Alias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.State2Purpose,
                    [discoveredState.Document.StateArtifactAlias, manifestRevision5Alias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.IntakeStateSchemaVersion),
                CreateArtifactEntry(
                    inventoryBackupAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.ApprovedInventoryBackupPurpose,
                    [requestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "inventory-backup:approved"),
            ],
        };
        byte[] replacementInventoryBytes =
            AtlasIntakeContracts.SerializeInventory(replacementInventory);
        InventoryReplaceResult inventoryReplace = await EnsureInventoryReplaceAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalApprovedInventoryBackupPath,
                AtlasIntakeContracts.ApprovedPhase,
                inventory.Bytes,
                replacementInventoryBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasIntakeStateDocument state2 = new()
        {
            SchemaVersion = AtlasIntakeContracts.IntakeStateSchemaVersion,
            SurveyAlias = request.SurveyAlias,
            StateRevision = AtlasIntakeContracts.ApprovedStateRevision,
            Phase = AtlasIntakeContracts.ApprovedPhase,
            StateArtifactAlias = state2Alias,
            SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
            BuildId = AtlasIntakeContracts.ExactBuildId,
            InventorySha256 = inventoryReplace.ReplacementSha256,
            DecisionCommit = request.DecisionCommit,
            DocumentBindings =
            [
                CreateDocumentBinding(
                    AtlasIntakeContracts.PredecessorStateRole,
                    discoveredState.Document.StateArtifactAlias,
                    discoveredState.AbsolutePath,
                    layout,
                    discoveredState.Sha256),
                CreateDocumentBinding(
                    AtlasIntakeContracts.ApprovedManifestRole,
                    manifestRevision5Alias,
                    approvedManifestFile.FinalPath,
                    layout,
                    approvedManifestFile.Sha256),
                sourceRootMapBinding,
                copyPlanBinding,
            ],
            ArtifactBindings =
            [
                CreateArtifactBinding(
                    AtlasIntakeContracts.ConfirmRequestRole,
                    requestAlias,
                    loadedRequest.AbsolutePath,
                    layout,
                    loadedRequest.Sha256),
                CreateArtifactBinding(
                    AtlasIntakeContracts.ApprovedInventoryBackupRole,
                    inventoryBackupAlias,
                    inventoryReplace.BackupPath,
                    layout,
                    inventoryReplace.BackupSha256),
            ],
        };
        byte[] stateBytes = AtlasIntakeContracts.SerializeState(state2);
        _ = await EnsureDeterministicFileAsync(
                layout.CanonicalApprovedStatePath,
                AtlasIntakeContracts.ApprovedPhase,
                stateBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static void ValidatePrivateWorkspace(AtlasWorkspaceLayout layout, AtlasIoSeams io)
    {
        ValidateExistingOrdinaryFile(layout.PrivateGitIgnorePath, io);
        string[] lines = io.ReadAllText(layout.PrivateGitIgnorePath)
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!lines.Contains("*", StringComparer.Ordinal)
            || !lines.Contains("!.gitignore", StringComparer.Ordinal))
        {
            throw new AtlasSafetyException("The .private .gitignore rules are invalid.");
        }
    }

    internal static async ValueTask<bool> TryReturnCompletedPhaseAsync(
        string statePath,
        int expectedRevision,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        if (!io.FileExists(statePath))
        {
            return false;
        }

        AtlasLoadedDocument<AtlasIntakeStateDocument> state =
            await AtlasIntakeContracts.ReadStateAsync(
                    statePath,
                    cancellationToken)
                .ConfigureAwait(false);
        if (state.Document.StateRevision != expectedRevision)
        {
            return false;
        }

        return true;
    }

    internal static void ValidateDiscoveryCanonicalPaths(
        string requestPath,
        AtlasIntakeDiscoveryRequest request,
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io)
    {
        ValidateCanonicalRequestFile(
            requestPath,
            layout.CanonicalDiscoverRequestPath,
            layout.WorkspaceRoot,
            io);
        ValidateExistingOrdinaryFile(request.BaselineManifestPath, io);
        ValidateExistingOrdinaryFile(request.InventoryPath, io);
        ValidateExistingOrdinaryDirectory(request.SaveRoots[0].Path, io);
        ValidateExistingOrdinaryDirectory(request.SaveRoots[1].Path, io);
        ValidateExistingOrdinaryDirectory(request.DefinitionRoot, io);
        ValidateExistingOrdinaryFile(request.GameExecutablePath, io);
        ValidateCreateNewOutputDirectory(
            layout.ManifestRevisionDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputDirectory(
            layout.RequestDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputDirectory(
            layout.StatesDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputDirectory(
            layout.InventoryBackupsDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputFile(
            request.SourceRootMapOutputPath,
            layout.CanonicalSourceRootMapPath,
            layout.WorkspaceRoot,
            io,
            allowExistingOutput: true);
        ValidateCreateNewOutputFile(
            request.CopyPlanOutputPath,
            layout.CanonicalCopyPlanPath,
            layout.WorkspaceRoot,
            io,
            allowExistingOutput: true);
        ValidateCreateNewOutputFile(
            request.InventoryBackupPath,
            layout.CanonicalDiscoveredInventoryBackupPath,
            layout.WorkspaceRoot,
            io,
            allowExistingOutput: true);
        ValidateCreateNewOutputFile(
            request.BaselineManifestPath,
            layout.CanonicalBaselineManifestPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        ValidateCreateNewOutputFile(
            request.InventoryPath,
            layout.CanonicalInventoryPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        ValidateCreateNewOutputDirectory(
            request.ManifestRevisionDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputDirectory(
            request.StateRevisionDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputFile(
            request.BaselineManifestPath,
            layout.CanonicalBaselineManifestPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);

        ValidateSourceOutsideWorkspace(layout.WorkspaceRoot, request.SaveRoots[0].Path);
        ValidateSourceOutsideWorkspace(layout.WorkspaceRoot, request.SaveRoots[1].Path);
        ValidateSourceOutsideWorkspace(layout.WorkspaceRoot, request.DefinitionRoot);
        AtlasIntakeContracts.AssertContainsPath(request.DefinitionRoot, request.GameExecutablePath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(request.GameExecutablePath),
                "Game.exe"))
        {
            throw new AtlasSafetyException("The game executable path is invalid.");
        }
    }

    internal static void ValidateConfirmationCanonicalPaths(
        string requestPath,
        AtlasIntakeConfirmationRequest request,
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io)
    {
        ValidateCanonicalRequestFile(
            requestPath,
            layout.CanonicalConfirmRequestPath,
            layout.WorkspaceRoot,
            io);
        ValidateExistingOrdinaryFile(request.DiscoveredStatePath, io);
        ValidateExistingOrdinaryFile(request.PendingManifestPath, io);
        ValidateExistingOrdinaryFile(request.SourceRootMapPath, io);
        ValidateExistingOrdinaryFile(request.CopyPlanPath, io);
        ValidateExistingOrdinaryFile(request.InventoryPath, io);
        ValidateCreateNewOutputDirectory(
            request.ManifestRevisionDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputDirectory(
            request.StateRevisionDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputDirectory(
            layout.InventoryBackupsDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCreateNewOutputFile(
            request.DiscoveredStatePath,
            layout.CanonicalDiscoveredStatePath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        ValidateCreateNewOutputFile(
            request.PendingManifestPath,
            layout.CanonicalPendingManifestPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        ValidateCreateNewOutputFile(
            request.SourceRootMapPath,
            layout.CanonicalSourceRootMapPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        ValidateCreateNewOutputFile(
            request.CopyPlanPath,
            layout.CanonicalCopyPlanPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        ValidateCreateNewOutputFile(
            request.InventoryPath,
            layout.CanonicalInventoryPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        ValidateCreateNewOutputFile(
            request.InventoryBackupPath,
            layout.CanonicalApprovedInventoryBackupPath,
            layout.WorkspaceRoot,
            io,
            allowExistingOutput: true);
    }

    internal static void ValidateCanonicalRequestFile(
        string actualRequestPath,
        string expectedPath,
        string workspaceRoot,
        AtlasIoSeams io)
    {
        ValidateExistingOrdinaryFile(actualRequestPath, io);
        AtlasIntakeContracts.AssertContainsPath(workspaceRoot, actualRequestPath);
        if (!AtlasIntakeContracts.PathEquals(actualRequestPath, expectedPath))
        {
            throw new AtlasSafetyException("The request path is not canonical.");
        }
    }

    internal static void ValidateExistingOrdinaryDirectory(string path, AtlasIoSeams io)
    {
        ValidatePathComponents(
            path,
            io,
            allowMissingLeaf: false,
            requireFileLeaf: false,
            requireDirectoryLeaf: true);
    }

    internal static void ValidateExistingOrdinaryFile(string path, AtlasIoSeams io)
    {
        ValidatePathComponents(
            path,
            io,
            allowMissingLeaf: false,
            requireFileLeaf: true,
            requireDirectoryLeaf: false);
    }

    internal static void ValidateCreateNewOutputDirectory(
        string path,
        string workspaceRoot,
        AtlasIoSeams io)
    {
        ValidatePathComponents(
            path,
            io,
            allowMissingLeaf: true,
            requireFileLeaf: false,
            requireDirectoryLeaf: false);
        AtlasIntakeContracts.AssertContainsPath(workspaceRoot, path);
        EnsureFixedDrive(path, io);
    }

    internal static void ValidateCreateNewOutputFile(
        string actualPath,
        string expectedPath,
        string workspaceRoot,
        AtlasIoSeams io,
        bool requireExisting = false,
        bool allowExistingOutput = false)
    {
        if (!AtlasIntakeContracts.PathEquals(actualPath, expectedPath))
        {
            throw new AtlasSafetyException("The canonical path is invalid.");
        }

        ValidatePathComponents(
            actualPath,
            io,
            allowMissingLeaf: !requireExisting,
            requireFileLeaf: requireExisting,
            requireDirectoryLeaf: false);
        AtlasIntakeContracts.AssertContainsPath(workspaceRoot, actualPath);
        EnsureFixedDrive(actualPath, io);
        if (!requireExisting
            && !allowExistingOutput
            && (io.FileExists(actualPath) || io.DirectoryExists(actualPath)))
        {
            throw new AtlasSafetyException("The create-new output already exists.");
        }
    }

    internal static void ValidateSourceOutsideWorkspace(string workspaceRoot, string sourceRoot)
    {
        if (IsContainedInEitherDirection(workspaceRoot, sourceRoot))
        {
            throw new AtlasSafetyException("The workspace and source roots must be disjoint.");
        }
    }

    internal static bool IsContainedInEitherDirection(string first, string second)
    {
        return ContainsPath(first, second) || ContainsPath(second, first);
    }

    internal static bool ContainsPath(string root, string candidate)
    {
        string normalizedRoot = AtlasIntakeContracts.NormalizePath(root)
            .TrimEnd(Path.DirectorySeparatorChar, '/');
        string normalizedCandidate = AtlasIntakeContracts.NormalizePath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, '/');
        if (StringComparer.OrdinalIgnoreCase.Equals(normalizedRoot, normalizedCandidate))
        {
            return true;
        }

        string prefix = AtlasIntakeContracts.AppendDirectorySeparator(normalizedRoot);
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static void ValidatePathComponents(
        string path,
        AtlasIoSeams io,
        bool allowMissingLeaf,
        bool requireFileLeaf,
        bool requireDirectoryLeaf)
    {
        AtlasIntakeContracts.ValidateAbsoluteDosPath(path, nameof(path));
        EnsureFixedDrive(path, io);

        string normalizedPath = AtlasIntakeContracts.NormalizePath(path);
        string root = Path.GetPathRoot(normalizedPath)
            ?? throw new AtlasSafetyException("The path root is missing.");
        List<string> segments = [];
        string relative = normalizedPath[root.Length..];
        if (relative.Length > 0)
        {
            segments.AddRange(relative.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries));
        }

        string current = root;
        ValidateExistingComponent(current, io, expectDirectory: true);
        bool missingSeen = false;
        for (int index = 0; index < segments.Count; index++)
        {
            string segment = segments[index];
            string next = Path.Combine(current, segment);
            bool exists = io.FileExists(next) || io.DirectoryExists(next);
            bool isLast = index == segments.Count - 1;
            if (!exists)
            {
                if (!isLast || !allowMissingLeaf)
                {
                    missingSeen = true;
                }

                if (!allowMissingLeaf && !isLast)
                {
                    throw new AtlasSafetyException("A required path component is missing.");
                }

                if (!isLast && !allowMissingLeaf)
                {
                    throw new AtlasSafetyException("A required path component is missing.");
                }

                if (!isLast)
                {
                    current = next;
                    continue;
                }

                if (!allowMissingLeaf)
                {
                    throw new AtlasSafetyException("The required path does not exist.");
                }

                current = next;
                continue;
            }

            if (missingSeen)
            {
                throw new AtlasSafetyException("A missing component precedes an existing path.");
            }

            bool expectDirectory = !isLast
                || requireDirectoryLeaf
                || (!requireFileLeaf && !requireDirectoryLeaf && io.DirectoryExists(next));
            ValidateExistingComponent(next, io, expectDirectory);
            current = next;
        }

        if (requireFileLeaf && !io.FileExists(normalizedPath))
        {
            throw new AtlasSafetyException("The file path is invalid.");
        }

        if (requireDirectoryLeaf && !io.DirectoryExists(normalizedPath))
        {
            throw new AtlasSafetyException("The directory path is invalid.");
        }
    }

    internal static void ValidateExistingComponent(
        string path,
        AtlasIoSeams io,
        bool expectDirectory)
    {
        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new AtlasSafetyException("Reparse points are not allowed.");
        }

        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (expectDirectory && !isDirectory)
        {
            throw new AtlasSafetyException("A directory component is a file.");
        }

        if (!expectDirectory && isDirectory)
        {
            throw new AtlasSafetyException("The file component is a directory.");
        }

        if ((attributes & FileAttributes.Device) != 0)
        {
            throw new AtlasSafetyException("Device paths are not allowed.");
        }
    }

    internal static void EnsureFixedDrive(string path, AtlasIoSeams io)
    {
        string root = Path.GetPathRoot(AtlasIntakeContracts.NormalizePath(path))
            ?? throw new AtlasSafetyException("The path root is invalid.");
        AtlasDriveInfo drive = io.GetDriveInfo(root);
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
        {
            throw new AtlasSafetyException("The path must use a ready fixed drive.");
        }
    }

    internal static AtlasSourceRootMapDocument CreateSourceRootMap(
        AtlasIntakeDiscoveryRequest request,
        DiscoveredManifest manifest) =>
        new()
        {
            SchemaVersion = AtlasIntakeContracts.SourceRootMapSchemaVersion,
            SurveyAlias = request.SurveyAlias,
            ManifestRevision = AtlasIntakeContracts.PendingManifestRevision,
            SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
            BuildId = AtlasIntakeContracts.ExactBuildId,
            SaveRoots =
            [
                .. manifest.PendingManifest.SaveRoots
                    .OrderBy(static root => root.LocationRole, StringComparer.Ordinal)
                    .Select(root => new AtlasSourceRootBinding
                    {
                        RootAlias = root.RootAlias,
                        LocationRole = root.LocationRole,
                        AbsolutePath = manifest.SaveRootPaths[root.RootAlias],
                    }),
            ],
            DefinitionRootPath = request.DefinitionRoot,
            GameExecutablePath = request.GameExecutablePath,
        };

    internal static AtlasCopyPlanDocument CreateCopyPlan(
        DiscoveredManifest manifest,
        int firstDestinationArtifactOrdinal)
    {
        List<AtlasCopyPlanEntry> entries = [];
        int nextOrdinal = firstDestinationArtifactOrdinal;
        foreach (AtlasManifestSaveEntry saveEntry in manifest.PendingManifest.SaveEntries
                     .Where(static entry =>
                         StringComparer.Ordinal.Equals(
                             entry.Decision,
                             AtlasIntakeContracts.IncludeSaveDecision))
                     .OrderBy(static entry => entry.SourceAlias, StringComparer.Ordinal))
        {
            entries.Add(new AtlasCopyPlanEntry
            {
                SourceAlias = saveEntry.SourceAlias,
                DestinationArtifactAlias = AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
                ArtifactClass = AtlasIntakeContracts.SaveCopyArtifactClass,
                DestinationRelativePath = $"saves/{saveEntry.SourceAlias}.rpgsave",
            });
        }

        foreach (AtlasManifestDefinitionEntry entry in manifest.PendingManifest.DefinitionEntries
                     .Where(static candidate =>
                         StringComparer.Ordinal.Equals(candidate.Decision, "include"))
                     .OrderBy(static candidate => candidate.SourceAlias, StringComparer.Ordinal))
        {
            string extension = Path.GetExtension(entry.RelativePath).ToLowerInvariant();
            entries.Add(new AtlasCopyPlanEntry
            {
                SourceAlias = entry.SourceAlias,
                DestinationArtifactAlias = AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
                ArtifactClass = AtlasIntakeContracts.DefinitionCopyArtifactClass,
                DestinationRelativePath = $"definitions/{entry.SourceAlias}{extension}",
            });
        }

        return new AtlasCopyPlanDocument
        {
            SchemaVersion = AtlasIntakeContracts.CopyPlanSchemaVersion,
            SurveyAlias = manifest.PendingManifest.SurveyAlias,
            ManifestRevision = AtlasIntakeContracts.PendingManifestRevision,
            Entries = [.. entries],
        };
    }

    internal static string FindManifestArtifactAlias(
        AtlasPrivateArtifactInventoryDocument inventory,
        string purpose)
    {
        AtlasPrivateArtifactEntry[] matches = inventory.Artifacts
            .Where(artifact =>
                StringComparer.Ordinal.Equals(
                    artifact.ArtifactClass,
                    AtlasIntakeContracts.LiveDiscoveryArtifactClass)
                && (StringComparer.Ordinal.Equals(artifact.Purpose, purpose)
                    || artifact.Purpose.Contains("manifest", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0].ArtifactAlias,
            _ => throw new AtlasSafetyException("The manifest artifact alias is ambiguous."),
        };
    }

    internal static int GetMaximumArtifactOrdinal(
        AtlasPrivateArtifactInventoryDocument inventory) =>
        inventory.Artifacts.Length == 0
            ? 0
            : inventory.Artifacts.Max(static artifact =>
                AtlasIntakeContracts.ParseArtifactOrdinal(artifact.ArtifactAlias));

    internal static int GetMaximumArtifactOrdinal(AtlasCopyPlanDocument copyPlan) =>
        copyPlan.Entries.Length == 0
            ? 0
            : copyPlan.Entries.Max(static entry =>
                AtlasIntakeContracts.ParseArtifactOrdinal(entry.DestinationArtifactAlias));

    internal static AtlasPrivateArtifactEntry CreateArtifactEntry(
        string artifactAlias,
        string artifactClass,
        string purpose,
        string[] lineageAliases,
        string lastUseMilestone,
        string plannedDisposition,
        string verificationMethod,
        string? qualification = null) =>
        new()
        {
            ArtifactAlias = artifactAlias,
            ArtifactClass = artifactClass,
            Qualification = qualification,
            Purpose = purpose,
            CustodianRole = AtlasIntakeContracts.ProjectLeaderRole,
            LineageAliases = lineageAliases,
            LastUseMilestone = lastUseMilestone,
            ExpiryCondition = $"after:{lastUseMilestone}",
            PlannedDisposition = plannedDisposition,
            Status = AtlasIntakeContracts.PresentArtifactStatus,
            VerificationMethod = verificationMethod,
        };

    internal static AtlasDocumentBinding CreateDocumentBinding(
        string role,
        string artifactAlias,
        string absolutePath,
        AtlasWorkspaceLayout layout,
        string? sha256 = null) =>
        new()
        {
            Role = role,
            ArtifactAlias = artifactAlias,
            RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                layout.WorkspaceRoot,
                absolutePath),
            Sha256 = sha256
                ?? AtlasIntakeContracts.ComputeSha256Hex(File.ReadAllBytes(absolutePath)),
        };

    internal static AtlasArtifactBinding CreateArtifactBinding(
        string role,
        string artifactAlias,
        string absolutePath,
        AtlasWorkspaceLayout layout,
        string sha256) =>
        new()
        {
            Role = role,
            ArtifactAlias = artifactAlias,
            RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                layout.WorkspaceRoot,
                absolutePath),
            Sha256 = sha256,
        };

    internal static async ValueTask<PublishedFile> EnsureDeterministicFileAsync(
        string finalPath,
        string phase,
        byte[] expectedBytes,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(expectedBytes);
        ArgumentNullException.ThrowIfNull(io);

        string stagingPath = GetStagingPath(finalPath, phase);
        string expectedSha256 = AtlasIntakeContracts.ComputeSha256Hex(expectedBytes);
        io.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        if (io.FileExists(finalPath))
        {
            if (io.FileExists(stagingPath))
            {
                throw new AtlasSafetyException("A completed file has unexpected staging bytes.");
            }

            byte[] existingBytes = await io.ReadAllBytesAsync(finalPath, cancellationToken)
                .ConfigureAwait(false);
            EnsureBytesMatch(expectedBytes, existingBytes);
            return new PublishedFile(finalPath, expectedSha256);
        }

        if (io.FileExists(stagingPath))
        {
            byte[] existingStagingBytes = await io.ReadAllBytesAsync(stagingPath, cancellationToken)
                .ConfigureAwait(false);
            EnsureBytesMatch(expectedBytes, existingStagingBytes);
            io.MoveFile(stagingPath, finalPath);
            return new PublishedFile(finalPath, expectedSha256);
        }

        await using (Stream stream = io.OpenFile(
                         stagingPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         FileOptions.None))
        {
            await stream.WriteAsync(expectedBytes, cancellationToken).ConfigureAwait(false);
            await FlushAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        byte[] stagedBytes = await io.ReadAllBytesAsync(stagingPath, cancellationToken)
            .ConfigureAwait(false);
        EnsureBytesMatch(expectedBytes, stagedBytes);
        io.MoveFile(stagingPath, finalPath);
        return new PublishedFile(finalPath, expectedSha256);
    }

    internal static async ValueTask<InventoryReplaceResult> EnsureInventoryReplaceAsync(
        string inventoryPath,
        string backupPath,
        string phase,
        byte[] priorInventoryBytes,
        byte[] replacementInventoryBytes,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        string stagingPath = GetStagingPath(inventoryPath, phase);
        string priorSha256 = AtlasIntakeContracts.ComputeSha256Hex(priorInventoryBytes);
        string replacementSha256 = AtlasIntakeContracts.ComputeSha256Hex(replacementInventoryBytes);
        byte[] currentBytes = await io.ReadAllBytesAsync(inventoryPath, cancellationToken)
            .ConfigureAwait(false);
        string currentSha256 = AtlasIntakeContracts.ComputeSha256Hex(currentBytes);
        if (StringComparer.Ordinal.Equals(currentSha256, replacementSha256))
        {
            if (!io.FileExists(backupPath))
            {
                throw new AtlasSafetyException("The inventory backup is missing.");
            }

            byte[] backupBytes = await io.ReadAllBytesAsync(backupPath, cancellationToken)
                .ConfigureAwait(false);
            string backupSha256 = AtlasIntakeContracts.ComputeSha256Hex(backupBytes);
            EnsureDigestMatches(
                priorSha256,
                backupSha256,
                static () => new AtlasSafetyException("The inventory backup digest is invalid."));
            return new InventoryReplaceResult(
                inventoryPath,
                backupPath,
                replacementSha256,
                backupSha256);
        }

        EnsureDigestMatches(
            priorSha256,
            currentSha256,
            static () => new AtlasSafetyException("The prior inventory digest is invalid."));
        if (io.FileExists(backupPath))
        {
            throw new AtlasSafetyException("The inventory backup already exists.");
        }

        io.CreateDirectory(Path.GetDirectoryName(inventoryPath)!);
        if (io.FileExists(stagingPath))
        {
            byte[] existingStagingBytes = await io.ReadAllBytesAsync(stagingPath, cancellationToken)
                .ConfigureAwait(false);
            EnsureBytesMatch(replacementInventoryBytes, existingStagingBytes);
        }
        else
        {
            await using Stream stream = io.OpenFile(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                FileOptions.None);
            await stream.WriteAsync(replacementInventoryBytes, cancellationToken)
                .ConfigureAwait(false);
            await FlushAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        io.ReplaceFile(stagingPath, inventoryPath, backupPath);
        byte[] replacedBytes = await io.ReadAllBytesAsync(inventoryPath, cancellationToken)
            .ConfigureAwait(false);
        EnsureBytesMatch(replacementInventoryBytes, replacedBytes);
        byte[] backupResultBytes = await io.ReadAllBytesAsync(backupPath, cancellationToken)
            .ConfigureAwait(false);
        EnsureBytesMatch(priorInventoryBytes, backupResultBytes);
        return new InventoryReplaceResult(
            inventoryPath,
            backupPath,
            replacementSha256,
            AtlasIntakeContracts.ComputeSha256Hex(backupResultBytes));
    }

    internal static string GetStagingPath(string finalPath, string phase) =>
        finalPath + "." + phase + ".staging";

    internal static async ValueTask FlushAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is FileStream fileStream)
        {
            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            fileStream.Flush(flushToDisk: true);
            return;
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static void EnsureBytesMatch(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new AtlasSafetyException("The deterministic bytes changed unexpectedly.");
        }
    }

    internal static void EnsureDigestMatches(
        string expectedSha256,
        string actualSha256,
        Func<Exception> createException)
    {
        if (!StringComparer.Ordinal.Equals(expectedSha256, actualSha256))
        {
            throw createException();
        }
    }

    internal static void EnsureStateDocumentMatchesBinding<TDocument>(
        AtlasLoadedDocument<TDocument> loadedDocument,
        AtlasDocumentBinding binding)
        where TDocument : class
    {
        EnsureDigestMatches(
            binding.Sha256,
            loadedDocument.Sha256,
            static () => new AtlasApprovalException("A state-bound digest does not match."));
    }

    internal static DiscoveredManifest DiscoverCurrentManifest(
        AtlasIntakeDiscoveryRequest request,
        AtlasCorpusIntakeManifest baselineManifest,
        AtlasIoSeams io)
    {
        Dictionary<string, string> baselineSaveRootPaths = request.SaveRoots.ToDictionary(
            static saveRoot => saveRoot.LocationRole,
            static saveRoot => AtlasIntakeContracts.NormalizePath(saveRoot.Path),
            StringComparer.Ordinal);

        Dictionary<string, AtlasManifestSaveRoot> baselineSaveRoots = baselineManifest.SaveRoots
            .ToDictionary(static saveRoot => saveRoot.LocationRole, StringComparer.Ordinal);
        Dictionary<string, AtlasManifestSaveEntry> baselineSaveEntries =
            baselineManifest.SaveEntries
            .ToDictionary(
                static entry => CreateSaveEntryIdentity(entry.RootAlias, entry.RelativePath),
                StringComparer.OrdinalIgnoreCase);

        AtlasManifestSaveRoot[] discoveredSaveRoots = [.. baselineManifest.SaveRoots];
        List<AtlasManifestSaveEntry> discoveredSaveEntries = [];
        foreach (AtlasManifestSaveRoot baselineSaveRoot in baselineManifest.SaveRoots
                     .OrderBy(static root => root.LocationRole, StringComparer.Ordinal))
        {
            string absoluteRootPath = baselineSaveRootPaths[baselineSaveRoot.LocationRole];
            List<DiscoveredSaveEntry> entries = EnumerateSaveRootEntries(
                absoluteRootPath,
                baselineSaveRoot,
                io);
            bool hasIncludedSave = entries.Any(
                static entry =>
                    StringComparer.Ordinal.Equals(
                        entry.ManifestEntry.Decision,
                        AtlasIntakeContracts.IncludeSaveDecision));
            AtlasManifestSaveRoot updatedRoot = baselineSaveRoot with
            {
                Activity = hasIncludedSave ? "active" : "inactive",
                Decision = hasIncludedSave
                    ? AtlasIntakeContracts.IncludeSaveRootDecision
                    : AtlasIntakeContracts.ExcludeNoSaveInputsDecision,
                ObservedEntryCount = entries.Count,
                IsReparsePoint = false,
            };
            discoveredSaveRoots = discoveredSaveRoots
                .Select(root =>
                    StringComparer.Ordinal.Equals(root.LocationRole, updatedRoot.LocationRole)
                        ? updatedRoot
                        : root)
                .ToArray();
            foreach (DiscoveredSaveEntry entry in entries)
            {
                string identity = CreateSaveEntryIdentity(
                    entry.ManifestEntry.RootAlias,
                    entry.ManifestEntry.RelativePath);
                if (!baselineSaveEntries.TryGetValue(
                        identity,
                        out AtlasManifestSaveEntry? baselineEntry))
                {
                    throw new AtlasSafetyException("The save discovery denominator changed.");
                }

                EnsureManifestSaveEntryMatchesBaseline(entry.ManifestEntry, baselineEntry);
                discoveredSaveEntries.Add(entry.ManifestEntry with
                {
                    SourceAlias = baselineEntry.SourceAlias,
                });
            }
        }

        if (discoveredSaveEntries.Count != baselineManifest.SaveEntries.Length)
        {
            throw new AtlasSafetyException("The save discovery denominator changed.");
        }

        Dictionary<string, AtlasManifestDefinitionEntry> baselineDefinitionEntries =
            baselineManifest.DefinitionEntries.ToDictionary(
                static entry => AtlasIntakeContracts.NormalizeRelativePath(entry.RelativePath),
                StringComparer.OrdinalIgnoreCase);
        List<AtlasManifestDefinitionEntry> discoveredDefinitionEntries = EnumerateDefinitionEntries(
            request.DefinitionRoot,
            baselineDefinitionEntries,
            baselineSaveRootPaths.Values,
            io);
        if (discoveredDefinitionEntries.Count != baselineManifest.DefinitionEntries.Length)
        {
            throw new AtlasSafetyException("The definition discovery denominator changed.");
        }

        AtlasManifestDefinitionGroup[] discoveredDefinitionGroups =
            baselineManifest.DefinitionGroups
            .Select(group => group with
            {
                DiscoveredCount = discoveredDefinitionEntries.Count(entry =>
                    StringComparer.Ordinal.Equals(entry.GroupId, group.GroupId)),
            })
            .ToArray();

        AtlasCorpusIntakeManifest pendingManifest = baselineManifest with
        {
            ManifestRevision = AtlasIntakeContracts.PendingManifestRevision,
            SaveRoots = discoveredSaveRoots.OrderBy(
                static root => root.LocationRole,
                StringComparer.Ordinal).ToArray(),
            DiscoveredSaveDirectoryEntryCount = discoveredSaveEntries.Count,
            IncludedSaveCount = discoveredSaveEntries.Count(
                entry =>
                    StringComparer.Ordinal.Equals(
                        entry.Decision,
                        AtlasIntakeContracts.IncludeSaveDecision)),
            SaveEntries = discoveredSaveEntries.OrderBy(
                    static entry => entry.SourceAlias,
                    StringComparer.Ordinal)
                .ToArray(),
            DiscoveredDefinitionEntryCount = discoveredDefinitionEntries.Count,
            IncludedDefinitionCount = discoveredDefinitionEntries.Count(entry =>
                StringComparer.Ordinal.Equals(entry.Decision, "include")),
            DefinitionGroups = discoveredDefinitionGroups.OrderBy(
                    static group => group.GroupId,
                    StringComparer.Ordinal)
                .ToArray(),
            DefinitionEntries = discoveredDefinitionEntries.OrderBy(
                    static entry => entry.SourceAlias,
                    StringComparer.Ordinal)
                .ToArray(),
        };

        Dictionary<string, string> saveRootPaths = pendingManifest.SaveRoots.ToDictionary(
            static root => root.RootAlias,
            root => baselineSaveRootPaths[root.LocationRole],
            StringComparer.Ordinal);
        return new DiscoveredManifest(pendingManifest, saveRootPaths);
    }

    internal static List<DiscoveredSaveEntry> EnumerateSaveRootEntries(
        string absoluteRootPath,
        AtlasManifestSaveRoot baselineRoot,
        AtlasIoSeams io)
    {
        List<DiscoveredSaveEntry> results = [];
        foreach (string entryPath in io
                     .EnumerateFileSystemEntries(absoluteRootPath, SearchOption.TopDirectoryOnly)
                     .OrderBy(
                         static path => Path.GetFileName(path),
                         StringComparer.OrdinalIgnoreCase))
        {
            FileAttributes attributes = io.GetAttributes(entryPath);
            bool isReparse = (attributes & FileAttributes.ReparsePoint) != 0;
            string entryType = (attributes & FileAttributes.Directory) != 0
                ? AtlasIntakeContracts.DirectoryEntryType
                : AtlasIntakeContracts.FileEntryType;
            if (isReparse)
            {
                throw new AtlasSafetyException("A save entry is reparse-backed.");
            }

            AtlasManifestSaveEntry manifestEntry = ClassifySaveEntry(
                baselineRoot.RootAlias,
                Path.GetFileName(entryPath),
                entryType);
            results.Add(new DiscoveredSaveEntry(entryPath, manifestEntry));
        }

        return results;
    }

    internal static AtlasManifestSaveEntry ClassifySaveEntry(
        string rootAlias,
        string fileName,
        string entryType)
    {
        AtlasManifestSaveEntry result = new()
        {
            RootAlias = rootAlias,
            RelativePath = fileName,
            EntryType = entryType,
            IsReparsePoint = false,
        };
        if (!StringComparer.Ordinal.Equals(entryType, AtlasIntakeContracts.FileEntryType))
        {
            throw new AtlasSafetyException("Unexpected non-file save entries are unsupported.");
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(fileName, "steam_autocloud.vdf"))
        {
            return result with
            {
                Role = AtlasIntakeContracts.SteamAutoCloudSaveRole,
                Decision = AtlasIntakeContracts.ExcludeSteamAutoCloudDecision,
            };
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(fileName, "global.rpgsave"))
        {
            return result with
            {
                Role = AtlasIntakeContracts.GlobalSaveRole,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
            };
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(fileName, "config.rpgsave"))
        {
            return result with
            {
                Role = AtlasIntakeContracts.ConfigSaveRole,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
            };
        }

        if (TryParseSlotSave(fileName, out int slotNumber))
        {
            return result with
            {
                Role = AtlasIntakeContracts.SlotSaveRole,
                SlotNumber = slotNumber,
                Decision = AtlasIntakeContracts.IncludeSaveDecision,
            };
        }

        return result with
        {
            Role = AtlasIntakeContracts.OtherSaveRole,
            Decision = AtlasIntakeContracts.ExcludeNonSaveDecision,
        };
    }

    internal static bool TryParseSlotSave(string fileName, out int slotNumber)
    {
        const string Prefix = "file";
        const string Suffix = ".rpgsave";
        if (!fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
        {
            slotNumber = 0;
            return false;
        }

        string value = fileName[Prefix.Length..^Suffix.Length];
        if (!int.TryParse(value, out slotNumber) || slotNumber is < 1 or > 20)
        {
            return false;
        }

        return true;
    }

    internal static List<AtlasManifestDefinitionEntry> EnumerateDefinitionEntries(
        string definitionRoot,
        Dictionary<string, AtlasManifestDefinitionEntry> baselineDefinitionEntries,
        IEnumerable<string> excludedDirectories,
        AtlasIoSeams io)
    {
        List<AtlasManifestDefinitionEntry> results = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        string[] excludedRoots = excludedDirectories
            .Select(AtlasIntakeContracts.NormalizePath)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        HashSet<string> relevantExtensions = baselineDefinitionEntries.Values
            .Select(static entry => Path.GetExtension(entry.RelativePath).ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        EnumerateDirectory(definitionRoot);
        if (seenPaths.Count != baselineDefinitionEntries.Count)
        {
            throw new AtlasSafetyException("The definition discovery denominator changed.");
        }

        return results;

        void EnumerateDirectory(string directoryPath)
        {
            if (excludedRoots.Any(excludedRoot =>
                    AtlasDiscovery.ContainsPath(excludedRoot, directoryPath)))
            {
                return;
            }

            foreach (string entryPath in io
                         .EnumerateFileSystemEntries(directoryPath, SearchOption.TopDirectoryOnly)
                         .OrderBy(
                             static path => Path.GetFileName(path),
                             StringComparer.OrdinalIgnoreCase))
            {
                FileAttributes attributes = io.GetAttributes(entryPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new AtlasSafetyException("A definition entry is reparse-backed.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    EnumerateDirectory(entryPath);
                    continue;
                }

                string relativePath = AtlasIntakeContracts.NormalizeRelativePath(
                    Path.GetRelativePath(definitionRoot, entryPath));
                if (!relevantExtensions.Contains(
                        Path.GetExtension(relativePath).ToLowerInvariant()))
                {
                    continue;
                }

                if (!baselineDefinitionEntries.TryGetValue(
                        relativePath,
                        out AtlasManifestDefinitionEntry? baseline))
                {
                    throw new AtlasSafetyException("The definition discovery denominator changed.");
                }

                if (!seenPaths.Add(relativePath))
                {
                    throw new AtlasSafetyException("The definition discovery denominator changed.");
                }

                results.Add(baseline with
                {
                    RelativePath = relativePath,
                    EntryType = AtlasIntakeContracts.FileEntryType,
                    IsReparsePoint = false,
                });
            }
        }
    }

    internal static string CreateSaveEntryIdentity(string rootAlias, string relativePath) =>
        $"{rootAlias}|{AtlasIntakeContracts.NormalizeRelativePath(relativePath)}";

    internal static void EnsureManifestSaveEntryMatchesBaseline(
        AtlasManifestSaveEntry actual,
        AtlasManifestSaveEntry baseline)
    {
        if (!StringComparer.Ordinal.Equals(actual.Role, baseline.Role)
            || !StringComparer.Ordinal.Equals(actual.Decision, baseline.Decision)
            || !StringComparer.Ordinal.Equals(actual.EntryType, baseline.EntryType)
            || actual.SlotNumber != baseline.SlotNumber)
        {
            throw new AtlasSafetyException("The save entry classification changed.");
        }
    }

    internal sealed record DiscoveredSaveEntry(
        string AbsolutePath,
        AtlasManifestSaveEntry ManifestEntry);

    internal sealed record DiscoveredManifest(
        AtlasCorpusIntakeManifest PendingManifest,
        IReadOnlyDictionary<string, string> SaveRootPaths);
}

internal sealed record PublishedFile(string FinalPath, string Sha256);

internal sealed record InventoryReplaceResult(
    string InventoryPath,
    string BackupPath,
    string ReplacementSha256,
    string BackupSha256);

internal readonly record struct AtlasDriveInfo(bool IsReady, DriveType DriveType);

internal sealed class AtlasIoSeams
{
    public static AtlasIoSeams Default { get; } = new();

    public Func<string, CancellationToken, ValueTask<byte[]>> ReadAllBytesAsync { get; init; } =
        static async (path, cancellationToken) =>
            await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);

    public Func<string, string> ReadAllText { get; init; } = File.ReadAllText;

    public Func<string, bool> FileExists { get; init; } = File.Exists;

    public Func<string, bool> DirectoryExists { get; init; } = Directory.Exists;

    public Func<string, FileAttributes> GetAttributes { get; init; } = File.GetAttributes;

    public Func<string, AtlasDriveInfo> GetDriveInfo { get; init; } =
        static path =>
        {
            DriveInfo drive = new(Path.GetPathRoot(path)!);
            return new AtlasDriveInfo(drive.IsReady, drive.DriveType);
        };

    public Func<string, SearchOption, IEnumerable<string>> EnumerateFileSystemEntries
    {
        get;
        init;
    } =
        static (path, searchOption) =>
            Directory.EnumerateFileSystemEntries(path, "*", searchOption);

    public Func<string, FileMode, FileAccess, FileShare, FileOptions, Stream> OpenFile
    {
        get;
        init;
    } =
        static (path, mode, access, share, options) =>
            new FileStream(
                path,
                new FileStreamOptions
                {
                    Access = access,
                    Mode = mode,
                    Options = options,
                    Share = share,
                });

    public Action<string> CreateDirectory { get; init; } =
        static path => Directory.CreateDirectory(path);

    public Action<string, string> MoveFile { get; init; } =
        static (source, destination) => File.Move(source, destination);

    public Action<string, string> MoveDirectory { get; init; } =
        static (source, destination) => Directory.Move(source, destination);

    public Action<string, string, string?> ReplaceFile { get; init; } =
        static (source, destination, backup) => File.Replace(source, destination, backup);

    public Action<string, bool> DeleteDirectory { get; init; } =
        static (path, recursive) => Directory.Delete(path, recursive);

    public Action<string, FileAttributes> SetAttributes { get; init; } = File.SetAttributes;

    public Func<string, long> GetLength { get; init; } =
        static path => new FileInfo(path).Length;

    public Func<string, DateTimeOffset> GetLastWriteTimeUtc { get; init; } =
        static path => new FileInfo(path).LastWriteTimeUtc;
}
