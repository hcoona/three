namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed record ReleaseHardeningPhase15MatrixResult
{
    public required IReadOnlyList<ReleaseHardeningPhase15Check> Checks { get; init; }

    public bool MvpLocalAcceptancePassed =>
        Checks
            .Where(static check => check.RequiredForMvp)
            .All(static check =>
                check.Status == ReleaseHardeningPhase15CheckStatus.Pass
                && !string.IsNullOrWhiteSpace(check.Evidence)
            );

    public bool FullReleaseEvidenceComplete =>
        Checks
            .Where(static check => check.RequiredForFullRelease)
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
                        + "not live acceptance."
                ),
                Pass(
                    "headless-ci-temporary-configuration",
                    "ci",
                    requiredForFullRelease: true,
                    "ConfigurationPhase14VerticalSliceServiceTests."
                        + "CleanupCiTemporaryRemovesAllOwnedPackageContainers",
                    "Validates WP5 opaque caller-provided token plans, secret markers, "
                        + "temporary package-manager state, and cleanup without claiming live "
                        + "runner acceptance or production composition."
                ),
                Pass(
                    "opaque-azure-pipelines-system-access-token",
                    "identity",
                    requiredForFullRelease: true,
                    "AzurePipelinesSystemAccessTokenWp5Tests; "
                        + "phase-wp5-azure-pipelines-system-access-token",
                    "Validates direct Git bearer and npm/pnpm/Yarn registry-token forms, "
                        + "job-scoped isolation, unknown expiry, no identity binding, no cache, "
                        + "and secret-safe diagnostics. "
                        + "Direct NuGet and Python mappings are disabled."
                ),
                Pass(
                    "secret-redaction-audit",
                    "security",
                    requiredForFullRelease: true,
                    "SecretRedactorTests; CliApplicationTests; "
                        + "AzurePipelinesSystemAccessTokenWp5Tests",
                    "Covers redaction helpers, deferred PAT input, and opaque system-access-token "
                        + "result, plan, manifest, and CLI paths."
                ),
                Pass(
                    "persistent-derived-cache-claim-audit",
                    "security",
                    requiredForFullRelease: true,
                    "CredentialCoreServiceTests; CliApplicationTests",
                    "Confirms MVP output reports product-owned persistent derived credential "
                        + "caching as disabled."
                ),
                Pass(
                    "file-locking-and-targeted-removal-safety",
                    "filesystem",
                    requiredForFullRelease: true,
                    "SystemFileSystemTests; ConfigurationManagerTests",
                    "Covers ownership-group locking, exact selector removal, unrelated "
                        + "configuration preservation, and malformed manifest handling."
                ),
                Pass(
                    "local-path-with-spaces-process-execution",
                    "platform",
                    requiredForFullRelease: true,
                    "SystemProcessRunnerTests."
                        + "RunAsyncPassesArgumentsEnvironmentWorkingDirectoryAndStandardInput",
                    "Covers local process arguments, environment variables, working directory, "
                        + "and stdin values containing spaces."
                ),
                Pass(
                    "configuration-scope-and-repository-write-audit",
                    "configuration",
                    requiredForFullRelease: true,
                    "ConfigurationManagerTests; ConfigurationPhase14VerticalSliceServiceTests",
                    "Keeps credential-bearing package-manager writes in user or CI temporary "
                        + "configuration-manager-owned scopes."
                ),
                DeferredNonMvp(
                    "git-for-windows-helper-discovery",
                    "git",
                    "phase-1r-git-discovery-rescope",
                    "MVP support intentionally excludes Git for Windows helper discovery until "
                        + "a superseding evidence record reintroduces it."
                ),
                DeferredNonMvp(
                    "gui-launched-git-discovery",
                    "git",
                    "phase-1r-git-discovery-rescope",
                    "MVP support intentionally excludes Visual Studio, VS Code, Git GUI, and "
                        + "PATH-only GUI-launched discovery."
                ),
                DeferredNonMvp(
                    "windows-git-path-with-spaces",
                    "git",
                    "phase-1r-git-discovery-rescope",
                    "Windows Git helper paths with spaces remain outside MVP support and must "
                        + "not be claimed as accepted."
                ),
                DeferredReleaseEvidence(
                    "remote-windows-first-platform-acceptance",
                    "platform",
                    "phase-0-decisions; phase-wp3-azureauth-process-provider; commit 11b669b9",
                    "Native Windows 11 Enterprise x64 apphost, identity configuration, and "
                        + "AzureAuth login acceptance passed on build 26200 on 2026-07-30. "
                        + "The exact Windows 11 24H2 baseline, Windows Server 2022 or 2025, "
                        + "and installer-produced binary acceptance remain required before full "
                        + "release readiness closes."
                ),
                ReleaseEvidencePass(
                    "standalone-linux-x64-platform-acceptance",
                    "platform",
                    "phase-wp3-azureauth-process-provider; "
                        + "phase-wp16-deployment-validation-bundle; implementation after "
                        + "commit 46424808; "
                        + "AzureAuth 0.9.5 release commit 21258ff3",
                    "On 2026-08-04, the internal deployment-validation Linux apphost completed "
                        + "explicit headless device-code login and subsequent Git and Python "
                        + "silent-only cache acquisitions through the verified official "
                        + "AzureAuth 0.9.5 linux-x64 artifact. Credential output was captured "
                        + "for structural validation but not printed. The run used WSL2 with WSL "
                        + "detection disabled; by explicit operator decision, this forced-native "
                        + "execution closes the repository's standalone Linux platform gate. "
                        + "AzureAuth used its documented owner-only headless file-cache fallback "
                        + "after keyring persistence was unavailable. All isolated product, "
                        + "configuration, and AzureAuth cache state was removed afterward. Native "
                        + "system-keyring behavior and release-installer-produced binary "
                        + "acceptance remain separate evidence."
                ),
                ReleaseEvidencePass(
                    "real-package-manager-invocation-paths",
                    "npm",
                    "phase-wp7-registry-credential-lifecycle; commit 31e60f70",
                    "On 2026-07-30, npm 11.9.0, pnpm 11.17.0, and Yarn 4.9.2 "
                        + "resolved a known package from the public Azure Artifacts feed through "
                        + "isolated production configuration paths. Product unconfiguration "
                        + "removed the auth selectors and ownership sidecars before the temporary "
                        + "root was deleted. Because the feed is public, this evidence does not "
                        + "claim private-feed authorization."
                ),
                Pass(
                    "internal-deployment-validation-bundle",
                    "installer",
                    requiredForFullRelease: true,
                    "phase-wp16-deployment-validation-bundle; "
                        + "New-DeploymentValidationBundle.ps1; "
                        + "Install-DeploymentValidationBundle.ps1; "
                        + "Uninstall-DeploymentValidationBundle.ps1",
                    "On 2026-07-31, an internal unsigned linux-x64 bundle completed an isolated "
                        + "install, CLI and Git launcher invocation, Git/NuGet/Python "
                        + "configuration without authentication, wheel contract validation, "
                        + "configuration-aware uninstall, and exact payload removal. This is "
                        + "deployment-shape evidence only, not a release installer, standalone "
                        + "Ubuntu acceptance, or Windows bundle acceptance."
                ),
                DeferredReleaseEvidence(
                    "final-installer-uninstaller-validation",
                    "installer",
                    "project-breakdown phase 15",
                    "Final installer package install/uninstall evidence must be recorded with "
                        + "release artifacts."
                ),
                DeferredOptionalFeature(
                    "pat-compatibility-production-path",
                    "identity",
                    "phase-wp5-azure-pipelines-system-access-token",
                    "The frozen PAT enum and wire value remain compatible, but reusable production "
                        + "PAT acquisition and materialization are explicitly deferred with no "
                        + "fallback, cache, or invented identity."
                ),
                ReleaseEvidencePass(
                    "azureauth-wsl-live-acceptance",
                    "identity",
                    "phase-wp3-azureauth-process-provider; commit 31e60f70",
                    "On 2026-07-30, the production WSL apphost discovered the pinned Windows "
                        + "AzureAuth 0.9.5 installation and completed token acquisition through "
                        + "the Windows default broker path without browser interaction or secret "
                        + "output. This does not establish Windows-native Git, Visual Studio, or "
                        + "NuGet.exe acceptance."
                ),
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

    private static ReleaseHardeningPhase15Check ReleaseEvidencePass(
        string id,
        string area,
        string evidence,
        string notes
    ) =>
        new()
        {
            Id = id,
            Area = area,
            Status = ReleaseHardeningPhase15CheckStatus.Pass,
            RequiredForMvp = false,
            RequiredForFullRelease = true,
            Evidence = evidence,
            Notes = notes,
        };

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
