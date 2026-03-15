namespace Hcoona.VsCodeCopilotTelegramHook.Logging;

internal sealed class SessionLogFileContext
{
    private readonly AsyncLocal<string?> currentLogFilePath = new();

    public string? CurrentLogFilePath => currentLogFilePath.Value;

    public IDisposable UseLogFile(string logFilePath)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            throw new InvalidOperationException("The log file path cannot be empty.");
        }

        string? previousPath = currentLogFilePath.Value;
        currentLogFilePath.Value = Path.GetFullPath(logFilePath);
        return new RestoreScope(this, previousPath);
    }

    private sealed class RestoreScope(
        SessionLogFileContext owner,
        string? previousPath) : IDisposable
    {
        private bool isDisposed;

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            owner.currentLogFilePath.Value = previousPath;
            isDisposed = true;
        }
    }
}
