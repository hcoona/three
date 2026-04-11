using System.Text;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Hcoona.QidianNovelDownloader.Logging;

internal sealed class FileLoggerProvider(TimeProvider timeProvider) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers =
        new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly string _logDirectory = Path.Combine(
        AppPaths.GetDefaultStateRoot(),
        AppConstants.LogsDirectoryName);
    private DateOnly? _currentLogDate;
    private string? _currentLogPath;
    private bool _isLogDirectoryReady;

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(
            categoryName,
            static (name, provider) => new FileLogger(name, provider),
            this);

    public void Dispose()
    {
    }

    internal void Write(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        string message,
        Exception? exception)
    {
        DateTimeOffset localNow = timeProvider.GetLocalNow();
        DateTimeOffset utcNow = timeProvider.GetUtcNow();
        string line =
            $"[{utcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] "
            + $"{logLevel,-11} {categoryName} [{eventId.Id}] {message}";

        if (exception is not null)
        {
            line = $"{line}{Environment.NewLine}{exception}";
        }

        lock (_lock)
        {
            EnsureLogFilePath(localNow);
            File.AppendAllText(
                _currentLogPath!,
                $"{line}{Environment.NewLine}",
                Encoding.UTF8);
        }
    }

    private void EnsureLogFilePath(DateTimeOffset localNow)
    {
        if (!_isLogDirectoryReady)
        {
            Directory.CreateDirectory(_logDirectory);
            _isLogDirectoryReady = true;
        }

        DateOnly currentDate = DateOnly.FromDateTime(localNow.DateTime);
        if (_currentLogDate == currentDate && _currentLogPath is not null)
        {
            return;
        }

        _currentLogDate = currentDate;
        _currentLogPath = Path.Combine(
            _logDirectory,
            $"qidian-novel-downloader-{localNow:yyyyMMdd}.log");
    }

    private sealed class FileLogger(string categoryName, FileLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(categoryName, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
