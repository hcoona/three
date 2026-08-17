using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class FakeProcessRunnerTests
{
    [Fact]
    public async Task RunAsyncRecordsStartSpecAndReturnsQueuedResult()
    {
        var runner = new FakeProcessRunner();
        var expectedResult = new ProcessResult(5, "stdout", "stderr");
        var startSpec = new ProcessStartSpec("tool", ["arg"]);
        runner.EnqueueResult(expectedResult);

        ProcessResult result = await runner.RunAsync(
            startSpec,
            TestContext.Current.CancellationToken
        );

        Assert.Same(expectedResult, result);
        Assert.Same(startSpec, Assert.Single(runner.RecordedStartSpecs));
    }

    [Fact]
    public async Task RunAsyncPropagatesQueuedFailureAfterRecordingCall()
    {
        var runner = new FakeProcessRunner();
        var expectedException = new InvalidOperationException("boom");
        var startSpec = new ProcessStartSpec("tool");
        runner.EnqueueFailure(expectedException);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                runner.RunAsync(startSpec, TestContext.Current.CancellationToken)
        );

        Assert.Same(expectedException, exception);
        Assert.Same(startSpec, Assert.Single(runner.RecordedStartSpecs));
    }
}
