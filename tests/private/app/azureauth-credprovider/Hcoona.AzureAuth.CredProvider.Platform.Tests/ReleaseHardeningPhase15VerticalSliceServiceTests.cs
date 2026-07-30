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
    public void EvaluateMarksFullReleaseEvidenceGapsAsDeferredReleaseEvidence()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        AssertDeferredReleaseEvidence(result, "remote-windows-first-platform-acceptance");
        AssertDeferredReleaseEvidence(result, "real-package-manager-invocation-paths");
        AssertDeferredReleaseEvidence(result, "final-installer-uninstaller-validation");

        ReleaseHardeningPhase15Check installer = Assert.Single(
            result.Checks,
            static check => check.Area == "installer"
        );
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.DeferredReleaseEvidence, installer.Status);
        Assert.Equal("project-breakdown phase 15", installer.Evidence);
        Assert.Contains("Final installer package", installer.Notes, StringComparison.Ordinal);
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
    public void EvaluateRequiresLiveAzureAuthWslReleaseEvidence()
    {
        ReleaseHardeningPhase15MatrixResult result =
            ReleaseHardeningPhase15VerticalSliceService.Evaluate();

        ReleaseHardeningPhase15Check check = Assert.Single(
            result.Checks,
            static candidate => candidate.Id == "azureauth-wsl-live-acceptance"
        );
        Assert.False(check.RequiredForMvp);
        Assert.True(check.RequiredForFullRelease);
        Assert.Equal(ReleaseHardeningPhase15CheckStatus.DeferredReleaseEvidence, check.Status);
        Assert.Contains("implemented", check.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("live", check.Notes, StringComparison.OrdinalIgnoreCase);
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
