using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonRepositoryInputsTests
{
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

    [TestMethod]
    [DataRow("src/private/app/workflow-delivery/Native/Ruby/facts.rb")]
    [DataRow("tests/private/app/workflow-delivery/Native/Ruby/test_facts.py")]
    public void RubyHelperInputsSelectOnlyTheirAdoptedPytestTarget(string path)
    {
        const string target = "tests/private/app/workflow-delivery/Native/Ruby";
        PythonFactsEndpoint basis = Facts(Endpoint(
            [target, "tests/unrelated.py"], [path], "before"));
        PythonFactsEndpoint candidate = Facts(Endpoint(
            [target, "tests/unrelated.py"], [path], "after"));
        SelectionReason[] reasons = PythonRepositoryInputs.Pair(basis, candidate, [path]);
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(basis, candidate,
            [path], false, reasons);
        PlannedCheck selected = Assert.ContainsSingle(
            ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons).Checks);
        Assert.AreEqual("pytest:" + target, selected.Work.Key.Target);
        Assert.IsNotEmpty(selected.Reasons);
        foreach (SelectionReason reason in selected.Reasons)
        {
            Assert.AreEqual(path, reason.Path);
            Assert.AreEqual("pytest:" + target, reason.Project);
        }
    }

    [TestMethod]
    public void SharedInputsReachOnlyActualOwners()
    {
        var endpoint = Endpoint(["src/tableless/tests", "tests/standalone.py"],
            ["uv.lock", "tests/other.py"], members: [Member("."), Member("src/tableless")]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        foreach (string path in new[] { "uv.lock", "mise.toml", "pytest.ini", "uv.toml",
                     ".github/workflows/ci.yml", "eng/scripts/run_python_tests.py",
                     "eng/scripts/run_python_ci_group.py" })
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
        Owners(bindings, "eng/scripts/run_python_ci_group.py",
            "distribution", "pytest:src/pkg/tests");
        Owners(bindings, "pytest.ini", "pytest:src/pkg/tests");
        Owners(bindings, "conftest.py", "pytest:src/pkg/tests");
        Owners(bindings, "eng/scripts/run_python_tests.py", "pytest:src/pkg/tests");
    }

    [TestMethod]
    [DataRow("tests/private/app/workflow-delivery/Native/Python",
        "src/private/app/workflow-delivery/Native/Python/native/main.rs")]
    [DataRow("tests/private/app/workflow-delivery/Native/Python",
        "src/public/lib/nbgv-python/src/nbgv_python/config.py")]
    [DataRow("src/public/lib/three-workflow-delivery-v3/tests",
        ".github/workflows/workflow-delivery-v3-python-smoke.yml")]
    [DataRow("src/public/lib/three-workflow-delivery-v3/tests",
        ".github/actions/workflow-delivery-v3-node/action.yml")]
    [DataRow("src/public/lib/three-workflow-delivery-v3/tests",
        "src/private/lib/hk/steps/Check.pkl")]
    [DataRow("src/public/lib/three-workflow-delivery-v3/tests",
        "src/private/app/workflow-delivery-v3-nuget-authority/Program.cs")]
    [DataRow("src/public/lib/three-workflow-delivery-v3/tests",
        "src/private/app/Directory.Build.props")]
    [DataRow("src/public/lib/three-workflow-delivery-v3/tests", "pnpm-lock.yaml")]
    [DataRow("src/private/app/azureauth-credprovider/tests",
        "tests/private/app/azureauth-credprovider/Interop.cs")]
    [DataRow("src/private/app/azureauth-credprovider/tests",
        "eng/scripts/azureauth-credprovider/verify.py")]
    [DataRow("src/private/app/azureauth-credprovider/tests",
        "src/public/lib/nbgv-python/src/nbgv_python/plugin.py")]
    [DataRow("src/private/app/azureauth-credprovider/tests",
        "src/private/Directory.Packages.props")]
    [DataRow("src/public/lib/nbgv-python/tests", "global.json")]
    [DataRow("tests/eng/test_typos_config.py", ".typos.toml")]
    [DataRow("tests/eng/test_run_node_ci_group.py", "eng/scripts/run_node_ci_group.py")]
    [DataRow("tests/eng/test_run_dotnet_ci_group.py", "eng/scripts/run_node_ci_group.py")]
    [DataRow("tests/eng/test_run_dotnet_ci_group.py", "eng/scripts/run_dotnet_ci_group.py")]
    [DataRow("tests/eng/test_run_python_ci_group.py", "eng/scripts/run_node_ci_group.py")]
    [DataRow("tests/eng/test_run_python_ci_group.py", "eng/scripts/run_dotnet_ci_group.py")]
    [DataRow("tests/eng/test_other.py", "eng/scripts/workflow_delivery_v3_hk.py")]
    [DataRow("tests/eng/test_legacy_release_contract.py", "src/product/three.release.yml")]
    [DataRow("tests/eng/test_legacy_release_contract.py", "src/product/three.quality.yml")]
    [DataRow("tests/eng/test_legacy_release_contract.py",
        "tests/fixtures/workflow-release-node/config.json")]
    [DataRow("tests/eng/test_legacy_release_contract.py",
        "eng/scripts/workflow_release_control.py")]
    public void SpecialInputsReachTheirActualTargets(string target, string path)
    {
        var endpoint = Endpoint([target, "tests/unrelated.py"], [path, "docs/unrelated.md"]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        Owners(bindings, path, "pytest:" + target);
        Assert.DoesNotContain("docs/unrelated.md", bindings.Select(binding => binding.Path));
    }

    [TestMethod]
    [DataRow("tests/eng/test_run_node_ci_group.py", "eng/scripts/run_dotnet_ci_group.py")]
    [DataRow("tests/eng/test_typos_config.py", ".typos.toml.backup")]
    [DataRow("tests/private/app/workflow-delivery/Native/Python",
        "src/public/lib/nbgv-python-other/source.py")]
    [DataRow("src/private/app/azureauth-credprovider/tests", "src/other/Directory.Build.props")]
    [DataRow("tests/eng/test_legacy_release_contract.py", "src/product/three.release.yaml")]
    public void NeighboringInputsDoNotSelectSpecialTargets(string target, string path)
    {
        var endpoint = Endpoint([target], [path]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        Assert.DoesNotContain(path, bindings.Select(binding => binding.Path));
    }

    [TestMethod]
    public void AbsentSpecialTargetsAreNotInvented()
    {
        var endpoint = Endpoint(["tests/standalone.py"], [".typos.toml",
            "src/public/lib/nbgv-python/source.py", "eng/scripts/run_node_ci_group.py"]);
        PythonInputBinding[] bindings = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        Assert.AreEqual("pytest:tests/standalone.py", Assert.ContainsSingle(bindings
            .SelectMany(binding => binding.Owners).Distinct(StringComparer.Ordinal)));
        Assert.DoesNotContain(".typos.toml", bindings.Select(binding => binding.Path));
        Assert.DoesNotContain("eng/scripts/run_node_ci_group.py",
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
    public void PairedDeletedInputRetainsOriginalRevisionAndSurvivingOwner()
    {
        var basis = Endpoint(["tests/eng/test_legacy_release_contract.py"],
            ["src/removed/three.release.yml"], "before");
        var candidate = Endpoint(["tests/eng/test_legacy_release_contract.py"], [], "after");
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(Facts(basis), Facts(candidate),
            ["src/removed/three.release.yml"], false);
        PlannedCheck check = Assert.ContainsSingle(ImpactPlanner.Plan(assembly.Request,
            assembly.PairedReasons).Checks);
        Assert.AreEqual("pytest:tests/eng/test_legacy_release_contract.py", check.Work.Key.Target);
        Assert.AreEqual(new SelectionReason("src/removed/three.release.yml", "before",
            "pytest:tests/eng/test_legacy_release_contract.py"),
            Assert.ContainsSingle(check.Reasons));
        Assert.IsEmpty(assembly.Request.Basis.UnaffectedPaths);
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void PairedSpecialInputsReachOppositeEndpointOwners(bool plugin, bool inputAtBasis)
    {
        string path = plugin ? "src/public/lib/nbgv-python/src/removed.py" :
            "src/removed/three.release.yml";
        string target = plugin ? "tests/private/app/workflow-delivery/Native/Python" :
            "tests/eng/test_legacy_release_contract.py";
        PythonProjectDependencies[] members = plugin ?
            [Member("."), Member("src/public/lib/nbgv-python")] : [Member(".")];
        PythonFactsEndpoint basis = Facts(Endpoint(inputAtBasis ? ["tests/unrelated.py"] :
            [target, "tests/unrelated.py"], inputAtBasis ? [path] : [], "before", members));
        PythonFactsEndpoint candidate = Facts(Endpoint(inputAtBasis ?
            [target, "tests/unrelated.py"] : ["tests/unrelated.py"],
            inputAtBasis ? [] : [path], "after", members));
        SelectionReason[] paired = PythonRepositoryInputs.Pair(basis, candidate, [path]);
        var expected = new SelectionReason(path, inputAtBasis ? "before" : "after",
            "pytest:" + target);
        Assert.AreEqual(expected, Assert.ContainsSingle(paired));
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(basis, candidate,
            [path], false, paired);
        Assert.AreEqual(expected, Assert.ContainsSingle(assembly.PairedReasons));
        Assert.IsEmpty(assembly.Request.Basis.UnaffectedPaths);
        Assert.IsEmpty(assembly.Request.Candidate.UnaffectedPaths);
        CiPlan plan = ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons);
        if (inputAtBasis)
        {
            PlannedCheck selected = Assert.ContainsSingle(plan.Checks);
            Assert.AreEqual("pytest:" + target, selected.Work.Key.Target);
            Assert.AreEqual(expected, Assert.ContainsSingle(selected.Reasons));
            Assert.DoesNotContain("pytest:" + target,
                assembly.Request.Basis.Projects.Select(project => project.Id));
        }
        else
        {
            Assert.IsEmpty(plan.Checks);
            Assert.DoesNotContain("pytest:" + target,
                assembly.Request.Candidate.Projects.Select(project => project.Id));
        }
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
        SelectionReason[] reasons = PythonRepositoryInputs.Pair(basis, candidate, ["uv.lock"]);
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(basis, candidate,
            ["uv.lock"], false, reasons);
        CiPlan plan = ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons);
        Assert.HasCount(3, plan.Checks);
        PlannedCheck[] distribution = plan.Checks.Where(check =>
            check.Work.Key.Target == "distribution").ToArray();
        Assert.HasCount(2, distribution);
        Assert.Contains("wheel", distribution.Select(check => check.Work.Key.Variant));
        Assert.Contains("sdist", distribution.Select(check => check.Work.Key.Variant));
        foreach (PlannedCheck check in distribution)
            Assert.AreEqual(new SelectionReason("uv.lock", "before", "distribution"),
                Assert.ContainsSingle(check.Reasons));
        Assert.DoesNotContain("distribution", assembly.Request.Basis.Projects.Select(p => p.Id));
        Assert.IsEmpty(assembly.Request.Basis.UnaffectedPaths);
    }

    [TestMethod]
    public void PairedAssociationsKeepKnownNonconsumerAndUnknownDistinct()
    {
        string path = "src/input/source.py";
        PythonFactsEndpoint endpoint = Facts(Endpoint(["tests/standalone.py"],
            [path, "docs/unknown.md"], members: [Member("."), Member("src/input")]));
        SelectionReason[] reasons = PythonRepositoryInputs.Pair(endpoint, endpoint, [path]);
        Assert.IsEmpty(reasons);
        PythonFactAssembly known = PythonFactsAssembler.Assemble(endpoint, endpoint,
            [path], false, reasons);
        Assert.AreEqual(path, Assert.ContainsSingle(known.Request.Basis.UnaffectedPaths));
        Assert.IsEmpty(ImpactPlanner.Plan(known.Request, known.PairedReasons).Checks);
        SelectionReason[] unknown = PythonRepositoryInputs.Pair(endpoint, endpoint,
            [path, "docs/unknown.md"]);
        Assert.IsEmpty(unknown);
        Assert.ThrowsExactly<InvalidDataException>(() => PythonFactsAssembler.Assemble(
            endpoint, endpoint, [path, "docs/unknown.md"], false, unknown));
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
    public void UnassociatedPathRemainsUnresolved()
    {
        var endpoint = Endpoint(["tests/standalone.py"], ["docs/unknown.md"]);
        PythonInputBinding[] inputs = PythonRepositoryInputs.Map(endpoint.Revision,
            endpoint.Projects, endpoint.Targets, []);
        var owner = new PythonOwnerEndpoint(endpoint.Revision, endpoint.Projects,
            [new("pytest:tests/standalone.py", null, ["tests/standalone.py"])], inputs);
        Assert.ThrowsExactly<InvalidDataException>(() => PythonOwnerProjection.Project(owner,
            owner, ["docs/unknown.md"]));
    }
}
