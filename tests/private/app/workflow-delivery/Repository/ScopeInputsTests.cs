using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ScopeInputsTests
{
    private const string Unrelated = "docs/guide.md";
    private const string Catalog = "docs/governance/record-families.yaml";

    [TestMethod]
    public void OwnedUnrelatedChangesRemainKnownWithoutSelectingWork()
    {
        var scenario = new Scenario(Unrelated);

        PlanRequest request = scenario.Read().Bind(scenario.Basis, scenario.Candidate);
        CiPlan plan = ImpactPlanner.Plan(request);

        Assert.IsEmpty(plan.Checks);
        CollectionAssert.AreEqual(new[] { Unrelated }, request.Basis.UnaffectedPaths);
        CollectionAssert.AreEqual(new[] { Unrelated }, request.Candidate.UnaffectedPaths);
        Assert.AreEqual(NodeScenario.Basis, plan.Comparison);
        Assert.AreEqual(NodeScenario.Candidate, plan.Candidate);
    }

    [TestMethod]
    [DataRow("src/a/index.js")]
    [DataRow("pnpm-lock.yaml")]
    public void NativeConsumersSupplyResponsibilityAndKeepSelection(string path)
    {
        var scenario = new Scenario(path);
        Assert.IsEmpty(scenario.Read().Basis.Single().Reasons);

        PlanRequest request = scenario.Read().Bind(scenario.Basis, scenario.Candidate);
        CiPlan plan = ImpactPlanner.Plan(request);

        Assert.IsEmpty(request.Basis.UnaffectedPaths);
        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.Contains(new SelectionReason(path, NodeScenario.Basis, "src/a"), check.Reasons);
            Assert.Contains(new SelectionReason(path, NodeScenario.Candidate, "src/a"),
                check.Reasons);
        }
    }

    [TestMethod]
    public void RemovedConsumptionRetainsBasisSelectionReasons()
    {
        var scenario = new Scenario(Unrelated);
        RepositoryFacts basis = scenario.Basis with
        {
            SharedInputs = [.. scenario.Basis.SharedInputs, new(Unrelated, ["src/a"])],
        };
        scenario.Row("basis")["reasons"] = new JsonArray();

        CiPlan plan = ImpactPlanner.Plan(scenario.Read().Bind(basis, scenario.Candidate));

        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
            Assert.AreEqual(new SelectionReason(Unrelated, NodeScenario.Basis, "src/a"),
                Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    [DataRow("basis")]
    [DataRow("candidate")]
    public void UnknownPresentEndpointCannotBorrowOtherEndpointResponsibility(string endpoint)
    {
        var scenario = new Scenario(Unrelated);
        scenario.Row(endpoint)["reasons"] = new JsonArray();

        Assert.ThrowsExactly<InvalidDataException>(() => scenario.Read().Bind(scenario.Basis,
            scenario.Candidate));
    }

    [TestMethod]
    public void ConfirmedAbsenceDoesNotRequireResponsibility()
    {
        var scenario = new Scenario(Unrelated);
        GitRevision basisRevision = scenario.Native.Basis with
        {
            Entries = scenario.Native.Basis.Entries.Where(entry => entry.Path != Unrelated)
                .ToArray(),
        };
        scenario.Native = scenario.Native with { Basis = basisRevision };
        scenario.Row("basis")["present"] = false;
        scenario.Row("basis")["mode"] = null;
        scenario.Row("basis")["reasons"] = new JsonArray();

        CiPlan plan = ImpactPlanner.Plan(scenario.Read().Bind(scenario.Basis, scenario.Candidate));

        Assert.IsEmpty(plan.Checks);
        Assert.AreEqual(NodeScenario.Basis, plan.Comparison);
        Assert.AreEqual(NodeScenario.Candidate, plan.Candidate);
    }

    [TestMethod]
    [DataRow("error")]
    [DataRow("unresolved-relation")]
    [DataRow("different-revision")]
    public void IncompleteNativeFactsCannotBecomeUnaffectedPaths(string defect)
    {
        var scenario = new Scenario(Unrelated);
        RepositoryFacts basis = defect switch
        {
            "error" => scenario.Basis with { Errors = ["failed native read"] },
            "unresolved-relation" => scenario.Basis with
            {
                Projects = [scenario.Basis.Projects.Single() with { Dependencies = ["unknown"] }],
            },
            "different-revision" => scenario.Basis with { Revision = NodeScenario.Candidate },
            _ => throw new InvalidOperationException("Unknown incomplete native fixture."),
        };

        Assert.ThrowsExactly<InvalidDataException>(() => scenario.Read().Bind(basis,
            scenario.Candidate));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EmptyAndFullComparisonsRetainTheirNativeSubject(bool full)
    {
        var scenario = new Scenario();
        RepositoryFacts basis = full ? scenario.Candidate : scenario.Basis;
        if (full)
        {
            scenario.Native = scenario.Native with { Basis = scenario.Native.Candidate };
            scenario.Payload["base"] = NodeScenario.Candidate;
            scenario.Payload["full"] = true;
            scenario.Endpoint("basis")["revision"] = NodeScenario.Candidate;
        }

        CiPlan plan = ImpactPlanner.Plan(scenario.Read(full).Bind(basis, scenario.Candidate));

        Assert.AreEqual(full ? NodeScenario.Candidate : NodeScenario.Basis, plan.Comparison);
        Assert.AreEqual(NodeScenario.Candidate, plan.Candidate);
        Assert.HasCount(full ? 2 : 0, plan.Checks);
    }

    [TestMethod]
    [DataRow("base")]
    [DataRow("candidate")]
    [DataRow("full")]
    [DataRow("full-type")]
    [DataRow("missing-coordinate")]
    [DataRow("extra-coordinate")]
    [DataRow("duplicate-coordinate")]
    [DataRow("basis-revision")]
    [DataRow("candidate-revision")]
    [DataRow("presence")]
    [DataRow("mode")]
    [DataRow("missing-row")]
    [DataRow("extra-row")]
    [DataRow("duplicate-row")]
    [DataRow("absent-with-reasons")]
    [DataRow("missing-field")]
    [DataRow("duplicate-field")]
    [DataRow("unrepresentable-coordinate")]
    public void ScopePayloadMustMatchTheNativeComparison(string defect)
    {
        var scenario = new Scenario(Unrelated);
        JsonArray rows = (JsonArray)scenario.Endpoint("basis")["paths"]!;
        switch (defect)
        {
            case "base": case "candidate": scenario.Payload[defect] = "wrong"; break;
            case "full": scenario.Payload["full"] = true; break;
            case "full-type": scenario.Payload["full"] = "false"; break;
            case "missing-coordinate": scenario.Payload["changed_paths"] = new JsonArray(); break;
            case "extra-coordinate":
            case "unrepresentable-coordinate":
                ((JsonArray)scenario.Payload["changed_paths"]!).Add(
                    defect == "extra-coordinate" ? "extra.txt" : "../other.txt"); break;
            case "duplicate-coordinate":
                ((JsonArray)scenario.Payload["changed_paths"]!).Add(Unrelated); break;
            case "basis-revision": scenario.Endpoint("basis")["revision"] = "wrong"; break;
            case "candidate-revision":
                scenario.Endpoint("candidate")["revision"] = "wrong";
                break;
            case "presence": scenario.Row("basis")["present"] = false; break;
            case "mode": scenario.Row("basis")["mode"] = "100755"; break;
            case "missing-row": rows.Clear(); break;
            case "extra-row":
                rows.Add(Scenario.Coordinate(scenario.Native.Basis, "src/a/index.js")); break;
            case "duplicate-row": rows.Add(scenario.Row("basis").DeepClone()); break;
            case "absent-with-reasons":
                scenario.Native = scenario.Native with
                {
                    Basis = scenario.Native.Basis with
                    {
                        Entries = scenario.Native.Basis.Entries.Where(entry =>
                            entry.Path != Unrelated).ToArray(),
                    }
                };
                scenario.Row("basis")["present"] = false;
                scenario.Row("basis")["mode"] = null; break;
            case "missing-field": scenario.Row("basis").Remove("mode"); break;
            case "duplicate-field":
                string text = scenario.Payload.ToJsonString().Replace("\"full\":false",
                    "\"full\":false,\"full\":false", StringComparison.Ordinal);
                Assert.ThrowsExactly<InvalidDataException>(() =>
                    ScopeInputs.Read(text, scenario.Native, false));
                return;
            default: Assert.Fail("Unknown scope fixture defect."); break;
        }

        Assert.ThrowsExactly<InvalidDataException>(() => scenario.Read());
    }

    [TestMethod]
    [DataRow("absent")]
    [DataRow("symlink")]
    [DataRow("empty")]
    [DataRow("duplicate")]
    public void ResponsibilityRequiresCommittedSourceCoordinates(string defect)
    {
        var scenario = new Scenario(Unrelated);
        JsonObject reason = (JsonObject)((JsonArray)scenario.Row("basis")["reasons"]!)[0]!;
        if (defect == "empty") reason["sources"] = new JsonArray();
        else if (defect == "duplicate") ((JsonArray)reason["sources"]!).Add(Catalog);
        else
            scenario.Native = scenario.Native with
            {
                Basis = scenario.Native.Basis with
                {
                    Entries = scenario.Native.Basis.Entries.Where(entry => entry.Path != Catalog)
                    .Concat(defect == "symlink" ? [new GitEntry(Catalog, "120000", "blob")] : [])
                    .ToArray(),
                }
            };

        Assert.ThrowsExactly<InvalidDataException>(() => scenario.Read());
    }

    private sealed class Scenario
    {
        internal RepositoryFacts Basis { get; }
        internal RepositoryFacts Candidate { get; }
        internal GitComparison Native { get; set; }
        internal JsonObject Payload { get; }

        internal Scenario(params string[] paths)
        {
            NodeRevisionInputs basis = Inputs(NodeScenario.Basis);
            NodeRevisionInputs candidate = Inputs(NodeScenario.Candidate);
            Basis = Facts(basis);
            Candidate = Facts(candidate);
            Native = new(basis.Revision, candidate.Revision, paths);
            Payload = new()
            {
                ["base"] = NodeScenario.Basis,
                ["candidate"] = NodeScenario.Candidate,
                ["full"] = false,
                ["changed_paths"] = new JsonArray(paths.Select(path =>
                    (JsonNode?)JsonValue.Create(path)).ToArray()),
                ["endpoint_owners"] = new JsonObject
                {
                    ["basis"] = End(basis.Revision),
                    ["candidate"] = End(candidate.Revision),
                },
                ["dotnet"] = false, // Existing aggregate fields are outside this consumer.
            };

            JsonObject End(GitRevision revision) => new()
            {
                ["revision"] = revision.Commit,
                ["paths"] = new JsonArray(paths.Select(path =>
                    (JsonNode?)Coordinate(revision, path)).ToArray()),
            };
        }

        internal JsonObject Endpoint(string name) => (JsonObject)Payload["endpoint_owners"]![name]!;
        internal JsonObject Row(string name) =>
            (JsonObject)((JsonArray)Endpoint(name)["paths"]!)[0]!;
        internal ScopeInputs Read(bool full = false) =>
            ScopeInputs.Read(Payload.ToJsonString(), Native, full);

        internal static JsonObject Coordinate(GitRevision revision, string path)
        {
            GitEntry? entry = revision.Entries.SingleOrDefault(item => item.Path == path);
            return new()
            {
                ["path"] = path,
                ["present"] = entry is not null,
                ["mode"] = entry?.Mode,
                ["reasons"] = path == Unrelated ? new JsonArray(new JsonObject
                {
                    ["owner"] = "record-system",
                    ["target"] = Unrelated,
                    ["rule"] = "record-binding:example",
                    ["sources"] = new JsonArray(Catalog),
                }) : new JsonArray(),
            };
        }

        private static NodeRevisionInputs Inputs(string revision)
        {
            NodeRevisionInputs inputs = NodeScenario.Inputs(revision,
                NodeScenario.Project("src/a"));
            return inputs with
            {
                OperationInputs = [new("pnpm-lock.yaml", ["src/a"])],
                Revision = inputs.Revision with
                {
                    Entries = [.. inputs.Revision.Entries, NodeScenario.File(Unrelated),
                        NodeScenario.File(Catalog)],
                },
            };
        }

        private static RepositoryFacts Facts(NodeRevisionInputs inputs)
        {
            RepositoryFacts facts = NodeFactsAssembler.Assemble(inputs);
            PnpmProject project = inputs.Graph.Projects.Single(item => item.Directory == "src/a");
            return facts with
            {
                Projects = [facts.Projects.Single() with
            {
                Checks = NodeQualityChecks.Expand(inputs.Quality["src/a"], NodeScenario.Scripts(
                    project)),
            } ]
            };
        }
    }
}
