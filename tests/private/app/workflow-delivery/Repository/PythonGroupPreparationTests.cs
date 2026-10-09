using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonGroupPreparationTests(TestContext context)
{
    private static readonly string[] CargoArguments = ["build", "--locked"];
    private static readonly string[] DotnetToolArguments = ["tool", "restore"];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreparedEndpointsRetainExactNativeToolAndSourceContexts(bool full)
    {
        using var fixture = new Fixture(full);
        PythonGroupPreparationResult prepared = await fixture.PrepareAsync(
            context.CancellationToken);
        PythonGroupRequest result = prepared.Request;

        Assert.AreEqual(fixture.Request.Basis, result.Basis.Checkout);
        Assert.AreEqual(fixture.Request.Candidate, result.Candidate.Checkout);
        Assert.AreEqual(full, result.Full);
        Assert.AreEqual(fixture.Request.ScopePath, result.ScopePath);
        Assert.AreEqual(result.Basis.Collection.Graph.SupplementExecutable,
            result.Candidate.Collection.Graph.SupplementExecutable);
        Assert.AreNotEqual(result.Basis.Collection.Graph.HelperPython,
            result.Candidate.Collection.Graph.HelperPython);
        NativeCommand compilation = Assert.ContainsSingle(prepared.Commands.Where(item =>
            item.Command.Executable == fixture.Request.Tools.Cargo)).Command;
        CollectionAssert.AreEqual(CargoArguments, compilation.Arguments);
        Assert.AreEqual(Path.Combine(fixture.Request.ControlSources, "uv"), compilation.Directory);
        Assert.AreEqual(Path.Combine(fixture.Request.Scratch, "native/output"),
            compilation.Environment!["CARGO_TARGET_DIR"]);
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
            Assert.IsTrue(File.Exists(graph.SupplementExecutable));
            Assert.AreNotEqual(graph.BootstrapScratch, graph.ActivityScratch);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(graph.BootstrapScratch));
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(graph.ActivityScratch));
            Assert.IsFalse(Path.Exists(endpoint.Environment["UV_PROJECT_ENVIRONMENT"]));
            Assert.AreEqual(graph.Cache, endpoint.Environment["UV_CACHE_DIR"]);
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
        using var fixture = new Fixture(false);
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
                    Assert.IsTrue(environment.ContainsKey(sentinel));
                    Assert.IsNull(environment[sentinel]);
                    foreach (string key in new[] { "GH_TOKEN", "UV_NO_BUILD_ISOLATION",
                        "UV_NO_SOURCES", "UV_CONFIG_FILE", "PYTHONPATH", "VIRTUAL_ENV" })
                        Assert.IsNull(environment.GetValueOrDefault(key));
                    Assert.Contains(Path.GetDirectoryName(fixture.Request.Tools.Dotnet)!,
                        environment["PATH"]!.Split(Path.PathSeparator));
                    Assert.AreEqual("never", environment["UV_PYTHON_DOWNLOADS"]);
                }
            }
        }
        finally { Environment.SetEnvironmentVariable(sentinel, null); }
    }

    [TestMethod]
    [DataRow("rustup")]
    [DataRow("rust")]
    [DataRow("restore")]
    [DataRow("sync")]
    [DataRow("candidate-restore")]
    [DataRow("candidate-sync")]
    [DataRow("missing-supplement")]
    [DataRow("missing-query")]
    public async Task FailedPreparationCannotReturnACompleteRequest(string phase)
    {
        using var fixture = new Fixture(false) { Failure = phase };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PrepareAsync(context.CancellationToken));
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
        using var fixture = new Fixture(false);
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
    [DataRow("reused")]
    [DataRow("source-overlap")]
    [DataRow("missing-source")]
    [DataRow("relative-tool")]
    [DataRow("full-mismatch")]
    [DataRow("deadline")]
    public async Task ReusedOrInvalidOwnedScratchStopsBeforePreparation(string defect)
    {
        using var fixture = new Fixture(false);
        if (defect == "reused")
            File.WriteAllText(Path.Combine(fixture.Request.Scratch, "old-state"), "old");
        else if (defect == "source-overlap") fixture.Request = fixture.Request with
            { Scratch = fixture.Request.Basis.Directory };
        else if (defect == "missing-source")
            File.Delete(Path.Combine(fixture.Request.ControlSources, "uv/Cargo.lock"));
        else if (defect == "relative-tool") fixture.Request = fixture.Request with
            { Tools = fixture.Request.Tools with { Uv = "uv" } };
        else if (defect == "deadline")
            fixture.Request = fixture.Request with { DeadlineSeconds = 0 };
        else fixture.Request = fixture.Request with { Full = true };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.PrepareAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Commands);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(),
            "python query preparation " + Guid.NewGuid().ToString("N"));
        internal PythonGroupPreparationRequest Request { get; set; }
        internal List<NativeCommand> Commands { get; } = [];
        internal string? Failure { get; init; }
        internal CancellationTokenSource? Cancel { get; set; }

        internal Fixture(bool full)
        {
            string basis = Path.Combine(directory, "basis"), candidate = full ? basis :
                Path.Combine(directory, "candidate"), sources = Path.Combine(directory, "control");
            string scratch = Path.Combine(directory, "scratch");
            foreach (string path in new[] { basis, candidate, sources, scratch })
                Directory.CreateDirectory(path);
            foreach (string source in new[] { "passive.py", "pytest_configuration.py",
                "packages.py",
                "uv/Cargo.toml", "uv/Cargo.lock", "uv/rust-toolchain.toml" })
            {
                string path = Path.Combine(sources, source);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "Controlled source; never executed.");
            }
            string Tool(string name) => Path.Combine(directory, "tools", name);
            Request = new(new(basis, new string('a', 40)),
                new(candidate, new string(full ? 'a' : 'b', 40)),
                full, Path.Combine(directory, "scope.json"), sources, scratch,
                new(Tool("uv"), Tool("python"), Tool("dotnet"), Tool("rustup"), Tool("cargo")),
                new("ubuntu-latest", "python-3.14", new()
                    { ["python"] = "3.14", ["platform"] = "linux" }), 600);
        }

        internal Task<PythonGroupPreparationResult> PrepareAsync(CancellationToken token) =>
            PythonGroupPreparation.PrepareAsync(Request, ExecuteAsync, token);

        private async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commands.Add(command);
            string phase = command.Executable == Request.Tools.Rustup ? "rustup" :
                command.Executable == Request.Tools.Cargo ? "rust" :
                command.Executable == Request.Tools.Dotnet ? "restore" : "sync";
            if (Failure == phase || Failure == "candidate-" + phase &&
                command.Directory == Request.Candidate.Directory)
                return new(NativeTermination.Exited, 1, "", "Controlled failure", 0, null);
            if (phase == "rust" && Failure != "missing-supplement")
                Create(Path.Combine(command.Environment!["CARGO_TARGET_DIR"]!, "debug",
                    "workflow-python-native-facts" + (OperatingSystem.IsWindows() ? ".exe" : "")));
            if (phase == "sync" && Failure != "missing-query")
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

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
