using System.Runtime.InteropServices;
using System.Security;
using Authentication.Core;
using Microsoft.Win32.SafeHandles;

namespace Authentication.Windows;

internal sealed partial class WindowsProfileSource : IProfileSource
{
    private const int MaximumProfileBytes = 65536;
    private const int PathBufferCharacters = 32768;
    private const uint DriveFixed = 3;

    // Path resolution and opening can block before the first asynchronous read.
    // Keep that work off the lifetime coordinator's calling thread.
    public Task<ReadOnlyMemory<byte>> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => ReadSnapshotAsync(path, cancellationToken), cancellationToken);

    private static async Task<ReadOnlyMemory<byte>> ReadSnapshotAsync(string path,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows() || path.Length < 3 || !char.IsAsciiLetter(path[0])
                || path[1] != ':' || path[2] is not ('\\' or '/') || path.Any(char.IsControl))
            {
                throw Unavailable();
            }

            var normalized = Path.GetFullPath(path);
            if (GetDriveType(Path.GetPathRoot(normalized)!) != DriveFixed) throw Unavailable();
            RequireFixedVolume(normalized);
            cancellationToken.ThrowIfCancellationRequested();

            using var file = new FileStream(normalized, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                BufferSize = 1,
            });

            // Check the opened target as well as the caller's path. FileStream keeps
            // ownership of this handle until the complete snapshot has been read.
            cancellationToken.ThrowIfCancellationRequested();
            RequireFixedVolume(GetFinalPath(file.SafeFileHandle));
            cancellationToken.ThrowIfCancellationRequested();
            var length = file.Length;
            if (length is < 1 or > MaximumProfileBytes) throw Unavailable();

            var snapshot = new byte[(int)length];
            await file.ReadExactlyAsync(snapshot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or SecurityException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Do not retain the original exception: it can contain a path or content.
            throw Unavailable();
        }
    }

    private static unsafe void RequireFixedVolume(string path)
    {
        var buffer = new char[PathBufferCharacters];
        fixed (char* output = buffer)
        {
            if (GetVolumePathName(path, output, (uint)buffer.Length) == 0) throw Unavailable();
        }

        var length = Array.IndexOf(buffer, '\0');
        if (length <= 0 || buffer[length - 1] != '\\'
            || GetDriveType(new string(buffer, 0, length)) != DriveFixed)
        {
            throw Unavailable();
        }
    }

    private static unsafe string GetFinalPath(SafeFileHandle file)
    {
        var buffer = new char[PathBufferCharacters];
        fixed (char* output = buffer)
        {
            // VOLUME_NAME_GUID requests the normalized local volume identity.
            // Success excludes NUL; insufficient capacity includes the required NUL.
            var length = GetFinalPathNameByHandle(file, output, (uint)buffer.Length, 1);
            if (length == 0 || length >= buffer.Length) throw Unavailable();
            var path = new string(buffer, 0, (int)length);
            if (!path.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)) throw Unavailable();
            return path;
        }
    }

    private static IOException Unavailable() => new("Profile is unavailable.");

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int GetVolumePathName(string path, char* volumePath, uint capacity);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDriveType(string rootPath);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* path,
        uint capacity, uint flags);
}
