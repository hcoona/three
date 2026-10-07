using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonPytestTargetsTests
{
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
        PythonPytestTargetSet result = PythonPytestTargets.Bind(Configuration(), Revision(),
            Projects());
        Assert.AreEqual("basis", result.Revision);
        Assert.AreEqual("pyproject.toml", result.ConfigurationFile);
        Assert.HasCount(2, result.Targets);
        Assert.AreEqual("src/member/tests", result.Targets[0].Target);
        Assert.AreEqual("src/member", result.Targets[0].Member);
        CollectionAssert.AreEqual(MemberInputs, result.Targets[0].Inputs);
        Assert.AreEqual("tests/one.py", result.Targets[1].Target);
        Assert.IsNull(result.Targets[1].Member);
        CollectionAssert.AreEqual(FileInput, result.Targets[1].Inputs);
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
        PythonPytestTargetSet result = PythonPytestTargets.Bind(configuration, revision,
            [.. Projects(), new("src/member/nested", "nested", [], [])]);
        Assert.AreEqual("src/member/nested", result.Targets[0].Member);
        Assert.IsNull(result.Targets[1].Member);
        CollectionAssert.AreEqual(FileInput, result.Targets[1].Inputs);
    }

    [TestMethod]
    [DataRow("missing-target")]
    [DataRow("empty-targets")]
    [DataRow("duplicate-target")]
    [DataRow("escape-target")]
    [DataRow("missing-config")]
    [DataRow("config-symlink")]
    [DataRow("target-symlink")]
    [DataRow("directory-input-symlink")]
    [DataRow("target-submodule")]
    [DataRow("missing-member-input")]
    [DataRow("member-symlink")]
    [DataRow("duplicate-member")]
    [DataRow("missing-root")]
    [DataRow("duplicate-entry")]
    [DataRow("missing-revision")]
    public void InvalidCommittedScopeCannotBecomeRunnableOwner(string fault)
    {
        GitRevision revision = Revision();
        PythonPytestConfiguration configuration = Configuration();
        PythonProjectDependencies[] projects = Projects();
        switch (fault)
        {
            case "missing-target":
                configuration = configuration with
                { TestPaths = ["tests/missing.py"] }; break;
            case "empty-targets": configuration = configuration with { TestPaths = [] }; break;
            case "duplicate-target":
                configuration = configuration with
                { TestPaths = ["tests/one.py", "tests/one.py"] }; break;
            case "escape-target":
                configuration = configuration with
                { TestPaths = ["../tests"] }; break;
            case "missing-config":
                configuration = configuration with
                { ConfigurationFile = "pytest.ini" }; break;
            case "config-symlink":
                configuration = configuration with
                { ConfigurationFile = "pytest.ini" };
                revision = revision with
                {
                    Entries = [.. revision.Entries,
                    new("pytest.ini", "120000", "blob")]
                };
                break;
            case "target-symlink":
                revision = Replace("tests/one.py", "120000", "blob");
                break;
            case "directory-input-symlink":
                revision = Replace(
                "src/member/tests/data.json", "120000", "blob"); break;
            case "target-submodule":
                revision = Replace("tests/one.py", "160000", "commit");
                break;
            case "missing-member-input":
                revision = revision with
                {
                    Entries = revision.Entries.Where(entry => entry.Path !=
                    "src/member/pyproject.toml").ToArray()
                }; break;
            case "member-symlink":
                revision = Replace(
                "src/member/pyproject.toml", "120000", "blob"); break;
            case "duplicate-member": projects = [.. projects, projects[1]]; break;
            case "missing-root": projects = [projects[1]]; break;
            case "duplicate-entry":
                revision = revision with
                { Entries = [.. revision.Entries, revision.Entries[0]] }; break;
            case "missing-revision": revision = revision with { Commit = "" }; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonPytestTargets.Bind(configuration, revision, projects));

        GitRevision Replace(string path, string mode, string type) => revision with
        {
            Entries = revision.Entries.Select(entry => entry.Path == path ?
                new(path, mode, type) : entry).ToArray()
        };
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
        Assert.ThrowsExactly<InvalidDataException>(() => PythonPytestTargets.Bind(
            configuration, revision, [.. Projects(), new("src/member/nested", "nested", [], [])]));
    }

    [TestMethod]
    public void EndpointBindingsRemainIndependent()
    {
        PythonPytestTargetSet basis = PythonPytestTargets.Bind(Configuration(), Revision(),
            Projects());
        PythonPytestTargetSet candidate = PythonPytestTargets.Bind(
            Configuration() with { TestPaths = ["src/member"] },
            Revision() with { Commit = "candidate" }, Projects());
        Assert.AreEqual("basis", basis.Revision);
        Assert.AreEqual("candidate", candidate.Revision);
        Assert.HasCount(2, basis.Targets);
        PythonPytestTarget target = Assert.ContainsSingle(candidate.Targets);
        Assert.AreEqual("src/member", target.Target);
        Assert.AreEqual("src/member", target.Member);
        Assert.HasCount(4, target.Inputs);
        Assert.Contains("src/member/source.py", target.Inputs);
    }
}
