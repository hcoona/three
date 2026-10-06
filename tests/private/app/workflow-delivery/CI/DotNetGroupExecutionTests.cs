using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetGroupExecutionTests(TestContext context)
{
    [TestMethod]
    [DataRow("MSBuildProjectFullPath")]
    [DataRow("PackageId")]
    [DataRow("NuGetPackageVersion")]
    [DataRow("PackageVersion")]
    [DataRow("AssemblyVersion")]
    [DataRow("AssemblyFileVersion")]
    [DataRow("AssemblyInformationalVersion")]
    public async Task TransferredPackageRejectsDifferentNativeProjection(string property)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        string original = JsonSerializer.Serialize(fixture.Plan, TransferJson.Default.CiPlan);
        CiPlan transferred = JsonSerializer.Deserialize(original, TransferJson.Default.CiPlan)!;
        string receiver = Path.Combine(fixture.Scratch, "receiver");
        await fixture.Repo.GitAsync("worktree", "add", "--detach", receiver, fixture.Candidate);
        fixture.ModifyPack = document =>
        {
            var values = document["Properties"]!.AsObject();
            values["MSBuildProjectFullPath"] =
                Path.GetFullPath(Path.Combine(receiver, DotNetPackageFixture.Project));
            if (property == "MSBuildProjectFullPath")
                values[property] = fixture.Node.Identity.Project;
            else if (property is "NuGetPackageVersion" or "PackageVersion")
            {
                values["NuGetPackageVersion"] = "2.0.0";
                values["PackageVersion"] = "2.0.0";
            }
            else values[property] = property switch
            {
                "PackageId" => "Different.Native.Package",
                "AssemblyInformationalVersion" => "different-native-source",
                _ => "2.0.0.0",
            };
        };
        string execution = Directory.CreateDirectory(
            Path.Combine(fixture.Scratch, "received-run")).FullName;
        DotNetGroupRunResult result = await DotNetGroupExecution.RunAsync(transferred,
            new(receiver, execution, 60), DotNetExecution.RunAsync,
            (plan, request, token) => DotNetPackageExecution.RunAsync(plan, request,
                fixture.ExecuteAsync, token), context.CancellationToken);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == DotNetPackageChecks.Pack).Status);
        Assert.IsTrue(result.Results.Where(item => item.Key.Check != DotNetPackageChecks.Pack)
            .All(item => item.Status == CheckStatus.Skipped));
        Assert.HasCount(3, result.Results);
        Assert.IsEmpty(result.Packages.Packages);
        Assert.IsEmpty(result.Packages.ConsumerOutputs);
        Assert.IsEmpty(result.Retained.Commands);
        Assert.AreEqual(receiver, Assert.ContainsSingle(fixture.Commands).Directory);
        Assert.AreEqual(original, JsonSerializer.Serialize(transferred,
            TransferJson.Default.CiPlan));
        string failure = Assert.ContainsSingle(result.Packages.Failures).Error;
        Assert.Contains(property == "MSBuildProjectFullPath" ? "project identity" :
            "NBGV expectations", failure);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OriginalMixedPlanRequiresBothPartitions(bool packageComplete)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        CiPlan retained = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Candidate, "MTP", project: DotNetPackageFixture.Project);
        CiPlan original = fixture.Plan with
        {
            Checks = [.. retained.Checks, ..
            fixture.Plan.Checks]
        };
        DotNetGroupRunResult result = await DotNetGroupExecution.RunAsync(original, fixture.Request,
            (plan, request, _) =>
            {
                Assert.AreEqual(original.Comparison, plan.Comparison);
                Assert.AreEqual(original.Candidate, plan.Candidate);
                Assert.AreEqual(original.Scope, plan.Scope);
                Assert.HasCount(2, plan.Checks);
                Assert.EndsWith("retained", request.Scratch);
                return Task.FromResult(new DotNetRunResult(plan.Candidate,
                    plan.Checks.Select(item => new CheckResult(plan.Candidate,
                        item.Work.Key, CheckStatus.Passed)).ToArray(), [], [], []));
            },
            (plan, request, _) =>
            {
                Assert.HasCount(3, plan.Checks);
                Assert.EndsWith("packages", request.Scratch);
                return Task.FromResult(new DotNetPackageRunResult(plan.Candidate,
                    plan.Checks.Take(packageComplete ? 3 : 2).Select(item =>
                        new CheckResult(plan.Candidate, item.Work.Key, CheckStatus.Passed))
                        .ToArray(), [], [], [], []));
            }, context.CancellationToken);
        Assert.AreEqual(packageComplete, result.Outcome.Satisfied);
        Assert.HasCount(5, result.Outcome.Checks);
        if (!packageComplete)
            Assert.AreEqual("missing", result.Outcome.Checks.Single(item =>
                item.Key.Check == DotNetPackageChecks.Consumer).Status);
        Assert.HasCount(5, original.Checks);
    }

    [TestMethod]
    public async Task InvalidSelectedContractFailsBeforeEffects()
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        CiPlan retained = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Candidate, "None", project: DotNetPackageFixture.Project);
        PlannedCheck broken = fixture.Plan.Checks[0] with
        { Work = fixture.Plan.Checks[0].Work with { Dimensions = [] } };
        CiPlan original = fixture.Plan with
        { Checks = [.. retained.Checks, broken, .. fixture.Plan.Checks.Skip(1)] };
        bool effects = false;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => DotNetGroupExecution.RunAsync(
            original, fixture.Request, (_, _, _) =>
            { effects = true; throw new InvalidOperationException("Product effects."); },
            (_, _, _) => throw new InvalidOperationException("Package effects."),
                context.CancellationToken));
        Assert.IsFalse(effects);
    }

    [TestMethod]
    public async Task EmptyPlanHasNoSelectedCommands()
    {
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
        CiPlan original = fixture.Plan with { Checks = [] };
        DotNetGroupRunResult result = await DotNetGroupExecution.RunAsync(original,
            fixture.Request, context.CancellationToken);
        Assert.IsTrue(result.Outcome.Satisfied);
        Assert.AreEqual(original.Comparison, result.Outcome.Comparison);
        Assert.AreEqual(original.Candidate, result.Outcome.Candidate);
        Assert.AreEqual(original.Scope, result.Outcome.Scope);
        Assert.IsEmpty(result.Results);
        Assert.IsEmpty(result.Retained.Commands);
        Assert.IsEmpty(result.Packages.Commands);
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Cancelled")]
    [DataRow("TimedOut")]
    public async Task FailedTestRetainsBuild(string nativeStatus)
    {
        CheckStatus status = Enum.Parse<CheckStatus>(nativeStatus);
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
        DotNetGroupRunResult result = await DotNetGroupExecution.RunAsync(fixture.Plan,
            fixture.Request, (plan, _, _) => Task.FromResult(new DotNetRunResult(plan.Candidate,
                plan.Checks.Select(item => new CheckResult(plan.Candidate, item.Work.Key,
                    item.Work.Key.Check == DotNetChecks.Build ? CheckStatus.Passed : status))
                    .ToArray(), [], [], [])), DotNetPackageExecution.RunAsync,
                        context.CancellationToken);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Build).Status);
        Assert.AreEqual(status, result.Results.Single(item => item.Key.Check ==
            DotNetChecks.Test).Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualCancellationRetainsCompletedPartition(bool selectedPackages)
    {
        using var fixture = await DotNetPackageFixture.CreateAsync(context.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        CiPlan retained = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Candidate, "MTP", project: DotNetPackageFixture.Project);
        CiPlan original = retained with
        { Checks = [.. retained.Checks, .. selectedPackages ? fixture.Plan.Checks : []] };
        string serialized = JsonSerializer.Serialize(original, TransferJson.Default.CiPlan);
        DotNetRunResult? completed = null;
        DotNetGroupRunResult result = await DotNetGroupExecution.RunAsync(original, fixture.Request,
            async (plan, _, token) =>
            {
                cancellation.Cancel();
                CheckKey test = plan.Checks.Single(item =>
                    item.Work.Key.Check == DotNetChecks.Test).Work.Key;
                var command = new NativeCommand("dotnet", fixture.Repo.Directory, ["test"], 60);
                NativeCommandResult observation = await NativeProcess.ExecuteAsync(command, token);
                Assert.AreEqual(NativeTermination.Cancelled, observation.Termination);
                completed = new(plan.Candidate, plan.Checks.Select(item =>
                    new CheckResult(plan.Candidate, item.Work.Key,
                        item.Work.Key.Check == DotNetChecks.Build ? CheckStatus.Passed :
                            CheckStatus.Cancelled)).ToArray(), [new(test, command, observation)],
                    [], []);
                return completed;
            }, DotNetPackageExecution.RunAsync, cancellation.Token);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreSame(completed, result.Retained);
        Assert.AreEqual(NativeTermination.Cancelled,
            Assert.ContainsSingle(result.Retained.Commands).Result.Termination);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Build).Status);
        Assert.AreEqual(CheckStatus.Cancelled, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Test).Status);
        Assert.HasCount(selectedPackages ? 3 : 0, result.Packages.Results);
        Assert.IsTrue(result.Packages.Results.All(item => item.Status == CheckStatus.Cancelled));
        Assert.IsEmpty(result.Packages.Commands);
        Assert.IsEmpty(result.Packages.Packages);
        Assert.HasCount(original.Checks.Length, result.Results);
        Assert.AreEqual(serialized,
            JsonSerializer.Serialize(original, TransferJson.Default.CiPlan));
    }

    [TestMethod]
    [DataRow(true, 0)]
    [DataRow(false, 1)]
    public async Task CliCollectsOriginalTransferredPlanDespiteClaimedOutcome(bool complete,
        int expectedExit)
    {
        using var fixture = await DotNetExecutionFixture.CreateAsync(context.CancellationToken);
        string planPath = Path.Combine(fixture.Scratch, "plan.json");
        string requestPath = Path.Combine(fixture.Scratch, "request.json");
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(fixture.Plan,
            TransferJson.Default.CiPlan), context.CancellationToken);
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(fixture.Request,
            TransferJson.Default.DotNetRunRequest), context.CancellationToken);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["ci", "run-dotnet", planPath, requestPath], output, error,
            runDotNet: (plan, _, _) =>
            {
                CheckResult[] results = plan.Checks.Take(complete ? 2 : 1).Select(item =>
                    new CheckResult(plan.Candidate, item.Work.Key, CheckStatus.Passed)).ToArray();
                return Task.FromResult(new DotNetGroupRunResult(plan.Candidate, results,
                    new(plan.Candidate, results, [], [], []),
                    new(plan.Candidate, [], [], [], [], []),
                    new(plan.Comparison, plan.Candidate, plan.Scope, true, [], [])));
            });
        Assert.AreEqual(expectedExit, exit, error.ToString());
        Assert.AreEqual("", error.ToString());
        DotNetGroupRunResult transferred = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.DotNetGroupRunResult)!;
        Assert.AreEqual(fixture.Plan.Candidate, transferred.Candidate);
        Assert.HasCount(complete ? 2 : 1, transferred.Results);
        Assert.AreEqual(complete, ResultCollector.Collect(fixture.Plan,
            transferred.Results).Satisfied);
    }
}
