using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetExecutionTests(TestContext context)
{
    [TestMethod]
    [DataRow("MTP", "InvokeTestingPlatform")]
    [DataRow("VSTest", "VSTest")]
    public async Task NativeStagesPreserveOriginalGlobalsAndNativeOutput(string capability,
        string target)
    {
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken,
            capability);
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.HasCount(2, fixture.Commands);
        Assert.Contains("-target:Build,GetTargetPath", fixture.Commands[0].Arguments);
        Assert.Contains("-target:Build,GetTargetPath," + target, fixture.Commands[1].Arguments);
        Assert.Contains("-getTargetResult:GetTargetPath," + target, fixture.Commands[1].Arguments);
        Assert.IsTrue(fixture.Commands.All(command => command.Executable == "dotnet" &&
            command.Directory == fixture.Repo.Directory &&
            command.Arguments.Contains("-noAutoResponse") &&
            command.Arguments.Contains("-property:IsGraphBuild=true") &&
            !command.Arguments.Contains("-restore") && !command.Arguments.Contains("-graphBuild")));
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
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationName);
        CheckStatus status = Enum.Parse<CheckStatus>(statusName);
        fixture.Override = command => command.Arguments.Contains(
            "-target:Build,GetTargetPath,InvokeTestingPlatform") ?
            new(termination, 1, "test failed after its Build prelude", "", 0.1, "failure") : null;
        DotNetRunResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Build).Status);
        Assert.AreEqual(status, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Test).Status);
        Assert.IsFalse(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied);
        Assert.HasCount(2, fixture.Commands);
    }

    [TestMethod]
    public async Task FailedBuildSkipsTestAndMissingCollectionRemainsUnsatisfied()
    {
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
        fixture.Override = command =>
        {
            if (!command.Arguments.Contains("-target:Build,GetTargetPath,InvokeTestingPlatform"))
                return null;
            if (defect == "invalid")
            {
                string response = command.Arguments.Single(argument => argument.StartsWith(
                    "-getResultOutputFile:", StringComparison.Ordinal));
                File.WriteAllText(response["-getResultOutputFile:".Length..], "{");
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
    [DataRow("target")]
    [DataRow("verdict")]
    [DataRow("missing-output")]
    [DataRow("ambiguous-output")]
    [DataRow("coordinate")]
    [DataRow("nonfile")]
    public async Task MissingOrInvalidNativeOutputFails(string defect)
    {
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
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
    [DataRow("scope")]
    [DataRow("origin")]
    [DataRow("capability")]
    [DataRow("global")]
    [DataRow("required")]
    [DataRow("missing-test")]
    [DataRow("missing-build")]
    public async Task InvalidPlanStopsBeforeProductCommands(string defect)
    {
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
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
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken,
            "None");
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
