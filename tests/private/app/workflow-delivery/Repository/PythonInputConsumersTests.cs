using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonInputConsumersTests
{
    private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Next = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Declaration = "workflow-delivery.quality.yml";

    [TestMethod]
    public void MapsNativeConfigurationAndSharedVersionSubjects()
    {
        var fixture = new Fixture();
        PythonInputBinding[] inputs = fixture.Map();
        Members(inputs, "pyproject.toml", ".", "src/a", "src/b");
        Members(inputs, "src/a/pyproject.toml", "src/a");
        Members(inputs, "shared/version.json", "src/a", "src/b");
        Members(inputs, "version.txt", "src/a", "src/b");
        Members(inputs, "shared/generated.py", "src/a", "src/b");
        Members(inputs, "shared/version.txt", "src/a", "src/b");
        Assert.IsFalse(fixture.Revision.Entries.Any(entry => entry.Path == "shared/generated.py"));
        Assert.IsFalse(fixture.Revision.Entries.Any(entry => entry.Path == "version.txt"));
        foreach (PythonInputBinding input in inputs) Assert.IsEmpty(input.Owners);
        string[] expected = ["pyproject.toml", "src/a/pyproject.toml", "src/b/pyproject.toml",
            "shared/version.json", "version.txt", "shared/version.txt", "shared/generated.py",
            Declaration, "src/" + Declaration, "src/a/" + Declaration, "src/b/" + Declaration];
        CollectionAssert.AreEquivalent(expected, inputs.Select(input => input.Path).ToArray());
    }

    [TestMethod]
    [DataRow("src/a/" + Declaration, 1)]
    [DataRow("src/" + Declaration, 2)]
    [DataRow(Declaration, 3)]
    [DataRow("", 3)]
    public void QualitySearchStopsAtEffectiveAncestor(string sourcePath, int count)
    {
        var fixture = new Fixture();
        fixture.Quality["src/a"] = sourcePath.Length == 0 ? null : new("opaque", sourcePath);
        if (sourcePath.Length != 0) fixture.AddFile(sourcePath);
        PythonInputBinding[] inputs = fixture.Map();
        string[] paths = inputs.Where(input => input.Path.EndsWith(Declaration,
            StringComparison.Ordinal) && input.Members.Contains("src/a", StringComparer.Ordinal))
            .Select(input => input.Path).ToArray();
        string[] expected = count switch
        {
            1 => ["src/a/" + Declaration],
            2 => ["src/" + Declaration, "src/a/" + Declaration],
            _ => ["src/a/" + Declaration, "src/" + Declaration, Declaration]
        };
        CollectionAssert.AreEquivalent(expected, paths);
        Members(inputs, Declaration, count == 3 ? [".", "src/a", "src/b"] : [".", "src/b"]);
    }

    [TestMethod]
    public void UnversionedProducersKeepConfigurationAndQualityInputs()
    {
        var fixture = new Fixture();
        fixture.Versions = [];
        fixture.Passive = fixture.Projects.Select(project =>
            Passive(fixture.Root, project.Directory)).ToArray();
        PythonInputBinding[] inputs = fixture.Map();
        Members(inputs, "pyproject.toml", ".", "src/a", "src/b");
        Members(inputs, "src/a/" + Declaration, "src/a");
        Assert.DoesNotContain("shared/version.json", inputs.Select(input => input.Path));
        Assert.DoesNotContain("shared/generated.py", inputs.Select(input => input.Path));
    }

    [TestMethod]
    public void RootVersionSubjectAndCommittedGeneratedFileKeepActualProducer()
    {
        var fixture = new Fixture();
        fixture.Passive = [Passive(fixture.Root, "."),
            Passive(fixture.Root, "src/a", "."), Passive(fixture.Root, "src/b")];
        fixture.Versions = [new(Source, ".", ["version.json"], ["shared/version.json"])];
        fixture.AddFile("shared/generated.py");
        PythonInputBinding[] inputs = fixture.Map();
        Members(inputs, "shared/version.json", "src/a");
        Members(inputs, "version.json", "src/a");
        Members(inputs, "shared/generated.py", "src/a");
    }

    [TestMethod]
    [DataRow("shared", false)]
    [DataRow(".", false)]
    [DataRow("shared", true)]
    public void PluginCoordinateSpellingsNormalizeWithinEndpoint(string subject,
        bool canonicalWorkingDirectory)
    {
        var fixture = new Fixture();
        string project = fixture.Absolute("src/a");
        string working = canonicalWorkingDirectory ? fixture.Absolute(subject) :
            subject == "." ? Path.Combine(project, "..", "..") :
                Path.Combine(project, "..", "..", subject);
        string generated = Path.Combine(project, "..", "..", "shared", "generated.py");
        fixture.Passive = [Passive(fixture.Root, "."),
            new(new(project, null), new(working, generated)), Passive(fixture.Root, "src/b")];
        fixture.Versions = [new(Source, subject, ["version.json"], ["shared/version.json"])];
        PythonInputBinding[] inputs = fixture.Map();
        Members(inputs, "shared/version.json", "src/a");
        Members(inputs, "version.json", "src/a");
        Members(inputs, "shared/generated.py", "src/a");
        Assert.IsFalse(inputs.Any(input => input.Path.Split('/').Contains("..")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NormalizedOutsidePluginCoordinatesRemainUnsupported(bool generated)
    {
        var fixture = new Fixture();
        string external = Path.Combine(fixture.Absolute("src/a"), "..", "..", "..", "outside");
        fixture.Passive[1] = fixture.Passive[1] with
        {
            Nbgv = generated ? new(fixture.Absolute("shared"), external + ".py") :
                new(external, null)
        };
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Map());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void AddedOrDeletedQualityCandidateKeepsCommittedRevision(bool added)
    {
        var absent = new Fixture();
        var present = new Fixture();
        present.Revision = present.Revision with { Commit = Next };
        present.Versions = [present.Versions[0] with { Commit = Next }];
        string path = "src/a/" + Declaration;
        present.AddFile(path);
        present.Quality["src/a"] = new("opaque", path);
        PythonQualityBinding[] quality = [new("check-a", "src/a", ["src/a/pyproject.toml"])];
        PythonOwnerEndpoint before = new(absent.Revision, absent.Projects, quality, absent.Map());
        PythonOwnerEndpoint after = new(present.Revision, present.Projects, quality, present.Map());
        PythonOwnerImpact impact = Assert.ContainsSingle(PythonOwnerProjection.Project(
            added ? before : after, added ? after : before, [path]));
        Assert.AreEqual(path, impact.Path);
        Assert.AreEqual(Next, impact.Revision);
        string[] expected = ["check-a"];
        CollectionAssert.AreEqual(expected, impact.Owners);
    }

    [TestMethod]
    public void MappingIsDeterministic()
    {
        var fixture = new Fixture();
        PythonInputBinding[] first = fixture.Map();
        fixture.Projects = fixture.Projects.Reverse().ToArray();
        fixture.Passive = fixture.Passive.Reverse().ToArray();
        fixture.Configuration = fixture.Configuration.Reverse().Concat(fixture.Configuration)
            .ToArray();
        fixture.Versions = [fixture.Versions[0] with
        {
            Paths = ["shared/version.json", "shared/version.json"],
            ConfigurationCandidates = ["shared/version.txt", "version.txt", "version.txt"]
        }];
        PythonInputBinding[] second = fixture.Map();
        CollectionAssert.AreEqual(first.Select(input => input.Path).ToArray(),
            second.Select(input => input.Path).ToArray());
        foreach (PythonInputBinding input in first)
            Members(second, input.Path, input.Members);
    }

    [TestMethod]
    public void MappedVersionInputTraversesMixedEndpoints()
    {
        var before = new Fixture();
        var after = new Fixture();
        after.Revision = after.Revision with { Commit = Next };
        after.Versions = [after.Versions[0] with { Commit = Next }];
        before.Projects = [Project("."), Project("src/a"),
            Project("src/b") with { Ordinary = ["src/a"] }, Project("src/c")];
        after.Projects = [Project("."), Project("src/a"), Project("src/b"),
            Project("src/c") with { Build = ["src/b"] }];
        foreach (Fixture fixture in new[] { before, after })
        {
            fixture.AddFile("src/c/pyproject.toml");
            fixture.Passive = [Passive(fixture.Root, "."),
                Passive(fixture.Root, "src/a", "shared"), Passive(fixture.Root, "src/b"),
                Passive(fixture.Root, "src/c")];
            fixture.Configuration = fixture.Projects.Select(project =>
                fixture.Absolute(Manifest(project.Directory))).ToArray();
            fixture.Quality["src/c"] = null;
        }
        PythonQualityBinding[] quality = [new("check-c", "src/c", ["src/c/pyproject.toml"])];
        PythonOwnerImpact[] impacts = PythonOwnerProjection.Project(
            new(before.Revision, before.Projects, quality, before.Map()),
            new(after.Revision, after.Projects, quality, after.Map()), ["shared/version.json"]);
        Assert.HasCount(2, impacts);
        foreach (PythonOwnerImpact impact in impacts)
        {
            Assert.AreEqual("shared/version.json", impact.Path);
            string[] expected = ["check-c"];
            CollectionAssert.AreEqual(expected, impact.Owners);
        }
        string[] revisions = [Source, Next];
        CollectionAssert.AreEqual(revisions, impacts.Select(impact => impact.Revision).ToArray());
    }

    [TestMethod]
    [DataRow("missing-root")]
    [DataRow("duplicate-native")]
    [DataRow("missing-passive")]
    [DataRow("duplicate-passive")]
    [DataRow("foreign-passive")]
    [DataRow("missing-quality")]
    [DataRow("foreign-quality")]
    [DataRow("foreign-quality-source")]
    [DataRow("missing-version")]
    [DataRow("duplicate-version")]
    [DataRow("unused-version")]
    [DataRow("foreign-revision")]
    [DataRow("foreign-working-directory")]
    [DataRow("foreign-generated-file")]
    [DataRow("missing-configuration")]
    [DataRow("outside-configuration")]
    [DataRow("internal-nonmember-configuration")]
    [DataRow("duplicate-git-entry")]
    [DataRow("invalid-native-coordinate")]
    [DataRow("invalid-version-coordinate")]
    [DataRow("invalid-candidate")]
    [DataRow("relative-root")]
    public void InvalidSuppliedSubjectsFail(string scenario)
    {
        var fixture = new Fixture();
        switch (scenario)
        {
            case "missing-root": fixture.Projects = fixture.Projects[1..]; break;
            case "duplicate-native":
                fixture.Projects = [.. fixture.Projects, fixture.Projects[1]];
                break;
            case "missing-passive": fixture.Passive = fixture.Passive[1..]; break;
            case "duplicate-passive":
                fixture.Passive = [.. fixture.Passive, fixture.Passive[1]];
                break;
            case "foreign-passive":
                fixture.Passive[1] = Passive(fixture.Root, "src/unknown"); break;
            case "missing-quality": fixture.Quality.Remove("src/a"); break;
            case "foreign-quality":
                fixture.Quality.Remove("src/a"); fixture.Quality["src/x"] = null;
                break;
            case "foreign-quality-source":
                fixture.AddFile("src/b/" + Declaration);
                fixture.Quality["src/a"] = new("opaque", "src/b/" + Declaration); break;
            case "missing-version": fixture.Versions = []; break;
            case "duplicate-version":
                fixture.Versions = [fixture.Versions[0], fixture.Versions[0]];
                break;
            case "unused-version":
                fixture.Versions = [.. fixture.Versions,
                new(Source, ".", [], [])]; break;
            case "foreign-revision":
                fixture.Versions[0] = fixture.Versions[0] with { Commit = Next };
                break;
            case "foreign-working-directory":
                fixture.Passive[1] = fixture.Passive[1] with
                { Nbgv = new(Path.GetDirectoryName(fixture.Root)!, null) }; break;
            case "foreign-generated-file":
                fixture.Passive[1] = fixture.Passive[1] with
                {
                    Nbgv = new(fixture.Absolute("shared"), Path.Combine(
                    Path.GetDirectoryName(fixture.Root)!, "external.py"))
                }; break;
            case "missing-configuration": fixture.Configuration = fixture.Configuration[1..]; break;
            case "outside-configuration":
                fixture.Configuration = [.. fixture.Configuration,
                Path.Combine(Path.GetDirectoryName(fixture.Root)!, "pyproject.toml")]; break;
            case "internal-nonmember-configuration":
                fixture.Configuration = [.. fixture.Configuration,
                fixture.Absolute("src/pyproject.toml")]; break;
            case "duplicate-git-entry": fixture.AddFile("pyproject.toml"); break;
            case "invalid-native-coordinate": fixture.Projects[1] = Project("src/../a"); break;
            case "invalid-version-coordinate":
                fixture.Versions[0] = fixture.Versions[0] with
                { Directory = "../shared" }; break;
            case "invalid-candidate":
                fixture.Versions[0] = fixture.Versions[0] with
                { ConfigurationCandidates = ["../version.json"] }; break;
            case "relative-root": fixture.Root = "relative"; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Map());
    }

    [TestMethod]
    [DataRow("pyproject.toml", "missing")]
    [DataRow("src/a/pyproject.toml", "symlink")]
    [DataRow("shared/version.json", "missing")]
    [DataRow("shared/version.json", "gitlink")]
    [DataRow("version.txt", "symlink")]
    [DataRow("shared/generated.py", "gitlink")]
    [DataRow("src/a/" + Declaration, "missing")]
    [DataRow("src/a/" + Declaration, "symlink")]
    [DataRow("src/" + Declaration, "gitlink")]
    public void RequiredInputsMustBeCommittedRegularFiles(string path, string kind)
    {
        var fixture = new Fixture();
        if (path == "src/a/" + Declaration) fixture.Quality["src/a"] = new("opaque", path);
        fixture.Revision = fixture.Revision with
        {
            Entries = fixture.Revision.Entries.Where(entry => entry.Path != path).Concat(
                kind == "missing" ? [] : new[] { new GitEntry(path,
                    kind == "symlink" ? "120000" : "160000",
                    kind == "symlink" ? "blob" : "commit") })
                .ToArray()
        };
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Map());
    }

    private static void Members(PythonInputBinding[] inputs, string path, params string[] expected)
    {
        PythonInputBinding input = Assert.ContainsSingle(inputs.Where(input => input.Path == path));
        CollectionAssert.AreEqual(expected, input.Members);
        Assert.IsEmpty(input.Owners);
    }

    private static PythonProjectDependencies Project(string directory) => new(directory,
        directory == "." ? "root" : directory.Replace('/', '-'), [], []);

    private static string Manifest(string directory) => directory == "." ? "pyproject.toml" :
        directory + "/pyproject.toml";

    private static PythonPassiveInputs Passive(string root, string directory,
        string? versionDirectory = null) => new(new(directory == "." ? root :
            Path.Combine(root, directory), null), versionDirectory is null ? null :
            new(versionDirectory == "." ? root : Path.Combine(root, versionDirectory),
                Path.Combine(root, "shared", "generated.py")));

    private sealed class Fixture
    {
        internal string Root { get; set; } = Path.Combine(Path.GetTempPath(),
            "python-input-endpoint");
        internal GitRevision Revision { get; set; } = new(Source,
            [File("pyproject.toml"), File("src/a/pyproject.toml"), File("src/b/pyproject.toml"),
                File("shared/version.json")]);
        internal PythonProjectDependencies[] Projects { get; set; } =
            [Project("."), Project("src/a"), Project("src/b")];
        internal PythonPassiveInputs[] Passive { get; set; }
        internal NbgvInputs[] Versions { get; set; } = [new(Source, "shared",
            ["version.txt", "shared/version.txt"], ["shared/version.json"])];
        internal Dictionary<string, QualitySelection?> Quality { get; } =
            new(StringComparer.Ordinal)
            {
                ["."] = null,
                ["src/a"] = null,
                ["src/b"] = null
            };
        internal string[] Configuration { get; set; }
        internal Fixture()
        {
            Passive = [PythonInputConsumersTests.Passive(Root, "."),
                PythonInputConsumersTests.Passive(Root, "src/a", "shared"),
                PythonInputConsumersTests.Passive(Root, "src/b", "shared")];
            Configuration = Projects.Select(project => Absolute(Manifest(project.Directory)))
                .ToArray();
        }
        internal string Absolute(string path) => Path.Combine(Root, path);
        internal void AddFile(string path) => Revision = Revision with
        {
            Entries = [.. Revision.Entries, File(path)]
        };
        internal PythonInputBinding[] Map() => PythonInputConsumers.Map(Revision, Root, Projects,
            Passive, Versions, Quality, Configuration);
        private static GitEntry File(string path) => new(path, "100644", "blob");
    }
}
