using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

[assembly: InternalsVisibleTo("Hcoona.CelesphoniaModifier.Atlas.Tests")]

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasIntakeContracts
{
    public const string DiscoveryRequestSchemaVersion = "atlas-intake-discovery-request/v1";
    public const string ConfirmationRequestSchemaVersion = "atlas-intake-confirmation-request/v1";
    public const string CopyRequestSchemaVersion = "atlas-intake-copy-request/v1";
    public const string CleanupPreflightRequestSchemaVersion =
        "atlas-cleanup-preflight-request/v1";
    public const string SourceRootMapSchemaVersion = "atlas-source-root-map/v1";
    public const string CopyPlanSchemaVersion = "atlas-copy-plan/v1";
    public const string IntakeStateSchemaVersion = "atlas-intake-state/v1";
    public const string CopyReceiptSchemaVersion = "atlas-copy-receipt/v1";
    public const string CleanupPreflightReportSchemaVersion = "atlas-cleanup-preflight/v1";
    public const string IntakeManifestSchemaVersion = "atlas-intake/v2";
    public const string InventorySchemaVersion = "atlas-private-inventory/v1";
    public const string TrustedLocalFilesystemProfile = "trusted-local-filesystem/v1";
    public const string SaveSnapshotRelativeRoot = "copies/snapshot-a2-000001";
    public const string IncompleteSaveSnapshotRelativeRoot = "copies/snapshot-a2-000001.incomplete";
    public const string ProjectLeaderRole = "project-leader";
    public const string ApprovalDecisionReferencePrefix = "commit:";
    public const string ExactSurveyAlias = "survey-000001";
    public const int ExactSteamAppId = 1786790;
    public const int ExactBuildId = 13624401;
    public const int ExactSaveEntryCount = 23;
    public const int ExactIncludedSaveCount = 21;
    public const int ExactExcludedSteamMetadataCount = 2;
    public const int ExactDefinitionEntryCount = 580;
    public const int ExactIncludedDefinitionCount = 496;
    public const int ExactExcludedDefinitionCount = 84;
    public const int MaxJsonDepth = 32;
    public const int BaselineManifestRevision = 3;
    public const int PendingManifestRevision = 4;
    public const int ApprovedManifestRevision = 5;
    public const int DiscoveredStateRevision = 1;
    public const int ApprovedStateRevision = 2;
    public const int QualifiedStateRevision = 3;
    public const int PreflightedStateRevision = 4;

    internal const string DeploymentRootSaveRole = "deployment-root-save";
    internal const string WebRootSaveRole = "web-root-save";
    internal const string IncludeSaveRootDecision = "include-save-root";
    internal const string ExcludeNoSaveInputsDecision = "exclude-no-save-inputs";
    internal const string IncludeSaveDecision = "include-save";
    internal const string ExcludeSteamAutoCloudDecision = "exclude-steam-autocloud";
    internal const string ExcludeNonSaveDecision = "exclude-nonsave";
    internal const string UnsupportedDecision = "unsupported";
    internal const string UnreadableDecision = "unreadable";
    internal const string ScopeNarrowedDecision = "scope-narrowed";
    internal const string PendingConfirmationStatus = "pending";
    internal const string ApprovedConfirmationStatus = "approved";
    internal const string RejectedConfirmationStatus = "rejected";
    internal const string SupersededConfirmationStatus = "superseded";
    internal const string DiscoveredPhase = "discovered";
    internal const string ApprovedPhase = "approved";
    internal const string QualifiedPhase = "qualified";
    internal const string PreflightedPhase = "preflighted";
    internal const string LiveDiscoveryArtifactClass = "live-discovery";
    internal const string SaveCopyArtifactClass = "save-copy";
    internal const string DefinitionCopyArtifactClass = "definition-copy";
    internal const string DecodedSaveArtifactClass = "decoded-save";
    internal const string PrivateEvidenceArtifactClass = "private-evidence";
    internal const string AgentEnvelopeArtifactClass = "agent-envelope";
    internal const string PrivateProvenanceArtifactClass = "private-provenance";
    internal const string PreservationManifestArtifactClass = "preservation-manifest";
    internal const string CleanupRecordArtifactClass = "cleanup-record";
    internal const string PlannedArtifactStatus = "planned";
    internal const string PresentArtifactStatus = "present";
    internal const string LastUseCompleteArtifactStatus = "last-use-complete";
    internal const string DeletionPendingArtifactStatus = "deletion-pending";
    internal const string DeletedArtifactStatus = "deleted";
    internal const string RetainedArtifactStatus = "retained";
    internal const string BlockedArtifactStatus = "blocked";
    internal const string DeleteDisposition = "delete";
    internal const string RetainPrivateDisposition = "retain-private";
    internal const string SupersedeDisposition = "supersede";
    internal const string NotApplicableDisposition = "not-applicable";
    internal const string PreservationUnqualifiedSaveQualification =
        "preservation-unqualified";
    internal const string A2QualifiedSaveQualification = "a2-qualified";
    internal const string AtlasToolValidationMethod = "atlas-tool";
    internal const string ManualA0ValidationMethod = "manual-a0";
    internal const string GlobalSaveRole = "global";
    internal const string ConfigSaveRole = "config";
    internal const string SlotSaveRole = "slot";
    internal const string SteamAutoCloudSaveRole = "steam-autocloud";
    internal const string OtherSaveRole = "other";
    internal const string ActiveSaveRootActivity = "active";
    internal const string InactiveSaveRootActivity = "inactive";
    internal const string IncludeDefinitionDecision = "include";
    internal const string ExcludeDefinitionDecision = "exclude";
    internal const string FileEntryType = "file";
    internal const string DirectoryEntryType = "directory";
    internal const string OtherEntryType = "other";
    internal const string DiscoveredRequestRole = "discover-request";
    internal const string ConfirmRequestRole = "confirm-request";
    internal const string CopyRequestRole = "copy-request";
    internal const string CleanupPreflightRequestRole = "cleanup-preflight-request";
    internal const string DiscoveredInventoryBackupRole = "discovered-inventory-backup";
    internal const string ApprovedInventoryBackupRole = "approved-inventory-backup";
    internal const string QualifiedInventoryBackupRole = "qualified-inventory-backup";
    internal const string PreflightedInventoryBackupRole = "preflighted-inventory-backup";
    internal const string BaselineManifestRole = "baseline-manifest";
    internal const string PendingManifestRole = "pending-manifest";
    internal const string ApprovedManifestRole = "approved-manifest";
    internal const string SourceRootMapRole = "source-root-map";
    internal const string CopyPlanRole = "copy-plan";
    internal const string CopyReceiptRole = "copy-receipt";
    internal const string CleanupPreflightReportRole = "cleanup-preflight-report";
    internal const string PredecessorStateRole = "predecessor-state";
    internal const string State1Purpose = "intake-state:r000001";
    internal const string State2Purpose = "intake-state:r000002";
    internal const string State3Purpose = "intake-state:r000003";
    internal const string State4Purpose = "intake-state:r000004";
    internal const string ManifestRevision3Purpose = "intake-manifest:r000003";
    internal const string ManifestRevision4Purpose = "intake-manifest:r000004";
    internal const string ManifestRevision5Purpose = "intake-manifest:r000005";
    internal const string SourceRootMapPurpose = "source-root-map";
    internal const string CopyPlanPurpose = "copy-plan";
    internal const string CopyReceiptPurpose = "copy-receipt";
    internal const string CleanupPreflightReportPurpose = "cleanup-preflight-report";
    internal const string DiscoveryInventoryBackupPurpose = "inventory-backup:discovered";
    internal const string ApprovedInventoryBackupPurpose = "inventory-backup:approved";
    internal const string QualifiedInventoryBackupPurpose = "inventory-backup:qualified";
    internal const string PreflightInventoryBackupPurpose = "inventory-backup:preflighted";
    internal const string DiscoverRequestPurpose = "request:discover";
    internal const string ConfirmRequestPurpose = "request:confirm";
    internal const string CopyRequestPurpose = "request:copy";
    internal const string CleanupPreflightRequestPurpose = "request:cleanup-preflight";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly AtlasJsonContext JsonContext = new(JsonOptions);
    private static readonly HashSet<string> AllowedInventoryArtifactClasses =
        new(StringComparer.Ordinal)
        {
            LiveDiscoveryArtifactClass,
            SaveCopyArtifactClass,
            DefinitionCopyArtifactClass,
            DecodedSaveArtifactClass,
            PrivateEvidenceArtifactClass,
            AgentEnvelopeArtifactClass,
            PrivateProvenanceArtifactClass,
            PreservationManifestArtifactClass,
            CleanupRecordArtifactClass,
        };
    private static readonly HashSet<string> AllowedInventoryStatuses =
        new(StringComparer.Ordinal)
        {
            PlannedArtifactStatus,
            PresentArtifactStatus,
            LastUseCompleteArtifactStatus,
            DeletionPendingArtifactStatus,
            DeletedArtifactStatus,
            RetainedArtifactStatus,
            BlockedArtifactStatus,
        };
    private static readonly HashSet<string> AllowedInventoryDispositions =
        new(StringComparer.Ordinal)
        {
            DeleteDisposition,
            RetainPrivateDisposition,
            SupersedeDisposition,
            NotApplicableDisposition,
        };
    private static readonly HashSet<string> AllowedCleanupPreflightResults =
        new(StringComparer.Ordinal)
        {
            "blocked-status",
            "blocked-disposition",
            "blocked-before-last-use",
            "indeterminate-expiry",
            "eligible-for-human-review",
        };

    internal static ValueTask<AtlasLoadedDocument<AtlasIntakeDiscoveryRequest>>
        ReadDiscoveryRequestAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        ReadDiscoveryRequestAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasIntakeDiscoveryRequest>>
        ReadDiscoveryRequestAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken = default) =>
        ReadRequestAsync(
            requestPath,
            "discover",
            JsonContext.AtlasIntakeDiscoveryRequest,
            static request => ValidateDiscoveryRequest(request),
            io,
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasIntakeConfirmationRequest>>
        ReadConfirmationRequestAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        ReadConfirmationRequestAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasIntakeConfirmationRequest>>
        ReadConfirmationRequestAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken = default) =>
        ReadRequestAsync(
            requestPath,
            "confirm",
            JsonContext.AtlasIntakeConfirmationRequest,
            static request => ValidateConfirmationRequest(request),
            io,
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasIntakeCopyRequest>>
        ReadCopyRequestAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        ReadCopyRequestAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasIntakeCopyRequest>>
        ReadCopyRequestAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken = default) =>
        ReadRequestAsync(
            requestPath,
            "copy",
            JsonContext.AtlasIntakeCopyRequest,
            static request => ValidateCopyRequest(request),
            io,
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasCleanupPreflightRequest>>
        ReadCleanupPreflightRequestAsync(
        string requestPath,
        CancellationToken cancellationToken = default) =>
        ReadCleanupPreflightRequestAsync(requestPath, AtlasIoSeams.Default, cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasCleanupPreflightRequest>>
        ReadCleanupPreflightRequestAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken = default) =>
        ReadRequestAsync(
            requestPath,
            "cleanup-preflight",
            JsonContext.AtlasCleanupPreflightRequest,
            static request => ValidateCleanupPreflightRequest(request),
            io,
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasCorpusIntakeManifest>> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReadDocumentAsync(
            path,
            JsonContext.AtlasCorpusIntakeManifest,
            static manifest => ValidateManifest(manifest),
            static message => new AtlasApprovalException(message),
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasPrivateArtifactInventoryDocument>>
        ReadInventoryAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReadDocumentAsync(
            path,
            JsonContext.AtlasPrivateArtifactInventoryDocument,
            static inventory => ValidateInventory(inventory),
            static message => new AtlasSafetyException(message),
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasSourceRootMapDocument>>
        ReadSourceRootMapAsync(
            string path,
            CancellationToken cancellationToken = default) =>
        ReadDocumentAsync(
            path,
            JsonContext.AtlasSourceRootMapDocument,
            static document => ValidateSourceRootMap(document),
            static message => new AtlasSafetyException(message),
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasCopyPlanDocument>> ReadCopyPlanAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReadDocumentAsync(
            path,
            JsonContext.AtlasCopyPlanDocument,
            static document => ValidateCopyPlan(document),
            static message => new AtlasSafetyException(message),
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasIntakeStateDocument>> ReadStateAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReadDocumentAsync(
            path,
            JsonContext.AtlasIntakeStateDocument,
            static document => ValidateState(document),
            static message => new AtlasApprovalException(message),
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasCopyReceiptDocument>> ReadCopyReceiptAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReadDocumentAsync(
            path,
            JsonContext.AtlasCopyReceiptDocument,
            static document => ValidateCopyReceipt(document),
            static message => new AtlasSafetyException(message),
            cancellationToken);

    internal static ValueTask<AtlasLoadedDocument<AtlasCleanupPreflightReportDocument>>
        ReadCleanupPreflightReportAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        ReadDocumentAsync(
            path,
            JsonContext.AtlasCleanupPreflightReportDocument,
            static document => ValidateCleanupPreflightReport(document),
            static message => new AtlasSafetyException(message),
            cancellationToken);

    internal static byte[] SerializeManifest(AtlasCorpusIntakeManifest manifest) =>
        SerializeDocument(
            manifest,
            JsonContext.AtlasCorpusIntakeManifest,
            static document => ValidateManifest(document));

    internal static byte[] SerializeInventory(AtlasPrivateArtifactInventoryDocument inventory) =>
        SerializeDocument(
            inventory,
            JsonContext.AtlasPrivateArtifactInventoryDocument,
            static document => ValidateInventory(document));

    internal static byte[] SerializeSourceRootMap(AtlasSourceRootMapDocument document) =>
        SerializeDocument(
            document,
            JsonContext.AtlasSourceRootMapDocument,
            static candidate => ValidateSourceRootMap(candidate));

    internal static byte[] SerializeCopyPlan(AtlasCopyPlanDocument document) =>
        SerializeDocument(
            document,
            JsonContext.AtlasCopyPlanDocument,
            static candidate => ValidateCopyPlan(candidate));

    internal static byte[] SerializeState(AtlasIntakeStateDocument document) =>
        SerializeDocument(
            document,
            JsonContext.AtlasIntakeStateDocument,
            static candidate => ValidateState(candidate));

    internal static byte[] SerializeCopyReceipt(AtlasCopyReceiptDocument document) =>
        SerializeDocument(
            document,
            JsonContext.AtlasCopyReceiptDocument,
            static candidate => ValidateCopyReceipt(candidate));

    internal static byte[] SerializeCleanupPreflightReport(
        AtlasCleanupPreflightReportDocument document) =>
        SerializeDocument(
            document,
            JsonContext.AtlasCleanupPreflightReportDocument,
            static candidate => ValidateCleanupPreflightReport(candidate));

    internal static byte[] SerializeRequest(AtlasIntakeDiscoveryRequest request) =>
        SerializeDocument(request, JsonContext.AtlasIntakeDiscoveryRequest);

    internal static byte[] SerializeRequest(AtlasIntakeConfirmationRequest request) =>
        SerializeDocument(request, JsonContext.AtlasIntakeConfirmationRequest);

    internal static byte[] SerializeRequest(AtlasIntakeCopyRequest request) =>
        SerializeDocument(request, JsonContext.AtlasIntakeCopyRequest);

    internal static byte[] SerializeRequest(AtlasCleanupPreflightRequest request) =>
        SerializeDocument(request, JsonContext.AtlasCleanupPreflightRequest);

    internal static string ComputeSha256Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string ToSurveyRelativePath(string workspaceRoot, string absolutePath)
    {
        string normalizedWorkspace = NormalizePath(workspaceRoot);
        string normalizedPath = NormalizePath(absolutePath);
        AssertContainsPath(normalizedWorkspace, normalizedPath);
        string rootWithSeparator = AppendDirectorySeparator(normalizedWorkspace);
        if (StringComparer.OrdinalIgnoreCase.Equals(normalizedWorkspace, normalizedPath))
        {
            throw new AtlasSafetyException("A child output must not equal the workspace root.");
        }

        return normalizedPath[rootWithSeparator.Length..].Replace('\\', '/');
    }

    internal static string NormalizePath(string path) => Path.GetFullPath(path);

    internal static string NormalizeRelativePath(string relativePath)
    {
        string[] segments = SplitRelativePath(relativePath);
        return string.Join("/", segments);
    }

    internal static string[] SplitRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new AtlasSafetyException("The relative path is required.");
        }

        return relativePath.Split(['\\', '/'], StringSplitOptions.None);
    }

    internal static bool PathEquals(string first, string second) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            NormalizePath(first).TrimEnd(Path.DirectorySeparatorChar, '/'),
            NormalizePath(second).TrimEnd(Path.DirectorySeparatorChar, '/'));

    internal static string AppendDirectorySeparator(string path)
    {
        string normalized = NormalizePath(path).TrimEnd(Path.DirectorySeparatorChar, '/');
        return normalized + Path.DirectorySeparatorChar;
    }

    internal static void AssertContainsPath(string rootPath, string candidatePath)
    {
        string normalizedRoot = NormalizePath(rootPath).TrimEnd(Path.DirectorySeparatorChar, '/');
        string normalizedCandidate = NormalizePath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, '/');
        string rootWithSeparator = AppendDirectorySeparator(normalizedRoot);
        if (StringComparer.OrdinalIgnoreCase.Equals(normalizedRoot, normalizedCandidate))
        {
            return;
        }

        if (!normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new AtlasSafetyException("The path is outside the approved root.");
        }
    }

    internal static bool IsCanonicalSurveyRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value[0] is '/' or '.'
            || value.Contains('\\', StringComparison.Ordinal)
            || HasAnyColon(value))
        {
            return false;
        }

        foreach (string segment in value.Split('/', StringSplitOptions.None))
        {
            if (segment.Length == 0
                || StringComparer.Ordinal.Equals(segment, ".")
                || StringComparer.Ordinal.Equals(segment, ".."))
            {
                return false;
            }
        }

        return true;
    }

    internal static string FormatArtifactAlias(int ordinal) =>
        $"private-artifact-{ordinal:000000}";

    internal static int ParseArtifactOrdinal(string alias)
    {
        if (!alias.StartsWith("private-artifact-", StringComparison.Ordinal)
            || alias.Length != "private-artifact-".Length + 6
            || !int.TryParse(alias.AsSpan("private-artifact-".Length), out int ordinal))
        {
            throw new AtlasSafetyException("The artifact alias is invalid.");
        }

        return ordinal;
    }

    internal static string FormatStatePath(int revision) =>
        $"intake/states/atlas-intake-state.r{revision:000000}.json";

    internal static string GetPhaseName(int revision) =>
        revision switch
        {
            DiscoveredStateRevision => DiscoveredPhase,
            ApprovedStateRevision => ApprovedPhase,
            QualifiedStateRevision => QualifiedPhase,
            PreflightedStateRevision => PreflightedPhase,
            _ => throw new AtlasSafetyException("The intake-state revision is invalid."),
        };

    internal static string GetInventoryBackupRelativePath(string phase) =>
        $"intake/inventory-backups/private-artifact-inventory.{phase}.json";

    internal static string GetCanonicalRequestRelativePath(string command) =>
        command switch
        {
            "discover" => "intake/requests/discover.json",
            "confirm" => "intake/requests/confirm.json",
            "copy" => "intake/requests/copy.json",
            "cleanup-preflight" => "intake/requests/cleanup-preflight.json",
            _ => throw new AtlasSafetyException("The command name is invalid."),
        };

    internal static string GetExpectedStateRelativePath(int revision) => FormatStatePath(revision);

    internal static AtlasWorkspaceLayout CreateWorkspaceLayout(
        string projectRoot,
        string workspaceRoot,
        string surveyAlias)
    {
        ValidateSurveyAlias(surveyAlias);
        ValidateAbsoluteDosPath(projectRoot, nameof(projectRoot));
        ValidateAbsoluteDosPath(workspaceRoot, nameof(workspaceRoot));

        string normalizedProjectRoot = NormalizePath(projectRoot);
        string expectedWorkspaceRoot = NormalizePath(
            Path.Combine(
                normalizedProjectRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier",
                ".private",
                "atlas-v0",
                surveyAlias));
        if (!PathEquals(workspaceRoot, expectedWorkspaceRoot))
        {
            throw new AtlasSafetyException("The workspace root does not match the canonical path.");
        }

        return new AtlasWorkspaceLayout(normalizedProjectRoot, expectedWorkspaceRoot, surveyAlias);
    }

    internal static void ValidateRequestFilePathBeforeRead(
        string requestPath,
        string commandName,
        AtlasIoSeams io)
    {
        ValidateAbsoluteDosPath(requestPath, nameof(requestPath));
        ValidateCanonicalRequestPath(requestPath, commandName);
        ValidateExistingRequestFilePath(requestPath, io);
    }

    internal static void ValidateAbsoluteDosPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AtlasRequestException($"The path '{parameterName}' is required.");
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal)
            || path.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            throw new AtlasRequestException($"The path '{parameterName}' must be a DOS path.");
        }

        if (path.Length < 3
            || !char.IsAsciiLetter(path[0])
            || path[1] != ':'
            || !IsDirectorySeparator(path[2]))
        {
            throw new AtlasRequestException($"The path '{parameterName}' must be absolute.");
        }

        if (HasColonBeyondDriveDesignator(path))
        {
            throw new AtlasRequestException(
                $"The path '{parameterName}' contains an unexpected colon.");
        }

        try
        {
            _ = NormalizePath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or IOException
            or NotSupportedException)
        {
            throw new AtlasRequestException(
                $"The path '{parameterName}' is not a valid DOS path.",
                exception);
        }
    }

    private static void ValidateCanonicalRequestPath(string requestPath, string commandName)
    {
        string normalizedPath = NormalizePath(requestPath);
        if (!StringComparer.OrdinalIgnoreCase.Equals(requestPath, normalizedPath))
        {
            throw new AtlasRequestException("The request path must be canonical.");
        }

        string requestFileName = commandName switch
        {
            "discover" => "discover.json",
            "confirm" => "confirm.json",
            "copy" => "copy.json",
            "cleanup-preflight" => "cleanup-preflight.json",
            _ => throw new AtlasSafetyException("The command name is invalid."),
        };

        string? requestsDirectory = Path.GetDirectoryName(normalizedPath);
        string? intakeDirectory = requestsDirectory is null
            ? null
            : Path.GetDirectoryName(requestsDirectory);
        string? workspaceRoot = intakeDirectory is null
            ? null
            : Path.GetDirectoryName(intakeDirectory);
        if (requestsDirectory is null
            || intakeDirectory is null
            || workspaceRoot is null
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(normalizedPath),
                requestFileName)
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(requestsDirectory),
                "requests")
            || !StringComparer.OrdinalIgnoreCase.Equals(
                Path.GetFileName(intakeDirectory),
                "intake")
            || !IsCanonicalRequestWorkspaceRoot(workspaceRoot))
        {
            throw new AtlasSafetyException("The request path is not canonical.");
        }
    }

    private static bool IsCanonicalRequestWorkspaceRoot(string workspaceRoot)
    {
        string normalizedWorkspaceRoot = NormalizePath(workspaceRoot);
        string root = Path.GetPathRoot(normalizedWorkspaceRoot)
            ?? throw new AtlasSafetyException("The request path root is invalid.");
        string[] segments = normalizedWorkspaceRoot[root.Length..]
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        string[] expectedSegments =
        [
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            ".private",
            "atlas-v0",
            ExactSurveyAlias,
        ];
        if (segments.Length < expectedSegments.Length)
        {
            return false;
        }

        int offset = segments.Length - expectedSegments.Length;
        for (int index = 0; index < expectedSegments.Length; index++)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(
                    segments[offset + index],
                    expectedSegments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateExistingRequestFilePath(string requestPath, AtlasIoSeams io)
    {
        string normalizedPath = NormalizePath(requestPath);
        EnsureRequestPathUsesFixedDrive(normalizedPath, io);
        string root = Path.GetPathRoot(normalizedPath)
            ?? throw new AtlasRequestException("The request path root is invalid.");
        ValidateRequestPathComponent(root, io, expectDirectory: true);
        string[] segments = normalizedPath[root.Length..]
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        string current = root;
        for (int index = 0; index < segments.Length; index++)
        {
            string next = Path.Combine(current, segments[index]);
            bool fileExists = io.FileExists(next);
            bool directoryExists = io.DirectoryExists(next);
            if (!fileExists && !directoryExists)
            {
                ValidateMissingRequestPathSegments(segments, index);
                return;
            }

            ValidateRequestPathComponent(
                next,
                io,
                expectDirectory: index != segments.Length - 1);
            current = next;
        }
    }

    private static void ValidateMissingRequestPathSegments(string[] segments, int startIndex)
    {
        for (int index = startIndex; index < segments.Length; index++)
        {
            if (IsReservedDosDeviceComponent(segments[index]))
            {
                throw new AtlasSafetyException("Device paths are not allowed.");
            }
        }
    }

    private static bool IsReservedDosDeviceComponent(string component)
    {
        string trimmedComponent = component.TrimEnd(' ', '.');
        int extensionIndex = trimmedComponent.IndexOf('.');
        string deviceName = extensionIndex >= 0
            ? trimmedComponent[..extensionIndex]
            : trimmedComponent;
        deviceName = deviceName.TrimEnd(' ');
        return StringComparer.OrdinalIgnoreCase.Equals(deviceName, "CON")
            || StringComparer.OrdinalIgnoreCase.Equals(deviceName, "PRN")
            || StringComparer.OrdinalIgnoreCase.Equals(deviceName, "AUX")
            || StringComparer.OrdinalIgnoreCase.Equals(deviceName, "NUL")
            || StringComparer.OrdinalIgnoreCase.Equals(deviceName, "CLOCK$")
            || StringComparer.OrdinalIgnoreCase.Equals(deviceName, "CONIN$")
            || StringComparer.OrdinalIgnoreCase.Equals(deviceName, "CONOUT$")
            || IsNumberedDosDevice(deviceName, "COM")
            || IsNumberedDosDevice(deviceName, "LPT");
    }

    private static bool IsNumberedDosDevice(string component, string prefix) =>
        component.Length == prefix.Length + 1
        && component.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && component[prefix.Length] is >= '1' and <= '9';

    private static void ValidateRequestPathComponent(
        string path,
        AtlasIoSeams io,
        bool expectDirectory)
    {
        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new AtlasSafetyException("Reparse points are not allowed.");
        }

        if ((attributes & FileAttributes.Device) != 0)
        {
            throw new AtlasSafetyException("Device paths are not allowed.");
        }

        bool isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (expectDirectory && !isDirectory)
        {
            throw new AtlasRequestException("A request path component is not a directory.");
        }

        if (!expectDirectory && isDirectory)
        {
            throw new AtlasRequestException("The request path must be a file.");
        }
    }

    private static void EnsureRequestPathUsesFixedDrive(string path, AtlasIoSeams io)
    {
        string root = Path.GetPathRoot(NormalizePath(path))
            ?? throw new AtlasRequestException("The request path root is invalid.");
        AtlasDriveInfo drive = io.GetDriveInfo(root);
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
        {
            throw new AtlasSafetyException("The request path must use a ready fixed drive.");
        }
    }

    internal static void ValidateSurveyAlias(string value)
    {
        if (!StringComparer.Ordinal.Equals(value, ExactSurveyAlias))
        {
            throw new AtlasRequestException("The surveyAlias is invalid.");
        }
    }

    internal static void ValidateLowerHexDigest(string value, string parameterName)
    {
        if (value.Length != 64 || !value.All(static character => char.IsAsciiHexDigit(character)))
        {
            throw new AtlasRequestException($"The '{parameterName}' digest is invalid.");
        }

        if (value.Any(
                static character =>
                    char.IsAsciiLetter(character) && !IsLowerAsciiLetter(character)))
        {
            throw new AtlasRequestException($"The '{parameterName}' digest is invalid.");
        }
    }

    internal static void ValidateGitCommit(string value, string parameterName)
    {
        if (value.Length != 40
            || value.Any(static character =>
                !(char.IsAsciiDigit(character)
                    || (IsLowerAsciiLetter(character) && character is >= 'a' and <= 'f'))))
        {
            throw new AtlasRequestException($"The '{parameterName}' commit is invalid.");
        }
    }

    internal static void ValidateCanonicalRelativePath(string value, string parameterName)
    {
        if (!IsCanonicalSurveyRelativePath(value))
        {
            throw new AtlasSafetyException($"The '{parameterName}' relative path is invalid.");
        }
    }

    internal static AtlasDocumentBinding GetRequiredDocumentBinding(
        AtlasIntakeStateDocument state,
        string role)
    {
        AtlasDocumentBinding? binding = state.DocumentBindings
            .SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Role, role));
        return binding
            ?? throw new AtlasApprovalException($"State '{state.Phase}' lacks '{role}'.");
    }

    internal static AtlasArtifactBinding GetRequiredArtifactBinding(
        AtlasIntakeStateDocument state,
        string role)
    {
        AtlasArtifactBinding? binding = state.ArtifactBindings
            .SingleOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Role, role));
        return binding
            ?? throw new AtlasApprovalException($"State '{state.Phase}' lacks '{role}'.");
    }

    private static async ValueTask<AtlasLoadedDocument<TDocument>> ReadRequestAsync<TDocument>(
        string requestPath,
        string commandName,
        JsonTypeInfo<TDocument> typeInfo,
        Action<TDocument> validator,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
        where TDocument : class
    {
        ValidateRequestFilePathBeforeRead(requestPath, commandName, io);
        AtlasLoadedDocument<TDocument> document = await ReadDocumentAsync(
                requestPath,
                typeInfo,
                validator,
                static message => new AtlasRequestException(message),
                io,
                cancellationToken)
            .ConfigureAwait(false);
        return document;
    }

    private static async ValueTask<AtlasLoadedDocument<TDocument>> ReadDocumentAsync<TDocument>(
        string path,
        JsonTypeInfo<TDocument> typeInfo,
        Action<TDocument> validator,
        Func<string, Exception> exceptionFactory,
        CancellationToken cancellationToken)
        where TDocument : class
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(exceptionFactory);

        byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        TDocument document = ParseDocument(bytes, typeInfo, validator, exceptionFactory);
        return new AtlasLoadedDocument<TDocument>(
            NormalizePath(path),
            bytes,
            ComputeSha256Hex(bytes),
            document);
    }

    private static async ValueTask<AtlasLoadedDocument<TDocument>> ReadDocumentAsync<TDocument>(
        string path,
        JsonTypeInfo<TDocument> typeInfo,
        Action<TDocument> validator,
        Func<string, Exception> exceptionFactory,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
        where TDocument : class
    {
        ArgumentNullException.ThrowIfNull(io);
        byte[] bytes = await io.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        TDocument document = ParseDocument(bytes, typeInfo, validator, exceptionFactory);
        return new AtlasLoadedDocument<TDocument>(
            NormalizePath(path),
            bytes,
            ComputeSha256Hex(bytes),
            document);
    }

    private static TDocument ParseDocument<TDocument>(
        ReadOnlySpan<byte> bytes,
        JsonTypeInfo<TDocument> typeInfo,
        Action<TDocument> validator,
        Func<string, Exception> exceptionFactory)
        where TDocument : class
    {
        try
        {
            EnsureStrictObjectJson(bytes);
            TDocument? document = JsonSerializer.Deserialize(bytes, typeInfo);
            if (document is null)
            {
                throw new JsonException("The document is null.");
            }

            validator(document);
            return document;
        }
        catch (Exception exception) when (
            exception is JsonException
            or NotSupportedException
            or InvalidOperationException
            or AtlasValidationException
            or AtlasRequestException)
        {
            throw exceptionFactory("The JSON document is invalid.");
        }
    }

    private static byte[] SerializeDocument<TDocument>(
        TDocument document,
        JsonTypeInfo<TDocument> typeInfo,
        Action<TDocument>? validator = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(typeInfo);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, typeInfo);
        EnsureStrictObjectJson(bytes);
        if (validator is not null)
        {
            ValidateSerializedDocument(bytes, typeInfo, validator);
        }

        return bytes;
    }

    private static void ValidateSerializedDocument<TDocument>(
        ReadOnlySpan<byte> bytes,
        JsonTypeInfo<TDocument> typeInfo,
        Action<TDocument> validator)
    {
        try
        {
            TDocument? document = JsonSerializer.Deserialize(bytes, typeInfo);
            if (document is null)
            {
                throw new JsonException("The document is null.");
            }

            validator(document);
        }
        catch (Exception exception) when (
            exception is JsonException
            or NotSupportedException
            or InvalidOperationException
            or AtlasValidationException
            or AtlasRequestException)
        {
            throw new AtlasValidationException("The JSON document is invalid.", exception);
        }
    }

    private static void EnsureStrictObjectJson(ReadOnlySpan<byte> bytes)
    {
        Utf8JsonReader reader = new(
            bytes,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaxJsonDepth,
            });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("The JSON document must be an object.");
        }

        Stack<HashSet<string>> scopes = new();
        scopes.Push(new HashSet<string>(StringComparer.Ordinal));
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    scopes.Pop();
                    if (scopes.Count == 0)
                    {
                        if (reader.Read())
                        {
                            throw new JsonException("Trailing JSON is not allowed.");
                        }

                        return;
                    }

                    break;
                case JsonTokenType.PropertyName:
                    {
                        string name = reader.GetString()
                            ?? throw new JsonException("The property is null.");
                        if (!scopes.Peek().Add(name))
                        {
                            throw new JsonException("Duplicate properties are not allowed.");
                        }

                        break;
                    }
                case JsonTokenType.Null:
                    throw new JsonException("Explicit JSON null is not allowed.");
            }
        }

        throw new JsonException("The JSON document is incomplete.");
    }

    private static JsonSerializerOptions CreateJsonOptions() =>
        new()
        {
            AllowTrailingCommas = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            MaxDepth = MaxJsonDepth,
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false,
        };

    private static bool HasAnyColon(string path) =>
        path.Contains(':', StringComparison.Ordinal);

    private static bool HasColonBeyondDriveDesignator(string path) =>
        path.Length > 2 && path.AsSpan(2).Contains(':');

    private static bool IsDirectorySeparator(char character) =>
        character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar;

    private static bool IsLowerAsciiLetter(char character) =>
        character is >= 'a' and <= 'z';

    private static void ValidateDiscoveryRequest(AtlasIntakeDiscoveryRequest request)
    {
        ValidateSharedRequest(
            request.SchemaVersion,
            DiscoveryRequestSchemaVersion,
            request.SurveyAlias);
        ValidateAbsoluteDosPath(request.ProjectRoot, nameof(request.ProjectRoot));
        ValidateAbsoluteDosPath(request.WorkspaceRoot, nameof(request.WorkspaceRoot));
        ValidateAbsoluteDosPath(request.BaselineManifestPath, nameof(request.BaselineManifestPath));
        ValidateAbsoluteDosPath(
            request.ManifestRevisionDirectory,
            nameof(request.ManifestRevisionDirectory));
        ValidateAbsoluteDosPath(request.DefinitionRoot, nameof(request.DefinitionRoot));
        ValidateAbsoluteDosPath(request.GameExecutablePath, nameof(request.GameExecutablePath));
        ValidateAbsoluteDosPath(
            request.SourceRootMapOutputPath,
            nameof(request.SourceRootMapOutputPath));
        ValidateAbsoluteDosPath(request.InventoryPath, nameof(request.InventoryPath));
        ValidateAbsoluteDosPath(request.InventoryBackupPath, nameof(request.InventoryBackupPath));
        ValidateAbsoluteDosPath(request.CopyPlanOutputPath, nameof(request.CopyPlanOutputPath));
        ValidateAbsoluteDosPath(
            request.StateRevisionDirectory,
            nameof(request.StateRevisionDirectory));
        ValidateLowerHexDigest(
            request.ExpectedBaselineSha256,
            nameof(request.ExpectedBaselineSha256));
        ValidateLowerHexDigest(
            request.ExpectedInventorySha256,
            nameof(request.ExpectedInventorySha256));
        ValidateSaveRoots(request.SaveRoots);
        ValidateRevision(request.ExpectedBaselineRevision, BaselineManifestRevision);
        ValidateRevision(request.NextManifestRevision, PendingManifestRevision);
        ValidateSteamAppAndBuild(request.ExpectedSteamAppId, request.ExpectedBuildId);
    }

    private static void ValidateConfirmationRequest(AtlasIntakeConfirmationRequest request)
    {
        ValidateSharedRequest(
            request.SchemaVersion,
            ConfirmationRequestSchemaVersion,
            request.SurveyAlias);
        ValidateAbsoluteDosPath(request.ProjectRoot, nameof(request.ProjectRoot));
        ValidateAbsoluteDosPath(request.WorkspaceRoot, nameof(request.WorkspaceRoot));
        ValidateAbsoluteDosPath(request.DiscoveredStatePath, nameof(request.DiscoveredStatePath));
        ValidateAbsoluteDosPath(request.PendingManifestPath, nameof(request.PendingManifestPath));
        ValidateAbsoluteDosPath(request.SourceRootMapPath, nameof(request.SourceRootMapPath));
        ValidateAbsoluteDosPath(request.CopyPlanPath, nameof(request.CopyPlanPath));
        ValidateAbsoluteDosPath(
            request.ManifestRevisionDirectory,
            nameof(request.ManifestRevisionDirectory));
        ValidateAbsoluteDosPath(
            request.StateRevisionDirectory,
            nameof(request.StateRevisionDirectory));
        ValidateAbsoluteDosPath(request.InventoryPath, nameof(request.InventoryPath));
        ValidateAbsoluteDosPath(request.InventoryBackupPath, nameof(request.InventoryBackupPath));
        ValidateLowerHexDigest(
            request.ExpectedDiscoveredStateSha256,
            nameof(request.ExpectedDiscoveredStateSha256));
        ValidateLowerHexDigest(
            request.ExpectedInventorySha256,
            nameof(request.ExpectedInventorySha256));
        ValidateGitCommit(request.DecisionCommit, nameof(request.DecisionCommit));
    }

    private static void ValidateCopyRequest(AtlasIntakeCopyRequest request)
    {
        ValidateSharedRequest(
            request.SchemaVersion,
            CopyRequestSchemaVersion,
            request.SurveyAlias);
        ValidateAbsoluteDosPath(request.ProjectRoot, nameof(request.ProjectRoot));
        ValidateAbsoluteDosPath(request.WorkspaceRoot, nameof(request.WorkspaceRoot));
        ValidateAbsoluteDosPath(request.ApprovedStatePath, nameof(request.ApprovedStatePath));
        ValidateAbsoluteDosPath(request.ApprovedManifestPath, nameof(request.ApprovedManifestPath));
        ValidateAbsoluteDosPath(request.SourceRootMapPath, nameof(request.SourceRootMapPath));
        ValidateAbsoluteDosPath(request.CopyPlanPath, nameof(request.CopyPlanPath));
        ValidateAbsoluteDosPath(request.IncompleteCopyPath, nameof(request.IncompleteCopyPath));
        ValidateAbsoluteDosPath(request.FinalCopyPath, nameof(request.FinalCopyPath));
        ValidateAbsoluteDosPath(
            request.StateRevisionDirectory,
            nameof(request.StateRevisionDirectory));
        ValidateAbsoluteDosPath(request.InventoryPath, nameof(request.InventoryPath));
        ValidateAbsoluteDosPath(request.InventoryBackupPath, nameof(request.InventoryBackupPath));
        ValidateLowerHexDigest(
            request.ExpectedApprovedStateSha256,
            nameof(request.ExpectedApprovedStateSha256));
        ValidateLowerHexDigest(
            request.ExpectedInventorySha256,
            nameof(request.ExpectedInventorySha256));
        ValidateGitCommit(request.DecisionCommit, nameof(request.DecisionCommit));
    }

    private static void ValidateCleanupPreflightRequest(AtlasCleanupPreflightRequest request)
    {
        ValidateSharedRequest(
            request.SchemaVersion,
            CleanupPreflightRequestSchemaVersion,
            request.SurveyAlias);
        ValidateAbsoluteDosPath(request.ProjectRoot, nameof(request.ProjectRoot));
        ValidateAbsoluteDosPath(request.WorkspaceRoot, nameof(request.WorkspaceRoot));
        ValidateAbsoluteDosPath(request.QualifiedStatePath, nameof(request.QualifiedStatePath));
        ValidateAbsoluteDosPath(
            request.StateRevisionDirectory,
            nameof(request.StateRevisionDirectory));
        ValidateAbsoluteDosPath(request.InventoryPath, nameof(request.InventoryPath));
        ValidateAbsoluteDosPath(request.InventoryBackupPath, nameof(request.InventoryBackupPath));
        ValidateAbsoluteDosPath(request.ReportOutputPath, nameof(request.ReportOutputPath));
        ValidateLowerHexDigest(
            request.ExpectedQualifiedStateSha256,
            nameof(request.ExpectedQualifiedStateSha256));
        ValidateLowerHexDigest(
            request.ExpectedInventorySha256,
            nameof(request.ExpectedInventorySha256));
        ValidateMilestone(request.ProposedMilestone, nameof(request.ProposedMilestone));
    }

    private static void ValidateSharedRequest(
        string schemaVersion,
        string expectedSchemaVersion,
        string surveyAlias)
    {
        if (!StringComparer.Ordinal.Equals(schemaVersion, expectedSchemaVersion))
        {
            throw new AtlasRequestException("The schemaVersion is invalid.");
        }

        ValidateSurveyAlias(surveyAlias);
    }

    private static void ValidateSaveRoots(AtlasRequestSaveRoot[] saveRoots)
    {
        if (saveRoots.Length != 2)
        {
            throw new AtlasRequestException("The saveRoots collection must contain two roots.");
        }

        HashSet<string> roles = new(StringComparer.Ordinal);
        foreach (AtlasRequestSaveRoot saveRoot in saveRoots)
        {
            if (!roles.Add(saveRoot.LocationRole))
            {
                throw new AtlasRequestException("The saveRoots roles must be unique.");
            }

            if (!StringComparer.Ordinal.Equals(saveRoot.LocationRole, DeploymentRootSaveRole)
                && !StringComparer.Ordinal.Equals(saveRoot.LocationRole, WebRootSaveRole))
            {
                throw new AtlasRequestException("The saveRoots role is invalid.");
            }

            ValidateAbsoluteDosPath(saveRoot.Path, nameof(saveRoot.Path));
        }

        if (!roles.Contains(DeploymentRootSaveRole) || !roles.Contains(WebRootSaveRole))
        {
            throw new AtlasRequestException("The saveRoots roles are incomplete.");
        }
    }

    private static void ValidateRevision(int actual, int expected)
    {
        if (actual != expected)
        {
            throw new AtlasRequestException("The revision value is invalid.");
        }
    }

    private static void ValidateSteamAppAndBuild(int steamAppId, int buildId)
    {
        if (steamAppId != ExactSteamAppId || buildId != ExactBuildId)
        {
            throw new AtlasRequestException("The public game identifiers are invalid.");
        }
    }

    private static void ValidateManifest(AtlasCorpusIntakeManifest manifest)
    {
        if (!StringComparer.Ordinal.Equals(manifest.SchemaVersion, IntakeManifestSchemaVersion))
        {
            throw new AtlasValidationException();
        }

        ValidateSurveyAlias(manifest.SurveyAlias);
        if (manifest.ManifestRevision != BaselineManifestRevision
            && manifest.ManifestRevision != PendingManifestRevision
            && manifest.ManifestRevision != ApprovedManifestRevision)
        {
            throw new AtlasValidationException();
        }

        if (manifest.SaveRoots.Length != 2
            || manifest.SaveEntries.Length != ExactSaveEntryCount
            || manifest.DiscoveredSaveDirectoryEntryCount != ExactSaveEntryCount
            || manifest.IncludedSaveCount != ExactIncludedSaveCount)
        {
            throw new AtlasValidationException();
        }

        if (manifest.DefinitionGroups.Length == 0
            || manifest.DefinitionEntries.Length != ExactDefinitionEntryCount
            || manifest.DiscoveredDefinitionEntryCount != ExactDefinitionEntryCount
            || manifest.IncludedDefinitionCount != ExactIncludedDefinitionCount)
        {
            throw new AtlasValidationException();
        }

        ValidateManifestConfirmation(manifest.Confirmation);
        ValidateManifestValidation(manifest.Validation);
        ValidateManifestSaveRoots(manifest.SaveRoots, manifest.SaveEntries);
        ValidateManifestSaveEntries(manifest.SaveEntries);
        ValidateManifestDefinitions(manifest.DefinitionGroups, manifest.DefinitionEntries);
        ValidateManifestRevisionPolicy(manifest);
        ValidateExactManifestCorpus(manifest);
    }

    private static void ValidateManifestSaveRoots(
        AtlasManifestSaveRoot[] saveRoots,
        AtlasManifestSaveEntry[] saveEntries)
    {
        HashSet<string> aliases = new(StringComparer.Ordinal);
        HashSet<string> roles = new(StringComparer.Ordinal);
        foreach (AtlasManifestSaveRoot saveRoot in saveRoots)
        {
            ValidateRootAlias(saveRoot.RootAlias);
            if (!aliases.Add(saveRoot.RootAlias))
            {
                throw new AtlasValidationException();
            }

            if (!roles.Add(saveRoot.LocationRole))
            {
                throw new AtlasValidationException();
            }

            if (saveRoot.ObservedEntryCount < 0
                || saveRoot.IsReparsePoint
                || (!StringComparer.Ordinal.Equals(saveRoot.Activity, ActiveSaveRootActivity)
                    && !StringComparer.Ordinal.Equals(
                        saveRoot.Activity,
                        InactiveSaveRootActivity))
                || (!StringComparer.Ordinal.Equals(
                        saveRoot.Decision,
                        IncludeSaveRootDecision)
                    && !StringComparer.Ordinal.Equals(
                        saveRoot.Decision,
                        ExcludeNoSaveInputsDecision)))
            {
                throw new AtlasValidationException();
            }

            if (!StringComparer.Ordinal.Equals(saveRoot.LocationRole, DeploymentRootSaveRole)
                && !StringComparer.Ordinal.Equals(saveRoot.LocationRole, WebRootSaveRole))
            {
                throw new AtlasValidationException();
            }
        }

        if (!roles.Contains(DeploymentRootSaveRole) || !roles.Contains(WebRootSaveRole))
        {
            throw new AtlasValidationException();
        }

        if (saveRoots.Sum(static saveRoot => saveRoot.ObservedEntryCount)
            != saveEntries.Length)
        {
            throw new AtlasValidationException();
        }

        foreach (AtlasManifestSaveRoot saveRoot in saveRoots)
        {
            AtlasManifestSaveEntry[] rootEntries = saveEntries
                .Where(entry => StringComparer.Ordinal.Equals(entry.RootAlias, saveRoot.RootAlias))
                .ToArray();
            bool hasIncludedSave = rootEntries.Any(entry =>
                StringComparer.Ordinal.Equals(entry.Decision, IncludeSaveDecision));
            if (rootEntries.Length != saveRoot.ObservedEntryCount
                || !StringComparer.Ordinal.Equals(
                    saveRoot.Activity,
                    hasIncludedSave ? ActiveSaveRootActivity : InactiveSaveRootActivity)
                || !StringComparer.Ordinal.Equals(
                    saveRoot.Decision,
                    hasIncludedSave
                        ? IncludeSaveRootDecision
                        : ExcludeNoSaveInputsDecision))
            {
                throw new AtlasValidationException();
            }
        }
    }

    private static void ValidateManifestSaveEntries(AtlasManifestSaveEntry[] saveEntries)
    {
        HashSet<string> aliases = new(StringComparer.Ordinal);
        HashSet<string> locators = new(StringComparer.OrdinalIgnoreCase);
        foreach (AtlasManifestSaveEntry saveEntry in saveEntries)
        {
            ValidateSaveSourceAlias(saveEntry.SourceAlias);
            ValidateRootAlias(saveEntry.RootAlias);
            ValidateRelativePathAllowingEitherSeparator(saveEntry.RelativePath);
            if (!aliases.Add(saveEntry.SourceAlias))
            {
                throw new AtlasValidationException();
            }

            string locator =
                $"{saveEntry.RootAlias}|{NormalizeRelativePath(saveEntry.RelativePath)}";
            if (!locators.Add(locator))
            {
                throw new AtlasValidationException();
            }

            if (!StringComparer.Ordinal.Equals(saveEntry.EntryType, FileEntryType)
                || saveEntry.IsReparsePoint)
            {
                throw new AtlasValidationException();
            }

            ValidateManifestSaveRole(saveEntry);
        }
    }

    private static void ValidateManifestDefinitions(
        AtlasManifestDefinitionGroup[] groups,
        AtlasManifestDefinitionEntry[] entries)
    {
        HashSet<string> groupIds = new(StringComparer.Ordinal);
        foreach (AtlasManifestDefinitionGroup group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.GroupId)
                || string.IsNullOrWhiteSpace(group.SelectionRule)
                || group.DiscoveredCount < 0
                || (!StringComparer.Ordinal.Equals(group.Decision, IncludeDefinitionDecision)
                    && !StringComparer.Ordinal.Equals(
                        group.Decision,
                        ExcludeDefinitionDecision))
                || !groupIds.Add(group.GroupId))
            {
                throw new AtlasValidationException();
            }
        }

        HashSet<string> aliases = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (AtlasManifestDefinitionEntry entry in entries)
        {
            ValidateDefinitionSourceAlias(entry.SourceAlias);
            ValidateRelativePathAllowingEitherSeparator(entry.RelativePath);
            if (!groupIds.Contains(entry.GroupId)
                || !aliases.Add(entry.SourceAlias)
                || !paths.Add(NormalizeRelativePath(entry.RelativePath)))
            {
                throw new AtlasValidationException();
            }

            if ((!StringComparer.Ordinal.Equals(entry.Decision, IncludeDefinitionDecision)
                    && !StringComparer.Ordinal.Equals(
                        entry.Decision,
                        ExcludeDefinitionDecision))
                || !StringComparer.Ordinal.Equals(entry.EntryType, FileEntryType)
                || entry.IsReparsePoint)
            {
                throw new AtlasValidationException();
            }
        }

        foreach (AtlasManifestDefinitionGroup group in groups)
        {
            AtlasManifestDefinitionEntry[] groupEntries = entries
                .Where(entry => StringComparer.Ordinal.Equals(entry.GroupId, group.GroupId))
                .ToArray();
            if (groupEntries.Length != group.DiscoveredCount
                || groupEntries.Any(entry =>
                    !StringComparer.Ordinal.Equals(entry.Decision, group.Decision)))
            {
                throw new AtlasValidationException();
            }
        }
    }

    private static void ValidateManifestSaveRole(AtlasManifestSaveEntry saveEntry)
    {
        if (StringComparer.Ordinal.Equals(saveEntry.Role, SlotSaveRole))
        {
            if (saveEntry.SlotNumber is null or < 1 or > 20
                || !StringComparer.Ordinal.Equals(saveEntry.Decision, IncludeSaveDecision))
            {
                throw new AtlasValidationException();
            }

            return;
        }

        if (saveEntry.SlotNumber is not null)
        {
            throw new AtlasValidationException();
        }

        if (StringComparer.Ordinal.Equals(saveEntry.Role, GlobalSaveRole)
            || StringComparer.Ordinal.Equals(saveEntry.Role, ConfigSaveRole))
        {
            if (!StringComparer.Ordinal.Equals(saveEntry.Decision, IncludeSaveDecision))
            {
                throw new AtlasValidationException();
            }

            return;
        }

        if (StringComparer.Ordinal.Equals(saveEntry.Role, SteamAutoCloudSaveRole))
        {
            if (!StringComparer.Ordinal.Equals(saveEntry.Decision, ExcludeSteamAutoCloudDecision))
            {
                throw new AtlasValidationException();
            }

            return;
        }

        if (StringComparer.Ordinal.Equals(saveEntry.Role, OtherSaveRole)
            && StringComparer.Ordinal.Equals(saveEntry.Decision, ExcludeNonSaveDecision))
        {
            return;
        }

        throw new AtlasValidationException();
    }

    private static void ValidateManifestConfirmation(AtlasManifestConfirmation confirmation)
    {
        if (!StringComparer.Ordinal.Equals(confirmation.Status, PendingConfirmationStatus)
            && !StringComparer.Ordinal.Equals(confirmation.Status, ApprovedConfirmationStatus)
            && !StringComparer.Ordinal.Equals(confirmation.Status, RejectedConfirmationStatus)
            && !StringComparer.Ordinal.Equals(confirmation.Status, SupersededConfirmationStatus))
        {
            throw new AtlasValidationException();
        }

        bool approved = StringComparer.Ordinal.Equals(
            confirmation.Status,
            ApprovedConfirmationStatus);
        if (approved)
        {
            if (!StringComparer.Ordinal.Equals(confirmation.ConfirmedByRole, ProjectLeaderRole))
            {
                throw new AtlasValidationException();
            }

            if (confirmation.DecisionReference is null
                || !confirmation.DecisionReference.StartsWith(
                    ApprovalDecisionReferencePrefix,
                    StringComparison.Ordinal))
            {
                throw new AtlasValidationException();
            }

            ValidateGitCommit(
                confirmation.DecisionReference[ApprovalDecisionReferencePrefix.Length..],
                nameof(confirmation.DecisionReference));
        }
        else if (
            confirmation.ConfirmedByRole is not null
            || confirmation.DecisionReference is not null)
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateManifestValidation(AtlasManifestValidation validation)
    {
        if (!StringComparer.Ordinal.Equals(validation.Method, ManualA0ValidationMethod)
            && !StringComparer.Ordinal.Equals(validation.Method, AtlasToolValidationMethod))
        {
            throw new AtlasValidationException();
        }

        if (!validation.AliasesUnique
            || !validation.SaveLocatorsUnique
            || !validation.DefinitionRelativePathsUnique
            || !validation.SaveRootMembershipReconciled
            || !validation.SaveRootCountsReconciled
            || !validation.SaveCountsReconciled
            || !validation.DefinitionCountsReconciled
            || !validation.RolesAndDecisionsConsistent
            || !validation.GroupMembershipReconciled)
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateInventory(AtlasPrivateArtifactInventoryDocument inventory)
    {
        if (!StringComparer.Ordinal.Equals(inventory.SchemaVersion, InventorySchemaVersion))
        {
            throw new AtlasValidationException();
        }

        ValidateSurveyAlias(inventory.SurveyAlias);
        if (inventory.Artifacts.Length == 0)
        {
            throw new AtlasValidationException();
        }

        HashSet<string> aliases = new(StringComparer.Ordinal);
        Dictionary<string, string[]> lineages = new(StringComparer.Ordinal);
        foreach (AtlasPrivateArtifactEntry artifact in inventory.Artifacts)
        {
            ValidateInventoryArtifact(artifact);
            ValidateArtifactAlias(artifact.ArtifactAlias);
            if (!aliases.Add(artifact.ArtifactAlias))
            {
                throw new AtlasValidationException();
            }

            if (artifact.LineageAliases is null)
            {
                throw new AtlasValidationException();
            }

            lineages.Add(artifact.ArtifactAlias, artifact.LineageAliases);
        }

        foreach (KeyValuePair<string, string[]> pair in lineages)
        {
            foreach (string predecessor in pair.Value)
            {
                if (!aliases.Contains(predecessor)
                    || StringComparer.Ordinal.Equals(pair.Key, predecessor))
                {
                    throw new AtlasValidationException();
                }
            }
        }

        EnsureAcyclicLineage(lineages);
    }

    private static void ValidateSourceRootMap(AtlasSourceRootMapDocument document)
    {
        if (!StringComparer.Ordinal.Equals(document.SchemaVersion, SourceRootMapSchemaVersion)
            || document.ManifestRevision != PendingManifestRevision
            || document.SteamAppId != ExactSteamAppId
            || document.BuildId != ExactBuildId
            || document.SaveRoots.Length != 2)
        {
            throw new AtlasValidationException();
        }

        ValidateSurveyAlias(document.SurveyAlias);
        ValidateOutputAbsoluteDosPath(document.DefinitionRootPath);
        ValidateOutputAbsoluteDosPath(document.GameExecutablePath);
        HashSet<string> roles = new(StringComparer.Ordinal);
        HashSet<string> aliases = new(StringComparer.Ordinal);
        foreach (AtlasSourceRootBinding binding in document.SaveRoots)
        {
            ValidateRootAlias(binding.RootAlias);
            if (!aliases.Add(binding.RootAlias) || !roles.Add(binding.LocationRole))
            {
                throw new AtlasValidationException();
            }

            ValidateOutputAbsoluteDosPath(binding.AbsolutePath);
        }

        if (!roles.Contains(DeploymentRootSaveRole) || !roles.Contains(WebRootSaveRole))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateCopyPlan(AtlasCopyPlanDocument document)
    {
        if (!StringComparer.Ordinal.Equals(document.SchemaVersion, CopyPlanSchemaVersion)
            || document.ManifestRevision != PendingManifestRevision
            || document.Entries.Length != ExactIncludedSaveCount + ExactIncludedDefinitionCount)
        {
            throw new AtlasValidationException();
        }

        ValidateSurveyAlias(document.SurveyAlias);
        HashSet<string> sourceAliases = new(StringComparer.Ordinal);
        HashSet<string> destinationAliases = new(StringComparer.Ordinal);
        HashSet<string> destinationPaths = new(StringComparer.Ordinal);
        int saveCount = 0;
        int definitionCount = 0;
        foreach (AtlasCopyPlanEntry entry in document.Entries)
        {
            ValidateSourceAliasByArtifactClass(entry.SourceAlias, entry.ArtifactClass);
            ValidateArtifactAlias(entry.DestinationArtifactAlias);
            ValidateCanonicalRelativePath(
                entry.DestinationRelativePath,
                nameof(entry.DestinationRelativePath));
            if (!sourceAliases.Add(entry.SourceAlias)
                || !destinationAliases.Add(entry.DestinationArtifactAlias)
                || !destinationPaths.Add(entry.DestinationRelativePath))
            {
                throw new AtlasValidationException();
            }

            if (StringComparer.Ordinal.Equals(entry.ArtifactClass, SaveCopyArtifactClass))
            {
                saveCount++;
            }
            else if (StringComparer.Ordinal.Equals(
                entry.ArtifactClass,
                DefinitionCopyArtifactClass))
            {
                definitionCount++;
            }
            else
            {
                throw new AtlasValidationException();
            }
        }

        if (saveCount != ExactIncludedSaveCount
            || definitionCount != ExactIncludedDefinitionCount)
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateState(AtlasIntakeStateDocument document)
    {
        if (!StringComparer.Ordinal.Equals(document.SchemaVersion, IntakeStateSchemaVersion))
        {
            throw new AtlasValidationException();
        }

        ValidateSurveyAlias(document.SurveyAlias);
        ValidateArtifactAlias(document.StateArtifactAlias);
        ValidateLowerHexDigest(document.InventorySha256, nameof(document.InventorySha256));
        ValidateSteamAppAndBuild(document.SteamAppId, document.BuildId);
        if (document.StateRevision is < DiscoveredStateRevision or > PreflightedStateRevision
            || !StringComparer.Ordinal.Equals(document.Phase, GetPhaseName(document.StateRevision)))
        {
            throw new AtlasValidationException();
        }

        if (document.DocumentBindings.Length == 0 || document.ArtifactBindings.Length != 2)
        {
            throw new AtlasValidationException();
        }

        ValidateBindings(document.DocumentBindings);
        ValidateBindings(document.ArtifactBindings);

        if (document.StateRevision == DiscoveredStateRevision)
        {
            RequireDocumentRoles(
                document.DocumentBindings,
                BaselineManifestRole,
                PendingManifestRole,
                SourceRootMapRole,
                CopyPlanRole);
            RequireArtifactRoles(
                document.ArtifactBindings,
                DiscoveredRequestRole,
                DiscoveredInventoryBackupRole);
            if (document.DecisionCommit is not null
                || document.FinalCopyRootRelativePath is not null)
            {
                throw new AtlasValidationException();
            }
        }
        else if (document.StateRevision == ApprovedStateRevision)
        {
            RequireDocumentRoles(
                document.DocumentBindings,
                PredecessorStateRole,
                ApprovedManifestRole,
                SourceRootMapRole,
                CopyPlanRole);
            RequireArtifactRoles(
                document.ArtifactBindings,
                ConfirmRequestRole,
                ApprovedInventoryBackupRole);
            ValidateGitCommit(
                document.DecisionCommit ?? string.Empty,
                nameof(document.DecisionCommit));
            if (document.FinalCopyRootRelativePath is not null)
            {
                throw new AtlasValidationException();
            }
        }
        else if (document.StateRevision == QualifiedStateRevision)
        {
            RequireDocumentRoles(
                document.DocumentBindings,
                PredecessorStateRole,
                ApprovedManifestRole,
                SourceRootMapRole,
                CopyPlanRole,
                CopyReceiptRole);
            RequireArtifactRoles(
                document.ArtifactBindings,
                CopyRequestRole,
                QualifiedInventoryBackupRole);
            ValidateGitCommit(
                document.DecisionCommit ?? string.Empty,
                nameof(document.DecisionCommit));
            if (!StringComparer.Ordinal.Equals(
                    document.FinalCopyRootRelativePath,
                    SaveSnapshotRelativeRoot))
            {
                throw new AtlasValidationException();
            }
        }
        else
        {
            RequireDocumentRoles(
                document.DocumentBindings,
                PredecessorStateRole,
                ApprovedManifestRole,
                SourceRootMapRole,
                CopyPlanRole,
                CopyReceiptRole,
                CleanupPreflightReportRole);
            RequireArtifactRoles(
                document.ArtifactBindings,
                CleanupPreflightRequestRole,
                PreflightedInventoryBackupRole);
            ValidateGitCommit(
                document.DecisionCommit ?? string.Empty,
                nameof(document.DecisionCommit));
            if (!StringComparer.Ordinal.Equals(
                    document.FinalCopyRootRelativePath,
                    SaveSnapshotRelativeRoot))
            {
                throw new AtlasValidationException();
            }
        }
    }

    private static void ValidateCopyReceipt(AtlasCopyReceiptDocument document)
    {
        if (!StringComparer.Ordinal.Equals(document.SchemaVersion, CopyReceiptSchemaVersion)
            || !StringComparer.Ordinal.Equals(document.Profile, TrustedLocalFilesystemProfile)
            || document.SteamAppId != ExactSteamAppId
            || document.BuildId != ExactBuildId
            || document.SaveCount != ExactIncludedSaveCount
            || document.DefinitionCount != ExactIncludedDefinitionCount
            || document.Entries.Length != ExactIncludedSaveCount + ExactIncludedDefinitionCount)
        {
            throw new AtlasValidationException();
        }

        ValidateSurveyAlias(document.SurveyAlias);
        ValidateArtifactAlias(document.ReceiptArtifactAlias);
        ValidateArtifactAlias(document.ApprovedManifestArtifactAlias);
        ValidateLowerHexDigest(document.CopyRequestSha256, nameof(document.CopyRequestSha256));
        ValidateLowerHexDigest(document.ApprovedStateSha256, nameof(document.ApprovedStateSha256));
        ValidateLowerHexDigest(
            document.ApprovedManifestSha256,
            nameof(document.ApprovedManifestSha256));
        ValidateLowerHexDigest(document.SourceRootMapSha256, nameof(document.SourceRootMapSha256));
        ValidateLowerHexDigest(document.CopyPlanSha256, nameof(document.CopyPlanSha256));
        ValidateLowerHexDigest(
            document.GameExecutableSha256,
            nameof(document.GameExecutableSha256));
        if (!StringComparer.Ordinal.Equals(
                document.FinalCopyRootRelativePath,
                SaveSnapshotRelativeRoot))
        {
            throw new AtlasValidationException();
        }

        if (!document.DecisionReference.StartsWith(
                ApprovalDecisionReferencePrefix,
                StringComparison.Ordinal))
        {
            throw new AtlasValidationException();
        }

        ValidateGitCommit(
            document.DecisionReference[ApprovalDecisionReferencePrefix.Length..],
            nameof(document.DecisionReference));
        HashSet<string> aliases = new(StringComparer.Ordinal);
        HashSet<string> sourceAliases = new(StringComparer.Ordinal);
        HashSet<string> destinations = new(StringComparer.Ordinal);
        int saveCount = 0;
        int definitionCount = 0;
        foreach (AtlasCopyReceiptEntry entry in document.Entries)
        {
            ValidateArtifactAlias(entry.DestinationArtifactAlias);
            ValidateSourceAliasByArtifactClass(entry.SourceAlias, entry.ArtifactClass);
            ValidateCanonicalRelativePath(
                entry.DestinationRelativePath,
                nameof(entry.DestinationRelativePath));
            ValidateLowerHexDigest(entry.SourceSha256, nameof(entry.SourceSha256));
            if (entry.SourceLength < 0)
            {
                throw new AtlasValidationException();
            }

            ValidateUtcTimestamp(
                entry.SourceLastWriteTimeUtc,
                nameof(entry.SourceLastWriteTimeUtc));
            if (!aliases.Add(entry.DestinationArtifactAlias)
                || !sourceAliases.Add(entry.SourceAlias)
                || !destinations.Add(entry.DestinationRelativePath))
            {
                throw new AtlasValidationException();
            }

            if (StringComparer.Ordinal.Equals(entry.ArtifactClass, SaveCopyArtifactClass))
            {
                saveCount++;
            }
            else if (StringComparer.Ordinal.Equals(
                entry.ArtifactClass,
                DefinitionCopyArtifactClass))
            {
                definitionCount++;
            }
            else
            {
                throw new AtlasValidationException();
            }
        }

        if (saveCount != document.SaveCount || definitionCount != document.DefinitionCount)
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateCleanupPreflightReport(
        AtlasCleanupPreflightReportDocument document)
    {
        if (!StringComparer.Ordinal.Equals(
                document.SchemaVersion,
                CleanupPreflightReportSchemaVersion)
            || document.Results.Length == 0)
        {
            throw new AtlasValidationException();
        }

        ValidateSurveyAlias(document.SurveyAlias);
        ValidateArtifactAlias(document.ReportArtifactAlias);
        ValidateLowerHexDigest(document.InventorySha256, nameof(document.InventorySha256));
        ValidateInventoryMilestone(document.ProposedMilestone);
        HashSet<string> aliases = new(StringComparer.Ordinal);
        foreach (AtlasCleanupPreflightResult result in document.Results)
        {
            ValidateArtifactAlias(result.ArtifactAlias);
            ValidateInventoryArtifactClass(result.ArtifactClass);
            ValidateInventoryStatus(result.Status);
            ValidateInventoryDisposition(result.PlannedDisposition);
            ValidateInventoryMilestone(result.LastUseMilestone);
            ValidateNonEmptyToken(result.ExpiryCondition);
            ValidateCleanupPreflightResult(result.Result);
            if (!aliases.Add(result.ArtifactAlias))
            {
                throw new AtlasValidationException();
            }
        }
    }

    private static void ValidateBindings<TBinding>(TBinding[] bindings)
        where TBinding : AtlasBindingBase
    {
        HashSet<string> roles = new(StringComparer.Ordinal);
        HashSet<string> aliases = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.Ordinal);
        foreach (TBinding binding in bindings)
        {
            if (!roles.Add(binding.Role) || !aliases.Add(binding.ArtifactAlias))
            {
                throw new AtlasValidationException();
            }

            ValidateArtifactAlias(binding.ArtifactAlias);
            ValidateCanonicalRelativePath(binding.RelativePath, nameof(binding.RelativePath));
            ValidateLowerHexDigest(binding.Sha256, nameof(binding.Sha256));
            if (!paths.Add(binding.RelativePath))
            {
                throw new AtlasValidationException();
            }
        }
    }

    private static void RequireDocumentRoles(
        AtlasDocumentBinding[] bindings,
        params string[] expectedRoles)
    {
        RequireRoles(bindings.Select(static binding => binding.Role), expectedRoles);
    }

    private static void RequireArtifactRoles(
        AtlasArtifactBinding[] bindings,
        params string[] expectedRoles)
    {
        RequireRoles(bindings.Select(static binding => binding.Role), expectedRoles);
    }

    private static void RequireRoles(IEnumerable<string> roles, params string[] expectedRoles)
    {
        HashSet<string> actual = roles.ToHashSet(StringComparer.Ordinal);
        if (actual.Count != expectedRoles.Length)
        {
            throw new AtlasValidationException();
        }

        foreach (string expectedRole in expectedRoles)
        {
            if (!actual.Contains(expectedRole))
            {
                throw new AtlasValidationException();
            }
        }
    }

    private static void ValidateRootAlias(string alias)
    {
        if (!alias.StartsWith("save-root-", StringComparison.Ordinal)
            || alias.Length != "save-root-".Length + 4
            || !alias.AsSpan("save-root-".Length).ToString().All(char.IsAsciiDigit))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateSaveSourceAlias(string alias)
    {
        if (!alias.StartsWith("save-source-", StringComparison.Ordinal)
            || alias.Length != "save-source-".Length + 4
            || !alias.AsSpan("save-source-".Length).ToString().All(char.IsAsciiDigit))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateDefinitionSourceAlias(string alias)
    {
        if (!alias.StartsWith("definition-source-", StringComparison.Ordinal)
            || alias.Length != "definition-source-".Length + 6
            || !alias.AsSpan("definition-source-".Length).ToString().All(char.IsAsciiDigit))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateArtifactAlias(string alias)
    {
        if (!alias.StartsWith("private-artifact-", StringComparison.Ordinal)
            || alias.Length != "private-artifact-".Length + 6
            || !alias.AsSpan("private-artifact-".Length).ToString().All(char.IsAsciiDigit))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateSourceAliasByArtifactClass(string alias, string artifactClass)
    {
        if (StringComparer.Ordinal.Equals(artifactClass, SaveCopyArtifactClass))
        {
            ValidateSaveSourceAlias(alias);
            return;
        }

        if (StringComparer.Ordinal.Equals(artifactClass, DefinitionCopyArtifactClass))
        {
            ValidateDefinitionSourceAlias(alias);
            return;
        }

        throw new AtlasValidationException();
    }

    private static void ValidateRelativePathAllowingEitherSeparator(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || Path.IsPathRooted(value)
            || HasAnyColon(value))
        {
            throw new AtlasValidationException();
        }

        foreach (string segment in SplitRelativePath(value))
        {
            if (segment.Length == 0
                || StringComparer.Ordinal.Equals(segment, ".")
                || StringComparer.Ordinal.Equals(segment, ".."))
            {
                throw new AtlasValidationException();
            }
        }
    }

    private static void ValidateMilestone(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !AtlasMilestoneOrder.TryGetValue(value, out _))
        {
            throw new AtlasRequestException($"The '{parameterName}' milestone is invalid.");
        }
    }

    private static void ValidateManifestRevisionPolicy(AtlasCorpusIntakeManifest manifest)
    {
        if (manifest.ManifestRevision == BaselineManifestRevision)
        {
            if (!StringComparer.Ordinal.Equals(
                    manifest.Validation.Method,
                    ManualA0ValidationMethod)
                || !StringComparer.Ordinal.Equals(
                    manifest.Confirmation.Status,
                    ApprovedConfirmationStatus))
            {
                throw new AtlasValidationException();
            }

            return;
        }

        if (!StringComparer.Ordinal.Equals(
                manifest.Validation.Method,
                AtlasToolValidationMethod))
        {
            throw new AtlasValidationException();
        }

        if (manifest.ManifestRevision == PendingManifestRevision)
        {
            if (!StringComparer.Ordinal.Equals(
                    manifest.Confirmation.Status,
                    PendingConfirmationStatus))
            {
                throw new AtlasValidationException();
            }

            return;
        }

        if (!StringComparer.Ordinal.Equals(
                manifest.Confirmation.Status,
                ApprovedConfirmationStatus))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateExactManifestCorpus(AtlasCorpusIntakeManifest manifest)
    {
        int includedSaves = 0;
        int excludedSteamMetadata = 0;
        foreach (AtlasManifestSaveEntry entry in manifest.SaveEntries)
        {
            if (StringComparer.Ordinal.Equals(entry.Decision, IncludeSaveDecision))
            {
                includedSaves++;
                continue;
            }

            if (StringComparer.Ordinal.Equals(
                    entry.Decision,
                    ExcludeSteamAutoCloudDecision))
            {
                excludedSteamMetadata++;
                continue;
            }

            throw new AtlasValidationException();
        }

        int includedDefinitions = 0;
        int excludedDefinitions = 0;
        foreach (AtlasManifestDefinitionEntry entry in manifest.DefinitionEntries)
        {
            if (StringComparer.Ordinal.Equals(entry.Decision, IncludeDefinitionDecision))
            {
                includedDefinitions++;
                continue;
            }

            if (StringComparer.Ordinal.Equals(entry.Decision, ExcludeDefinitionDecision))
            {
                excludedDefinitions++;
                continue;
            }

            throw new AtlasValidationException();
        }

        if (includedSaves != ExactIncludedSaveCount
            || excludedSteamMetadata != ExactExcludedSteamMetadataCount
            || includedDefinitions != ExactIncludedDefinitionCount
            || excludedDefinitions != ExactExcludedDefinitionCount)
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateInventoryArtifact(AtlasPrivateArtifactEntry artifact)
    {
        if (artifact.LineageAliases is null)
        {
            throw new AtlasValidationException();
        }

        ValidateInventoryArtifactClass(artifact.ArtifactClass);
        ValidateNonEmptyToken(artifact.Purpose);
        ValidateCustodianRole(artifact.CustodianRole);
        ValidateInventoryMilestone(artifact.LastUseMilestone);
        ValidateNonEmptyToken(artifact.ExpiryCondition);
        ValidateInventoryDisposition(artifact.PlannedDisposition);
        ValidateInventoryStatus(artifact.Status);
        ValidateNonEmptyToken(artifact.VerificationMethod);

        HashSet<string> lineageAliases = new(StringComparer.Ordinal);
        foreach (string lineageAlias in artifact.LineageAliases)
        {
            ValidateArtifactAlias(lineageAlias);
            if (!lineageAliases.Add(lineageAlias))
            {
                throw new AtlasValidationException();
            }
        }

        if (StringComparer.Ordinal.Equals(artifact.ArtifactClass, SaveCopyArtifactClass))
        {
            if (!StringComparer.Ordinal.Equals(
                    artifact.Qualification,
                    PreservationUnqualifiedSaveQualification)
                && !StringComparer.Ordinal.Equals(
                    artifact.Qualification,
                    A2QualifiedSaveQualification))
            {
                throw new AtlasValidationException();
            }
        }
        else if (artifact.Qualification is not null)
        {
            throw new AtlasValidationException();
        }

        string trustedReceiptPrefix = TrustedLocalFilesystemProfile + ";receipt:";
        if (artifact.VerificationMethod.StartsWith(trustedReceiptPrefix, StringComparison.Ordinal))
        {
            if (!StringComparer.Ordinal.Equals(artifact.ArtifactClass, SaveCopyArtifactClass)
                && !StringComparer.Ordinal.Equals(
                    artifact.ArtifactClass,
                    DefinitionCopyArtifactClass))
            {
                throw new AtlasValidationException();
            }

            ValidateArtifactAlias(
                artifact.VerificationMethod[trustedReceiptPrefix.Length..]);
        }
    }

    private static void ValidateInventoryArtifactClass(string value)
    {
        if (!AllowedInventoryArtifactClasses.Contains(value))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateInventoryStatus(string value)
    {
        if (!AllowedInventoryStatuses.Contains(value))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateInventoryDisposition(string value)
    {
        if (!AllowedInventoryDispositions.Contains(value))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateInventoryMilestone(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !AtlasMilestoneOrder.ContainsKey(value))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateCustodianRole(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AtlasValidationException();
        }

        foreach (char character in value)
        {
            if (!(IsLowerAsciiLetter(character)
                    || char.IsAsciiDigit(character)
                    || character == '-'))
            {
                throw new AtlasValidationException();
            }
        }
    }

    private static void ValidateNonEmptyToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateCleanupPreflightResult(string value)
    {
        if (!AllowedCleanupPreflightResults.Contains(value))
        {
            throw new AtlasValidationException();
        }
    }

    private static void ValidateOutputAbsoluteDosPath(string path)
    {
        try
        {
            ValidateAbsoluteDosPath(path, nameof(path));
        }
        catch (AtlasRequestException exception)
        {
            throw new AtlasValidationException("The JSON document is invalid.", exception);
        }
    }

    private static void ValidateUtcTimestamp(DateTimeOffset value, string parameterName)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            throw new AtlasValidationException(
                $"The '{parameterName}' timestamp must be a non-default UTC value.");
        }
    }

    private static void EnsureAcyclicLineage(Dictionary<string, string[]> lineages)
    {
        HashSet<string> visiting = new(StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);
        foreach (string alias in lineages.Keys)
        {
            Visit(alias);
        }

        void Visit(string alias)
        {
            if (!visiting.Add(alias))
            {
                throw new AtlasValidationException();
            }

            if (visited.Contains(alias))
            {
                visiting.Remove(alias);
                return;
            }

            foreach (string predecessor in lineages[alias])
            {
                Visit(predecessor);
            }

            visiting.Remove(alias);
            visited.Add(alias);
        }
    }

    internal static readonly IReadOnlyDictionary<string, int> AtlasMilestoneOrder =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["A2"] = 0,
            ["A3"] = 1,
            ["A4"] = 2,
            ["A5"] = 3,
            ["A6"] = 4,
            ["A7"] = 5,
            ["A8"] = 6,
            ["post-A8-appeal"] = 7,
        };
}

public sealed record class AtlasIntakeDiscoveryRequest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ProjectRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string WorkspaceRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string BaselineManifestPath { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedBaselineSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public int ExpectedBaselineRevision { get; init; }

    [JsonRequired]
    public int NextManifestRevision { get; init; }

    [JsonRequired]
    public string ManifestRevisionDirectory { get; init; } = string.Empty;

    [JsonRequired]
    public AtlasRequestSaveRoot[] SaveRoots { get; init; } = [];

    [JsonRequired]
    public string DefinitionRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string GameExecutablePath { get; init; } = string.Empty;

    [JsonRequired]
    public string SourceRootMapOutputPath { get; init; } = string.Empty;

    [JsonRequired]
    public string InventoryPath { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedInventorySha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string InventoryBackupPath { get; init; } = string.Empty;

    [JsonRequired]
    public string CopyPlanOutputPath { get; init; } = string.Empty;

    [JsonRequired]
    public string StateRevisionDirectory { get; init; } = string.Empty;

    [JsonRequired]
    public int ExpectedSteamAppId { get; init; }

    [JsonRequired]
    public int ExpectedBuildId { get; init; }
}

public sealed record class AtlasIntakeConfirmationRequest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ProjectRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string WorkspaceRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string DiscoveredStatePath { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedDiscoveredStateSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string PendingManifestPath { get; init; } = string.Empty;

    [JsonRequired]
    public string SourceRootMapPath { get; init; } = string.Empty;

    [JsonRequired]
    public string CopyPlanPath { get; init; } = string.Empty;

    [JsonRequired]
    public string DecisionCommit { get; init; } = string.Empty;

    [JsonRequired]
    public string ManifestRevisionDirectory { get; init; } = string.Empty;

    [JsonRequired]
    public string StateRevisionDirectory { get; init; } = string.Empty;

    [JsonRequired]
    public string InventoryPath { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedInventorySha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string InventoryBackupPath { get; init; } = string.Empty;
}

public sealed record class AtlasIntakeCopyRequest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ProjectRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string WorkspaceRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string ApprovedStatePath { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedApprovedStateSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string ApprovedManifestPath { get; init; } = string.Empty;

    [JsonRequired]
    public string SourceRootMapPath { get; init; } = string.Empty;

    [JsonRequired]
    public string CopyPlanPath { get; init; } = string.Empty;

    [JsonRequired]
    public string DecisionCommit { get; init; } = string.Empty;

    [JsonRequired]
    public string IncompleteCopyPath { get; init; } = string.Empty;

    [JsonRequired]
    public string FinalCopyPath { get; init; } = string.Empty;

    [JsonRequired]
    public string StateRevisionDirectory { get; init; } = string.Empty;

    [JsonRequired]
    public string InventoryPath { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedInventorySha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string InventoryBackupPath { get; init; } = string.Empty;
}

public sealed record class AtlasCleanupPreflightRequest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ProjectRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string WorkspaceRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string QualifiedStatePath { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedQualifiedStateSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string StateRevisionDirectory { get; init; } = string.Empty;

    [JsonRequired]
    public string InventoryPath { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedInventorySha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string InventoryBackupPath { get; init; } = string.Empty;

    [JsonRequired]
    public string ProposedMilestone { get; init; } = string.Empty;

    [JsonRequired]
    public string ReportOutputPath { get; init; } = string.Empty;
}

public sealed record class AtlasRequestSaveRoot
{
    [JsonRequired]
    public string LocationRole { get; init; } = string.Empty;

    [JsonRequired]
    public string Path { get; init; } = string.Empty;
}

public sealed record class AtlasCorpusIntakeManifest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public int ManifestRevision { get; init; }

    [JsonRequired]
    public AtlasManifestSaveRoot[] SaveRoots { get; init; } = [];

    [JsonRequired]
    public int DiscoveredSaveDirectoryEntryCount { get; init; }

    [JsonRequired]
    public int IncludedSaveCount { get; init; }

    [JsonRequired]
    public AtlasManifestSaveEntry[] SaveEntries { get; init; } = [];

    [JsonRequired]
    public int DiscoveredDefinitionEntryCount { get; init; }

    [JsonRequired]
    public int IncludedDefinitionCount { get; init; }

    [JsonRequired]
    public AtlasManifestDefinitionGroup[] DefinitionGroups { get; init; } = [];

    [JsonRequired]
    public AtlasManifestDefinitionEntry[] DefinitionEntries { get; init; } = [];

    [JsonRequired]
    public AtlasManifestValidation Validation { get; init; } = new();

    [JsonRequired]
    public AtlasManifestConfirmation Confirmation { get; init; } = new();
}

public sealed record class AtlasManifestSaveRoot
{
    [JsonRequired]
    public string RootAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string LocationRole { get; init; } = string.Empty;

    [JsonRequired]
    public string Activity { get; init; } = string.Empty;

    [JsonRequired]
    public string Decision { get; init; } = string.Empty;

    [JsonRequired]
    public int ObservedEntryCount { get; init; }
    public string? ReasonCode { get; init; }

    [JsonRequired]
    public bool IsReparsePoint { get; init; }
}

public sealed record class AtlasManifestSaveEntry
{
    [JsonRequired]
    public string SourceAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string RootAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string RelativePath { get; init; } = string.Empty;

    [JsonRequired]
    public string Role { get; init; } = string.Empty;
    public int? SlotNumber { get; init; }

    [JsonRequired]
    public string Decision { get; init; } = string.Empty;
    public string? ReasonCode { get; init; }

    [JsonRequired]
    public string EntryType { get; init; } = string.Empty;

    [JsonRequired]
    public bool IsReparsePoint { get; init; }
}

public sealed record class AtlasManifestDefinitionGroup
{
    [JsonRequired]
    public string GroupId { get; init; } = string.Empty;

    [JsonRequired]
    public string SelectionRule { get; init; } = string.Empty;

    [JsonRequired]
    public int DiscoveredCount { get; init; }

    [JsonRequired]
    public string Decision { get; init; } = string.Empty;
    public string? ReasonCode { get; init; }
}

public sealed record class AtlasManifestDefinitionEntry
{
    [JsonRequired]
    public string SourceAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string RelativePath { get; init; } = string.Empty;

    [JsonRequired]
    public string GroupId { get; init; } = string.Empty;

    [JsonRequired]
    public string Decision { get; init; } = string.Empty;
    public string? ReasonCode { get; init; }

    [JsonRequired]
    public string EntryType { get; init; } = string.Empty;

    [JsonRequired]
    public bool IsReparsePoint { get; init; }
}

public sealed record class AtlasManifestValidation
{
    [JsonRequired]
    public string Method { get; init; } = string.Empty;

    [JsonRequired]
    public bool AliasesUnique { get; init; }

    [JsonRequired]
    public bool SaveLocatorsUnique { get; init; }

    [JsonRequired]
    public bool DefinitionRelativePathsUnique { get; init; }

    [JsonRequired]
    public bool SaveRootMembershipReconciled { get; init; }

    [JsonRequired]
    public bool SaveRootCountsReconciled { get; init; }

    [JsonRequired]
    public bool SaveCountsReconciled { get; init; }

    [JsonRequired]
    public bool DefinitionCountsReconciled { get; init; }

    [JsonRequired]
    public bool RolesAndDecisionsConsistent { get; init; }

    [JsonRequired]
    public bool GroupMembershipReconciled { get; init; }
}

public sealed record class AtlasManifestConfirmation
{
    [JsonRequired]
    public string Status { get; init; } = string.Empty;
    public string? ConfirmedByRole { get; init; }
    public string? DecisionReference { get; init; }
}

public sealed record class AtlasPrivateArtifactInventoryDocument
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public AtlasPrivateArtifactEntry[] Artifacts { get; init; } = [];
}

public sealed record class AtlasPrivateArtifactEntry
{
    [JsonRequired]
    public string ArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ArtifactClass { get; init; } = string.Empty;
    public string? Qualification { get; init; }

    [JsonRequired]
    public string Purpose { get; init; } = string.Empty;

    [JsonRequired]
    public string CustodianRole { get; init; } = string.Empty;

    [JsonRequired]
    public string[] LineageAliases { get; init; } = [];

    [JsonRequired]
    public string LastUseMilestone { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpiryCondition { get; init; } = string.Empty;

    [JsonRequired]
    public string PlannedDisposition { get; init; } = string.Empty;

    [JsonRequired]
    public string Status { get; init; } = string.Empty;

    [JsonRequired]
    public string VerificationMethod { get; init; } = string.Empty;
}

public sealed record class AtlasSourceRootMapDocument
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public int ManifestRevision { get; init; }

    [JsonRequired]
    public int SteamAppId { get; init; }

    [JsonRequired]
    public int BuildId { get; init; }

    [JsonRequired]
    public AtlasSourceRootBinding[] SaveRoots { get; init; } = [];

    [JsonRequired]
    public string DefinitionRootPath { get; init; } = string.Empty;

    [JsonRequired]
    public string GameExecutablePath { get; init; } = string.Empty;
}

public sealed record class AtlasSourceRootBinding
{
    [JsonRequired]
    public string RootAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string LocationRole { get; init; } = string.Empty;

    [JsonRequired]
    public string AbsolutePath { get; init; } = string.Empty;
}

public sealed record class AtlasCopyPlanDocument
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public int ManifestRevision { get; init; }

    [JsonRequired]
    public AtlasCopyPlanEntry[] Entries { get; init; } = [];
}

public sealed record class AtlasCopyPlanEntry
{
    [JsonRequired]
    public string SourceAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string DestinationArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ArtifactClass { get; init; } = string.Empty;

    [JsonRequired]
    public string DestinationRelativePath { get; init; } = string.Empty;
}

public sealed record class AtlasIntakeStateDocument
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public int StateRevision { get; init; }

    [JsonRequired]
    public string Phase { get; init; } = string.Empty;

    [JsonRequired]
    public string StateArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public int SteamAppId { get; init; }

    [JsonRequired]
    public int BuildId { get; init; }

    [JsonRequired]
    public string InventorySha256 { get; init; } = string.Empty;
    public string? DecisionCommit { get; init; }
    public string? FinalCopyRootRelativePath { get; init; }

    [JsonRequired]
    public AtlasDocumentBinding[] DocumentBindings { get; init; } = [];

    [JsonRequired]
    public AtlasArtifactBinding[] ArtifactBindings { get; init; } = [];
}

public abstract record class AtlasBindingBase
{
    [JsonRequired]
    public string Role { get; init; } = string.Empty;

    [JsonRequired]
    public string ArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string RelativePath { get; init; } = string.Empty;

    [JsonRequired]
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record class AtlasDocumentBinding : AtlasBindingBase;

public sealed record class AtlasArtifactBinding : AtlasBindingBase;

public sealed record class AtlasCopyReceiptDocument
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ReceiptArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string Profile { get; init; } = string.Empty;

    [JsonRequired]
    public string CopyRequestSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string ApprovedStateSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string ApprovedManifestSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string SourceRootMapSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string CopyPlanSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string DecisionReference { get; init; } = string.Empty;

    [JsonRequired]
    public string ApprovedManifestArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string FinalCopyRootRelativePath { get; init; } = string.Empty;

    [JsonRequired]
    public int SteamAppId { get; init; }

    [JsonRequired]
    public int BuildId { get; init; }

    [JsonRequired]
    public string GameExecutableSha256 { get; init; } = string.Empty;

    [JsonRequired]
    public int SaveCount { get; init; }

    [JsonRequired]
    public int DefinitionCount { get; init; }

    [JsonRequired]
    public AtlasCopyReceiptEntry[] Entries { get; init; } = [];
}

public sealed record class AtlasCopyReceiptEntry
{
    [JsonRequired]
    public string DestinationArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string SourceAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ArtifactClass { get; init; } = string.Empty;

    [JsonRequired]
    public string DestinationRelativePath { get; init; } = string.Empty;

    [JsonRequired]
    public long SourceLength { get; init; }

    [JsonRequired]
    public DateTimeOffset SourceLastWriteTimeUtc { get; init; }

    [JsonRequired]
    public string SourceSha256 { get; init; } = string.Empty;
}

public sealed record class AtlasCleanupPreflightReportDocument
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string SurveyAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ReportArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string InventorySha256 { get; init; } = string.Empty;

    [JsonRequired]
    public string ProposedMilestone { get; init; } = string.Empty;

    [JsonRequired]
    public AtlasCleanupPreflightResult[] Results { get; init; } = [];
}

public sealed record class AtlasCleanupPreflightResult
{
    [JsonRequired]
    public string ArtifactAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string ArtifactClass { get; init; } = string.Empty;

    [JsonRequired]
    public string Status { get; init; } = string.Empty;

    [JsonRequired]
    public string PlannedDisposition { get; init; } = string.Empty;

    [JsonRequired]
    public string LastUseMilestone { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpiryCondition { get; init; } = string.Empty;

    [JsonRequired]
    public string Result { get; init; } = string.Empty;
}

internal sealed record AtlasLoadedDocument<TDocument>(
    string AbsolutePath,
    byte[] Bytes,
    string Sha256,
    TDocument Document)
    where TDocument : class;

internal sealed record AtlasWorkspaceLayout(
    string ProjectRoot,
    string WorkspaceRoot,
    string SurveyAlias)
{
    public string IntakeDirectory => Path.Combine(WorkspaceRoot, "intake");

    public string RequestDirectory => Path.Combine(IntakeDirectory, "requests");

    public string StatesDirectory => Path.Combine(IntakeDirectory, "states");

    public string ManifestRevisionDirectory => Path.Combine(IntakeDirectory, "manifest-revisions");

    public string InventoryBackupsDirectory => Path.Combine(IntakeDirectory, "inventory-backups");

    public string CleanupDirectory => Path.Combine(WorkspaceRoot, "cleanup");

    public string CopiesDirectory => Path.Combine(WorkspaceRoot, "copies");

    public string CanonicalBaselineManifestPath =>
        Path.Combine(IntakeDirectory, "corpus-intake-manifest.json");

    public string CanonicalInventoryPath =>
        Path.Combine(IntakeDirectory, "private-artifact-inventory.json");

    public string CanonicalSourceRootMapPath =>
        Path.Combine(IntakeDirectory, "source-root-map.json");

    public string CanonicalCopyPlanPath =>
        Path.Combine(IntakeDirectory, "copy-plan.json");

    public string CanonicalPendingManifestPath =>
        Path.Combine(ManifestRevisionDirectory, "corpus-intake-manifest.r000004.json");

    public string CanonicalApprovedManifestPath =>
        Path.Combine(ManifestRevisionDirectory, "corpus-intake-manifest.r000005.json");

    public string CanonicalDiscoveredStatePath =>
        Path.Combine(StatesDirectory, "atlas-intake-state.r000001.json");

    public string CanonicalApprovedStatePath =>
        Path.Combine(StatesDirectory, "atlas-intake-state.r000002.json");

    public string CanonicalQualifiedStatePath =>
        Path.Combine(StatesDirectory, "atlas-intake-state.r000003.json");

    public string CanonicalPreflightedStatePath =>
        Path.Combine(StatesDirectory, "atlas-intake-state.r000004.json");

    public string CanonicalDiscoveredInventoryBackupPath =>
        Path.Combine(InventoryBackupsDirectory, "private-artifact-inventory.discovered.json");

    public string CanonicalApprovedInventoryBackupPath =>
        Path.Combine(InventoryBackupsDirectory, "private-artifact-inventory.approved.json");

    public string CanonicalQualifiedInventoryBackupPath =>
        Path.Combine(InventoryBackupsDirectory, "private-artifact-inventory.qualified.json");

    public string CanonicalPreflightedInventoryBackupPath =>
        Path.Combine(InventoryBackupsDirectory, "private-artifact-inventory.preflighted.json");

    public string CanonicalDiscoverRequestPath =>
        Path.Combine(RequestDirectory, "discover.json");

    public string CanonicalConfirmRequestPath =>
        Path.Combine(RequestDirectory, "confirm.json");

    public string CanonicalCopyRequestPath =>
        Path.Combine(RequestDirectory, "copy.json");

    public string CanonicalCleanupPreflightRequestPath =>
        Path.Combine(RequestDirectory, "cleanup-preflight.json");

    public string CanonicalIncompleteCopyPath =>
        Path.Combine(CopiesDirectory, "snapshot-a2-000001.incomplete");

    public string CanonicalFinalCopyPath =>
        Path.Combine(CopiesDirectory, "snapshot-a2-000001");

    public string CanonicalCopyReceiptPath =>
        Path.Combine(CanonicalFinalCopyPath, "copy-receipt.json");

    public string CanonicalCleanupPreflightReportPath =>
        Path.Combine(CleanupDirectory, "a2-preflight.json");

    public string PrivateGitIgnorePath => Path.Combine(
        ProjectRoot,
        "src",
        "private",
        "app",
        "celesphonia-modifier",
        ".private",
        ".gitignore");
}

public sealed class AtlasRequestException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class AtlasApprovalException(string message) : Exception(message);

public sealed class AtlasSafetyException(string message) : Exception(message);

internal sealed class AtlasValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public AtlasValidationException()
        : this("The JSON document is invalid.")
    {
    }
}

[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = false)]
[JsonSerializable(typeof(AtlasIntakeDiscoveryRequest))]
[JsonSerializable(typeof(AtlasIntakeConfirmationRequest))]
[JsonSerializable(typeof(AtlasIntakeCopyRequest))]
[JsonSerializable(typeof(AtlasCleanupPreflightRequest))]
[JsonSerializable(typeof(AtlasCorpusIntakeManifest))]
[JsonSerializable(typeof(AtlasPrivateArtifactInventoryDocument))]
[JsonSerializable(typeof(AtlasSourceRootMapDocument))]
[JsonSerializable(typeof(AtlasCopyPlanDocument))]
[JsonSerializable(typeof(AtlasIntakeStateDocument))]
[JsonSerializable(typeof(AtlasCopyReceiptDocument))]
[JsonSerializable(typeof(AtlasCleanupPreflightReportDocument))]
internal sealed partial class AtlasJsonContext : JsonSerializerContext;
