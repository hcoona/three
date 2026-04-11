using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class ResolvedAppSettingsTests
{
    [Fact]
    public void MergeForDownloadUsesCommandLineValuesOverConfiguration()
    {
        AppSettings configuration = new()
        {
            BrowserPath = "config-browser",
            BrowserProfileDir = "config-profile",
            OutputDir = "config-output",
            ReadingSpeed = 5000,
            MinimumRequestDelaySeconds = 5,
            MaximumRequestDelaySeconds = 12,
            RetryCount = 3,
            CatalogCacheTtlHours = 24,
            DefaultBooks = ["1045928363"],
        };

        ResolvedAppSettings resolved = ResolvedAppSettings.Merge(
            configuration,
            new DownloadCommandOptions
            {
                BrowserPath = "cli-browser",
                BrowserProfileDir = "cli-profile",
                OutputDir = "cli-output",
                ReadingSpeed = 6000,
                MinimumRequestDelaySeconds = 6,
                MaximumRequestDelaySeconds = 15,
                RetryCount = 5,
                CatalogCacheTtlHours = 48,
            });

        Assert.Equal("cli-browser", resolved.BrowserPath);
        Assert.Equal("cli-profile", resolved.BrowserProfileDir);
        Assert.Equal("cli-output", resolved.OutputDir);
        Assert.Equal(6000, resolved.ReadingSpeed);
        Assert.Equal(6, resolved.MinimumRequestDelaySeconds);
        Assert.Equal(15, resolved.MaximumRequestDelaySeconds);
        Assert.Equal(5, resolved.RetryCount);
        Assert.Equal(48, resolved.CatalogCacheTtlHours);
        Assert.Equal(["1045928363"], resolved.DefaultBooks);
    }
}
