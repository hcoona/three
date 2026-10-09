using System.Text.Json;
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
        Assert.HasCount(4, package.Checks);
        foreach (CheckSpec check in package.Checks)
            Assert.AreEqual("1.2.3", check.Package!.ExpectedVersion);
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
    public async Task NativeVersionCoordinatesSelectOnlyTheirTransitiveQualityConsumers()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        await fixture.UpdateAsync("version.json", """
            { "version": "1.2", "pathFilters": ["src/a", "version.json"] }
            """, context.CancellationToken);
        fixture.Native = fixture.Native with
        {
            Passive = fixture.Native.Passive.Select(input => input.Project.Directory ==
                Path.Combine(fixture.Root, "src/a") ? input with
                { Nbgv = new(fixture.Root, Path.Combine(fixture.Root, "src/a/generated.py")) } :
                input).ToArray()
        };
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
    [DataRow("missing-quality")]
    [DataRow("missing-unit")]
    [DataRow("unsupported-preset")]
    [DataRow("unsupported-output")]
    [DataRow("mixed-unit")]
    [DataRow("foreign-entry")]
    [DataRow("malformed-unit")]
    public async Task MissingOrUnsupportedAdoptionCannotEraseDistributionObligations(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        string quality = "src/c/workflow-delivery.quality.yml";
        string unit = "src/c/workflow-delivery.release-unit.yml";
        if (defect == "missing-quality")
            await fixture.RemoveAsync(quality, context.CancellationToken);
        else if (defect == "missing-unit")
            await fixture.RemoveAsync(unit, context.CancellationToken);
        else if (defect == "unsupported-preset")
            await fixture.UpdateAsync(quality, Fixture.Quality.Replace(PythonPackageChecks.Preset,
                "python/unknown-v1", StringComparison.Ordinal), context.CancellationToken);
        else await fixture.UpdateAsync(unit, defect switch
        {
            "unsupported-output" => Fixture.Unit.Replace("python-sdist", "unknown-output",
                StringComparison.Ordinal),
            "mixed-unit" => Fixture.Unit + """

                  - id: foreign
                    definition: node/foreign
                    entry-point: package.json
                    outputs:
                      - id: foreign
                        role: primary-package
                        kind: npm-tgz
                """,
            "foreign-entry" => Fixture.Unit.Replace("pyproject.toml", "missing/pyproject.toml",
                StringComparison.Ordinal),
            _ => "not a declaration"
        }, context.CancellationToken);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
    }

    [TestMethod]
    [DataRow("failed")]
    [DataRow("timed-out")]
    [DataRow("invalid-json")]
    [DataRow("foreign-name")]
    [DataRow("empty-version")]
    public async Task NativeIdentityFailureCannotSupplyAnInventedVersion(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Identity = defect switch
        {
            "failed" => new(NativeTermination.Exited, 1, "", "", 0, null),
            "timed-out" => new(NativeTermination.TimedOut, null, "", "", 30, "deadline"),
            "invalid-json" => Success("not json"),
            "foreign-name" => Success("{\"name\":\"other\",\"version\":\"1.2.3\"}"),
            _ => Success("{\"name\":\"c\",\"version\":\"\"}")
        };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.HasCount(2, fixture.Commands);
    }

    [TestMethod]
    [DataRow("graph-error")]
    [DataRow("foreign-root")]
    [DataRow("foreign-interpreter")]
    [DataRow("pytest-error")]
    [DataRow("pytest-foreign-target")]
    public async Task RequiredNativeFactFailureStopsCollection(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        if (defect == "graph-error") fixture.GraphError = true;
        else if (defect == "foreign-root") fixture.Native = fixture.Native with
        { Metadata = fixture.Native.Metadata with { Root = Path.GetTempPath() } };
        else if (defect == "foreign-interpreter") fixture.Native = fixture.Native with
        { Interpreter = Path.Combine(fixture.Root, "other-python") };
        else if (defect == "pytest-error") fixture.PytestError = true;
        else fixture.Target = "tests/missing.py";
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.IsFalse(fixture.Commands.Any(command => command.Arguments.Contains("identity")));
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
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
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
        { Projects = fixture.Native.Projects.Select(project => project.Directory == "src/b" ?
            project with { Ordinary = [] } : project).ToArray() };
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
        inputs = inputs with { Facts = inputs.Facts with
        { Projects = inputs.Facts.Projects.Select(project => project with
            { Ordinary = [], Build = [] }).ToArray() } };
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

    private sealed class Fixture(GitFixture repo) : IDisposable
    {
        private readonly GitFixture repository = repo;
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
        internal string Root => repository.Directory;
        internal GitRevision Revision { get; private set; } = null!;
        internal PythonNativeGraph Native { get; set; } = null!;
        internal PythonRepositoryRequest Request { get; private set; } = null!;
        internal PythonGroupOperation[] Operations { get; private set; } = [];
        internal List<NativeCommand> Commands { get; } = [];
        internal NativeCommandResult Identity { get; set; } =
            Success("{\"name\":\"c\",\"version\":\"1.2.3\"}");
        internal bool GraphError { get; set; }
        internal bool PytestError { get; set; }
        internal string Target { get; set; } = "src/c/tests";
        internal Action? AfterGraph { get; set; }
        internal Action? DuringPytest { get; set; }
        internal bool CancelledPytestResult { get; set; }

        internal static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            var fixture = new Fixture(await GitFixture.CreateAsync(token));
            try
            {
                foreach (string directory in new[] { ".", "src/a", "src/b", "src/c" })
                    await fixture.repository.SetAsync(directory == "." ? "pyproject.toml" :
                        directory + "/pyproject.toml", "native fixture");
                await fixture.repository.SetAsync("src/a/source.py");
                await fixture.repository.SetAsync("src/c/tests/test_one.py");
                await fixture.repository.SetAsync("src/c/workflow-delivery.quality.yml", Quality);
                await fixture.repository.SetAsync("src/c/workflow-delivery.release-unit.yml", Unit);
                await fixture.RefreshAsync(token);
                PythonProjectDependencies[] projects = [new(".", "root", [], []),
                    new("src/a", "a", [], []), new("src/b", "b", ["src/a"], []),
                    new("src/c", "c", ["src/b"], [])];
                var metadata = new PythonMetadata(fixture.Root, "workspace", ">=3.14",
                    projects.Select(project => new PythonMetadataMember(project.Name,
                        project.Directory, project.Name)).ToArray(),
                    new Dictionary<string, PythonMetadataNode>());
                string interpreter = Path.Combine(fixture.Root, "interpreter");
                fixture.Native = new(metadata, interpreter, projects.Select(project =>
                    new PythonPassiveInputs(new(Path.GetFullPath(Path.Combine(fixture.Root,
                        project.Directory)), null), null)).ToArray(), [], projects,
                    projects.Select(project => Path.Combine(fixture.Root, project.Directory,
                        "pyproject.toml")).ToArray());
                fixture.Request = new(new(fixture.Root, "uv", interpreter, interpreter,
                    Path.Combine(fixture.Root, "passive.py"), "supplement", "cache",
                    "bootstrap", "activity"), "pytest_configuration.py", "packages.py",
                    new("ubuntu-latest", "python-3.14", new()
                    { ["python"] = "3.14", ["platform"] = "linux" }));
                return fixture;
            }
            catch { fixture.Dispose(); throw; }
        }

        internal async Task UpdateAsync(string path, string content, CancellationToken token)
        {
            await repository.SetAsync(path, content);
            await RefreshAsync(token);
        }

        internal async Task RemoveAsync(string path, CancellationToken token)
        {
            await repository.RemoveAsync(path);
            await RefreshAsync(token);
        }

        private async Task RefreshAsync(CancellationToken token)
        {
            string commit = await repository.CommitAsync(Revision is null ? [] :
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
                if (GraphError) throw new InvalidDataException("Controlled native graph failure.");
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
                return Task.FromResult(PytestError ? new(NativeTermination.Exited, 1, "", "", 0,
                    null) : Success(JsonSerializer.Serialize(new
                    {
                        directory = Root, root = Root,
                        configuration_file = Path.Combine(Root, "pyproject.toml"),
                        testpaths = new[] { Target }, addopts = ImportOptions,
                        pythonpath = Array.Empty<string>(), required_plugins = Array.Empty<string>()
                    })));
            }).ReadAsync(Request, new Dictionary<string, string?>(), token);
        }

        public void Dispose() => repository.Dispose();
    }
}
