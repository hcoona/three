namespace Hcoona.VsCodeCopilotTelegramHook;

internal sealed record FileSystemMetadataSnapshot(
    UnixFileMode? FileUnixMode,
    bool FilePathWasDirectory,
    IReadOnlyList<DirectoryMetadataSnapshot> Directories)
{
    public static FileSystemMetadataSnapshot Capture(string filePath, bool fileExisted)
    {
        UnixFileMode? fileUnixMode = null;
        if (fileExisted && !OperatingSystem.IsWindows())
        {
            try
            {
                fileUnixMode = File.GetUnixFileMode(filePath);
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        List<DirectoryMetadataSnapshot> directories = [];
        string? directoryPath = Path.GetDirectoryName(Path.GetFullPath(filePath));
        while (!string.IsNullOrWhiteSpace(directoryPath))
        {
            bool existed = Directory.Exists(directoryPath);
            UnixFileMode? unixMode = null;
            if (existed && !OperatingSystem.IsWindows())
            {
                try
                {
                    unixMode = File.GetUnixFileMode(directoryPath);
                }
                catch (PlatformNotSupportedException)
                {
                }
            }

            directories.Add(new DirectoryMetadataSnapshot(directoryPath, existed, unixMode));
            if (existed)
            {
                break;
            }

            directoryPath = Path.GetDirectoryName(directoryPath);
        }

        return new FileSystemMetadataSnapshot(
            fileUnixMode,
            Directory.Exists(filePath),
            directories);
    }

    public void Restore(string filePath)
    {
        if (FileUnixMode is not null && File.Exists(filePath) && !OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(filePath, FileUnixMode.Value);
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        foreach (DirectoryMetadataSnapshot directory in Directories)
        {
            directory.Restore();
        }
    }

    public bool MatchesCurrent(string filePath, bool fileExists)
    {
        if (fileExists != File.Exists(filePath))
        {
            return false;
        }

        if (FilePathWasDirectory != Directory.Exists(filePath))
        {
            return false;
        }

        if (
            fileExists
            && FileUnixMode is not null
            && !OperatingSystem.IsWindows()
            && File.GetUnixFileMode(filePath) != FileUnixMode.Value
        )
        {
            return false;
        }

        return Directories.All(static directory => directory.MatchesCurrent());
    }
}

internal sealed record DirectoryMetadataSnapshot(
    string DirectoryPath,
    bool Existed,
    UnixFileMode? UnixMode)
{
    public void Restore()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return;
        }

        if (Existed)
        {
            if (UnixMode is not null && !OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(DirectoryPath, UnixMode.Value);
                }
                catch (PlatformNotSupportedException)
                {
                }
            }

            return;
        }

        if (!Directory.EnumerateFileSystemEntries(DirectoryPath).Any())
        {
            try
            {
                Directory.Delete(DirectoryPath);
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException) when (
                Directory.Exists(DirectoryPath)
                && Directory.EnumerateFileSystemEntries(DirectoryPath).Any())
            {
            }
        }
    }

    public bool MatchesCurrent()
    {
        if (Existed != Directory.Exists(DirectoryPath))
        {
            return false;
        }

        return !Existed
            || UnixMode is null
            || OperatingSystem.IsWindows()
            || File.GetUnixFileMode(DirectoryPath) == UnixMode.Value;
    }
}
