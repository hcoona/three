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

        var result = await runner.RunAsync(startSpec, TestContext.Current.CancellationToken);

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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(startSpec, TestContext.Current.CancellationToken)
        );

        Assert.Same(expectedException, exception);
        Assert.Same(startSpec, Assert.Single(runner.RecordedStartSpecs));
    }

    [Fact]
    public async Task RunAsyncWithInvalidEnvironmentModeThrowsWithoutRecordingCall()
    {
        var runner = new FakeProcessRunner();
        var startSpec = new ProcessStartSpec("tool", environmentMode: (ProcessEnvironmentMode)42);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunAsync(startSpec, TestContext.Current.CancellationToken)
        );

        Assert.Empty(runner.RecordedStartSpecs);
    }

    [Fact]
    public async Task RunAsyncInvokesPreStartValidationBeforeQueuedHandler()
    {
        var runner = new FakeProcessRunner();
        var calls = new List<string>();
        var startSpec = new ProcessStartSpec(
            "tool",
            preStartValidation: _ =>
            {
                calls.Add("validate");
                return ValueTask.CompletedTask;
            }
        );
        runner.EnqueueHandler(
            (_, _) =>
            {
                calls.Add("run");
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
            }
        );

        await runner.RunAsync(startSpec, TestContext.Current.CancellationToken);

        Assert.Equal(["validate", "run"], calls);
    }

    [Fact]
    public async Task RunAsyncPropagatesPreStartValidationFailureWithoutRunningQueuedHandler()
    {
        var runner = new FakeProcessRunner();
        var expectedException = new UnauthorizedAccessException("integrity check failed");
        var handlerRan = false;
        var startSpec = new ProcessStartSpec(
            "tool",
            preStartValidation: _ => ValueTask.FromException(expectedException)
        );
        runner.EnqueueHandler(
            (_, _) =>
            {
                handlerRan = true;
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
            }
        );

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            runner.RunAsync(startSpec, TestContext.Current.CancellationToken)
        );

        Assert.Same(expectedException, exception);
        Assert.False(handlerRan);
    }

    [Fact]
    public async Task RunAsyncWithCancellationAfterPreStartValidationThrows()
    {
        var runner = new FakeProcessRunner();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var handlerRan = false;
        var startSpec = new ProcessStartSpec(
            "tool",
            preStartValidation: _ =>
            {
                cancellation.Cancel();
                return ValueTask.CompletedTask;
            }
        );
        runner.EnqueueHandler(
            (_, _) =>
            {
                handlerRan = true;
                return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
            }
        );

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(startSpec, cancellation.Token)
        );

        Assert.False(handlerRan);
    }
}
