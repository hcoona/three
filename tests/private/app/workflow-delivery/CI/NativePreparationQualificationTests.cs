using System.Xml.Linq;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class NativePreparationQualificationTests
{
    private static readonly string[] RubyRows = [
        NativePreparationQualification.RubyMethod +
            " (\"buddy\",\"github-packages-ruby\",\"refs/heads/preparation-development\",False)",
        NativePreparationQualification.RubyMethod +
            " (\"official\",\"rubygems\",\"refs/heads/main\",False)",
        NativePreparationQualification.RubyMethod +
            " (\"official\",\"rubygems\",\"refs/tags/preparation-preview\",True)"];

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

    [TestMethod]
    [DataRow("python")]
    [DataRow("ruby")]
    public void CompleteOriginalNativeRowsQualify(string ecosystem) =>
        NativePreparationQualification.RequirePassed(ecosystem,
            Report(ecosystem, "complete"));

    [TestMethod]
    [DataRow("python", "missing")]
    [DataRow("python", "skipped")]
    [DataRow("python", "failed")]
    [DataRow("python", "substituted")]
    [DataRow("python", "duplicate")]
    [DataRow("python", "counters")]
    [DataRow("ruby", "missing")]
    [DataRow("ruby", "skipped")]
    [DataRow("ruby", "failed")]
    [DataRow("ruby", "substituted")]
    [DataRow("ruby", "duplicate")]
    [DataRow("ruby", "counters")]
    public void SummarySuccessCannotHideIncompleteOrUnsuccessfulNativeRows(
        string ecosystem, string defect) => Assert.ThrowsExactly<InvalidDataException>(() =>
            NativePreparationQualification.RequirePassed(ecosystem, Report(ecosystem, defect)));

    [TestMethod]
    public void UnsupportedEcosystemAndMalformedReportCannotQualify()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            NativePreparationQualification.RequirePassed("node", ""));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            NativePreparationQualification.RequirePassed("python", "<invalid"));
    }

    private static string Report(string ecosystem, string defect)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        string[] names = ecosystem == "ruby" ? RubyRows :
            [NativePreparationQualification.PythonMethod];
        var results = names.Select(name => new XElement(ns + "UnitTestResult",
            new XAttribute("testName", name), new XAttribute("outcome", "Passed"))).ToList();
        if (defect == "missing") results.RemoveAt(0);
        if (defect is "skipped" or "failed")
            results[0].SetAttributeValue("outcome", defect == "skipped" ? "NotExecuted" : "Failed");
        if (defect == "substituted") results[0].SetAttributeValue("testName", "AnotherScenario");
        if (defect == "duplicate") results.Add(new XElement(results[0]));
        var summary = new XElement(ns + "ResultSummary");
        if (defect != "counters") summary.Add(new XElement(ns + "Counters",
            new XAttribute("total", names.Length), new XAttribute("executed", names.Length),
            new XAttribute("passed", names.Length)));
        return new XElement(ns + "TestRun", summary,
            new XElement(ns + "Results", results)).ToString();
    }
}
