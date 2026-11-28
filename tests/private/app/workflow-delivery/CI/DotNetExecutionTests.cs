using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetExecutionTests(TestContext context)
{
    // Rows that vary only the plan, request or native answers read one committed
    // repository; each row still owns fresh scratch and native output.
    private static readonly string[] WrapperPrefix = ["msbuild", "--disable-build-servers"];
    private static GitFixture? shared;
    private static string sharedCommit = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedCommit = await DotNetExecutionFixture.CommitAsync(repo);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private static DotNetExecutionFixture Shared() =>
        DotNetExecutionFixture.Shared(shared!, sharedCommit);

    [TestMethod]
    public async Task SharedRepositoryControlQualifies()
    {
        using var fixture = Shared();
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied,
            string.Join("; ", result.Failures.Select(failure => failure.Error)));
        Assert.HasCount(2, result.Outputs);
        // Neither wrapper may leave reusable MSBuild nodes or compiler servers behind. The
        // nested BuildManager reads the compiler-server choices from the environment.
        foreach (NativeCommand command in fixture.Commands)
        {
            CollectionAssert.AreEqual(WrapperPrefix, command.Arguments[..2]);
            Assert.AreEqual("false", command.Environment!["UseSharedCompilation"]);
            Assert.AreEqual("false", command.Environment["UseRazorBuildServer"]);
        }
        Assert.AreEqual("true", fixture.Commands[1].Environment!["VsTestUseMSBuildOutput"]);
    }

    [TestMethod]
    [DataRow("MTP", "InvokeTestingPlatform")]
    [DataRow("VSTest", "VSTest")]
    public async Task NativeStagesPreserveOriginalGlobalsAndNativeOutput(string capability,
        string target)
    {
        // Private repository: this row checks native output written inside the checkout.
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken,
            capability);
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.Contains("-target:BuildSelected", fixture.Commands[0].Arguments);
        Assert.IsNotNull(result.BatchBuild);
        DotNetBuildRequest request = JsonSerializer.Deserialize(await File.ReadAllTextAsync(
            Path.Combine(fixture.Scratch, "build-request.json"), context.CancellationToken),
            DotNetNativeJson.Default.DotNetBuildRequest)!;
        Assert.AreEqual("true", Assert.ContainsSingle(request.Entries).Globals["IsGraphBuild"]);
        Assert.Contains("-target:TestSelected", fixture.Commands[1].Arguments);
        DotNetTestRequest tests = JsonSerializer.Deserialize(await File.ReadAllTextAsync(
            Path.Combine(fixture.Scratch, "test-request.json"), context.CancellationToken),
            DotNetNativeJson.Default.DotNetTestRequest)!;
        DotNetTestEntry test = Assert.ContainsSingle(tests.Entries);
        Assert.AreEqual(target, test.TestTarget);
        CollectionAssert.AreEquivalent(request.Entries[0].Globals, test.Identity.Globals);
        Assert.AreEqual(request.Entries[0].Project, test.Identity.Project);
        Assert.IsTrue(fixture.Commands.All(command => command.Executable == "dotnet" &&
            command.Directory == fixture.Repo.Directory &&
            command.Arguments.Contains("-noAutoResponse") &&
            !command.Arguments.Contains("-restore") && !command.Arguments.Contains("-graphBuild")));
        Assert.IsNotNull(result.TestBatch);
        Assert.AreEqual("ordinary product log 0", Assert.ContainsSingle(result.Tests).Log);
        Assert.HasCount(2, result.Outputs);
        Assert.IsTrue(result.Outputs.All(output => output.Path == fixture.Output));
        Assert.IsEmpty(result.Failures);
        DotNetRunResult transfer = JsonSerializer.Deserialize(JsonSerializer.Serialize(result,
            TransferJson.Default.DotNetRunResult), TransferJson.Default.DotNetRunResult)!;
        CollectionAssert.AreEqual(result.Results, transfer.Results);
    }

    [TestMethod]
    public async Task FailedTestCannotEraseEarlierBuild()
    {
        using var fixture = Shared();
        fixture.Override = command => command.Arguments.Contains("-target:TestSelected") ?
            new(NativeTermination.Exited, 1, "test failed after its Build prelude", "", 0.1,
                "failure") : null;
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Build).Status);
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Test).Status);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.AreEqual(NativeTermination.Exited, result.TestBatch!.Result.Termination);
        Assert.IsNotNull(result.BatchBuild);
        Assert.AreEqual(DotNetChecks.Build, Assert.ContainsSingle(result.Outputs).Key.Check);
    }

    [TestMethod]
    [DataRow((int)NativeTermination.Cancelled, (int)CheckStatus.Cancelled)]
    [DataRow((int)NativeTermination.TimedOut, (int)CheckStatus.TimedOut)]
    public async Task InterruptedBatchCannotPassFromSuccessfulBatchResponse(
        int terminationValue, int statusValue)
    {
        using var fixture = Shared();
        var termination = (NativeTermination)terminationValue;
        var status = (CheckStatus)statusValue;
        DotNetRunResult result = await DotNetExecution.RunAsync(fixture.Plan, fixture.Request,
            async (command, token) =>
            {
                // The controlled batch writes a successful response before the interruption.
                NativeCommandResult native = await fixture.ExecuteAsync(command, token);
                return command.Arguments.Contains("-target:BuildSelected") ?
                    native with { Termination = termination, ExitCode = null } : native;
            }, context.CancellationToken);

        Assert.IsTrue(File.Exists(Path.Combine(fixture.Scratch, "build-response.json")));
        Assert.AreEqual(status, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results[1].Status);
        Assert.IsEmpty(result.Outputs);
        Assert.IsNull(result.TestBatch);
        Assert.IsNotNull(result.BatchBuild);
        Assert.AreEqual(termination, result.BatchBuild.Result.Termination);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(null)]
    public async Task FailedOuterCommandCannotPassFromSuccessfulBatchResponse(int? exitCode)
    {
        using var fixture = Shared();
        DotNetRunResult result = await DotNetExecution.RunAsync(fixture.Plan, fixture.Request,
            async (command, token) =>
            {
                NativeCommandResult native = await fixture.ExecuteAsync(command, token);
                return native with
                { ExitCode = exitCode, Error = "Wrapper failed after response." };
            }, context.CancellationToken);

        Assert.IsTrue(File.Exists(Path.Combine(fixture.Scratch, "build-response.json")));
        Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results[1].Status);
        Assert.IsEmpty(result.Outputs);
        Assert.AreEqual("Wrapper failed after response.",
            Assert.ContainsSingle(result.Failures).Error);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.IsNotNull(result.BatchBuild);
        Assert.AreEqual(exitCode, result.BatchBuild.Result.ExitCode);
    }

    [TestMethod]
    public async Task SuccessfulTestWrapperWithoutReceiptDoesNotQualifyTest()
    {
        using var fixture = Shared();
        fixture.Override = command =>
        {
            if (!command.Arguments.Contains("-target:TestSelected")) return null;
            return new(NativeTermination.Exited, 0, "The diagnostic stream claims success.",
                "", 0.1, null);
        };
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Passed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Failed, result.Results[1].Status);
        Assert.AreEqual(DotNetChecks.Build, Assert.ContainsSingle(result.Outputs).Key.Check);
        Assert.AreEqual(DotNetChecks.Test, Assert.ContainsSingle(result.Failures).Key.Check);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidTestReceiptCannotQualifyWhileIndependentReceiptPasses(
        bool malformed)
    {
        using var fixture = Shared();
        CiPlan plan = WithNet8Variant(fixture, "MTP");
        if (!malformed)
            fixture.ModifyTestReceipt = (index, receipt) =>
            {
                if (index == 0) receipt["identity"]!["globals"]!["TargetFramework"] = "net8.0";
            };

        DotNetRunResult result = await DotNetExecution.RunAsync(plan, fixture.Request,
            async (command, token) =>
            {
                NativeCommandResult native = await fixture.ExecuteAsync(command, token);
                if (malformed && command.Arguments.Contains("-target:TestSelected"))
                {
                    string receipts = Path.Combine(fixture.Scratch, "test-receipts");
                    await File.WriteAllTextAsync(DotNetTestTask.Receipt(receipts, 0), "{",
                        token);
                }
                return native;
            }, context.CancellationToken);

        CheckKey rejected = plan.Checks[1].Work.Key;
        CheckKey independent = plan.Checks[3].Work.Key;
        Assert.AreEqual(CheckStatus.Failed,
            result.Results.Single(item => item.Key == rejected).Status);
        Assert.AreEqual(CheckStatus.Passed,
            result.Results.Single(item => item.Key == independent).Status);
        var failure = Assert.ContainsSingle(result.Failures);
        Assert.AreEqual(rejected, failure.Key);
        if (!malformed)
            Assert.AreEqual("Different selected native test identity.", failure.Error);
        Assert.IsTrue(result.Outputs.Any(item => item.Key == independent));
        Assert.IsFalse(result.Outputs.Any(item => item.Key == rejected));
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task FailedTestReceiptCannotBeMaskedByAnotherPassingVariant()
    {
        using var fixture = DotNetExecutionFixture.Shared(shared!, sharedCommit);
        CiPlan plan = WithNet8Variant(fixture, "MTP");
        fixture.ModifyTestReceipt = (index, receipt) =>
        {
            if (index != 0) return;
            receipt["test"] = "Failure";
            receipt["log"] = "entry zero diagnostic";
        };
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken, plan);
        CheckKey first = plan.Checks[1].Work.Key;
        CheckKey other = plan.Checks[3].Work.Key;
        Assert.AreEqual(CheckStatus.Failed,
            result.Results.Single(item => item.Key == first).Status);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item => item.Key == other)
            .Status);
        Assert.AreEqual(first, Assert.ContainsSingle(result.Failures).Key);
        Assert.Contains("entry zero diagnostic", result.Failures[0].Error);
        Assert.IsTrue(result.Outputs.Any(output => output.Key == other));
        Assert.IsFalse(result.Outputs.Any(output => output.Key == first));
        Assert.HasCount(2, result.TestBatch!.Keys);
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task CallerCancellationRetainsCompletedTestReceipts()
    {
        using var fixture = DotNetExecutionFixture.Shared(shared!, sharedCommit);
        CiPlan plan = WithNet8Variant(fixture, "MTP");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        DotNetRunResult result = await DotNetExecution.RunAsync(plan, fixture.Request,
            async (command, token) =>
            {
                NativeCommandResult native = await fixture.ExecuteAsync(command, token);
                if (!command.Arguments.Contains("-target:TestSelected")) return native;
                // The first submission completed; the caller then cancels the session.
                File.Delete(DotNetTestTask.Receipt(Path.Combine(fixture.Scratch,
                    "test-receipts"), 1));
                await cancellation.CancelAsync();
                return new(NativeTermination.Cancelled, null, "", "", 0.1,
                    "Command cancelled.");
            }, cancellation.Token);
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.AreEqual(CheckStatus.Passed, result.Results[1].Status);
        Assert.AreEqual(CheckStatus.Cancelled, result.Results[3].Status);
        Assert.IsTrue(result.Outputs.Any(output => output.Key == plan.Checks[1].Work.Key));
        Assert.AreEqual("ordinary product log 0", Assert.ContainsSingle(result.Tests).Log);
        Assert.AreEqual(plan.Checks[3].Work.Key, Assert.ContainsSingle(result.Failures).Key);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(null)]
    public async Task FailedTestWrapperCannotPassFromReceipts(int? exitCode)
    {
        using var fixture = Shared();
        DotNetRunResult result = await DotNetExecution.RunAsync(fixture.Plan, fixture.Request,
            async (command, token) =>
            {
                NativeCommandResult native = await fixture.ExecuteAsync(command, token);
                return command.Arguments.Contains("-target:TestSelected") ? native with
                { ExitCode = exitCode, Error = "Wrapper failed after receipts." } : native;
            }, context.CancellationToken);
        Assert.AreEqual(CheckStatus.Passed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Failed, result.Results[1].Status);
        Assert.AreEqual("Wrapper failed after receipts.",
            Assert.ContainsSingle(result.Failures).Error);
        Assert.IsEmpty(result.Tests);
        Assert.AreEqual(DotNetChecks.Build, Assert.ContainsSingle(result.Outputs).Key.Check);
    }

    [TestMethod]
    public async Task SuccessfulBuildWithoutNativeOutputSkipsTest()
    {
        using var fixture = Shared();
        fixture.ModifyResponse = response =>
            response["TargetResults"]!["GetTargetPath"]!["Items"] = new JsonArray();
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results[1].Status);
        Assert.IsEmpty(result.Outputs);
        Assert.IsNotEmpty(result.Failures);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    public async Task BuildOutputFromDifferentNativeVariantCannotQualifyCandidate()
    {
        using var fixture = Shared();
        fixture.ModifyBatchResponse = response =>
            response["entries"]![0]!["identity"]!["globals"]!["TargetFramework"] = "net8.0";
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results[1].Status);
        Assert.IsEmpty(result.Outputs);
    }

    [TestMethod]
    public async Task ReorderedBatchResultsRetainOriginalVariantBinding()
    {
        using var fixture = DotNetExecutionFixture.Shared(shared!, sharedCommit, "None");
        CiPlan plan = WithNet8Variant(fixture, "None");
        fixture.ModifyBatchResponse = response =>
        {
            JsonArray entries = response["entries"]!.AsArray();
            JsonNode first = entries[0]!;
            first["build"] = "Failure";
            entries.RemoveAt(0);
            entries.Add(first);
        };
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken, plan);
        Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Passed, result.Results[1].Status);
        Assert.AreEqual(plan.Checks[1].Work.Key, Assert.ContainsSingle(result.Outputs).Key);
        Assert.IsNull(result.TestBatch);
        Assert.IsNotNull(result.BatchBuild);
        Assert.HasCount(2, result.BatchBuild.Keys);
    }

    private static CiPlan WithNet8Variant(DotNetExecutionFixture fixture, string capability)
    {
        CiPlan alternate = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Plan.Candidate, capability, DotNetNativeFixture.Globals("net8.0"));
        return fixture.Plan with { Checks = [.. fixture.Plan.Checks, .. alternate.Checks] };
    }

    [TestMethod]
    public async Task IncompleteRetainedPlanStopsBeforeProductCommands()
    {
        using var fixture = Shared();
        CiPlan plan = fixture.Plan with { Checks = fixture.Plan.Checks[..1] };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.RunAsync(
            context.CancellationToken, plan));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow("test-capability", "VSTest")]
    [DataRow(DotNetTools.Dimension, """{"python":"1.0.1"}""")]
    public async Task RetainedBuildAndTestRequireMatchingCapabilitiesAndTools(string dimension,
        string value)
    {
        using var fixture = Shared();
        CiPlan plan = fixture.Plan;
        foreach (PlannedCheck check in plan.Checks)
            check.Work.Dimensions[DotNetTools.Dimension] = """{"python":"1.0.0"}""";
        PlannedCheck test = plan.Checks.Single(check => check.Work.Key.Check == DotNetChecks.Test);
        test.Work.Dimensions[dimension] = value;

        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan));

        Assert.AreEqual("Incomplete selected native identity obligations.", error.Message);
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    public async Task ProjectAbsentFromCandidateCannotStartBuild()
    {
        using var fixture = Shared();
        CiPlan plan = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Plan.Candidate, "MTP", project: "uncommitted.csproj");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.RunAsync(context.CancellationToken, plan));

        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    public async Task EmptyPlanRunsNoProductCommandAndNoneBuildsOnly()
    {
        using var fixture = DotNetExecutionFixture.Shared(shared!, sharedCommit, "None");
        DotNetRunResult empty = await fixture.RunAsync(context.CancellationToken,
            fixture.Plan with { Checks = [] });
        Assert.IsEmpty(empty.Results);
        Assert.IsEmpty(fixture.Commands);
        DotNetRunResult build = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Passed, Assert.ContainsSingle(build.Results).Status);
        Assert.IsNull(build.TestBatch);
    }

    [TestMethod]
    [DataRow("Probe", "\0")]
    [DataRow("RestoreSources", "https://example.invalid/feed")]
    public void UnsupportedGlobalsFailBeforeCommands(string name, string value)
    {
        var globals = new Dictionary<string, string> { [name] = value };
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            DotNetChecks.Properties(globals));
        Assert.Contains(name, error.Message);
    }
}
