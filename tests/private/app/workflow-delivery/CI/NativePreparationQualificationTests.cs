using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class NativePreparationQualificationTests
{
    [TestMethod]
    [DataRow("README.md", false, false)]
    [DataRow("pyproject.toml", true, false)]
    [DataRow("uv.lock", true, false)]
    [DataRow("uv.toml", true, false)]
    [DataRow(".config/uv/uv.toml", true, false)]
    [DataRow(".python-version", true, false)]
    [DataRow("src/public/lib/nbgv-python/nbgv.py", true, false)]
    [DataRow("src/private/app/workflow-delivery/Release/RubyPreparation.cs", true, true)]
    [DataRow("tests/private/app/workflow-delivery/Release/PythonPreparationNativeTests.cs",
        true, true)]
    [DataRow("src/private/Directory.Build.props", true, true)]
    [DataRow("src/global.json", true, true)]
    [DataRow("tests/nuget.config", true, true)]
    [DataRow("src/private/app/workflow-delivery/docs/README.md", false, false)]
    [DataRow("tests/workflow-delivery.quality.yml", true, true)]
    [DataRow("src/private/app/workflow-delivery/workflow-delivery.quality.yml", false, false)]
    [DataRow("src/public/lib/nbgv-python/workflow-delivery.quality.yml", false, false)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/" +
        "hcoona-release-smoke-python/workflow-delivery.quality.yml", true, false)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/" +
        "hcoona-release-smoke-ruby/workflow-delivery.quality.yml", false, true)]
    [DataRow("src/public/lib/other/version.json", false, false)]
    [DataRow("mise.lock", true, true)]
    public void NativeScenarioSelectionRetainsActualSourceAndInheritedInputs(
        string path, bool python, bool ruby)
    {
        NativePreparationSelection result = NativePreparationQualification.Select(
            new("basis", "candidate", false), [path]);
        Assert.AreEqual(python, result.Python);
        Assert.AreEqual(ruby, result.Ruby);
        Assert.AreEqual(new CiComparison("basis", "candidate", false), result.Comparison);
    }

    [TestMethod]
    public void ExplicitFullSelectsBothNativeObligationsWithoutChangingSource()
    {
        NativePreparationSelection result = NativePreparationQualification.Select(
            new("candidate", "candidate", true), []);
        Assert.IsTrue(result.Python && result.Ruby);
        Assert.ThrowsExactly<InvalidDataException>(() => NativePreparationQualification.Select(
            new("basis", "candidate", true), []));
    }
}
