using System.Text.Json;
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
            channels: [buddy, official-dry-run]
            builds:
              - id: z-build
                definition: custom/not-registered-here
                entry-point: nested/package.json
                qualification: {probe: 'runProbe()', test-before-build: true}
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
        string[] channels = ["buddy", "official-dry-run"];
        CollectionAssert.AreEqual(channels, result.Channels);
        Assert.HasCount(2, result.Builds);
        AssertBuild(result.Builds[0], "z-build", "custom/not-registered-here",
            "nested/package.json",
            [new("z-output", "primary-package", "custom-binary"),
             new("a-output", "source-package", "source-archive")]);
        InventoryQualification inventory = Assert.IsInstanceOfType<InventoryQualification>(
            result.Builds[0].Qualification);
        Assert.AreEqual("runProbe()", Assert.ContainsSingle(inventory.Fields["probe"]));
        Assert.AreEqual("true", Assert.ContainsSingle(inventory.Fields["test-before-build"]));
        AssertBuild(result.Builds[1], "a-build", "another/native-definition", ".",
            [new("second-output", "primary-package", "other-package")]);
    }

    [TestMethod]
    public void ConcreteNpmQualificationSurvivesTransferWithoutOtherRecipeOptions()
    {
        string declaration = Declaration.Replace("native/build-v1",
            "node/npm-package-v1, qualification: {probe: 'runProbe()', test-before-build: true}",
            StringComparison.Ordinal);
        ReleaseUnitDeclaration parsed = ReleaseUnitDeclarationReader.Read(declaration, Source);
        string transfer = JsonSerializer.Serialize(
            parsed, TransferJson.Default.ReleaseUnitDeclaration);
        ReleaseUnitDeclaration? received = JsonSerializer.Deserialize(transfer,
            TransferJson.Default.ReleaseUnitDeclaration);

        Assert.IsNotNull(received);
        Assert.AreEqual(new NpmQualification("runProbe()", true), received.Builds[0].Qualification);
        Assert.DoesNotContain("staticVersion", transfer);
    }

    [TestMethod]
    [DataRow("node/npm-package-v1", "probe: run, static-version: true")]
    [DataRow("ruby/gem-v1", "require: no-probe")]
    [DataRow("python/distribution-set-v1", "rspec: true")]
    [DataRow("dotnet/nuget-package-v1", "manifest-name: extension")]
    public void ConcreteRecipeRejectsInapplicableOrMissingQualification(string definition,
        string fields)
    {
        string declaration = Declaration.Replace("native/build-v1",
            $"{definition}, qualification: {{{fields}}}", StringComparison.Ordinal);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseUnitDeclarationReader.Read(declaration, Source));
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
    [DataRow("workflow-delivery/v3/release-unit", "wrong")]
    [DataRow("release-unit: product\n", "")]
    [DataRow("release-unit: product", "release-unit: product\nextra: value")]
    [DataRow("definition: native/build-v1", "definition: '  '")]
    [DataRow("definition: native/build-v1", "definition: [native/build-v1]")]
    [DataRow("entry-point: manifest, ", "")]
    [DataRow("entry-point: manifest", "entry-point: null")]
    [DataRow("id: build", "id: build, extra: value")]
    [DataRow("role: primary, ", "")]
    [DataRow("kind: package", "kind: package, extra: value")]
    public void ReadRejectsMalformedApplicationSchema(string original, string replacement)
    {
        string content = Declaration.Replace(original, replacement, StringComparison.Ordinal);

        Assert.ThrowsExactly<InvalidDataException>(
            () => ReleaseUnitDeclarationReader.Read(content, Source));
    }

    [TestMethod]
    [DataRow("builds: [", "null")]
    [DataRow("builds: [", "{}")]
    [DataRow("builds: [", "[]")]
    [DataRow("builds: [", "[null]")]
    [DataRow("outputs: [", "[]")]
    [DataRow("outputs: [", "[[]]")]
    public void ReadRejectsInvalidSequence(string field, string sequence)
    {
        string prefix = Declaration[..Declaration.IndexOf(field, StringComparison.Ordinal)];
        string content = prefix + field[..^1] + sequence + (field == "outputs: [" ? "}]" : "");

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
    public void ReadRejectsInvalidIdentities(string original, string replacement)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseUnitDeclarationReader.Read(
            Declaration.Replace(original, replacement, StringComparison.Ordinal), Source));
    }

    [TestMethod]
    [DataRow(".")]
    [DataRow("../outside.yml")]
    public void ReadRejectsNoncanonicalSourcePaths(string source)
    {
        Assert.ThrowsExactly<InvalidDataException>(
            () => ReleaseUnitDeclarationReader.Read(Declaration, source));
    }

    [TestMethod]
    [DataRow("/outside")]
    [DataRow("src/../manifest")]
    [DataRow("src//manifest")]
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
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseUnitDeclarationReader.Read(content, Source));

        Assert.IsInstanceOfType<YamlException>(error.InnerException);
        Assert.Contains(Source, error.Message);
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
