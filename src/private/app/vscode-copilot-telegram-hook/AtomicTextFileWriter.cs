using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal interface ITextFileWriter
{
    void WriteAllText(string path, string content);
}

internal static class AtomicTextFileWriter
{
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly ITextFileWriter DefaultWriter = new DefaultAtomicTextFileWriter();
    private static readonly AsyncLocal<ITextFileWriter?> TestWriter = new();

    public static void WriteAllText(string path, string content)
        => (TestWriter.Value ?? DefaultWriter).WriteAllText(path, content);

    internal static IDisposable UseWriterForTesting(ITextFileWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ITextFileWriter? previousWriter = TestWriter.Value;
        TestWriter.Value = writer;
        return new RestoreWriterScope(previousWriter);
    }

    private sealed class RestoreWriterScope(ITextFileWriter? previousWriter) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            TestWriter.Value = previousWriter;
            disposed = true;
        }
    }

    private sealed class DefaultAtomicTextFileWriter : ITextFileWriter
    {
        public void WriteAllText(string path, string content)
        {
            string fullPath = Path.GetFullPath(path);
            string directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException(
                    $"Cannot determine the parent directory for '{path}'.");
            EnsureOwnerOnlyDirectory(directoryPath);

            string tempPath = Path.Combine(
                directoryPath,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

            try
            {
                using (FileStream stream = OpenTempFile(tempPath))
                using (StreamWriter writer = new(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }
                ReplaceFile(tempPath, fullPath);
                tempPath = string.Empty;
            }
            finally
            {
                TryDeleteTempFile(tempPath);
            }
        }

        private static void ReplaceFile(string tempPath, string destinationPath)
        {
            if (OperatingSystem.IsWindows() && File.Exists(destinationPath))
            {
                File.Replace(tempPath, destinationPath, destinationBackupFileName: null);
                return;
            }

            File.Move(tempPath, destinationPath, overwrite: true);
        }

        private static FileStream OpenTempFile(string tempPath)
        {
            FileStreamOptions options = new()
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = OwnerOnlyFileMode;
            }

            return new FileStream(tempPath, options);
        }

        private static void TryDeleteTempFile(string tempPath)
        {
            if (string.IsNullOrWhiteSpace(tempPath) || !File.Exists(tempPath))
            {
                return;
            }

            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void EnsureOwnerOnlyDirectory(string directoryPath)
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directoryPath);
                return;
            }

            bool directoryExisted = Directory.Exists(directoryPath);
            Directory.CreateDirectory(directoryPath, OwnerOnlyDirectoryMode);
            if (!directoryExisted)
            {
                File.SetUnixFileMode(directoryPath, OwnerOnlyDirectoryMode);
            }
        }

    }
}
