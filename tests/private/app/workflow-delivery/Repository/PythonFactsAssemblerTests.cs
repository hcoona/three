using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

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
    [DataRow("checks", false)]
    [DataRow("checks", true)]
    [DataRow("preset", false)]
    [DataRow("preset", true)]
    [DataRow("origin", false)]
    [DataRow("origin", true)]
    [DataRow("member", false)]
    [DataRow("member", true)]
    [DataRow("relation", false)]
    [DataRow("relation", true)]
    [DataRow("prerequisite", false)]
    [DataRow("prerequisite", true)]
    [DataRow("duplicate-input", false)]
    [DataRow("duplicate-input", true)]
    public void EitherMalformedActualOwnerFailsBeforeScopeCertification(string fault,
        bool invalidBasis)
    {
        var package = Package("src/package");
        ProjectFacts invalid = fault switch
        {
            "checks" => package with { Checks = [] },
            "preset" => package with { QualityPreset = null },
            "origin" => package with { Origin = CheckOrigin.NativeRetained },
            "member" => package with { Directory = "src/missing" },
            "relation" => package with { Dependencies = ["src/input"] },
            "prerequisite" => package with
            { Checks = [Check("src/package", prerequisites: [Check("missing").Key])] },
            "duplicate-input" => package with
            { OwnedPaths = ["src/package/pyproject.toml", "src/package/pyproject.toml"] },
            _ => throw new ArgumentException("Unknown fault.", nameof(fault)),
        };
        var valid = Endpoint("valid", [Member("."), Member("src/package"),
            Member("src/input")], ["tests/standalone.py"], [package]);
        var malformed = valid with { Packages = [invalid] };
        Assert.ThrowsExactly<InvalidDataException>(() => PythonFactsAssembler.Assemble(
            invalidBasis ? malformed : valid, invalidBasis ? valid : malformed, [], false));
    }

    [TestMethod]
    [DataRow("options")]
    [DataRow("target")]
    [DataRow("member-manifest")]
    [DataRow("input-member")]
    [DataRow("dimension")]
    public void InvalidNativeScopeCannotBecomeRunnableFacts(string fault)
    {
        var endpoint = Endpoint("same", [Member("."), Member("src/member")],
            ["src/member/tests"], paths: ["shared.json"]);
        endpoint = fault switch
        {
            "options" => endpoint with
            { Configuration = endpoint.Configuration with { Options = ["-p", "plugin"] } },
            "target" => endpoint with
            { Configuration = endpoint.Configuration with { TestPaths = ["tests/missing.py"] } },
            "member-manifest" => endpoint with
            {
                Revision = endpoint.Revision with
                {
                    Entries = endpoint.Revision.Entries.Where(entry =>
                    entry.Path != "src/member/pyproject.toml").ToArray()
                }
            },
            "input-member" => endpoint with { Inputs = [new("shared.json", ["missing"], [])] },
            "dimension" => endpoint with
            {
                Operation = endpoint.Operation with
                { Dimensions = new() { ["testPath"] = "other" } }
            },
            _ => throw new ArgumentException("Unknown fault.", nameof(fault)),
        };
        Assert.ThrowsExactly<InvalidDataException>(() => PythonFactsAssembler.Assemble(
            endpoint, endpoint, [], false));
    }
}
