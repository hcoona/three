using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonPackageChecksTests
{
    internal static readonly ReleaseUnitDeclaration Unit = new(
        "product/workflow-delivery.release-unit.yml", "product",
        [new("distributions", PythonPackageChecks.Build, "pyproject.toml",
            [new("wheel", "primary-package", "python-wheel"),
                new("source", "source-package", "python-sdist")])]);
    internal static readonly PythonMetadataMember[] Members =
        [new("hcoona-release-smoke-python", "product", "native-id")];

    [TestMethod]
    public void CompletePythonObligationsRetainOriginalOutputs()
    {
        CheckSpec[] checks = PythonPackageChecks.Expand(Unit, Members,
            new Dictionary<string, string> { ["product"] = "1.0.0.dev3+g1234567" });
        Assert.HasCount(4, checks);
        CollectionAssert.AreEquivalent(PythonPackageChecks.Kinds,
            checks.Select(check => check.Key.Check).ToArray());
        CheckSpec build = checks.Single(check => check.Key.Check == PythonPackageChecks.Build);
        CheckSpec contents = checks.Single(
            check => check.Key.Check == PythonPackageChecks.Contents);
        Assert.IsEmpty(build.Prerequisites);
        Assert.AreEqual(build.Key, Assert.ContainsSingle(contents.Prerequisites));
        foreach (CheckSpec check in checks)
        {
            Assert.AreEqual("release/product/distributions", check.Key.Target);
            Assert.AreEqual("default", check.Key.Variant);
            Assert.AreEqual("ubuntu-latest", check.Runner);
            Assert.IsTrue(check.Required);
            Assert.IsEmpty(check.Dimensions);
            Assert.AreEqual("product/pyproject.toml", check.Package!.EntryPoint);
            Assert.AreEqual("1.0.0.dev3+g1234567", check.Package.ExpectedVersion);
            CollectionAssert.AreEqual(new PackageOutput[] {
                new("wheel", "primary-package", "python-wheel"),
                new("source", "source-package", "python-sdist") }, check.Package.Outputs);
            if (check.Key.Check is PythonPackageChecks.Wheel or PythonPackageChecks.Sdist)
                CollectionAssert.AreEqual(new[] { build.Key, contents.Key }, check.Prerequisites);
        }
    }

    [TestMethod]
    [DataRow("member")]
    [DataRow("version")]
    [DataRow("entry")]
    [DataRow("definition")]
    [DataRow("output")]
    public void UnresolvedNativePackageFactsRejectBeforeExecution(string defect)
    {
        ReleaseBuild build = Unit.Builds[0];
        if (defect == "entry") build = build with { EntryPoint = "other.toml" };
        if (defect == "definition") build = build with { Definition = "unknown" };
        if (defect == "output") build = build with { Outputs = [build.Outputs[0]] };
        Assert.ThrowsExactly<InvalidDataException>(() => PythonPackageChecks.Expand(
            Unit with { Builds = [build] }, defect == "member" ? [] : Members,
            defect == "version" ? new Dictionary<string, string>() :
                new Dictionary<string, string> { ["product"] = "1.0" }));
    }
}
