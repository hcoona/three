using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonGroupPreparationTests(TestContext context)
{
    private static readonly string[] DotnetToolArguments = ["tool", "restore"];

    // Rows that vary only the request, owned scratch or controlled native answers read one
    // class-owned endpoint pair. A row that dirties a checkout owns that endpoint.
    private static GitFixture? sharedBasis;
    private static GitFixture? sharedCandidate;
    private static MaterializedEndpoint? sharedBasisEndpoint;
    private static MaterializedEndpoint? sharedCandidateEndpoint;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        try
        {
            sharedBasis = await GitFixture.CreateAsync(classContext.CancellationToken);
            sharedBasisEndpoint = await Fixture.EndpointAsync(sharedBasis, "basis");
            sharedCandidate = await GitFixture.CreateAsync(classContext.CancellationToken);
            sharedCandidateEndpoint = await Fixture.EndpointAsync(sharedCandidate, "candidate");
        }
        catch { Cleanup(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        sharedCandidate?.Dispose();
        sharedBasis?.Dispose();
    }

    [TestMethod]
    public async Task SharedEndpointsQualifyCompletePreparation()
    {
        // Defect-free control: the shared endpoint pair itself prepares completely.
        using var fixture = Fixture.Shared(sharedBasisEndpoint!, sharedCandidateEndpoint!);
        PythonGroupPreparationResult prepared = await fixture.PrepareAsync(
            context.CancellationToken);
        Assert.AreEqual(sharedBasisEndpoint, prepared.Request.Basis.Checkout);
        Assert.AreEqual(sharedCandidateEndpoint, prepared.Request.Candidate.Checkout);
        Assert.HasCount(4, prepared.Commands);
        Assert.IsTrue(prepared.Commands.All(item => item.Result.Succeeded));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreparedEndpointsRetainExactNativeToolAndSourceContexts(bool full)
    {
        using var fixture = Fixture.Shared(full ? sharedCandidateEndpoint! : sharedBasisEndpoint!,
            sharedCandidateEndpoint!, full);
        PythonGroupPreparationResult prepared = await fixture.PrepareAsync(
            context.CancellationToken);
        PythonGroupRequest result = prepared.Request;

        Assert.AreEqual(fixture.Request.Basis, result.Basis.Checkout);
        Assert.AreEqual(fixture.Request.Candidate, result.Candidate.Checkout);
        Assert.AreEqual(full, result.Full);
        Assert.AreEqual(fixture.Request.ScopePath, result.ScopePath);
        Assert.AreEqual(result.Basis.Collection.Graph.PassiveScript,
            result.Candidate.Collection.Graph.PassiveScript);
        Assert.AreNotEqual(result.Basis.Collection.Graph.HelperPython,
            result.Candidate.Collection.Graph.HelperPython);
        foreach (PythonGroupEndpoint endpoint in new[] { result.Basis, result.Candidate })
        {
            PythonGraphRequest graph = endpoint.Collection.Graph;
            Assert.AreEqual(endpoint.Checkout.Directory, graph.Root);
            Assert.AreEqual(fixture.Request.Tools.Uv, graph.Uv);
            Assert.AreEqual(fixture.Request.Tools.Interpreter, graph.Interpreter);
            Assert.AreSame(fixture.Request.Operation, endpoint.Collection.Operation);
            Assert.AreEqual(Path.Combine(fixture.Request.ControlSources, "passive.py"),
                graph.PassiveScript);
            Assert.AreEqual(Path.Combine(fixture.Request.ControlSources, "packages.py"),
                endpoint.Collection.PackageHelper);
            Assert.AreEqual(Path.Combine(fixture.Request.ControlSources, "pytest_configuration.py"),
                endpoint.Collection.PytestHelper);
            Assert.IsTrue(File.Exists(graph.HelperPython));
            Assert.IsFalse(Path.Exists(endpoint.Environment["UV_PROJECT_ENVIRONMENT"]));
            Assert.IsTrue(endpoint.Environment["UV_CACHE_DIR"]!.StartsWith(
                fixture.Request.Scratch, StringComparison.Ordinal));
        }
        NativeCommand[] installations = prepared.Commands.Select(item => item.Command)
            .Where(command => command.Executable == fixture.Request.Tools.Uv).ToArray();
        Assert.HasCount(2, installations);
        CollectionAssert.AreEqual(new[] { "sync", "--frozen", "--only-group", "dev",
            "--only-group", "workflow-delivery-python", "--no-editable", "--python",
            fixture.Request.Tools.Interpreter }, installations[0].Arguments);
        CollectionAssert.AreEqual(installations[0].Arguments, installations[1].Arguments);
        Assert.AreNotEqual(installations[0].Environment!["UV_PROJECT_ENVIRONMENT"],
            result.Basis.Environment["UV_PROJECT_ENVIRONMENT"]);
        Assert.AreNotEqual(installations[0].Environment!["HOME"],
            installations[1].Environment!["HOME"]);
    }

    [TestMethod]
    public async Task BackendQueriesRetainPreparedNbgvAndCredentialFreeState()
    {
        using var fixture = Fixture.Shared(sharedBasisEndpoint!, sharedCandidateEndpoint!);
        string sentinel = "WORKFLOW_PREPARATION_TEST_SECRET_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(sentinel, "controlled-secret-value");
        try
        {
            PythonGroupPreparationResult result = await fixture.PrepareAsync(
                context.CancellationToken);
            foreach (PythonGroupEndpoint endpoint in new[] { result.Request.Basis,
                         result.Request.Candidate })
            {
                PythonPreparationCommand[] commands = result.Commands.Where(item =>
                    item.Command.Directory == endpoint.Checkout.Directory).ToArray();
                CollectionAssert.AreEqual(DotnetToolArguments, commands[0].Command.Arguments);
                Assert.AreEqual(fixture.Request.Tools.Dotnet, commands[0].Command.Executable);
                Assert.AreEqual(fixture.Request.Tools.Uv, commands[1].Command.Executable);
                foreach (IReadOnlyDictionary<string, string?> environment in new[] {
                    commands[0].Command.Environment!, commands[1].Command.Environment!,
                    endpoint.Environment })
                {
                    Assert.AreEqual(endpoint.Environment["HOME"], environment["HOME"]);
                    Assert.AreEqual(environment["HOME"], environment["DOTNET_CLI_HOME"]);
                    Assert.IsTrue(Directory.Exists(environment["HOME"]));
                    Assert.IsTrue(Directory.Exists(environment["TMPDIR"]));
                    Assert.IsFalse(environment.ContainsKey(sentinel));
                    foreach (string key in new[] { "GH_TOKEN", "UV_NO_BUILD_ISOLATION",
                        "UV_NO_SOURCES", "UV_CONFIG_FILE", "PYTHONPATH", "VIRTUAL_ENV" })
                        Assert.IsFalse(environment.ContainsKey(key));
                    Assert.Contains(Path.GetDirectoryName(fixture.Request.Tools.Dotnet)!,
                        environment["PATH"]!.Split(Path.PathSeparator));
                    Assert.AreEqual("never", environment["UV_PYTHON_DOWNLOADS"]);
                    Assert.AreEqual("1", environment["UV_NO_SYSTEM_CONFIG"]);
                }
                Assert.IsTrue(commands.All(item => !item.Command.InheritEnvironment));
            }
        }
        finally { Environment.SetEnvironmentVariable(sentinel, null); }
    }

    [TestMethod]
    [DataRow("restore")]
    [DataRow("sync")]
    [DataRow("candidate-restore")]
    [DataRow("candidate-sync")]
    [DataRow("missing-query")]
    [DataRow("candidate-missing-query")]
    public async Task FailedPreparationCannotReturnACompleteRequest(string phase)
    {
        using var fixture = Fixture.Shared(sharedBasisEndpoint!, sharedCandidateEndpoint!);
        fixture.Failure = phase;
        PythonGroupPreparationException failure = await Assert.ThrowsExactlyAsync<
            PythonGroupPreparationException>(() => fixture.PrepareAsync(
                context.CancellationToken));
        Assert.HasCount(fixture.Commands.Count, failure.Commands);
        CollectionAssert.AreEqual(fixture.Commands.ToArray(),
            failure.Commands.Select(item => item.Command).ToArray());
        Assert.AreEqual(Path.Combine(fixture.Request.Scratch, "preparation-failure.json"),
            failure.DiagnosticsPath);
        if (phase.Contains("missing-", StringComparison.Ordinal))
        {
            Assert.IsTrue(failure.Commands.All(item => item.Result.Succeeded));
            Assert.IsTrue(failure.Commands.All(item =>
                item.Result.Stdout == "Controlled preparation"));
            Assert.Contains("interpreter", failure.Message);
            Assert.DoesNotContain("Controlled preparation", failure.Message);
        }
        else
        {
            Assert.AreEqual(Fixture.FailureStderr, failure.Commands[^1].Result.Stderr);
            Assert.AreEqual(NativeTermination.Exited, failure.Commands[^1].Result.Termination);
            Assert.AreEqual(1, failure.Commands[^1].Result.ExitCode);
            // The stage and a bounded stderr tail reach the message; the head stays retained.
            Assert.StartsWith("Python query preparation failed: " +
                $"{Path.GetFileName(fixture.Commands[^1].Executable)} " +
                $"{fixture.Commands[^1].Arguments[0]}, Exited, exit 1. stderr: [truncated] ",
                failure.Message);
            Assert.EndsWith("Controlled failure tail", failure.Message);
            Assert.DoesNotContain("Controlled head", failure.Message);
            Assert.IsLessThan(NativeDiagnostic.TailLength + 200, failure.Message.Length);
        }
        Assert.IsNotEmpty(fixture.Commands);
        if (phase.StartsWith("candidate-", StringComparison.Ordinal))
            Assert.AreEqual(fixture.Request.Candidate.Directory, fixture.Commands[^1].Directory);
        else Assert.IsFalse(fixture.Commands.Any(command =>
            command.Directory == fixture.Request.Candidate.Directory));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelledPreparationCannotContinueOrReturnRequest(bool inFlight)
    {
        using var fixture = Fixture.Shared(sharedBasisEndpoint!, sharedCandidateEndpoint!);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        if (inFlight) fixture.Cancel = cancellation;
        else await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.PrepareAsync(cancellation.Token));
        if (inFlight)
            Assert.AreEqual(fixture.Request.Tools.Uv, fixture.Commands[^1].Executable);
        else Assert.IsEmpty(fixture.Commands);
        Assert.IsFalse(fixture.Commands.Any(command =>
            command.Directory == fixture.Request.Candidate.Directory));
    }

    [TestMethod]
    public async Task ReusedScratchCannotBePreparedOrOverwritten()
    {
        using var fixture = Fixture.Shared(sharedBasisEndpoint!, sharedCandidateEndpoint!);
        string state = Path.Combine(fixture.Request.Scratch, "old-state");
        await File.WriteAllTextAsync(state, "old", context.CancellationToken);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PrepareAsync(context.CancellationToken));

        Assert.IsEmpty(fixture.Commands);
        Assert.AreEqual("old", await File.ReadAllTextAsync(state, context.CancellationToken));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FreshScratchCannotOverlapOwnedInputs(bool controlSources)
    {
        using var fixture = Fixture.Shared(sharedBasisEndpoint!, sharedCandidateEndpoint!);
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        MaterializedEndpoint basis = await Fixture.EndpointAsync(repo, "row-owned basis");
        string scratch = Path.Combine(
            controlSources ? fixture.Request.ControlSources : basis.Directory, "scratch");
        Directory.CreateDirectory(scratch);
        fixture.Request = fixture.Request with { Basis = basis, Scratch = scratch };
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(scratch));

        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PrepareAsync(context.CancellationToken));

        Assert.Contains("fresh external state", failure.Message);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(scratch));
    }

    [TestMethod]
    public async Task CandidateMustMaterializeTheRequestedRevision()
    {
        using var fixture = Fixture.Shared(sharedBasisEndpoint!, sharedCandidateEndpoint!);
        fixture.Request = fixture.Request with
        {
            Candidate = fixture.Request.Candidate with
            { Reference = fixture.Request.Basis.Reference },
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PrepareAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Request.Scratch));
    }

    [TestMethod]
    [DataRow("basis")]
    [DataRow("candidate")]
    public async Task DirtyEndpointStopsBeforeNativePreparation(string side)
    {
        using var fixture = Fixture.Shared(sharedBasisEndpoint!, sharedCandidateEndpoint!);
        MaterializedEndpoint dirty = await fixture.DirtyAsync(side, context.CancellationToken);
        fixture.Request = side == "basis" ? fixture.Request with { Basis = dirty }
            : fixture.Request with { Candidate = dirty };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PrepareAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Commands);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Request.Scratch));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(),
            "python query preparation " + Guid.NewGuid().ToString("N"));
        internal PythonGroupPreparationRequest Request { get; set; }
        internal List<NativeCommand> Commands { get; } = [];
        internal string? Failure { get; set; }
        internal CancellationTokenSource? Cancel { get; set; }

        private GitFixture? dirtyRepository;

        private Fixture(bool full)
        {
            string basis = Path.Combine(directory, "basis"), candidate = full ? basis :
                Path.Combine(directory, "candidate"), sources = Path.Combine(directory, "control");
            string scratch = Path.Combine(directory, "scratch");
            foreach (string path in new[] { basis, candidate, sources, scratch })
                Directory.CreateDirectory(path);
            foreach (string source in new[] { "passive.py", "pytest_configuration.py",
                "packages.py" })
            {
                string path = Path.Combine(sources, source);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "Controlled source; never executed.");
            }
            string Tool(string name) => Path.Combine(directory, "tools", name);
            Request = new(new(basis, new string('a', 40)),
                new(candidate, new string(full ? 'a' : 'b', 40)),
                full, Path.Combine(directory, "scope.json"), sources, scratch,
                new(Tool("uv"), Tool("python"), Tool("dotnet")),
                new("ubuntu-latest", "python-3.14", new()
                { ["python"] = "3.14", ["platform"] = "linux" }), 600);
        }

        internal async Task<MaterializedEndpoint> DirtyAsync(string side,
            CancellationToken token)
        {
            dirtyRepository = await GitFixture.CreateAsync(token);
            MaterializedEndpoint endpoint = await EndpointAsync(dirtyRepository, side);
            await File.WriteAllTextAsync(Path.Combine(endpoint.Directory, "input.txt"),
                "modified", token);
            return endpoint;
        }

        // Reads class-owned endpoints; the row owns only its control sources and scratch.
        internal static Fixture Shared(MaterializedEndpoint basis, MaterializedEndpoint candidate,
            bool full = false)
        {
            var fixture = new Fixture(full);
            fixture.Request = fixture.Request with { Basis = basis, Candidate = candidate };
            return fixture;
        }

        internal static async Task<MaterializedEndpoint> EndpointAsync(GitFixture repository,
            string content)
        {
            await repository.SetAsync("input.txt", content);
            string revision = await repository.CommitAsync();
            await repository.GitAsync("checkout", "--force", revision);
            return new(repository.Directory, revision);
        }

        internal Task<PythonGroupPreparationResult> PrepareAsync(CancellationToken token) =>
            PythonGroupPreparation.PrepareAsync(Request, ExecuteAsync, token);

        internal static readonly string FailureStderr = "Controlled head " +
            new string('x', NativeDiagnostic.TailLength) + " Controlled failure tail";

        private async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commands.Add(command);
            string phase = command.Executable == Request.Tools.Dotnet ? "restore" : "sync";
            if (Failure == phase || Failure == "candidate-" + phase &&
                command.Directory == Request.Candidate.Directory)
                return new(NativeTermination.Exited, 1, "", FailureStderr, 0, null);
            if (phase == "sync" && Failure != "missing-query" &&
                !(Failure == "candidate-missing-query" &&
                    command.Directory == Request.Candidate.Directory))
                Create(Path.Combine(command.Environment!["UV_PROJECT_ENVIRONMENT"]!,
                    OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python"));
            if (phase == "sync" && Cancel is not null) await Cancel.CancelAsync();
            return new(NativeTermination.Exited, 0, "Controlled preparation", "", 0, null);

            static void Create(string path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "Controlled output; never executed.");
            }
        }

        public void Dispose()
        {
            dirtyRepository?.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
