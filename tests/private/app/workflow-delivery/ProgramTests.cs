using System.Runtime.InteropServices;
using System.Text.Json;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.CI;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests;

[TestClass]
public sealed class ProgramTests(TestContext context)
{
    [TestMethod]
    [DataRow("")]
    [DataRow("ci")]
    [DataRow("ci plan request.json")]
    [DataRow("ci comparison pull_request event.json candidate")]
    [DataRow("ci control-inputs request.json")]
    [DataRow("ci result plan.json results.json")]
    [DataRow("release publish")]
    public void UnsupportedArgumentsReportUsage(string arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(arguments.Split(' '), output, error);

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        Assert.StartsWith("Usage: WorkflowDelivery ci caller-", error.ToString());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("[]")]
    public void InvalidOrMissingScopeInputReportsNeutralizedErrorWithoutJson(string? scopeJson)
    {
        using var files = new TransferFiles();
        string scopePath = Path.Combine(files.Root, "scope.json");
        if (scopeJson is not null)
            File.WriteAllText(scopePath, scopeJson);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(
            ["ci", "native-preparation-plan", files.Root, scopePath,
                Path.Combine(files.Root, "out")], output, error);

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        AssertDiagnosticLines(error.ToString());
    }

    [TestMethod]
    public void CallerErrorCannotStartWorkflowCommands()
    {
        using var files = new TransferFiles();
        string unexpectedProperty = JsonSerializer.Serialize(
            CiCallerTransferTests.WorkflowCommandText);
        File.WriteAllText(Path.Combine(files.Root, "context.json"), $"{{{unexpectedProperty}:0}}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(
            ["ci", "caller-node", "plan", files.Root, Path.Combine(files.Root, "scope.json")],
            output, error);

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        Assert.Contains("add-mask]x", error.ToString());
        CiCallerTransferTests.AssertNoWorkflowCommand(error.ToString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FailedPythonPreparationRetainsPrivateDiagnosticsWithoutOverwriting(bool existing)
    {
        using var files = new TransferFiles();
        var command = new NativeCommand("/tools/uv", "/endpoint", ["sync"], 600,
            new Dictionary<string, string?> { ["HOME"] = "/owned/home" }, false);
        var native = new NativeCommandResult(NativeTermination.Exited, 7, "private stdout",
            CiCallerTransferTests.WorkflowCommandText, 1, null);
        var failure = new PythonGroupPreparationException(files.Root, [new(command, native)]);
        if (existing)
            File.WriteAllText(failure.DiagnosticsPath, "existing observation");
        using var error = new StringWriter();

        failure.WriteDiagnostics(error);

        string diagnostics = error.ToString();
        Assert.Contains("uv sync, Exited, exit 7.", diagnostics);
        Assert.Contains("add-mask]x", diagnostics);
        Assert.DoesNotContain(native.Stdout, diagnostics);
        Assert.DoesNotContain("/owned/home", diagnostics);
        CiCallerTransferTests.AssertNoWorkflowCommand(diagnostics);
        AssertDiagnosticLines(diagnostics);
        if (existing)
        {
            Assert.AreEqual("existing observation", File.ReadAllText(failure.DiagnosticsPath));
            Assert.Contains("could not be retained", diagnostics);
        }
        else
        {
            Assert.Contains(failure.DiagnosticsPath, diagnostics);
            var retained = JsonSerializer.Deserialize(File.ReadAllText(failure.DiagnosticsPath),
                TransferJson.Default.PythonPreparationCommandArray);
            Assert.IsNotNull(retained);
            PythonPreparationCommand observation = Assert.ContainsSingle(retained);
            Assert.AreEqual(native, observation.Result);
            Assert.AreEqual("/owned/home", observation.Command.Environment!["HOME"]);
        }
    }

    [TestMethod]
    public async Task PythonCallerPlanFailureReportsAndRetainsDiagnostics()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        using var files = new TransferFiles();
        await repo.SetAsync(".python-version", "3.14");
        // Force the real dotnet tool restore to fail before it can restore any tools.
        await repo.SetAsync(".config/dotnet-tools.json", "{");
        string candidate = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", candidate);
        var endpoint = new MaterializedEndpoint(repo.Directory, candidate);
        string scopePath = Path.Combine(files.Root, "scope.json");
        string controlSources = Directory.CreateDirectory(
            Path.Combine(files.Root, "control")).FullName;
        var caller = new PythonCiCallerContext(endpoint, endpoint, true, scopePath,
            typeof(Program).Assembly.Location, controlSources);
        File.WriteAllText(Path.Combine(files.Root, "context.json"),
            JsonSerializer.Serialize(caller, TransferJson.Default.PythonCiCallerContext));
        string failingDotnetExecutable = Path.GetFullPath(Path.Combine(
            RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
            OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(
            ["ci", "caller-python", "plan", files.Root, scopePath, Path.Combine(files.Root, "uv"),
                Path.Combine(files.Root, "python"), failingDotnetExecutable], output, error);

        Assert.AreEqual(2, exit);
        Assert.IsEmpty(output.ToString());
        string path = Path.Combine(files.Root, "query-preparation", "preparation-failure.json");
        Assert.Contains("tool, Exited, exit 1.", error.ToString());
        Assert.Contains($"Original diagnostics: {path}", error.ToString());
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void NativeMessageSchemasWritesContracts()
    {
        using var files = new TransferFiles();
        string directory = Path.Combine(files.Root, "schemas");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(["native-message-schemas", directory], output, error);

        Assert.AreEqual(0, exit);
        Assert.IsEmpty(error.ToString());
        Assert.IsTrue(File.Exists(Path.Combine(directory, "ruby-fact-request.schema.json")));
    }

    private static void AssertDiagnosticLines(string diagnostics)
    {
        Assert.IsNotEmpty(diagnostics);
        foreach (string line in diagnostics.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            Assert.StartsWith("  | ", line);
    }

    private sealed class TransferFiles : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory(
            "workflow-delivery-test-").FullName;

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
