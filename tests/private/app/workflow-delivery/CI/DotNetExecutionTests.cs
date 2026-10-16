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
        Assert.HasCount(2, fixture.Commands);
        Assert.HasCount(2, result.Outputs);
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
        Assert.HasCount(2, fixture.Commands);
        Assert.Contains("-target:BuildSelected", fixture.Commands[0].Arguments);
        Assert.IsNotNull(result.BatchBuild);
        DotNetBuildRequest request = JsonSerializer.Deserialize(await File.ReadAllTextAsync(
            Path.Combine(fixture.Scratch, "build-request.json"), context.CancellationToken),
            TransferJson.Default.DotNetBuildRequest)!;
        Assert.AreEqual("true", Assert.ContainsSingle(request.Entries).Globals["IsGraphBuild"]);
        Assert.Contains("-target:TestSelected", fixture.Commands[1].Arguments);
        DotNetTestRequest tests = JsonSerializer.Deserialize(await File.ReadAllTextAsync(
            Path.Combine(fixture.Scratch, "test-request.json"), context.CancellationToken),
            TransferJson.Default.DotNetTestRequest)!;
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
    [DataRow("Exited", "Failed")]
    [DataRow("Cancelled", "Cancelled")]
    [DataRow("TimedOut", "TimedOut")]
    [DataRow("CleanupFailed", "Failed")]
    public async Task FailedTestCannotEraseEarlierBuild(string terminationName, string statusName)
    {
        using var fixture = Shared();
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationName);
        CheckStatus status = Enum.Parse<CheckStatus>(statusName);
        fixture.Override = command => command.Arguments.Contains("-target:TestSelected") ?
            new(termination, 1, "test failed after its Build prelude", "", 0.1, "failure") : null;
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Build).Status);
        Assert.AreEqual(status, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Test).Status);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.HasCount(2, fixture.Commands);
        Assert.AreEqual(termination, result.TestBatch!.Result.Termination);
        Assert.IsNotNull(result.BatchBuild);
        Assert.AreEqual(DotNetChecks.Build, Assert.ContainsSingle(result.Outputs).Key.Check);
    }

    [TestMethod]
    [DataRow("Cancelled", "Cancelled")]
    [DataRow("TimedOut", "TimedOut")]
    public async Task InterruptedBatchCannotPassFromSuccessfulBatchResponse(
        string terminationName, string statusName)
    {
        using var fixture = Shared();
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationName);
        CheckStatus status = Enum.Parse<CheckStatus>(statusName);
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
        Assert.HasCount(1, fixture.Commands);
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
    public async Task FailedBuildSkipsTestAndMissingCollectionRemainsUnsatisfied()
    {
        using var fixture = Shared();
        fixture.Override = _ => new(NativeTermination.StartFailed, null, "", "", 0, "cannot start");
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results[1].Status);
        Assert.HasCount(1, fixture.Commands);
        CiOutcome missing = ResultCollector.Collect(fixture.Plan, result.Results[..1]);
        Assert.IsFalse(missing.Satisfied);
        Assert.AreEqual("missing", missing.Checks[1].Status);
        CiOutcome conflict = ResultCollector.Collect(fixture.Plan,
            [.. result.Results, result.Results[0] with { Status = CheckStatus.Passed }]);
        Assert.AreEqual("conflicting", conflict.Checks[0].Status);
        Assert.IsFalse(conflict.Satisfied);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("invalid")]
    public async Task MissingTestReceiptRetainsBuildWithoutFabricatingTest(string defect)
    {
        using var fixture = Shared();
        fixture.Override = command =>
        {
            if (!command.Arguments.Contains("-target:TestSelected")) return null;
            if (defect == "invalid")
            {
                string receipts = Directory.CreateDirectory(Path.Combine(fixture.Scratch,
                    "test-receipts")).FullName;
                File.WriteAllText(DotNetTestTask.Receipt(receipts, 0), "{");
            }
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
    [DataRow("test", "Failed")]
    [DataRow("overall", "Failed")]
    [DataRow("identity", "Failed")]
    [DataRow("target", "Failed")]
    [DataRow("receipt", "Cancelled")]
    public async Task TestBatchEntryCannotMaskAnotherEntry(string defect, string statusName)
    {
        using var fixture = DotNetExecutionFixture.Shared(shared!, sharedCommit);
        CiPlan second = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Plan.Candidate, "MTP", DotNetNativeFixture.Globals("net8.0"));
        CiPlan plan = fixture.Plan with { Checks = [.. fixture.Plan.Checks, .. second.Checks] };
        bool missing = defect == "receipt";
        fixture.ModifyTestReceipt = (index, receipt) =>
        {
            if (index != 0) return;
            switch (defect)
            {
                case "test": receipt["test"] = "Failure"; break;
                case "overall": receipt["overall"] = "Failure"; break;
                case "identity":
                    receipt["identity"]!["globals"]!["TargetFramework"] = "net8.0";
                    break;
                case "target": receipt["testTarget"] = "VSTest"; break;
            }
            receipt["log"] = "entry zero diagnostic";
        };
        DotNetRunResult result = await DotNetExecution.RunAsync(plan, fixture.Request,
            async (command, token) =>
            {
                NativeCommandResult native = await fixture.ExecuteAsync(command, token);
                if (!missing || !command.Arguments.Contains("-target:TestSelected"))
                    return native;
                // An interrupted session retains completed receipts, never the unfinished one.
                File.Delete(DotNetTestTask.Receipt(Path.Combine(fixture.Scratch,
                    "test-receipts"), 0));
                return native with
                {
                    Termination = NativeTermination.Cancelled,
                    ExitCode = null,
                    Error = "Command cancelled.",
                };
            }, context.CancellationToken);
        CheckKey first = plan.Checks[1].Work.Key;
        CheckKey other = plan.Checks[3].Work.Key;
        Assert.AreEqual(Enum.Parse<CheckStatus>(statusName),
            result.Results.Single(item => item.Key == first).Status);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item => item.Key == other)
            .Status);
        Assert.AreEqual(first, Assert.ContainsSingle(result.Failures).Key);
        if (defect is "test" or "overall")
            Assert.Contains("entry zero diagnostic", result.Failures[0].Error);
        Assert.IsTrue(result.Outputs.Any(output => output.Key == other));
        Assert.IsFalse(result.Outputs.Any(output => output.Key == first));
        Assert.HasCount(2, result.TestBatch!.Keys);
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
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
    [DataRow("target")]
    [DataRow("verdict")]
    [DataRow("missing-output")]
    [DataRow("ambiguous-output")]
    [DataRow("coordinate")]
    [DataRow("nonfile")]
    public async Task MissingOrInvalidNativeOutputFails(string defect)
    {
        using var fixture = Shared();
        fixture.ModifyResponse = response =>
        {
            JsonObject targets = response["TargetResults"]!.AsObject();
            JsonObject output = targets["GetTargetPath"]!.AsObject();
            switch (defect)
            {
                case "target": targets.Remove("Build"); break;
                case "verdict": output["Result"] = "Skipped"; break;
                case "missing-output": output["Items"] = new JsonArray(); break;
                case "ambiguous-output":
                    output["Items"]!.AsArray().Add(
                    output["Items"]![0]!.DeepClone()); break;
                case "coordinate": output["Items"]![0]!["FullPath"] = "relative.bin"; break;
                case "nonfile": output["Items"]![0]!["FullPath"] = fixture.Scratch; break;
            }
        };
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results[1].Status);
        Assert.IsEmpty(result.Outputs);
        Assert.IsNotEmpty(result.Failures);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("project")]
    [DataRow("globals")]
    public async Task MissingOrDifferentBatchIdentityCannotPass(string defect)
    {
        using var fixture = Shared();
        fixture.ModifyBatchResponse = response =>
        {
            JsonArray entries = response["entries"]!.AsArray();
            if (defect == "missing") entries.Clear();
            else if (defect == "duplicate") entries.Add(entries[0]!.DeepClone());
            else if (defect == "project") entries[0]!["identity"]!["project"] =
                Path.Combine(fixture.Repo.Directory, "other.csproj");
            else entries[0]!["identity"]!["globals"]!["TargetFramework"] = "net8.0";
        };
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, result.Results[0].Status);
        Assert.AreEqual(CheckStatus.Skipped, result.Results[1].Status);
        Assert.HasCount(1, fixture.Commands);
        Assert.IsEmpty(result.Outputs);
    }

    [TestMethod]
    public async Task ReorderedBatchResultsRetainOriginalVariantBinding()
    {
        using var fixture = DotNetExecutionFixture.Shared(shared!, sharedCommit, "None");
        var globals = new Dictionary<string, string>(DotNetNativeFixture.Globals("net8.0"));
        CiPlan second = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Plan.Candidate, "None", globals);
        CiPlan plan = fixture.Plan with { Checks = [.. fixture.Plan.Checks, .. second.Checks] };
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
        Assert.AreEqual(second.Checks[0].Work.Key, Assert.ContainsSingle(result.Outputs).Key);
        Assert.HasCount(1, fixture.Commands);
        Assert.IsNull(result.TestBatch);
        Assert.IsNotNull(result.BatchBuild);
        Assert.HasCount(2, result.BatchBuild.Keys);
    }

    [TestMethod]
    [DataRow("scope")]
    [DataRow("origin")]
    [DataRow("capability")]
    [DataRow("global")]
    [DataRow("required")]
    [DataRow("missing-test")]
    [DataRow("missing-build")]
    public async Task InvalidPlanStopsBeforeProductCommands(string defect)
    {
        using var fixture = Shared();
        CiPlan plan = fixture.Plan;
        PlannedCheck first = plan.Checks[0];
        if (defect == "scope") plan = plan with { Scope = "other" };
        else if (defect == "missing-test") plan = plan with { Checks = plan.Checks[..1] };
        else if (defect == "missing-build") plan = plan with { Checks = plan.Checks[1..] };
        else
        {
            if (defect == "origin") first = first with
            { Origins = [CheckOrigin.Preset], QualityPresets = ["adopted-quality"] };
            else if (defect == "required") first = first with
            { Work = first.Work with { Required = false } };
            else
            {
                var dimensions = new Dictionary<string, string>(first.Work.Dimensions);
                dimensions[defect == "capability" ? "test-capability" : "globals"] =
                    defect == "capability" ? "unknown" : "{}";
                first = first with { Work = first.Work with { Dimensions = dimensions } };
            }
            plan = plan with { Checks = [first, .. plan.Checks[1..]] };
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.RunAsync(
            context.CancellationToken, plan));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow("scratch-used")]
    [DataRow("scratch-inside-checkout")]
    [DataRow("scratch-missing")]
    [DataRow("scratch-relative")]
    [DataRow("deadline")]
    [DataRow("dirty")]
    [DataRow("wrong-head")]
    [DataRow("uncommitted-project")]
    public async Task InvalidRequestStopsBeforeProductCommands(string defect)
    {
        // Rows that change the checkout keep a private repository.
        using var fixture = defect is "scratch-inside-checkout" or "dirty" or "wrong-head"
            ? await DotNetExecutionFixture.CreateAsync(context.CancellationToken) : Shared();
        DotNetRunRequest request = fixture.Request;
        CiPlan plan = fixture.Plan;
        switch (defect)
        {
            case "scratch-used":
                await File.WriteAllTextAsync(Path.Combine(fixture.Scratch,
                "existing"), "used", context.CancellationToken); break;
            case "scratch-inside-checkout":
                request = request with
                {
                    Scratch = Directory.CreateDirectory(Path.Combine(fixture.Repo.Directory,
                        "scratch")).FullName
                }; break;
            case "scratch-missing":
                request = request with
                { Scratch = Path.Combine(fixture.Scratch, "absent") }; break;
            case "scratch-relative": request = request with { Scratch = "scratch" }; break;
            case "deadline": request = request with { DeadlineSeconds = 0 }; break;
            case "dirty":
                await File.AppendAllTextAsync(Path.Combine(fixture.Repo.Directory,
                "product.csproj"), "changed", context.CancellationToken); break;
            case "wrong-head":
                await fixture.Repo.SetAsync("new.txt", "new candidate");
                string other = await fixture.Repo.CommitAsync(plan.Candidate);
                await fixture.Repo.GitAsync("reset", "--hard", other);
                break;
            case "uncommitted-project":
                plan = DotNetExecutionFixture.CreatePlan(
                fixture.Repo.Directory, plan.Candidate, "MTP", project: "uncommitted.csproj");
                break;
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => DotNetExecution.RunAsync(
            plan, request, fixture.ExecuteAsync, context.CancellationToken));
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
        Assert.HasCount(1, fixture.Commands);
    }

    [TestMethod]
    [DataRow("nul", "Probe", "\0")]
    [DataRow("restore-sources", "RestoreSources", "https://example.invalid/feed")]
    public void UnsupportedGlobalsFailBeforeCommands(string scenario, string name, string value)
    {
        var globals = new Dictionary<string, string> { [name] = value };
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            DotNetChecks.Properties(globals));
        Assert.Contains(name, error.Message, scenario);
    }
}
