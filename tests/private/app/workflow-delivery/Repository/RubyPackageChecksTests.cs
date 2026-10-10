using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyPackageChecksTests
{
    private static readonly string Root = Path.GetFullPath("ruby package endpoint");
    private static readonly string[] RequirePaths = ["lib"];
    private const string Declaration = """
        schema: workflow-delivery/v3/release-unit
        release-unit: hcoona-release-smoke-ruby
        builds:
          - id: ruby-gem
            definition: ruby/gem-v1
            entry-point: hcoona-release-smoke-ruby.gemspec
            outputs:
              - id: gem
                role: primary-package
                kind: ruby-gem
        """;

    private static ReleaseUnitDeclaration Unit(string directory) =>
        ReleaseUnitDeclarationReader.Read(Declaration,
            directory + "/workflow-delivery.release-unit.yml");

    private static RubySourceGemFacts Source(string directory, string version) =>
        new(Path.GetFullPath(Path.Combine(Root, directory,
            "hcoona-release-smoke-ruby.gemspec")),
            new("hcoona-release-smoke-ruby", version, "ruby", RequirePaths, [], []));

    [TestMethod]
    [DataRow("src/original", "1.2.3")]
    [DataRow("src/moved", "1.2.3.pre.beta.4")]
    public void StandaloneGemRetainsCompleteChecksAndOriginalNativeSubject(
        string directory, string expectedVersion)
    {
        ReleaseUnitDeclaration unit = Unit(directory);
        var sources = new Dictionary<string, RubySourceGemFacts> {
            [directory] = Source(directory, expectedVersion) };
        CheckSpec[] checks = RubyPackageChecks.Expand(unit, Root, sources);
        CollectionAssert.AreEqual(RubyPackageChecks.Kinds,
            checks.Select(check => check.Key.Check).ToArray());
        Assert.IsTrue(checks.All(check => check.Required && check.Runner == "ubuntu-latest" &&
            check.Key.Target == "release/hcoona-release-smoke-ruby/ruby-gem" &&
            check.Key.Variant == "default"));
        PackageTarget subject = checks[0].Package!;
        Assert.AreEqual(directory, subject.Directory);
        Assert.AreEqual(expectedVersion, subject.ExpectedVersion);
        Assert.AreEqual(unit.SourcePath, subject.Declaration);
        Assert.AreEqual(directory + "/hcoona-release-smoke-ruby.gemspec", subject.EntryPoint);
        Assert.AreEqual("ruby/gem-v1", subject.Definition);
        Assert.IsNull(subject.PublishDirectory);
        Assert.AreEqual(new PackageOutput("gem", "primary-package", "ruby-gem"),
            Assert.ContainsSingle(subject.Outputs));
        Assert.IsTrue(checks.All(check => check.Package == subject));
        Assert.IsEmpty(checks[0].Prerequisites);
        CollectionAssert.AreEqual(new[] { checks[0].Key }, checks[1].Prerequisites);
        CollectionAssert.AreEqual(new[] { checks[0].Key, checks[1].Key },
            checks[2].Prerequisites);
    }

    [TestMethod]
    [DataRow("missing-source")]
    [DataRow("other-gemspec")]
    [DataRow("relative-gemspec")]
    [DataRow("other-name")]
    [DataRow("missing-version")]
    [DataRow("native-platform")]
    [DataRow("extra-output")]
    public void UnresolvedOrUnsupportedNativeGemCannotSupplyAdoptedChecks(string defect)
    {
        const string directory = "src/gem";
        RubySourceGemFacts source = Source(directory, "1.2.3");
        source = defect switch
        {
            "other-gemspec" => source with { Gemspec = Path.Combine(Root, "other.gemspec") },
            "relative-gemspec" => source with { Gemspec = "relative.gemspec" },
            "other-name" => source with { Gem = source.Gem with { Name = "other" } },
            "missing-version" => source with { Gem = source.Gem with { Version = "" } },
            "native-platform" => source with { Gem = source.Gem with { Platform = "x86_64" } },
            _ => source,
        };
        var sources = new Dictionary<string, RubySourceGemFacts>();
        if (defect != "missing-source") sources.Add(directory, source);
        ReleaseUnitDeclaration unit = Unit(directory);
        if (defect == "extra-output")
            unit = unit with { Builds = [unit.Builds[0] with {
                Outputs = [.. unit.Builds[0].Outputs, new("other", "source-package", "ruby-gem")]
            }] };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            RubyPackageChecks.Expand(unit, Root, sources));
    }

    [TestMethod]
    [DataRow("definition: ruby/gem-v1", "definition: ruby/other-v1")]
    [DataRow("entry-point: hcoona-release-smoke-ruby.gemspec", "entry-point: other.gemspec")]
    [DataRow("kind: ruby-gem", "kind: npm-tarball")]
    [DataRow("role: primary-package", "role: source-package")]
    public void UnsupportedDeclarationCannotBePartiallyExpanded(string before, string after)
    {
        const string directory = "src/gem";
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
            Declaration.Replace(before, after, StringComparison.Ordinal),
            directory + "/workflow-delivery.release-unit.yml");
        Assert.ThrowsExactly<InvalidDataException>(() => RubyPackageChecks.Expand(unit, Root,
            new Dictionary<string, RubySourceGemFacts> {
                [directory] = Source(directory, "1.2.3") }));
    }
}
