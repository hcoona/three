using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class NativePreparationQualificationTests
{
    [TestMethod]
    [DataRow("README.md", false, false, false)]
    [DataRow("pyproject.toml", true, false, false)]
    [DataRow("uv.lock", true, false, false)]
    [DataRow("uv.toml", true, false, false)]
    [DataRow(".config/uv/uv.toml", true, false, false)]
    [DataRow(".python-version", true, false, false)]
    [DataRow("src/public/lib/nbgv-python/nbgv.py", true, false, false)]
    [DataRow("src/private/app/workflow-delivery/Release/RubyPreparation.cs", true, true, true)]
    [DataRow("tests/private/app/workflow-delivery/Release/PythonPreparationNativeTests.cs",
        true, true, true)]
    [DataRow("src/private/Directory.Build.props", true, true, true)]
    [DataRow("src/global.json", true, true, true)]
    [DataRow("tests/nuget.config", true, true, true)]
    [DataRow("src/private/app/workflow-delivery/docs/README.md", false, false, false)]
    [DataRow("tests/workflow-delivery.quality.yml", true, true, false)]
    [DataRow("src/private/app/workflow-delivery/workflow-delivery.quality.yml",
        false, false, false)]
    [DataRow("src/public/lib/nbgv-python/workflow-delivery.quality.yml",
        false, false, false)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/" +
        "hcoona-release-smoke-python/workflow-delivery.quality.yml", true, false, false)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/" +
        "hcoona-release-smoke-ruby/workflow-delivery.quality.yml", false, true, false)]
    [DataRow("src/public/lib/other/version.json", false, false, false)]
    [DataRow("mise.lock", true, true, true)]
    [DataRow("pnpm-lock.yaml", false, false, true)]
    [DataRow("pnpm-workspace.yaml", false, false, true)]
    [DataRow("package.json", false, false, true)]
    [DataRow(".pnpmfile.mjs", false, false, true)]
    [DataRow("mise.toml", true, true, true)]
    [DataRow(".github/workflows/ci.yml", true, true, true)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/pnpm-contract/pnpm-lock.yaml",
        true, true, true)]
    [DataRow("src/private/app/workflow-delivery/Repository/PnpmGraphReader.cs", true, true,
        true)]
    [DataRow("src/public/lib/hexo-renderer-asciidoc/package.json", false, false, false)]
    public void NativeScenarioSelectionRetainsActualSourceAndInheritedInputs(
        string path, bool python, bool ruby, bool node)
    {
        NativePreparationSelection result = NativePreparationQualification.Select(
            new("basis", "candidate", false), [path]);
        Assert.AreEqual(python, result.Python);
        Assert.AreEqual(ruby, result.Ruby);
        Assert.AreEqual(node, result.Node);
        Assert.AreEqual(new CiComparison("basis", "candidate", false), result.Comparison);
    }

    [TestMethod]
    public void ExplicitFullSelectsBothNativeObligationsWithoutChangingSource()
    {
        NativePreparationSelection result = NativePreparationQualification.Select(
            new("candidate", "candidate", true), []);
        Assert.IsTrue(result.Python && result.Ruby && result.Node);
        Assert.ThrowsExactly<InvalidDataException>(() => NativePreparationQualification.Select(
            new("basis", "candidate", true), []));
    }
}
