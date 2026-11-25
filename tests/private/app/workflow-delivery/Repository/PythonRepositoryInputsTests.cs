using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonRepositoryInputsTests
{
    private const string AzureAuthPytest = "src/private/app/azureauth-credprovider/python/tests";
    private const string NbgvPytest = "src/public/lib/nbgv-python/tests";
    private const string RuntimePytest = "tests/eng/test_python_runtime.py";
    private static PythonProjectDependencies Member(string path) => new(path, path, [], []);

    private static (GitRevision Revision, PythonProjectDependencies[] Projects,
        PythonPytestTargetSet Targets) Endpoint(string[] targets, string[] paths,
            string revision = "same", PythonProjectDependencies[]? members = null)
    {
        PythonProjectDependencies[] projects = members ?? [Member(".")];
        string[] inputs = projects.Select(project => project.Directory == "." ?
            "pyproject.toml" : project.Directory + "/pyproject.toml")
            .Concat(targets.Select(target => target.EndsWith(".py", StringComparison.Ordinal) ?
                target : target + "/test_one.py")).Concat(paths)
            .Distinct(StringComparer.Ordinal).ToArray();
        var git = new GitRevision(revision, inputs.Select(path =>
            new GitEntry(path, "100644", "blob")).ToArray());
        return (git, projects, PythonPytestTargets.Bind(new("pyproject.toml", targets, []),
            git, projects));
    }

    private static ProjectFacts Package(string id = "distribution", string directory = "src/pkg") =>
        new(id, directory, [], [], null, "python/package-v1",
            [new(new(id, "python/distribution-v1", "default"), [], "ubuntu-latest", true, [])]);

    private static PythonFactsEndpoint Facts((GitRevision Revision,
        PythonProjectDependencies[] Projects, PythonPytestTargetSet Targets) endpoint,
        ProjectFacts[]? packages = null) => new(endpoint.Revision, endpoint.Projects,
            new(endpoint.Targets.ConfigurationFile,
                endpoint.Targets.Targets.Select(target => target.Target).ToArray(), []),
            new("ubuntu-latest", "default", []), packages ?? [], PythonRepositoryInputs.Map(
                endpoint.Revision, endpoint.Projects, endpoint.Targets, packages ?? []));

    private static void Owners(PythonInputBinding[] bindings, string path, params string[] expected)
    {
        PythonInputBinding binding = Assert.ContainsSingle(
            bindings.Where(item => item.Path == path));
        CollectionAssert.AreEqual(expected.Order(StringComparer.Ordinal).ToArray(), binding.Owners);
        Assert.IsEmpty(binding.Members);
    }

    private static CiPlan Plan(PythonFactsEndpoint basis, PythonFactsEndpoint candidate,
        string path, bool full = false)
    {
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(basis, candidate,
            [path], full, PythonRepositoryInputs.Pair(basis, candidate, [path]));
        return ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons);
    }

    [TestMethod]
    public void AttributesSelectOnlyConfiguredDotnetDependentChecks()
    {
        const string path = ".gitattributes";
        string[] targets = [NbgvPytest, AzureAuthPytest, "tests/unrelated.py"];
        CiPlan plan = Plan(Facts(Endpoint(targets, [path], "before")),
            Facts(Endpoint(targets, [path], "after")), path);

        CheckKey[] expected =
        [
            new("pytest:" + NbgvPytest, "python/pytest-v1", "default"),
            new("pytest:" + AzureAuthPytest, "python/pytest-v1", "default")
        ];
        CollectionAssert.AreEquivalent(expected, plan.Checks.Select(check => check.Work.Key)
            .ToArray());
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.IsTrue(check.Work.Required);
            string owner = check.Work.Key.Target;
            CollectionAssert.AreEquivalent(
                (SelectionReason[])[new(path, "before", owner), new(path, "after", owner)],
                check.Reasons);
        }
    }

    [TestMethod]
    [DataRow("src/.gitattributes", true)]
    [DataRow("src/private/.gitattributes", true)]
    [DataRow("tests/.gitattributes", true)]
    [DataRow("src/private/app/.gitattributes", true)]
    [DataRow("unrelated/.gitattributes", false)]
    [DataRow("src/public/.gitattributes", false)]
    [DataRow("src/private/app/workflow-delivery-other/.gitattributes", false)]
    public void AncestorAttributesRespectActualConsumerRoots(string path, bool consumed)
    {
        PythonFactsEndpoint endpoint = Facts(Endpoint([AzureAuthPytest, "tests/unrelated.py"],
            [path]));
        if (consumed)
            Owners(endpoint.Inputs, path, "pytest:" + AzureAuthPytest);
        else
            Assert.DoesNotContain(path, endpoint.Inputs.Select(input => input.Path));
    }

    [TestMethod]
    [DataRow(".gitattributes", AzureAuthPytest, true)]
    [DataRow(".gitattributes", AzureAuthPytest, false)]
    [DataRow("renovate.json", RuntimePytest, true)]
    [DataRow("src/public/lib/nbgv-python/src/removed.py",
        "tests/private/app/workflow-delivery/Native/Python", true)]
    public void OneSidedInputSelectsIntroducedCheckWithItsOriginalRevision(string path,
        string target, bool inputAtBasis)
    {
        PythonFactsEndpoint basis = Facts(Endpoint(["tests/unrelated.py"],
            inputAtBasis ? [path] : [], "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint([target, "tests/unrelated.py"],
            inputAtBasis ? [] : [path], "after"));
        PlannedCheck check = Assert.ContainsSingle(Plan(basis, candidate, path).Checks);
        Assert.AreEqual(new CheckKey("pytest:" + target, "python/pytest-v1", "default"),
            check.Work.Key);
        Assert.AreEqual(new SelectionReason(path, inputAtBasis ? "before" : "after",
            "pytest:" + target),
            Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    public void PythonReleaseCallerSelectsBothOfItsDeclaredPytestTargets()
    {
        const string path = ".github/workflows/workflow-delivery-v3-python-release.yml";
        const string callerTarget = "tests/eng/test_python_release_caller.py";
        string[] targets = [callerTarget, RuntimePytest, "tests/unrelated.py"];
        PythonFactsEndpoint basis = Facts(Endpoint(
            targets, [path], "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint(targets, [path], "after"));

        CollectionAssert.AreEquivalent(
            new[] { "pytest:" + callerTarget, "pytest:" + RuntimePytest },
            Plan(basis, candidate, path).Checks.Select(check => check.Work.Key.Target).ToArray());
    }

    [TestMethod]
    public void UnknownDeletedProducerStillFailsPlanning()
    {
        const string path = "eng/scripts/unknown_producer.py";
        PythonFactsEndpoint basis = Facts(Endpoint(["tests/unrelated.py"], [path], "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint(["tests/unrelated.py"], [], "after"));
        Assert.ThrowsExactly<InvalidDataException>(() => Plan(basis, candidate, path));
    }

    [TestMethod]
    public void SharedInputsReachOnlyActualOwners()
    {
        var endpoint = Endpoint(["src/tableless/tests", "tests/standalone.py"],
            ["uv.lock", "tests/other.py"], members: [Member("."), Member("src/tableless")]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        foreach (string path in new[] { "uv.lock", "mise.toml", "pytest.ini", "uv.toml",
                     ".github/workflows/ci.yml", "eng/scripts/test-python.sh",
                     "eng/scripts/locked_tool_versions.py",
                     "eng/dotnet-frameworks/mise.toml", "eng/dotnet-frameworks/mise.lock",
                     "src/private/app/workflow-delivery/CI/Callers/PythonCiCaller.cs" })
            Owners(bindings, path, "pytest:src/tableless/tests", "pytest:tests/standalone.py");
        Assert.DoesNotContain("tests/other.py", bindings.Select(binding => binding.Path));
        Assert.IsNull(endpoint.Targets.Targets.Single(target =>
            target.Target == "tests/standalone.py").Member);
    }

    [TestMethod]
    public void PackagePreparationExcludesPytestOnlyInputs()
    {
        var endpoint = Endpoint(["src/pkg/tests"], [], members: [Member("."), Member("src/pkg")]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, [Package()]);
        Owners(bindings, "uv.lock", "distribution", "pytest:src/pkg/tests");
        Owners(bindings, "eng/scripts/sync_python_version.py",
            "distribution", "pytest:src/pkg/tests");
        Owners(bindings, "src/private/app/workflow-delivery/CI/Callers/PythonCiCaller.cs",
            "distribution", "pytest:src/pkg/tests");
        Owners(bindings, "pytest.ini", "pytest:src/pkg/tests");
        Owners(bindings, "conftest.py", "pytest:src/pkg/tests");
        Owners(bindings, "eng/scripts/test-python.sh", "pytest:src/pkg/tests");
    }

    [TestMethod]
    [DataRow("tests/private/app/workflow-delivery/Native/Python",
        "src/private/app/workflow-delivery/Native/Python/native/main.rs")]
    [DataRow("tests/private/app/workflow-delivery/Native/Python",
        "src/public/lib/nbgv-python/src/nbgv_python/config.py")]
    [DataRow("src/private/app/azureauth-credprovider/tests",
        "tests/private/app/azureauth-credprovider/Interop.cs")]
    [DataRow("src/private/app/azureauth-credprovider/tests",
        "eng/scripts/azureauth-credprovider/verify.py")]
    [DataRow("src/private/app/azureauth-credprovider/tests",
        "src/public/lib/nbgv-python/src/nbgv_python/plugin.py")]
    [DataRow("src/private/app/azureauth-credprovider/tests",
        "src/private/Directory.Packages.props")]
    [DataRow("src/public/lib/nbgv-python/tests", "global.json")]
    [DataRow("tests/eng/test_general_ci_workflow.py", "tests/eng/ci_workflow_shell.py")]
    [DataRow("tests/eng/test_general_ci_workflow.py",
        ".github/workflows/workflow-delivery-v3-nuget-reproducibility.yml")]
    [DataRow("tests/eng/test_general_ci_workflow.py", ".github/workflows/release-buddy.yml")]
    [DataRow("tests/eng/test_general_ci_workflow.py",
        ".github/workflows/workflow-delivery-v3-npm-release.yml")]
    [DataRow("tests/eng/test_typos_config.py", ".typos.toml")]
    [DataRow("tests/eng/test_other.py", "eng/scripts/workflow_delivery_v3_hk.py")]
    [DataRow("tests/eng/test_node_release_schedule.py",
        "eng/scripts/update_node_release_schedule.py")]
    [DataRow("tests/eng/test_node_release_schedule.py", "eng/node-release-schedule.json")]
    [DataRow("tests/eng/test_node_release_schedule.py", ".github/renovate/global.cjs")]
    [DataRow("tests/eng/test_python_runtime.py",
        ".github/workflows/workflow-delivery-v3-npm-release.yml")]
    [DataRow("tests/eng/test_python_runtime.py",
        ".github/workflows/workflow-delivery-v3-python-release.yml")]
    [DataRow("tests/eng/test_python_runtime.py", ".github/workflows/renovate.yml")]
    [DataRow("tests/eng/test_python_runtime.py", ".github/workflows/codeql.yml")]
    [DataRow("tests/eng/test_python_runtime.py", "renovate.json")]
    [DataRow("tests/eng/test_python_runtime.py",
        ".github/workflows/workflow-delivery-v3-ruby-release.yml")]
    [DataRow("tests/eng/test_general_ci_workflow.py", "eng/scripts/hk_scope_drift.py")]
    [DataRow("tests/eng/test_python_runtime.py", "global.json")]
    [DataRow("tests/eng/test_python_release_caller.py", "global.json")]
    [DataRow("tests/eng/test_browser_store_release_caller.py",
        ".github/workflows/workflow-delivery-v3-browser-store-release.yml")]
    [DataRow("tests/eng/test_release_batch_dispatch.py",
        "eng/scripts/release_batch_dispatch.py")]
    [DataRow("tests/eng/test_release_batch_dispatch.py",
        ".github/workflows/workflow-delivery-v3-ruby-release.yml")]
    [DataRow("tests/eng/test_release_batch_dispatch.py",
        ".github/workflows/workflow-delivery-v3-nuget-release.yml")]
    public void SpecialInputsReachTheirActualTargets(string target, string path)
    {
        var endpoint = Endpoint([target, "tests/unrelated.py"], [path, "docs/unrelated.md"]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        Owners(bindings, path, "pytest:" + target);
        Assert.DoesNotContain("docs/unrelated.md", bindings.Select(binding => binding.Path));
    }

    [TestMethod]
    [DataRow("tests/eng/test_typos_config.py", ".typos.toml.backup")]
    [DataRow("tests/eng/test_python_runtime.py", ".github/workflows/README.md")]
    [DataRow("tests/eng/test_python_runtime.py", ".github/workflows/nested/caller.yml")]
    [DataRow("tests/eng/test_general_ci_workflow.py",
        ".github/workflows/workflow-delivery-v3-nuget-release.yml")]
    [DataRow("tests/private/app/workflow-delivery/Native/Python",
        "src/public/lib/nbgv-python-other/source.py")]
    [DataRow("src/private/app/azureauth-credprovider/tests", "src/other/Directory.Build.props")]
    public void NeighboringInputsDoNotSelectSpecialTargets(string target, string path)
    {
        var endpoint = Endpoint([target], [path]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        Assert.DoesNotContain(path, bindings.Select(binding => binding.Path));
    }

    [TestMethod]
    [DataRow("eng/scripts/update_node_mise_lock.py", RuntimePytest)]
    [DataRow("eng/scripts/release_batch_dispatch.py", "tests/eng/test_release_batch_dispatch.py")]
    [DataRow(".github/workflows/workflow-delivery-v3-nuget-reproducibility.yml",
        "tests/eng/test_general_ci_workflow.py")]
    public void DeletedInputStillSelectsSurvivingPytestTargetAtItsBasis(string path, string target)
    {
        PythonFactsEndpoint basis = Facts(Endpoint([target, "tests/unrelated.py"], [path],
            "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint([target, "tests/unrelated.py"], [],
            "after"));
        PlannedCheck selected = Assert.ContainsSingle(Plan(basis, candidate, path).Checks);
        Assert.AreEqual("pytest:" + target, selected.Work.Key.Target);
        Assert.AreEqual(new SelectionReason(path, "before", "pytest:" + target),
            Assert.ContainsSingle(selected.Reasons));
    }

    [TestMethod]
    [DataRow("renovate.json")]
    [DataRow(".github/workflow-delivery/native/python-requests.json")]
    [DataRow(".github/workflow-delivery/native/other.json")]
    [DataRow("other.json")]
    [DataRow(".gitattributes")]
    [DataRow("src/.gitattributes")]
    [DataRow("eng/scripts/sync_python_build_backend.py")]
    [DataRow("eng/scripts/workflow_delivery_v3_prepare_static_reference.py")]
    [DataRow("eng/scripts/workflow_delivery_v3_run_created_epoch.py")]
    [DataRow("eng/scripts/workflow_delivery_v3_static_reference.py")]
    public void FiniteInputsWithoutConfiguredConsumersRemainUnresolved(string path)
    {
        PythonFactsEndpoint endpoint = Facts(Endpoint(["tests/unrelated.py"], [path]));
        Assert.ThrowsExactly<InvalidDataException>(() => Plan(endpoint, endpoint, path));
    }

    [TestMethod]
    public void AbsentSpecialTargetsAreNotInvented()
    {
        var endpoint = Endpoint(["tests/standalone.py"], [".typos.toml",
            "src/public/lib/nbgv-python/source.py",
            "src/private/app/workflow-delivery/CI/Callers/NodeCiCaller.cs",
            "eng/scripts/sync_python_version.py",
            "tests/private/app/workflow-delivery/fixtures/products/" +
                "hcoona-release-smoke-ruby/hcoona-release-smoke-ruby.gemspec",
            "src/public/lib/three-workflow-delivery-v3/" +
                "src/three_workflow_delivery_v3/_ruby_helper.rb"]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        Assert.AreEqual("pytest:tests/standalone.py", Assert.ContainsSingle(bindings
            .SelectMany(binding => binding.Owners).Distinct(StringComparer.Ordinal)));
        Assert.DoesNotContain(".typos.toml", bindings.Select(binding => binding.Path));
        Assert.DoesNotContain("src/private/app/workflow-delivery/CI/Callers/NodeCiCaller.cs",
            bindings.Select(binding => binding.Path));
        Assert.DoesNotContain("renovate.json",
            bindings.Select(binding => binding.Path));
        Assert.DoesNotContain("tests/private/app/workflow-delivery/fixtures/products/" +
                "hcoona-release-smoke-ruby/" +
            "hcoona-release-smoke-ruby.gemspec", bindings.Select(binding => binding.Path));
        Assert.DoesNotContain("src/public/lib/three-workflow-delivery-v3/" +
            "src/three_workflow_delivery_v3/_ruby_helper.rb",
            bindings.Select(binding => binding.Path));
    }

    [TestMethod]
    public void InputsAreDeterministicAndDoNotCreateProducers()
    {
        var endpoint = Endpoint(["tests/z.py", "tests/a.py"], ["uv.lock"]);
        PythonInputBinding[] first = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        PythonInputBinding[] second = PythonRepositoryInputs.Map(endpoint.Revision with
        { Entries = endpoint.Revision.Entries.Reverse().ToArray() }, endpoint.Projects,
            endpoint.Targets with { Targets = endpoint.Targets.Targets.Reverse().ToArray() }, []);
        CollectionAssert.AreEqual(first.Select(binding => binding.Path).ToArray(),
            second.Select(binding => binding.Path).ToArray());
        CollectionAssert.AreEqual(
            first.Select(binding => string.Join(",", binding.Owners)).ToArray(),
            second.Select(binding => string.Join(",", binding.Owners)).ToArray());
        foreach (PythonInputBinding binding in first) Assert.IsEmpty(binding.Members);
        Owners(first, "uv.lock", "pytest:tests/a.py", "pytest:tests/z.py");
    }

    [TestMethod]
    [DataRow("revision")]
    [DataRow("target-member")]
    [DataRow("target-inputs")]
    [DataRow("duplicate-target")]
    [DataRow("duplicate-package")]
    [DataRow("foreign-package")]
    [DataRow("unresolved-check")]
    [DataRow("root-package")]
    [DataRow("nonregular")]
    public void InvalidSuppliedScopeFails(string scenario)
    {
        var endpoint = Endpoint(["src/pkg/tests"], ["uv.lock"],
            members: [Member("."), Member("src/pkg")]);
        GitRevision revision = endpoint.Revision;
        PythonPytestTargetSet targets = endpoint.Targets;
        ProjectFacts[] packages = [];
        switch (scenario)
        {
            case "revision": targets = targets with { Revision = "foreign" }; break;
            case "target-member":
                targets = targets with
                { Targets = [targets.Targets[0] with { Member = null }] }; break;
            case "target-inputs":
                targets = targets with
                { Targets = [targets.Targets[0] with { Inputs = ["uv.lock"] }] }; break;
            case "duplicate-target":
                targets = targets with
                { Targets = [targets.Targets[0], targets.Targets[0]] }; break;
            case "duplicate-package": packages = [Package(), Package()]; break;
            case "foreign-package": packages = [Package(directory: "src/foreign")]; break;
            case "unresolved-check": packages = [Package() with { Checks = [] }]; break;
            case "root-package": packages = [Package(directory: ".")]; break;
            case "nonregular":
                revision = revision with
                {
                    Entries = revision.Entries.Select(entry =>
                    entry.Path == "uv.lock" ? entry with { Mode = "120000" } : entry).ToArray()
                }; break;
            default: Assert.Fail("Unknown supplied scope case."); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => PythonRepositoryInputs.Map(revision,
            endpoint.Projects, targets, packages));
    }

    [TestMethod]
    public void NewWorkflowInputDoesNotReviveARemovedPytestTarget()
    {
        const string path = ".github/workflows/workflow-delivery-v3-nuget-reproducibility.yml";
        const string target = "tests/eng/test_general_ci_workflow.py";
        PythonFactsEndpoint basis = Facts(Endpoint([target, "tests/unrelated.py"], [], "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint(["tests/unrelated.py"], [path], "after"));
        Assert.IsEmpty(Plan(basis, candidate, path).Checks);
    }

    [TestMethod]
    public void PairedSharedInputSelectsCompleteCandidateChecks()
    {
        PythonFactsEndpoint basis = Facts(Endpoint(["tests/before.py"], ["uv.lock"], "before"));
        ProjectFacts package = Package() with
        {
            Checks = [
                new(new("distribution", "python/distribution-v1", "wheel"), [],
                    "ubuntu-latest", true, []),
                new(new("distribution", "python/distribution-v1", "sdist"), [],
                    "ubuntu-latest", true, [])]
        };
        PythonFactsEndpoint candidate = Facts(Endpoint(["tests/after.py"], [], "after",
            [Member("."), Member("src/pkg")]), [package]);
        CiPlan plan = Plan(basis, candidate, "uv.lock");
        Assert.HasCount(3, plan.Checks);
        PlannedCheck[] distribution = plan.Checks.Where(check =>
            check.Work.Key.Target == "distribution").ToArray();
        Assert.HasCount(2, distribution);
        Assert.Contains("wheel", distribution.Select(check => check.Work.Key.Variant));
        Assert.Contains("sdist", distribution.Select(check => check.Work.Key.Variant));
        foreach (PlannedCheck check in distribution)
            Assert.AreEqual(new SelectionReason("uv.lock", "before", "distribution"),
                Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    public void PairedAssociationsKeepKnownNonconsumerAndUnknownDistinct()
    {
        string path = "src/input/source.py";
        PythonFactsEndpoint endpoint = Facts(Endpoint(["tests/standalone.py"],
            [path, "docs/unknown.md"], members: [Member("."), Member("src/input")]));
        Assert.IsEmpty(Plan(endpoint, endpoint, path).Checks);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            Plan(endpoint, endpoint, "docs/unknown.md", full: true));
    }

    [TestMethod]
    [DataRow("owner")]
    [DataRow("revision")]
    [DataRow("uncommitted-revision")]
    [DataRow("unchanged-path")]
    [DataRow("duplicate")]
    [DataRow("nonregular")]
    public void InvalidPairedReasonsCannotCertifyScope(string scenario)
    {
        PythonFactsEndpoint basis = Facts(Endpoint(["tests/standalone.py"],
            ["docs/unknown.md", "docs/unchanged.md"], "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint(["tests/standalone.py"], [], "after"));
        var reason = new SelectionReason("docs/unknown.md", "before", "pytest:tests/standalone.py");
        reason = scenario switch
        {
            "owner" => reason with { Project = "foreign" },
            "revision" => reason with { Revision = "foreign" },
            "uncommitted-revision" => reason with { Revision = "after" },
            "unchanged-path" => reason with { Path = "docs/unchanged.md" },
            _ => reason
        };
        if (scenario == "nonregular") basis = basis with
        {
            Revision = basis.Revision with
            {
                Entries = basis.Revision.Entries.Select(entry => entry.Path == reason.Path ?
                    entry with { Mode = "120000" } : entry).ToArray()
            }
        };
        SelectionReason[] supplied = scenario == "duplicate" ? [reason, reason] : [reason];
        Assert.ThrowsExactly<InvalidDataException>(() => PythonFactsAssembler.Assemble(
            basis, candidate, ["docs/unknown.md"], false, supplied));
    }

    [TestMethod]
    [DataRow("absent")]
    [DataRow("duplicate")]
    [DataRow("escape")]
    [DataRow("nonregular")]
    public void InvalidPairCoordinatesFail(string scenario)
    {
        PythonFactsEndpoint endpoint = Facts(Endpoint(["tests/standalone.py"], [".typos.toml"]));
        string[] paths = scenario switch
        {
            "absent" => ["missing.py"],
            "duplicate" => [".typos.toml", ".typos.toml"],
            "escape" => ["../outside.py"],
            _ => [".typos.toml"]
        };
        if (scenario == "nonregular") endpoint = endpoint with
        {
            Revision = endpoint.Revision with
            {
                Entries = endpoint.Revision.Entries.Select(entry => entry.Path == ".typos.toml" ?
                    entry with { ObjectType = "tree" } : entry).ToArray()
            }
        };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonRepositoryInputs.Pair(endpoint, endpoint, paths));
    }

    [TestMethod]
    [DataRow("src/private/app/workflow-delivery/README.md")]
    [DataRow("src/private/app/workflow-delivery/docs/high-level-design.md")]
    public void InertControlDocumentsDoNotSelectPythonWork(string path)
    {
        PythonFactsEndpoint basis = Facts(Endpoint(["tests/unrelated.py"], [path], "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint(["tests/unrelated.py"], [], "after"));
        Assert.IsEmpty(Plan(basis, candidate, path).Checks);
    }

    [TestMethod]
    public void ActualDocumentConsumerTakesPrecedenceOverInertControlClassification()
    {
        const string path = "src/private/app/workflow-delivery/docs/native-input.json";
        const string target = "tests/unrelated.py";
        PythonFactsEndpoint basis = Facts(Endpoint([target], [path], "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint([target], [path], "after"));
        candidate = candidate with
        {
            Inputs = [.. candidate.Inputs, new(path, [], ["pytest:" + target])]
        };
        PlannedCheck check = Assert.ContainsSingle(Plan(basis, candidate, path).Checks);
        Assert.AreEqual("pytest:" + target, check.Work.Key.Target);
    }

    [TestMethod]
    [DataRow("src/private/app/workflow-delivery/README-other.md")]
    [DataRow("src/private/app/workflow-delivery/docs-other/input.txt")]
    public void DocumentationExclusionCannotCertifyNearbyUnmappedInputs(string path)
    {
        PythonFactsEndpoint endpoint = Facts(Endpoint(["tests/unrelated.py"], [path]));
        Assert.ThrowsExactly<InvalidDataException>(() => Plan(endpoint, endpoint, path));
    }
}
