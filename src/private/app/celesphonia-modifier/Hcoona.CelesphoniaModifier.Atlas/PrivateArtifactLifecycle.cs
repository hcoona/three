namespace Hcoona.CelesphoniaModifier.Atlas;

public static class PrivateArtifactLifecycle
{
    public static ValueTask CleanupPreflightAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        CleanupPreflightAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    internal static async ValueTask CleanupPreflightAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);

        AtlasLoadedDocument<AtlasCleanupPreflightRequest> loadedRequest =
            await AtlasIntakeContracts.ReadCleanupPreflightRequestAsync(
                    requestPath,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasCleanupPreflightRequest request = loadedRequest.Document;
        AtlasWorkspaceLayout layout = AtlasIntakeContracts.CreateWorkspaceLayout(
            request.ProjectRoot,
            request.WorkspaceRoot,
            request.SurveyAlias);
        if (AtlasIntakeContracts.PathEquals(
                request.QualifiedStatePath,
                layout.CanonicalQualifiedStatePath)
            && AtlasDiscovery.IsRequiredFileAbsent(layout.CanonicalQualifiedStatePath, io))
        {
            if (!AtlasDiscovery.IsRequiredFileAbsent(
                    layout.CanonicalPreflightedStatePath,
                    io))
            {
                throw new AtlasSafetyException(
                    "A completed state lacks its qualified predecessor.");
            }

            throw new AtlasApprovalException("The qualified state is required.");
        }

        ValidateCleanupPreflightCanonicalPaths(loadedRequest.AbsolutePath, request, layout, io);
        AtlasDiscovery.ValidateCommandWorkspaceCensus(
            layout,
            AtlasIntakeContracts.PreflightedStateRevision,
            io);

        if (await AtlasDiscovery.TryReturnValidatedPreflightAsync(
                loadedRequest,
                layout,
                io,
                cancellationToken)
            .ConfigureAwait(false))
        {
            return;
        }

        AtlasLoadedDocument<AtlasIntakeStateDocument> qualifiedState =
            await AtlasIntakeContracts.ReadStateAsync(
                    request.QualifiedStatePath,
                    cancellationToken)
                .ConfigureAwait(false);
        if (qualifiedState.Document.StateRevision != AtlasIntakeContracts.QualifiedStateRevision)
        {
            throw new AtlasSafetyException("The qualified state revision is invalid.");
        }

        AtlasDiscovery.EnsureDigestMatches(
            request.ExpectedQualifiedStateSha256,
            qualifiedState.Sha256,
            static () => new AtlasSafetyException("The qualified state digest is invalid."));

        PhaseInventoryContext inventoryContext = await TrustedLocalCopy.LoadPhaseInventoryAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalPreflightedInventoryBackupPath,
                request.ExpectedInventorySha256,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        if (!StringComparer.Ordinal.Equals(
                qualifiedState.Document.InventorySha256,
                inventoryContext.PriorInventory.Sha256))
        {
            AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> state3Inventory =
                await AtlasIntakeContracts.ReadInventoryAsync(
                        layout.CanonicalQualifiedInventoryBackupPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (!StringComparer.Ordinal.Equals(
                    qualifiedState.Document.InventorySha256,
                    state3Inventory.Sha256))
            {
                throw new AtlasSafetyException("The qualified inventory digest is invalid.");
            }

            inventoryContext = new PhaseInventoryContext(
                state3Inventory,
                inventoryContext.CurrentInventory);
        }

        await AtlasDiscovery.ValidateStateChainAsync(
                layout,
                qualifiedState,
                inventoryContext.PriorInventory,
                new AtlasDiscovery.StateValidationExpectations(),
                io,
                cancellationToken)
            .ConfigureAwait(false);

        PreflightPhaseAliases aliases = ResolvePreflightAliases(inventoryContext);
        AtlasCleanupPreflightReportDocument report = CreateCleanupReport(
            request,
            inventoryContext.PriorInventory.Document,
            aliases.ReportAlias);
        byte[] reportBytes = AtlasIntakeContracts.SerializeCleanupPreflightReport(report);
        PublishedFile reportFile = await AtlasDiscovery.EnsureDeterministicFileAsync(
                layout.CanonicalCleanupPreflightReportPath,
                AtlasIntakeContracts.PreflightedPhase,
                reportBytes,
                AtlasDiscovery.ReadCleanupReportShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasPrivateArtifactInventoryDocument replacementInventory = CreatePreflightInventory(
            inventoryContext.PriorInventory.Document,
            aliases);
        byte[] replacementInventoryBytes =
            AtlasIntakeContracts.SerializeInventory(replacementInventory);
        InventoryReplaceResult inventoryReplace = await AtlasDiscovery.EnsureInventoryReplaceAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalPreflightedInventoryBackupPath,
                AtlasIntakeContracts.PreflightedPhase,
                inventoryContext.PriorInventory.Bytes,
                replacementInventoryBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasIntakeStateDocument state4 = CreatePreflightedState(
            request,
            layout,
            qualifiedState,
            aliases,
            loadedRequest.Sha256,
            inventoryReplace.BackupSha256,
            inventoryReplace.ReplacementSha256,
            reportFile.Sha256);
        byte[] stateBytes = AtlasIntakeContracts.SerializeState(state4);
        _ = await AtlasDiscovery.EnsureDeterministicFileAsync(
                layout.CanonicalPreflightedStatePath,
                AtlasIntakeContracts.PreflightedPhase,
                stateBytes,
                AtlasDiscovery.ReadStateShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static void ValidateCleanupPreflightCanonicalPaths(
        string requestPath,
        AtlasCleanupPreflightRequest request,
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io)
    {
        AtlasDiscovery.ValidateCanonicalRequestFile(
            requestPath,
            layout.CanonicalCleanupPreflightRequestPath,
            layout.WorkspaceRoot,
            io);
        AtlasDiscovery.ValidateExistingOrdinaryFile(request.QualifiedStatePath, io);
        AtlasDiscovery.ValidateExistingOrdinaryFile(request.InventoryPath, io);
        AtlasDiscovery.ValidateCanonicalOutputDirectory(
            request.StateRevisionDirectory,
            layout.StatesDirectory,
            layout.WorkspaceRoot,
            io);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.QualifiedStatePath,
            layout.CanonicalQualifiedStatePath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.InventoryPath,
            layout.CanonicalInventoryPath,
            layout.WorkspaceRoot,
            io,
            requireExisting: true);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.InventoryBackupPath,
            layout.CanonicalPreflightedInventoryBackupPath,
            layout.WorkspaceRoot,
            io,
            allowExistingOutput: true);
        AtlasDiscovery.ValidateCreateNewOutputFile(
            request.ReportOutputPath,
            layout.CanonicalCleanupPreflightReportPath,
            layout.WorkspaceRoot,
            io,
            allowExistingOutput: true);
    }

    internal static PreflightPhaseAliases ResolvePreflightAliases(
        PhaseInventoryContext inventoryContext)
    {
        string? requestAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.CleanupPreflightRequestPurpose);
        string? reportAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.CleanupPreflightReportPurpose);
        string? stateAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.State4Purpose);
        string? backupAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.PreflightInventoryBackupPurpose);
        if (requestAlias is not null
            || reportAlias is not null
            || stateAlias is not null
            || backupAlias is not null
            || !StringComparer.Ordinal.Equals(
                inventoryContext.CurrentInventory.Sha256,
                inventoryContext.PriorInventory.Sha256))
        {
            if (requestAlias is null
                || reportAlias is null
                || stateAlias is null
                || backupAlias is null)
            {
                throw new AtlasSafetyException(
                    "The preflight inventory transition is incomplete.");
            }

            PreflightPhaseAliases aliases = new(
                requestAlias,
                reportAlias,
                stateAlias,
                backupAlias);
            ValidateRecoveredPreflightAliases(inventoryContext, aliases);
            return aliases;
        }

        return CreatePreflightPhaseAliases(
            AtlasDiscovery.GetMaximumArtifactOrdinal(inventoryContext.PriorInventory.Document)
            + 1);
    }

    private static PreflightPhaseAliases CreatePreflightPhaseAliases(int firstOrdinal) =>
        new(
            AtlasIntakeContracts.FormatArtifactAlias(firstOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(firstOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(firstOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(firstOrdinal));

    private static void ValidateRecoveredPreflightAliases(
        PhaseInventoryContext inventoryContext,
        PreflightPhaseAliases aliases)
    {
        PreflightPhaseAliases expected = CreatePreflightPhaseAliases(
            AtlasDiscovery.GetMaximumArtifactOrdinal(inventoryContext.PriorInventory.Document)
            + 1);
        TrustedLocalCopy.ValidateRecoveredPhaseArtifacts(
            inventoryContext,
            [
                new(
                    expected.RequestAlias,
                    AtlasIntakeContracts.CleanupPreflightRequestPurpose,
                    AtlasIntakeContracts.PrivateEvidenceArtifactClass),
                new(
                    expected.ReportAlias,
                    AtlasIntakeContracts.CleanupPreflightReportPurpose,
                    AtlasIntakeContracts.CleanupRecordArtifactClass),
                new(
                    expected.StateAlias,
                    AtlasIntakeContracts.State4Purpose,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass),
                new(
                    expected.InventoryBackupAlias,
                    AtlasIntakeContracts.PreflightInventoryBackupPurpose,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass),
            ],
            "The preflight inventory aliases are invalid.");
        if (aliases != expected)
        {
            throw new AtlasSafetyException("The preflight inventory aliases are invalid.");
        }
    }

    internal static AtlasCleanupPreflightReportDocument CreateCleanupReport(
        AtlasCleanupPreflightRequest request,
        AtlasPrivateArtifactInventoryDocument inventory,
        string reportAlias) =>
        new()
        {
            SchemaVersion = AtlasIntakeContracts.CleanupPreflightReportSchemaVersion,
            SurveyAlias = request.SurveyAlias,
            ReportArtifactAlias = reportAlias,
            InventorySha256 = AtlasIntakeContracts.ComputeSha256Hex(
                AtlasIntakeContracts.SerializeInventory(inventory)),
            ProposedMilestone = request.ProposedMilestone,
            Results =
            [
                .. inventory.Artifacts
                    .OrderBy(static artifact => artifact.ArtifactAlias, StringComparer.Ordinal)
                    .Select(artifact => new AtlasCleanupPreflightResult
                    {
                        ArtifactAlias = artifact.ArtifactAlias,
                        ArtifactClass = artifact.ArtifactClass,
                        Status = artifact.Status,
                        PlannedDisposition = artifact.PlannedDisposition,
                        LastUseMilestone = artifact.LastUseMilestone,
                        ExpiryCondition = artifact.ExpiryCondition,
                        Result = EvaluateLifecycleResult(
                            artifact,
                            request.ProposedMilestone),
                    }),
            ],
        };

    internal static string EvaluateLifecycleResult(
        AtlasPrivateArtifactEntry artifact,
        string proposedMilestone) =>
        AtlasIntakeContracts.EvaluateCleanupPreflightResult(
            artifact.ArtifactClass,
            artifact.Status,
            artifact.PlannedDisposition,
            artifact.LastUseMilestone,
            artifact.ExpiryCondition,
            artifact.Qualification,
            proposedMilestone);

    internal static AtlasPrivateArtifactInventoryDocument CreatePreflightInventory(
        AtlasPrivateArtifactInventoryDocument priorInventory,
        PreflightPhaseAliases aliases)
    {
        string predecessorStateAlias = TrustedLocalCopy.TryFindPhaseAlias(
                priorInventory,
                AtlasIntakeContracts.State3Purpose)
            ?? throw new AtlasSafetyException("The qualified predecessor state is missing.");
        return
        priorInventory with
        {
            Artifacts =
            [
                .. priorInventory.Artifacts,
                AtlasDiscovery.CreateArtifactEntry(
                    aliases.RequestAlias,
                    AtlasIntakeContracts.PrivateEvidenceArtifactClass,
                    AtlasIntakeContracts.CleanupPreflightRequestPurpose,
                    [],
                    "A8",
                    AtlasIntakeContracts.DeleteDisposition,
                    "atlas-cli:cleanup-preflight"),
                AtlasDiscovery.CreateArtifactEntry(
                    aliases.ReportAlias,
                    AtlasIntakeContracts.CleanupRecordArtifactClass,
                    AtlasIntakeContracts.CleanupPreflightReportPurpose,
                    [aliases.RequestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.CleanupPreflightReportSchemaVersion),
                AtlasDiscovery.CreateArtifactEntry(
                    aliases.StateAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.State4Purpose,
                    [predecessorStateAlias, aliases.ReportAlias, aliases.InventoryBackupAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.IntakeStateSchemaVersion),
                AtlasDiscovery.CreateArtifactEntry(
                    aliases.InventoryBackupAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.PreflightInventoryBackupPurpose,
                    [aliases.RequestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "inventory-backup:preflighted"),
            ],
        };
    }

    internal static AtlasIntakeStateDocument CreatePreflightedState(
        AtlasCleanupPreflightRequest request,
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> qualifiedState,
        PreflightPhaseAliases aliases,
        string requestSha256,
        string backupSha256,
        string inventorySha256,
        string reportSha256) =>
        new()
        {
            SchemaVersion = AtlasIntakeContracts.IntakeStateSchemaVersion,
            SurveyAlias = request.SurveyAlias,
            StateRevision = AtlasIntakeContracts.PreflightedStateRevision,
            Phase = AtlasIntakeContracts.PreflightedPhase,
            StateArtifactAlias = aliases.StateAlias,
            SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
            BuildId = AtlasIntakeContracts.ExactBuildId,
            InventorySha256 = inventorySha256,
            DecisionCommit = qualifiedState.Document.DecisionCommit,
            FinalCopyRootRelativePath = qualifiedState.Document.FinalCopyRootRelativePath,
            DocumentBindings =
            [
                AtlasDiscovery.CreateDocumentBinding(
                    AtlasIntakeContracts.PredecessorStateRole,
                    qualifiedState.Document.StateArtifactAlias,
                    qualifiedState.AbsolutePath,
                    layout,
                    qualifiedState.Sha256),
                .. qualifiedState.Document.DocumentBindings
                    .Where(static binding =>
                        !StringComparer.Ordinal.Equals(
                            binding.Role,
                            AtlasIntakeContracts.PredecessorStateRole)),
                new AtlasDocumentBinding
                {
                    Role = AtlasIntakeContracts.CleanupPreflightReportRole,
                    ArtifactAlias = aliases.ReportAlias,
                    RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                        layout.WorkspaceRoot,
                        layout.CanonicalCleanupPreflightReportPath),
                    Sha256 = reportSha256,
                },
            ],
            ArtifactBindings =
            [
                new AtlasArtifactBinding
                {
                    Role = AtlasIntakeContracts.CleanupPreflightRequestRole,
                    ArtifactAlias = aliases.RequestAlias,
                    RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                        layout.WorkspaceRoot,
                        layout.CanonicalCleanupPreflightRequestPath),
                    Sha256 = requestSha256,
                },
                new AtlasArtifactBinding
                {
                    Role = AtlasIntakeContracts.PreflightedInventoryBackupRole,
                    ArtifactAlias = aliases.InventoryBackupAlias,
                    RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                        layout.WorkspaceRoot,
                        layout.CanonicalPreflightedInventoryBackupPath),
                    Sha256 = backupSha256,
                },
            ],
        };
}

internal sealed record PreflightPhaseAliases(
    string RequestAlias,
    string ReportAlias,
    string StateAlias,
    string InventoryBackupAlias);
