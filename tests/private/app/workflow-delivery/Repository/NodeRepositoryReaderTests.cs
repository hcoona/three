using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeRepositoryReaderTests(TestContext context)
{
    private static readonly JsonSerializerOptions NativeJsonOptions = new()
    { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private const string Project = "src/private/app/im-acp-gateway/poc/telegram-bot-verifier";
    private static readonly string[] SetupInputs =
        ["package.json", "pnpm-workspace.yaml", "pnpm-lock.yaml", "mise.toml", "mise.lock"];

    [TestMethod]
    public async Task ReadIgnoresUncommittedUnitAndKeepsUnversionedRoster()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        foreach (string path in SetupInputs)
            await repo.SetAsync(path);
        await repo.SetAsync(".gitignore", "ignored/\n");
        await repo.SetAsync(Project + "/package.json", "{}");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        Directory.CreateDirectory(Path.Combine(repo.Directory, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(repo.Directory,
            "ignored/workflow-delivery.release-unit.yml"), "malformed ignored declaration",
            context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo.Directory,
            Project + "/workflow-delivery.release-unit.yml"), "malformed untracked declaration",
            context.CancellationToken);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);
        var reader = new NodeRepositoryReader(checkout, (arguments, token) =>
            NativeAnswer(repo.Directory, Project, arguments, token));

        NodeRevisionInputs inputs = await reader.ReadAsync(context.CancellationToken);

        Assert.AreEqual(commit, inputs.Revision.Commit);
        Assert.HasCount(2, inputs.Graph.Projects);
        Assert.AreEqual(Project, Assert.ContainsSingle(inputs.Graph.Projects.Where(project =>
            project.Directory != ".")).Directory);
        Assert.IsEmpty(inputs.Versions);
        Assert.IsNull(inputs.Quality[Project]);
        Assert.IsEmpty(inputs.Units);
        foreach (string path in SetupInputs)
            Assert.AreEqual(Project, Assert.ContainsSingle(inputs.OperationInputs.Single(input =>
                input.Path == path).Consumers));
        foreach (string path in new[] { "biome.jsonc", ".gitignore", ".ignore" })
            Assert.AreEqual(Project, Assert.ContainsSingle(inputs.OperationInputs.Single(input =>
                input.Path == path).Consumers));
        Assert.DoesNotContain(".editorconfig", inputs.OperationInputs.Select(input => input.Path));
        Assert.DoesNotContain("LICENSE", inputs.OperationInputs.Select(input => input.Path));
    }

    [TestMethod]
    public async Task ReadSurfacesControlledNativeQueryFailure()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);
        var reader = new NodeRepositoryReader(checkout,
            (_, _) => Task.FromException<string>(
                new TimeoutException("controlled native deadline")));

        TimeoutException error = await Assert.ThrowsExactlyAsync<TimeoutException>(() =>
            reader.ReadAsync(context.CancellationToken));

        Assert.AreEqual("controlled native deadline", error.Message);
    }

    [TestMethod]
    public async Task ReadRejectsUnknownNativeScriptInputScope()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);
        var reader = new NodeRepositoryReader(checkout, (arguments, token) =>
            NativeAnswer(repo.Directory, "src/unknown", arguments, token));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(context.CancellationToken));

        Assert.Contains("Unresolved Node script input scope: src/unknown", error.Message);
    }


    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadRejectsMissingSetupOrAdditionalConfiguration(bool additionalConfiguration)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        foreach (string path in SetupInputs)
            if (additionalConfiguration || path != "mise.lock")
                await repo.SetAsync(path);
        await repo.SetAsync(Project + "/package.json", "{}");
        if (additionalConfiguration)
            await repo.SetAsync(".npmrc", "opaque unsupported configuration");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);
        var reader = new NodeRepositoryReader(checkout, (arguments, token) =>
            NativeAnswer(repo.Directory, Project, arguments, token));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(context.CancellationToken));

        Assert.Contains(additionalConfiguration ? ".npmrc" : "mise.lock", error.Message);
    }

    [TestMethod]
    [DataRow("src/public/lib/hcoona-release-smoke-npm")]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-npm")]
    public async Task ReadRejectsUncommittedNativeVersionConfiguration(string product)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        foreach (string path in SetupInputs)
            await repo.SetAsync(path);
        await repo.SetAsync(product + "/package.json", "{}");
        await repo.SetAsync(product + "/version.json",
            "{\"version\":\"1.2\",\"pathFilters\":[\".\"]}");
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        await File.WriteAllTextAsync(Path.Combine(repo.Directory, product + "/version.txt"), "9.9",
            context.CancellationToken);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);
        var reader = new NodeRepositoryReader(checkout, (arguments, token) =>
            NativeAnswer(repo.Directory, product, arguments, token));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(context.CancellationToken));

        Assert.Contains("Uncommitted native version configuration", error.Message);
        Assert.Contains(product + "/version.txt", error.Message);
    }


    [TestMethod]
    [DataRow("src/public/lib/hcoona-release-smoke-npm")]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-npm")]
    public async Task ReadKeepsCurrentProductLegalInputConsumers(string fixture)
    {
        string[] products =
        [
            fixture,
            "src/public/lib/hexo-renderer-asciidoc",
            "src/public/lib/steam-account-history-to-csv",
        ];
        string[] pocs =
        [
            Project,
            "src/private/app/im-acp-gateway/poc/telegram-topic-session-bridge",
            "src/private/app/im-acp-gateway/poc/wechat-ilink-verifier",
        ];
        string[] projects = [.. products, .. pocs];
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        foreach (string path in SetupInputs)
            await repo.SetAsync(path);
        foreach (string product in products)
        {
            await repo.SetAsync(product + "/package.json", "{}");
            await repo.SetAsync(product + "/version.json",
                "{\"version\":\"1.2\",\"pathFilters\":[\".\"]}");
        }
        foreach (string poc in pocs)
            await repo.SetAsync(poc + "/package.json", "{}");
        await repo.SetAsync(".gitignore", "ignored/\n");
        foreach (string path in new[] { "LICENSE", "COPYING", "COPYING.LESSER",
            "LICENSES/LGPL-3.0-linking-exception.txt", "LICENSES/notice.txt" })
            await repo.SetAsync(path);
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync(commit,
            context.CancellationToken);
        GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);
        var reader = new NodeRepositoryReader(checkout, (arguments, token) =>
            NativeAnswer(repo.Directory, projects, arguments, token));

        NodeRevisionInputs inputs = await reader.ReadAsync(context.CancellationToken);

        Assert.HasCount(3, inputs.Versions);
        AssertConsumers(inputs, "LICENSE", products[0], products[1]);
        AssertConsumers(inputs, "COPYING", products[1], products[2]);
        AssertConsumers(inputs, "COPYING.LESSER", products[1], products[2]);
        AssertConsumers(inputs, "LICENSES/LGPL-3.0-linking-exception.txt",
            products[1], products[2]);
        AssertConsumers(inputs, "LICENSES/notice.txt", products[2]);
        AssertConsumers(inputs, "biome.jsonc", products[1], pocs[0], pocs[1], pocs[2]);
        AssertConsumers(inputs, ".gitignore", products[1], pocs[0], pocs[1], pocs[2]);
        AssertConsumers(inputs, ".ignore", products[1], pocs[0], pocs[1], pocs[2]);
        AssertConsumers(inputs, ".editorconfig", products[1]);
        Assert.DoesNotContain(".ignore", inputs.Revision.Entries.Select(entry => entry.Path));
    }

    private static void AssertConsumers(NodeRevisionInputs inputs, string path,
        params string[] consumers) => CollectionAssert.AreEquivalent(consumers,
            inputs.OperationInputs.Single(input => input.Path == path).Consumers);

    private static Task<string> NativeAnswer(string root, string project, string[] arguments,
        CancellationToken token) => NativeAnswer(root, [project], arguments, token);

    private static Task<string> NativeAnswer(string root, string[] projects, string[] arguments,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (arguments[0] == "config")
            return Task.FromResult(arguments[2] == "shared-workspace-lockfile" ? "true" : "null");
        if (arguments[0] == "--dir")
            return Task.FromResult("");
        string[] directories = [".", .. projects];
        int filter = Array.IndexOf(arguments, "--filter");
        if (filter >= 0)
        {
            string selector = arguments[filter + 1];
            directories = [selector.EndsWith("...", StringComparison.Ordinal)
                ? selector[3..^4] : selector[3..^1]];
        }
        return Task.FromResult(JsonSerializer.Serialize(directories.Select(directory => new
        {
            path = Path.GetFullPath(directory, root),
            name = directory.EndsWith("/hcoona-release-smoke-npm", StringComparison.Ordinal)
                ? NodeRepositoryReader.FixtureName : null,
            dependencies = new Dictionary<string, object>(),
        }), NativeJsonOptions));
    }
}
