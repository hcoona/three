using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonGroupReaderTests(TestContext context)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExactPairedSelectionRetainsCandidateContextAndFullMode(bool full)
    {
        using var fixture = await Fixture.CreateAsync("src/input/source.py", full,
            context.CancellationToken);
        PythonGroupReadback result = await fixture.ReadAsync(context.CancellationToken);

        PlannedCheck check = Assert.ContainsSingle(result.Plan.Checks);
        Assert.AreEqual(fixture.Request.Candidate.Checkout.Reference, result.Plan.Candidate);
        Assert.AreEqual(fixture.Request.Basis.Checkout.Reference, result.Plan.Comparison);
        Assert.AreEqual("pytest:src/input/tests", check.Work.Key.Target);
        Assert.AreEqual(fixture.Request.Candidate.Collection.Graph.Interpreter,
            result.Interpreter);
        Assert.AreEqual("input", result.Members.Single(member =>
            member.Directory == "src/input").Name);
        Assert.AreEqual(result.Plan.Candidate, result.Targets.Revision);
        Assert.AreEqual("src/input", Assert.ContainsSingle(result.Targets.Targets).Member);
        Assert.AreSame(fixture.After!.Facts.Configuration, result.Configuration);
        Assert.AreSame(fixture.After.Facts.Operation, result.Operation);
        if (!full)
            Assert.Contains(new SelectionReason("src/input/source.py",
                fixture.Request.Basis.Checkout.Reference, check.Work.Key.Target), check.Reasons);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KnownOtherResponsibilityCannotSuppressNativePythonConsumption(bool consumed)
    {
        using var fixture = await Fixture.CreateAsync("src/other/code.cs", false,
            context.CancellationToken, "dotnet");
        fixture.CrossInput = consumed;
        PythonGroupReadback result = await fixture.ReadAsync(context.CancellationToken);

        Assert.AreEqual(consumed ? 1 : 0, result.Plan.Checks.Length);
        if (consumed)
        {
            PlannedCheck check = Assert.ContainsSingle(result.Plan.Checks);
            Assert.HasCount(2, check.Reasons);
            foreach (SelectionReason reason in check.Reasons)
                Assert.AreEqual("src/other/code.cs", reason.Path);
        }
        else
        {
            Assert.IsEmpty(result.Plan.Checks);
            Assert.AreEqual(PythonFactsAssembler.Scope, result.Plan.Scope);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("python")]
    public async Task UnknownOrLegacyPythonHeuristicCannotBecomeSuccessfulNoWork(string? owner)
    {
        using var fixture = await Fixture.CreateAsync("unresolved.txt", false,
            context.CancellationToken, owner);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
    }

    [TestMethod]
    [DataRow("comparison")]
    [DataRow("omitted-change")]
    [DataRow("full-mode")]
    [DataRow("entry-mode")]
    public async Task BadScopeTransferStopsBeforeNativeCollection(string defect)
    {
        using var fixture = await Fixture.CreateAsync("src/input/source.py", false,
            context.CancellationToken);
        var text = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(
            fixture.Request.ScopePath, context.CancellationToken))!;
        if (defect == "comparison") text["candidate"] = new string('a', 40);
        else if (defect == "omitted-change") text["changed_paths"] = new
            System.Text.Json.Nodes.JsonArray();
        else if (defect == "full-mode") text["full"] = true;
        else text["endpoint_owners"]!["candidate"]!["paths"]![0]!["mode"] = "120000";
        await File.WriteAllTextAsync(fixture.Request.ScopePath, text.ToJsonString(),
            context.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Collected);
    }

    [TestMethod]
    [DataRow("interpreter")]
    [DataRow("supplement")]
    [DataRow("helper")]
    [DataRow("operation")]
    public async Task EndpointsCannotSubstituteAnotherControlRuntime(string defect)
    {
        using var fixture = await Fixture.CreateAsync("src/input/source.py", false,
            context.CancellationToken);
        PythonRepositoryRequest candidate = fixture.Request.Candidate.Collection;
        candidate = defect switch
        {
            "interpreter" => candidate with
            { Graph = candidate.Graph with { Interpreter = "/other/python" } },
            "supplement" => candidate with
            { Graph = candidate.Graph with { SupplementExecutable = "/other/supplement" } },
            "helper" => candidate with { PackageHelper = "/other/packages.py" },
            _ => candidate with { Operation = candidate.Operation with { Variant = "other" } }
        };
        fixture.Request = fixture.Request with
        { Candidate = fixture.Request.Candidate with { Collection = candidate } };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Collected);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedEndpointDoesNotUseOtherEndpointFactsOrFullFallback(bool candidate)
    {
        using var fixture = await Fixture.CreateAsync("src/input/source.py", false,
            context.CancellationToken);
        fixture.FailedEndpoint = candidate ? fixture.Request.Candidate.Checkout.Reference :
            fixture.Request.Basis.Checkout.Reference;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsNull(fixture.After);
    }

    [TestMethod]
    public async Task CancelledBasisCollectionDoesNotQueryCandidateOrReturnNoWork()
    {
        using var fixture = await Fixture.CreateAsync("src/input/source.py", false,
            context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.AfterBasis = cancellation.Cancel;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.ReadAsync(cancellation.Token));
        Assert.IsNull(fixture.After);
        Assert.AreEqual(fixture.Request.Basis.Checkout.Reference,
            Assert.ContainsSingle(fixture.Collected));
    }

    private sealed class Fixture(GitFixture repository) : IDisposable
    {
        private readonly GitFixture repo = repository;
        internal PythonGroupRequest Request { get; set; } = null!;
        internal PythonRevisionInputs? After { get; private set; }
        internal List<string> Collected { get; } = [];
        internal bool CrossInput { get; set; }
        internal string? FailedEndpoint { get; set; }
        internal Action? AfterBasis { get; set; }

        internal static async Task<Fixture> CreateAsync(string changed, bool full,
            CancellationToken token, string? owner = null)
        {
            var fixture = new Fixture(await GitFixture.CreateAsync(token));
            try
            {
                foreach (string path in (string[])["pyproject.toml", "src/input/pyproject.toml",
                    "src/input/source.py", "src/input/tests/test_one.py", changed])
                    await repositorySet(path);
                string before = await fixture.repo.CommitAsync();
                await fixture.repo.GitAsync("reset", "--hard", before);
                string parent = Path.GetDirectoryName(fixture.repo.Directory)!;
                string beforeDirectory = Path.Combine(parent, "basis");
                await fixture.repo.GitAsync("worktree", "add", "--detach", beforeDirectory,
                    before);
                await fixture.repo.SetAsync(changed, "updated");
                string after = await fixture.repo.CommitAsync(before);
                await fixture.repo.GitAsync("reset", "--hard", after);
                if (full) { before = after; beforeDirectory = fixture.repo.Directory; }
                string scopePath = Path.Combine(parent, "scope.json");
                object Endpoint(string revision) => new
                {
                    revision,
                    paths = full ? Array.Empty<object>() : new object[] { new
                    {
                        path = changed, present = true, mode = "100644",
                        reasons = owner is null ? Array.Empty<object>() : new object[] { new
                        { owner, target = changed, rule = "controlled-responsibility",
                            sources = new[] { "pyproject.toml" } } }
                    } }
                };
                await File.WriteAllTextAsync(scopePath, JsonSerializer.Serialize(new
                {
                    @base = before, candidate = after, full,
                    changed_paths = full ? Array.Empty<string>() : new[] { changed },
                    endpoint_owners = new { basis = Endpoint(before), candidate = Endpoint(after) }
                }), token);
                fixture.Request = new(EndpointRequest(beforeDirectory, before),
                    EndpointRequest(fixture.repo.Directory, after), full, scopePath);
                return fixture;

                Task repositorySet(string path) => fixture.repo.SetAsync(path);
            }
            catch { fixture.Dispose(); throw; }
        }

        private static PythonGroupEndpoint EndpointRequest(string directory, string commit) =>
            new(new(directory, commit), new(new(directory, "uv", "/runtime/python",
                Path.Combine(directory, ".query-tools/python"), "/control/passive.py",
                "/control/supplement", "/query/cache", "/query/bootstrap", "/query/activity"),
                "/control/pytest_configuration.py", "/control/packages.py",
                new("ubuntu-latest", "python-3.14", new()
                { ["python"] = "3.14", ["platform"] = "linux" })),
                new Dictionary<string, string?>());

        internal Task<PythonGroupReadback> ReadAsync(CancellationToken token) =>
            PythonGroupReader.ReadAsync(Request, (checkout, endpoint, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (checkout.Revision.Commit == FailedEndpoint)
                    throw new InvalidDataException("Required native endpoint failed.");
                Collected.Add(checkout.Revision.Commit);
                PythonProjectDependencies[] projects = [new(".", "root", [], []),
                    new("src/input", "input", [], [])];
                var configuration = new PythonPytestConfiguration("pyproject.toml",
                    ["src/input/tests"], ["--import-mode=importlib"]);
                PythonInputBinding[] inputs = CrossInput ?
                    [new("src/other/code.cs", [], ["pytest:src/input/tests"])] : [];
                var metadata = new PythonMetadata(checkout.Root, "workspace", ">=3.14",
                    projects.Select(project => new PythonMetadataMember(project.Name,
                        project.Directory, project.Name)).ToArray(),
                    new Dictionary<string, PythonMetadataNode>());
                PythonPytestTargetSet targets = PythonPytestTargets.Bind(configuration,
                    checkout.Revision, projects);
                var result = new PythonRevisionInputs(new(checkout.Revision, projects,
                    configuration, endpoint.Collection.Operation, [], inputs),
                    new(metadata, endpoint.Collection.Graph.Interpreter, [], [], projects, []),
                    targets);
                if (ReferenceEquals(endpoint, Request.Candidate)) After = result;
                else AfterBasis?.Invoke();
                return Task.FromResult(result);
            }, token);

        public void Dispose() => repo.Dispose();
    }
}
