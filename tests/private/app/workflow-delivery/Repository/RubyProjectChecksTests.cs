using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyProjectChecksTests
{
    [TestMethod]
    public void ExplicitAdoptionRequiresTheWholeProjectRSpecCheck()
    {
        var check = Assert.ContainsSingle(RubyProjectChecks.Expand(
            new("ruby/rspec-v1", "src/product/workflow-delivery.quality.yml"), "src/product"));
        Assert.AreEqual("src/product", check.Key.Target);
        Assert.AreEqual("ruby/project-rspec-v1", check.Key.Check);
        Assert.AreEqual("default", check.Key.Variant);
        Assert.AreEqual("ubuntu-latest", check.Runner);
        Assert.IsTrue(check.Required);
        Assert.IsEmpty(check.Dimensions);
        Assert.IsEmpty(check.Prerequisites);
        Assert.IsNull(check.Package);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("ruby/unknown-v1")]
    [DataRow("node/hcoona-release-smoke-npm-v1")]
    public void DiscoveryCannotSupplyAnImplicitQualityObligation(string? preset)
    {
        QualitySelection? selection = preset is null ? null : new(preset, "quality.yml");
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RubyProjectChecks.Expand(selection, "src/product"));
    }

    [TestMethod]
    [DataRow(".")]
    [DataRow("../product")]
    [DataRow("/product")]
    [DataRow("src\\product")]
    public void WholeProjectWorkRequiresItsCanonicalSourceDirectory(string directory) =>
        Assert.ThrowsExactly<InvalidDataException>(() => RubyProjectChecks.Expand(
            new("ruby/rspec-v1", "quality.yml"), directory));
}
