using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class RequestDelayPlannerTests
{
    private static readonly ResolvedAppSettings Settings = new(
        BrowserPath: null,
        BrowserProfileDir: null,
        OutputDir: null,
        ReadingSpeed: 5000,
        MinimumRequestDelaySeconds: 5,
        MaximumRequestDelaySeconds: 12,
        RetryCount: 3,
        CatalogCacheTtlHours: 24,
        DefaultBooks: []);

    [Fact]
    public void CalculateDelayClampsToMinimum()
    {
        TimeSpan delay = RequestDelayPlanner.CalculateDelay(50, Settings, 0);

        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public void CalculateDelayClampsToMaximum()
    {
        TimeSpan delay = RequestDelayPlanner.CalculateDelay(20_000, Settings, 1);

        Assert.Equal(TimeSpan.FromSeconds(12), delay);
    }

    [Fact]
    public void CalculateDelayUsesReadingSpeedWithinBounds()
    {
        TimeSpan delay = RequestDelayPlanner.CalculateDelay(5_000, Settings, 0.5);

        Assert.Equal(TimeSpan.FromSeconds(12), delay);
    }
}
