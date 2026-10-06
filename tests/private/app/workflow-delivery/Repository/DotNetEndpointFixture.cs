using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

internal sealed class DotNetEndpointFixture : IDisposable
{
    internal GitFixture Repo { get; }
    internal string Root => Repo.Directory;
    internal string Scratch { get; } = Path.Combine(Path.GetTempPath(),
        "workflow-dotnet-reader-" + Guid.NewGuid().ToString("N"));
    internal GitRevision Revision { get; private set; } = null!;
    internal GitMaterialization Checkout { get; private set; } = null!;
    internal DotNetGraphResponse Graph { get; set; } = null!;
    private readonly CancellationToken token;

    private DotNetEndpointFixture(GitFixture repo, CancellationToken token)
    {
        Repo = repo;
        this.token = token;
        Directory.CreateDirectory(Scratch);
    }

    internal static async Task<DotNetEndpointFixture> CreateAsync(CancellationToken token)
    {
        var fixture = new DotNetEndpointFixture(await GitFixture.CreateAsync(token), token);
        try
        {
            foreach (string path in new[] { "dirs.proj", "product/A.csproj", "product/B.csproj",
                         "product/code.cs", "linked/data.txt", "product/fixtures/example.txt" })
                await fixture.Repo.SetAsync(path);
            await fixture.Repo.SetAsync("version.json", """
                {"version":"1.0","pathFilters":["./product/A.csproj","./product/B.csproj",
                  "./product/code.cs","./linked/source.cs"]}
                """);
            await fixture.CommitAsync();
            fixture.Graph = fixture.Response();
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    internal async Task CommitAsync()
    {
        string commit = Revision is null ? await Repo.CommitAsync() :
            await Repo.CommitAsync(Revision.Commit);
        await Repo.GitAsync("reset", "--hard", commit);
        Revision = await new GitReader(Root).ReadAsync(commit, token);
        Checkout = await GitMaterialization.BindAsync(Root, Revision, token);
    }

    internal string PathOf(string path) => Path.Combine(Root, path);

    internal DotNetNodeIdentity Identity(string project, string? framework = null)
    {
        var globals = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Configuration"] = "Debug",
            ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true",
        };
        if (framework is not null) globals.Add("TargetFramework", framework);
        return new(PathOf(project), globals);
    }

    internal DotNetGraphResponse Response(bool includeB = true)
    {
        DotNetNodeIdentity traversal = Identity("dirs.proj");
        DotNetNodeIdentity outer = Identity("product/A.csproj");
        DotNetNodeIdentity first = Identity("product/A.csproj", "net10.0");
        DotNetNodeIdentity second = Identity("product/A.csproj", "net9.0");
        DotNetNodeIdentity dependency = Identity("product/B.csproj", "net10.0");
        var nodes = new List<DotNetGraphNode>
        {
            Node(traversal, false, []), Node(outer, true, [PathOf("product/A.csproj")]),
            Node(first, false, [PathOf("product/A.csproj"), PathOf("product/code.cs")]),
            Node(second, false, [PathOf("product/A.csproj")]),
        };
        var edges = new List<DotNetGraphEdge>
        {
            new(traversal, outer), new(outer, first), new(outer, second),
        };
        if (includeB)
        {
            nodes.Add(Node(dependency, false, [PathOf("product/B.csproj")]));
            edges.Add(new(first, dependency));
            edges.Add(new(second, dependency));
        }
        DotNetGraphInput[] inputs = nodes.Select(node => new DotNetGraphInput(
            node.Identity.Project, "Project", "Evaluation", node.Identity)).Concat(
            [new DotNetGraphInput(PathOf("linked/data.txt"), "EmbeddedResource",
                "Evaluation", first)])
            .ToArray();
        return new(Revision.Commit, Root, PathOf("dirs.proj"), nodes.ToArray(),
            edges.ToArray(), inputs);

        DotNetGraphNode Node(DotNetNodeIdentity identity, bool outerBuild, string[] owned) =>
            new(identity, outerBuild, new("Debug", identity.Globals.GetValueOrDefault(
                "TargetFramework", ""), ""), outerBuild ? "net10.0;net9.0" : "", "", owned, "None");
    }

    internal DotNetRepositoryReader Reader()
    {
        string prefix = Guid.NewGuid().ToString("N");
        return new(Checkout, new(typeof(DotNetGraphTask).Assembly.Location,
            Path.Combine(Scratch, prefix + ".proj"),
            Path.Combine(Scratch, prefix + ".request.json"),
            Path.Combine(Scratch, prefix + ".response.json"), 30));
    }

    internal Task<DotNetRevisionInputs> BindAsync() => Reader().BindAsync(Graph, token);

    public void Dispose()
    {
        Repo.Dispose();
        Directory.Delete(Scratch, true);
    }
}
