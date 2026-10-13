using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class DotNetNativeExecutionTests(TestContext context)
{
    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task NativeGlobalsRoundTripThroughSelectedSdk()
    {
        using var fixture = await DotNetNativeFixture.CreateAsync("VSTest",
            context.CancellationToken);
        Dictionary<string, string> globals = DotNetNativeFixture.Globals();
        foreach ((string name, string value) in new Dictionary<string, string>
        {
            ["ProbeEmpty"] = "",
            ["ProbeSpace"] = "two words",
            ["ProbeComma"] = "left%2Cright",
            ["ProbeSemicolon"] = "left%3Bright",
            ["ProbePercent"] = "percent%25tail",
            ["ProbeEquals"] = "left=right",
            ["ProbeQuote"] = "say \"hello\"",
            ["ProbeSlash"] = "space and trailing\\",
            ["ProbeTogether"] = "space %3B %25 = \"quote\" \\tail",
        }) globals.Add(name, value);
        using JsonDocument response = await fixture.QueryAsync(globals, globals.Keys.ToArray(),
            context.CancellationToken);
        JsonElement properties = response.RootElement.GetProperty("Properties");
        foreach ((string name, string original) in globals)
            Assert.AreEqual(ProjectCollection.Unescape(original),
                properties.GetProperty(name).GetString(),
                "Original native global: " + name);
    }

    [TestMethod]
    [DataRow("net10.0", true)]
    [DataRow("net8.0", false)]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task NativeMtpInnerVariantsRetainSettingsAndOutput(string framework, bool appHost)
    {
        using var fixture = await DotNetNativeFixture.CreateAsync("MTP", context.CancellationToken);
        Dictionary<string, string> globals = DotNetNativeFixture.Globals(framework, appHost);
        using JsonDocument properties = await fixture.QueryAsync(globals,
            ["IsTestingPlatformApplication", "TargetFrameworks", "UseAppHost"],
            context.CancellationToken);
        Assert.AreEqual("true", properties.RootElement.GetProperty("Properties")
            .GetProperty("IsTestingPlatformApplication").GetString());
        Assert.AreEqual("net10.0;net8.0", properties.RootElement.GetProperty("Properties")
            .GetProperty("TargetFrameworks").GetString());
        CiPlan plan = DotNetExecutionFixture.CreatePlan(fixture.Root, fixture.Candidate, "MTP",
            globals, DotNetNativeFixture.Project);
        DotNetRunResult result = await DotNetExecution.RunAsync(plan,
            new(fixture.Root, fixture.RunScratch, 60), context.CancellationToken);
        context.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.DotNetRunResult));
        AssertSuccessfulNativeRun(fixture, plan, result, framework, "InvokeTestingPlatform");
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task NativeVstestUsesProjectSettings()
    {
        using var fixture = await DotNetNativeFixture.CreateAsync("VSTest",
            context.CancellationToken);
        Dictionary<string, string> globals = DotNetNativeFixture.Globals();
        using JsonDocument properties = await fixture.QueryAsync(globals,
            ["IsTestingPlatformApplication", "IsTestProject"], context.CancellationToken);
        Assert.AreEqual("false", properties.RootElement.GetProperty("Properties")
            .GetProperty("IsTestingPlatformApplication").GetString());
        Assert.AreEqual("true", properties.RootElement.GetProperty("Properties")
            .GetProperty("IsTestProject").GetString());
        CiPlan plan = DotNetExecutionFixture.CreatePlan(fixture.Root, fixture.Candidate, "VSTest",
            globals, DotNetNativeFixture.Project);
        DotNetRunResult result = await DotNetExecution.RunAsync(plan,
            new(fixture.Root, fixture.RunScratch, 60), context.CancellationToken);
        context.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.DotNetRunResult));
        AssertSuccessfulNativeRun(fixture, plan, result, "net10.0", "VSTest");
    }

    [TestMethod]
    [DataRow("MTP")]
    [DataRow("VSTest")]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task NativeNoTestsPolicyRemainsNative(string capability)
    {
        using var fixture = await DotNetNativeFixture.CreateAsync(capability,
            context.CancellationToken);
        Dictionary<string, string> globals = DotNetNativeFixture.Globals();
        globals.Add("FixtureNoTests", "true");
        CiPlan plan = DotNetExecutionFixture.CreatePlan(fixture.Root, fixture.Candidate, capability,
            globals, DotNetNativeFixture.Project);
        DotNetRunResult result = await DotNetExecution.RunAsync(plan,
            new(fixture.Root, fixture.RunScratch, 60), context.CancellationToken);
        context.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.DotNetRunResult));
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Build).Status, Diagnostics(result));
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == DotNetChecks.Test).Status, Diagnostics(result));
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
        Assert.HasCount(1, result.Commands);
        Assert.IsNotNull(result.BatchBuild);
        NativeCommandResult native = result.Commands[0].Result;
        Assert.AreEqual(NativeTermination.Exited, native.Termination);
        Assert.AreEqual(1, native.ExitCode);
        Assert.Contains(capability == "MTP" ? "Minimum expected tests policy violation" :
            "No test matches the given testcase filter", native.Stdout);
        XDocument report = XDocument.Load(Path.Combine(fixture.Root, "tests", "receipts",
            "net10.0", "native.trx"));
        XElement counters = report.Descendants().Single(element => element.Name.LocalName ==
            "Counters");
        Assert.AreEqual("0", counters.Attribute("total")!.Value);
        Assert.AreEqual("0", counters.Attribute("passed")!.Value);
        Assert.AreEqual(DotNetChecks.Build, Assert.ContainsSingle(result.Outputs).Key.Check);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task NativeInterruptedTestRetainsBuildResult(bool cancel)
    {
        using var fixture = await DotNetNativeFixture.CreateAsync("MTP", context.CancellationToken);
        Dictionary<string, string> globals = DotNetNativeFixture.Globals();
        globals.Add("FixtureSlow", "true");
        CiPlan plan = DotNetExecutionFixture.CreatePlan(fixture.Root, fixture.Candidate, "MTP",
            globals, DotNetNativeFixture.Project);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        Task<DotNetRunResult> running = DotNetExecution.RunAsync(plan,
            new(fixture.Root, fixture.RunScratch, cancel ? 60 : 10), cancellation.Token);
        string started = Path.Combine(fixture.Root, "tests", "slow-started.txt");
        try
        {
            if (cancel)
            {
                using var startDeadline = CancellationTokenSource.CreateLinkedTokenSource(
                    context.CancellationToken);
                startDeadline.CancelAfter(TimeSpan.FromSeconds(60));
                while (!File.Exists(started) && !running.IsCompleted)
                    await Task.Delay(50, startDeadline.Token);
                await cancellation.CancelAsync();
            }
            DotNetRunResult result = await running;
            context.WriteLine(JsonSerializer.Serialize(result,
                TransferJson.Default.DotNetRunResult));
            Assert.IsTrue(File.Exists(started), Diagnostics(result));
            Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
                item.Key.Check == DotNetChecks.Build).Status);
            Assert.AreEqual(cancel ? CheckStatus.Cancelled : CheckStatus.TimedOut,
                result.Results.Single(item => item.Key.Check == DotNetChecks.Test).Status,
                Diagnostics(result));
            Assert.HasCount(1, result.Commands);
            Assert.IsNotNull(result.BatchBuild);
            Assert.AreEqual(cancel ? NativeTermination.Cancelled : NativeTermination.TimedOut,
                result.Commands[0].Result.Termination);
            Assert.AreEqual(DotNetChecks.Build, Assert.ContainsSingle(result.Outputs).Key.Check);
            Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
        }
        finally
        {
            await cancellation.CancelAsync();
            await running;
        }
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task NativeBatchRetainsIndependentVariantsWithNbgvWhenAnotherEntryFails()
    {
        using var fixture = await DotNetNativeFixture.CreateAsync("MTP",
            context.CancellationToken, nbgv: true);
        await fixture.Repo.SetAsync("broken/Broken.csproj", """
            <Project><Target Name="Build"><Error Text="Intentional independent failure" />
            </Target><Target Name="GetTargetPath" /></Project>
            """);
        string candidate = await fixture.Repo.CommitAsync(fixture.Candidate);
        await fixture.Repo.GitAsync("reset", "--hard", candidate);
        CiPlan first = DotNetExecutionFixture.CreatePlan(fixture.Root, candidate, "MTP",
            DotNetNativeFixture.Globals(), DotNetNativeFixture.Project);
        CiPlan second = DotNetExecutionFixture.CreatePlan(fixture.Root, candidate, "MTP",
            DotNetNativeFixture.Globals("net8.0", false), DotNetNativeFixture.Project);
        CiPlan broken = DotNetExecutionFixture.CreatePlan(fixture.Root, candidate, "None",
            project: "broken/Broken.csproj");
        CiPlan plan = first with { Checks = [.. first.Checks, .. broken.Checks, .. second.Checks] };
        DotNetRunResult result = await DotNetExecution.RunAsync(plan,
            new(fixture.Root, fixture.RunScratch, 60), context.CancellationToken);
        context.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.DotNetRunResult));
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Target == "broken/Broken.csproj").Status, Diagnostics(result));
        Assert.IsTrue(result.Results.Where(item => item.Key.Target == DotNetNativeFixture.Project)
            .All(item => item.Status == CheckStatus.Passed), Diagnostics(result));
        Assert.IsNotNull(result.BatchBuild);
        Assert.HasCount(3, result.BatchBuild.Keys);
        Assert.HasCount(2, result.Commands);
        Assert.HasCount(4, result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(90000, CooperativeCancellation = true)]
    public async Task NativeInterruptedBatchCannotFabricateBuildResult(bool cancel)
    {
        using var repo = await WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture
            .CreateAsync(context.CancellationToken);
        await repo.SetAsync("slow.csproj", """
            <Project>
              <UsingTask TaskName="WaitForInterruption" TaskFactory="RoslynCodeTaskFactory"
                AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll">
                <ParameterGroup><Signal ParameterType="System.String" Required="true" />
                </ParameterGroup><Task><Code Type="Fragment" Language="cs"><![CDATA[
                  System.IO.File.WriteAllText(Signal, "started");
                  System.Threading.Thread.Sleep(30000);
                ]]></Code></Task>
              </UsingTask>
              <Target Name="Build"><WaitForInterruption
                Signal="$(MSBuildProjectDirectory)/started.txt" /></Target>
              <Target Name="GetTargetPath" />
            </Project>
            """);
        string candidate = await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", candidate);
        using var fixture = new DotNetExecutionFixture(repo,
            DotNetExecutionFixture.CreatePlan(repo.Directory, candidate, "None",
                project: "slow.csproj"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        Task<DotNetRunResult> running = DotNetExecution.RunAsync(fixture.Plan,
            fixture.Request with { DeadlineSeconds = cancel ? 60 : 10 }, cancellation.Token);
        string signal = Path.Combine(repo.Directory, "started.txt");
        try
        {
            if (cancel)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                    context.CancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(60));
                while (!File.Exists(signal) && !running.IsCompleted)
                    await Task.Delay(50, deadline.Token);
                await cancellation.CancelAsync();
            }
            DotNetRunResult result = await running;
            Assert.IsTrue(File.Exists(signal), Diagnostics(result));
            Assert.AreEqual(cancel ? CheckStatus.Cancelled : CheckStatus.TimedOut,
                Assert.ContainsSingle(result.Results).Status, Diagnostics(result));
            Assert.IsNotNull(result.BatchBuild);
            Assert.AreEqual(cancel ? NativeTermination.Cancelled : NativeTermination.TimedOut,
                result.BatchBuild.Result.Termination);
            Assert.IsEmpty(result.Outputs);
            Assert.IsEmpty(result.Commands);
        }
        finally
        {
            await cancellation.CancelAsync();
            await running;
        }
    }

    private static void AssertSuccessfulNativeRun(DotNetNativeFixture fixture, CiPlan plan,
        DotNetRunResult result, string framework, string target)
    {
        Assert.IsTrue(ResultCollector.Collect(plan, result.Results).Satisfied, Diagnostics(result));
        Assert.HasCount(1, result.Commands);
        Assert.IsNotNull(result.BatchBuild);
        Assert.Contains("-target:Build,GetTargetPath," + target,
            result.Commands[0].Command.Arguments);
        Assert.HasCount(2, result.Outputs);
        Assert.IsTrue(result.Outputs.All(output => File.Exists(output.Path) &&
            output.Path.Contains("native-bin", StringComparison.Ordinal)));
        string report = Path.Combine(fixture.Root, "tests", "receipts", framework, "native.trx");
        XDocument trx = XDocument.Load(report);
        XElement[] tests = trx.Descendants().Where(element => element.Name.LocalName ==
            "UnitTestResult").ToArray();
        Assert.HasCount(2, tests);
        Assert.AreEqual("Passed", tests.Single(test => test.Attribute("testName")!.Value ==
            "SelectedPasses").Attribute("outcome")!.Value);
        Assert.AreEqual("NotExecuted", tests.Single(test => test.Attribute("testName")!.Value ==
            "SelectedSkipped").Attribute("outcome")!.Value);
        Assert.IsEmpty(result.Failures);
    }

    private static string Diagnostics(DotNetRunResult result) => string.Join("\n",
        result.Commands.Select(command => command.Result.Error + "\n" + command.Result.Stdout +
            command.Result.Stderr).Concat(result.BatchBuild is { } batch
            ? [batch.Result.Error + "\n" + batch.Result.Stdout + batch.Result.Stderr] : [])
            .Concat(result.Failures.Select(failure => failure.Error)));
}
