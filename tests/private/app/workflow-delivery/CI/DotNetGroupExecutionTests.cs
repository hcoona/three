using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetGroupExecutionTests(TestContext context)
{
    // Transferred-projection rows read one committed repository and one detached receiver
    // worktree created here; each row still owns fresh scratch for every native effect.
    private static GitFixture? shared;
    private static string sharedCandidate = "";
    private static string sharedReceiver = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedCandidate = await DotNetPackageFixture.CommitAsync(repo);
            // A sibling of the repository is removed with the fixture's temporary root.
            sharedReceiver = Path.Combine(Path.GetDirectoryName(repo.Directory)!, "receiver");
            await repo.GitAsync("worktree", "add", "--detach", sharedReceiver, sharedCandidate);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task TransferredPackageControlQualifies()
    {
        DotNetGroupRunResult result = await RunTransferredAsync(null);
        Assert.IsTrue(result.Outcome.Satisfied, string.Join("; ",
            result.Packages.Failures.Select(failure => failure.Error)));
        Assert.HasCount(3, result.Results);
        Assert.HasCount(1, result.Packages.Packages);
        Assert.HasCount(1, result.Packages.ConsumerOutputs);
    }

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
        DotNetGroupRunResult result = await RunTransferredAsync(property);
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == DotNetPackageChecks.Pack).Status);
        Assert.IsTrue(result.Results.Where(item => item.Key.Check != DotNetPackageChecks.Pack)
            .All(item => item.Status == CheckStatus.Skipped));
        Assert.HasCount(3, result.Results);
        Assert.IsEmpty(result.Packages.Packages);
        Assert.IsEmpty(result.Packages.ConsumerOutputs);
        Assert.IsEmpty(result.Retained.Commands);
        string failure = Assert.ContainsSingle(result.Packages.Failures).Error;
        Assert.Contains(property == "MSBuildProjectFullPath" ? "project identity" :
            "NBGV expectations", failure);
    }

    private async Task<DotNetGroupRunResult> RunTransferredAsync(string? property)
    {
        using var fixture = DotNetPackageFixture.Shared(shared!, sharedCandidate);
        string original = JsonSerializer.Serialize(fixture.Plan, TransferJson.Default.CiPlan);
        CiPlan transferred = JsonSerializer.Deserialize(original, TransferJson.Default.CiPlan)!;
        string receiver = sharedReceiver;
        fixture.ModifyPack = document =>
        {
            var values = document["Properties"]!.AsObject();
            values["MSBuildProjectFullPath"] =
                Path.GetFullPath(Path.Combine(receiver, DotNetPackageFixture.Project));
            if (property is null) return;
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
        if (property is not null)
            Assert.AreEqual(receiver, Assert.ContainsSingle(fixture.Commands).Directory);
        Assert.AreEqual(original, JsonSerializer.Serialize(transferred,
            TransferJson.Default.CiPlan));
        return result;
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
    public async Task OuterCancellationRetainsCompletedPartition(bool selectedPackages)
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
            (plan, _, token) =>
            {
                cancellation.Cancel();
                CheckKey test = plan.Checks.Single(item =>
                    item.Work.Key.Check == DotNetChecks.Test).Work.Key;
                Assert.IsTrue(token.IsCancellationRequested);
                var command = new NativeCommand("dotnet", fixture.Repo.Directory, ["test"], 60);
                var observation = new NativeCommandResult(NativeTermination.Cancelled, null, "",
                    "", 0, "Command cancelled.");
                DotNetRunResult run = new(plan.Candidate, plan.Checks.Select(item =>
                    new CheckResult(plan.Candidate, item.Work.Key,
                        item.Work.Key.Check == DotNetChecks.Build ? CheckStatus.Passed :
                            CheckStatus.Cancelled)).ToArray(), [new(test, command, observation)],
                    [], []);
                completed = run;
                return Task.FromResult(run);
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
    public async Task CliRetainsExecutionOwnerOutcomeAndMissingRequiredResults(bool complete,
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
                    ResultCollector.Collect(plan, results)));
            });
        Assert.AreEqual(expectedExit, exit, error.ToString());
        Assert.AreEqual("", error.ToString());
        DotNetGroupRunResult transferred = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.DotNetGroupRunResult)!;
        Assert.AreEqual(fixture.Plan.Candidate, transferred.Candidate);
        Assert.HasCount(complete ? 2 : 1, transferred.Results);
        Assert.AreEqual(complete, transferred.Outcome.Satisfied);
        Assert.AreEqual(fixture.Plan.Comparison, transferred.Outcome.Comparison);
        Assert.AreEqual(fixture.Plan.Scope, transferred.Outcome.Scope);
        Assert.HasCount(complete ? 0 : 1,
            transferred.Outcome.Checks.Where(check => check.Status == "missing"));
    }
}
