using System.Text;
using System.Text.Json;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

[TestClass]
[DoNotParallelize]
public sealed class ProcessScenarios
{
    [TestMethod]
    public async Task RootHelpCompletesWithoutAuthentication()
    {
        using var child = new ProcessFixture("help", Program.NativeCliExecutable);
        await child.FinishAsync();
        AssertNormalExit(child, 0);
        var text = Encoding.UTF8.GetString(child.Output);
        StringAssert.StartsWith(text, "AzureAuth Unofficial V2 (hcoona/microsoft-authentication-cli)\n"
            + "Unofficial project: not a Microsoft product and not supported by Microsoft.\n"
            + "Version " + ProductVersion.Full + "\n");
        StringAssert.Contains(text, "authenticate");
        StringAssert.Contains(text, "--profile");
        StringAssert.Contains(text, "azureauth-unofficial --version");
        Assert.IsFalse(text.Contains("SYNTHETIC_INVALID_VERSION", StringComparison.Ordinal));
        Assert.IsFalse(child.Output.Contains((byte)'\r'), "Help output must use LF line endings only.");
        Assert.IsTrue(text.EndsWith("stdout.\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RootVersionCompletesWithoutAuthentication()
    {
        using var child = new ProcessFixture("version", Program.NativeCliExecutable);
        await child.FinishAsync();
        AssertNormalExit(child, 0);
        Assert.AreEqual("azureauth-unofficial " + ProductVersion.Full + "\n", Encoding.UTF8.GetString(child.Output));
        Assert.AreEqual(0, child.Error.Length);
    }

    [TestMethod]
    public void HelpBytesUseLineFeedsOnly()
    {
        var help = WindowsProcess.Help;
        Assert.IsFalse(help.Contains((byte)'\r'), "Help must not depend on the checkout line endings.");
        Assert.AreEqual((byte)'\n', help[^1]);
    }

    [TestMethod]
    public async Task MalformedAuthenticationReturnsTheBootstrapFailure()
    {
        using var child = new ProcessFixture("malformed", Program.NativeCliExecutable);
        await child.FinishAsync();
        AssertFailure(child, "invalid_request");
        Assert.IsFalse(Encoding.UTF8.GetString(child.Output).Contains("SYNTHETIC_INVALID_VERSION", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SelectedRequestReturnsOneSuccessDespiteBrokenDiagnostics()
    {
        using var child = new ProcessFixture("success");
        await child.FinishAsync();
        AssertNormalExit(child, 0);
        AssertSuccessBytes(child);
        Assert.IsTrue(child.Marked("candidate-returned"));
        Assert.AreEqual(0, child.Error.Length);
    }

    [TestMethod]
    public async Task FlaggedRegularFileStopsBeforeProfileAndProvider()
    {
        using var child = new ProcessFixture("file-stdin");
        await child.FinishAsync();
        AssertFailure(child, "invalid_request");
        AssertNoAdmissionEffects(child);
    }

    [TestMethod]
    public async Task AlreadyClosedLifetimePipeCancelsBeforeAuthentication()
    {
        using var child = new ProcessFixture("closed-stdin");
        await child.FinishAsync();
        AssertFailure(child, "cancelled");
        AssertNoAdmissionEffects(child);
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task WriterClosureRejectsLateSuccessAndEndsTheProcess()
    {
        using var child = new ProcessFixture("close-pending");
        await RequireMarkerAsync(child, "provider-ready");
        child.CloseInput();
        await child.FinishAsync();
        AssertFailure(child, "cancelled");
        Assert.IsTrue(child.Marked("cancel-observed"));
        Assert.IsTrue(child.Marked("candidate-returned"));
        AssertExitInterval(child, child.WriterCloseBefore, 1);
    }

    [TestMethod]
    public async Task ClosedStdinWithoutTheFlagDoesNotCancel()
    {
        using var child = new ProcessFixture("unused-stdin");
        await child.FinishAsync();
        AssertNormalExit(child, 0);
        AssertSuccessBytes(child);
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task LifetimePipePayloadIsIgnoredAndClosureStillCancels()
    {
        using var child = new ProcessFixture("data-close");
        await RequireMarkerAsync(child, "provider-ready");
        child.CloseInput(payload: true);
        await child.FinishAsync();
        AssertFailure(child, "cancelled");
        Assert.IsTrue(child.Marked("cancel-observed"));
        AssertExitInterval(child, child.WriterCloseBefore, 1);
        Assert.IsFalse(Encoding.UTF8.GetString(child.Output.Concat(child.Error).ToArray())
            .Contains("SYNTHETIC_IGNORED_STDIN_DATA", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task DeadlineEndsUncooperativeWorkWithinTheProcessBound()
    {
        using var child = new ProcessFixture("deadline");
        await RequireMarkerAsync(child, "provider-ready");
        await child.FinishAsync();
        Assert.IsFalse(child.Forced, "The product must end before fixture enforcement.");
        AssertExitInterval(child, child.EntryTimestamp, 2);
        Assert.IsTrue(child.Marked("callback-pending"));
        Assert.IsFalse(child.Marked("candidate-returned"));
        // Undrained work forces termination. A typed timeout written before then
        // keeps its exit status; without a complete result the exit is a transport failure.
        if (child.Output.Length == 0)
        {
            Assert.AreEqual(2u, child.ExitCode, "Forced shutdown without a result is a transport failure.");
            return;
        }
        using var json = ParseOne(child.Output);
        Assert.AreEqual("timeout", json.RootElement.GetProperty("outcome").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("accessToken", out _));
        Assert.AreEqual(1u, child.ExitCode, "A written typed timeout fixes the exit status.");
    }

    [TestMethod]
    public async Task BrokenResultReaderEndsWithTransportFailure()
    {
        using var child = new ProcessFixture("broken-output");
        await child.FinishAsync();
        Assert.IsFalse(child.Forced);
        Assert.IsTrue(child.Marked("candidate-returned"));
        Assert.AreEqual(0, child.Output.Length);
        Assert.AreEqual(2u, child.ExitCode);
        // Telemetry is on: an undelivered result has no completion indication,
        // token-shape observation or request-span stop event.
        var error = Encoding.UTF8.GetString(child.Error);
        Assert.IsFalse(error.Contains("tokenFormat", StringComparison.Ordinal), "A token-shape observation followed failed delivery.");
        Assert.IsFalse(error.Contains("request_completed", StringComparison.Ordinal));
        Assert.IsFalse(error.Contains("Authentication request completed.", StringComparison.Ordinal));
        Assert.IsFalse(error.Split('\n').Any(line => line.Contains("\"stage_completed\"", StringComparison.Ordinal)
            && line.Contains("\"stage\":\"request\"", StringComparison.Ordinal)), "A request-span event followed failed delivery.");
        Assert.IsFalse(error.Contains(ProcessChild.Token, StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("diagnostic-stall", 0u)]
    [DataRow("diagnostic-stall-denied", 1u)]
    public async Task PendingDiagnosticsCannotReplaceTheDrainedTypedExit(string scenario, uint expected)
    {
        // The diagnostic drain never completes, so the process bound always ends the
        // process; this asserts only which exit status it reports, not when.
        using var child = new ProcessFixture(scenario);
        await child.FinishAsync();
        Assert.IsTrue(child.Marked("diagnostic-stall"), "The never-completing diagnostic drain was not reached.");
        AssertNormalExit(child, expected);
        if (expected == 0) AssertSuccessBytes(child);
        else
        {
            using var json = ParseOne(child.Output);
            Assert.AreEqual("denied", json.RootElement.GetProperty("outcome").GetString());
        }
    }

    [TestMethod]
    [DataRow("drain-stall", 0u)]
    [DataRow("drain-stall-denied", 1u)]
    public async Task StalledRequiredDrainCannotReplaceTheWrittenTypedExit(string scenario, uint expected)
    {
        // A provider cancellation callback never returns, so required drain never
        // completes and the process bound always ends the process after the result is
        // written; this asserts only which exit status it reports, not when.
        using var child = new ProcessFixture(scenario);
        await child.FinishAsync();
        Assert.IsTrue(child.Marked("callback-pending"), "The never-completing cancellation callback was not reached.");
        AssertNormalExit(child, expected);
        if (expected == 0) AssertSuccessBytes(child);
        else
        {
            using var json = ParseOne(child.Output);
            Assert.AreEqual("denied", json.RootElement.GetProperty("outcome").GetString());
        }
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task UndrainedResultPipeCannotKeepTheProcessAlive()
    {
        using var child = new ProcessFixture("blocked-output");
        await RequireMarkerAsync(child, "candidate-returned");
        Assert.IsTrue(await child.ObserveBufferedOutputAsync(), "Observe actual result bytes before claiming a blocked write.");
        await child.FinishAsync();
        Assert.IsFalse(child.Forced, "The product must end before fixture enforcement.");
        Assert.AreEqual(2u, child.ExitCode);
        AssertExitInterval(child, child.EntryTimestamp, 2);
        Assert.IsTrue(child.Output.Length < 262144);
        Assert.IsFalse(child.Output.AsSpan().EndsWith("}\n"u8));
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task BlockedDiagnosticsDoNotChangeTheAuthenticationResultOrKeepTheProcessAlive()
    {
        using var child = new ProcessFixture("blocked-diagnostics");
        await child.FinishAsync();
        Assert.IsFalse(child.Forced);
        AssertExitInterval(child, child.EntryTimestamp, 2);
        AssertSuccessBytes(child);
        Assert.AreEqual(0u, child.ExitCode, "Diagnostic loss must preserve successful process status.");
        Assert.IsTrue(child.DiagnosticPrefill > 0);
        Assert.IsTrue(child.Error.Length <= child.DiagnosticPrefill + 8192);
    }

    private static async Task RequireMarkerAsync(ProcessFixture child, string name)
    {
        var observed = await child.WaitForMarkerAsync(name);
        Assert.IsTrue(child.Marked("entered"), "The controlled child must enter before a business red is accepted.");
        Assert.IsTrue(observed, "The controlled request must reach its required scenario state.");
    }

    private static void AssertExitInterval(ProcessFixture child, long start, int seconds)
    {
        // Same-host Windows QPC underlies TimeProvider.System in both processes.
        // The exit observation is taken when the process handle is signaled; the
        // fixed 100 ms tolerance covers only scheduling of that waiting thread and
        // does not enlarge the product's allowance.
        Assert.IsTrue(start > 0 && child.ExitObservedTimestamp >= start);
        var interval = TimeProvider.System.GetElapsedTime(start, child.ExitObservedTimestamp);
        Assert.IsTrue(interval <= TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(100),
            "The observed product interval exceeded its admitted measurement tolerance.");
    }

    private static void AssertNoAdmissionEffects(ProcessFixture child)
    {
        Assert.IsTrue(child.Marked("entered"));
        Assert.IsFalse(child.Marked("profile-read"));
        Assert.IsFalse(child.Marked("provider-created"));
    }

    private static void AssertNormalExit(ProcessFixture child, uint expected)
    {
        Assert.IsFalse(child.Forced);
        Assert.AreEqual(expected, child.ExitCode);
    }

    private static void AssertFailure(ProcessFixture child, string outcome)
    {
        AssertNormalExit(child, 1);
        using var json = ParseOne(child.Output);
        Assert.AreEqual(outcome, json.RootElement.GetProperty("outcome").GetString());
        Assert.AreEqual(outcome, json.RootElement.GetProperty("reason").GetString());
        CollectionAssert.AreEquivalent(new[] { "protocol", "outcome", "reason" },
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static void AssertSuccessBytes(ProcessFixture child)
    {
        using var json = ParseOne(child.Output);
        var result = json.RootElement;
        Assert.AreEqual("success", result.GetProperty("outcome").GetString());
        Assert.AreEqual(ProcessChild.Email, result.GetProperty("accountEmail").GetString());
        Assert.AreEqual(ProcessChild.Token, result.GetProperty("accessToken").GetString());
        Assert.AreEqual(ProcessChild.Tenant, result.GetProperty("tenantId").GetString());
        Assert.AreEqual("Bearer", result.GetProperty("tokenType").GetString());
        Assert.AreEqual("silent", result.GetProperty("interaction").GetString());
        CollectionAssert.AreEqual(new[] { ProcessChild.Scope },
            result.GetProperty("scopes").EnumerateArray().Select(value => value.GetString()).ToArray());
        CollectionAssert.AreEquivalent(new[] { "protocol", "outcome", "accessToken", "tokenType", "expiresOn",
            "accountEmail", "tenantId", "authority", "scopes", "mechanism", "interaction", "warnings" },
            result.EnumerateObject().Select(property => property.Name).ToArray());
    }

    private static JsonDocument ParseOne(byte[] bytes)
    {
        Assert.IsTrue(bytes.Length > 1 && bytes[^1] == (byte)'\n');
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.AreEqual(1, bytes.Count(value => value == (byte)'\n'));
        var json = JsonDocument.Parse(bytes);
        Assert.AreEqual(1, json.RootElement.GetProperty("protocol").GetInt32());
        return json;
    }
}
