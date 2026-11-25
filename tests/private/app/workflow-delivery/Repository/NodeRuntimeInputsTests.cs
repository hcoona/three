using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class NodeRuntimeInputsTests(TestContext context)
{
    private const string Source = "packages/adopted";
    internal const string Workflow = "jobs:\n  node-tests:\n    strategy:\n      matrix:\n"
        + "        node-version: ${{ fromJSON(needs.node-group.outputs.node_versions) }}\n";
    internal const string ScheduleCommit = "0123456789abcdef0123456789abcdef01234567";
    private const string Mise = "[tools]\nnode='24'\n";
    private const string MiseLock = "version=3\n";
    private static readonly string[] PlannedKeys = ["node24", "node26", "node27"];
    private static readonly string[] PreOneRows = ["\"v0.12\": { \"start\": \"2015-02-06\" }"];
    private static readonly string[] NativeCommands =
        ["ls --json --current --locked node", "ls --json --current --locked pnpm"];
    private static readonly string[] MixedMatrix = ["20.x", "22.x", "24.x", "26.x"];
    private static readonly string[] AdoptedVariants = ["node24", "node26"];
    private static readonly string[] RetainedVariants = ["node20", "node22", "node24", "node26"];
    private static readonly string[] BuildAndTestChecks =
        ["node/project-build-v1", "node/project-test-v1"];
    private static GitFixture? shared;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext) =>
        shared = await RuntimeCheckoutAsync(false, classContext.CancellationToken);

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task ReadPlansFromCommittedDataAndNativeMiseToolIdentities()
    {
        GitFixture repo = shared!;
        GitMaterialization checkout = await BindAsync(repo);
        var commands = new List<string>();
        NodeRuntimeInputs inputs = await new NodeRuntimeReader(checkout, (args, _) =>
        {
            commands.Add(string.Join(' ', args));
            return Task.FromResult(Tool(repo.Directory, args[^1]));
        }).ReadAsync([Source], context.CancellationToken);

        Assert.AreEqual(checkout.Revision.Commit, inputs.Revision);
        CollectionAssert.AreEqual(new[] { new NodeRuntimeVariant("node22", "22.x", null),
            new NodeRuntimeVariant("node24", "24.x", "24.21.0"),
            new NodeRuntimeVariant("node26", "26.x", null) }, inputs.Variants);
        Assert.AreEqual("12.8.2", inputs.PnpmVersion);
        Assert.AreEqual("node24", inputs.PackagingVariant);
        Assert.AreEqual((ScheduleCommit, "26.1.0", 22),
            (inputs.ScheduleCommit, inputs.NewestRelease, inputs.LowerBound));
        CollectionAssert.AreEqual(new[] { NodeRuntimeReader.Schedule, "mise.toml", "mise.lock",
            "package.json", Source + "/package.json" }, inputs.Sources);
        CollectionAssert.AreEqual(NativeCommands, commands);
    }

    // Planning compares majors and LTS designation only; no date or clock is evaluated.
    [TestMethod]
    [DataRow("26.1.0", ">=22", ">=22.6.0", 22, new[] { 22, 24, 26 })]
    [DataRow("27.0.0", ">=22", ">=22.6.0", 22, new[] { 22, 24, 26, 27 })]
    [DataRow("25.2.1", ">=22", ">=22.6.0", 22, new[] { 22, 24, 25 })]
    [DataRow("29.0.0", ">=22", ">=24", 22, new[] { 22, 24, 26, 27, 28, 29 })]
    [DataRow("26.1.0", ">=18", ">=24.0.0", 18, new[] { 18, 20, 22, 24, 26 })]
    [DataRow("26.1.0", ">=24.0.0", ">=20.19", 20, new[] { 20, 22, 24, 26 })]
    [DataRow("26.1.0", ">=24", ">=23", 23, new[] { 24, 26 })]
    public void MatrixIsEveryLtsMajorFromTheLowestMinimumPlusTheNewestMajor(
        string newest, string root, string member, int lowerBound, int[] majors)
    {
        NodeRuntimeInputs inputs = Read(schedule: Schedule(newest), root: root, member: member);

        CollectionAssert.AreEqual(majors.Select(major => major + ".x").ToArray(),
            inputs.Variants.Select(variant => variant.Selector).ToArray());
        CollectionAssert.AreEqual(majors.Select(major => "node" + major).ToArray(),
            inputs.Variants.Select(variant => variant.Key).ToArray());
        Assert.AreEqual(lowerBound, inputs.LowerBound);
        Assert.AreEqual(newest, inputs.NewestRelease);
        Assert.AreEqual("24.21.0", inputs.Variants.Single(v => v.Key == "node24").ExactVersion);
        Assert.ContainsSingle(inputs.Variants.Where(v => v.ExactVersion is not null));
    }

    [TestMethod]
    public async Task MemberRangesProjectTheUnionMatrixAndFilterAdoptedObligations()
    {
        const string retained = "packages/retained";
        string root = Path.Combine(Path.GetTempPath(), "node-runtime-checkout");
        NodeRuntimeInputs inputs = NodeRuntimeReader.Read(new(NodeScenario.Candidate, []), root,
            Workflow, Schedule(), Mise, MiseLock,
            [
                new("package.json", Manifest(">=22")),
                new(Source + "/package.json", Manifest(">=24.0.0")),
                new(retained + "/package.json", Manifest(">=20")),
            ], Tool(root, "node"), Tool(root, "pnpm"));
        NodeRevisionInputs At(string revision) => NodeScenario.Units(NodeScenario.Inputs(revision,
            NodeScenario.Project(Source), NodeScenario.Project(retained)),
            NodeScenario.Unit("fixture", ("npm", Source)));
        CiPlan original = await NodeScenario.Plan(At(NodeScenario.Basis),
            At(NodeScenario.Candidate), Source + "/index.js", retained + "/index.js");

        CiPlan plan = inputs.Expand(original);

        CollectionAssert.AreEqual(MixedMatrix,
            inputs.Variants.Select(variant => variant.Selector).ToArray());
        Assert.AreEqual(20, inputs.LowerBound);
        CollectionAssert.AreEquivalent(new[]
        {
            new NodeRuntimeMember(Source, System.Version.Parse("24.0.0")),
            new NodeRuntimeMember(retained, System.Version.Parse("20.0.0")),
        }, inputs.Members);
        Assert.HasCount(15, plan.Checks);
        PlannedCheck[] buildAndTest = plan.Checks.Where(check =>
            check.Work.Key.Check is "node/project-build-v1" or "node/project-test-v1").ToArray();
        Assert.HasCount(12, buildAndTest);
        foreach ((string directory, string[] variants) in new[]
        {
            (Source, AdoptedVariants),
            (retained, RetainedVariants),
        })
        {
            PlannedCheck[] memberChecks = buildAndTest.Where(check =>
                check.Work.Key.Target == directory).ToArray();
            CollectionAssert.AreEquivalent(variants, memberChecks
                .Select(check => check.Work.Key.Variant).Distinct().ToArray());
            foreach (string variant in variants)
                CollectionAssert.AreEquivalent(BuildAndTestChecks,
                    memberChecks.Where(check => check.Work.Key.Variant == variant)
                    .Select(check => check.Work.Key.Check).ToArray());
        }
        PlannedCheck[] packaging = plan.Checks.Where(check =>
            check.Work.Key.Check is "node/npm-artifact-v1" or
                "node/npm-artifact-contents-v1" or "node/npm-install-import-v1").ToArray();
        Assert.HasCount(3, packaging);
        Assert.IsTrue(packaging.All(check => check.Work.Key.Variant == "node24"));
        Assert.IsTrue(packaging.All(check => check.Work.Package?.Directory == Source));
        PlannedCheck pack = packaging.Single(check => check.Work.Key.Check ==
            "node/npm-artifact-v1");
        Assert.AreEqual(new CheckKey(Source, "node/project-build-v1", "node24"),
            Assert.ContainsSingle(pack.Work.Prerequisites));
        Assert.IsEmpty(plan.Checks.Where(check => check.Work.Key.Variant == "node26" &&
            check.Work.Key.Check.StartsWith("node/npm-", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow(">=24.21.0", true)]
    [DataRow(">=24.21.1", false)]
    [DataRow(">=24.22.0", false)]
    public async Task ConfiguredPackagingVariantMustSatisfyTheFullAdoptedMemberRange(
        string range, bool admitted)
    {
        const string retained = "packages/retained";
        string root = Path.Combine(Path.GetTempPath(), "node-runtime-checkout");
        NodeRuntimeInputs inputs = NodeRuntimeReader.Read(new(NodeScenario.Candidate, []), root,
            Workflow, Schedule(), Mise, MiseLock,
            [
                new("package.json", Manifest(">=22")),
                new(Source + "/package.json", Manifest(range)),
                new(retained + "/package.json", Manifest(">=20")),
            ], Tool(root, "node"), Tool(root, "pnpm"));
        CiPlan original = await PackagePlanAsync();

        if (admitted)
        {
            CiPlan plan = inputs.Expand(original);
            Assert.IsNotEmpty(plan.Checks.Where(check =>
                check.Work.Key.Check.StartsWith("node/npm-", StringComparison.Ordinal)));
        }
        else
        {
            InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
                inputs.Expand(original));
            Assert.AreEqual(
                "Configured packaging runtime is outside the adopted Node engines range.",
                error.Message);
        }
        Assert.IsTrue(inputs.Variants.Any(variant => variant.Key == "node26"));
        Assert.AreEqual("node24", inputs.PackagingVariant);
    }

    [TestMethod]
    [DataRow("^22.0.0")]
    [DataRow(">=22 <27")]
    [DataRow(">= 22")]
    [DataRow(">=22.0.0-rc.1")]
    [DataRow(null)]
    public void UnsupportedEnginesCannotBeApproximatedByAMatrix(string? range)
    {
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(member: range));
        Assert.AreEqual("Unsupported Node engines range: " + Member, error.Message);
    }

    private const string Member = Source + "/package.json";
    private const string Authority = "Native runtime tool is outside committed authority.";

    [TestMethod]
    public void ScheduleMustCoverTheCompleteRange()
    {
        string missingMajor = Schedule().Replace("\"v23\"", "\"v123\"", StringComparison.Ordinal);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(schedule: missingMajor));
        Assert.AreEqual("The Node release schedule has no row: v23", error.Message);
    }

    [TestMethod]
    [DataRow("27.0.0-rc.1", "")]
    [DataRow("26.1.0", ",\n      \"v24\": { \"start\": \"2000-01-01\" }")]
    public void PrereleaseOrAmbiguousScheduleCannotSupplyTheMatrix(string newest, string extra)
    {
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(schedule: Schedule(newest, extra: extra)));
        Assert.AreEqual("Invalid committed Node release schedule.", error.Message);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("schedule")]
    public void UnknownScheduleMembersCannotSupplyTheMatrix(string? section)
    {
        JsonObject schedule = JsonNode.Parse(Schedule())!.AsObject();
        JsonNode target = section is null ? schedule : schedule[section]!;
        target["generated"] = "2026-01-01";

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(schedule: schedule.ToJsonString()));
        Assert.AreEqual("Invalid committed Node release schedule.", error.Message);
    }

    [TestMethod]
    [DataRow("[22.x, 24.x, 26.x]")]
    [DataRow("${{ fromJSON(needs.node-group.outputs.retained_directories) }}")]
    public void WorkflowMustConsumeTheComputedRuntimeMatrix(string matrix)
    {
        string workflow = Workflow.Replace(NodeRuntimeReader.MatrixConsumer, matrix,
            StringComparison.Ordinal);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(workflow: workflow));
        Assert.AreEqual("Unsupported complete Node runtime matrix.", error.Message);
    }

    [TestMethod]
    public async Task MissingCommittedScheduleStopsBeforeNativeTools()
    {
        using GitFixture repo = await RuntimeCheckoutAsync(true, context.CancellationToken);
        GitMaterialization checkout = await BindAsync(repo);
        var reader = new NodeRuntimeReader(checkout, (_, _) =>
            throw new AssertFailedException("No runtime query is valid without a schedule."));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync([Source], context.CancellationToken));
        Assert.Contains("Missing committed Node release schedule", error.Message);
    }

    [TestMethod]
    [DataRow("node", null, false)]
    [DataRow("node", "22", true)]
    [DataRow("pnpm", "latest", true)]
    public async Task RuntimeToolsMustReturnActiveLockedSelections(
        string tool, string? requested, bool active)
    {
        GitFixture repo = shared!;
        GitMaterialization checkout = await BindAsync(repo);
        var reader = new NodeRuntimeReader(checkout, (args, _) => Task.FromResult(args[^1] == tool
            ? Tool(repo.Directory, tool, requested, active) : Tool(repo.Directory, args[^1])));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync([Source], context.CancellationToken));
        Assert.AreEqual(Authority, error.Message);
    }
    [TestMethod]
    public async Task NativeRuntimeToolCannotTakeItsVersionFromForeignConfiguration()
    {
        GitFixture repo = shared!;
        GitMaterialization checkout = await BindAsync(repo);
        string foreign = Path.Combine(repo.Directory, "uncommitted");
        var reader = new NodeRuntimeReader(checkout, (args, _) => Task.FromResult(
            Tool(args[^1] == "node" ? foreign : repo.Directory, args[^1])));

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync([Source], context.CancellationToken));
        Assert.AreEqual(Authority, error.Message);
    }

    [TestMethod]
    public async Task RuntimeProjectionKeepsTwoObligationsPerRuntimePlusPackagingAndSubject()
    {
        CiPlan original = await PackagePlanAsync();
        CiPlan plan = Inputs(original.Candidate).Expand(original);

        Assert.AreEqual(original.Comparison, plan.Comparison);
        Assert.AreEqual(original.Candidate, plan.Candidate);
        Assert.AreEqual(original.Scope, plan.Scope);
        Assert.HasCount(7, plan.Checks);
        Assert.HasCount(2, plan.Checks.Where(c => c.Work.Key.Variant == "node22"));
        Assert.HasCount(5, plan.Checks.Where(c => c.Work.Key.Variant == "node24"));
        foreach (PlannedCheck check in plan.Checks)
        {
            PlannedCheck source = original.Checks.Single(c =>
                c.Work.Key.Target == check.Work.Key.Target &&
                c.Work.Key.Check == check.Work.Key.Check);
            CollectionAssert.AreEqual(source.Reasons, check.Reasons);
            CollectionAssert.AreEqual(source.QualityPresets, check.QualityPresets);
            Assert.AreSame(source.Work.Package, check.Work.Package);
            Assert.AreEqual("12.8.2", check.Work.Dimensions["pnpm"]);
            Assert.AreEqual(check.Work.Key.Variant == "node22" ? "22.x" : "24.x",
                check.Work.Dimensions["node"]);
            Assert.AreEqual(check.Work.Key.Variant == "node24",
                check.Work.Dimensions.ContainsKey("node-version"));
            if (check.Work.Key.Variant == "node24")
                Assert.AreEqual("24.21.0", check.Work.Dimensions["node-version"]);
            CollectionAssert.AreEqual(source.Work.Prerequisites.Select(p =>
                p with { Variant = check.Work.Key.Variant }).ToArray(), check.Work.Prerequisites);
        }
        foreach (PlannedCheck test in plan.Checks.Where(c =>
            c.Work.Key.Check == "node/project-test-v1"))
            Assert.IsEmpty(test.Work.Prerequisites);
        PlannedCheck pack = plan.Checks.Single(c => c.Work.Key.Check == "node/npm-artifact-v1");
        Assert.AreEqual(new CheckKey(Source, "node/project-build-v1", "node24"),
            Assert.ContainsSingle(pack.Work.Prerequisites));
        foreach (PlannedCheck check in plan.Checks.Where(c =>
            c.Work.Key.Check is "node/npm-artifact-contents-v1" or "node/npm-install-import-v1"))
            Assert.AreEqual(pack.Work.Key, Assert.ContainsSingle(check.Work.Prerequisites));
        CiOutcome outcome = ResultCollector.Collect(plan, plan.Checks.Select(c =>
            new CheckResult(plan.Candidate, c.Work.Key, CheckStatus.Passed)).ToArray());
        Assert.IsTrue(outcome.Satisfied);
        Assert.HasCount(7, outcome.Checks);
        Assert.IsEmpty(outcome.Errors);
    }

    [TestMethod]
    public void EmptyProjectionKeepsTheComparisonAndCandidate()
    {
        var original = new CiPlan(NodeScenario.Basis, NodeScenario.Candidate,
            NodeFactsAssembler.Scope, []);

        CiPlan plan = Inputs(original.Candidate).Expand(original);

        Assert.AreEqual(original, plan with { Checks = original.Checks });
        Assert.IsEmpty(plan.Checks);
        Assert.IsTrue(ResultCollector.Collect(plan, []).Satisfied);
    }

    [TestMethod]
    [DataRow(false, "missing")]
    [DataRow(true, "conflicting")]
    public async Task CollectorRequiresOneResultForEveryRuntime(
        bool conflictingResults, string expectedStatus)
    {
        CiPlan original = await PackagePlanAsync();
        CiPlan plan = Inputs(original.Candidate).Expand(original);
        CheckKey key = new(Source, "node/project-test-v1", "node22");
        var results = plan.Checks.Select(c =>
            new CheckResult(plan.Candidate, c.Work.Key, CheckStatus.Passed)).ToList();
        CheckResult passed = results.Single(r => r.Key == key);
        if (conflictingResults) results.Add(passed with { Status = CheckStatus.Failed });
        else results.Remove(passed);

        CiOutcome outcome = ResultCollector.Collect(plan, [.. results]);

        Assert.IsFalse(outcome.Satisfied);
        Assert.HasCount(7, outcome.Checks);
        Assert.AreEqual(expectedStatus, outcome.Checks.Single(c => c.Key == key).Status);
        Assert.HasCount(6, outcome.Checks.Where(c => c.Status == "passed"));
    }

    [TestMethod]
    public async Task ProjectionCannotUseRuntimeFactsFromTheBasis()
    {
        CiPlan plan = await PackagePlanAsync();

        Assert.ThrowsExactly<InvalidDataException>(() =>
            Inputs(NodeScenario.Basis).Expand(plan));
    }

    [TestMethod]
    public async Task ConfiguredPackagingMajorCanBeBelowOtherPlannedRuntimes()
    {
        NodeRuntimeInputs inputs = Read(schedule: Schedule("27.0.0"), root: ">=24",
            member: ">=24");
        Assert.AreEqual("node24", inputs.PackagingVariant);
        CollectionAssert.AreEqual(PlannedKeys,
            inputs.Variants.Select(v => v.Key).ToArray());
        CiPlan parent = await PackagePlanAsync();
        CiPlan plan = inputs.Expand(parent with { Candidate = inputs.Revision });
        Assert.HasCount(2 * 3 + 3, plan.Checks);
        Assert.HasCount(2, NodeExecution.SelectRuntime(plan, "node27").Checks);
        Assert.HasCount(5, NodeExecution.SelectRuntime(plan, "node24").Checks);
        Assert.IsTrue(ResultCollector.Collect(plan, plan.Checks.Select(check =>
            new CheckResult(plan.Candidate, check.Work.Key, CheckStatus.Passed)).ToArray(
                )).Satisfied);
    }

    // Even majors are LTS before 27; every major from 27 is LTS (upstream schedule).
    internal static string Schedule(string newest = "26.1.0", int last = 29,
        string extra = "", string commit = ScheduleCommit, string top = "")
    {
        IEnumerable<string> rows = PreOneRows.Concat(Enumerable.Range(16, last - 15).Select(major =>
                $"\"v{major}\": {{ \"start\": \"2000-01-01\"" +
                (major % 2 == 0 || major >= 27 ? ", \"lts\": \"2000-06-01\"" : "") +
                (major == 27 ? ", \"alpha\": \"1999-12-01\", \"codename\": \"\"" : "") +
                ", \"end\": \"2001-01-01\" }"));
        return "{\n  \"newestRelease\": \"" + newest + "\"" + top + ",\n  \"schedule\": {\n" +
            "    \"commit\": \"" + commit + "\",\n    \"releases\": {\n      " +
            string.Join(",\n      ", rows) + extra + "\n    }\n  }\n}\n";
    }

    internal static string Manifest(string? range) => range is null
        ? "{ \"name\": \"member\" }\n"
        : "{ \"name\": \"member\", \"engines\": { \"node\": " + JsonSerializer.Serialize(range) +
            " } }\n";

    private static NodeRuntimeInputs Read(string? schedule = null,
        string root = ">=22", string? member = ">=22.6.0", string workflow = Workflow)
    {
        string path = Path.Combine(Path.GetTempPath(), "node-runtime-checkout");
        return NodeRuntimeReader.Read(new(NodeScenario.Candidate, []), path, workflow,
            schedule ?? Schedule(), Mise, MiseLock,
            [new(Member, Manifest(member)), new("package.json", Manifest(root))],
            Tool(path, "node"), Tool(path, "pnpm"));
    }

    private static NodeRuntimeInputs Inputs(string revision) => new(revision,
        [new("node22", "22.x", null), new("node24", "24.x", "24.21.0")], "12.8.2",
        [NodeRuntimeReader.Schedule, "mise.toml", "mise.lock"], "node24", ScheduleCommit,
        "26.1.0", 22, [new(Source, System.Version.Parse("22.0.0"))]);

    private static Task<CiPlan> PackagePlanAsync()
    {
        NodeRevisionInputs At(string revision) => NodeScenario.Units(NodeScenario.Inputs(revision,
            NodeScenario.Project(Source)), NodeScenario.Unit("fixture", ("npm", Source)));
        return NodeScenario.Plan(At(NodeScenario.Basis), At(NodeScenario.Candidate),
            Source + "/index.js");
    }

    private static async Task<GitFixture> RuntimeCheckoutAsync(bool omitSchedule,
        CancellationToken token)
    {
        GitFixture repo = await GitFixture.CreateAsync(token);
        try
        {
            await repo.SetAsync(NodeRuntimeReader.Workflow, Workflow);
            if (!omitSchedule)
                await repo.SetAsync(NodeRuntimeReader.Schedule, Schedule());
            await repo.SetAsync("mise.toml", Mise);
            await repo.SetAsync("mise.lock", MiseLock);
            await repo.SetAsync("package.json", Manifest(">=22"));
            await repo.SetAsync(Member, Manifest(">=22.6.0"));
            string commit = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", commit);
            return repo;
        }
        catch
        {
            repo.Dispose();
            throw;
        }
    }

    private async Task<GitMaterialization> BindAsync(GitFixture repo) =>
        await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync("HEAD", context.CancellationToken),
            context.CancellationToken);

    internal static string Tool(string root, string name, string? requested = null,
        bool active = true)
    {
        string version = name == "node" ? "24.21.0" : "12.8.2";
        object tool = new
        {
            version,
            requested_version = requested ?? (name == "node" ? "24" : version),
            active,
            source = new
            {
                type = "mise.toml",
                path = Path.Combine(root, "mise.toml"),
            },
        };
        return JsonSerializer.Serialize(new[] { tool });
    }
}
