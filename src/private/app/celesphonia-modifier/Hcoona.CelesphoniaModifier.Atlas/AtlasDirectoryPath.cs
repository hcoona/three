using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Hcoona.CelesphoniaModifier.Atlas;

internal static class AtlasDirectoryPathResolver
{
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint ShareReadWriteDelete = 0x00000007;
    private const uint FileNameNormalized = 0;
    private const uint VolumeNameGuid = 1;
    private const int MaximumFinalPathLength = 32_768;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    public static string? TryGetFinalPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Directory identity resolution requires Windows.");
        }

        using SafeFileHandle handle = CreateFile(
            path,
            desiredAccess: 0,
            ShareReadWriteDelete,
            securityAttributes: IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            templateFile: IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error is ErrorFileNotFound or ErrorPathNotFound)
            {
                return null;
            }

            throw CreateIOException(
                "Unable to open a directory for final-path validation.",
                error);
        }

        char[] finalPath = new char[MaximumFinalPathLength];
        uint length = GetFinalPathNameByHandle(
                handle,
                finalPath,
                (uint)finalPath.Length,
                FileNameNormalized | VolumeNameGuid);
        if (length == 0 || length >= finalPath.Length)
        {
            throw CreateIOException(
                "Unable to resolve a directory final path.",
                Marshal.GetLastWin32Error());
        }

        return new string(finalPath, 0, checked((int)length));
    }

    private static IOException CreateIOException(string message, int error) =>
        new(message, new Win32Exception(error));

    [DllImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        [Out] char[] filePath,
        uint filePathLength,
        uint flags);
}
