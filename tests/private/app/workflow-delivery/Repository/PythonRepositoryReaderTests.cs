using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonRepositoryReaderTests(TestContext context)
{
    private static readonly string[] NativeDirectories = [".", "src/a", "src/b", "src/c"];
    private static readonly string[] ImportOptions = ["--import-mode=importlib"];
    private const string QualityPath = "src/c/workflow-delivery.quality.yml";
    private const string UnitPath = "src/c/workflow-delivery.release-unit.yml";
    private const string MissingQuality = "Python distribution lacks adopted quality";

    // Rows that vary only controlled native answers read one class-owned committed checkout.
    private static GitFixture? shared;
    private static GitRevision? sharedRevision;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        CancellationToken token = classContext.CancellationToken;
        shared = await GitFixture.CreateAsync(token);
        try
        {
            await Fixture.StageAsync(shared);
            string commit = await shared.CommitAsync();
            await shared.GitAsync("reset", "--hard", commit);
            sharedRevision = await new GitReader(shared.Directory).ReadAsync(commit, token);
        }
        catch { shared.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task SharedCheckoutQualifiesAdoptedDistribution()
    {
        // Defect-free control: the shared checkout collects the adopted package obligations.
        using var fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        PythonRevisionInputs inputs = await fixture.ReadAsync(context.CancellationToken);
        Assert.HasCount(4, Assert.ContainsSingle(inputs.Facts.Packages).Checks);
        Assert.HasCount(2, fixture.Commands);
    }

    [TestMethod]
    public async Task CollectsNativeTargetsAdoptedDistributionAndInputOnlyProducers()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonRevisionInputs inputs = await fixture.ReadAsync(context.CancellationToken);
        Assert.AreSame(fixture.Native, inputs.Graph);
        Assert.AreEqual(fixture.Revision.Commit, inputs.Facts.Revision.Commit);
        Assert.HasCount(4, inputs.Facts.Projects);
        ProjectFacts package = Assert.ContainsSingle(inputs.Facts.Packages);
        Assert.AreEqual("src/c", package.Directory);
        Assert.AreEqual("product", package.ReleaseUnit);
        Assert.AreEqual(PythonPackageChecks.Preset, package.QualityPreset);
        CiPlan plan = PythonGroupPlanning.Plan(inputs, inputs,
            new(fixture.Revision, fixture.Revision, [UnitPath]), false);
        CheckSpec[] checks = plan.Checks.Select(check => check.Work)
            .Where(work => work.Package is not null).ToArray();
        CollectionAssert.AreEquivalent(PythonPackageChecks.Kinds,
            checks.Select(check => check.Key.Check).ToArray());
        CheckSpec build = checks.Single(check => check.Key.Check == PythonPackageChecks.Build);
        CheckSpec contents = checks.Single(check =>
            check.Key.Check == PythonPackageChecks.Contents);
        CheckKey[] consumerPrerequisites = [build.Key, contents.Key];
        PackageOutput[] outputs =
        [
            new("wheel", "primary-package", "python-wheel"),
            new("sdist", "source-package", "python-sdist"),
        ];
        Assert.AreEqual(build.Key, Assert.ContainsSingle(contents.Prerequisites));
        foreach (CheckSpec check in checks)
        {
            Assert.AreEqual("release/product/distributions", check.Key.Target);
            Assert.AreEqual("src/c/pyproject.toml", check.Package!.EntryPoint);
            Assert.AreEqual("1.2.3", check.Package.ExpectedVersion);
            CollectionAssert.AreEqual(outputs, check.Package.Outputs);
            if (check.Key.Check is PythonPackageChecks.Wheel or PythonPackageChecks.Sdist)
                CollectionAssert.AreEqual(consumerPrerequisites, check.Prerequisites);
        }
        Assert.AreEqual("src/c/tests", Assert.ContainsSingle(inputs.Targets.Targets).Target);
        Assert.AreEqual("src/c", Assert.ContainsSingle(inputs.Targets.Targets).Member);
        Assert.IsFalse(inputs.Facts.Packages.Any(package => package.Directory == "src/b"));
        Assert.Contains("src/b", inputs.Facts.Inputs.Single(input =>
            input.Path == "src/b/pyproject.toml").Members);
        Assert.AreEqual("-I", fixture.Commands[1].Arguments[0]);
        Assert.AreEqual(fixture.Request.PackageHelper, fixture.Commands[1].Arguments[1]);
        Assert.AreEqual("identity", fixture.Commands[1].Arguments[2]);
        CollectionAssert.AreEquivalent(NativeDirectories,
            fixture.Operations.Select(operation => operation.Id).ToArray());
        foreach (PythonGroupOperation operation in fixture.Operations)
        {
            Assert.IsEmpty(operation.Packages);
            Assert.IsFalse(operation.NoDev);
        }
    }

    [TestMethod]
    [DataRow(".")]
    [DataRow("src/a/../..")]
    public async Task NativeVersionCoordinatesSelectOnlyTransitiveQualityConsumers(string working)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        await fixture.UpdateAsync("version.json", """
            { "version": "1.2", "pathFilters": ["src/a", "version.json"] }
            """, context.CancellationToken);
        fixture.VersionPlugin(working, "src/a/generated.py");
        PythonRevisionInputs inputs = await fixture.ReadAsync(context.CancellationToken);
        Assert.AreEqual("src/a", Assert.ContainsSingle(inputs.Facts.Inputs.Single(input =>
            input.Path == "src/a/generated.py").Members));
        CiPlan plan = PythonGroupPlanning.Plan(inputs, inputs,
            new(fixture.Revision, fixture.Revision, ["version.json"]), false);
        Assert.HasCount(5, plan.Checks);
        foreach (PlannedCheck check in plan.Checks)
            Assert.AreEqual("version.json", Assert.ContainsSingle(check.Reasons).Path);
    }

    [TestMethod]
    [DataRow(".", "../generated.py", "Expected a normalized repository-relative path:")]
    [DataRow("..", "src/a/generated.py", "Python coordinate is outside its endpoint.")]
    public async Task NativeVersionCoordinatesMustStayInsideTheRepository(
        string working, string generated, string diagnostic)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        await fixture.UpdateAsync("version.json", "{\"version\":\"1.2\"}",
            context.CancellationToken);
        fixture.VersionPlugin(working, generated);

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains(diagnostic, failure.Message);
    }

    [TestMethod]
    public async Task NativeConfigurationInventoryMustIncludeEveryMember()
    {
        using var fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.Native = fixture.Native with
        {
            ConfigurationInputs = fixture.Native.ConfigurationInputs[1..],
        };

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains("Incomplete or unsupported native configuration inputs.", failure.Message);
    }

    [TestMethod]
    [DataRow(null, "Required native Python input is not committed.")]
    [DataRow("120000", "Python inputs require regular committed files.")]
    public void NativeVersionInputMustBeACommittedRegularFile(string? mode, string diagnostic)
    {
        using var fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.VersionPlugin(".", "src/a/generated.py");
        const string path = "src/a/source.py";
        GitRevision revision = fixture.Revision with
        {
            Entries = fixture.Revision.Entries
                .Where(entry => entry.Path != path || mode is not null)
                .Select(entry => entry.Path == path ? entry with { Mode = mode! } : entry)
                .ToArray(),
        };
        var quality = fixture.Native.Projects.ToDictionary(project => project.Directory,
            project => project.Directory == "src/c"
                ? new QualitySelection(PythonPackageChecks.Preset, QualityPath) : null,
            StringComparer.Ordinal);

        InvalidDataException failure = Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonInputConsumers.Map(revision, fixture.Root, fixture.Native.Projects,
                fixture.Native.Passive, [new(revision.Commit, ".", [], [path])], quality,
                fixture.Native.ConfigurationInputs));
        Assert.AreEqual(diagnostic, failure.Message);
    }

    [TestMethod]
    [DataRow(QualityPath, null, MissingQuality)]
    [DataRow(UnitPath, null, "Adopted Python quality lacks a complete distribution")]
    [DataRow(UnitPath, "not a declaration", "Expected a release-unit mapping")]
    public async Task MissingOrUnreadableAdoptionCannotEraseDistributionObligations(
        string path, string? content, string diagnostic)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        await fixture.UpdateAsync(path, content, context.CancellationToken);

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains(diagnostic, failure.Message);
    }

    [TestMethod]
    [DataRow("python-sdist", "unknown-output", "Unsupported complete Python distribution set")]
    [DataRow("pyproject.toml", "missing/pyproject.toml", "has no native member")]
    public async Task UnresolvableDistributionCannotSupplyWork(
        string original, string replacement, string diagnostic)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        await fixture.UpdateAsync(UnitPath,
            Fixture.Unit.Replace(original, replacement, StringComparison.Ordinal),
            context.CancellationToken);

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.Contains(diagnostic, failure.Message);
    }

    [TestMethod]
    [DataRow("nbgv-python")]
    [DataRow("unregistered-product")]
    public async Task OnlyRegisteredDistributionKeepsItsAdoptedObligations(string package)
    {
        // The committed nbgv-python declaration and quality selection are read as adopted.
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        string real = Path.Combine(RepositoryToolSelection.SourceRoot, "src", "public", "lib",
            "nbgv-python");
        foreach (string file in new[] { "workflow-delivery.quality.yml",
                     "workflow-delivery.release-unit.yml" })
            await fixture.UpdateAsync("src/c/" + file, await File.ReadAllTextAsync(
                Path.Combine(real, file), context.CancellationToken), context.CancellationToken);
        fixture.Answer(package);
        fixture.Identity = Success("{\"name\":\"" + package + "\",\"version\":\"2.1.0.dev1\"}");
        if (package != "nbgv-python")
        {
            PythonRevisionInputs unregistered = await fixture.ReadAsync(context.CancellationToken);
            Assert.AreEqual("Unsupported Python Release unit.",
                Assert.ThrowsExactly<InvalidDataException>(() =>
                    PythonRepositoryReader.RequireRegisteredUnits(unregistered)).Message);
            return;
        }
        PythonRevisionInputs inputs = await fixture.ReadAsync(context.CancellationToken);
        PythonRepositoryReader.RequireRegisteredUnits(inputs);
        ProjectFacts owner = Assert.ContainsSingle(inputs.Facts.Packages);
        Assert.AreEqual("nbgv-python", owner.ReleaseUnit);
        Assert.AreEqual(PythonPackageChecks.Preset, owner.QualityPreset);
        CollectionAssert.AreEquivalent(PythonPackageChecks.Kinds, owner.Checks.Select(check =>
            check.Key.Check).ToArray());
        Assert.IsTrue(owner.Checks.All(check => check.Required &&
            check.Package!.ExpectedVersion == "2.1.0.dev1"));
        Assert.AreEqual("src/c/tests", Assert.ContainsSingle(inputs.Targets.Targets).Target);
    }

    [TestMethod]
    public async Task RetiredBasisPythonUnitReadsCleanlyWhileUnregisteredCandidateUnitFails()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        const string retired = "retired-python-unit";
        await fixture.UpdateAsync("src/c/workflow-delivery.release-unit.yml",
            Fixture.Unit.Replace("product", retired, StringComparison.Ordinal),
            context.CancellationToken);
        fixture.Answer(retired);
        fixture.Identity = Success("{\"name\":\"" + retired + "\",\"version\":\"1.0.0\"}");

        PythonRevisionInputs basis = await fixture.ReadAsync(context.CancellationToken);
        Assert.AreEqual(retired, Assert.ContainsSingle(basis.Facts.Packages).ReleaseUnit);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonRepositoryReader.RequireRegisteredUnits(basis));

        await fixture.UpdateAsync("src/c/workflow-delivery.release-unit.yml", null,
            context.CancellationToken);
        await fixture.UpdateAsync("src/c/workflow-delivery.quality.yml", null,
            context.CancellationToken);
        PythonRevisionInputs candidate = await fixture.ReadAsync(context.CancellationToken);
        PythonRepositoryReader.RequireRegisteredUnits(candidate);
        CiPlan plan = PythonGroupPlanning.Plan(basis, candidate,
            new(basis.Facts.Revision, candidate.Facts.Revision,
                ["src/c/workflow-delivery.release-unit.yml",
                    "src/c/workflow-delivery.quality.yml"]), false);
        Assert.IsEmpty(plan.Checks.Where(c => c.Work.Package is not null));
    }

    [TestMethod]
    [DataRow(1, "")]
    [DataRow(0, "not json")]
    [DataRow(0, "{\"name\":\"other\",\"version\":\"1.2.3\"}")]
    public async Task UnusableNativeIdentityCannotSupplyAVersion(int exitCode, string stdout)
    {
        using var fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.Identity = Success(stdout) with { ExitCode = exitCode };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
    }

    [TestMethod]
    public async Task ForeignGraphCannotSupplyPythonWork()
    {
        using var fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.Native = fixture.Native with
        { Interpreter = Path.Combine(fixture.Root, "other-python") };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow(1, "src/c/tests")]
    [DataRow(0, "tests/missing.py")]
    public async Task FailedPytestDiscoveryCannotSupplyWork(int exitCode, string target)
    {
        using var fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.PytestExitCode = exitCode;
        fixture.Target = target;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(fixture.Commands.Any(command => command.Arguments.Contains("identity")));
    }

    [TestMethod]
    [DataRow("addopts", null)]
    [DataRow("configuration_file", "\"pyproject.toml\"")]
    [DataRow("testpaths", "[]")]
    [DataRow("testpaths", "[\"tests\",\"tests\"]")]
    [DataRow("testpaths", "[\"tests/*\"]")]
    [DataRow("testpaths", "[\"../tests\"]")]
    [DataRow("testpaths", "\"tests\"")]
    [DataRow("addopts", "[\"-p\",\"custom-plugin\"]")]
    [DataRow("pythonpath", "[\"src/external\"]")]
    [DataRow("required_plugins", "[\"custom-plugin\"]")]
    public async Task InvalidNativeConfigurationCannotCertifyTargets(string field, string? json)
    {
        using Fixture fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.EditPytestAnswer = answer =>
        {
            if (json is null) answer.Remove(field);
            else answer[field] = JsonNode.Parse(json);
        };
        await RejectPytestAnswerAsync(fixture);
    }

    [TestMethod]
    [DataRow("directory", ".")]
    [DataRow("root", ".")]
    [DataRow("configuration_file", "pytest.ini")]
    public async Task ForeignPytestCoordinatesCannotCertifyTargets(string field, string adjacent)
    {
        using Fixture fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.EditPytestAnswer = answer =>
            answer[field] = Path.GetFullPath("../" + adjacent, fixture.Root);
        await RejectPytestAnswerAsync(fixture);
    }

    [TestMethod]
    public async Task AbsolutePytestTargetsCannotSupplyWork()
    {
        using Fixture fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.EditPytestAnswer = answer => answer["testpaths"] = new JsonArray(fixture.Root);
        await RejectPytestAnswerAsync(fixture);
    }

    [TestMethod]
    public async Task TruncatedPytestAnswerCannotSupplyWork()
    {
        using Fixture fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        fixture.RawPytestAnswer = "{";
        await RejectPytestAnswerAsync(fixture);
    }

    private async Task RejectPytestAnswerAsync(Fixture fixture)
    {
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.ContainsSingle(fixture.Commands);
    }

    [TestMethod]
    public async Task CancellationAfterGraphStopsBeforeFurtherNativeQueries()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.AfterGraph = cancellation.Cancel;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.ReadAsync(cancellation.Token));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationDuringPytestQueryPreservesCancellationAndStopsIdentity(
        bool successfulResultRace)
    {
        using var fixture = Fixture.Shared(shared!.Directory, sharedRevision!);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.DuringPytest = cancellation.Cancel;
        fixture.CancelledPytestResult = !successfulResultRace;

        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.ReadAsync(cancellation.Token));

        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        NativeCommand query = Assert.ContainsSingle(fixture.Commands);
        Assert.DoesNotContain("identity", query.Arguments);
    }

    [TestMethod]
    public async Task PairedRemovedProducerEdgeRetainsOriginalReasonAndCompleteChecks()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonRevisionInputs basis = await fixture.ReadAsync(context.CancellationToken);
        await fixture.UpdateAsync("src/a/source.py", "changed", context.CancellationToken);
        fixture.Native = fixture.Native with
        {
            Projects = fixture.Native.Projects.Select(project => project.Directory == "src/b" ?
            project with { Ordinary = [] } : project).ToArray()
        };
        PythonRevisionInputs candidate = await fixture.ReadAsync(context.CancellationToken);
        CiPlan plan = PythonGroupPlanning.Plan(basis, candidate,
            new(basis.Facts.Revision, candidate.Facts.Revision, ["src/a/source.py"]), false);
        Assert.HasCount(5, plan.Checks);
        Assert.AreEqual(candidate.Facts.Revision.Commit, plan.Candidate);
        foreach (PlannedCheck check in plan.Checks)
        {
            Assert.Contains(new SelectionReason("src/a/source.py", basis.Facts.Revision.Commit,
                check.Work.Key.Target.StartsWith("pytest:", StringComparison.Ordinal) ?
                    "pytest:src/c/tests" : "python-package:src/c"), check.Reasons);
            Assert.IsTrue(check.Work.Required);
        }
        Assert.AreEqual(4, plan.Checks.Count(check => check.Work.Package is not null));
    }

    [TestMethod]
    public async Task KnownUnrelatedInputProducesEmptyIncrementalButExplicitFullKeepsAllChecks()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonRevisionInputs inputs = await fixture.ReadAsync(context.CancellationToken);
        var comparison = new GitComparison(fixture.Revision, fixture.Revision,
            ["src/a/source.py"]);
        inputs = inputs with
        {
            Facts = inputs.Facts with
            {
                Projects = inputs.Facts.Projects.Select(project => project with
                { Ordinary = [], Build = [] }).ToArray()
            }
        };
        Assert.IsEmpty(PythonGroupPlanning.Plan(inputs, inputs, comparison, false).Checks);
        Assert.HasCount(5, PythonGroupPlanning.Plan(inputs, inputs, comparison, true).Checks);
    }

    [TestMethod]
    public async Task UnresolvedChangeAndForeignComparisonCannotBecomeSuccessfulNoWork()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        await fixture.UpdateAsync("unknown.txt", "unresolved", context.CancellationToken);
        PythonRevisionInputs inputs = await fixture.ReadAsync(context.CancellationToken);
        Assert.ThrowsExactly<InvalidDataException>(() => PythonGroupPlanning.Plan(inputs,
            inputs, new(fixture.Revision, fixture.Revision, ["unknown.txt"]), false));
        Assert.ThrowsExactly<InvalidDataException>(() => PythonGroupPlanning.Plan(inputs,
            inputs, new(fixture.Revision with { Commit = new('a', 40) }, fixture.Revision, []),
            false));
    }

    private static NativeCommandResult Success(string stdout) =>
        new(NativeTermination.Exited, 0, stdout, "", 0, null);

    private sealed class Fixture(GitFixture? repo, string root) : IDisposable
    {
        private readonly GitFixture? repository = repo;
        internal const string Quality = """
            schema: workflow-delivery/v3/quality-selection
            ecosystems:
              python:
                preset: python/hcoona-release-smoke-python-v1
            """;
        internal const string Unit = """
            schema: workflow-delivery/v3/release-unit
            release-unit: product
            builds:
              - id: distributions
                definition: python/distribution-set-v1
                entry-point: pyproject.toml
                outputs:
                  - id: wheel
                    role: primary-package
                    kind: python-wheel
                  - id: sdist
                    role: source-package
                    kind: python-sdist
            """;
        internal string Root { get; } = root;
        internal GitRevision Revision { get; private set; } = null!;
        internal PythonNativeGraph Native { get; set; } = null!;
        internal PythonRepositoryRequest Request { get; private set; } = null!;
        internal PythonGroupOperation[] Operations { get; private set; } = [];
        internal List<NativeCommand> Commands { get; } = [];
        internal NativeCommandResult Identity { get; set; } =
            Success("""{"name":"hcoona-release-smoke-python","version":"1.2.3"}""");
        internal int PytestExitCode { get; set; }
        internal string Target { get; set; } = "src/c/tests";
        internal Action? AfterGraph { get; set; }
        internal Action? DuringPytest { get; set; }
        internal bool CancelledPytestResult { get; set; }
        internal Action<JsonObject>? EditPytestAnswer { get; set; }
        internal string? RawPytestAnswer { get; set; }

        internal static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            var fixture = new Fixture(repo, repo.Directory);
            try
            {
                await StageAsync(repo);
                await fixture.RefreshAsync(token);
                fixture.Answer();
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        // Reads a class-owned committed checkout; the row owns only its controlled answers.
        internal static Fixture Shared(string root, GitRevision revision)
        {
            var fixture = new Fixture(null, root) { Revision = revision };
            fixture.Answer();
            return fixture;
        }

        internal static async Task StageAsync(GitFixture repository)
        {
            foreach (string directory in NativeDirectories)
                await repository.SetAsync(directory == "." ? "pyproject.toml" :
                    directory + "/pyproject.toml", "native fixture");
            await repository.SetAsync("src/a/source.py");
            await repository.SetAsync("src/c/tests/test_one.py");
            await repository.SetAsync("src/c/workflow-delivery.quality.yml", Quality);
            await repository.SetAsync("src/c/workflow-delivery.release-unit.yml", Unit);
        }

        // Answers native facts whose src/c member is the named distribution.
        internal void Answer(string package = "hcoona-release-smoke-python")
        {
            PythonProjectDependencies[] projects = [new(".", "root", [], []),
                new("src/a", "a", [], []), new("src/b", "b", ["src/a"], []),
                new("src/c", package, ["src/b"], [])];
            var metadata = new PythonMetadata(Root, "workspace", ">=3.14",
                projects.Select(project => new PythonMetadataMember(project.Name,
                    project.Directory, project.Name)).ToArray(),
                new Dictionary<string, PythonMetadataNode>());
            string interpreter = Path.Combine(Root, "interpreter");
            Native = new(metadata, interpreter, projects.Select(project =>
                new PythonPassiveInputs(new(Path.GetFullPath(Path.Combine(Root,
                    project.Directory)), null), null)).ToArray(), [], projects,
                projects.Select(project => Path.Combine(Root, project.Directory,
                    "pyproject.toml")).ToArray());
            Request = new(new(Root, "uv", interpreter, interpreter,
                Path.Combine(Root, "passive.py")), "pytest_configuration.py", "packages.py",
                new("ubuntu-latest", "python-3.14", new()
                { ["python"] = "3.14", ["platform"] = "linux" }));
        }

        internal async Task UpdateAsync(string path, string? content, CancellationToken token)
        {
            if (content is null) await repository!.RemoveAsync(path);
            else await repository!.SetAsync(path, content);
            await RefreshAsync(token);
        }

        internal void VersionPlugin(string working, string generated) => Native = Native with
        {
            Passive = Native.Passive.Select(input => input.Project.Directory ==
                Path.Combine(Root, "src", "a") ? input with
                {
                    Nbgv = new(Path.Combine(Root, working), Path.Combine(Root, generated)),
                } : input).ToArray(),
        };

        private async Task RefreshAsync(CancellationToken token)
        {
            string commit = await repository!.CommitAsync(Revision is null ? [] :
                [Revision.Commit]);
            await repository.GitAsync("reset", "--hard", commit);
            Revision = await new GitReader(Root).ReadAsync(commit, token);
        }

        internal async Task<PythonRevisionInputs> ReadAsync(CancellationToken token)
        {
            GitMaterialization checkout = await GitMaterialization.BindAsync(Root, Revision, token);
            return await new PythonRepositoryReader(checkout, (request, factory, environment,
                cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                Assert.AreSame(Request.Graph, request);
                Operations = factory(Native.Metadata);
                AfterGraph?.Invoke();
                return Task.FromResult(Native);
            }, (command, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                Commands.Add(command);
                if (command.Arguments.Contains("identity")) return Task.FromResult(Identity);
                DuringPytest?.Invoke();
                if (CancelledPytestResult)
                    return Task.FromResult(new NativeCommandResult(NativeTermination.Cancelled,
                        null, "", "", 0, "Controlled in-flight cancellation."));
                var answer = new JsonObject
                {
                    ["directory"] = Root,
                    ["root"] = Root,
                    ["configuration_file"] = Path.Combine(Root, "pyproject.toml"),
                    ["testpaths"] = new JsonArray(Target),
                    ["addopts"] = new JsonArray(ImportOptions.Select(option =>
                        (JsonNode?)JsonValue.Create(option)).ToArray()),
                    ["pythonpath"] = new JsonArray(),
                    ["required_plugins"] = new JsonArray(),
                };
                EditPytestAnswer?.Invoke(answer);
                return Task.FromResult(Success(RawPytestAnswer ?? answer.ToJsonString()) with
                { ExitCode = PytestExitCode });
            }).ReadAsync(Request, new Dictionary<string, string?>(), token);
        }

        public void Dispose() => repository?.Dispose();
    }
}
