using System.Text;

namespace Hcoona.VsCodeCopilotTelegramHook;

internal static class AppFileSystem
{
    private const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode OwnerOnlyFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void AppendAllText(string path, string content, Encoding encoding)
    {
        EnsureParentDirectory(path);

        using FileStream stream = OpenFileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.Write,
            FileShare.Read,
            FileOptions.None);

        EnsureOwnerOnlyFileMode(path);
        stream.Seek(0, SeekOrigin.End);

        using StreamWriter writer = new(stream, encoding, bufferSize: 1024, leaveOpen: false);
        writer.Write(content);
    }

    public static FileStream CreateFile(string path)
    {
        EnsureParentDirectory(path);

        FileStream stream = OpenFileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            FileOptions.Asynchronous);

        EnsureOwnerOnlyFileMode(path);
        return stream;
    }

    private static void EnsureParentDirectory(string path)
    {
        string? directoryPath = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directoryPath);
            return;
        }

        Directory.CreateDirectory(directoryPath, OwnerOnlyDirectoryMode);
        File.SetUnixFileMode(directoryPath, OwnerOnlyDirectoryMode);
    }

    private static void EnsureOwnerOnlyFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(path, OwnerOnlyFileMode);
    }

    private static FileStream OpenFileStream(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        FileOptions options)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = mode,
                    Access = access,
                    Share = share,
                    Options = options,
                });
        }

        return new FileStream(
            path,
            new FileStreamOptions
            {
                Mode = mode,
                Access = access,
                Share = share,
                Options = options,
                UnixCreateMode = OwnerOnlyFileMode,
            });
    }
}
