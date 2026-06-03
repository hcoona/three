using System.Diagnostics;
using Xunit;

namespace Hcoona.CfDdnsUpdater.Tests;

[Collection(TestCollectionDefinition.Name)]
public sealed class CloudflareTelemetryTests
{
    [Fact]
    public void MarkRunExitCodeMarksErrorForNonZeroExit()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);
        using (Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
                   CloudflareTelemetry.RunActivityName,
                   ActivityKind.Internal))
        {
            CloudflareTelemetry.MarkRunExitCode(activity, 1);
        }

        Activity stoppedActivity = Assert.Single(recorder.StoppedActivities);
        Assert.Equal(ActivityStatusCode.Error, stoppedActivity.Status);
    }

    [Fact]
    public void MarkRunExitCodeLeavesSuccessPathUntouched()
    {
        using ActivityRecorder recorder = ActivityRecorder.Start(CloudflareTelemetry.ActivitySourceName);
        using (Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
                   CloudflareTelemetry.RunActivityName,
                   ActivityKind.Internal))
        {
            CloudflareTelemetry.MarkRunExitCode(activity, 0);
            CloudflareTelemetry.MarkOutcome(activity, "success");
        }

        Activity stoppedActivity = Assert.Single(recorder.StoppedActivities);
        Assert.Equal(ActivityStatusCode.Unset, stoppedActivity.Status);
        Assert.Equal("success", stoppedActivity.GetTagItem(CloudflareTelemetry.OutcomeTagName));
    }
}
