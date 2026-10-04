using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;
using YamlDotNet.Core;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class QualitySelectionReaderTests(TestContext context)
{
    private const string Schema = "schema: workflow-delivery/v3/quality-selection\n";

    [TestMethod]
    public async Task ReadKeepsNearestProjectPresetWithoutInspectingOverriddenAncestor()
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await files.WriteAsync(".", "malformed: [");
        await files.WriteAsync("src", Selection("node", "base-node"));
        await files.WriteAsync("src/project", Selection("node", "project-owned-preset"));

        QualitySelection? result = await files.ReadAsync("src/project", "node");

        Assert.AreEqual(new QualitySelection("project-owned-preset",
            "src/project/workflow-delivery.quality.yml"), result);
    }

    [TestMethod]
    public async Task ReadPassesUnrelatedEcosystemWithoutResolvingItsPreset()
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await files.WriteAsync(".", Selection("node", "root-node"));
        await files.WriteAsync("src/project", Selection("python", "unknown-to-node"));

        QualitySelection? result = await files.ReadAsync("src/project", "node");

        Assert.AreEqual(new QualitySelection("root-node", "workflow-delivery.quality.yml"), result);
    }

    [TestMethod]
    public async Task ReadIncludesRootAndStopsBeforeItsParent()
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(files.Parent, "workflow-delivery.quality.yml"),
            "malformed: [", context.CancellationToken);
        Assert.IsNull(await files.ReadAsync(".", "node"));
        await files.WriteAsync(".", Selection("node", "root-node"));

        QualitySelection? result = await files.ReadAsync(".", "node");

        Assert.AreEqual(new QualitySelection("root-node", "workflow-delivery.quality.yml"), result);
    }

    [TestMethod]
    public async Task ReadReturnsAbsenceWhenNoAncestorSelectsRequestedEcosystem()
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await files.WriteAsync(".", Selection("python", "python-custom"));
        files.Directory("src/project");

        Assert.IsNull(await files.ReadAsync("src/project", "node"));
    }

    [TestMethod]
    public async Task ReadUsesLibraryAliasesForScalarPresetValues()
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await files.WriteAsync(".", Schema + "ecosystems:\n" +
            "  python: {preset: &selected shared-preset}\n  node: {preset: *selected}\n");

        QualitySelection? result = await files.ReadAsync(".", "node");

        Assert.AreEqual(new QualitySelection("shared-preset", "workflow-delivery.quality.yml"),
            result);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("~")]
    public async Task ReadPreservesQuotedLiteralIdentifiers(string preset)
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await files.WriteAsync(".", Schema + $"ecosystems: {{node: {{preset: '{preset}'}}}}");

        QualitySelection? result = await files.ReadAsync(".", "node");

        Assert.AreEqual(new QualitySelection(preset, "workflow-delivery.quality.yml"), result);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("[]")]
    [DataRow("ecosystems: {node: {preset: node}}")]
    [DataRow("schema: wrong\necosystems: {node: {preset: node}}")]
    [DataRow(Schema)]
    [DataRow(Schema + "ecosystems: {}")]
    [DataRow(Schema + "ecosystems: []")]
    [DataRow(Schema + "ecosystems: {node: {preset: node}}\nextra: true")]
    [DataRow(Schema + "ecosystems: {node: {}}")]
    [DataRow(Schema + "ecosystems: {node: null}")]
    [DataRow(Schema + "ecosystems: {node: {preset: ''}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: '  '}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: }}")]
    [DataRow(Schema + "ecosystems: {node: {preset: null}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: ~}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: [node]}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: {value: node}}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: node, extra: true}}")]
    [DataRow(Schema + "ecosystems: {python: {}}")]
    [DataRow(Schema + "ecosystems: {python: {preset: null}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: &missing null}, python: {preset: *missing}}")]
    public async Task ReadRejectsMalformedDeclarationWithoutParentFallback(string declaration)
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await files.WriteAsync(".", Selection("node", "root-node"));
        await files.WriteAsync("src/project", declaration);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => files.ReadAsync("src/project", "node"));
    }

    [TestMethod]
    [DataRow("ecosystems: [")]
    [DataRow(Schema + Schema + "ecosystems: {node: {preset: node}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: node}, node: {preset: other}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: node, preset: other}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: !!null ignored}}")]
    [DataRow(Schema + "ecosystems: {node: {preset: node}}\n---\n" + Schema +
        "ecosystems: {node: {preset: other}}")]
    public async Task ReadPropagatesLibrarySyntaxAndDuplicateKeyFailures(string declaration)
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await files.WriteAsync(".", Selection("node", "root-node"));
        await files.WriteAsync("src/project", declaration);

        await Assert.ThrowsAsync<YamlException>(() => files.ReadAsync("src/project", "node"));
    }

    [TestMethod]
    [DataRow("/outside")]
    [DataRow("../outside")]
    [DataRow("src/../outside")]
    [DataRow("src/./project")]
    [DataRow("src//project")]
    [DataRow("src/project/")]
    [DataRow("src\\project")]
    [DataRow("C:/outside")]
    public async Task ReadRejectsNoncanonicalManifestDirectory(string directory)
    {
        using var files = new DeclarationFixture(context.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => files.ReadAsync(directory, "node"));
    }

    [TestMethod]
    public async Task ReadRejectsMissingMaterializedDirectory()
    {
        using var files = new DeclarationFixture(context.CancellationToken);

        await Assert.ThrowsExactlyAsync<DirectoryNotFoundException>(
            () => files.ReadAsync("missing/project", "node"));
    }

    [TestMethod]
    public async Task ReadDoesNotTreatFailedDescriptorReadAsAbsence()
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        await files.WriteAsync(".", Selection("node", "root-node"));
        System.IO.Directory.CreateDirectory(Path.Combine(files.Directory("src/project"),
            "workflow-delivery.quality.yml"));

        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(
            () => files.ReadAsync("src/project", "node"));
    }

    [TestMethod]
    public async Task ReadCancellationCannotReturnSelectionOrAbsence()
    {
        using var files = new DeclarationFixture(context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        await cancellation.CancelAsync();
        var reader = new QualitySelectionReader(files.Root);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.ReadAsync(".", "node", cancellation.Token));
    }

    private static string Selection(string ecosystem, string preset) =>
        Schema + $"ecosystems:\n  {ecosystem}:\n    preset: {preset}\n";

    private sealed class DeclarationFixture : IDisposable
    {
        private readonly CancellationToken token;
        internal string Parent { get; } = Path.Combine(Path.GetTempPath(), "workflow quality " +
            Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(Parent, "revision");

        internal DeclarationFixture(CancellationToken token)
        {
            this.token = token;
            System.IO.Directory.CreateDirectory(Root);
        }

        internal string Directory(string path) =>
            System.IO.Directory.CreateDirectory(Path.Combine(Root, path)).FullName;

        internal Task WriteAsync(string directory, string content) =>
            File.WriteAllTextAsync(
                Path.Combine(Directory(directory), "workflow-delivery.quality.yml"),
                content, token);

        internal Task<QualitySelection?> ReadAsync(string directory, string ecosystem) =>
            new QualitySelectionReader(Root).ReadAsync(directory, ecosystem, token);

        public void Dispose() => System.IO.Directory.Delete(Parent, recursive: true);
    }
}
