using Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ReleaseHardeningPhase15VerticalSliceServiceTests
{
    [Fact]
    public void EvaluatePassesMvpLocalAcceptanceWithEvidenceBackedRows()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        Assert.True(result.MvpLocalAcceptancePassed);
        Assert.False(result.BlockingFailuresPresent);
        Assert.False(result.FullReleaseEvidenceComplete);
        Assert.All(
            result.Checks.Where(static check => check.RequiredForMvp),
            check =>
            {
                Assert.Equal(ReleaseHardeningPhase15CheckStatus.Pass, check.Status);
                Assert.False(string.IsNullOrWhiteSpace(check.Evidence));
                Assert.False(string.IsNullOrWhiteSpace(check.Notes));
            }
        );
    }

    [Fact]
    public void EvaluateMarksWindowsGuiGitAsDeferredNonMvpInsteadOfAccepted()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        AssertDeferredNonMvp(result, "git-for-windows-helper-discovery");
        AssertDeferredNonMvp(result, "gui-launched-git-discovery");
        AssertDeferredNonMvp(result, "windows-git-path-with-spaces");
    }

    [Fact]
    public void EvaluateMarksRemainingFullReleaseEvidenceGapsAsDeferredReleaseEvidence()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        AssertDeferredReleaseEvidence(result, "remote-windows-first-platform-acceptance");
        AssertDeferredReleaseEvidence(result, "standalone-linux-x64-platform-acceptance");
        AssertDeferredReleaseEvidence(result, "final-installer-uninstaller-validation");

        ReleaseHardeningPhase15Check windows = Assert.Single(
            result.Checks,
            static check => check.Id == "remote-windows-first-platform-acceptance"
        );
        Assert.Contains("11b669b9", windows.Evidence, StringComparison.Ordinal);
        Assert.Contains("Windows 11 Enterprise", windows.Notes, StringComparison.Ordinal);
        Assert.Contains("Windows 11 24H2", windows.Notes, StringComparison.Ordinal);
        Assert.Contains("Windows Server", windows.Notes, StringComparison.Ordinal);
        Assert.Contains("remain required", windows.Notes, StringComparison.Ordinal);

        ReleaseHardeningPhase15Check linux = Assert.Single(
            result.Checks,
            static check => check.Id == "standalone-linux-x64-platform-acceptance"
        );
        Assert.Contains("21258ff3", linux.Evidence, StringComparison.Ordinal);
        Assert.Contains("linux-x64", linux.Notes, StringComparison.Ordinal);
        Assert.Contains("silent-only", linux.Notes, StringComparison.Ordinal);
        Assert.Contains("under WSL2", linux.Notes, StringComparison.Ordinal);
        Assert.Contains("Standalone Ubuntu 24.04", linux.Notes, StringComparison.Ordinal);

        ReleaseHardeningPhase15Check installer = Assert.Single(
            result.Checks,
            static check => check.Id == "final-installer-uninstaller-validation"
        );
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.DeferredReleaseEvidence, installer.Status);
        Assert.Equal("project-breakdown phase 15", installer.Evidence);
        Assert.Contains("Final installer package", installer.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateRecordsInternalDeploymentBundleWithoutReleaseClaims()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        ReleaseHardeningPhase15Check check = Assert.Single(
            result.Checks,
            static candidate => candidate.Id == "internal-deployment-validation-bundle"
        );
        Assert.True(check.RequiredForMvp);
        Assert.True(check.RequiredForFullRelease);
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.Pass, check.Status);
        Assert.Contains("phase-wp16-deployment-validation-bundle", check.Evidence);
        Assert.Contains("internal unsigned linux-x64", check.Notes);
        Assert.Contains("without authentication", check.Notes);
        Assert.Contains("not a release installer", check.Notes);
        Assert.Contains("Windows bundle acceptance", check.Notes);
    }

    [Fact]
    public void EvaluateRecordsRealPackageManagerInvocationReleaseEvidence()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        ReleaseHardeningPhase15Check check = Assert.Single(
            result.Checks,
            static candidate => candidate.Id == "real-package-manager-invocation-paths"
        );
        Assert.False(check.RequiredForMvp);
        Assert.True(check.RequiredForFullRelease);
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.Pass, check.Status);
        Assert.Contains("31e60f70", check.Evidence, StringComparison.Ordinal);
        Assert.Contains("npm 11.9.0", check.Notes, StringComparison.Ordinal);
        Assert.Contains("pnpm 11.17.0", check.Notes, StringComparison.Ordinal);
        Assert.Contains("Yarn 4.9.2", check.Notes, StringComparison.Ordinal);
        Assert.Contains("feed is public", check.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateIncludesPersistentCacheClaimAuditAsMvpPass()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        ReleaseHardeningPhase15Check check = Assert.Single(
            result.Checks,
            static candidate => candidate.Id == "persistent-derived-cache-claim-audit"
        );
        Assert.True(check.RequiredForMvp);
        Assert.True(check.RequiredForFullRelease);
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.Pass, check.Status);
        Assert.Contains("disabled", check.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateRecordsLiveAzureAuthWslReleaseEvidence()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        ReleaseHardeningPhase15Check check = Assert.Single(
            result.Checks,
            static candidate => candidate.Id == "azureauth-wsl-live-acceptance"
        );
        Assert.False(check.RequiredForMvp);
        Assert.True(check.RequiredForFullRelease);
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.Pass, check.Status);
        Assert.Contains("31e60f70", check.Evidence, StringComparison.Ordinal);
        Assert.Contains("broker", check.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without browser", check.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not establish", check.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateRecordsOpaqueCiEvidenceAndPatDeferral()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        ReleaseHardeningPhase15Check opaqueCi = Assert.Single(
            result.Checks,
            static candidate => candidate.Id == "opaque-azure-pipelines-system-access-token"
        );
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.Pass, opaqueCi.Status);
        Assert.Contains("no cache", opaqueCi.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Git bearer", opaqueCi.Notes, StringComparison.Ordinal);
        Assert.Contains("npm/pnpm/Yarn", opaqueCi.Notes, StringComparison.Ordinal);
        Assert.Contains(
            "NuGet and Python mappings are disabled",
            opaqueCi.Notes,
            StringComparison.Ordinal
        );

        ReleaseHardeningPhase15Check pat = Assert.Single(
            result.Checks,
            static candidate => candidate.Id == "pat-compatibility-production-path"
        );
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.DeferredOptionalFeature, pat.Status);
        Assert.Contains("deferred", pat.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateOptionalAzureAuthWslBackendDoesNotBlockMvpAcceptance()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        Assert.True(result.MvpLocalAcceptancePassed);
        Assert.False(result.BlockingFailuresPresent);
        Assert.False(result.FullReleaseEvidenceComplete);
    }

    private static void AssertDeferredNonMvp(ReleaseHardeningPhase15MatrixResult result, string id)
    {
        ReleaseHardeningPhase15Check check = Assert.Single(
            result.Checks,
            candidate => candidate.Id == id
        );
        Assert.False(check.RequiredForMvp);
        Assert.False(check.RequiredForFullRelease);
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.DeferredNonMvp, check.Status);
    }

    private static void AssertDeferredReleaseEvidence(
        ReleaseHardeningPhase15MatrixResult result,
        string id
    )
    {
        ReleaseHardeningPhase15Check check = Assert.Single(
            result.Checks,
            candidate => candidate.Id == id
        );
        Assert.False(check.RequiredForMvp);
        Assert.True(check.RequiredForFullRelease);
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.DeferredReleaseEvidence, check.Status);
    }
}
