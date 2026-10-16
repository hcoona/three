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
    // A per-command budget, not a performance assertion: concurrent native graph builds
    // on a 4-core hosted runner exceeded 60 seconds. Each row runs up to three native
    // commands after fixture setup, so these rows use the native-row method timeout.
    private const int NativeCommandDeadlineSeconds = 150;

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public async Task NativeGlobalsRoundTripThroughSelectedSdk()
    {
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
        string directory = Directory.CreateTempSubdirectory("workflow-native-globals-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "global.json"),
                DotNetNativeFixture.SdkSelection, context.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "query.proj"), "<Project />",
                context.CancellationToken);
            string receipt = Path.Combine(directory, "query.json");
            NativeCommandResult query = await NativeProcess.ExecuteAsync(new("dotnet", directory,
                ["msbuild", "query.proj", "-nologo", "-noAutoResponse",
                    "-getProperty:" + string.Join(',', globals.Keys),
                    "-getResultOutputFile:" + receipt, .. DotNetChecks.Properties(globals)], 60),
                context.CancellationToken);
            DotNetNativeFixture.RequireSuccess(query, "Native property query");
            using JsonDocument response = JsonDocument.Parse(await File.ReadAllTextAsync(receipt,
                context.CancellationToken));
            JsonElement properties = response.RootElement.GetProperty("Properties");
            foreach ((string name, string original) in globals)
                Assert.AreEqual(ProjectCollection.Unescape(original),
                    properties.GetProperty(name).GetString(), "Original native global: " + name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativeMtpInnerVariantRetainsSettingsAndOutput()
    {
        using var fixture = await DotNetNativeFixture.CreateAsync("MTP", context.CancellationToken);
        Dictionary<string, string> globals = DotNetNativeFixture.Globals();
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
            new(fixture.Root, fixture.RunScratch, NativeCommandDeadlineSeconds),
            context.CancellationToken);
        context.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.DotNetRunResult));
        AssertSuccessfulNativeRun(fixture, plan, result, "net10.0", "InvokeTestingPlatform");
    }

    [TestMethod]
    [Timeout(600000, CooperativeCancellation = true)]
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
            new(fixture.Root, fixture.RunScratch, NativeCommandDeadlineSeconds),
            context.CancellationToken);
        context.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.DotNetRunResult));
        AssertSuccessfulNativeRun(fixture, plan, result, "net10.0", "VSTest");
    }

    [TestMethod]
    [Timeout(600000, CooperativeCancellation = true)]
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
            new(fixture.Root, fixture.RunScratch, NativeCommandDeadlineSeconds),
            context.CancellationToken);
        context.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.DotNetRunResult));
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Target == "broken/Broken.csproj").Status, Diagnostics(result));
        Assert.IsTrue(result.Results.Where(item => item.Key.Target == DotNetNativeFixture.Project)
            .All(item => item.Status == CheckStatus.Passed), Diagnostics(result));
        Assert.IsNotNull(result.BatchBuild);
        Assert.HasCount(3, result.BatchBuild.Keys);
        Assert.IsNotNull(result.TestBatch);
        Assert.HasCount(2, result.TestBatch.Keys);
        Assert.HasCount(2, result.Tests);
        Assert.IsTrue(result.Tests.All(test => test.Test == "Success"), Diagnostics(result));
        Assert.HasCount(4, result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
    }

    [TestMethod]
    [Timeout(600000, CooperativeCancellation = true)]
    public async Task NativeTestBatchKeepsEachEntryResultWhenAnotherEntryFails()
    {
        using var fixture = await DotNetNativeFixture.CreateAsync("MTP",
            context.CancellationToken, failNet8Test: true);
        CiPlan passing = DotNetExecutionFixture.CreatePlan(fixture.Root, fixture.Candidate, "MTP",
            DotNetNativeFixture.Globals(), DotNetNativeFixture.Project);
        CiPlan failing = DotNetExecutionFixture.CreatePlan(fixture.Root, fixture.Candidate, "MTP",
            DotNetNativeFixture.Globals("net8.0", false), DotNetNativeFixture.Project);
        CiPlan plan = failing with { Checks = [.. failing.Checks, .. passing.Checks] };
        DotNetRunResult result = await DotNetExecution.RunAsync(plan,
            new(fixture.Root, fixture.RunScratch, NativeCommandDeadlineSeconds),
            context.CancellationToken);
        context.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.DotNetRunResult));
        Assert.IsTrue(result.Results.Where(item => item.Key.Check == DotNetChecks.Build)
            .All(item => item.Status == CheckStatus.Passed), Diagnostics(result));
        CheckKey failed = failing.Checks[1].Work.Key;
        CheckKey passed = passing.Checks[1].Work.Key;
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item => item.Key == failed)
            .Status, Diagnostics(result));
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item => item.Key == passed)
            .Status, Diagnostics(result));
        Assert.IsNotNull(result.TestBatch);
        Assert.IsTrue(result.TestBatch.Result.Succeeded, Diagnostics(result));
        Assert.HasCount(2, result.TestBatch.Keys);
        Assert.Contains("Intentional native net8.0 test failure",
            Assert.ContainsSingle(result.Failures).Error);
        Assert.AreEqual(failed, result.Failures[0].Key);
        Assert.DoesNotContain("Intentional native net8.0 test failure",
            result.Tests.Single(test => test.Key == passed).Log);
        Assert.HasCount(3, result.Outputs);
        Assert.IsFalse(ResultCollector.Collect(plan, result.Results).Satisfied);
    }

    private static void AssertSuccessfulNativeRun(DotNetNativeFixture fixture, CiPlan plan,
        DotNetRunResult result, string framework, string target)
    {
        Assert.IsTrue(ResultCollector.Collect(plan, result.Results).Satisfied, Diagnostics(result));
        Assert.IsNotNull(result.BatchBuild);
        Assert.IsNotNull(result.TestBatch);
        Assert.AreEqual("Success", Assert.ContainsSingle(result.Tests).Test);
        Assert.Contains(target, File.ReadAllText(Path.Combine(fixture.RunScratch,
            "test-request.json")));
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
        new[] { result.BatchBuild, result.TestBatch }.OfType<DotNetBatchCommandObservation>()
            .Select(batch => batch.Result.Error + "\n" + batch.Result.Stdout +
                batch.Result.Stderr)
            .Concat(result.Tests.Select(test => test.Log))
            .Concat(result.Failures.Select(failure => failure.Error)));
}
