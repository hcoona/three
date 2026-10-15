using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyGroupReaderTests(TestContext context)
{
    private const string Spec = "src/input/spec/one_spec.rb";

    // Rows that vary only collection answers or the changed caller input read class-owned
    // committed endpoints. Each caller input has its own candidate worktree over one shared
    // basis, and every row writes its scope and native requests to private scratch.
    private static GitFixture? shared;
    private static Fixture.Pair? sharedSpec;
    private static readonly Dictionary<string, Fixture.Pair> SharedCallers =
        new(StringComparer.Ordinal);

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        shared = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            (sharedSpec, Fixture.Pair[] callers) = await Fixture.CommitSharedAsync(shared,
                RubyRepositoryReader.CallerSources);
            foreach (Fixture.Pair pair in callers) SharedCallers.Add(pair.Changed, pair);
        }
        catch { shared.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task SharedEndpointsQualifyPairedSelection()
    {
        // Defect-free control: the shared pair itself selects the changed Ruby check.
        using var fixture = await Fixture.SharedAsync(sharedSpec!, false,
            context.CancellationToken);
        RubyGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        PlannedCheck check = Assert.ContainsSingle(result.Plan.Checks);
        Assert.Contains(new SelectionReason(Spec, result.Plan.Comparison, "ruby:src/input"),
            check.Reasons);
        CollectionAssert.AreEqual(new[] { fixture.Request.Basis.Checkout.Reference,
            fixture.Request.Candidate.Checkout.Reference }, fixture.Collected);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExactPairedSelectionRetainsCandidateContext(bool full)
    {
        using var fixture = await Fixture.CreateAsync("src/input/spec/one_spec.rb", full,
            context.CancellationToken);
        RubyGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        PlannedCheck check = Assert.ContainsSingle(result.Plan.Checks);
        Assert.AreEqual(RubyFactsAssembler.Scope, result.Plan.Scope);
        Assert.AreEqual(fixture.Request.Candidate.Checkout.Reference, result.Plan.Candidate);
        Assert.AreEqual(fixture.Request.Basis.Checkout.Reference, result.Plan.Comparison);
        Assert.AreEqual(RubyProjectChecks.RSpec, check.Work.Key.Check);
        Assert.AreEqual(result.Plan.Candidate, result.Candidate.Facts.Revision.Commit);
        Assert.AreEqual(result.Plan.Comparison, result.Basis.Facts.Revision.Commit);
        CollectionAssert.AreEqual(new[] { fixture.Request.Basis.Checkout.Reference,
            fixture.Request.Candidate.Checkout.Reference }, fixture.Collected);
        Assert.AreEqual("asciidoctor-latexmath", Assert.ContainsSingle(result.Candidate.Sources)
            .Gem.Name);
        if (!full)
            Assert.Contains(new SelectionReason("src/input/spec/one_spec.rb",
                result.Plan.Comparison, "ruby:src/input"), check.Reasons);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OtherResponsibilityCannotSuppressRubyConsumption(bool consumed)
    {
        using var fixture = await Fixture.CreateAsync("src/other/code.cs", false,
            context.CancellationToken, "dotnet");
        fixture.CrossInput = consumed;
        RubyGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        Assert.HasCount(consumed ? 1 : 0, result.Plan.Checks);
        if (consumed)
        {
            PlannedCheck check = Assert.ContainsSingle(result.Plan.Checks);
            Assert.HasCount(2, check.Reasons);
            Assert.IsTrue(check.Reasons.All(reason => reason.Path == "src/other/code.cs"));
        }
        else Assert.IsEmpty(result.Plan.Checks);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("ruby")]
    public async Task UnknownOrLegacyRubyHeuristicCannotBecomeNoWork(string? owner)
    {
        using var fixture = await Fixture.CreateAsync("unresolved.txt", false,
            context.CancellationToken, owner);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
    }

    [TestMethod]
    [DataRow(".github/workflows/ci.yml")]
    [DataRow("src/private/app/workflow-delivery/CI/Callers/RubyCiCaller.cs")]
    [DataRow("src/private/app/workflow-delivery/CI/Callers/NodeCiCaller.cs")]
    [DataRow("src/private/app/workflow-delivery/CI/Callers/DotNetCiCaller.cs")]
    [DataRow("src/private/app/workflow-delivery/CI/Callers/ControlCiCaller.cs")]
    [DataRow("mise.toml")]
    [DataRow("mise.lock")]
    public async Task ActualCallerInputsSelectAdoptedChecksAcrossBothEndpoints(string path)
    {
        using var fixture = await Fixture.SharedAsync(SharedCallers[path], true,
            context.CancellationToken, "ruby");
        RubyGroupReadback result = await fixture.ReadAsync(context.CancellationToken);
        PlannedCheck check = Assert.ContainsSingle(result.Plan.Checks);
        Assert.AreEqual(RubyProjectChecks.RSpec, check.Work.Key.Check);
        Assert.HasCount(2, check.Reasons);
        Assert.Contains(new SelectionReason(path, result.Plan.Comparison, "ruby:src/input"),
            check.Reasons);
        Assert.Contains(new SelectionReason(path, result.Plan.Candidate, "ruby:src/input"),
            check.Reasons);
        foreach (RubyRevisionInputs endpoint in new[] { result.Basis, result.Candidate })
            Assert.AreEqual("ruby:src/input", Assert.ContainsSingle(endpoint.Facts.OperationInputs
                .Single(input => input.Path == path).Consumers));
    }

    [TestMethod]
    public async Task BadScopeStopsBeforeCollection()
    {
        using var fixture = await Fixture.CreateAsync("src/input/spec/one_spec.rb", false,
            context.CancellationToken);
        var scope = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(
            fixture.Request.ScopePath, context.CancellationToken))!;
        scope["changed_paths"] = new System.Text.Json.Nodes.JsonArray();
        await File.WriteAllTextAsync(fixture.Request.ScopePath, scope.ToJsonString(),
            context.CancellationToken);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Collected);
    }

    [TestMethod]
    public async Task DifferentControlRuntimeStopsBeforeCollection()
    {
        using var fixture = await Fixture.CreateAsync("src/input/spec/one_spec.rb", false,
            context.CancellationToken);
        RubyGroupEndpoint candidate = fixture.Request.Candidate;
        fixture.Request = fixture.Request with
        {
            Candidate = candidate with
            {
                Collection = candidate.Collection with { Nbgv = "/other/nbgv" }
            }
        };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Collected);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedEndpointCannotBecomeSuccessfulNoWork(bool candidate)
    {
        using var fixture = await Fixture.SharedAsync(sharedSpec!, false,
            context.CancellationToken);
        fixture.FailedEndpoint = candidate ? fixture.Request.Candidate.Checkout.Reference :
            fixture.Request.Basis.Checkout.Reference;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(fixture.Collected.Contains(fixture.FailedEndpoint));
    }

    [TestMethod]
    public async Task CancelledCollectionCannotReturnPlan()
    {
        using var fixture = await Fixture.CreateAsync("src/input/spec/one_spec.rb", false,
            context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.AfterBasis = cancellation.Cancel;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.ReadAsync(cancellation.Token));
        Assert.AreEqual(fixture.Request.Basis.Checkout.Reference,
            Assert.ContainsSingle(fixture.Collected));
    }

    private sealed class Fixture(GitFixture? repository, string state) : IDisposable
    {
        private readonly GitFixture? repo = repository;
        internal RubyGroupRequest Request { get; set; } = null!;
        internal bool CrossInput { get; set; }
        internal bool ActualCollection { get; set; }
        internal string? FailedEndpoint { get; set; }
        internal Action? AfterBasis { get; set; }
        internal List<string> Collected { get; } = [];

        internal sealed record Pair(string Changed, string Basis, string BasisRoot,
            string Candidate, string CandidateRoot);

        private static readonly string[] Sources = ["src/input/asciidoctor-latexmath.gemspec",
            Spec];

        private static async Task StageAsync(GitFixture repo, IEnumerable<string> changed,
            bool actualCollection)
        {
            foreach (string path in Sources.Concat(changed))
                await repo.SetAsync(path);
            if (actualCollection)
            {
                await repo.SetAsync("src/input/version.json", "{\"version\":\"1.2\"}");
                await repo.SetAsync("src/input/workflow-delivery.quality.yml",
                    "schema: workflow-delivery/v3/quality-selection\necosystems:\n" +
                    "  ruby:\n    preset: ruby/rspec-v1\n");
            }
        }

        // One adopted basis; each changed path gets a sibling candidate in its own worktree.
        internal static async Task<(Pair Spec, Pair[] Callers)> CommitSharedAsync(
            GitFixture repo, string[] callers)
        {
            await StageAsync(repo, callers, true);
            string basis = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", basis);
            string parent = Path.GetDirectoryName(repo.Directory)!;
            string basisRoot = Path.Combine(parent, "basis");
            await repo.GitAsync("worktree", "add", "--detach", basisRoot, basis);
            var pairs = new List<Pair>();
            foreach (string changed in callers.Prepend(Spec))
            {
                await repo.SetAsync(changed, "updated");
                string candidate = await repo.CommitAsync(basis);
                string root = Path.Combine(parent, "candidate-" + pairs.Count);
                await repo.GitAsync("worktree", "add", "--detach", root, candidate);
                pairs.Add(new(changed, basis, basisRoot, candidate, root));
                await repo.SetAsync(changed);
            }
            return (pairs[0], pairs.Skip(1).ToArray());
        }

        // Reads class-owned endpoints; the row owns only its scope and native scratch.
        internal static async Task<Fixture> SharedAsync(Pair pair, bool actualCollection,
            CancellationToken token, string? owner = null)
        {
            var fixture = new Fixture(null, Directory.CreateTempSubdirectory(
                "workflow-ruby-group-").FullName) { ActualCollection = actualCollection };
            try
            {
                await fixture.RequestAsync(pair.Changed, false, owner, pair.Basis,
                    pair.BasisRoot, pair.Candidate, pair.CandidateRoot, token);
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        internal static async Task<Fixture> CreateAsync(string changed, bool full,
            CancellationToken token, string? owner = null, bool actualCollection = false)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            var fixture = new Fixture(repo, Directory.CreateDirectory(Path.Combine(
                Path.GetDirectoryName(repo.Directory)!, "state")).FullName);
            try
            {
                fixture.ActualCollection = actualCollection;
                await StageAsync(repo, [changed], actualCollection);
                string before = await repo.CommitAsync();
                await repo.GitAsync("reset", "--hard", before);
                string parent = Path.GetDirectoryName(repo.Directory)!;
                string beforeRoot = Path.Combine(parent, "basis");
                await repo.GitAsync("worktree", "add", "--detach", beforeRoot, before);
                await repo.SetAsync(changed, "updated");
                string after = await repo.CommitAsync(before);
                await repo.GitAsync("reset", "--hard", after);
                if (full) { before = after; beforeRoot = repo.Directory; }
                await fixture.RequestAsync(changed, full, owner, before, beforeRoot, after,
                    repo.Directory, token);
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        private async Task RequestAsync(string changed, bool full, string? owner,
            string before, string beforeRoot, string after, string afterRoot,
            CancellationToken token)
        {
            string scope = Path.Combine(state, "scope.json");
            object Endpoint(string revision) => new
            {
                revision,
                paths = full ? Array.Empty<object>() : new object[] { new
                {
                    path = changed, present = true, mode = "100644",
                    reasons = owner is null ? Array.Empty<object>() : new object[] { new
                    { owner, target = changed, rule = "retained-responsibility",
                        sources = new[] { "src/input/asciidoctor-latexmath.gemspec" } } }
                } }
            };
            await File.WriteAllTextAsync(scope, JsonSerializer.Serialize(new
            {
                @base = before, candidate = after, full,
                changed_paths = full ? Array.Empty<string>() : new[] { changed },
                endpoint_owners = new { basis = Endpoint(before), candidate = Endpoint(after) }
            }), token);
            // Native fact requests use row-owned scratch, never a checkout.
            string scratch = Directory.CreateDirectory(Path.Combine(state, "facts")).FullName;
            RubyGroupEndpoint EndpointRequest(string root, string revision) => new(
                new(root, revision),
                new(new(Path.Combine(state, "ruby"), Path.Combine(state, "facts.rb"), scratch,
                    new Dictionary<string, string?>(), 30), Path.Combine(state, "nbgv"),
                    "2.7.2"));
            Request = new(EndpointRequest(beforeRoot, before), EndpointRequest(afterRoot, after),
                full, scope);
        }

        internal Task<RubyGroupReadback> ReadAsync(CancellationToken token) =>
            RubyGroupReader.ReadAsync(Request, (checkout, _, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (checkout.Revision.Commit == FailedEndpoint)
                    throw new InvalidDataException("Required native endpoint failed.");
                Collected.Add(checkout.Revision.Commit);
                if (ActualCollection)
                    return RubyRepositoryReader.ReadAsync(checkout,
                        Request.Candidate.Collection, async (command, cancellationToken) =>
                        {
                            object value;
                            if (command.Executable == Request.Candidate.Collection.Nbgv)
                                value = new { SemVer2 = "1.2.3+original" };
                            else
                            {
                                using JsonDocument input = JsonDocument.Parse(
                                    await File.ReadAllTextAsync(command.Arguments[^1],
                                        cancellationToken));
                                string gemspec = Path.Combine(checkout.Root,
                                    "src/input/asciidoctor-latexmath.gemspec");
                                string? operation = input.RootElement.GetProperty("operation")
                                    .GetString();
                                value = operation ==
                                    "version" ? (object)new
                                    {
                                        gemspec,
                                        raw = "1.2.3+original",
                                        version = "1.2.3",
                                        literal = "\"1.2.3\""
                                    } :
                                    new
                                    {
                                        gemspec,
                                        name = "asciidoctor-latexmath",
                                        version = "1.2.3",
                                        platform = "ruby",
                                        require_paths = Array.Empty<string>(),
                                        files = Array.Empty<string>(),
                                        runtime_dependencies = Array.Empty<object>()
                                    };
                            }
                            return new WorkflowDelivery.Platform.NativeCommandResult(
                                WorkflowDelivery.Platform.NativeTermination.Exited, 0,
                                JsonSerializer.Serialize(value), "", 0, null);
                        }, cancellation);
                var check = new CheckSpec(new("src/input", RubyProjectChecks.RSpec, "default"),
                    [], "ubuntu-latest", true, []);
                var owner = new ProjectFacts("ruby:src/input", "src/input", [], [], null,
                    RubyProjectChecks.Preset, [check]);
                SharedInput[] inputs = CrossInput ? [new("src/other/code.cs", ["src/input"])] : [];
                var graph = new RubyDependencyGraph(
                    [new("src/input", "asciidoctor-latexmath", [])], []);
                var facts = new RubyFactsEndpoint(checkout.Revision, new(graph, inputs),
                    [owner], []);
                var source = new RubySourceGemFacts(Path.Combine(checkout.Root,
                    "src/input/asciidoctor-latexmath.gemspec"),
                    new("asciidoctor-latexmath", "1.2.3", "ruby", ["lib"], [], []));
                if (checkout.Revision.Commit == Request.Basis.Checkout.Reference)
                    AfterBasis?.Invoke();
                return Task.FromResult(new RubyRevisionInputs(facts, [source], [], []));
            }, token);

        public void Dispose()
        {
            if (repo is null) Directory.Delete(state, recursive: true);
            else repo.Dispose();
        }
    }
}
