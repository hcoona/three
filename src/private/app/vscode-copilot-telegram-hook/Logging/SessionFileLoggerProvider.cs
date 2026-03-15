using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal sealed class SessionFileLoggerProvider(
    SessionLogFileContext logFileContext) : ILoggerProvider
{
    private const string AllowedCategoryPrefix = "Hcoona.VsCodeCopilotTelegramHook";

    private static readonly string UserProfilePath =
        Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly ConcurrentDictionary<string, object> fileLocks =
        new(StringComparer.Ordinal);

    public static bool IsCategoryAllowed(string? categoryName)
        => !string.IsNullOrWhiteSpace(categoryName)
            && categoryName.StartsWith(AllowedCategoryPrefix, StringComparison.Ordinal);

    public ILogger CreateLogger(string categoryName)
        => new SessionFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void WriteEntry(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        string message,
        Exception? exception)
    {
        string? logFilePath = logFileContext.CurrentLogFilePath;
        if (string.IsNullOrWhiteSpace(logFilePath)
            || !IsCategoryAllowed(categoryName))
        {
            return;
        }

        StringBuilder builder = new();
        builder.Append(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'"));
        builder.Append(' ');
        builder.Append('[');
        builder.Append(GetShortLevel(logLevel));
        builder.Append("] ");
        builder.Append(categoryName);

        if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
        {
            builder.Append(" {EventId=");
            builder.Append(eventId.Id);
            if (!string.IsNullOrWhiteSpace(eventId.Name))
            {
                builder.Append(", EventName=");
                builder.Append(eventId.Name);
            }

            builder.Append('}');
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            builder.Append(": ");
            builder.Append(SanitizeSingleLine(message));
        }

        if (exception is not null)
        {
            builder.Append(" | ");
            builder.Append(RenderException(exception));
        }

        builder.AppendLine();

        object fileLock = fileLocks.GetOrAdd(logFilePath, static _ => new object());
        lock (fileLock)
        {
            AppFileSystem.AppendAllText(logFilePath, builder.ToString(), Encoding.UTF8);
        }
    }

    private static string RenderException(Exception exception)
    {
        return SanitizeSingleLine(
            $"{exception.GetType().FullName}: {exception.Message}");
    }

    private static string GetShortLevel(LogLevel logLevel)
        => logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "NON",
        };

    private sealed class SessionFileLogger(
        SessionFileLoggerProvider owner,
        string categoryName) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
            => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || !IsCategoryAllowed(categoryName))
            {
                return;
            }

            string message = formatter(state, exception);
            owner.WriteEntry(categoryName, logLevel, eventId, message, exception);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private static string SanitizeSingleLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        sanitized = RedactTelegramRequestPath(sanitized);

        if (!string.IsNullOrWhiteSpace(UserProfilePath))
        {
            sanitized = sanitized.Replace(UserProfilePath, "~", PathComparison);
        }

        return sanitized;
    }

    private static string RedactTelegramRequestPath(string value)
    {
        const string botMarker = "/bot";
        const string sendMessageMarker = "/sendMessage";

        int searchIndex = 0;
        while (searchIndex < value.Length)
        {
            int markerIndex = value.IndexOf(botMarker, searchIndex, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                break;
            }

            int tokenStartIndex = markerIndex + botMarker.Length;
            int markerEndIndex = value.IndexOf(
                sendMessageMarker,
                tokenStartIndex,
                StringComparison.Ordinal);
            if (markerEndIndex < 0)
            {
                break;
            }

            value = string.Concat(
                value.AsSpan(0, tokenStartIndex),
                "<redacted>",
                value.AsSpan(markerEndIndex));
            searchIndex = tokenStartIndex + "<redacted>".Length;
        }

        return value;
    }
}
