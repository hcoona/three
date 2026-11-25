using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodePackageChecksTests
{
    [TestMethod]
    [DataRow("definition")]
    [DataRow("entry")]
    [DataRow("multiple-outputs")]
    [DataRow("kind")]
    [DataRow("missing-version")]
    [DataRow("publish-directory")]
    public void ExpandRejectsUnresolvedOrUnsupportedCompleteBuild(string defect)
    {
        ReleaseUnitDeclaration unit = NodeScenario.Unit("product", ("build", "src/a"));
        ReleaseBuild build = unit.Builds[0];
        build = defect switch
        {
            "definition" => build with { Definition = "python/package-v1" },
            "entry" => build with { EntryPoint = "src/missing/package.json" },
            "multiple-outputs" => build with
            {
                Outputs = [.. build.Outputs, new("extra", "primary-package", "npm-tarball")],
            },
            "kind" => build with { Outputs = [new("archive", "primary-package", "unknown")] },
            _ => build,
        };
        unit = unit with { Builds = [build] };
        var native = new Dictionary<string, PnpmProject>
        {
            ["src/a"] = NodeScenario.Project("src/a")
        };
        if (defect == "publish-directory")
            native["src/a"] = new("src/a", "src/a/dist", []);
        var versions = new Dictionary<string, string>();
        if (defect != "missing-version")
            versions.Add("src/a", "1.2.3");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            NodePackageChecks.Expand(unit, native, versions));
    }
}
