using System.Text.Json;
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

    [TestMethod]
    public async Task ReadPlansFromCommittedDataAndNativeMiseToolIdentities()
    {
        using var repo = await RuntimeCheckoutAsync();
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
    [DataRow("current", "26.1.0", ">=22", ">=22.6.0", new[] { 22, 24, 26 })]
    [DataRow("new-current-major", "27.0.0", ">=22", ">=22.6.0", new[] { 22, 24, 26, 27 })]
    [DataRow("odd-current-major", "25.2.1", ">=22", ">=22.6.0", new[] { 22, 24, 25 })]
    [DataRow("all-lts-from-27", "29.0.0", ">=22", ">=24", new[] { 22, 24, 26, 27, 28, 29 })]
    [DataRow("end-of-life-lower-bound", "26.1.0", ">=18", ">=24.0.0",
        new[] { 18, 20, 22, 24, 26 })]
    [DataRow("lowest-minimum-union", "26.1.0", ">=24.0.0", ">=20.19", new[] { 20, 22, 24, 26 })]
    [DataRow("non-lts-lower-bound", "26.1.0", ">=24", ">=23", new[] { 24, 26 })]
    public void MatrixIsEveryLtsMajorFromTheLowestMinimumPlusTheNewestMajor(string scenario,
        string newest, string root, string member, int[] majors)
    {
        NodeRuntimeInputs inputs = Read(schedule: Schedule(newest), root: root, member: member);

        CollectionAssert.AreEqual(majors.Select(major => major + ".x").ToArray(),
            inputs.Variants.Select(variant => variant.Selector).ToArray(), scenario);
        CollectionAssert.AreEqual(majors.Select(major => "node" + major).ToArray(),
            inputs.Variants.Select(variant => variant.Key).ToArray(), scenario);
        Assert.AreEqual(scenario == "non-lts-lower-bound" ? 23 : majors[0], inputs.LowerBound);
        Assert.AreEqual(newest, inputs.NewestRelease);
        Assert.AreEqual("24.21.0", inputs.Variants.Single(v => v.Key == "node24").ExactVersion);
        Assert.ContainsSingle(inputs.Variants.Where(v => v.ExactVersion is not null));
    }

    [TestMethod]
    [DataRow("lower-above-newest", "The declared Node lower bound is above the newest release.")]
    [DataRow("missing-row", "The Node release schedule has no row: v23")]
    [DataRow("packaging-outside", Packaging)]
    [DataRow("unsupported-caret", "Unsupported Node engines range: " + Member)]
    [DataRow("unsupported-compound", "Unsupported Node engines range: " + Member)]
    [DataRow("unsupported-whitespace", "Unsupported Node engines range: " + Member)]
    [DataRow("unsupported-prerelease", "Unsupported Node engines range: " + Member)]
    [DataRow("missing-range", "Unsupported Node engines range: " + Member)]
    [DataRow("missing-engines", "Unsupported Node engines range: " + Member)]
    [DataRow("unreadable-manifest", "Unreadable Node engines manifest: " + Member)]
    [DataRow("invalid-manifest", "Unreadable Node engines manifest: " + Member)]
    [DataRow("duplicate-manifest-key", "Unreadable Node engines manifest: " + Member)]
    [DataRow("missing-root-manifest", "Missing root Node engines manifest.")]
    [DataRow("missing-schedule", "Missing committed Node release schedule: " +
        NodeRuntimeReader.Schedule)]
    [DataRow("invalid-schedule-json", InvalidSchedule)]
    [DataRow("unknown-top-level-member", InvalidSchedule)]
    [DataRow("unknown-schedule-member", InvalidSchedule)]
    [DataRow("missing-newest", InvalidSchedule)]
    [DataRow("prerelease-newest", InvalidSchedule)]
    [DataRow("prefixed-newest", InvalidSchedule)]
    [DataRow("numeric-newest", InvalidSchedule)]
    [DataRow("short-commit", InvalidSchedule)]
    [DataRow("uppercase-commit", InvalidSchedule)]
    [DataRow("duplicate-key", InvalidSchedule)]
    [DataRow("duplicate-top-level-key", InvalidSchedule)]
    [DataRow("invalid-key", InvalidSchedule)]
    [DataRow("leading-zero-key", InvalidSchedule)]
    [DataRow("non-object-row", InvalidSchedule)]
    [DataRow("non-string-lts", InvalidSchedule)]
    [DataRow("empty-lts", InvalidSchedule)]
    [DataRow("literal-matrix", Matrix)]
    [DataRow("other-expression", Matrix)]
    [DataRow("matrix-include", Matrix)]
    [DataRow("duplicate-yaml-key", "Invalid Node runtime workflow YAML.")]
    [DataRow("missing-workflow", "Missing committed Node runtime workflow.")]
    [DataRow("missing-mise", "Missing committed runtime authority: mise.toml")]
    [DataRow("missing-lock", "Missing committed runtime authority: mise.lock")]
    [DataRow("tool-source", Authority)]
    [DataRow("tool-inactive", Authority)]
    [DataRow("node-request", Authority)]
    [DataRow("node-version", Authority)]
    [DataRow("pnpm-floating", Authority)]
    [DataRow("duplicate-tool", "Unresolved native runtime tool: node")]
    public void UnsupportedRuntimeFactsCannotBecomeAPlan(string defect, string message)
    {
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            Read(defect));
        Assert.AreEqual(message, error.Message);
    }

    private const string Member = Source + "/package.json";
    private const string Matrix = "Unsupported complete Node runtime matrix.";
    private const string Authority = "Native runtime tool is outside committed authority.";
    private const string InvalidSchedule = "Invalid committed Node release schedule.";
    private const string Packaging = "The configured packaging runtime is absent from the matrix.";

    // Committed declaration, schedule, engines and matrix defects stop before any native
    // query; tool defects stop at the query that answered them.
    [TestMethod]
    [DataRow("missing-mise", 0, "Missing committed runtime authority: mise.toml")]
    [DataRow("literal-matrix", 0, Matrix)]
    [DataRow("missing-schedule", 0, "Missing committed Node release schedule: " +
        NodeRuntimeReader.Schedule)]
    [DataRow("missing-row", 0, "The Node release schedule has no row: v23")]
    [DataRow("unsupported-caret", 0, "Unsupported Node engines range: " + Member)]
    [DataRow("unreadable-manifest", 0, "Unreadable Node engines manifest: " + Member)]
    [DataRow("tool-inactive", 1, Authority)]
    [DataRow("pnpm-floating", 2, Authority)]
    [DataRow("packaging-outside", 2, Packaging)]
    public async Task UnsupportedRuntimeFactsStopTheReader(string defect, int queries,
        string message)
    {
        using var repo = await RuntimeCheckoutAsync(defect);
        GitMaterialization checkout = await BindAsync(repo);
        var commands = new List<string>();
        var reader = new NodeRuntimeReader(checkout, (args, _) =>
        {
            commands.Add(string.Join(' ', args));
            return Task.FromResult(Tool(repo.Directory, args[^1], defect));
        });

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync([Source], context.CancellationToken));
        Assert.AreEqual(message, error.Message);
        CollectionAssert.AreEqual(NativeCommands.Take(queries).ToArray(), commands);
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
    [DataRow("missing")]
    [DataRow("failed")]
    [DataRow("cancelled")]
    [DataRow("timedout")]
    [DataRow("skipped")]
    [DataRow("conflicting")]
    public async Task CollectorRequiresEveryOriginalRuntimeResult(string state)
    {
        CiPlan original = await PackagePlanAsync();
        CiPlan plan = Inputs(original.Candidate).Expand(original);
        CheckKey key = new(Source, "node/project-test-v1", "node22");
        var results = plan.Checks.Select(c =>
            new CheckResult(plan.Candidate, c.Work.Key, CheckStatus.Passed)).ToList();
        CheckResult passed = results.Single(r => r.Key == key);
        if (state != "conflicting") results.Remove(passed);
        if (state != "missing")
        {
            CheckStatus status = state switch
            {
                "cancelled" => CheckStatus.Cancelled,
                "timedout" => CheckStatus.TimedOut,
                "skipped" => CheckStatus.Skipped,
                _ => CheckStatus.Failed,
            };
            results.Add(passed with { Status = status });
        }

        CiOutcome outcome = ResultCollector.Collect(plan, [.. results]);

        Assert.IsFalse(outcome.Satisfied);
        Assert.HasCount(7, outcome.Checks);
        Assert.AreEqual(state, outcome.Checks.Single(c => c.Key == key).Status);
        Assert.HasCount(6, outcome.Checks.Where(c => c.Status == "passed"));
    }

    [TestMethod]
    [DataRow("candidate")]
    [DataRow("scope")]
    [DataRow("variant")]
    [DataRow("check")]
    public async Task ProjectionRejectsAnUnsupportedParentPlan(string defect)
    {
        CiPlan plan = await PackagePlanAsync();
        plan = defect switch
        {
            "candidate" => plan with { Candidate = NodeScenario.Basis },
            "scope" => plan with { Scope = "unknown" },
            _ => plan with
            {
                Checks = plan.Checks.Select(c => c with
                {
                    Work = c.Work with
                    {
                        Key = c.Work.Key with
                        {
                            Variant = defect == "variant" ? "other" : c.Work.Key.Variant,
                            Check = defect == "check" ? "unknown" : c.Work.Key.Check,
                        }
                    },
                }).ToArray()
            },
        };

        Assert.ThrowsExactly<InvalidDataException>(() =>
            Inputs(NodeScenario.Candidate).Expand(plan));
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

    private static NodeRuntimeInputs Read(string defect = "", string? schedule = null,
        string root = ">=22", string member = ">=22.6.0")
    {
        string path = Path.Combine(Path.GetTempPath(), "node-runtime-checkout");
        return NodeRuntimeReader.Read(new(NodeScenario.Candidate, []), path,
            defect == "missing-workflow" ? null : WorkflowFor(defect),
            defect == "missing-schedule" ? null : schedule ?? ScheduleFor(defect),
            defect == "missing-mise" ? null : Mise, defect == "missing-lock" ? null : MiseLock,
            Manifests(defect, root, member), Tool(path, "node", defect),
            Tool(path, "pnpm", defect));
    }

    private static NodeEnginesManifest[] Manifests(string defect, string root, string member)
    {
        string? text = defect switch
        {
            "unsupported-caret" => Manifest("^22.0.0"),
            "unsupported-compound" => Manifest(">=22 <27"),
            "unsupported-whitespace" => Manifest(">= 22"),
            "unsupported-prerelease" => Manifest(">=22.0.0-rc.1"),
            "missing-range" => "{ \"engines\": { \"npm\": \">=10\" } }\n",
            "missing-engines" => Manifest(null),
            "unreadable-manifest" => null,
            "invalid-manifest" => "{ \"engines\": ",
            "duplicate-manifest-key" => "{ \"engines\": { \"node\": \">=22\", " +
                "\"node\": \">=24\" } }\n",
            "lower-above-newest" => Manifest(">=27"),
            _ => Manifest(member),
        };
        NodeEnginesManifest[] manifests = [new(Member, text), new("package.json",
            Manifest(defect == "lower-above-newest" ? ">=27" : root))];
        return defect == "missing-root-manifest" ? manifests[..1] : manifests;
    }

    private static string ScheduleFor(string defect) => defect switch
    {
        "missing-row" => Schedule().Replace("\"v23\"", "\"v123\""),
        "packaging-outside" => Schedule("26.1.0").Replace("\"v24\": { \"start\": " +
            "\"2000-01-01\", \"lts\": \"2000-06-01\"", "\"v24\": { \"start\": \"2000-01-01\""),
        "invalid-schedule-json" => Schedule()[..^4],
        "unknown-top-level-member" => Schedule(top: ",\n  \"generated\": \"2026-01-01\""),
        "unknown-schedule-member" => Schedule().Replace("\"releases\":",
            "\"source\": \"main\",\n    \"releases\":"),
        "missing-newest" => Schedule().Replace("\"newestRelease\": \"26.1.0\",\n", ""),
        "prerelease-newest" => Schedule("27.0.0-rc.1"),
        "prefixed-newest" => Schedule("v26.1.0"),
        "numeric-newest" => Schedule().Replace("\"26.1.0\"", "26"),
        "short-commit" => Schedule(commit: ScheduleCommit[..39]),
        "uppercase-commit" => Schedule(commit: ScheduleCommit.ToUpperInvariant()),
        "duplicate-key" => Schedule(extra: ",\n      \"v24\": { \"start\": \"2000-01-01\" }"),
        "duplicate-top-level-key" => Schedule(top: ",\n  \"newestRelease\": \"26.1.0\""),
        "invalid-key" => Schedule(extra: ",\n      \"node30\": { \"start\": \"2000-01-01\" }"),
        "leading-zero-key" => Schedule(extra: ",\n      \"v030\": { \"start\": \"2000-01-01\" }"),
        "non-object-row" => Schedule(extra: ",\n      \"v30\": \"2000-01-01\""),
        "non-string-lts" => Schedule(extra: ",\n      \"v30\": { \"lts\": true }"),
        "empty-lts" => Schedule(extra: ",\n      \"v30\": { \"lts\": \"\" }"),
        "lower-above-newest" => Schedule(),
        _ => Schedule(),
    };

    private static string WorkflowFor(string defect) => defect switch
    {
        "literal-matrix" => Workflow.Replace(NodeRuntimeReader.MatrixConsumer,
            "[22.x, 24.x, 26.x]"),
        "other-expression" => Workflow.Replace("node_versions", "retained_directories"),
        "matrix-include" => Workflow + "        include: []\n",
        "duplicate-yaml-key" => Workflow + "        node-version: [24.x, 22.x]\n",
        _ => Workflow,
    };

    private static NodeRuntimeInputs Inputs(string revision) => new(revision,
        [new("node22", "22.x", null), new("node24", "24.x", "24.21.0")], "12.8.2",
        [NodeRuntimeReader.Schedule, "mise.toml", "mise.lock"], "node24", ScheduleCommit,
        "26.1.0", 22);

    private static Task<CiPlan> PackagePlanAsync()
    {
        NodeRevisionInputs At(string revision) => NodeScenario.Units(NodeScenario.Inputs(revision,
            NodeScenario.Project(Source)), NodeScenario.Unit("fixture", ("npm", Source)));
        return NodeScenario.Plan(At(NodeScenario.Basis), At(NodeScenario.Candidate),
            Source + "/index.js");
    }

    private async Task<GitFixture> RuntimeCheckoutAsync(string defect = "")
    {
        GitFixture repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync(NodeRuntimeReader.Workflow, WorkflowFor(defect));
        if (defect != "missing-schedule")
            await repo.SetAsync(NodeRuntimeReader.Schedule, ScheduleFor(defect));
        if (defect != "missing-mise") await repo.SetAsync("mise.toml", Mise);
        await repo.SetAsync("mise.lock", MiseLock);
        foreach (NodeEnginesManifest manifest in Manifests(defect, ">=22", ">=22.6.0"))
            if (manifest.Text is not null) await repo.SetAsync(manifest.Path, manifest.Text);
        string commit = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", commit);
        return repo;
    }

    private async Task<GitMaterialization> BindAsync(GitFixture repo) =>
        await GitMaterialization.BindAsync(repo.Directory,
            await new GitReader(repo.Directory).ReadAsync("HEAD", context.CancellationToken),
            context.CancellationToken);

    internal static string Tool(string root, string name, string defect = "")
    {
        string version = name == "node" ? "24.21.0" : "12.8.2";
        string requested = name == "node" ? "24" : version;
        if (name == "node" && defect == "node-request") requested = "22";
        if (name == "node" && defect == "node-version") version = "22.20.0";
        if (name == "pnpm" && defect == "pnpm-floating") requested = "latest";
        object tool = new
        {
            version,
            requested_version = requested,
            active = defect != "tool-inactive",
            source = new
            {
                type = "mise.toml",
                path = Path.Combine(root,
                defect == "tool-source" ? "other.toml" : "mise.toml")
            },
        };
        return JsonSerializer.Serialize(defect == "duplicate-tool" ? new[] { tool, tool } : [tool]);
    }
}
