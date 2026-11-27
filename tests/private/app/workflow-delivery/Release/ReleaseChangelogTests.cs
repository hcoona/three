using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class ReleaseChangelogTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [TestMethod]
    public void StableVersionExtractsTargetAndPreviousSectionWhileIgnoringCodeFences()
    {
        const string markdown = """
            # Changelog

            ## [Unreleased]

            - Upcoming work.

            ## [2.1.0] - 2026-04-20

            ### Added

            - Stable release notes.

            ```markdown
            ## [Fake] - ignored inside code fence
            ```

            ## [1.0.0] - 2026-01-01

            - Initial release.

            ## [0.9.0] - 2025-12-01

            - Older history excluded beyond two sections.
            """;

        string pkgUrl = $"https://github.com/hcoona/three/blob/{Commit}/pkg/CHANGELOG.md";
        string expectedTwoSections = $"""
            ## [2.1.0] - 2026-04-20

            ### Added

            - Stable release notes.

            ```markdown
            ## [Fake] - ignored inside code fence
            ```

            ## [1.0.0] - 2026-01-01

            - Initial release.

            Full changelog: {pkgUrl}
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.AreEqual(expectedTwoSections, ReleaseChangelog.Extract(markdown, "2.1.0", Commit,
            "pkg/CHANGELOG.md"));

        string expectedSingleTail = $"""
            ## [0.9.0] - 2025-12-01

            - Older history excluded beyond two sections.

            Full changelog: {pkgUrl}
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.AreEqual(expectedSingleTail, ReleaseChangelog.Extract(markdown, "0.9.0", Commit,
            "pkg/CHANGELOG.md"));
    }

    [TestMethod]
    [DataRow("2.1.0-beta.1")]
    [DataRow("2.1.0a1")]
    [DataRow("1.0.0+g1234567")]
    public void PrereleaseVersionFallsBackToUnreleasedAndIncludesPreviousRelease(string version)
    {
        const string markdown = """
            # Changelog

            ## [Unreleased]

            ### Changed

            - Preview change.

            ## [2.0.0] - 2026-03-01

            - Older release.
            """;

        string rootUrl = $"https://github.com/hcoona/three/blob/{Commit}/CHANGELOG.md";
        string expected = $"""
            ## [Unreleased]

            ### Changed

            - Preview change.

            ## [2.0.0] - 2026-03-01

            - Older release.

            Full changelog: {rootUrl}
            """.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.AreEqual(expected, ReleaseChangelog.Extract(markdown, version, Commit,
            "CHANGELOG.md"));
    }

    [TestMethod]
    [DataRow("# Changelog\n\n## [1.0.0]\n\n- Old.\n", "2.1.0")]
    [DataRow("# Changelog\n\n## [2.1.0]\n\n   \n## [1.0.0]\n\n- Old.\n", "2.1.0")]
    [DataRow("# Changelog\n\n## [2.1.0]\n\n<!-- empty -->\n### Added\n", "2.1.0")]
    [DataRow("# Changelog\n\n## [2.1.0]\n\n- A.\n\n## [2.1.0]\n\n- B.\n", "2.1.0")]
    [DataRow("# Changelog\n\n## [1.0.0]\n\n- Old.\n", "2.1.0-rc.1")]
    [DataRow("# Changelog\n\n## [Unreleased]\n\n## [1.0.0]\n\n- Old.\n", "2.1.0-rc.1")]
    public void MissingEmptyOrDuplicateSectionFailsClosed(string markdown, string version) =>
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseChangelog.Extract(markdown, version, Commit, "CHANGELOG.md"));
}
