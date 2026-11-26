using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Authentication.Windows;

// Borrow inherited handles. Native I/O reports real transfer counts, including
// broken and short writes that Console's stream abstraction can suppress.
internal static partial class WindowsStandardHandles
{
    internal const uint Input = unchecked((uint)-10);
    internal const uint Output = unchecked((uint)-11);
    internal const uint Error = unchecked((uint)-12);
    internal const uint Disk = 1;
    internal const uint Character = 2;
    internal const uint Pipe = 3;
    internal const int BrokenPipe = 109;
    internal const int NoData = 232;
    internal const int NotConnected = 233;
    internal const int MoreData = 234;
    private const int Pending = 997;

    internal static SafeFileHandle Borrow(uint identifier) => new(GetStdHandle(identifier), ownsHandle: false);

    internal static unsafe int Peek(SafeFileHandle handle, out uint available)
    {
        uint count = 0;
        var success = PeekNamedPipe(handle, null, 0, null, &count, null);
        var error = success == 0 ? Marshal.GetLastPInvokeError() : 0;
        available = count;
        return error;
    }

    internal static unsafe int Discard(SafeFileHandle handle, bool asynchronous, byte[] buffer,
        uint length, out uint transferred)
    {
        fixed (byte* pointer = buffer)
        {
            if (!asynchronous)
            {
                uint count = 0;
                var success = ReadFile(handle, pointer, length, &count, null);
                var error = success == 0 ? Marshal.GetLastPInvokeError() : 0;
                transferred = count;
                return error;
            }

            using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
            var overlapped = new NativeOverlapped { EventHandle = completed.SafeWaitHandle.DangerousGetHandle() };
            var started = ReadFile(handle, pointer, length, null, &overlapped);
            var failure = started == 0 ? Marshal.GetLastPInvokeError() : 0;
            if (failure is not (0 or Pending))
            {
                transferred = 0;
                return failure;
            }

            // Keep the buffer, event and OVERLAPPED alive until actual completion.
            // Cancelling managed waiting elsewhere must not release native state.
            var result = GetOverlappedResult(handle, &overlapped, out transferred, 1);
            return result == 0 ? Marshal.GetLastPInvokeError() : 0;
        }
    }

    internal static unsafe bool Write(uint identifier, byte[] bytes)
    {
        using var handle = Borrow(identifier);
        if (handle.IsInvalid) return false;
        var type = GetFileType(handle);
        // Console mode queries require read rights. A writable console must not
        // be rejected merely because that unrelated query would fail.
        var asynchronous = type != Character && handle.IsAsync;
        long position = 0;
        if (asynchronous && type == Disk && SetFilePointerEx(handle, 0, out position, 1) == 0)
        {
            return false;
        }

        using var completed = asynchronous ? new EventWaitHandle(false, EventResetMode.ManualReset) : null;
        fixed (byte* pointer = bytes)
        {
            var offset = 0;
            while (offset < bytes.Length)
            {
                uint transferred;
                if (asynchronous)
                {
                    var overlapped = new NativeOverlapped
                    {
                        EventHandle = completed!.SafeWaitHandle.DangerousGetHandle(),
                        OffsetLow = unchecked((int)position),
                        OffsetHigh = unchecked((int)(position >> 32)),
                    };
                    var started = WriteFile(handle, pointer + offset, (uint)(bytes.Length - offset), null, &overlapped);
                    var failure = started == 0 ? Marshal.GetLastPInvokeError() : 0;
                    if (failure is not (0 or Pending)) return false;
                    if (GetOverlappedResult(handle, &overlapped, out transferred, 1) == 0) return false;
                }
                else
                {
                    transferred = 0;
                    if (WriteFile(handle, pointer + offset, (uint)(bytes.Length - offset), &transferred, null) == 0)
                    {
                        return false;
                    }
                }

                if (transferred > bytes.Length - offset) return false;
                if (transferred == 0)
                {
                    // A nonblocking pipe can make no progress. The independent
                    // process watchdog owns the unchanged delivery deadline.
                    Thread.Sleep(10);
                    continue;
                }

                offset += (int)transferred;
                if (asynchronous && type == Disk)
                {
                    position = checked(position + transferred);
                    // Explicit-offset writes do not advance the inherited stream
                    // cursor. Preserve it for the caller's next sequential write.
                    if (SetFilePointerEx(handle, position, out _, 0) == 0) return false;
                }
            }
        }
        return true;
    }

    internal static void Terminate(int exitCode = 2)
    {
        // Self termination normally does not return. The fallback also emits no
        // exception or CLR diagnostic containing request/provider information.
        _ = TerminateProcess(GetCurrentProcess(), unchecked((uint)exitCode));
        Environment.Exit(exitCode);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetStdHandle(uint identifier);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static partial uint GetFileType(SafeFileHandle handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int PeekNamedPipe(SafeFileHandle handle, void* buffer, uint size,
        uint* read, uint* available, uint* remaining);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int ReadFile(SafeFileHandle handle, byte* buffer, uint size,
        uint* read, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int WriteFile(SafeFileHandle handle, byte* buffer, uint size,
        uint* written, NativeOverlapped* overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int GetOverlappedResult(SafeFileHandle handle, NativeOverlapped* overlapped,
        out uint transferred, int wait);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetFilePointerEx(SafeFileHandle handle, long distance, out long position, uint method);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int TerminateProcess(nint process, uint exitCode);
}
