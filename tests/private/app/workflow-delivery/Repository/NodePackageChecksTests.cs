using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodePackageChecksTests
{
    private static readonly string[] BuildIds = ["first", "second"];
    [TestMethod]
    public void ExpandRetainsEverySelectedBuildAndOutputWithRealPrerequisites()
    {
        ReleaseUnitDeclaration unit = NodeScenario.Unit("product", ("first", "src/a"),
            ("second", "src/a"));
        var native = new Dictionary<string, PnpmProject>
        {
            ["src/a"] = new("src/a", null, []),
        };
        var versions = new Dictionary<string, string> { ["src/a"] = "1.2.3" };

        CheckSpec[] checks = NodePackageChecks.Expand(unit, native, versions);

        Assert.HasCount(6, checks);
        CollectionAssert.AreEquivalent(BuildIds,
            checks.Select(check => check.Package!.Build).Distinct().ToArray());
        foreach (ReleaseBuild build in unit.Builds)
        {
            CheckSpec[] work = checks.Where(check => check.Package!.Build == build.Id).ToArray();
            Assert.HasCount(3, work);
            CheckSpec pack = work.Single(check => check.Key.Check == "node/npm-artifact-v1");
            CheckSpec contents = work.Single(check =>
                check.Key.Check == "node/npm-artifact-contents-v1");
            CheckSpec consumer = work.Single(check =>
                check.Key.Check == "node/npm-install-import-v1");
            Assert.AreEqual(new CheckKey("src/a", "node/project-build-v1", "default"),
                Assert.ContainsSingle(pack.Prerequisites));
            Assert.AreEqual(pack.Key, Assert.ContainsSingle(contents.Prerequisites));
            Assert.AreEqual(pack.Key, Assert.ContainsSingle(consumer.Prerequisites));
            foreach (CheckSpec check in work)
            {
                Assert.AreEqual("release/product/" + build.Id, check.Key.Target);
                Assert.AreEqual("default", check.Key.Variant);
                Assert.AreEqual("ubuntu-latest", check.Runner);
                Assert.IsTrue(check.Required);
                Assert.IsEmpty(check.Dimensions);
                PackageTarget subject = check.Package!;
                Assert.AreEqual(unit.Id, subject.Unit);
                Assert.AreEqual(unit.SourcePath, subject.Declaration);
                Assert.AreEqual(build.Definition, subject.Definition);
                Assert.AreEqual("src/a", subject.Directory);
                Assert.AreEqual("src/a/package.json", subject.EntryPoint);
                Assert.IsNull(subject.PublishDirectory);
                Assert.AreEqual("1.2.3", subject.ExpectedVersion);
                Assert.AreEqual(new PackageOutput(build.Id + "-package", "primary-package",
                    "npm-tarball"), Assert.ContainsSingle(subject.Outputs));
            }
        }
    }

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
