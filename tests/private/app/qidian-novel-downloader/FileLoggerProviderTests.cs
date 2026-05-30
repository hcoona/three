using Hcoona.QidianNovelDownloader.Logging;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Hcoona.QidianNovelDownloader.Tests;

public sealed class FileLoggerProviderTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void LogErrorDoesNotWriteThroughReparseLogsDirectory()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string outsideRoot = Path.Combine(temporaryDirectory.FullPath, "outside");
        string logsRoot = Path.Combine(temporaryDirectory.FullPath, "logs");
        Directory.CreateDirectory(outsideRoot);
        if (!CanCreateDirectorySymbolicLink(temporaryDirectory.FullPath))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        try
        {
            Directory.CreateSymbolicLink(logsRoot, outsideRoot);

            using FileLoggerProvider provider = new(TimeProvider.System, logsRoot);
            ILogger logger = provider.CreateLogger("test");
            logger.Log(
                LogLevel.Error,
                new EventId(),
                "expected failure",
                exception: null,
                static (state, _) => state);
        }
        finally
        {
            DeleteReparseDirectoryIfExists(logsRoot);
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(outsideRoot));
    }

    [Fact]
    public void LogErrorDoesNotWriteThroughDanglingReparseLogFile()
    {
        using TemporaryDirectory temporaryDirectory = new();
        string logsRoot = Path.Combine(temporaryDirectory.FullPath, "logs");
        Directory.CreateDirectory(logsRoot);
        if (!CanCreateFileSymbolicLink(temporaryDirectory.FullPath))
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }

        string danglingTarget = Path.Combine(temporaryDirectory.FullPath, "missing-target.log");
        string logPath = Path.Combine(logsRoot, "qidian-novel-downloader-20240102.log");
        try
        {
            File.CreateSymbolicLink(logPath, danglingTarget);

            using FileLoggerProvider provider = new(new FixedTimeProvider(), logsRoot);
            ILogger logger = provider.CreateLogger("test");
            logger.Log(
                LogLevel.Error,
                new EventId(),
                "expected failure",
                exception: null,
                static (state, _) => state);
        }
        finally
        {
            DeleteReparseFileIfExists(logPath);
        }

        Assert.False(File.Exists(danglingTarget));
    }

    private static bool CanCreateDirectorySymbolicLink(string root)
    {
        string target = Path.Combine(root, "symlink-target");
        string link = Path.Combine(root, "symlink-link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Directory.Delete(link);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }
    }

    private static bool CanCreateFileSymbolicLink(string root)
    {
        string target = Path.Combine(root, "file-symlink-target");
        string link = Path.Combine(root, "file-symlink-link");
        File.WriteAllText(target, "target");
        try
        {
            File.CreateSymbolicLink(link, target);
            File.Delete(link);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "Symbolic link creation is unavailable; reparse-point coverage skipped.");
        }
    }

    private static void DeleteReparseDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path);
        }
    }

    private static void DeleteReparseFileIfExists(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                File.Delete(path);
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string FullPath { get; } = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(FullPath))
            {
                Directory.Delete(FullPath, recursive: true);
            }
        }
    }
}
