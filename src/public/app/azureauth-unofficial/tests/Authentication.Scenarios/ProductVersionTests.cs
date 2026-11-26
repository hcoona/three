using System.Diagnostics;
using System.Text.RegularExpressions;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

// The computed version is the release identity recorded in operational-identities.yaml.
[TestClass]
public sealed partial class ProductVersionTests
{
    [TestMethod]
    public void SemanticVersionIsAnAlphaSemVer2WithoutBuildMetadata()
    {
        // main-v2 builds yield 0.1.0-alpha.<height>; other refs add the commit-ID suffix.
        Assert.IsTrue(Semantic().IsMatch(ProductVersion.Semantic), "Unexpected version shape.");
        Assert.IsFalse(ProductVersion.Semantic.Contains('+'), "Product tokens carry no build metadata.");
    }

    [TestMethod]
    public void FullVersionAppendsTheAbbreviatedCommitAsBuildMetadata()
    {
        Assert.IsTrue(ProductVersion.Full.StartsWith(ProductVersion.Semantic + "+", StringComparison.Ordinal));
        Assert.IsTrue(Commit().IsMatch(ProductVersion.Full[(ProductVersion.Semantic.Length + 1)..]));
    }

    [TestMethod]
    public void TelemetrySourceCarriesTheSemanticVersion()
    {
        string? observed = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source =>
            {
                if (source.Name == AuthenticationTrace.SourceName) observed = source.Version;
                return false;
            },
        };
        ActivitySource.AddActivityListener(listener);
        AuthenticationTrace.InitializeSource();
        Assert.AreEqual(ProductVersion.Semantic, observed);
    }

    [GeneratedRegex(@"^0\.1\.0-alpha\.(0|[1-9][0-9]*)(\.g[0-9a-f]{10})?$")]
    private static partial Regex Semantic();

    [GeneratedRegex("^[0-9a-f]{10}$")]
    private static partial Regex Commit();
}
