using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

// Shared package subject for the CI execution fixtures.
internal static class PythonPackageChecksTests
{
    internal static readonly ReleaseUnitDeclaration Unit = new(
        "product/workflow-delivery.release-unit.yml", "product",
        [new("distributions", PythonPackageChecks.Build, "pyproject.toml",
            [new("wheel", "primary-package", "python-wheel"),
                new("source", "source-package", "python-sdist")])]);
    internal static readonly PythonMetadataMember[] Members =
        [new("hcoona-release-smoke-python", "product", "native-id")];
}

[TestClass]
public sealed class PythonFactsAssemblerTests
{
    private static PythonProjectDependencies Member(string path, string[]? dependencies = null) =>
        new(path, path, dependencies ?? [], []);

    private static PythonFactsEndpoint Endpoint(string revision,
        PythonProjectDependencies[] members, string[] targets, ProjectFacts[]? packages = null,
        PythonInputBinding[]? inputs = null, string[]? paths = null)
    {
        string[] committed = members.Select(member => member.Directory == "." ?
            "pyproject.toml" : member.Directory + "/pyproject.toml")
            .Concat(targets.Select(target => target.EndsWith(".py", StringComparison.Ordinal)
                ? target : target + "/test_one.py"))
            .Concat(paths ?? []).Distinct(StringComparer.Ordinal).ToArray();
        return new(new(revision, committed.Select(path => new GitEntry(path,
            "100644", "blob")).ToArray()), members,
            new("pyproject.toml", targets, ["--import-mode=importlib"]),
            new("ubuntu-latest", "python-3.14", new()
            { ["python"] = "3.14", ["platform"] = "linux" }), packages ?? [], inputs ?? []);
    }

    private static CheckSpec Check(string target, string variant = "default",
        CheckKey[]? prerequisites = null) => new(new(target, "python/package-check-v1", variant),
            [], "ubuntu-latest", true, prerequisites ?? []);

    private static ProjectFacts Package(string directory, CheckSpec[]? checks = null) =>
        new(directory, directory, [], [], null, "python/package-v1", checks ?? [Check(directory)]);

    private static CiPlan Plan(PythonFactAssembly assembly) =>
        ImpactPlanner.Plan(assembly.Request, assembly.PairedReasons);

    [TestMethod]
    public void MixedEndpointsReachCandidateOnlyTargetWithoutProxy()
    {
        var basis = Endpoint("before", [Member("."), Member("src/a"),
            Member("src/b", ["src/a"])], ["tests/before.py"], paths: ["src/a/source.py"]);
        var candidate = Endpoint("after", [Member("."), Member("src/b"),
            Member("src/c", ["src/b"])], ["src/c/tests"]);
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(basis, candidate,
            ["src/a/source.py"], false);
        PlannedCheck check = Assert.ContainsSingle(Plan(assembly).Checks);

        Assert.AreEqual("pytest:src/c/tests", check.Work.Key.Target);
        Assert.DoesNotContain("pytest:src/c/tests",
            assembly.Request.Basis.Projects.Select(project => project.Id));
        Assert.AreEqual(new SelectionReason("src/a/source.py", "before", "pytest:src/c/tests"),
            Assert.ContainsSingle(check.Reasons));
        Assert.IsEmpty(assembly.Request.Basis.UnaffectedPaths);
        Assert.HasCount(1, assembly.Request.ChangedPaths);
        Assert.AreEqual(CheckOrigin.NativeRetained, Assert.ContainsSingle(check.Origins));
    }

    [TestMethod]
    public void ConfiguredTargetsKeepNativeOptionsAndExactConsumers()
    {
        var endpoint = Endpoint("same", [Member("."), Member("src/tableless")],
            ["src/tableless/tests", "tests/standalone.py"], paths: ["tests/unrelated.py"]);
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(endpoint, endpoint,
            ["pyproject.toml"], false);
        CiPlan plan = Plan(assembly);
        Assert.HasCount(2, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.AreEqual("importlib", check.Work.Dimensions["importMode"]);
            Assert.AreEqual("3.14", check.Work.Dimensions["python"]);
            Assert.AreEqual("linux", check.Work.Dimensions["platform"]);
            Assert.AreEqual("pyproject.toml", check.Work.Dimensions["configuration"]);
            Assert.AreEqual("python-3.14", check.Work.Key.Variant);
            Assert.AreEqual("ubuntu-latest", check.Work.Runner);
            Assert.IsEmpty(check.QualityPresets);
        }
        var standalone = assembly.Request.Candidate.Projects.Single(project =>
            project.Id == "pytest:tests/standalone.py");
        Assert.AreEqual(".", standalone.Directory);
        Assert.AreEqual("tests/standalone.py", Assert.ContainsSingle(standalone.OwnedPaths!));
        Assert.IsEmpty(ImpactPlanner.DirectConsumers(assembly.Request.Candidate,
            "tests/unrelated.py"));
    }

    [TestMethod]
    public void NestedInputOnlyMemberDoesNotAcquireAncestorPackageChecks()
    {
        var endpoint = Endpoint("same", [Member("."), Member("src/parent"),
            Member("src/parent/input")], ["tests/standalone.py"],
            [Package("src/parent")], paths: ["src/parent/input/source.py"]);
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(endpoint, endpoint,
            ["src/parent/input/source.py"], false);

        Assert.IsEmpty(Plan(assembly).Checks);
        Assert.AreEqual("src/parent/input/source.py",
            Assert.ContainsSingle(assembly.Request.Candidate.UnaffectedPaths));
        Assert.IsEmpty(ImpactPlanner.DirectConsumers(assembly.Request.Candidate,
            "src/parent/input/source.py"));
        Assert.HasCount(2, assembly.Request.Candidate.Projects);
    }

    [TestMethod]
    public void PackageUnitChecksAndPrerequisitesRemainComplete()
    {
        CheckSpec prerequisite = Check("src/helper");
        var subject = new PackageTarget("unit", "src/package/workflow-delivery.release-unit.yml",
            "package", "python/package-build-v1", "src/package", "src/package/pyproject.toml",
            null, "1.0.0", [new("wheel", "distribution", "wheel"),
                new("sdist", "distribution", "sdist")]);
        CheckSpec wheel = Check("src/package", "wheel", [prerequisite.Key]) with
        { Package = subject };
        CheckSpec sdist = Check("src/package", "sdist", [prerequisite.Key]) with
        { Package = subject };
        var first = Package("src/package", [wheel, sdist]) with { ReleaseUnit = "unit" };
        var sibling = Package("src/sibling") with { ReleaseUnit = "unit" };
        var endpoint = Endpoint("same", [Member("."), Member("src/package"),
            Member("src/sibling"), Member("src/helper")], ["tests/standalone.py"],
            [first, sibling, Package("src/helper", [prerequisite])],
            paths: ["src/package/source.py"]);
        CiPlan plan = Plan(PythonFactsAssembler.Assemble(endpoint, endpoint,
            ["src/package/source.py"], false));

        Assert.HasCount(4, plan.Checks);
        CollectionAssert.AreEquivalent(new[] { wheel.Key, sdist.Key, prerequisite.Key,
            sibling.Checks[0].Key }, plan.Checks.Select(check => check.Work.Key).ToArray());
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.IsTrue(check.Work.Required);
            Assert.AreEqual("python/package-v1", Assert.ContainsSingle(check.QualityPresets));
            Assert.AreEqual(CheckOrigin.Preset, Assert.ContainsSingle(check.Origins));
            Assert.AreEqual(new SelectionReason("src/package/source.py", "same", "src/package"),
                Assert.ContainsSingle(check.Reasons));
        }
        CollectionAssert.AreEqual(new[] { prerequisite.Key }, plan.Checks.Single(check =>
            check.Work.Key == wheel.Key).Work.Prerequisites);
        foreach (PlannedCheck check in plan.Checks.Where(check =>
            check.Work.Key.Target == "src/package"))
        {
            Assert.IsNotNull(check.Work.Package);
            Assert.AreEqual("1.0.0", check.Work.Package.ExpectedVersion);
            CollectionAssert.AreEqual(subject.Outputs, check.Work.Package.Outputs);
        }
    }

    [TestMethod]
    public void ExplicitPackageInputsRemainBound()
    {
        var package = Package("src/package") with { OwnedPaths = ["templates/version.py"] };
        var endpoint = Endpoint("same", [Member("."), Member("src/package")],
            ["tests/standalone.py"], [package], paths: ["templates/version.py"]);
        PlannedCheck check = Assert.ContainsSingle(Plan(PythonFactsAssembler.Assemble(
            endpoint, endpoint, ["templates/version.py"], false)).Checks);
        Assert.AreEqual("src/package", check.Work.Key.Target);
        Assert.AreEqual("templates/version.py", Assert.ContainsSingle(check.Reasons).Path);
    }

    [TestMethod]
    public void MemberOnlySharedInputsReachActualOwnersWithoutSyntheticProjects()
    {
        var endpoint = Endpoint("same", [Member("."), Member("src/input"),
            Member("src/member", ["src/input"])], ["src/member/tests", "tests/unrelated.py"],
            inputs: [new("shared.json", ["src/input"], [])], paths: ["shared.json"]);
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(endpoint, endpoint,
            ["shared.json"], false);
        PlannedCheck check = Assert.ContainsSingle(Plan(assembly).Checks);
        Assert.AreEqual("pytest:src/member/tests", check.Work.Key.Target);
        Assert.AreEqual(new SelectionReason("shared.json", "same", "pytest:src/member/tests"),
            Assert.ContainsSingle(check.Reasons));
        Assert.HasCount(2, assembly.Request.Candidate.Projects);
    }

    [TestMethod]
    public void DeletedExplicitInputKeepsOnlyItsCommittedInitiatingRevision()
    {
        var package = Package("src/package") with { OwnedPaths = ["templates/version.py"] };
        var basis = Endpoint("before", [Member("."), Member("src/package")],
            ["tests/standalone.py"], [package], paths: ["templates/version.py"]);
        var candidate = Endpoint("after", [Member("."), Member("src/package")],
            ["tests/standalone.py"], [package]);
        PlannedCheck check = Assert.ContainsSingle(Plan(PythonFactsAssembler.Assemble(
            basis, candidate, ["templates/version.py"], false)).Checks);
        Assert.AreEqual(new SelectionReason("templates/version.py", "before", "src/package"),
            Assert.ContainsSingle(check.Reasons));
    }

    [TestMethod]
    public void DeletedOwnerDoesNotExecute()
    {
        var basis = Endpoint("before", [Member("."), Member("src/removed")],
            ["src/removed/tests"], [Package("src/removed")], paths: ["src/removed/source.py"]);
        var candidate = Endpoint("after", [Member(".")], ["tests/current.py"]);
        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(basis, candidate,
            ["src/removed/source.py"], false);
        Assert.IsEmpty(Plan(assembly).Checks);
        Assert.HasCount(2, assembly.PairedReasons);
        Assert.IsEmpty(assembly.Request.Basis.UnaffectedPaths);
        Assert.IsEmpty(assembly.Request.Candidate.UnaffectedPaths);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KnownEmptyAndUnknownScopeRemainDistinct(bool full)
    {
        var endpoint = Endpoint("same", [Member("."), Member("src/input")],
            ["tests/standalone.py"], paths: ["src/input/source.py", "unknown.py"]);
        PythonFactAssembly known = PythonFactsAssembler.Assemble(endpoint, endpoint,
            ["src/input/source.py"], full);
        Assert.AreEqual(full ? 1 : 0, Plan(known).Checks.Length);
        Assert.IsEmpty(known.PairedReasons);
        Assert.ThrowsExactly<InvalidDataException>(() => PythonFactsAssembler.Assemble(
            endpoint, endpoint, ["unknown.py"], full));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InputPresentAtBothEndpointsRetainsOppositeOwner(bool ownerAtBasis)
    {
        const string path = "src/input/shared.json";
        const string owner = "pytest:src/member/tests";
        var basis = Endpoint("before", [Member("."), Member("src/input"),
            Member("src/member")], ["src/member/tests"], paths: [path]);
        var candidate = basis with { Revision = basis.Revision with { Commit = "after" } };
        PythonInputBinding[] inputs = [new(path, [], [owner])];
        if (ownerAtBasis) basis = basis with { Inputs = inputs };
        else candidate = candidate with { Inputs = inputs };

        PythonFactAssembly assembly = PythonFactsAssembler.Assemble(basis, candidate,
            [path], false);
        var reason = new SelectionReason(path, ownerAtBasis ? "before" : "after", owner);
        Assert.AreEqual(reason, Assert.ContainsSingle(assembly.PairedReasons));
        Assert.IsEmpty(assembly.Request.Basis.UnaffectedPaths);
        Assert.IsEmpty(assembly.Request.Candidate.UnaffectedPaths);
        Assert.AreEqual(reason, Assert.ContainsSingle(
            Assert.ContainsSingle(Plan(assembly).Checks).Reasons));
    }

    [TestMethod]
    public void FullAndEmptyPlansKeepActualOwners()
    {
        var endpoint = Endpoint("same", [Member("."), Member("src/member"),
            Member("src/input")], ["src/member/tests", "tests/standalone.py"]);
        PythonFactAssembly full = PythonFactsAssembler.Assemble(endpoint, endpoint, [], true);
        Assert.HasCount(2, Plan(full).Checks);
        Assert.HasCount(2, full.Request.Candidate.Projects);
        Assert.IsEmpty(Plan(PythonFactsAssembler.Assemble(endpoint, endpoint, [], false)).Checks);
    }

    [TestMethod]
    [DataRow("checks", true)]
    [DataRow("preset", false)]
    [DataRow("origin", true)]
    [DataRow("member", false)]
    [DataRow("relation", true)]
    [DataRow("prerequisite", false)]
    [DataRow("duplicate input", true)]
    public void MalformedActualOwnerFailsBeforeScopeCertification(string defect, bool invalidBasis)
    {
        var package = Package("src/package");
        var valid = Endpoint("valid", [Member("."), Member("src/package"),
            Member("src/input")], ["tests/standalone.py"], [package]);
        ProjectFacts invalid = defect switch
        {
            "checks" => package with { Checks = [] },
            "preset" => package with { QualityPreset = null },
            "origin" => package with { Origin = CheckOrigin.NativeRetained },
            "member" => package with { Directory = "src/missing" },
            "relation" => package with { Dependencies = ["src/input"] },
            "prerequisite" => package with
            { Checks = [Check("src/package", prerequisites: [Check("missing").Key])] },
            "duplicate input" => package with
            { OwnedPaths = ["src/package/pyproject.toml", "src/package/pyproject.toml"] },
            _ => throw new AssertFailedException(defect),
        };
        var malformed = valid with { Packages = [invalid] };
        Assert.ThrowsExactly<InvalidDataException>(() => PythonFactsAssembler.Assemble(
            invalidBasis ? malformed : valid, invalidBasis ? valid : malformed, [], false));
    }

    [TestMethod]
    public void InvalidNativeScopeCannotBecomeRunnableFacts()
    {
        var endpoint = Endpoint("same", [Member("."), Member("src/member")],
            ["src/member/tests"], paths: ["shared.json"]);
        Reject(endpoint with
        { Configuration = endpoint.Configuration with { Options = ["-p", "plugin"] } },
            "unsupported option");
        Reject(endpoint with
        { Configuration = endpoint.Configuration with { TestPaths = ["tests/missing.py"] } },
            "missing target");
        Reject(endpoint with
        {
            Revision = endpoint.Revision with
            {
                Entries = endpoint.Revision.Entries.Where(entry =>
                entry.Path != "src/member/pyproject.toml").ToArray()
            }
        }, "missing member manifest");
        Reject(endpoint with { Inputs = [new("shared.json", ["missing"], [])] }, "unknown member");
        Reject(endpoint with
        { Operation = endpoint.Operation with { Dimensions = new() { ["testPath"] = "other" } } },
            "wrong dimension");

        static void Reject(PythonFactsEndpoint endpoint, string scenario) =>
            Assert.ThrowsExactly<InvalidDataException>(() => PythonFactsAssembler.Assemble(
                endpoint, endpoint, [], false), scenario);
    }

    private static readonly string[] C = ["pytest:c"];
    private static readonly string[] Parent = ["pytest:parent"];
    private static readonly string[] Revisions = ["before", "after"];
    private static readonly string[] Eng = ["pytest:eng"];
    private static readonly string[] RootConsumers = ["pytest:eng", "pytest:package"];
    private static readonly string[] CycleConsumers = ["package:a", "pytest:a", "pytest:b"];
    private static PythonProjectDependencies OwnerMember(string path, string[]? ordinary = null,
        string[]? build = null) => new(path, path, ordinary ?? [], build ?? []);

    private static PythonOwnerEndpoint OwnerEndpoint(string revision,
        PythonProjectDependencies[] projects, PythonQualityBinding[] quality,
        string[] paths, PythonInputBinding[]? inputs = null) => new(
            new(revision, projects.Select(project => project.Directory == "." ?
                "pyproject.toml" : project.Directory + "/pyproject.toml")
                .Concat(paths).Distinct(StringComparer.Ordinal)
                .Select(path => new GitEntry(path, "100644", "blob")).ToArray()),
            projects, quality, inputs ?? []);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MixedEndpointClosureKeepsInputOnlyIntermediary(bool buildEdge)
    {
        var basis = OwnerEndpoint("before", [OwnerMember("."), OwnerMember("src/a"),
            OwnerMember("src/b", buildEdge ? [] : ["src/a"], buildEdge ? ["src/a"] : []),
            OwnerMember("src/c")], [new("pytest:c", "src/c", [])], ["src/a/source.py"]);
        var candidate = OwnerEndpoint("after",
            [OwnerMember("."), OwnerMember("src/a"), OwnerMember("src/b"),
            OwnerMember("src/c", ["src/b"])], [new("pytest:c", "src/c", [])],
            ["src/a/source.py"]);

        PythonOwnerImpact[] result = PythonOwnerProjection.Project(basis, candidate,
            ["src/a/source.py"]);

        Assert.HasCount(2, result);
        foreach (PythonOwnerImpact impact in result)
        {
            Assert.AreEqual("src/a/source.py", impact.Path);
            CollectionAssert.AreEqual(C, impact.Owners);
        }
        CollectionAssert.AreEqual(Revisions, result.Select(impact =>
            impact.Revision).ToArray());
    }

    [TestMethod]
    public void NestedInputOnlyMemberDoesNotInheritParentChecks()
    {
        var endpoint = OwnerEndpoint("same", [OwnerMember("."), OwnerMember("src/parent"),
            OwnerMember("src/parent/input")], [new("pytest:parent", "src/parent", [])],
            ["src/parent/input/data.py", "src/parent/source.py", "src/parent-other/data.py"],
            [new("src/parent-other/data.py", [], [])]);
        PythonOwnerImpact[] result = PythonOwnerProjection.Project(endpoint, endpoint,
            ["src/parent/input/data.py", "src/parent/source.py", "src/parent-other/data.py"]);
        Assert.HasCount(6, result);
        foreach (PythonOwnerImpact impact in result)
            if (impact.Path is "src/parent/input/data.py" or "src/parent-other/data.py")
                Assert.IsEmpty(impact.Owners);
            else CollectionAssert.AreEqual(Parent, impact.Owners);
    }

    [TestMethod]
    public void DeletedProducerPreservesOldPathAndRevisionConsumers()
    {
        var basis = OwnerEndpoint("before", [OwnerMember("."), OwnerMember("src/a"),
            OwnerMember("src/c", ["src/a"])], [new("pytest:c", "src/c", [])],
            ["src/a/removed.py"]);
        var candidate = OwnerEndpoint("after", [OwnerMember("."), OwnerMember("src/c")],
            [new("pytest:c", "src/c", [])], []);
        PythonOwnerImpact impact = Assert.ContainsSingle(PythonOwnerProjection.Project(
            basis, candidate, ["src/a/removed.py"]));
        Assert.AreEqual("before", impact.Revision);
        Assert.AreEqual("src/a/removed.py", impact.Path);
        CollectionAssert.AreEqual(C, impact.Owners);
    }

    [TestMethod]
    public void StandaloneTargetsAndRootInputsHaveOnlyExplicitConsumers()
    {
        var endpoint = OwnerEndpoint("same", [OwnerMember("."), OwnerMember("src/package")],
            [new("pytest:eng", null, ["tests/eng/test_scope.py"]),
                new("pytest:package", "src/package", [])],
            ["tests/eng/test_scope.py", "uv.lock", "docs/unrelated.md"],
            [new("uv.lock", [], ["pytest:eng", "pytest:package"]),
                new("docs/unrelated.md", [], [])]);
        PythonOwnerImpact[] result = PythonOwnerProjection.Project(endpoint, endpoint,
            ["uv.lock", "tests/eng/test_scope.py", "docs/unrelated.md"]);
        foreach (PythonOwnerImpact impact in result)
            switch (impact.Path)
            {
                case "uv.lock":
                    CollectionAssert.AreEqual(RootConsumers, impact.Owners);
                    break;
                case "tests/eng/test_scope.py":
                    CollectionAssert.AreEqual(Eng, impact.Owners);
                    break;
                default:
                    Assert.IsEmpty(impact.Owners);
                    break;
            }
    }

    [TestMethod]
    public void CyclesAndMultipleQualityConsumersKeepFiniteCompleteUnion()
    {
        var endpoint = OwnerEndpoint("same", [OwnerMember("."), OwnerMember("src/a", ["src/b"]),
            OwnerMember("src/b", ["src/a"])], [new("pytest:a", "src/a", []),
                new("package:a", "src/a", []), new("pytest:b", "src/b", [])],
            ["src/a/source.py"]);
        foreach (PythonOwnerImpact impact in PythonOwnerProjection.Project(endpoint, endpoint,
            ["src/a/source.py"]))
            CollectionAssert.AreEqual(CycleConsumers, impact.Owners);
    }

    [TestMethod]
    public void PackageNamesDoNotAssociateDifferentRepositoryCoordinates()
    {
        var basis = OwnerEndpoint("before", [OwnerMember("."), new("src/old", "same-name", [], [])],
            [], ["src/old/source.py"]);
        var candidate = OwnerEndpoint("after",
            [OwnerMember("."), new("src/new", "same-name", [], [])],
            [new("pytest:new", "src/new", [])], ["src/new/source.py"]);
        PythonOwnerImpact[] result = PythonOwnerProjection.Project(basis, candidate,
            ["src/old/source.py", "src/new/source.py"]);
        Assert.HasCount(2, result);
        PythonOwnerImpact removed = result.Single(impact => impact.Revision == "before");
        Assert.AreEqual("src/old/source.py", removed.Path);
        Assert.IsEmpty(removed.Owners);
        PythonOwnerImpact added = result.Single(impact => impact.Revision == "after");
        Assert.AreEqual("src/new/source.py", added.Path);
        Assert.AreEqual("pytest:new", Assert.ContainsSingle(added.Owners));
    }

    [TestMethod]
    public void QualityBindingChangesKeepBothEndpointConsumers()
    {
        var basis = OwnerEndpoint("before", [OwnerMember("."), OwnerMember("src/a")],
            [new("pytest:old", "src/a", [])], ["src/a/source.py"]);
        var candidate = OwnerEndpoint("after", [OwnerMember("."), OwnerMember("src/a")],
            [new("pytest:new", "src/a", [])], ["src/a/source.py"]);
        foreach (PythonOwnerImpact impact in PythonOwnerProjection.Project(basis, candidate,
            ["src/a/source.py"]))
        {
            Assert.HasCount(2, impact.Owners);
            Assert.Contains("pytest:old", impact.Owners);
            Assert.Contains("pytest:new", impact.Owners);
        }
    }

    [TestMethod]
    public void PositiveOtherEndpointDoesNotHideUnknownPresentInput()
    {
        var basis = OwnerEndpoint("before", [OwnerMember("."), OwnerMember("src/a")], [],
            ["shared.py"], [new("shared.py", [], [])]);
        var candidate = OwnerEndpoint("after", [OwnerMember("."), OwnerMember("src/a")], [],
            ["shared.py"]);
        Assert.ThrowsExactly<InvalidDataException>(() => PythonOwnerProjection.Project(
            basis, candidate, ["shared.py"]));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EitherIncompleteEndpointFailsDespiteValidOtherEndpoint(bool invalidBasis)
    {
        var valid = OwnerEndpoint("valid", [OwnerMember("."), OwnerMember("src/a")],
            [new("pytest:a", "src/a", [])], ["src/a/source.py"]);
        Reject(valid with { Projects = [OwnerMember("src/a")] }, "missing root member");
        Reject(valid with { Revision = valid.Revision with { Commit = " " } }, "missing commit");
        Reject(valid with
        {
            Revision = valid.Revision with
            {
                Entries = valid.Revision.Entries.Where(entry =>
                    entry.Path != "src/a/pyproject.toml").ToArray()
            }
        }, "missing member manifest");

        void Reject(PythonOwnerEndpoint invalid, string scenario) =>
            Assert.ThrowsExactly<InvalidDataException>(() => PythonOwnerProjection.Project(
                invalidBasis ? invalid : valid, invalidBasis ? valid : invalid,
                ["src/a/source.py"]), scenario);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnknownSharedInputMemberFailsWithValidOppositeEndpoint(bool invalidBasis)
    {
        var valid = OwnerEndpoint("valid", [OwnerMember("."), OwnerMember("src/a")],
            [new("pytest:a", "src/a", [])], ["shared.json"],
            [new("shared.json", [], [])]);
        var invalid = valid with
        {
            Inputs = [new("shared.json", ["missing"], [])]
        };

        Assert.ThrowsExactly<InvalidDataException>(() => PythonOwnerProjection.Project(
            invalidBasis ? invalid : valid, invalidBasis ? valid : invalid,
            ["shared.json"]));
    }

    [TestMethod]
    public void MemberOnlySharedInputReachesIndirectQualityOwner()
    {
        var basis = OwnerEndpoint("before",
            [OwnerMember("."), OwnerMember("src/a"), OwnerMember("src/b"),
            OwnerMember("src/unrelated")], [new("pytest:b", "src/b", []),
                new("pytest:unrelated", "src/unrelated", [])], ["shared.json"],
            [new("shared.json", ["src/a"], [])]);
        var candidate = OwnerEndpoint("after", [OwnerMember("."), OwnerMember("src/a"),
            OwnerMember("src/b", ["src/a"]), OwnerMember("src/unrelated")],
            [new("pytest:b", "src/b", []), new("pytest:unrelated", "src/unrelated", [])],
            []);

        PythonOwnerImpact impact = Assert.ContainsSingle(PythonOwnerProjection.Project(
            basis, candidate, ["shared.json"]));

        Assert.AreEqual("shared.json", impact.Path);
        Assert.AreEqual("before", impact.Revision);
        Assert.AreEqual("pytest:b", Assert.ContainsSingle(impact.Owners));
    }

    [TestMethod]
    public void IncompleteOwnershipCannotBecomeKnownNonselection()
    {
        var endpoint = OwnerEndpoint("same", [OwnerMember("."), OwnerMember("src/a")],
            [new("pytest:a", "src/a", [])], ["src/a/source.py", "unknown.py"]);
        const string path = "src/a/source.py";
        Reject(endpoint, "unknown file", "unknown.py");
        Reject(endpoint, "absent file", "absent.py");
        Reject(endpoint, "path escape", "../outside.py");
        Reject(endpoint, "duplicate path", path, path);
        Reject(endpoint with { Projects = [OwnerMember("."), OwnerMember("src/a", ["src/b"])] },
            "dangling dependency", path);
        Reject(endpoint with { Projects = [OwnerMember("src/a")] }, "missing root", path);
        Reject(endpoint with { Inputs = [new(path, [], ["missing"])] }, "unknown owner", path);
        Reject(endpoint with { Quality = [new("pytest:a", "missing", [])] },
            "unknown member", path);
        Reject(endpoint with { Quality = [new("pytest:a", "src/a", ["absent"])] },
            "missing target", path);
        Reject(endpoint with
        {
            Projects = [OwnerMember("."), OwnerMember("src/a"), OwnerMember("src/a")]
        }, "duplicate member", path);
        Reject(endpoint with
        {
            Revision = endpoint.Revision with
            {
                Entries = endpoint.Revision.Entries.Select(entry => entry.Path == path ?
                    entry with { Mode = "120000" } : entry).ToArray()
            }
        }, "symlink", path);

        static void Reject(PythonOwnerEndpoint edited, string scenario, params string[] changed) =>
            Assert.ThrowsExactly<InvalidDataException>(() => PythonOwnerProjection.Project(
                edited, edited, changed), scenario);
    }
}
