using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static DotNetPackageFixture PackageFixture() =>
        DotNetPackageFixture.Shared(shared!, sharedCandidate);

    private static DotNetExecutionFixture RetainedFixture() =>
        new(shared!, DotNetExecutionFixture.CreatePlan(shared!.Directory, sharedCandidate, "MTP",
            project: DotNetPackageFixture.Project), ownsRepository: false);

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
    public async Task TransferredPackageCannotUseDifferentVersionProjection()
    {
        DotNetGroupRunResult result = await RunTransferredAsync(properties =>
        {
            properties["NuGetPackageVersion"] = "2.0.0";
            properties["PackageVersion"] = "2.0.0";
        });
        Assert.IsFalse(result.Outcome.Satisfied);
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == DotNetPackageChecks.Pack).Status);
        Assert.IsTrue(result.Results.Where(item => item.Key.Check != DotNetPackageChecks.Pack)
            .All(item => item.Status == CheckStatus.Skipped));
        Assert.HasCount(3, result.Results);
        Assert.IsEmpty(result.Packages.Packages);
        Assert.IsEmpty(result.Packages.ConsumerOutputs);
        Assert.IsNull(result.Retained.TestBatch);
        string failure = Assert.ContainsSingle(result.Packages.Failures).Error;
        Assert.Contains("NBGV expectations", failure);
    }

    private async Task<DotNetGroupRunResult> RunTransferredAsync(
        Action<JsonObject>? alterProjection)
    {
        using var fixture = PackageFixture();
        string original = JsonSerializer.Serialize(fixture.Plan, TransferJson.Default.CiPlan);
        CiPlan transferred = JsonSerializer.Deserialize(original, TransferJson.Default.CiPlan)!;
        string receiver = sharedReceiver;
        fixture.ModifyPack = document =>
        {
            var values = document["Properties"]!.AsObject();
            values["MSBuildProjectFullPath"] =
                Path.GetFullPath(Path.Combine(receiver, DotNetPackageFixture.Project));
            alterProjection?.Invoke(values);
        };
        string execution = Directory.CreateDirectory(
            Path.Combine(fixture.Scratch, "received-run")).FullName;
        DotNetGroupRunResult result = await DotNetGroupExecution.RunAsync(transferred,
            new(receiver, execution, 60), null, null, context.CancellationToken,
            fixture.ExecuteAsync);
        if (alterProjection is not null)
            Assert.AreEqual(receiver, Assert.ContainsSingle(fixture.Commands).Directory);
        Assert.AreEqual(original, JsonSerializer.Serialize(transferred,
            TransferJson.Default.CiPlan));
        return result;
    }

    [TestMethod]
    [DataRow(0, true, (int)CheckStatus.Passed)]
    [DataRow(1, false, (int)CheckStatus.Failed)]
    public async Task MixedPlanQualifiesOnlyWhenBothPartitionsPass(int restoreExit,
        bool satisfied, int consumerStatus)
    {
        using var packages = PackageFixture();
        using var retained = RetainedFixture();
        CiPlan original = Combine(retained.Plan, packages.Plan);
        if (restoreExit != 0)
            packages.Override = command => command.Arguments[0] == "restore"
                ? new(NativeTermination.Exited, restoreExit, "", "restore failed", 0.1, null)
                : null;

        DotNetGroupRunResult result = await RunMixedAsync(original, retained, packages,
            context.CancellationToken);

        Assert.AreEqual(satisfied, result.Outcome.Satisfied);
        CollectionAssert.AreEquivalent(original.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.IsTrue(result.Retained.Results.All(item => item.Status == CheckStatus.Passed));
        Assert.ContainsSingle(result.Packages.Packages);
        Assert.AreEqual((CheckStatus)consumerStatus, result.Results.Single(item =>
            item.Key.Check == DotNetPackageChecks.Consumer).Status);
    }

    [TestMethod]
    public async Task InvalidSelectedContractFailsBeforeEffects()
    {
        using var fixture = PackageFixture();
        CiPlan retained = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Candidate, "None", project: DotNetPackageFixture.Project);
        PlannedCheck broken = fixture.Plan.Checks[0] with
        { Work = fixture.Plan.Checks[0].Work with { Dimensions = [] } };
        CiPlan original = fixture.Plan with
        { Checks = [.. retained.Checks, broken, .. fixture.Plan.Checks.Skip(1)] };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => DotNetGroupExecution.RunAsync(
            original, fixture.Request, null, null, context.CancellationToken,
            (_, _) => throw new AssertFailedException("Incomplete plan reached native work.")));
    }

    [TestMethod]
    public async Task MixedToolGroupsFailBeforeEffects()
    {
        using var fixture = PackageFixture();
        CiPlan retained = DotNetExecutionFixture.CreatePlan(fixture.Repo.Directory,
            fixture.Candidate, "MTP", project: DotNetPackageFixture.Project);
        PlannedCheck[] node = [.. retained.Checks.Select(check => check with
        {
            Work = check.Work with
            {
                Dimensions = new(check.Work.Dimensions)
                { [DotNetTools.Dimension] = "{\"node\":\"24.1.0\"}" },
            },
        })];
        CiPlan original = fixture.Plan with { Checks = [.. node, .. fixture.Plan.Checks] };
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => DotNetGroupExecution.RunAsync(original, fixture.Request, null, null,
                context.CancellationToken,
                (_, _) => throw new AssertFailedException("Mixed tools reached native work.")));
        Assert.Contains("one external tool group", error.Message);
    }

    [TestMethod]
    public async Task EmptyPlanHasNoSelectedCommands()
    {
        using var fixture = RetainedFixture();
        CiPlan original = fixture.Plan with { Checks = [] };
        DotNetGroupRunResult result = await DotNetGroupExecution.RunAsync(original,
            fixture.Request, context.CancellationToken);
        Assert.IsTrue(result.Outcome.Satisfied);
        Assert.AreEqual(original.Comparison, result.Outcome.Comparison);
        Assert.AreEqual(original.Candidate, result.Outcome.Candidate);
        Assert.AreEqual(original.Scope, result.Outcome.Scope);
        Assert.IsEmpty(result.Results);
        Assert.IsNull(result.Retained.TestBatch);
        Assert.IsEmpty(result.Packages.Commands);
    }

    [TestMethod]
    public async Task CancelledTestRetainsCompletedBuildAndSkipsNativePackageWork()
    {
        using var retained = RetainedFixture();
        using var packages = PackageFixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        CiPlan original = Combine(retained.Plan, packages.Plan);
        retained.Override = command =>
        {
            if (!command.Arguments.Contains("-target:TestSelected")) return null;
            cancellation.Cancel();
            return new(NativeTermination.Cancelled, null, "", "", 0, "Command cancelled.");
        };

        DotNetGroupRunResult result = await RunMixedAsync(original, retained, packages,
            cancellation.Token);

        Assert.IsFalse(result.Outcome.Satisfied);
        CollectionAssert.AreEquivalent(original.Checks.Select(item => item.Work.Key).ToArray(),
            result.Results.Select(item => item.Key).ToArray());
        Assert.AreEqual(NativeTermination.Cancelled,
            result.Retained.TestBatch!.Result.Termination);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Build).Status);
        Assert.AreEqual(CheckStatus.Cancelled, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Test).Status);
        Assert.IsTrue(result.Packages.Results.All(item => item.Status == CheckStatus.Cancelled));
        Assert.IsEmpty(result.Packages.Commands);
        Assert.IsEmpty(result.Packages.Packages);
    }

    private static CiPlan Combine(CiPlan retained, CiPlan packages) =>
        packages with { Checks = [.. retained.Checks, .. packages.Checks] };

    private static Task<DotNetGroupRunResult> RunMixedAsync(CiPlan plan,
        DotNetExecutionFixture retained, DotNetPackageFixture packages, CancellationToken token) =>
        DotNetGroupExecution.RunAsync(plan, packages.Request, null, null, token,
            (command, cancellation) =>
                command.Arguments[^1] is "-target:BuildSelected" or "-target:TestSelected"
                    ? retained.ExecuteAsync(command, cancellation)
                    : packages.ExecuteAsync(command, cancellation));
}
