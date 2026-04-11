namespace Hcoona.QidianNovelDownloader;

internal static class RequestDelayPlanner
{
    public static TimeSpan CalculateDelay(int? chapterWordCount, ResolvedAppSettings settings)
        => CalculateDelay(chapterWordCount, settings, Random.Shared.NextDouble());

    internal static TimeSpan CalculateDelay(
        int? chapterWordCount,
        ResolvedAppSettings settings,
        double jitterUnit)
    {
        double boundedJitterUnit = Math.Clamp(jitterUnit, 0, 1);
        double baseDelaySeconds = chapterWordCount is > 0
            ? (double)chapterWordCount.Value / settings.ReadingSpeed * 60
            : settings.MinimumRequestDelaySeconds;
        double jitterFactor = 0.85 + (0.3 * boundedJitterUnit);
        double delayedSeconds = baseDelaySeconds * jitterFactor;
        double clampedSeconds = Math.Clamp(
            delayedSeconds,
            settings.MinimumRequestDelaySeconds,
            settings.MaximumRequestDelaySeconds);
        return TimeSpan.FromSeconds(clampedSeconds);
    }
}
