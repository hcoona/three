using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Authentication.Windows.Scenarios.OwnedProcessScenarios;

namespace Authentication.Windows.Scenarios;

[TestClass]
[DoNotParallelize]
public sealed class DefaultHttpCompositionScenarios
{
    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task SharedDefaultHttpOwnershipSurvivesCancellationUntilDrain()
    {
        using var child = new ProcessFixture("default-http-cancel-drain");
        try
        {
            Assert.IsTrue(await child.WaitForMarkerAsync("host-callback-pending"),
                "The actual owned cancellation callback must be waiting for HTTP notification.");
            child.CloseInput();
            Assert.IsTrue(await child.WaitForMarkerAsync("pending-drain-observed"),
                "The actual send, provider, host and callbacks must remain pending together.");
        }
        finally
        {
            // A failed prerequisite still releases the one held gate, then the
            // existing fixture performs bounded process/capture finalization.
            if (!child.Marked("release-drain"))
            {
                using var release = new FileStream(Path.Combine(child.DirectoryPath, "release-drain"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
        }
        await child.FinishAsync();
        RequireCommonDrain(child);
        RequireMarkers(child, "host-callback-pending", "http-cancel-observed", "provider-callback-pending",
            "host-callback-forwarded", "pending-drain-observed", "drain-release-observed", "process-returned");
        AssertOrdered(child, "http-send-entered", "host-callback-pending", "http-cancel-observed",
            "provider-callback-pending", "pending-drain-observed", "drain-release-observed", "drain-boundary");
        AssertOrdered(child, "http-cancel-observed", "host-callback-forwarded", "pending-drain-observed");
        Assert.IsTrue(child.MarkerTimestamp("host-callback-pending") <= child.WriterCloseBefore
            && child.MarkerTimestamp("host-closing") <= child.WriterCloseBefore
            && child.WriterCloseBefore <= child.WriterCloseAfter
            && child.WriterCloseBefore <= child.MarkerTimestamp("http-cancel-observed"));
        AssertExit(child, 1);
        AssertFailure(child, "cancelled");
        AssertExitBound(child, "host-closing");
        Assert.IsFalse(child.Marked("candidate-returned"));
        Assert.IsFalse(child.Marked("dispose-stall-entered"));

        Assert.IsTrue(child.Marked("http-dispose-entered"),
            "Shared process ownership must dispose HTTP after drain before completion.");
        RequireMarkers(child, "http-dispose-completed");
        AssertOrdered(child, "drain-boundary", "http-dispose-entered", "http-dispose-completed", "process-returned");
    }

    [TestMethod]
    [TestCategory(WindowsHostTiming.Category)]
    public async Task SharedDefaultHttpDisposalStallRetainsTheProcessWatchdog()
    {
        using var child = new ProcessFixture("default-http-dispose-stall");
        await child.FinishAsync();
        RequireCommonDrain(child);
        RequireMarkers(child, "candidate-returned");
        AssertOrdered(child, "http-send-entered", "candidate-returned", "before-commit", "after-commit", "drain-boundary");
        AssertSuccess(child);
        // The watchdog ends this process, so the indication may be cut short.
        Assert.IsTrue("Authentication request completed.\n"u8.StartsWith(child.Error), "Unexpected diagnostic bytes.");
        foreach (var name in new[] { "host-callback-pending", "http-cancel-observed", "provider-callback-pending",
            "host-callback-forwarded", "pending-drain-observed", "drain-release-observed", "release-drain" })
            Assert.IsFalse(child.Marked(name));

        Assert.IsTrue(child.Marked("http-dispose-entered"),
            "Shared process ownership must enter HTTP disposal before completion.");
        RequireMarkers(child, "dispose-stall-entered");
        AssertOrdered(child, "drain-boundary", "http-dispose-entered", "dispose-stall-entered");
        // The stalled disposal follows a complete success, which keeps exit status 0.
        AssertExit(child, 0, watchdog: true);
        AssertExitBound(child, "host-closing");
        Assert.IsFalse(child.Marked("http-dispose-completed"));
        Assert.IsFalse(child.Marked("process-returned"));
    }

    private static void RequireCommonDrain(ProcessFixture child)
    {
        Assert.IsFalse(child.Forced, "The product must finish before fixture enforcement.");
        RequireMarkers(child, "host-created", "http-owner-created", "loader-entered", "session-created",
            "ui-thread-started", "hidden-parent-created", "provider-ready", "http-send-entered",
            "host-closing", "native-cleanup-completed", "before-commit", "after-commit", "drain-boundary");
        AssertOrdered(child, "entered", "host-created", "http-owner-created", "loader-entered",
            "session-created", "ui-thread-started", "hidden-parent-created", "provider-ready", "http-send-entered");
        AssertOrdered(child, "host-closing", "native-cleanup-completed", "drain-boundary");
        AssertOrdered(child, "before-commit", "after-commit", "drain-boundary");
        Assert.IsFalse(child.Marked("premature-http-disposal"));
        Assert.IsFalse(child.Marked("cancellation-order-invalid"));
    }
}
