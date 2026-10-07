using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonOwnerProjectionTests
{
    private static readonly string[] C = ["pytest:c"];
    private static readonly string[] Parent = ["pytest:parent"];
    private static readonly string[] Revisions = ["before", "after"];
    private static readonly string[] Eng = ["pytest:eng"];
    private static readonly string[] RootConsumers = ["pytest:eng", "pytest:package"];
    private static readonly string[] CycleConsumers = ["package:a", "pytest:a", "pytest:b"];
    private static PythonProjectDependencies Member(string path, string[]? ordinary = null,
        string[]? build = null) => new(path, path, ordinary ?? [], build ?? []);

    private static PythonOwnerEndpoint Endpoint(string revision,
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
        var basis = Endpoint("before", [Member("."), Member("src/a"),
            Member("src/b", buildEdge ? [] : ["src/a"], buildEdge ? ["src/a"] : []),
            Member("src/c")], [new("pytest:c", "src/c", [])], ["src/a/source.py"]);
        var candidate = Endpoint("after", [Member("."), Member("src/a"), Member("src/b"),
            Member("src/c", ["src/b"])], [new("pytest:c", "src/c", [])],
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
        var endpoint = Endpoint("same", [Member("."), Member("src/parent"),
            Member("src/parent/input")], [new("pytest:parent", "src/parent", [])],
            ["src/parent/input/data.py", "src/parent/source.py"]);
        PythonOwnerImpact[] result = PythonOwnerProjection.Project(endpoint, endpoint,
            ["src/parent/input/data.py", "src/parent/source.py"]);
        foreach (PythonOwnerImpact impact in result)
            if (impact.Path == "src/parent/input/data.py") Assert.IsEmpty(impact.Owners);
            else CollectionAssert.AreEqual(Parent, impact.Owners);
    }

    [TestMethod]
    public void DeletedProducerPreservesOldPathAndRevisionConsumers()
    {
        var basis = Endpoint("before", [Member("."), Member("src/a"),
            Member("src/c", ["src/a"])], [new("pytest:c", "src/c", [])],
            ["src/a/removed.py"]);
        var candidate = Endpoint("after", [Member("."), Member("src/c")],
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
        var endpoint = Endpoint("same", [Member("."), Member("src/package")],
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
        var endpoint = Endpoint("same", [Member("."), Member("src/a", ["src/b"]),
            Member("src/b", ["src/a"])], [new("pytest:a", "src/a", []),
                new("package:a", "src/a", []), new("pytest:b", "src/b", [])],
            ["src/a/source.py"]);
        foreach (PythonOwnerImpact impact in PythonOwnerProjection.Project(endpoint, endpoint,
            ["src/a/source.py"]))
            CollectionAssert.AreEqual(CycleConsumers, impact.Owners);
    }

    [TestMethod]
    public void PackageNamesDoNotAssociateDifferentRepositoryCoordinates()
    {
        var basis = Endpoint("before", [Member("."), new("src/old", "same-name", [], [])],
            [], ["src/old/source.py"]);
        var candidate = Endpoint("after", [Member("."), new("src/new", "same-name", [], [])],
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
        var basis = Endpoint("before", [Member("."), Member("src/a")],
            [new("pytest:old", "src/a", [])], ["src/a/source.py"]);
        var candidate = Endpoint("after", [Member("."), Member("src/a")],
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
        var basis = Endpoint("before", [Member("."), Member("src/a")], [],
            ["shared.py"], [new("shared.py", [], [])]);
        var candidate = Endpoint("after", [Member("."), Member("src/a")], [],
            ["shared.py"]);
        Assert.ThrowsExactly<InvalidDataException>(() => PythonOwnerProjection.Project(
            basis, candidate, ["shared.py"]));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void EitherIncompleteEndpointFailsDespiteValidOtherEndpoint(bool invalidBasis)
    {
        var valid = Endpoint("valid", [Member("."), Member("src/a")],
            [new("pytest:a", "src/a", [])], ["src/a/source.py"]);
        var invalid = valid with
        {
            Revision = valid.Revision with { Commit = "invalid" },
            Projects = [Member("src/a")]
        };

        Assert.ThrowsExactly<InvalidDataException>(() => PythonOwnerProjection.Project(
            invalidBasis ? invalid : valid, invalidBasis ? valid : invalid,
            ["src/a/source.py"]));
    }

    [TestMethod]
    public void MemberOnlySharedInputReachesIndirectQualityOwner()
    {
        var basis = Endpoint("before", [Member("."), Member("src/a"), Member("src/b"),
            Member("src/unrelated")], [new("pytest:b", "src/b", []),
                new("pytest:unrelated", "src/unrelated", [])], ["shared.json"],
            [new("shared.json", ["src/a"], [])]);
        var candidate = Endpoint("after", [Member("."), Member("src/a"),
            Member("src/b", ["src/a"]), Member("src/unrelated")],
            [new("pytest:b", "src/b", []), new("pytest:unrelated", "src/unrelated", [])],
            []);

        PythonOwnerImpact impact = Assert.ContainsSingle(PythonOwnerProjection.Project(
            basis, candidate, ["shared.json"]));

        Assert.AreEqual("shared.json", impact.Path);
        Assert.AreEqual("before", impact.Revision);
        Assert.AreEqual("pytest:b", Assert.ContainsSingle(impact.Owners));
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("absent")]
    [DataRow("dangling")]
    [DataRow("missing-root")]
    [DataRow("unknown-owner")]
    [DataRow("unknown-member")]
    [DataRow("symlink")]
    [DataRow("missing-target")]
    [DataRow("duplicate-member")]
    [DataRow("escape")]
    [DataRow("duplicate-path")]
    public void IncompleteOwnershipCannotBecomeKnownNonselection(string failure)
    {
        var endpoint = Endpoint("same", [Member("."), Member("src/a")],
            [new("pytest:a", "src/a", [])], ["src/a/source.py", "unknown.py"]);
        string path = "src/a/source.py";
        endpoint = failure switch
        {
            "dangling" => endpoint with { Projects = [Member("."), Member("src/a", ["src/b"])] },
            "missing-root" => endpoint with { Projects = [Member("src/a")] },
            "unknown-owner" => endpoint with { Inputs = [new(path, [], ["missing"])] },
            "unknown-member" => endpoint with { Quality = [new("pytest:a", "missing", [])] },
            "missing-target" => endpoint with { Quality = [new("pytest:a", "src/a", ["absent"])] },
            "duplicate-member" => endpoint with
            { Projects = [Member("."), Member("src/a"), Member("src/a")] },
            "symlink" => endpoint with
            {
                Revision = endpoint.Revision with
                {
                    Entries =
                endpoint.Revision.Entries.Select(entry => entry.Path == path ?
                    entry with { Mode = "120000" } : entry).ToArray()
                }
            },
            _ => endpoint
        };
        if (failure == "unknown") path = "unknown.py";
        if (failure == "absent") path = "absent.py";
        if (failure == "escape") path = "../outside.py";
        string[] changed = failure == "duplicate-path" ? [path, path] : [path];
        Assert.ThrowsExactly<InvalidDataException>(() => PythonOwnerProjection.Project(
            endpoint, endpoint, changed));
    }
}
