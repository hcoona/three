using System.Runtime.ExceptionServices;

namespace WorkflowDelivery.Tests;

internal static class FixtureCleanup
{
    internal static async Task RunAsync(Func<Task> body, Action dispose)
    {
        ExceptionDispatchInfo? bodyFailure = null;
        try { await body(); }
        catch (Exception exception) { bodyFailure = ExceptionDispatchInfo.Capture(exception); }

        ExceptionDispatchInfo? cleanupFailure = null;
        try { dispose(); }
        catch (Exception exception) { cleanupFailure = ExceptionDispatchInfo.Capture(exception); }

        if (bodyFailure is not null && cleanupFailure is not null)
            throw new AggregateException(
                bodyFailure.SourceException, cleanupFailure.SourceException
            );
        bodyFailure?.Throw();
        cleanupFailure?.Throw();
    }
}
