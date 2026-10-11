using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class RubyGroupReaderTests(TestContext context)
{
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
        using var fixture = await Fixture.CreateAsync("src/input/spec/one_spec.rb", false,
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

    private sealed class Fixture(GitFixture repository) : IDisposable
    {
        private readonly GitFixture repo = repository;
        internal RubyGroupRequest Request { get; set; } = null!;
        internal bool CrossInput { get; set; }
        internal string? FailedEndpoint { get; set; }
        internal Action? AfterBasis { get; set; }
        internal List<string> Collected { get; } = [];

        internal static async Task<Fixture> CreateAsync(string changed, bool full,
            CancellationToken token, string? owner = null)
        {
            var fixture = new Fixture(await GitFixture.CreateAsync(token));
            try
            {
                foreach (string path in new[] { "src/input/asciidoctor-latexmath.gemspec",
                             "src/input/spec/one_spec.rb", changed })
                    await fixture.repo.SetAsync(path);
                string before = await fixture.repo.CommitAsync();
                await fixture.repo.GitAsync("reset", "--hard", before);
                string parent = Path.GetDirectoryName(fixture.repo.Directory)!;
                string beforeRoot = Path.Combine(parent, "basis");
                await fixture.repo.GitAsync("worktree", "add", "--detach", beforeRoot, before);
                await fixture.repo.SetAsync(changed, "updated");
                string after = await fixture.repo.CommitAsync(before);
                await fixture.repo.GitAsync("reset", "--hard", after);
                if (full) { before = after; beforeRoot = fixture.repo.Directory; }
                string scope = Path.Combine(parent, "scope.json");
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
                RubyGroupEndpoint Request(string root, string revision) => new(new(root, revision),
                    new(new(Path.Combine(parent, "ruby"), Path.Combine(parent, "facts.rb"), root,
                        new Dictionary<string, string?>(), 30), Path.Combine(parent, "nbgv"),
                        "2.7.2"));
                fixture.Request = new(Request(beforeRoot, before),
                    Request(fixture.repo.Directory, after), full, scope);
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        internal Task<RubyGroupReadback> ReadAsync(CancellationToken token) =>
            RubyGroupReader.ReadAsync(Request, (checkout, _, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (checkout.Revision.Commit == FailedEndpoint)
                    throw new InvalidDataException("Required native endpoint failed.");
                Collected.Add(checkout.Revision.Commit);
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

        public void Dispose() => repo.Dispose();
    }
}
