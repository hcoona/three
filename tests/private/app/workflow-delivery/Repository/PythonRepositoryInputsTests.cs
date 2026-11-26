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
    public void RetiredQidianLabScriptOnBasisHasNoPythonOwner()
    {
        const string path = "src/lab/qidian-novel-downloader/poc_headless_login.py";
        PythonFactsEndpoint basis = Facts(Endpoint([RuntimePytest], [path], "before"));

        Owners(basis.Inputs, path);
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
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/" +
        "hcoona-release-smoke-python/src/hcoona_release_smoke_python/__init__.py",
        "tests/private/app/workflow-delivery/Native/Python", true)]
    [DataRow("src/public/lib/CircularList/workflow-delivery.release-unit.yml",
        "tests/eng/test_release_batch_dispatch.py", true)]
    [DataRow("tests/private/app/workflow-delivery/fixtures/products/" +
        "hcoona-release-smoke-npm/workflow-delivery.release-unit.yml",
        "tests/eng/test_release_batch_dispatch.py", false)]
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
    [DataRow("tests/eng/test_python_runtime.py", "tests/eng/ci_workflow_shell.py")]
    [DataRow("tests/eng/test_node_release_schedule.py", "tests/eng/ci_workflow_shell.py")]
    [DataRow("tests/eng/test_release_batch_dispatch.py", "tests/eng/ci_workflow_shell.py")]
    [DataRow("tests/eng/test_workflow_delivery_v3_hk.py", "tests/eng/ci_workflow_shell.py")]
    [DataRow("tests/eng/test_workflow_delivery_v3_hk.py", "eng/scripts/hk_exec.py")]
    [DataRow("tests/eng/test_workflow_delivery_v3_hk.py", "eng/scripts/hk_actionlint.py")]
    [DataRow("tests/eng/test_workflow_delivery_v3_hk.py", "eng/scripts/hk_pkl_eval.py")]
    [DataRow("tests/eng/test_workflow_delivery_v3_hk.py", "eng/scripts/hk_file_operands.py")]
    [DataRow("tests/eng/test_python_release_caller.py", "global.json")]
    [DataRow("tests/eng/test_python_release_caller.py", "tests/eng/ci_workflow_shell.py")]
    [DataRow("tests/eng/test_browser_store_release_caller.py",
        ".github/workflows/workflow-delivery-v3-browser-store-release.yml")]
    [DataRow("tests/eng/test_browser_store_release_caller.py", "tests/eng/ci_workflow_shell.py")]
    [DataRow("tests/eng/test_browser_store_release_caller.py", "global.json")]
    [DataRow("tests/eng/test_release_batch_dispatch.py",
        "eng/scripts/release_batch_dispatch.py")]
    [DataRow("tests/eng/test_release_batch_dispatch.py",
        ".github/workflows/workflow-delivery-v3-ruby-release.yml")]
    [DataRow("tests/eng/test_release_batch_dispatch.py",
        ".github/workflows/workflow-delivery-v3-nuget-release.yml")]
    [DataRow("tests/eng/test_svgedit_renovate.py", "renovate.json")]
    [DataRow("tests/eng/test_svgedit_renovate.py", ".github/renovate/global.cjs")]
    [DataRow("tests/eng/test_svgedit_renovate.py",
        "src/public/app/ImageOcclusionEditor/ImageOcclusionEditorWinUI3/Update-LocalSvgEditor.ps1")]
    [DataRow("tests/eng/test_release_triage.py", "eng/scripts/release_triage.py")]
    [DataRow("tests/eng/test_release_triage.py", ".github/workflows/release-triage.yml")]
    [DataRow("tests/eng/test_release_triage.py",
        "src/public/lib/Memoization/workflow-delivery.release-unit.yml")]
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
    [DataRow("tests/eng/test_typos_config.py", "tests/eng/ci_workflow_shell.py")]
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
    public void InvalidSuppliedScopeFails()
    {
        var endpoint = Endpoint(["src/pkg/tests"], ["uv.lock"],
            members: [Member("."), Member("src/pkg")]);
        var target = Assert.ContainsSingle(endpoint.Targets.Targets);
        Reject("foreign revision", targets: endpoint.Targets with { Revision = "foreign" });
        Reject("missing target member",
            targets: endpoint.Targets with { Targets = [target with { Member = null }] });
        Reject("wrong target input",
            targets: endpoint.Targets with { Targets = [target with { Inputs = ["uv.lock"] }] });
        Reject("duplicate target", targets: endpoint.Targets with { Targets = [target, target] });
        Reject("duplicate package", [Package(), Package()]);
        Reject("foreign package", [Package(directory: "src/foreign")]);
        Reject("unresolved check", [Package() with { Checks = [] }]);
        Reject("root package", [Package(directory: ".")]);
        Reject("nonregular lock", revision: ChangeEntry(endpoint.Revision, "uv.lock",
            entry => entry with { Mode = "120000" }));

        void Reject(string scenario, ProjectFacts[]? packages = null,
            PythonPytestTargetSet? targets = null, GitRevision? revision = null) =>
            Assert.ThrowsExactly<InvalidDataException>(() => PythonRepositoryInputs.Map(
                revision ?? endpoint.Revision, endpoint.Projects, targets ?? endpoint.Targets,
                packages ?? []), scenario);
    }

    private static GitRevision ChangeEntry(GitRevision revision, string path,
        Func<GitEntry, GitEntry> edit) => revision with
        {
            Entries = revision.Entries.Select(entry => entry.Path == path ? edit(entry) : entry)
                .ToArray()
        };

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
    [DataRow("docs/unknown.md", "before", "foreign")]
    [DataRow("docs/unknown.md", "foreign", "pytest:tests/standalone.py")]
    [DataRow("docs/unknown.md", "after", "pytest:tests/standalone.py")]
    [DataRow("docs/unchanged.md", "before", "pytest:tests/standalone.py")]
    public void InvalidPairedReasonsCannotCertifyScope(string path, string revision, string owner)
    {
        var (basis, candidate) = PairedEndpoints();
        RejectReasons(basis, candidate, new SelectionReason(path, revision, owner));
    }

    [TestMethod]
    public void DuplicatePairedReasonsCannotCertifyScope()
    {
        var (basis, candidate) = PairedEndpoints();
        var reason = new SelectionReason("docs/unknown.md", "before", "pytest:tests/standalone.py");
        RejectReasons(basis, candidate, reason, reason);
    }

    [TestMethod]
    public void NonregularPairedReasonCannotCertifyScope()
    {
        var (basis, candidate) = PairedEndpoints();
        var reason = new SelectionReason("docs/unknown.md", "before", "pytest:tests/standalone.py");
        RejectReasons(basis with
        {
            Revision = ChangeEntry(basis.Revision, reason.Path,
                entry => entry with { Mode = "120000" })
        }, candidate, reason);
    }

    private static (PythonFactsEndpoint Basis, PythonFactsEndpoint Candidate) PairedEndpoints() =>
        (Facts(Endpoint(["tests/standalone.py"],
             ["docs/unknown.md", "docs/unchanged.md"], "before")),
         Facts(Endpoint(["tests/standalone.py"], [], "after")));

    private static void RejectReasons(PythonFactsEndpoint basis, PythonFactsEndpoint candidate,
        params SelectionReason[] reasons) =>
        Assert.ThrowsExactly<InvalidDataException>(() => PythonFactsAssembler.Assemble(
            basis, candidate, ["docs/unknown.md"], false, reasons));

    [TestMethod]
    [DataRow("missing.py")]
    [DataRow(".typos.toml", ".typos.toml")]
    [DataRow("../outside.py")]
    public void InvalidPairCoordinatesFail(params string[] paths)
    {
        PythonFactsEndpoint endpoint = Facts(Endpoint(["tests/standalone.py"], [".typos.toml"]));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonRepositoryInputs.Pair(endpoint, endpoint, paths));
    }

    [TestMethod]
    public void NonregularPairCoordinateFails()
    {
        const string path = ".typos.toml";
        PythonFactsEndpoint endpoint = Facts(Endpoint(["tests/standalone.py"], [path]));
        endpoint = endpoint with
        {
            Revision = ChangeEntry(endpoint.Revision, path,
                entry => entry with { ObjectType = "tree" })
        };
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonRepositoryInputs.Pair(endpoint, endpoint, [path]));
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

    private static readonly string[] Targets = ["src/member/tests", "tests/one.py"];
    private static readonly string[] MemberInputs =
        ["src/member/tests/data.json", "src/member/tests/test_one.py"];
    private static readonly string[] FileInput = ["tests/one.py"];

    private static GitEntry Entry(string path) => new(path, "100644", "blob");
    private static PythonPytestConfiguration Configuration() => new("pyproject.toml",
        Targets, []);
    private static PythonProjectDependencies[] Projects() =>
        [new(".", "root", [], []), new("src/member", "member", [], [])];
    private static GitRevision Revision() => new("basis",
        [Entry("pyproject.toml"), Entry("src/member/pyproject.toml"),
        Entry("src/member/source.py"), Entry("src/member/tests/test_one.py"),
        Entry("src/member/tests/data.json"), Entry("tests/one.py"), Entry("tests/one.pyc"),
        Entry("tests/unrelated.py")]);

    [TestMethod]
    public void ConfiguredTargetsBindExactInputsAndNativeMembers()
    {
        RepositoryFacts result = TargetFacts();
        Assert.AreEqual("basis", result.Revision);
        Assert.AreEqual("pyproject.toml", Assert.ContainsSingle(result.SharedInputs).Path);
        Assert.HasCount(2, result.Projects);
        ProjectFacts member = Target(result, "src/member/tests");
        Assert.AreEqual("src/member", member.Directory);
        CollectionAssert.AreEqual(MemberInputs, member.OwnedPaths);
        ProjectFacts standalone = Target(result, "tests/one.py");
        Assert.AreEqual(".", standalone.Directory);
        CollectionAssert.AreEqual(FileInput, standalone.OwnedPaths);
    }

    [TestMethod]
    public void NearestMemberDoesNotTurnStandaloneTargetsIntoRootWork()
    {
        GitRevision revision = Revision() with
        {
            Entries = [.. Revision().Entries, Entry("src/member/nested/pyproject.toml"),
                Entry("src/member/nested/tests/test_nested.py")]
        };
        PythonPytestConfiguration configuration = Configuration() with
        {
            TestPaths = ["src/member/nested/tests", "tests/one.py"]
        };
        RepositoryFacts result = TargetFacts(configuration, revision,
            [.. Projects(), new("src/member/nested", "nested", [], [])]);
        Assert.AreEqual("src/member/nested", Target(result, "src/member/nested/tests").Directory);
        ProjectFacts standalone = Target(result, "tests/one.py");
        Assert.AreEqual(".", standalone.Directory);
        CollectionAssert.AreEqual(FileInput, standalone.OwnedPaths);
    }

    [TestMethod]
    [DataRow("tests/missing.py")]
    [DataRow]
    [DataRow("tests/one.py", "tests/one.py")]
    [DataRow("../tests")]
    public void InvalidConfiguredTargetsCannotBecomeRunnableOwners(params string[] targets) =>
        Assert.ThrowsExactly<InvalidDataException>(() =>
            TargetFacts(Configuration() with { TestPaths = targets }));

    [TestMethod]
    public void UncommittedOrSymlinkedConfigurationCannotBecomeRunnableOwners()
    {
        GitRevision revision = Revision();
        var missing = Configuration() with { ConfigurationFile = "pytest.ini" };
        Assert.ThrowsExactly<InvalidDataException>(() => TargetFacts(missing));
        var symlink = revision with
        {
            Entries = [.. revision.Entries, new("pytest.ini", "120000", "blob")]
        };
        Assert.ThrowsExactly<InvalidDataException>(() => TargetFacts(missing, symlink));
    }

    [TestMethod]
    [DataRow("tests/one.py", "120000", "blob")]
    [DataRow("src/member/tests/data.json", "120000", "blob")]
    [DataRow("tests/one.py", "160000", "commit")]
    [DataRow("src/member/pyproject.toml", "120000", "blob")]
    public void NonregularTargetInputCannotBecomeRunnableOwner(string path, string mode,
        string type) => Assert.ThrowsExactly<InvalidDataException>(() =>
        TargetFacts(revision: ChangeEntry(Revision(), path, _ => new(path, mode, type))));

    [TestMethod]
    public void IncompleteCommittedScopeCannotBecomeRunnableOwner()
    {
        GitRevision revision = Revision();
        PythonProjectDependencies[] projects = Projects();
        Reject(revision with { Entries = [.. revision.Entries, revision.Entries[0]] },
            projects, "duplicate entry");
        Reject(revision with { Commit = "" }, projects, "missing revision");
        Reject(revision, [.. projects, projects[1]], "duplicate member");
        Reject(revision, [projects[1]], "missing root member");
        Reject(revision with
        {
            Entries = revision.Entries.Where(entry =>
                entry.Path != "src/member/pyproject.toml").ToArray()
        }, projects, "missing manifest");

        static void Reject(GitRevision revision, PythonProjectDependencies[] projects,
            string scenario) => Assert.ThrowsExactly<InvalidDataException>(() =>
            TargetFacts(revision: revision, projects: projects), scenario);
    }

    [TestMethod]
    public void EnclosingMultipleMembersFailsInsteadOfGuessing()
    {
        GitRevision revision = Revision() with
        {
            Entries = [.. Revision().Entries, Entry("src/member/nested/pyproject.toml")]
        };
        PythonPytestConfiguration configuration = Configuration() with
        { TestPaths = ["src/member"] };
        Assert.ThrowsExactly<InvalidDataException>(() => TargetFacts(
            configuration, revision, [.. Projects(), new("src/member/nested", "nested", [], [])]));
    }

    [TestMethod]
    public void EndpointBindingsRemainIndependent()
    {
        PythonFactsEndpoint before = TargetEndpoint(Configuration(), Revision(), Projects());
        PythonFactsEndpoint after = TargetEndpoint(
            Configuration() with { TestPaths = ["src/member"] },
            Revision() with { Commit = "candidate" }, Projects());
        PlanRequest plan = PythonFactsAssembler.Assemble(before, after, [], false).Request;
        RepositoryFacts basis = plan.Basis;
        RepositoryFacts candidate = plan.Candidate;
        Assert.AreEqual("basis", basis.Revision);
        Assert.AreEqual("candidate", candidate.Revision);
        Assert.HasCount(2, basis.Projects);
        ProjectFacts target = Assert.ContainsSingle(candidate.Projects);
        Assert.AreEqual("pytest:src/member", target.Id);
        Assert.AreEqual("src/member", target.Directory);
        Assert.HasCount(4, target.OwnedPaths!);
        Assert.Contains("src/member/source.py", target.OwnedPaths!);
    }

    private static ProjectFacts Target(RepositoryFacts facts, string path) =>
        Assert.ContainsSingle(facts.Projects.Where(project => project.Id == "pytest:" + path));

    private static RepositoryFacts TargetFacts(PythonPytestConfiguration? configuration = null,
        GitRevision? revision = null, PythonProjectDependencies[]? projects = null)
    {
        var endpoint = TargetEndpoint(configuration ?? Configuration(), revision ?? Revision(),
            projects ?? Projects());
        return PythonFactsAssembler.Assemble(endpoint, endpoint, [], false).Request.Candidate;
    }

    private static PythonFactsEndpoint TargetEndpoint(PythonPytestConfiguration configuration,
        GitRevision revision, PythonProjectDependencies[] projects) =>
        new(revision, projects, configuration, new("ubuntu-latest", "default", []), [], []);
}
