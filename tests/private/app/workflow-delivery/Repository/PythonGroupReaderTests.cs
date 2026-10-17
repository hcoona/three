using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonGroupReaderTests(TestContext context)
{
    private const string Source = "src/input/source.py";
    private const string Other = "src/other/code.cs";
    private const string ControlCaller =
        "src/private/app/workflow-delivery/CI/Callers/ControlCiCaller.cs";
    private const string Passive = "src/private/app/workflow-delivery/Native/Python/passive.py";
    private const string PassiveTarget = "tests/private/app/workflow-delivery/Native/Python";
    private const string Unresolved = "unresolved.txt";
    private static readonly string[] ChangedPaths = [Source, Other, ControlCaller, Passive,
        "src/private/app/workflow-delivery/README.md",
        "src/private/app/workflow-delivery/docs/high-level-design.md", Unresolved];

    // Every row reads class-owned committed endpoints: one basis and a sibling candidate
    // worktree per changed path. Each row writes its scope transfer to private scratch; a
    // row that must edit or fetch into an endpoint needs its own repository.
    private static GitFixture? shared;
    private static readonly Dictionary<string, Fixture.Pair> SharedPairs =
        new(StringComparer.Ordinal);
    private static Fixture.Pair SharedPair => SharedPairs[Source];

    // Full mode reads the candidate commit and root for both endpoints.
    private static Fixture.Pair SharedFull => SharedPair with
    { Full = true, Basis = SharedPair.Candidate, BasisRoot = SharedPair.CandidateRoot };

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        shared = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            foreach (Fixture.Pair pair in await Fixture.CommitSharedAsync(shared, ChangedPaths,
                [Fixture.DefaultTarget, PassiveTarget]))
                SharedPairs.Add(pair.Changed, pair);
        }
        catch { shared.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task SharedEndpointsQualifyPairedSelection()
    {
        // Defect-free control: the shared pair itself selects the changed pytest target.
        using var fixture = await Fixture.SharedAsync(SharedPair, context.CancellationToken);
        PythonGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        PlannedCheck check = Assert.ContainsSingle(result.Plan.Checks);
        Assert.Contains(new SelectionReason(Source, fixture.Request.Basis.Checkout.Reference,
            check.Work.Key.Target), check.Reasons);
        Assert.HasCount(2, fixture.Collected);
        AssertCandidateContext(fixture, result, check);
    }

    [TestMethod]
    public async Task FullModeRetainsCandidateContext()
    {
        using var fixture = await Fixture.SharedAsync(SharedFull, context.CancellationToken);
        PythonGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        AssertCandidateContext(fixture, result, Assert.ContainsSingle(result.Plan.Checks));
    }

    private static void AssertCandidateContext(Fixture fixture, PythonGroupReadback result,
        PlannedCheck check)
    {
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
    }

    [TestMethod]
    public async Task TransferredPreparationExcludesNewReceiverEnvironmentAtQueryLaunch()
    {
        using var fixture = await Fixture.SharedAsync(SharedFull, context.CancellationToken);
        string state = Path.GetDirectoryName(fixture.Request.ScopePath)!;
        string sources = Directory.CreateDirectory(Path.Combine(state, "control")).FullName;
        string scratch = Directory.CreateDirectory(Path.Combine(state, "preparation")).FullName;
        string Tool(string name) => Path.Combine(state, "tools", name);
        var preparation = new PythonGroupPreparationRequest(fixture.Request.Basis.Checkout,
            fixture.Request.Candidate.Checkout, true, fixture.Request.ScopePath, sources, scratch,
            new(Tool("uv"), Tool("python"), Tool("dotnet")),
            fixture.Request.Candidate.Collection.Operation, 600);
        string sentinel = "WORKFLOW_RECEIVER_SECRET_" + Guid.NewGuid().ToString("N");
        Assert.IsNull(Environment.GetEnvironmentVariable(sentinel));
        PythonGroupPreparationResult prepared = await PythonGroupPreparation.PrepareAsync(
            preparation, (command, _) =>
            {
                string? created = command.Executable == preparation.Tools.Uv
                        ? Path.Combine(command.Environment!["UV_PROJECT_ENVIRONMENT"]!,
                            OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python")
                        : null;
                if (created is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(created)!);
                    File.WriteAllText(created, "Controlled preparation output; never executed.");
                }
                return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0,
                    "", "", 0, null));
            }, context.CancellationToken);
        PythonGroupRequest transferred = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(prepared.Request, TransferJson.Default.PythonGroupRequest),
            TransferJson.Default.PythonGroupRequest)!;
        Assert.IsFalse(transferred.Basis.Environment.ContainsKey(sentinel));
        Environment.SetEnvironmentVariable(sentinel, "receiver-only-secret");
        bool launched = false;
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                PythonGroupReader.ReadAsync(transferred, (command, _) =>
                {
                    // NativeProcessTests proves InheritEnvironment=false clears the child
                    // block; this row qualifies only the command boundary the reader builds.
                    Assert.IsFalse(command.InheritEnvironment);
                    Assert.IsNotNull(command.Environment);
                    Assert.IsFalse(command.Environment.ContainsKey(sentinel));
                    Assert.AreEqual(transferred.Basis.Environment["HOME"],
                        command.Environment["HOME"]);
                    launched = true;
                    // Stop before metadata parsing; this test qualifies the process boundary only.
                    return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 1,
                        "", "Controlled query stop", 0, null));
                }, context.CancellationToken));
            Assert.IsTrue(launched);
            Assert.AreEqual("receiver-only-secret", Environment.GetEnvironmentVariable(sentinel));
        }
        finally { Environment.SetEnvironmentVariable(sentinel, null); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KnownOtherResponsibilityCannotSuppressNativePythonConsumption(bool consumed)
    {
        using var fixture = await Fixture.SharedAsync(SharedPairs[Other],
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
    public async Task ControlCallerUsesNativeManagedResponsibility()
    {
        using var fixture = await Fixture.SharedAsync(SharedPairs[ControlCaller],
            context.CancellationToken, "dotnet");
        PythonGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        Assert.IsEmpty(result.Plan.Checks);
        Assert.HasCount(2, fixture.Collected);
    }

    [TestMethod]
    public async Task NativeManagedResponsibilityPreservesPythonHelperConsumer()
    {
        const string path = Passive;
        const string target = PassiveTarget;
        using var fixture = await Fixture.SharedAsync(SharedPairs[path],
            context.CancellationToken, "dotnet", target);
        PythonGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        PlannedCheck check = Assert.ContainsSingle(result.Plan.Checks);
        Assert.AreEqual("pytest:" + target, check.Work.Key.Target);
        Assert.IsTrue(check.Work.Required);
        CollectionAssert.AreEquivalent(new[] {
            new SelectionReason(path, fixture.Request.Basis.Checkout.Reference,
                "pytest:" + target),
            new SelectionReason(path, fixture.Request.Candidate.Checkout.Reference,
                "pytest:" + target) }, check.Reasons);
    }

    [TestMethod]
    [DataRow("src/private/app/workflow-delivery/README.md")]
    [DataRow("src/private/app/workflow-delivery/docs/high-level-design.md")]
    public async Task DeclaredControlDocumentsRemainNonconsumersWithPythonCallerResponsibility(
        string path)
    {
        using var fixture = await Fixture.SharedAsync(SharedPairs[path],
            context.CancellationToken, "python");
        PythonGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        Assert.IsEmpty(result.Plan.Checks);
        Assert.HasCount(2, fixture.Collected);
        Assert.AreEqual(fixture.Request.Candidate.Checkout.Reference, result.Plan.Candidate);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("python")]
    public async Task UnknownOrLegacyPythonHeuristicCannotBecomeSuccessfulNoWork(string? owner)
    {
        using var fixture = await Fixture.SharedAsync(SharedPairs[Unresolved],
            context.CancellationToken, owner);
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.AreEqual("Unresolved Python input: unresolved.txt", error.Message);
    }

    [TestMethod]
    [DataRow("dotnet")]
    [DataRow("record-system")]
    public async Task SharedUnresolvedPairQualifiesWithKnownOtherResponsibility(string owner)
    {
        // Defect-free control: the shared unresolved-path pair itself reads successfully.
        using var fixture = await Fixture.SharedAsync(SharedPairs[Unresolved],
            context.CancellationToken, owner);
        PythonGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        Assert.IsEmpty(result.Plan.Checks);
        Assert.HasCount(2, fixture.Collected);
    }

    [TestMethod]
    [DataRow("comparison")]
    [DataRow("omitted-change")]
    [DataRow("full-mode")]
    [DataRow("entry-mode")]
    public async Task BadScopeTransferStopsBeforeNativeCollection(string defect)
    {
        using var fixture = await Fixture.SharedAsync(SharedPair, context.CancellationToken);
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
    [DataRow("passive")]
    [DataRow("helper")]
    [DataRow("operation")]
    public async Task EndpointsCannotSubstituteAnotherControlRuntime(string defect)
    {
        using var fixture = await Fixture.SharedAsync(SharedPair, context.CancellationToken);
        PythonRepositoryRequest candidate = fixture.Request.Candidate.Collection;
        candidate = defect switch
        {
            "interpreter" => candidate with
            { Graph = candidate.Graph with { Interpreter = "/other/python" } },
            "passive" => candidate with
            { Graph = candidate.Graph with { PassiveScript = "/other/passive.py" } },
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
        using var fixture = await Fixture.SharedAsync(SharedPair, context.CancellationToken);
        fixture.FailedEndpoint = candidate ? fixture.Request.Candidate.Checkout.Reference :
            fixture.Request.Basis.Checkout.Reference;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsNull(fixture.After);
    }

    [TestMethod]
    public async Task CancelledBasisCollectionDoesNotQueryCandidateOrReturnNoWork()
    {
        using var fixture = await Fixture.SharedAsync(SharedPair, context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.AfterBasis = cancellation.Cancel;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.ReadAsync(cancellation.Token));
        Assert.IsNull(fixture.After);
        Assert.AreEqual(fixture.Request.Basis.Checkout.Reference,
            Assert.ContainsSingle(fixture.Collected));
    }

    private sealed class Fixture(string state) : IDisposable
    {
        internal const string DefaultTarget = "src/input/tests";
        internal PythonGroupRequest Request { get; set; } = null!;
        internal PythonRevisionInputs? After { get; private set; }
        internal List<string> Collected { get; } = [];
        internal bool CrossInput { get; set; }
        private string testTarget = DefaultTarget;
        internal string? FailedEndpoint { get; set; }
        internal Action? AfterBasis { get; set; }

        internal sealed record Pair(string Changed, bool Full, string Basis, string BasisRoot,
            string Candidate, string CandidateRoot);

        // One basis worktree; each changed path gets a sibling candidate in its own worktree.
        internal static async Task<Pair[]> CommitSharedAsync(GitFixture repo, string[] changed,
            string[] targets)
        {
            foreach (string path in ((string[])["pyproject.toml", "src/input/pyproject.toml",
                "src/input/source.py"]).Concat(targets.Select(target => target + "/test_one.py"))
                .Concat(changed).Distinct(StringComparer.Ordinal))
                await repo.SetAsync(path);
            string basis = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", basis);
            string parent = Path.GetDirectoryName(repo.Directory)!;
            string basisRoot = Path.Combine(parent, "basis");
            await repo.GitAsync("worktree", "add", "--detach", basisRoot, basis);
            var pairs = new List<Pair>();
            foreach (string path in changed)
            {
                await repo.SetAsync(path, "updated");
                string candidate = await repo.CommitAsync(basis);
                string root = Path.Combine(parent, "candidate-" + pairs.Count);
                await repo.GitAsync("worktree", "add", "--detach", root, candidate);
                pairs.Add(new(path, false, basis, basisRoot, candidate, root));
                await repo.SetAsync(path);
            }
            return [.. pairs];
        }

        // Reads class-owned endpoints; the row owns only its scope transfer.
        internal static async Task<Fixture> SharedAsync(Pair pair, CancellationToken token,
            string? owner = null, string target = DefaultTarget)
        {
            var fixture = new Fixture(Directory.CreateTempSubdirectory(
                "workflow-python-group-").FullName) { testTarget = target };
            try
            {
                await fixture.RequestAsync(pair, owner, token);
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        private async Task RequestAsync(Pair pair, string? owner, CancellationToken token)
        {
            string changed = pair.Changed;
            bool full = pair.Full;
            string scopePath = Path.Combine(state, "scope.json");
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
                @base = pair.Basis, candidate = pair.Candidate, full,
                changed_paths = full ? Array.Empty<string>() : new[] { changed },
                endpoint_owners = new
                { basis = Endpoint(pair.Basis), candidate = Endpoint(pair.Candidate) }
            }), token);
            Request = new(EndpointRequest(pair.BasisRoot, pair.Basis),
                EndpointRequest(pair.CandidateRoot, pair.Candidate), full, scopePath);
        }

        private static PythonGroupEndpoint EndpointRequest(string directory, string commit) =>
            new(new(directory, commit), new(new(directory, "uv", "/runtime/python",
                Path.Combine(directory, ".query-tools/python"), "/control/passive.py"),
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
                    [testTarget], ["--import-mode=importlib"]);
                var metadata = new PythonMetadata(checkout.Root, "workspace", ">=3.14",
                    projects.Select(project => new PythonMetadataMember(project.Name,
                        project.Directory, project.Name)).ToArray(),
                    new Dictionary<string, PythonMetadataNode>());
                PythonPytestTargetSet targets = PythonPytestTargets.Bind(configuration,
                    checkout.Revision, projects);
                PythonInputBinding[] inputs = [.. PythonRepositoryInputs.Map(checkout.Revision,
                    projects, targets, []), .. CrossInput
                        ? new[] { new PythonInputBinding("src/other/code.cs", [],
                            ["pytest:src/input/tests"]) } : []];
                var result = new PythonRevisionInputs(new(checkout.Revision, projects,
                    configuration, endpoint.Collection.Operation, [], inputs),
                    new(metadata, endpoint.Collection.Graph.Interpreter, [], [], projects, []),
                    targets);
                if (ReferenceEquals(endpoint, Request.Candidate)) After = result;
                else AfterBasis?.Invoke();
                return Task.FromResult(result);
            }, token);

        public void Dispose() => Directory.Delete(state, recursive: true);
    }
}
