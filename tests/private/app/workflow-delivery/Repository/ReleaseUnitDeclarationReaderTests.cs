using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;
using YamlDotNet.Core;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ReleaseUnitDeclarationReaderTests
{
    private const string Source = "src/project/workflow-delivery.release-unit.yml";
    private const string Declaration = "schema: workflow-delivery/v3/release-unit\n" +
        "release-unit: product\n" +
        "builds: [{id: build, definition: native/build-v1, entry-point: manifest, " +
        "outputs: [{id: output, role: primary, kind: package}]}]";

    [TestMethod]
    [DataRow("npm", "npm-package", "node/npm-package-v1", "package.json", "npm-tarball",
        "npm-tarball")]
    [DataRow("github-packages", "nuget-package", "dotnet/nuget-package-v1",
        "hcoona-release-smoke-github-packages.csproj", "nuget-package", "nuget-package")]
    [DataRow("python", "python-distributions", "python/distribution-set-v1", "pyproject.toml",
        "wheel", "python-wheel")]
    [DataRow("ruby", "ruby-gem", "ruby/gem-v1", "hcoona-release-smoke-ruby.gemspec", "gem",
        "ruby-gem")]
    public void ReadPreservesExistingDeclarationShapes(string ecosystem, string buildId,
        string definition, string entryPoint, string outputId, string kind)
    {
        string unitId = "hcoona-release-smoke-" + ecosystem;
        string source = "src/public/lib/" + unitId + "/workflow-delivery.release-unit.yml";
        string content = $"""
            schema: workflow-delivery/v3/release-unit
            release-unit: {unitId}
            builds:
              - id: {buildId}
                definition: {definition}
                entry-point: {entryPoint}
                outputs:
                  - id: {outputId}
                    role: primary-package
                    kind: {kind}
            """;
        ReleaseOutput[] expected = [new(outputId, "primary-package", kind)];
        if (ecosystem == "python")
        {
            content += "\n      - id: sdist\n        role: source-package\n" +
                "        kind: python-sdist";
            expected = [.. expected, new("sdist", "source-package", "python-sdist")];
        }

        ReleaseUnitDeclaration result = ReleaseUnitDeclarationReader.Read(content, source);

        Assert.AreEqual(source, result.SourcePath);
        Assert.AreEqual(unitId, result.Id);
        ReleaseBuild build = Assert.ContainsSingle(result.Builds);
        AssertBuild(build, buildId, definition, entryPoint, expected);
    }

    [TestMethod]
    public void ReadPreservesAllBuildsAndUnitWideOutputOrder()
    {
        const string content = """
            schema: workflow-delivery/v3/release-unit
            release-unit: combined-product
            builds:
              - id: z-build
                definition: custom/not-registered-here
                entry-point: nested/package.json
                outputs:
                  - {id: z-output, role: primary-package, kind: custom-binary}
                  - {id: a-output, role: source-package, kind: source-archive}
              - id: a-build
                definition: another/native-definition
                entry-point: .
                outputs:
                  - {id: second-output, role: primary-package, kind: other-package}
            """;

        ReleaseUnitDeclaration result = ReleaseUnitDeclarationReader.Read(content,
            "workflow-delivery.release-unit.yml");

        Assert.AreEqual("workflow-delivery.release-unit.yml", result.SourcePath);
        Assert.AreEqual("combined-product", result.Id);
        Assert.HasCount(2, result.Builds);
        AssertBuild(result.Builds[0], "z-build", "custom/not-registered-here",
            "nested/package.json",
            [new("z-output", "primary-package", "custom-binary"),
             new("a-output", "source-package", "source-archive")]);
        AssertBuild(result.Builds[1], "a-build", "another/native-definition", ".",
            [new("second-output", "primary-package", "other-package")]);
    }

    [TestMethod]
    public void ReadUsesLibraryAliasesAndQuotedLiteralDefinitions()
    {
        const string content = """
            schema: workflow-delivery/v3/release-unit
            release-unit: product
            builds:
              - id: build
                definition: 'null'
                entry-point: manifest
                outputs:
                  - id: output
                    role: &role primary
                    kind: *role
            """;

        ReleaseUnitDeclaration result = ReleaseUnitDeclarationReader.Read(content, Source);

        AssertBuild(Assert.ContainsSingle(result.Builds), "build", "null", "manifest",
            [new("output", "primary", "primary")]);
    }

    [TestMethod]
    [DataRow("schema: workflow-delivery/v3/release-unit\n", "")]
    [DataRow("workflow-delivery/v3/release-unit", "wrong")]
    [DataRow("release-unit: product\n", "")]
    [DataRow("release-unit: product", "release-unit: null")]
    [DataRow("release-unit: product", "release-unit: product\nextra: value")]
    [DataRow("builds: [", "other: [")]
    [DataRow("id: build, ", "")]
    [DataRow("definition: native/build-v1, ", "")]
    [DataRow("definition: native/build-v1", "definition: '  '")]
    [DataRow("definition: native/build-v1", "definition: [native/build-v1]")]
    [DataRow("entry-point: manifest, ", "")]
    [DataRow("entry-point: manifest", "entry-point: null")]
    [DataRow("entry-point: manifest", "entry-point: {path: manifest}")]
    [DataRow("id: build", "id: build, extra: value")]
    [DataRow("outputs:", "other:")]
    [DataRow("id: output, ", "")]
    [DataRow("role: primary, ", "")]
    [DataRow("role: primary", "role: null")]
    [DataRow("kind: package", "other: package")]
    [DataRow("kind: package", "kind: package, extra: value")]
    public void ReadRejectsMalformedApplicationSchema(string original, string replacement)
    {
        string content = Declaration.Replace(original, replacement, StringComparison.Ordinal);

        Assert.ThrowsExactly<InvalidDataException>(
            () => ReleaseUnitDeclarationReader.Read(content, Source));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[]")]
    [DataRow("[null]")]
    [DataRow("[[]]")]
    public void ReadRejectsMissingOrInvalidBuilds(string builds)
    {
        string content = "schema: workflow-delivery/v3/release-unit\n" +
            "release-unit: product\nbuilds: " + builds;

        Assert.ThrowsExactly<InvalidDataException>(
            () => ReleaseUnitDeclarationReader.Read(content, Source));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("[]")]
    [DataRow("[null]")]
    [DataRow("[[]]")]
    public void ReadRejectsMissingOrInvalidOutputs(string outputs)
    {
        string content = "schema: workflow-delivery/v3/release-unit\nrelease-unit: product\n" +
            "builds: [{id: build, definition: native/build-v1, entry-point: manifest, outputs: " +
            outputs + "}]";

        Assert.ThrowsExactly<InvalidDataException>(
            () => ReleaseUnitDeclarationReader.Read(content, Source));
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void ReadRejectsDuplicateBuildAndOutputIdentities(bool duplicateBuild,
        bool sameBuildOutputs)
    {
        string builds = sameBuildOutputs
            ? "[{id: build, definition: native/build, entry-point: manifest, outputs: " +
                "[{id: output, role: primary, kind: package}, " +
                "{id: output, role: source, kind: archive}]}]"
            : "[{id: build, definition: native/build, entry-point: first, outputs: " +
                "[{id: output, role: primary, kind: package}]}, " +
                "{id: " + (duplicateBuild ? "build" : "second") +
                ", definition: other/build, entry-point: second, outputs: " +
                "[{id: " + (duplicateBuild ? "other" : "output") +
                ", role: primary, kind: package}]}]";

        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseUnitDeclarationReader.Read(
            "schema: workflow-delivery/v3/release-unit\nrelease-unit: product\nbuilds: " + builds,
            Source));
    }

    [TestMethod]
    [DataRow("release-unit: product", "release-unit: Product")]
    [DataRow("id: build", "id: -build")]
    [DataRow("id: output", "id: output-")]
    [DataRow("role: primary", "role: primary_package")]
    [DataRow("kind: package", "kind: 'two words'")]
    [DataRow("kind: package", "kind: ''")]
    public void ReadRejectsInvalidIdentities(string original, string replacement)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseUnitDeclarationReader.Read(
            Declaration.Replace(original, replacement, StringComparison.Ordinal), Source));
    }

    [TestMethod]
    [DataRow(".")]
    [DataRow("/outside.yml")]
    [DataRow("../outside.yml")]
    [DataRow("src/../unit.yml")]
    [DataRow("src/./unit.yml")]
    [DataRow("src//unit.yml")]
    [DataRow("src/unit.yml/")]
    [DataRow("src\\unit.yml")]
    [DataRow("C:/unit.yml")]
    public void ReadRejectsNoncanonicalSourcePaths(string source)
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => ReleaseUnitDeclarationReader.Read(Declaration, source));
    }

    [TestMethod]
    [DataRow("/outside")]
    [DataRow("../outside")]
    [DataRow("src/../manifest")]
    [DataRow("src/./manifest")]
    [DataRow("src//manifest")]
    [DataRow("src/manifest/")]
    [DataRow("src\\manifest")]
    [DataRow("C:/manifest")]
    public void ReadRejectsNoncanonicalEntryPaths(string entry)
    {
        string content = Declaration.Replace("entry-point: manifest", $"entry-point: '{entry}'",
            StringComparison.Ordinal);

        Assert.ThrowsExactly<InvalidDataException>(
            () => ReleaseUnitDeclarationReader.Read(content, Source));
    }

    [TestMethod]
    [DataRow("builds: [")]
    [DataRow(Declaration + "\nschema: workflow-delivery/v3/release-unit")]
    [DataRow(Declaration + "\n---\n" + Declaration)]
    [DataRow("schema: workflow-delivery/v3/release-unit\nrelease-unit: product\n" +
        "builds: [{id: build, id: second, definition: native/build, entry-point: manifest, " +
        "outputs: [{id: output, role: primary, kind: package}]}]")]
    [DataRow("schema: workflow-delivery/v3/release-unit\nrelease-unit: product\n" +
        "builds: [{id: build, definition: native/build, entry-point: manifest, " +
        "outputs: [{id: output, role: primary, kind: package, kind: archive}]}]")]
    public void ReadPropagatesLibraryFailures(string content)
    {
        Assert.Throws<YamlException>(() => ReleaseUnitDeclarationReader.Read(content, Source));
    }

    private static void AssertBuild(ReleaseBuild actual, string id, string definition,
        string entryPoint, ReleaseOutput[] outputs)
    {
        Assert.AreEqual(id, actual.Id);
        Assert.AreEqual(definition, actual.Definition);
        Assert.AreEqual(entryPoint, actual.EntryPoint);
        CollectionAssert.AreEqual(outputs, actual.Outputs);
    }
}
