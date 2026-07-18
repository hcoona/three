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
        ValidateCommandWorkspaceCensus(layout, AtlasIntakeContracts.DiscoveredStateRevision, io);

        if (await TryReturnValidatedDiscoveryAsync(
                loadedRequest,
                layout,
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

        PhaseInventoryContext inventoryContext = await TrustedLocalCopy.LoadPhaseInventoryAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalDiscoveredInventoryBackupPath,
                request.ExpectedInventorySha256,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        DiscoveryPhaseAliases aliases = ResolveDiscoveryAliases(inventoryContext);
        string baselineManifestAlias = FindManifestArtifactAlias(
            inventoryContext.PriorInventory.Document,
            AtlasIntakeContracts.ManifestRevision3Purpose);
        DiscoveredManifest discovered = DiscoverCurrentManifest(
            request,
            baselineManifest.Document,
            io);
        int nextArtifactOrdinal = GetNextDiscoveryDestinationArtifactOrdinal(aliases);

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
                ReadManifestShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasSourceRootMapDocument sourceRootMap = CreateSourceRootMap(request, discovered);
        byte[] sourceRootMapBytes = AtlasIntakeContracts.SerializeSourceRootMap(sourceRootMap);
        PublishedFile sourceRootMapFile = await EnsureDeterministicFileAsync(
                layout.CanonicalSourceRootMapPath,
                AtlasIntakeContracts.DiscoveredPhase,
                sourceRootMapBytes,
                ReadSourceRootMapShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasCopyPlanDocument copyPlan = CreateCopyPlan(discovered, nextArtifactOrdinal);
        byte[] copyPlanBytes = AtlasIntakeContracts.SerializeCopyPlan(copyPlan);
        PublishedFile copyPlanFile = await EnsureDeterministicFileAsync(
                layout.CanonicalCopyPlanPath,
                AtlasIntakeContracts.DiscoveredPhase,
                copyPlanBytes,
                ReadCopyPlanShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> loadedPendingManifest =
            new(
                pendingManifestFile.FinalPath,
                pendingManifestBytes,
                pendingManifestFile.Sha256,
                pendingManifest);
        AtlasLoadedDocument<AtlasSourceRootMapDocument> loadedSourceRootMap =
            new(
                sourceRootMapFile.FinalPath,
                sourceRootMapBytes,
                sourceRootMapFile.Sha256,
                sourceRootMap);
        AtlasLoadedDocument<AtlasCopyPlanDocument> loadedCopyPlan =
            new(
                copyPlanFile.FinalPath,
                copyPlanBytes,
                copyPlanFile.Sha256,
                copyPlan);
        AtlasPrivateArtifactInventoryDocument replacementInventory = CreateDiscoveredInventory(
            inventoryContext.PriorInventory.Document,
            baselineManifestAlias,
            aliases);
        byte[] replacementInventoryBytes =
            AtlasIntakeContracts.SerializeInventory(replacementInventory);
        InventoryReplaceResult inventoryReplace = await EnsureInventoryReplaceAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalDiscoveredInventoryBackupPath,
                AtlasIntakeContracts.DiscoveredPhase,
                inventoryContext.PriorInventory.Bytes,
                replacementInventoryBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasIntakeStateDocument state1 = CreateDiscoveredState(
            loadedRequest,
            layout,
            baselineManifestAlias,
            baselineManifest,
            loadedPendingManifest,
            loadedSourceRootMap,
            loadedCopyPlan,
            aliases,
            inventoryReplace.BackupSha256,
            inventoryReplace.ReplacementSha256);
        byte[] stateBytes = AtlasIntakeContracts.SerializeState(state1);
        _ = await EnsureDeterministicFileAsync(
                layout.CanonicalDiscoveredStatePath,
                AtlasIntakeContracts.DiscoveredPhase,
                stateBytes,
                ReadStateShaAsync,
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
        ValidateCommandWorkspaceCensus(layout, AtlasIntakeContracts.ApprovedStateRevision, io);

        if (await TryReturnValidatedConfirmationAsync(
                loadedRequest,
                layout,
                io,
                cancellationToken)
            .ConfigureAwait(false))
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

        PhaseInventoryContext inventoryContext = await TrustedLocalCopy.LoadPhaseInventoryAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalApprovedInventoryBackupPath,
                request.ExpectedInventorySha256,
                io,
                cancellationToken)
            .ConfigureAwait(false);
        EnsureDigestMatches(
            discoveredState.Document.InventorySha256,
            inventoryContext.PriorInventory.Sha256,
            static () => new AtlasSafetyException("The discovered inventory digest is invalid."));
        await ValidateDiscoveredStateAsync(
                layout,
                discoveredState,
                inventoryContext.PriorInventory,
                new StateValidationExpectations(),
                cancellationToken)
            .ConfigureAwait(false);

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

        ConfirmationPhaseAliases aliases = ResolveConfirmationAliases(
            inventoryContext,
            copyPlan.Document);

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
                ReadManifestShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> loadedApprovedManifest =
            new(
                approvedManifestFile.FinalPath,
                approvedManifestBytes,
                approvedManifestFile.Sha256,
                approvedManifest);
        AtlasPrivateArtifactInventoryDocument replacementInventory = CreateApprovedInventory(
            inventoryContext.PriorInventory.Document,
            pendingManifestBinding.ArtifactAlias,
            discoveredState.Document.StateArtifactAlias,
            aliases);
        byte[] replacementInventoryBytes =
            AtlasIntakeContracts.SerializeInventory(replacementInventory);
        InventoryReplaceResult inventoryReplace = await EnsureInventoryReplaceAsync(
                layout.CanonicalInventoryPath,
                layout.CanonicalApprovedInventoryBackupPath,
                AtlasIntakeContracts.ApprovedPhase,
                inventoryContext.PriorInventory.Bytes,
                replacementInventoryBytes,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        AtlasIntakeStateDocument state2 = CreateApprovedState(
            loadedRequest,
            layout,
            discoveredState,
            loadedApprovedManifest,
            sourceRootMap,
            copyPlan,
            aliases,
            inventoryReplace.BackupSha256,
            inventoryReplace.ReplacementSha256);
        byte[] stateBytes = AtlasIntakeContracts.SerializeState(state2);
        _ = await EnsureDeterministicFileAsync(
                layout.CanonicalApprovedStatePath,
                AtlasIntakeContracts.ApprovedPhase,
                stateBytes,
                ReadStateShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static void ValidatePrivateWorkspace(AtlasWorkspaceLayout layout, AtlasIoSeams io)
    {
        ValidateExistingOrdinaryFile(layout.PrivateGitIgnorePath, io);
        string contents = io.ReadAllText(layout.PrivateGitIgnorePath);
        if (contents.Length > 0 && contents[0] == '\uFEFF')
        {
            throw new AtlasSafetyException("The .private .gitignore rules are invalid.");
        }

        string normalized = contents.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (!StringComparer.Ordinal.Equals(normalized, "*\n!.gitignore\n")
            && !StringComparer.Ordinal.Equals(normalized, "*\n!.gitignore"))
        {
            throw new AtlasSafetyException("The .private .gitignore rules are invalid.");
        }
    }

    internal static ValueTask<bool> TryReturnValidatedDiscoveryAsync(
        AtlasLoadedDocument<AtlasIntakeDiscoveryRequest> request,
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io,
        CancellationToken cancellationToken) =>
        TryReturnValidatedStateAsync(
            layout,
            new StateValidationExpectations(DiscoveryRequest: request),
            io,
            cancellationToken,
            layout.CanonicalPreflightedStatePath,
            layout.CanonicalQualifiedStatePath,
            layout.CanonicalApprovedStatePath,
            layout.CanonicalDiscoveredStatePath);

    internal static ValueTask<bool> TryReturnValidatedConfirmationAsync(
        AtlasLoadedDocument<AtlasIntakeConfirmationRequest> request,
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io,
        CancellationToken cancellationToken) =>
        TryReturnValidatedStateAsync(
            layout,
            new StateValidationExpectations(ConfirmationRequest: request),
            io,
            cancellationToken,
            layout.CanonicalPreflightedStatePath,
            layout.CanonicalQualifiedStatePath,
            layout.CanonicalApprovedStatePath);

    internal static ValueTask<bool> TryReturnValidatedCopyAsync(
        AtlasLoadedDocument<AtlasIntakeCopyRequest> request,
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io,
        CancellationToken cancellationToken) =>
        TryReturnValidatedStateAsync(
            layout,
            new StateValidationExpectations(CopyRequest: request),
            io,
            cancellationToken,
            layout.CanonicalPreflightedStatePath,
            layout.CanonicalQualifiedStatePath);

    internal static ValueTask<bool> TryReturnValidatedPreflightAsync(
        AtlasLoadedDocument<AtlasCleanupPreflightRequest> request,
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io,
        CancellationToken cancellationToken) =>
        TryReturnValidatedStateAsync(
            layout,
            new StateValidationExpectations(PreflightRequest: request),
            io,
            cancellationToken,
            layout.CanonicalPreflightedStatePath);

    internal static async ValueTask<bool> TryReturnValidatedStateAsync(
        AtlasWorkspaceLayout layout,
        StateValidationExpectations expectations,
        AtlasIoSeams io,
        CancellationToken cancellationToken,
        params string[] statePaths)
    {
        foreach (string statePath in statePaths)
        {
            if (!io.FileExists(statePath))
            {
                continue;
            }

            AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> currentInventory =
                await AtlasIntakeContracts.ReadInventoryAsync(
                        layout.CanonicalInventoryPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            AtlasLoadedDocument<AtlasIntakeStateDocument> state =
                await AtlasIntakeContracts.ReadStateAsync(
                        statePath,
                        cancellationToken)
                    .ConfigureAwait(false);
            await ValidateStateChainAsync(
                    layout,
                    state,
                    currentInventory,
                    expectations,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }

        return false;
    }

    internal static async ValueTask ValidateStateChainAsync(
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> state,
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> phaseInventory,
        StateValidationExpectations expectations,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        switch (state.Document.StateRevision)
        {
            case AtlasIntakeContracts.DiscoveredStateRevision:
                await ValidateDiscoveredStateAsync(
                        layout,
                        state,
                        phaseInventory,
                        expectations,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AtlasIntakeContracts.ApprovedStateRevision:
                await ValidateApprovedStateAsync(
                        layout,
                        state,
                        phaseInventory,
                        expectations,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AtlasIntakeContracts.QualifiedStateRevision:
                await ValidateQualifiedStateAsync(
                        layout,
                        state,
                        phaseInventory,
                        expectations,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case AtlasIntakeContracts.PreflightedStateRevision:
                await ValidatePreflightedStateAsync(
                        layout,
                        state,
                        phaseInventory,
                        expectations,
                        io,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            default:
                throw new AtlasSafetyException("The intake-state revision is invalid.");
        }
    }

    internal static async ValueTask ValidateDiscoveredStateAsync(
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> state,
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> phaseInventory,
        StateValidationExpectations expectations,
        CancellationToken cancellationToken)
    {
        EnsureDigestMatches(
            state.Document.InventorySha256,
            phaseInventory.Sha256,
            static () => new AtlasSafetyException("The discovered inventory digest is invalid."));
        AtlasArtifactBinding requestBinding = AtlasIntakeContracts.GetRequiredArtifactBinding(
            state.Document,
            AtlasIntakeContracts.DiscoveredRequestRole);
        AtlasArtifactBinding backupBinding = AtlasIntakeContracts.GetRequiredArtifactBinding(
            state.Document,
            AtlasIntakeContracts.DiscoveredInventoryBackupRole);
        AtlasDocumentBinding baselineBinding = AtlasIntakeContracts.GetRequiredDocumentBinding(
            state.Document,
            AtlasIntakeContracts.BaselineManifestRole);
        AtlasDocumentBinding pendingBinding = AtlasIntakeContracts.GetRequiredDocumentBinding(
            state.Document,
            AtlasIntakeContracts.PendingManifestRole);
        AtlasDocumentBinding sourceRootMapBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                state.Document,
                AtlasIntakeContracts.SourceRootMapRole);
        AtlasDocumentBinding copyPlanBinding = AtlasIntakeContracts.GetRequiredDocumentBinding(
            state.Document,
            AtlasIntakeContracts.CopyPlanRole);

        AtlasLoadedDocument<AtlasIntakeDiscoveryRequest> request =
            await AtlasIntakeContracts.ReadDiscoveryRequestAsync(
                    layout.CanonicalDiscoverRequestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            request,
            requestBinding,
            layout.CanonicalDiscoverRequestPath,
            layout);
        EnsureExpectedRequestMatches(request, expectations.DiscoveryRequest);

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> priorInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                    layout.CanonicalDiscoveredInventoryBackupPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            priorInventory,
            backupBinding,
            layout.CanonicalDiscoveredInventoryBackupPath,
            layout);

        AtlasLoadedDocument<AtlasCorpusIntakeManifest> baselineManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                    layout.CanonicalBaselineManifestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            baselineManifest,
            baselineBinding,
            layout.CanonicalBaselineManifestPath,
            layout);
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> pendingManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                    layout.CanonicalPendingManifestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            pendingManifest,
            pendingBinding,
            layout.CanonicalPendingManifestPath,
            layout);
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap =
            await AtlasIntakeContracts.ReadSourceRootMapAsync(
                    layout.CanonicalSourceRootMapPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            sourceRootMap,
            sourceRootMapBinding,
            layout.CanonicalSourceRootMapPath,
            layout);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                    layout.CanonicalCopyPlanPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(copyPlan, copyPlanBinding, layout.CanonicalCopyPlanPath, layout);

        EnsureDigestMatches(
            request.Document.ExpectedBaselineSha256,
            baselineManifest.Sha256,
            static () => new AtlasSafetyException("The baseline manifest digest is invalid."));
        EnsureDigestMatches(
            request.Document.ExpectedInventorySha256,
            priorInventory.Sha256,
            static () => new AtlasSafetyException("The discovered backup digest is invalid."));
        Dictionary<string, string> saveRootPaths = request.Document.SaveRoots.ToDictionary(
            static saveRoot => saveRoot.LocationRole,
            static saveRoot => AtlasIntakeContracts.NormalizePath(saveRoot.Path),
            StringComparer.Ordinal);
        AtlasSourceRootMapDocument expectedSourceRootMap = CreateSourceRootMap(
            request.Document,
            new DiscoveredManifest(
                pendingManifest.Document,
                pendingManifest.Document.SaveRoots.ToDictionary(
                    static root => root.RootAlias,
                    root => saveRootPaths[root.LocationRole],
                    StringComparer.Ordinal)));
        EnsureBytesMatch(
            AtlasIntakeContracts.SerializeSourceRootMap(expectedSourceRootMap),
            sourceRootMap.Bytes);
        DiscoveryPhaseAliases aliases = new(
            requestBinding.ArtifactAlias,
            pendingBinding.ArtifactAlias,
            sourceRootMapBinding.ArtifactAlias,
            copyPlanBinding.ArtifactAlias,
            state.Document.StateArtifactAlias,
            backupBinding.ArtifactAlias);
        AtlasCopyPlanDocument expectedCopyPlan = CreateCopyPlan(
            pendingManifest.Document,
            GetNextDiscoveryDestinationArtifactOrdinal(aliases));
        EnsureBytesMatch(AtlasIntakeContracts.SerializeCopyPlan(expectedCopyPlan), copyPlan.Bytes);
        ValidateSourceRootMapAgainstManifest(sourceRootMap.Document, pendingManifest.Document);
        ValidateCopyPlanAgainstManifest(copyPlan.Document, pendingManifest.Document);
        AtlasPrivateArtifactInventoryDocument expectedInventory = CreateDiscoveredInventory(
            priorInventory.Document,
            baselineBinding.ArtifactAlias,
            aliases);
        EnsureBytesMatch(
            AtlasIntakeContracts.SerializeInventory(expectedInventory),
            phaseInventory.Bytes);
        AtlasIntakeStateDocument expectedState = CreateDiscoveredState(
            request,
            layout,
            baselineBinding.ArtifactAlias,
            baselineManifest,
            pendingManifest,
            sourceRootMap,
            copyPlan,
            aliases,
            priorInventory.Sha256,
            phaseInventory.Sha256);
        EnsureBytesMatch(AtlasIntakeContracts.SerializeState(expectedState), state.Bytes);
    }

    internal static async ValueTask ValidateApprovedStateAsync(
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> state,
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> phaseInventory,
        StateValidationExpectations expectations,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        EnsureDigestMatches(
            state.Document.InventorySha256,
            phaseInventory.Sha256,
            static () => new AtlasSafetyException("The approved inventory digest is invalid."));
        if (!io.FileExists(layout.CanonicalDiscoveredStatePath))
        {
            throw new AtlasApprovalException("The discovered state is required.");
        }

        AtlasArtifactBinding requestBinding = AtlasIntakeContracts.GetRequiredArtifactBinding(
            state.Document,
            AtlasIntakeContracts.ConfirmRequestRole);
        AtlasArtifactBinding backupBinding = AtlasIntakeContracts.GetRequiredArtifactBinding(
            state.Document,
            AtlasIntakeContracts.ApprovedInventoryBackupRole);
        AtlasDocumentBinding predecessorBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                state.Document,
                AtlasIntakeContracts.PredecessorStateRole);
        AtlasDocumentBinding approvedManifestBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                state.Document,
                AtlasIntakeContracts.ApprovedManifestRole);
        AtlasDocumentBinding sourceRootMapBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                state.Document,
                AtlasIntakeContracts.SourceRootMapRole);
        AtlasDocumentBinding copyPlanBinding = AtlasIntakeContracts.GetRequiredDocumentBinding(
            state.Document,
            AtlasIntakeContracts.CopyPlanRole);

        AtlasLoadedDocument<AtlasIntakeConfirmationRequest> request =
            await AtlasIntakeContracts.ReadConfirmationRequestAsync(
                    layout.CanonicalConfirmRequestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            request,
            requestBinding,
            layout.CanonicalConfirmRequestPath,
            layout);
        EnsureExpectedRequestMatches(request, expectations.ConfirmationRequest);

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> priorInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                    layout.CanonicalApprovedInventoryBackupPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            priorInventory,
            backupBinding,
            layout.CanonicalApprovedInventoryBackupPath,
            layout);

        AtlasLoadedDocument<AtlasIntakeStateDocument> discoveredState =
            await AtlasIntakeContracts.ReadStateAsync(
                    layout.CanonicalDiscoveredStatePath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            discoveredState,
            predecessorBinding,
            layout.CanonicalDiscoveredStatePath,
            layout);
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                    layout.CanonicalApprovedManifestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            approvedManifest,
            approvedManifestBinding,
            layout.CanonicalApprovedManifestPath,
            layout);
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap =
            await AtlasIntakeContracts.ReadSourceRootMapAsync(
                    layout.CanonicalSourceRootMapPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            sourceRootMap,
            sourceRootMapBinding,
            layout.CanonicalSourceRootMapPath,
            layout);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                    layout.CanonicalCopyPlanPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(copyPlan, copyPlanBinding, layout.CanonicalCopyPlanPath, layout);

        EnsureDigestMatches(
            request.Document.ExpectedDiscoveredStateSha256,
            discoveredState.Sha256,
            static () => new AtlasSafetyException("The discovered state digest is invalid."));
        EnsureDigestMatches(
            request.Document.ExpectedInventorySha256,
            priorInventory.Sha256,
            static () => new AtlasSafetyException("The approved backup digest is invalid."));
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> pendingManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                    layout.CanonicalPendingManifestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        AtlasDocumentBinding pendingManifestBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                discoveredState.Document,
                AtlasIntakeContracts.PendingManifestRole);
        EnsureLoadedBindingMatches(
            pendingManifest,
            pendingManifestBinding,
            layout.CanonicalPendingManifestPath,
            layout);
        AtlasCorpusIntakeManifest expectedApprovedManifest = pendingManifest.Document with
        {
            ManifestRevision = AtlasIntakeContracts.ApprovedManifestRevision,
            Confirmation = new AtlasManifestConfirmation
            {
                Status = AtlasIntakeContracts.ApprovedConfirmationStatus,
                ConfirmedByRole = AtlasIntakeContracts.ProjectLeaderRole,
                DecisionReference = AtlasIntakeContracts.ApprovalDecisionReferencePrefix
                    + request.Document.DecisionCommit,
            },
        };
        EnsureBytesMatch(
            AtlasIntakeContracts.SerializeManifest(expectedApprovedManifest),
            approvedManifest.Bytes);
        ValidateSourceRootMapAgainstManifest(sourceRootMap.Document, approvedManifest.Document);
        ValidateCopyPlanAgainstManifest(copyPlan.Document, approvedManifest.Document);
        await ValidateDiscoveredStateAsync(
                layout,
                discoveredState,
                priorInventory,
                expectations,
                cancellationToken)
            .ConfigureAwait(false);

        ConfirmationPhaseAliases aliases = new(
            requestBinding.ArtifactAlias,
            approvedManifestBinding.ArtifactAlias,
            state.Document.StateArtifactAlias,
            backupBinding.ArtifactAlias);
        AtlasPrivateArtifactInventoryDocument expectedInventory = CreateApprovedInventory(
            priorInventory.Document,
            pendingManifestBinding.ArtifactAlias,
            discoveredState.Document.StateArtifactAlias,
            aliases);
        EnsureBytesMatch(
            AtlasIntakeContracts.SerializeInventory(expectedInventory),
            phaseInventory.Bytes);
        AtlasIntakeStateDocument expectedState = CreateApprovedState(
            request,
            layout,
            discoveredState,
            approvedManifest,
            sourceRootMap,
            copyPlan,
            aliases,
            priorInventory.Sha256,
            phaseInventory.Sha256);
        EnsureBytesMatch(AtlasIntakeContracts.SerializeState(expectedState), state.Bytes);
    }

    internal static async ValueTask ValidateQualifiedStateAsync(
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> state,
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> phaseInventory,
        StateValidationExpectations expectations,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        EnsureDigestMatches(
            state.Document.InventorySha256,
            phaseInventory.Sha256,
            static () => new AtlasSafetyException("The qualified inventory digest is invalid."));
        if (!io.FileExists(layout.CanonicalApprovedStatePath))
        {
            throw new AtlasApprovalException("The approved state is required.");
        }

        AtlasArtifactBinding requestBinding = AtlasIntakeContracts.GetRequiredArtifactBinding(
            state.Document,
            AtlasIntakeContracts.CopyRequestRole);
        AtlasArtifactBinding backupBinding = AtlasIntakeContracts.GetRequiredArtifactBinding(
            state.Document,
            AtlasIntakeContracts.QualifiedInventoryBackupRole);
        AtlasDocumentBinding predecessorBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                state.Document,
                AtlasIntakeContracts.PredecessorStateRole);
        AtlasDocumentBinding approvedManifestBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                state.Document,
                AtlasIntakeContracts.ApprovedManifestRole);
        AtlasDocumentBinding sourceRootMapBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                state.Document,
                AtlasIntakeContracts.SourceRootMapRole);
        AtlasDocumentBinding copyPlanBinding = AtlasIntakeContracts.GetRequiredDocumentBinding(
            state.Document,
            AtlasIntakeContracts.CopyPlanRole);
        AtlasDocumentBinding receiptBinding = AtlasIntakeContracts.GetRequiredDocumentBinding(
            state.Document,
            AtlasIntakeContracts.CopyReceiptRole);

        AtlasLoadedDocument<AtlasIntakeCopyRequest> request =
            await AtlasIntakeContracts.ReadCopyRequestAsync(
                    layout.CanonicalCopyRequestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            request,
            requestBinding,
            layout.CanonicalCopyRequestPath,
            layout);
        EnsureExpectedRequestMatches(request, expectations.CopyRequest);

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> priorInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                    layout.CanonicalQualifiedInventoryBackupPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            priorInventory,
            backupBinding,
            layout.CanonicalQualifiedInventoryBackupPath,
            layout);

        AtlasLoadedDocument<AtlasIntakeStateDocument> approvedState =
            await AtlasIntakeContracts.ReadStateAsync(
                    layout.CanonicalApprovedStatePath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            approvedState,
            predecessorBinding,
            layout.CanonicalApprovedStatePath,
            layout);
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest =
            await AtlasIntakeContracts.ReadManifestAsync(
                    layout.CanonicalApprovedManifestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            approvedManifest,
            approvedManifestBinding,
            layout.CanonicalApprovedManifestPath,
            layout);
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap =
            await AtlasIntakeContracts.ReadSourceRootMapAsync(
                    layout.CanonicalSourceRootMapPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            sourceRootMap,
            sourceRootMapBinding,
            layout.CanonicalSourceRootMapPath,
            layout);
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan =
            await AtlasIntakeContracts.ReadCopyPlanAsync(
                    layout.CanonicalCopyPlanPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(copyPlan, copyPlanBinding, layout.CanonicalCopyPlanPath, layout);
        AtlasLoadedDocument<AtlasCopyReceiptDocument> receipt =
            await AtlasIntakeContracts.ReadCopyReceiptAsync(
                    layout.CanonicalCopyReceiptPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            receipt,
            receiptBinding,
            layout.CanonicalCopyReceiptPath,
            layout);

        EnsureDigestMatches(
            request.Document.ExpectedApprovedStateSha256,
            approvedState.Sha256,
            static () => new AtlasSafetyException("The approved state digest is invalid."));
        EnsureDigestMatches(
            request.Document.ExpectedInventorySha256,
            priorInventory.Sha256,
            static () => new AtlasSafetyException("The qualified backup digest is invalid."));
        ValidateSourceRootMapAgainstManifest(sourceRootMap.Document, approvedManifest.Document);
        ValidateCopyPlanAgainstManifest(copyPlan.Document, approvedManifest.Document);
        await ValidateApprovedStateAsync(
                layout,
                approvedState,
                priorInventory,
                expectations,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        CopyPhaseAliases aliases = new(
            requestBinding.ArtifactAlias,
            receiptBinding.ArtifactAlias,
            state.Document.StateArtifactAlias,
            backupBinding.ArtifactAlias);
        AtlasCopyReceiptDocument expectedReceipt = TrustedLocalCopy.CreateCopyReceipt(
            request.Sha256,
            request.Document,
            approvedState,
            approvedManifest,
            sourceRootMap,
            copyPlan,
            aliases,
            receipt.Document.GameExecutableSha256,
            receipt.Document.Entries);
        EnsureBytesMatch(
            AtlasIntakeContracts.SerializeCopyReceipt(expectedReceipt),
            receipt.Bytes);
        TrustedLocalCopy.ValidateReceiptAgainstBindings(
            request.Sha256,
            request.Document,
            approvedState,
            approvedManifest,
            sourceRootMap,
            copyPlan,
            aliases,
            receipt.Document);
        if (!TrustedLocalCopy.HasCompleteCopySet(
                layout.CanonicalFinalCopyPath,
                copyPlan.Document,
                layout.CanonicalCopyReceiptPath,
                io))
        {
            throw new AtlasSafetyException("The qualified copy set is incomplete.");
        }

        TrustedLocalCopy.ValidateCopiedFilesAgainstReceipt(
            layout.CanonicalFinalCopyPath,
            receipt.Document,
            io,
            cancellationToken);
        AtlasPrivateArtifactInventoryDocument expectedInventory =
            TrustedLocalCopy.CreateQualifiedInventory(
                priorInventory.Document,
                approvedManifestBinding.ArtifactAlias,
                copyPlan.Document,
                aliases,
                layout.CanonicalCopyReceiptPath);
        EnsureBytesMatch(
            AtlasIntakeContracts.SerializeInventory(expectedInventory),
            phaseInventory.Bytes);
        AtlasIntakeStateDocument expectedState = TrustedLocalCopy.CreateQualifiedState(
            request.Document,
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
            request.Sha256,
            priorInventory.Sha256,
            phaseInventory.Sha256,
            receipt.Sha256);
        EnsureBytesMatch(AtlasIntakeContracts.SerializeState(expectedState), state.Bytes);
    }

    internal static async ValueTask ValidatePreflightedStateAsync(
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> state,
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> phaseInventory,
        StateValidationExpectations expectations,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        EnsureDigestMatches(
            state.Document.InventorySha256,
            phaseInventory.Sha256,
            static () => new AtlasSafetyException("The preflighted inventory digest is invalid."));
        if (!io.FileExists(layout.CanonicalQualifiedStatePath))
        {
            throw new AtlasApprovalException("The qualified state is required.");
        }

        AtlasArtifactBinding requestBinding = AtlasIntakeContracts.GetRequiredArtifactBinding(
            state.Document,
            AtlasIntakeContracts.CleanupPreflightRequestRole);
        AtlasArtifactBinding backupBinding = AtlasIntakeContracts.GetRequiredArtifactBinding(
            state.Document,
            AtlasIntakeContracts.PreflightedInventoryBackupRole);
        AtlasDocumentBinding predecessorBinding =
            AtlasIntakeContracts.GetRequiredDocumentBinding(
                state.Document,
                AtlasIntakeContracts.PredecessorStateRole);
        AtlasDocumentBinding reportBinding = AtlasIntakeContracts.GetRequiredDocumentBinding(
            state.Document,
            AtlasIntakeContracts.CleanupPreflightReportRole);

        AtlasLoadedDocument<AtlasCleanupPreflightRequest> request =
            await AtlasIntakeContracts.ReadCleanupPreflightRequestAsync(
                    layout.CanonicalCleanupPreflightRequestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            request,
            requestBinding,
            layout.CanonicalCleanupPreflightRequestPath,
            layout);
        EnsureExpectedRequestMatches(request, expectations.PreflightRequest);

        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> priorInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(
                    layout.CanonicalPreflightedInventoryBackupPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            priorInventory,
            backupBinding,
            layout.CanonicalPreflightedInventoryBackupPath,
            layout);

        AtlasLoadedDocument<AtlasIntakeStateDocument> qualifiedState =
            await AtlasIntakeContracts.ReadStateAsync(
                    layout.CanonicalQualifiedStatePath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            qualifiedState,
            predecessorBinding,
            layout.CanonicalQualifiedStatePath,
            layout);
        AtlasLoadedDocument<AtlasCleanupPreflightReportDocument> report =
            await AtlasIntakeContracts.ReadCleanupPreflightReportAsync(
                    layout.CanonicalCleanupPreflightReportPath,
                    cancellationToken)
                .ConfigureAwait(false);
        EnsureLoadedBindingMatches(
            report,
            reportBinding,
            layout.CanonicalCleanupPreflightReportPath,
            layout);

        EnsureDigestMatches(
            request.Document.ExpectedQualifiedStateSha256,
            qualifiedState.Sha256,
            static () => new AtlasSafetyException("The qualified state digest is invalid."));
        EnsureDigestMatches(
            request.Document.ExpectedInventorySha256,
            priorInventory.Sha256,
            static () => new AtlasSafetyException("The preflight backup digest is invalid."));
        await ValidateQualifiedStateAsync(
                layout,
                qualifiedState,
                priorInventory,
                expectations,
                io,
                cancellationToken)
            .ConfigureAwait(false);

        PreflightPhaseAliases aliases = new(
            requestBinding.ArtifactAlias,
            reportBinding.ArtifactAlias,
            state.Document.StateArtifactAlias,
            backupBinding.ArtifactAlias);
        AtlasCleanupPreflightReportDocument expectedReport =
            PrivateArtifactLifecycle.CreateCleanupReport(
                request.Document,
                priorInventory.Document,
                aliases.ReportAlias);
        EnsureBytesMatch(
            AtlasIntakeContracts.SerializeCleanupPreflightReport(expectedReport),
            report.Bytes);
        AtlasPrivateArtifactInventoryDocument expectedInventory =
            PrivateArtifactLifecycle.CreatePreflightInventory(priorInventory.Document, aliases);
        EnsureBytesMatch(
            AtlasIntakeContracts.SerializeInventory(expectedInventory),
            phaseInventory.Bytes);
        AtlasIntakeStateDocument expectedState = PrivateArtifactLifecycle.CreatePreflightedState(
            request.Document,
            layout,
            qualifiedState,
            aliases,
            request.Sha256,
            priorInventory.Sha256,
            phaseInventory.Sha256,
            report.Sha256);
        EnsureBytesMatch(AtlasIntakeContracts.SerializeState(expectedState), state.Bytes);
    }

    internal static void EnsureLoadedBindingMatches<TDocument>(
        AtlasLoadedDocument<TDocument> loadedDocument,
        AtlasBindingBase binding,
        string expectedAbsolutePath,
        AtlasWorkspaceLayout layout)
        where TDocument : class
    {
        string expectedRelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
            layout.WorkspaceRoot,
            expectedAbsolutePath);
        if (!StringComparer.Ordinal.Equals(binding.RelativePath, expectedRelativePath)
            || !AtlasIntakeContracts.PathEquals(loadedDocument.AbsolutePath, expectedAbsolutePath))
        {
            throw new AtlasSafetyException("A state-bound path is invalid.");
        }

        EnsureDigestMatches(
            binding.Sha256,
            loadedDocument.Sha256,
            static () => new AtlasSafetyException("A state-bound digest does not match."));
    }

    internal static void EnsureExpectedRequestMatches<TRequest>(
        AtlasLoadedDocument<TRequest> boundRequest,
        AtlasLoadedDocument<TRequest>? expectedRequest)
        where TRequest : class
    {
        if (expectedRequest is null)
        {
            return;
        }

        if (!AtlasIntakeContracts.PathEquals(
                boundRequest.AbsolutePath,
                expectedRequest.AbsolutePath))
        {
            throw new AtlasSafetyException("The completed phase is bound to a different request.");
        }

        EnsureDigestMatches(
            expectedRequest.Sha256,
            boundRequest.Sha256,
            static () =>
                new AtlasSafetyException("The completed phase is bound to a different request."));
    }

    internal static void ValidateSourceRootMapAgainstManifest(
        AtlasSourceRootMapDocument sourceRootMap,
        AtlasCorpusIntakeManifest manifest)
    {
        if (!StringComparer.Ordinal.Equals(sourceRootMap.SurveyAlias, manifest.SurveyAlias))
        {
            throw new AtlasSafetyException("The source-root map does not match the manifest.");
        }

        Dictionary<string, AtlasManifestSaveRoot> manifestRoots = manifest.SaveRoots.ToDictionary(
            static root => root.LocationRole,
            StringComparer.Ordinal);
        if (sourceRootMap.SaveRoots.Length != manifest.SaveRoots.Length)
        {
            throw new AtlasSafetyException("The source-root map does not match the manifest.");
        }

        foreach (AtlasSourceRootBinding binding in sourceRootMap.SaveRoots)
        {
            if (!manifestRoots.TryGetValue(binding.LocationRole, out AtlasManifestSaveRoot? root)
                || !StringComparer.Ordinal.Equals(binding.RootAlias, root.RootAlias))
            {
                throw new AtlasSafetyException("The source-root map does not match the manifest.");
            }
        }
    }

    internal static void ValidateCopyPlanAgainstManifest(
        AtlasCopyPlanDocument copyPlan,
        AtlasCorpusIntakeManifest manifest)
    {
        if (!StringComparer.Ordinal.Equals(copyPlan.SurveyAlias, manifest.SurveyAlias))
        {
            throw new AtlasSafetyException("The copy plan does not match the manifest.");
        }

        AtlasCopyPlanEntry[] expectedEntries =
        [
            .. manifest.SaveEntries
                .Where(static entry =>
                    StringComparer.Ordinal.Equals(
                        entry.Decision,
                        AtlasIntakeContracts.IncludeSaveDecision))
                .OrderBy(static entry => entry.SourceAlias, StringComparer.Ordinal)
                .Select(static entry => new AtlasCopyPlanEntry
                {
                    SourceAlias = entry.SourceAlias,
                    ArtifactClass = AtlasIntakeContracts.SaveCopyArtifactClass,
                    DestinationRelativePath = $"saves/{entry.SourceAlias}.rpgsave",
                }),
            .. manifest.DefinitionEntries
                .Where(static entry =>
                    StringComparer.Ordinal.Equals(
                        entry.Decision,
                        AtlasIntakeContracts.IncludeDefinitionDecision))
                .OrderBy(static entry => entry.SourceAlias, StringComparer.Ordinal)
                .Select(entry => new AtlasCopyPlanEntry
                {
                    SourceAlias = entry.SourceAlias,
                    ArtifactClass = AtlasIntakeContracts.DefinitionCopyArtifactClass,
                    DestinationRelativePath =
                        $"definitions/{entry.SourceAlias}"
                        + Path.GetExtension(entry.RelativePath).ToLowerInvariant(),
                }),
        ];
        if (copyPlan.Entries.Length != expectedEntries.Length)
        {
            throw new AtlasSafetyException("The copy plan does not match the manifest.");
        }

        for (int index = 0; index < expectedEntries.Length; index++)
        {
            AtlasCopyPlanEntry actual = copyPlan.Entries[index];
            AtlasCopyPlanEntry expected = expectedEntries[index];
            if (!StringComparer.Ordinal.Equals(actual.SourceAlias, expected.SourceAlias)
                || !StringComparer.Ordinal.Equals(actual.ArtifactClass, expected.ArtifactClass)
                || !StringComparer.Ordinal.Equals(
                    actual.DestinationRelativePath,
                    expected.DestinationRelativePath))
            {
                throw new AtlasSafetyException("The copy plan does not match the manifest.");
            }
        }
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
        ValidateCanonicalOutputDirectory(
            request.ManifestRevisionDirectory,
            layout.ManifestRevisionDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCanonicalOutputDirectory(
            request.StateRevisionDirectory,
            layout.StatesDirectory,
            layout.WorkspaceRoot,
            io);

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
        ValidateCanonicalOutputDirectory(
            request.ManifestRevisionDirectory,
            layout.ManifestRevisionDirectory,
            layout.WorkspaceRoot,
            io);
        ValidateCanonicalOutputDirectory(
            request.StateRevisionDirectory,
            layout.StatesDirectory,
            layout.WorkspaceRoot,
            io);
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

    internal static void ValidateCanonicalOutputDirectory(
        string actualPath,
        string expectedPath,
        string workspaceRoot,
        AtlasIoSeams io)
    {
        if (!AtlasIntakeContracts.PathEquals(actualPath, expectedPath))
        {
            throw new AtlasSafetyException("The canonical path is invalid.");
        }

        ValidateCreateNewOutputDirectory(actualPath, workspaceRoot, io);
    }

    internal static void ValidateCommandWorkspaceCensus(
        AtlasWorkspaceLayout layout,
        int targetStateRevision,
        AtlasIoSeams io)
    {
        int completedStateRevision = GetHighestCompletedStateRevision(layout, io);
        bool allowCurrentPhaseTransients = completedStateRevision == targetStateRevision - 1;
        string targetPhase = AtlasIntakeContracts.GetPhaseName(targetStateRevision);

        ValidateDirectoryEntryCensus(
            layout.WorkspaceRoot,
            [],
            ["intake", "copies", "cleanup"],
            io);

        HashSet<string> intakeFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFileName(layout.CanonicalBaselineManifestPath),
            Path.GetFileName(layout.CanonicalInventoryPath),
        };
        if (completedStateRevision >= AtlasIntakeContracts.DiscoveredStateRevision
            || (allowCurrentPhaseTransients
                && targetStateRevision == AtlasIntakeContracts.DiscoveredStateRevision))
        {
            intakeFiles.Add(Path.GetFileName(layout.CanonicalSourceRootMapPath));
            intakeFiles.Add(Path.GetFileName(layout.CanonicalCopyPlanPath));
        }

        if (allowCurrentPhaseTransients)
        {
            if (targetStateRevision == AtlasIntakeContracts.DiscoveredStateRevision)
            {
                intakeFiles.Add(Path.GetFileName(
                    GetStagingPath(
                        layout.CanonicalSourceRootMapPath,
                        AtlasIntakeContracts.DiscoveredPhase)));
                intakeFiles.Add(Path.GetFileName(
                    GetStagingPath(
                        layout.CanonicalCopyPlanPath,
                        AtlasIntakeContracts.DiscoveredPhase)));
            }

            intakeFiles.Add(Path.GetFileName(
                GetStagingPath(layout.CanonicalInventoryPath, targetPhase)));
        }

        ValidateDirectoryEntryCensus(
            layout.IntakeDirectory,
            intakeFiles,
            ["requests", "manifest-revisions", "states", "inventory-backups"],
            io);

        HashSet<string> requestFiles = new(StringComparer.OrdinalIgnoreCase);
        int highestRequestRevision = Math.Max(completedStateRevision, targetStateRevision);
        for (int revision = AtlasIntakeContracts.DiscoveredStateRevision;
             revision <= highestRequestRevision;
             revision++)
        {
            requestFiles.Add(Path.GetFileName(GetCanonicalRequestPath(layout, revision)));
        }

        ValidateDirectoryEntryCensus(layout.RequestDirectory, requestFiles, [], io);

        HashSet<string> manifestFiles = new(StringComparer.OrdinalIgnoreCase);
        if (completedStateRevision >= AtlasIntakeContracts.DiscoveredStateRevision
            || (allowCurrentPhaseTransients
                && targetStateRevision == AtlasIntakeContracts.DiscoveredStateRevision))
        {
            manifestFiles.Add(Path.GetFileName(layout.CanonicalPendingManifestPath));
            if (allowCurrentPhaseTransients
                && targetStateRevision == AtlasIntakeContracts.DiscoveredStateRevision)
            {
                manifestFiles.Add(Path.GetFileName(
                    GetStagingPath(
                        layout.CanonicalPendingManifestPath,
                        AtlasIntakeContracts.DiscoveredPhase)));
            }
        }

        if (completedStateRevision >= AtlasIntakeContracts.ApprovedStateRevision
            || (allowCurrentPhaseTransients
                && targetStateRevision == AtlasIntakeContracts.ApprovedStateRevision))
        {
            manifestFiles.Add(Path.GetFileName(layout.CanonicalApprovedManifestPath));
            if (allowCurrentPhaseTransients
                && targetStateRevision == AtlasIntakeContracts.ApprovedStateRevision)
            {
                manifestFiles.Add(Path.GetFileName(
                    GetStagingPath(
                        layout.CanonicalApprovedManifestPath,
                        AtlasIntakeContracts.ApprovedPhase)));
            }
        }

        ValidateDirectoryEntryCensus(layout.ManifestRevisionDirectory, manifestFiles, [], io);

        HashSet<string> stateFiles = new(StringComparer.OrdinalIgnoreCase);
        for (int revision = AtlasIntakeContracts.DiscoveredStateRevision;
             revision <= completedStateRevision;
             revision++)
        {
            stateFiles.Add(Path.GetFileName(GetStatePath(layout, revision)));
        }

        if (allowCurrentPhaseTransients)
        {
            string currentStatePath = GetStatePath(layout, targetStateRevision);
            stateFiles.Add(Path.GetFileName(currentStatePath));
            stateFiles.Add(Path.GetFileName(GetStagingPath(currentStatePath, targetPhase)));
        }

        ValidateDirectoryEntryCensus(layout.StatesDirectory, stateFiles, [], io);

        HashSet<string> inventoryBackupFiles = new(StringComparer.OrdinalIgnoreCase);
        for (int revision = AtlasIntakeContracts.DiscoveredStateRevision;
             revision <= completedStateRevision;
             revision++)
        {
            inventoryBackupFiles.Add(Path.GetFileName(GetInventoryBackupPath(layout, revision)));
        }

        if (allowCurrentPhaseTransients)
        {
            inventoryBackupFiles.Add(Path.GetFileName(
                GetInventoryBackupPath(layout, targetStateRevision)));
        }

        ValidateDirectoryEntryCensus(
            layout.InventoryBackupsDirectory,
            inventoryBackupFiles,
            [],
            io);

        HashSet<string> cleanupFiles = new(StringComparer.OrdinalIgnoreCase);
        if (completedStateRevision >= AtlasIntakeContracts.PreflightedStateRevision
            || (allowCurrentPhaseTransients
                && targetStateRevision == AtlasIntakeContracts.PreflightedStateRevision))
        {
            cleanupFiles.Add(Path.GetFileName(layout.CanonicalCleanupPreflightReportPath));
        }

        if (allowCurrentPhaseTransients
            && targetStateRevision == AtlasIntakeContracts.PreflightedStateRevision)
        {
            cleanupFiles.Add(Path.GetFileName(
                GetStagingPath(
                    layout.CanonicalCleanupPreflightReportPath,
                    AtlasIntakeContracts.PreflightedPhase)));
        }

        ValidateDirectoryEntryCensus(layout.CleanupDirectory, cleanupFiles, [], io);

        ValidateCopyDirectoryNameCensus(
            layout,
            completedStateRevision,
            allowCurrentPhaseTransients
                && targetStateRevision == AtlasIntakeContracts.QualifiedStateRevision,
            io);
    }

    private static void ValidateCopyDirectoryNameCensus(
        AtlasWorkspaceLayout layout,
        int completedStateRevision,
        bool allowQualifiedPhaseTransients,
        AtlasIoSeams io)
    {
        bool finalExistsAsDirectory = io.DirectoryExists(layout.CanonicalFinalCopyPath);
        bool incompleteExistsAsDirectory = io.DirectoryExists(layout.CanonicalIncompleteCopyPath);
        if (io.FileExists(layout.CanonicalFinalCopyPath) && !finalExistsAsDirectory)
        {
            throw new AtlasSafetyException("The final copy path is invalid.");
        }

        if (io.FileExists(layout.CanonicalIncompleteCopyPath) && !incompleteExistsAsDirectory)
        {
            throw new AtlasSafetyException("The incomplete copy path is invalid.");
        }

        HashSet<string> allowedDirectories = new(StringComparer.OrdinalIgnoreCase);
        if (completedStateRevision >= AtlasIntakeContracts.QualifiedStateRevision
            || allowQualifiedPhaseTransients)
        {
            allowedDirectories.Add(Path.GetFileName(layout.CanonicalFinalCopyPath));
        }

        if (allowQualifiedPhaseTransients)
        {
            allowedDirectories.Add(Path.GetFileName(layout.CanonicalIncompleteCopyPath));
        }

        ValidateDirectoryEntryCensus(layout.CopiesDirectory, [], allowedDirectories, io);
        if (finalExistsAsDirectory && incompleteExistsAsDirectory)
        {
            throw new AtlasSafetyException("Unexpected copy directories require human inspection.");
        }
    }

    private static void ValidateDirectoryEntryCensus(
        string directoryPath,
        IEnumerable<string> allowedFiles,
        IEnumerable<string> allowedDirectories,
        AtlasIoSeams io)
    {
        bool directoryExists = io.DirectoryExists(directoryPath);
        if (!directoryExists)
        {
            if (io.FileExists(directoryPath))
            {
                throw new AtlasSafetyException("The directory path is invalid.");
            }

            return;
        }

        ValidateExistingOrdinaryDirectory(directoryPath, io);
        HashSet<string> fileNames = allowedFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> directoryNames = allowedDirectories.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        foreach (string entryPath in io.EnumerateFileSystemEntries(
                     directoryPath,
                     SearchOption.TopDirectoryOnly))
        {
            FileAttributes attributes = io.GetAttributes(entryPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Device) != 0)
            {
                throw new AtlasSafetyException("A workspace output entry is reparse-backed.");
            }

            string entryName = Path.GetFileName(entryPath);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!directoryNames.Contains(entryName))
                {
                    throw new AtlasSafetyException("The workspace output census is invalid.");
                }

                continue;
            }

            if (!fileNames.Contains(entryName))
            {
                throw new AtlasSafetyException("The workspace output census is invalid.");
            }
        }
    }

    private static int GetHighestCompletedStateRevision(
        AtlasWorkspaceLayout layout,
        AtlasIoSeams io)
    {
        for (int revision = AtlasIntakeContracts.PreflightedStateRevision;
             revision >= AtlasIntakeContracts.DiscoveredStateRevision;
             revision--)
        {
            if (io.FileExists(GetStatePath(layout, revision)))
            {
                return revision;
            }
        }

        return 0;
    }

    private static string GetCanonicalRequestPath(AtlasWorkspaceLayout layout, int stateRevision) =>
        stateRevision switch
        {
            AtlasIntakeContracts.DiscoveredStateRevision => layout.CanonicalDiscoverRequestPath,
            AtlasIntakeContracts.ApprovedStateRevision => layout.CanonicalConfirmRequestPath,
            AtlasIntakeContracts.QualifiedStateRevision => layout.CanonicalCopyRequestPath,
            AtlasIntakeContracts.PreflightedStateRevision =>
                layout.CanonicalCleanupPreflightRequestPath,
            _ => throw new AtlasSafetyException("The intake-state revision is invalid."),
        };

    private static string GetStatePath(AtlasWorkspaceLayout layout, int stateRevision) =>
        stateRevision switch
        {
            AtlasIntakeContracts.DiscoveredStateRevision => layout.CanonicalDiscoveredStatePath,
            AtlasIntakeContracts.ApprovedStateRevision => layout.CanonicalApprovedStatePath,
            AtlasIntakeContracts.QualifiedStateRevision => layout.CanonicalQualifiedStatePath,
            AtlasIntakeContracts.PreflightedStateRevision => layout.CanonicalPreflightedStatePath,
            _ => throw new AtlasSafetyException("The intake-state revision is invalid."),
        };

    private static string GetInventoryBackupPath(AtlasWorkspaceLayout layout, int stateRevision) =>
        stateRevision switch
        {
            AtlasIntakeContracts.DiscoveredStateRevision =>
                layout.CanonicalDiscoveredInventoryBackupPath,
            AtlasIntakeContracts.ApprovedStateRevision =>
                layout.CanonicalApprovedInventoryBackupPath,
            AtlasIntakeContracts.QualifiedStateRevision =>
                layout.CanonicalQualifiedInventoryBackupPath,
            AtlasIntakeContracts.PreflightedStateRevision =>
                layout.CanonicalPreflightedInventoryBackupPath,
            _ => throw new AtlasSafetyException("The intake-state revision is invalid."),
        };

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
        if (io.FileExists(path) && !io.DirectoryExists(path))
        {
            throw new AtlasSafetyException("The directory path is invalid.");
        }
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
        bool fileExists = io.FileExists(actualPath);
        bool directoryExists = io.DirectoryExists(actualPath);
        if (directoryExists)
        {
            throw new AtlasSafetyException("The file path is invalid.");
        }

        if (fileExists)
        {
            ValidateExistingOrdinaryFile(actualPath, io);
        }

        if (!requireExisting
            && !allowExistingOutput
            && (fileExists || directoryExists))
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
        int firstDestinationArtifactOrdinal) =>
        CreateCopyPlan(manifest.PendingManifest, firstDestinationArtifactOrdinal);

    internal static AtlasCopyPlanDocument CreateCopyPlan(
        AtlasCorpusIntakeManifest manifest,
        int firstDestinationArtifactOrdinal)
    {
        List<AtlasCopyPlanEntry> entries = [];
        int nextOrdinal = firstDestinationArtifactOrdinal;
        foreach (AtlasManifestSaveEntry saveEntry in manifest.SaveEntries
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

        foreach (AtlasManifestDefinitionEntry entry in manifest.DefinitionEntries
                     .Where(static candidate =>
                         StringComparer.Ordinal.Equals(
                             candidate.Decision,
                             AtlasIntakeContracts.IncludeDefinitionDecision))
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
            SurveyAlias = manifest.SurveyAlias,
            ManifestRevision = AtlasIntakeContracts.PendingManifestRevision,
            Entries = [.. entries],
        };
    }

    internal static DiscoveryPhaseAliases ResolveDiscoveryAliases(
        PhaseInventoryContext inventoryContext)
    {
        string? requestAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.DiscoverRequestPurpose);
        string? manifestAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.ManifestRevision4Purpose);
        string? sourceRootMapAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.SourceRootMapPurpose);
        string? copyPlanAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.CopyPlanPurpose);
        string? stateAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.State1Purpose);
        string? backupAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.DiscoveryInventoryBackupPurpose);
        if (requestAlias is not null
            || manifestAlias is not null
            || sourceRootMapAlias is not null
            || copyPlanAlias is not null
            || stateAlias is not null
            || backupAlias is not null)
        {
            if (requestAlias is null
                || manifestAlias is null
                || sourceRootMapAlias is null
                || copyPlanAlias is null
                || stateAlias is null
                || backupAlias is null)
            {
                throw new AtlasSafetyException(
                    "The discovered inventory transition is incomplete.");
            }

            return new DiscoveryPhaseAliases(
                requestAlias,
                manifestAlias,
                sourceRootMapAlias,
                copyPlanAlias,
                stateAlias,
                backupAlias);
        }

        int nextOrdinal =
            GetMaximumArtifactOrdinal(inventoryContext.PriorInventory.Document) + 1;
        return new DiscoveryPhaseAliases(
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal));
    }

    internal static int GetNextDiscoveryDestinationArtifactOrdinal(
        DiscoveryPhaseAliases aliases) =>
        AtlasIntakeContracts.ParseArtifactOrdinal(aliases.InventoryBackupAlias) + 1;

    internal static ConfirmationPhaseAliases ResolveConfirmationAliases(
        PhaseInventoryContext inventoryContext,
        AtlasCopyPlanDocument copyPlan)
    {
        string? requestAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.ConfirmRequestPurpose);
        string? manifestAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.ManifestRevision5Purpose);
        string? stateAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.State2Purpose);
        string? backupAlias = TrustedLocalCopy.TryFindPhaseAlias(
            inventoryContext.CurrentInventory.Document,
            AtlasIntakeContracts.ApprovedInventoryBackupPurpose);
        if (requestAlias is not null
            || manifestAlias is not null
            || stateAlias is not null
            || backupAlias is not null)
        {
            if (requestAlias is null
                || manifestAlias is null
                || stateAlias is null
                || backupAlias is null)
            {
                throw new AtlasSafetyException("The approved inventory transition is incomplete.");
            }

            return new ConfirmationPhaseAliases(
                requestAlias,
                manifestAlias,
                stateAlias,
                backupAlias);
        }

        int nextOrdinal = Math.Max(
                GetMaximumArtifactOrdinal(inventoryContext.PriorInventory.Document),
                GetMaximumArtifactOrdinal(copyPlan))
            + 1;
        return new ConfirmationPhaseAliases(
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal++),
            AtlasIntakeContracts.FormatArtifactAlias(nextOrdinal));
    }

    internal static AtlasPrivateArtifactInventoryDocument CreateDiscoveredInventory(
        AtlasPrivateArtifactInventoryDocument priorInventory,
        string baselineManifestAlias,
        DiscoveryPhaseAliases aliases) =>
        priorInventory with
        {
            Artifacts =
            [
                .. priorInventory.Artifacts,
                CreateArtifactEntry(
                    aliases.RequestAlias,
                    AtlasIntakeContracts.PrivateEvidenceArtifactClass,
                    AtlasIntakeContracts.DiscoverRequestPurpose,
                    [],
                    "A8",
                    AtlasIntakeContracts.DeleteDisposition,
                    "atlas-cli:intake-discover"),
                CreateArtifactEntry(
                    aliases.ManifestAlias,
                    AtlasIntakeContracts.LiveDiscoveryArtifactClass,
                    AtlasIntakeContracts.ManifestRevision4Purpose,
                    [baselineManifestAlias],
                    "A2",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "atlas-intake/v2;r000004"),
                CreateArtifactEntry(
                    aliases.SourceRootMapAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.SourceRootMapPurpose,
                    [aliases.ManifestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.SourceRootMapSchemaVersion),
                CreateArtifactEntry(
                    aliases.CopyPlanAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.CopyPlanPurpose,
                    [aliases.ManifestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.CopyPlanSchemaVersion),
                CreateArtifactEntry(
                    aliases.StateAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.State1Purpose,
                    [aliases.ManifestAlias, aliases.SourceRootMapAlias, aliases.CopyPlanAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.IntakeStateSchemaVersion),
                CreateArtifactEntry(
                    aliases.InventoryBackupAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.DiscoveryInventoryBackupPurpose,
                    [aliases.RequestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "inventory-backup:discovered"),
            ],
        };

    internal static AtlasIntakeStateDocument CreateDiscoveredState(
        AtlasLoadedDocument<AtlasIntakeDiscoveryRequest> request,
        AtlasWorkspaceLayout layout,
        string baselineManifestAlias,
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> baselineManifest,
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> pendingManifest,
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap,
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan,
        DiscoveryPhaseAliases aliases,
        string backupSha256,
        string inventorySha256) =>
        new()
        {
            SchemaVersion = AtlasIntakeContracts.IntakeStateSchemaVersion,
            SurveyAlias = request.Document.SurveyAlias,
            StateRevision = AtlasIntakeContracts.DiscoveredStateRevision,
            Phase = AtlasIntakeContracts.DiscoveredPhase,
            StateArtifactAlias = aliases.StateAlias,
            SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
            BuildId = AtlasIntakeContracts.ExactBuildId,
            InventorySha256 = inventorySha256,
            DocumentBindings =
            [
                CreateDocumentBinding(
                    AtlasIntakeContracts.BaselineManifestRole,
                    baselineManifestAlias,
                    baselineManifest.AbsolutePath,
                    layout,
                    baselineManifest.Sha256),
                CreateDocumentBinding(
                    AtlasIntakeContracts.PendingManifestRole,
                    aliases.ManifestAlias,
                    pendingManifest.AbsolutePath,
                    layout,
                    pendingManifest.Sha256),
                CreateDocumentBinding(
                    AtlasIntakeContracts.SourceRootMapRole,
                    aliases.SourceRootMapAlias,
                    sourceRootMap.AbsolutePath,
                    layout,
                    sourceRootMap.Sha256),
                CreateDocumentBinding(
                    AtlasIntakeContracts.CopyPlanRole,
                    aliases.CopyPlanAlias,
                    copyPlan.AbsolutePath,
                    layout,
                    copyPlan.Sha256),
            ],
            ArtifactBindings =
            [
                CreateArtifactBinding(
                    AtlasIntakeContracts.DiscoveredRequestRole,
                    aliases.RequestAlias,
                    request.AbsolutePath,
                    layout,
                    request.Sha256),
                new AtlasArtifactBinding
                {
                    Role = AtlasIntakeContracts.DiscoveredInventoryBackupRole,
                    ArtifactAlias = aliases.InventoryBackupAlias,
                    RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                        layout.WorkspaceRoot,
                        layout.CanonicalDiscoveredInventoryBackupPath),
                    Sha256 = backupSha256,
                },
            ],
        };

    internal static AtlasPrivateArtifactInventoryDocument CreateApprovedInventory(
        AtlasPrivateArtifactInventoryDocument priorInventory,
        string pendingManifestAlias,
        string discoveredStateAlias,
        ConfirmationPhaseAliases aliases) =>
        priorInventory with
        {
            Artifacts =
            [
                .. priorInventory.Artifacts,
                CreateArtifactEntry(
                    aliases.RequestAlias,
                    AtlasIntakeContracts.PrivateEvidenceArtifactClass,
                    AtlasIntakeContracts.ConfirmRequestPurpose,
                    [],
                    "A8",
                    AtlasIntakeContracts.DeleteDisposition,
                    "atlas-cli:intake-confirm"),
                CreateArtifactEntry(
                    aliases.ManifestAlias,
                    AtlasIntakeContracts.LiveDiscoveryArtifactClass,
                    AtlasIntakeContracts.ManifestRevision5Purpose,
                    [pendingManifestAlias],
                    "A2",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "atlas-intake/v2;r000005"),
                CreateArtifactEntry(
                    aliases.StateAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.State2Purpose,
                    [discoveredStateAlias, aliases.ManifestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    AtlasIntakeContracts.IntakeStateSchemaVersion),
                CreateArtifactEntry(
                    aliases.InventoryBackupAlias,
                    AtlasIntakeContracts.PrivateProvenanceArtifactClass,
                    AtlasIntakeContracts.ApprovedInventoryBackupPurpose,
                    [aliases.RequestAlias],
                    "A8",
                    AtlasIntakeContracts.RetainPrivateDisposition,
                    "inventory-backup:approved"),
            ],
        };

    internal static AtlasIntakeStateDocument CreateApprovedState(
        AtlasLoadedDocument<AtlasIntakeConfirmationRequest> request,
        AtlasWorkspaceLayout layout,
        AtlasLoadedDocument<AtlasIntakeStateDocument> discoveredState,
        AtlasLoadedDocument<AtlasCorpusIntakeManifest> approvedManifest,
        AtlasLoadedDocument<AtlasSourceRootMapDocument> sourceRootMap,
        AtlasLoadedDocument<AtlasCopyPlanDocument> copyPlan,
        ConfirmationPhaseAliases aliases,
        string backupSha256,
        string inventorySha256) =>
        new()
        {
            SchemaVersion = AtlasIntakeContracts.IntakeStateSchemaVersion,
            SurveyAlias = request.Document.SurveyAlias,
            StateRevision = AtlasIntakeContracts.ApprovedStateRevision,
            Phase = AtlasIntakeContracts.ApprovedPhase,
            StateArtifactAlias = aliases.StateAlias,
            SteamAppId = AtlasIntakeContracts.ExactSteamAppId,
            BuildId = AtlasIntakeContracts.ExactBuildId,
            InventorySha256 = inventorySha256,
            DecisionCommit = request.Document.DecisionCommit,
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
                    aliases.ManifestAlias,
                    approvedManifest.AbsolutePath,
                    layout,
                    approvedManifest.Sha256),
                CreateDocumentBinding(
                    AtlasIntakeContracts.SourceRootMapRole,
                    AtlasIntakeContracts.GetRequiredDocumentBinding(
                        discoveredState.Document,
                        AtlasIntakeContracts.SourceRootMapRole).ArtifactAlias,
                    sourceRootMap.AbsolutePath,
                    layout,
                    sourceRootMap.Sha256),
                CreateDocumentBinding(
                    AtlasIntakeContracts.CopyPlanRole,
                    AtlasIntakeContracts.GetRequiredDocumentBinding(
                        discoveredState.Document,
                        AtlasIntakeContracts.CopyPlanRole).ArtifactAlias,
                    copyPlan.AbsolutePath,
                    layout,
                    copyPlan.Sha256),
            ],
            ArtifactBindings =
            [
                CreateArtifactBinding(
                    AtlasIntakeContracts.ConfirmRequestRole,
                    aliases.RequestAlias,
                    request.AbsolutePath,
                    layout,
                    request.Sha256),
                new AtlasArtifactBinding
                {
                    Role = AtlasIntakeContracts.ApprovedInventoryBackupRole,
                    ArtifactAlias = aliases.InventoryBackupAlias,
                    RelativePath = AtlasIntakeContracts.ToSurveyRelativePath(
                        layout.WorkspaceRoot,
                        layout.CanonicalApprovedInventoryBackupPath),
                    Sha256 = backupSha256,
                },
            ],
        };

    internal static string FindManifestArtifactAlias(
        AtlasPrivateArtifactInventoryDocument inventory,
        string purpose)
    {
        AtlasPrivateArtifactEntry[] matches = inventory.Artifacts
            .Where(artifact =>
                StringComparer.Ordinal.Equals(
                    artifact.ArtifactClass,
                    AtlasIntakeContracts.LiveDiscoveryArtifactClass)
                && StringComparer.Ordinal.Equals(artifact.Purpose, purpose))
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
        Func<string, CancellationToken, ValueTask<string>> readShaAsync,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(phase);
        ArgumentNullException.ThrowIfNull(expectedBytes);
        ArgumentNullException.ThrowIfNull(readShaAsync);
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
            await ValidatePublishedDocumentAsync(
                    finalPath,
                    expectedSha256,
                    readShaAsync,
                    cancellationToken)
                .ConfigureAwait(false);
            return new PublishedFile(finalPath, expectedSha256);
        }

        if (io.FileExists(stagingPath))
        {
            byte[] existingStagingBytes = await io.ReadAllBytesAsync(stagingPath, cancellationToken)
                .ConfigureAwait(false);
            EnsureBytesMatch(expectedBytes, existingStagingBytes);
            await MoveValidatedFileAsync(
                    stagingPath,
                    finalPath,
                    expectedSha256,
                    readShaAsync,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
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
        await ValidatePublishedDocumentAsync(
                stagingPath,
                expectedSha256,
                readShaAsync,
                cancellationToken)
            .ConfigureAwait(false);
        await MoveValidatedFileAsync(
                stagingPath,
                finalPath,
                expectedSha256,
                readShaAsync,
                io,
                cancellationToken)
            .ConfigureAwait(false);
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
            if (io.FileExists(stagingPath) || io.DirectoryExists(stagingPath))
            {
                throw new AtlasSafetyException(
                    "The inventory replacement left unexpected staging content.");
            }

            if (!io.FileExists(backupPath))
            {
                throw new AtlasSafetyException("The inventory backup is missing.");
            }

            AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> currentInventory =
                await AtlasIntakeContracts.ReadInventoryAsync(inventoryPath, cancellationToken)
                    .ConfigureAwait(false);
            AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> backupInventory =
                await AtlasIntakeContracts.ReadInventoryAsync(backupPath, cancellationToken)
                    .ConfigureAwait(false);
            EnsureDigestMatches(
                priorSha256,
                backupInventory.Sha256,
                static () => new AtlasSafetyException("The inventory backup digest is invalid."));
            return new InventoryReplaceResult(
                inventoryPath,
                backupPath,
                currentInventory.Sha256,
                backupInventory.Sha256);
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

        _ = await AtlasIntakeContracts.ReadInventoryAsync(stagingPath, cancellationToken)
            .ConfigureAwait(false);
        io.ReplaceFile(stagingPath, inventoryPath, backupPath);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> replacedInventory =
            await AtlasIntakeContracts.ReadInventoryAsync(inventoryPath, cancellationToken)
                .ConfigureAwait(false);
        EnsureBytesMatch(replacementInventoryBytes, replacedInventory.Bytes);
        AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument> backupResult =
            await AtlasIntakeContracts.ReadInventoryAsync(backupPath, cancellationToken)
                .ConfigureAwait(false);
        EnsureBytesMatch(priorInventoryBytes, backupResult.Bytes);
        return new InventoryReplaceResult(
            inventoryPath,
            backupPath,
            replacedInventory.Sha256,
            backupResult.Sha256);
    }

    internal static async ValueTask MoveValidatedFileAsync(
        string stagingPath,
        string finalPath,
        string expectedSha256,
        Func<string, CancellationToken, ValueTask<string>> readShaAsync,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        await ValidatePublishedDocumentAsync(
                stagingPath,
                expectedSha256,
                readShaAsync,
                cancellationToken)
            .ConfigureAwait(false);
        io.MoveFile(stagingPath, finalPath);
        await ValidatePublishedDocumentAsync(
                finalPath,
                expectedSha256,
                readShaAsync,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask ValidatePublishedDocumentAsync(
        string path,
        string expectedSha256,
        Func<string, CancellationToken, ValueTask<string>> readShaAsync,
        CancellationToken cancellationToken)
    {
        string actualSha256 = await readShaAsync(path, cancellationToken).ConfigureAwait(false);
        EnsureDigestMatches(
            expectedSha256,
            actualSha256,
            static () => new AtlasSafetyException("The published bytes are invalid."));
    }

    internal static async ValueTask<string> ReadManifestShaAsync(
        string path,
        CancellationToken cancellationToken) =>
        (await AtlasIntakeContracts.ReadManifestAsync(path, cancellationToken)
            .ConfigureAwait(false))
        .Sha256;

    internal static async ValueTask<string> ReadSourceRootMapShaAsync(
        string path,
        CancellationToken cancellationToken) =>
        (await AtlasIntakeContracts.ReadSourceRootMapAsync(path, cancellationToken)
                .ConfigureAwait(false))
        .Sha256;

    internal static async ValueTask<string> ReadCopyPlanShaAsync(
        string path,
        CancellationToken cancellationToken) =>
        (await AtlasIntakeContracts.ReadCopyPlanAsync(path, cancellationToken)
                .ConfigureAwait(false))
        .Sha256;

    internal static async ValueTask<string> ReadStateShaAsync(
        string path,
        CancellationToken cancellationToken) =>
        (await AtlasIntakeContracts.ReadStateAsync(path, cancellationToken).ConfigureAwait(false))
        .Sha256;

    internal static async ValueTask<string> ReadCopyReceiptShaAsync(
        string path,
        CancellationToken cancellationToken) =>
        (await AtlasIntakeContracts.ReadCopyReceiptAsync(path, cancellationToken)
                .ConfigureAwait(false))
        .Sha256;

    internal static async ValueTask<string> ReadCleanupReportShaAsync(
        string path,
        CancellationToken cancellationToken) =>
        (await AtlasIntakeContracts.ReadCleanupPreflightReportAsync(path, cancellationToken)
                .ConfigureAwait(false))
        .Sha256;

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
            baselineManifest.DefinitionGroups,
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
        IReadOnlyList<AtlasManifestDefinitionGroup> definitionGroups,
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
                AtlasManifestDefinitionGroup? definitionGroup = TryGetDefinitionGroupForPath(
                    definitionGroups,
                    relativePath);
                if (definitionGroup is null)
                {
                    continue;
                }

                if (!baselineDefinitionEntries.TryGetValue(
                        relativePath,
                        out AtlasManifestDefinitionEntry? baseline))
                {
                    throw new AtlasSafetyException("The definition discovery denominator changed.");
                }

                EnsureManifestDefinitionEntryMatchesBaseline(definitionGroup, baseline);
                if (!seenPaths.Add(relativePath))
                {
                    throw new AtlasSafetyException("The definition discovery denominator changed.");
                }

                results.Add(baseline with
                {
                    RelativePath = relativePath,
                    GroupId = definitionGroup.GroupId,
                    Decision = definitionGroup.Decision,
                    EntryType = AtlasIntakeContracts.FileEntryType,
                    IsReparsePoint = false,
                });
            }
        }
    }

    private static AtlasManifestDefinitionGroup? TryGetDefinitionGroupForPath(
        IReadOnlyList<AtlasManifestDefinitionGroup> definitionGroups,
        string relativePath)
    {
        AtlasManifestDefinitionGroup? match = null;
        foreach (AtlasManifestDefinitionGroup definitionGroup in definitionGroups)
        {
            if (!MatchesDefinitionSelectionRule(definitionGroup.SelectionRule, relativePath))
            {
                continue;
            }

            if (match is not null)
            {
                throw new AtlasSafetyException("The definition selection rules are ambiguous.");
            }

            match = definitionGroup;
        }

        return match;
    }

    private static bool MatchesDefinitionSelectionRule(string selectionRule, string relativePath)
    {
        string[] ruleSegments = SplitDefinitionSelectionRule(selectionRule);
        string[] pathSegments = AtlasIntakeContracts.NormalizeRelativePath(relativePath)
            .Split('/', StringSplitOptions.None);
        return MatchesDefinitionSelectionRule(ruleSegments, 0, pathSegments, 0);
    }

    private static string[] SplitDefinitionSelectionRule(string selectionRule)
    {
        string[] segments = AtlasIntakeContracts.SplitRelativePath(selectionRule);
        if (segments.Any(segment =>
                segment.Length == 0
                || StringComparer.Ordinal.Equals(segment, ".")
                || StringComparer.Ordinal.Equals(segment, "..")
                || segment.Contains(':')))
        {
            throw new AtlasSafetyException("The definition selection rule is invalid.");
        }

        return segments;
    }

    private static bool MatchesDefinitionSelectionRule(
        string[] ruleSegments,
        int ruleIndex,
        string[] pathSegments,
        int pathIndex)
    {
        while (ruleIndex < ruleSegments.Length)
        {
            if (StringComparer.Ordinal.Equals(ruleSegments[ruleIndex], "**"))
            {
                while (ruleIndex + 1 < ruleSegments.Length
                       && StringComparer.Ordinal.Equals(ruleSegments[ruleIndex + 1], "**"))
                {
                    ruleIndex++;
                }

                if (ruleIndex == ruleSegments.Length - 1)
                {
                    return true;
                }

                for (int candidateIndex = pathIndex;
                     candidateIndex <= pathSegments.Length;
                     candidateIndex++)
                {
                    if (MatchesDefinitionSelectionRule(
                            ruleSegments,
                            ruleIndex + 1,
                            pathSegments,
                            candidateIndex))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (pathIndex >= pathSegments.Length
                || !MatchesDefinitionSelectionRuleSegment(
                    ruleSegments[ruleIndex],
                    pathSegments[pathIndex]))
            {
                return false;
            }

            ruleIndex++;
            pathIndex++;
        }

        return pathIndex == pathSegments.Length;
    }

    private static bool MatchesDefinitionSelectionRuleSegment(
        string ruleSegment,
        string pathSegment)
    {
        int ruleIndex = 0;
        int pathIndex = 0;
        int starIndex = -1;
        int matchIndex = -1;
        while (pathIndex < pathSegment.Length)
        {
            if (ruleIndex < ruleSegment.Length
                && char.ToUpperInvariant(ruleSegment[ruleIndex])
                    == char.ToUpperInvariant(pathSegment[pathIndex]))
            {
                ruleIndex++;
                pathIndex++;
                continue;
            }

            if (ruleIndex < ruleSegment.Length && ruleSegment[ruleIndex] == '*')
            {
                starIndex = ruleIndex++;
                matchIndex = pathIndex;
                continue;
            }

            if (starIndex < 0)
            {
                return false;
            }

            ruleIndex = starIndex + 1;
            pathIndex = ++matchIndex;
        }

        while (ruleIndex < ruleSegment.Length && ruleSegment[ruleIndex] == '*')
        {
            ruleIndex++;
        }

        return ruleIndex == ruleSegment.Length;
    }

    internal static void EnsureManifestDefinitionEntryMatchesBaseline(
        AtlasManifestDefinitionGroup actualGroup,
        AtlasManifestDefinitionEntry baseline)
    {
        if (!StringComparer.Ordinal.Equals(actualGroup.GroupId, baseline.GroupId)
            || !StringComparer.Ordinal.Equals(actualGroup.Decision, baseline.Decision)
            || !StringComparer.Ordinal.Equals(
                baseline.EntryType,
                AtlasIntakeContracts.FileEntryType)
            || baseline.IsReparsePoint)
        {
            throw new AtlasSafetyException("The definition entry classification changed.");
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

    internal sealed record DiscoveryPhaseAliases(
        string RequestAlias,
        string ManifestAlias,
        string SourceRootMapAlias,
        string CopyPlanAlias,
        string StateAlias,
        string InventoryBackupAlias);

    internal sealed record ConfirmationPhaseAliases(
        string RequestAlias,
        string ManifestAlias,
        string StateAlias,
        string InventoryBackupAlias);

    internal sealed record StateValidationExpectations(
        AtlasLoadedDocument<AtlasIntakeDiscoveryRequest>? DiscoveryRequest = null,
        AtlasLoadedDocument<AtlasIntakeConfirmationRequest>? ConfirmationRequest = null,
        AtlasLoadedDocument<AtlasIntakeCopyRequest>? CopyRequest = null,
        AtlasLoadedDocument<AtlasCleanupPreflightRequest>? PreflightRequest = null);
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
