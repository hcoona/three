using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Development validation (docs/research/experiment-safety.md). These scenarios exercise
// the production process with synthetic providers and real owned windows; no account,
// broker, token-cache or external UI operation occurs.
[TestClass]
[DoNotParallelize]
public sealed class OwnedProcessScenarios
{
    [TestMethod]
    public async Task NormalOwnedClosurePreservesSuccessAndDrains()
    {
        using var child = new ProcessFixture("host-success");
        await child.FinishAsync();
        RequireMarkers(child, "provider-ready", "candidate-returned", "host-closing",
            "native-cleanup-completed", "host-drained");
        AssertExit(child, 0);
        AssertSuccess(child);
        Assert.IsFalse(child.Marked("cancel-pending"));
        Assert.IsFalse(child.Marked("fault-pending"));
    }

    [TestMethod]
    public async Task LocalCancellationSuppressesSuccessBeforeDelayedNotification()
    {
        using var child = new ProcessFixture("host-cancel");
        await child.FinishAsync();
        RequireMarkers(child, "provider-ready", "host-closing", "cancel-pending", "candidate-returned",
            "after-commit", "cancel-drained");
        AssertOrdered(child, "cancel-pending", "candidate-returned", "after-commit", "cancel-forwarded");
        AssertFailure(child, "cancelled");
        AssertExit(child, 1);
    }

    [TestMethod]
    public async Task LocalHostFaultSuppressesSuccessBeforeDelayedNotification()
    {
        using var child = new ProcessFixture("host-fault");
        await child.FinishAsync();
        RequireMarkers(child, "provider-ready", "native-fault-injected", "fault-pending",
            "candidate-returned", "fault-drained");
        AssertOrdered(child, "fault-pending", "candidate-returned", "after-commit", "fault-drained");
        AssertFailure(child, "internal_failure");
        AssertExit(child, 1);
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task CancellationDuringCreationSurvivesClosureFailure()
    {
        using var child = new ProcessFixture("host-create-cancel");
        await child.FinishAsync();
        RequireMarkers(child, "hidden-parent-created", "local-close-sent", "host-closing",
            "local-close-returned", "native-cleanup-completed");
        Assert.IsFalse(child.Marked("provider-ready"));
        AssertFailure(child, "cancelled");
        // Failed owned cleanup is never marked drained, so the watchdog ends the
        // process; the complete result already written still fixes the exit status.
        AssertExit(child, 1, watchdog: true);
        AssertExitBound(child, "host-closing");
    }

    [TestMethod]
    public async Task OrdinaryCreationFailureRemainsMechanismUnavailable()
    {
        using var child = new ProcessFixture("host-create-failure");
        await child.FinishAsync();
        RequireMarkers(child, "hidden-parent-created", "native-cleanup-completed", "host-drained");
        Assert.IsFalse(child.Marked("provider-ready"));
        Assert.IsFalse(child.Marked("cancel-pending"));
        AssertFailure(child, "mechanism_unavailable");
        AssertExit(child, 1);
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task CleanupFaultAfterNormalClosureSuppressesUncommittedSuccess()
    {
        using var child = new ProcessFixture("host-fault-before-commit");
        await child.FinishAsync();
        RequireMarkers(child, "candidate-returned", "host-closing", "native-cleanup-completed",
            "cleanup-fault-injected", "fault-pending", "fault-drained");
        AssertOrdered(child, "host-closing", "fault-pending", "before-commit", "after-commit");
        AssertFailure(child, "internal_failure");
        AssertExit(child, 1, watchdog: true);
        AssertExitBound(child, "host-closing");
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task CleanupFaultAfterCommitCannotReplaceTheResult()
    {
        using var child = new ProcessFixture("host-fault-after-commit");
        await child.FinishAsync();
        RequireMarkers(child, "candidate-returned", "after-commit", "cleanup-fault-injected", "fault-drained");
        AssertOrdered(child, "after-commit", "cleanup-fault-injected", "fault-drained");
        AssertSuccess(child);
        AssertExit(child, 0, watchdog: true);
        AssertExitBound(child, "host-closing");
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task ProcessWaitsForTheActualOwnedThreadExit()
    {
        using var child = new ProcessFixture("host-ui-join");
        await child.FinishAsync();
        RequireMarkers(child, "native-cleanup-completed", "after-commit");
        AssertSuccess(child);
        AssertExit(child, 0, watchdog: true);
        Assert.IsFalse(child.Marked("host-drained"));
        AssertExitBound(child, "host-closing");
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task ProcessWaitsForTheOutgoingOwnedCallback()
    {
        using var child = new ProcessFixture("host-callback-drain");
        await child.FinishAsync();
        RequireMarkers(child, "cancel-pending", "native-cleanup-completed", "ui-thread-exited",
            "after-commit", "cancel-forwarded");
        AssertOrdered(child, "native-cleanup-completed", "ui-thread-exited", "before-commit");
        AssertOrdered(child, "cancel-pending", "candidate-returned", "after-commit", "cancel-forwarded");
        AssertFailure(child, "cancelled");
        AssertExit(child, 1, watchdog: true);
        Assert.IsFalse(child.Marked("cancel-drained"));
        AssertExitBound(child, "host-closing");
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task NormalClosureArmsTheBoundBeforeCoreTerminalSelection()
    {
        using var child = new ProcessFixture("host-close-stall");
        await child.FinishAsync();
        RequireMarkers(child, "provider-ready", "candidate-returned", "host-closing");
        // No result was committed or written, so the watchdog reports transport failure.
        AssertExit(child, 2);
        AssertExitBound(child, "host-closing");
        Assert.AreEqual(0, child.Output.Length);
        Assert.IsFalse(child.Marked("before-commit"));
    }

    private const string Notice = "Unofficial hcoona/microsoft-authentication-cli. Profile: synthetic-process-profile. "
        + "Registration owner: Synthetic external owner. Sign-in continues in the provider display.";

    [TestMethod]
    public async Task DefaultHostQueuesTheNoticeBeforeTheCompletionIndication()
    {
        using var child = new ProcessFixture("host-notice");
        await child.FinishAsync();
        RequireMarkers(child, "provider-ready", "candidate-returned");
        Assert.IsFalse(child.Forced);
        Assert.AreEqual(0u, child.ExitCode);
        AssertSuccess(child);
        var lines = Encoding.UTF8.GetString(child.Error).Split('\n');
        Assert.AreEqual(1, lines.Count(line => line == Notice));
        var completed = Array.IndexOf(lines, "Authentication request completed.");
        Assert.IsTrue(completed > Array.IndexOf(lines, Notice), "The notice must precede the completion indication.");
        // Telemetry on: the completion event carries the token-shape category but
        // never token material or an accessToken member.
        var events = lines.Where(line => line.StartsWith('{')).ToArray();
        // The process waits for the local writer before finishing, so the request
        // span's stop event and the completion event are both present, not raced.
        Assert.AreEqual(1, events.Count(line => line.Contains("\"stage_completed\"", StringComparison.Ordinal)
            && line.Contains("\"stage\":\"request\"", StringComparison.Ordinal)));
        using var completion = JsonDocument.Parse(events.Single(line => line.Contains("\"request_completed\"", StringComparison.Ordinal)));
        Assert.AreEqual("success", completion.RootElement.GetProperty("outcome").GetString());
        Assert.IsTrue(completion.RootElement.TryGetProperty("tokenFormat", out var format));
        Assert.IsFalse(string.IsNullOrEmpty(format.GetString()));
        Assert.IsFalse(completion.RootElement.TryGetProperty("accessToken", out _));
        var error = Encoding.UTF8.GetString(child.Error);
        Assert.IsFalse(error.Contains(ProcessChild.Token, StringComparison.Ordinal), "Token material reached stderr.");
        Assert.IsFalse(error.Contains("accessToken", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DefaultHostWritesTheExactNoticeWithoutTelemetry()
    {
        using var child = new ProcessFixture("host-notice-raw");
        await child.FinishAsync();
        RequireMarkers(child, "provider-ready", "candidate-returned");
        Assert.IsFalse(child.Forced);
        Assert.AreEqual(0u, child.ExitCode);
        AssertSuccess(child);
        // Separate background writers have no guaranteed mutual order.
        var lines = Encoding.UTF8.GetString(child.Error).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(1, lines.Count(line => line == Notice));
        Assert.IsTrue(lines.All(line => line == Notice || line == "Authentication request completed."));
        // Telemetry off: no completion event and no token-shape observation.
        var error = Encoding.UTF8.GetString(child.Error);
        Assert.IsFalse(error.Contains("request_completed", StringComparison.Ordinal));
        Assert.IsFalse(error.Contains("tokenFormat", StringComparison.Ordinal));
        Assert.IsFalse(error.Contains(ProcessChild.Token, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task BlockedStderrCannotStallTheNoticeOrTheResult()
    {
        using var child = new ProcessFixture("host-notice-blocked");
        await child.FinishAsync();
        RequireMarkers(child, "provider-ready", "candidate-returned");
        Assert.IsFalse(child.Forced, "A blocked notice writer must not keep the process alive.");
        Assert.AreEqual(0u, child.ExitCode);
        AssertSuccess(child);
        Assert.IsTrue(child.DiagnosticPrefill > 0);
    }

    internal static void RequireMarkers(ProcessFixture child, params string[] names)
    {
        Assert.IsTrue(child.Marked("entered"), "Actual controlled entry is required.");
        foreach (var name in names)
            Assert.IsTrue(child.Marked(name), "The required actual state was not observed: " + name);
    }

    internal static void AssertOrdered(ProcessFixture child, params string[] names)
    {
        for (var index = 1; index < names.Length; index++)
            Assert.IsTrue(child.MarkerTimestamp(names[index - 1]) <= child.MarkerTimestamp(names[index]),
                "The required actual event ordering was not established.");
    }

    // A watchdog exit ends the process before the diagnostic drain. With a written
    // result it keeps that result's typed status, but the indication may be cut short.
    internal static void AssertExit(ProcessFixture child, uint code, bool watchdog = false)
    {
        Assert.IsFalse(child.Forced, "The product must end before fixture enforcement.");
        Assert.AreEqual(code, child.ExitCode);
        ReadOnlySpan<byte> indication = [];
        if (child.Output.Length != 0)
        {
            Assert.AreNotEqual(2u, code, "A complete written result must fix a typed exit status.");
            using var result = ParseOne(child.Output);
            var outcome = result.RootElement.GetProperty("outcome").GetString();
            Assert.AreEqual(outcome == "success" ? 0u : 1u, code, "The exit status must follow the written result.");
            indication = outcome switch
            {
                "cancelled" => "Authentication request cancelled.\n"u8,
                // Synthetic hosts record no fixed mechanism reason.
                "mechanism_unavailable" => "Authentication request completed.\nMechanism unavailable at unavailable.\n"u8,
                _ => "Authentication request completed.\n"u8,
            };
        }
        // A drained process waits for its fixed indication within the shutdown bound,
        // so a normally drained exit carries it whole. Watchdog exits may cut it short.
        if (!watchdog && code is 0 or 1) Assert.IsTrue(indication.SequenceEqual(child.Error), "Incomplete diagnostic bytes.");
        else Assert.IsTrue(indication.StartsWith(child.Error), "Unexpected diagnostic bytes.");
    }

    internal static void AssertExitBound(ProcessFixture child, string firstEnding)
    {
        var start = child.MarkerTimestamp(firstEnding);
        Assert.IsTrue(start > 0 && child.ExitObservedTimestamp >= start);
        // Preserve the accepted QPC clock basis and 100 ms observation tolerance;
        // the product still has one second, independently of the fixture's waits.
        Assert.IsTrue(TimeProvider.System.GetElapsedTime(start, child.ExitObservedTimestamp)
            <= TimeSpan.FromMilliseconds(1100), "Owned shutdown exceeded the measured local bound.");
    }

    internal static void AssertFailure(ProcessFixture child, string outcome)
    {
        using var json = ParseOne(child.Output);
        Assert.AreEqual(outcome, json.RootElement.GetProperty("outcome").GetString());
        Assert.AreEqual(outcome, json.RootElement.GetProperty("reason").GetString());
        CollectionAssert.AreEquivalent(new[] { "protocol", "outcome", "reason" },
            json.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    internal static void AssertSuccess(ProcessFixture child)
    {
        using var json = ParseOne(child.Output);
        var result = json.RootElement;
        Assert.AreEqual("success", result.GetProperty("outcome").GetString());
        Assert.AreEqual(ProcessChild.Token, result.GetProperty("accessToken").GetString());
        Assert.AreEqual(ProcessChild.Email, result.GetProperty("accountEmail").GetString());
        Assert.AreEqual(ProcessChild.Tenant, result.GetProperty("tenantId").GetString());
        Assert.AreEqual("interactive", result.GetProperty("interaction").GetString());
    }

    private static JsonDocument ParseOne(byte[] bytes)
    {
        Assert.IsTrue(bytes.Length > 1 && bytes[^1] == (byte)'\n');
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.AreEqual(1, bytes.Count(value => value == (byte)'\n'));
        var json = JsonDocument.Parse(bytes);
        Assert.AreEqual(1, json.RootElement.GetProperty("protocol").GetInt32());
        Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains("Synthetic closure failure", StringComparison.Ordinal));
        return json;
    }
}
