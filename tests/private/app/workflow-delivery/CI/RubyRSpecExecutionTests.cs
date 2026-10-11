using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class RubyRSpecExecutionTests
{
    public TestContext TestContext { get; set; } = null!;
    private static readonly CheckKey Check = new("src/product", "ruby/project-rspec-v1", "default");

    private static string Fixture(string scenario)
    {
        var assembly = typeof(RubyRSpecExecutionTests).Assembly;
        string name = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith($"ruby-rspec-{scenario}.json", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static NativeCommandResult Command(string termination = "Exited", int code = 0) =>
        new(Enum.Parse<NativeTermination>(termination), code, "native stdout", "native stderr",
            0.5, null);

    private static RubyRSpecRequest Request(string root)
    {
        string checkout = Path.Combine(root, "checkout");
        string scratch = Path.Combine(root, "scratch");
        Directory.CreateDirectory(Path.Combine(checkout, "src/product"));
        Directory.CreateDirectory(scratch);
        return new(checkout, Path.Combine(root, "ruby"), Path.Combine(root, "bundle"),
            "2.4.20", Path.Combine(checkout, "src/product/Gemfile"), scratch, 120,
            new Dictionary<string, string?> { ["PROJECT_INPUT"] = "preserved" });
    }

    [TestMethod]
    [DataRow("passed", "Passed")]
    [DataRow("failed", "Failed")]
    [DataRow("pending", "Passed")]
    [DataRow("outside", "Failed")]
    public async Task WholeProjectInvocationKeepsOriginalExamplesAndNativeDefaults(
        string scenario, string expected)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            RubyRSpecRequest request = Request(root);
            string original = Fixture(scenario);
            RubyRSpecRun run = await RubyRSpecExecution.RunAsync(Check, request,
                async (command, token) =>
                {
                    Assert.AreEqual(request.Ruby, command.Executable);
                    Assert.AreEqual(Path.GetFullPath(Path.Combine(request.Checkout, "src/product")),
                        command.Directory);
                    string[] expectedArguments = [request.Bundle, "_2.4.20_", "exec", "rspec",
                        "--format", "json", "--out", command.Arguments[^1]];
                    CollectionAssert.AreEqual(expectedArguments, command.Arguments);
                    Assert.AreEqual(request.Gemfile, command.Environment!["BUNDLE_GEMFILE"]);
                    Assert.AreEqual("1", command.Environment["BUNDLE_FROZEN"]);
                    Assert.AreEqual("preserved", command.Environment["PROJECT_INPUT"]);
                    Assert.IsFalse(command.InheritEnvironment);
                    Assert.AreEqual(request.DeadlineSeconds, command.DeadlineSeconds);
                    await File.WriteAllTextAsync(command.Arguments[^1], original, token);
                    return Command();
                }, TestContext.CancellationToken);
            Assert.AreEqual(Enum.Parse<CheckStatus>(expected), run.Status);
            Assert.IsNotNull(run.Report);
            Assert.AreEqual(original.Trim(), run.Report.Original.GetRawText());
            Assert.IsNull(run.ReportError);
            Assert.AreEqual("native stderr", run.Result.Stderr);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Scratch));
            Assert.HasCount(1, request.Environment);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow("Exited", 1, "Failed")]
    [DataRow("TimedOut", 0, "TimedOut")]
    [DataRow("Cancelled", 0, "Cancelled")]
    [DataRow("InvalidOutput", 0, "Failed")]
    [DataRow("StartFailed", 0, "Failed")]
    public async Task CompletePassingReportCannotOverrideNativeCommandFailure(
        string termination, int code, string expected)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            RubyRSpecRequest request = Request(root);
            RubyRSpecRun run = await RubyRSpecExecution.RunAsync(Check, request,
                async (command, token) =>
                {
                    await File.WriteAllTextAsync(command.Arguments[^1], Fixture("passed"), token);
                    return Command(termination, code);
                }, TestContext.CancellationToken);
            Assert.AreEqual(Enum.Parse<CheckStatus>(expected), run.Status);
            Assert.IsNotNull(run.Report);
            Assert.AreEqual(Enum.Parse<NativeTermination>(termination), run.Result.Termination);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Scratch));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task CancellationAfterNativeCompletionCannotPassAndCleansOwnedOutput()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.CancellationToken);
        try
        {
            RubyRSpecRequest request = Request(root);
            RubyRSpecRun run = await RubyRSpecExecution.RunAsync(Check, request,
                async (command, token) =>
                {
                    await File.WriteAllTextAsync(command.Arguments[^1], Fixture("passed"), token);
                    await cancelled.CancelAsync();
                    return Command();
                }, cancelled.Token);
            Assert.AreEqual(CheckStatus.Cancelled, run.Status);
            Assert.IsTrue(run.Result.Succeeded);
            Assert.IsNull(run.Report);
            Assert.IsNotNull(run.ReportError);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Scratch));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("{\"examples\":")]
    public async Task ZeroExitWithoutACompleteNativeResultFails(string? output)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            RubyRSpecRequest request = Request(root);
            RubyRSpecRun run = await RubyRSpecExecution.RunAsync(Check, request,
                async (command, token) =>
                {
                    if (output is not null)
                        await File.WriteAllTextAsync(command.Arguments[^1], output, token);
                    return Command();
                }, TestContext.CancellationToken);
            Assert.AreEqual(CheckStatus.Failed, run.Status);
            Assert.IsNull(run.Report);
            Assert.IsNotNull(run.ReportError);
            Assert.IsEmpty(Directory.EnumerateFileSystemEntries(request.Scratch));
        }
        finally { Directory.Delete(root, true); }
    }
}
