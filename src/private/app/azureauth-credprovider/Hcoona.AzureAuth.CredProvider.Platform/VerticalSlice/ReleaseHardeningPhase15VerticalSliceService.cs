namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record ReleaseHardeningPhase15MatrixResult
{
    public required IReadOnlyList<ReleaseHardeningPhase15Check> Checks { get; init; }

    public bool MvpLocalAcceptancePassed =>
        Checks.Where(static check => check.RequiredForMvp)
            .All(static check =>
                check.Status == ReleaseHardeningPhase15CheckStatus.Pass
                && !string.IsNullOrWhiteSpace(check.Evidence));

    public bool FullReleaseEvidenceComplete =>
        Checks.Where(static check => check.RequiredForFullRelease)
            .All(static check => check.Status == ReleaseHardeningPhase15CheckStatus.Pass);

    public bool BlockingFailuresPresent =>
        Checks.Any(static check => check.Status == ReleaseHardeningPhase15CheckStatus.Blocked);
}

public sealed record ReleaseHardeningPhase15Check
{
    public required string Id { get; init; }

    public required string Area { get; init; }

    public required ReleaseHardeningPhase15CheckStatus Status { get; init; }

    public required bool RequiredForMvp { get; init; }

    public required bool RequiredForFullRelease { get; init; }

    public required string Evidence { get; init; }

    public required string Notes { get; init; }
}

public enum ReleaseHardeningPhase15CheckStatus
{
    Pass,
    DeferredNonMvp,
    DeferredReleaseEvidence,
    Blocked,
    DeferredOptionalFeature,
}

public static class ReleaseHardeningPhase15VerticalSliceService
{
    public static ReleaseHardeningPhase15MatrixResult Evaluate()
    {
        return new ReleaseHardeningPhase15MatrixResult
        {
            Checks =
            [
                Pass(
                    "cross-ecosystem-configure-doctor-cleanup",
                    "cross-ecosystem",
                    requiredForFullRelease: true,
                    "CliApplicationTests; ConfigurationPhase14VerticalSliceServiceTests",
                    "Exercises Git, NuGet, Python, npm, pnpm, and Yarn local orchestration. "
                        + "Azure Pipelines coverage is deterministic plan/lifecycle evidence, "
                        + "not live acceptance."),
                Pass(
                    "headless-ci-temporary-configuration",
                    "ci",
                    requiredForFullRelease: true,
                    "ConfigurationPhase14VerticalSliceServiceTests."
                        + "CleanupCiTemporaryRemovesAllOwnedPackageContainers",
                    "Validates WP5 opaque caller-provided token plans, secret markers, "
                        + "temporary package-manager state, and cleanup without claiming live "
                        + "runner acceptance or production composition."),
                Pass(
                    "opaque-azure-pipelines-system-access-token",
                    "identity",
                    requiredForFullRelease: true,
                    "AzurePipelinesSystemAccessTokenWp5Tests; "
                        + "phase-wp5-azure-pipelines-system-access-token",
                    "Validates direct Git bearer and npm/pnpm/Yarn registry-token forms, "
                        + "job-scoped isolation, unknown expiry, no identity binding, no cache, "
                        + "and secret-safe diagnostics. Direct NuGet and Python mappings are disabled."),
                Pass(
                    "secret-redaction-audit",
                    "security",
                    requiredForFullRelease: true,
                    "SecretRedactorTests; CliApplicationTests; "
                        + "AzurePipelinesSystemAccessTokenWp5Tests",
                    "Covers redaction helpers, deferred PAT input, and opaque system-access-token "
                        + "result, plan, manifest, and CLI paths."),
                Pass(
                    "persistent-derived-cache-claim-audit",
                    "security",
                    requiredForFullRelease: true,
                    "CredentialCoreServiceTests; CliApplicationTests",
                    "Confirms MVP output reports product-owned persistent derived credential "
                        + "caching as disabled."),
                Pass(
                    "fake-adapter-installer-uninstaller-scaffold",
                    "installer",
                    requiredForFullRelease: true,
                    "InstallerDiscoveryScaffoldTests."
                        + "RemovePlacementsAfterMaterializeMakesAllArtifactsMissing",
                    "Materializes, probes, removes, and re-probes fake adapter artifacts on "
                        + "Windows, Linux, and macOS layouts."),
                Pass(
                    "file-locking-and-rollback-safety",
                    "filesystem",
                    requiredForFullRelease: true,
                    "SystemFileSystemTests; ConfigurationManagerTests",
                    "Covers conditional mutations, lock escape rejection, rollback, and "
                        + "manifest-integrity failure modes."),
                Pass(
                    "local-path-with-spaces-process-execution",
                    "platform",
                    requiredForFullRelease: true,
                    "SystemProcessRunnerTests."
                        + "RunAsyncPassesArgumentsEnvironmentWorkingDirectoryAndStandardInput",
                    "Covers local process arguments, environment variables, working directory, "
                        + "and stdin values containing spaces."),
                Pass(
                    "configuration-scope-and-repository-write-audit",
                    "configuration",
                    requiredForFullRelease: true,
                    "ConfigurationManagerTests; ConfigurationPhase14VerticalSliceServiceTests",
                    "Keeps credential-bearing package-manager writes in user or CI temporary "
                        + "configuration-manager-owned scopes."),
                DeferredNonMvp(
                    "git-for-windows-helper-discovery",
                    "git",
                    "phase-1r-git-discovery-rescope",
                    "MVP support intentionally excludes Git for Windows helper discovery until "
                        + "a superseding evidence record reintroduces it."),
                DeferredNonMvp(
                    "gui-launched-git-discovery",
                    "git",
                    "phase-1r-git-discovery-rescope",
                    "MVP support intentionally excludes Visual Studio, VS Code, Git GUI, and "
                        + "PATH-only GUI-launched discovery."),
                DeferredNonMvp(
                    "windows-git-path-with-spaces",
                    "git",
                    "phase-1r-git-discovery-rescope",
                    "Windows Git helper paths with spaces remain outside MVP support and must "
                        + "not be claimed as accepted."),
                DeferredReleaseEvidence(
                    "remote-windows-first-platform-acceptance",
                    "platform",
                    "phase-0-decisions",
                    "Remote Windows 11 and Windows Server acceptance evidence is still required "
                        + "before full release readiness closes."),
                DeferredReleaseEvidence(
                    "real-package-manager-invocation-paths",
                    "npm",
                    "phase-1.4-npm-yarn-config-evidence",
                    "Selected real npm, pnpm, and Yarn invocation-path evidence remains a "
                        + "release evidence item beyond deterministic local fake coverage."),
                DeferredReleaseEvidence(
                    "final-installer-uninstaller-validation",
                    "installer",
                    "project-breakdown phase 15",
                    "The fake adapter scaffold is validated locally; final installer package "
                        + "install/uninstall evidence must be recorded with release artifacts."),
                DeferredOptionalFeature(
                    "pat-compatibility-production-path",
                    "identity",
                    "phase-wp5-azure-pipelines-system-access-token",
                    "The frozen PAT enum and wire value remain compatible, but reusable production "
                        + "PAT acquisition and materialization are explicitly deferred with no "
                        + "fallback, cache, or invented identity."),
                DeferredOptionalFeature(
                    "optional-azureauth-wsl-backend",
                    "identity",
                    "phase-v5a-wsl-azureauth-backend-governance",
                    "Optional WSL-to-Windows AzureAuth.exe backend. Disabled and unshippable "
                        + "until all v5a gates pass. Does not block MVP or direct-MSAL release "
                        + "readiness. Not Windows-native Git/Visual Studio/NuGet.exe acceptance. "
                        + "Current fake Phase 15 rows are not live evidence for this path."),
            ],
        };
    }

    private static ReleaseHardeningPhase15Check Pass(
        string id,
        string area,
        bool requiredForFullRelease,
        string evidence,
        string notes
    )
    {
        return new ReleaseHardeningPhase15Check
        {
            Id = id,
            Area = area,
            Status = ReleaseHardeningPhase15CheckStatus.Pass,
            RequiredForMvp = true,
            RequiredForFullRelease = requiredForFullRelease,
            Evidence = evidence,
            Notes = notes,
        };
    }

    private static ReleaseHardeningPhase15Check DeferredNonMvp(
        string id,
        string area,
        string evidence,
        string notes
    )
    {
        return new ReleaseHardeningPhase15Check
        {
            Id = id,
            Area = area,
            Status = ReleaseHardeningPhase15CheckStatus.DeferredNonMvp,
            RequiredForMvp = false,
            RequiredForFullRelease = false,
            Evidence = evidence,
            Notes = notes,
        };
    }

    private static ReleaseHardeningPhase15Check DeferredReleaseEvidence(
        string id,
        string area,
        string evidence,
        string notes
    )
    {
        return new ReleaseHardeningPhase15Check
        {
            Id = id,
            Area = area,
            Status = ReleaseHardeningPhase15CheckStatus.DeferredReleaseEvidence,
            RequiredForMvp = false,
            RequiredForFullRelease = true,
            Evidence = evidence,
            Notes = notes,
        };
    }

    private static ReleaseHardeningPhase15Check DeferredOptionalFeature(
        string id,
        string area,
        string evidence,
        string notes
    ) =>
        new ReleaseHardeningPhase15Check
        {
            Id = id,
            Area = area,
            Status = ReleaseHardeningPhase15CheckStatus.DeferredOptionalFeature,
            RequiredForMvp = false,
            RequiredForFullRelease = false,
            Evidence = evidence,
            Notes = notes,
        };
}
