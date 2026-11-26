using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Authentication.Windows.Scenarios;

// Observes only a supplied live HWND owned by this scenario process and its
// descendants. This does not inspect the desktop or establish physical usability.
internal static unsafe partial class OwnedWindowObservation
{
    internal sealed record Control(nint Handle, string ClassName, string Text);

    internal static uint RequireOwned(nint window)
    {
        if (window == 0 || window == 0xffff)
            throw new InvalidOperationException("A concrete owned window is required.");
        uint process = 0;
        var thread = GetWindowThreadProcessId(window, &process);
        if (thread == 0 || process != (uint)Environment.ProcessId)
            throw new InvalidOperationException("Window ownership was not established.");
        return thread;
    }

    internal static bool IsVisible(nint window)
    {
        RequireOwned(window);
        return IsWindowVisible(window) != 0;
    }

    // Never throws: callers sample from inside native dispatch on the creating thread.
    internal static bool IsVisibleOnCreatingThread(nint window) =>
        IsWindow(window) != 0 && IsWindowVisible(window) != 0;

    internal static bool Exists(nint window) => window != 0 && IsWindow(window) != 0;

    internal static IReadOnlyList<Control> ReadControls(nint parent)
    {
        RequireOwned(parent); // Null would enumerate unrelated top-level windows.
        var observation = new Observation();
        var context = GCHandle.Alloc(observation);
        try
        {
            _ = EnumChildWindows(parent, &ObserveChild, GCHandle.ToIntPtr(context));
            if (observation.Failed)
                throw new InvalidOperationException("Owned control observation failed.");
            return observation.Controls;
        }
        finally
        {
            context.Free(); // Enumeration and every callback have returned.
        }
    }

    internal static string ReadText(nint window)
    {
        RequireOwned(window);
        char* buffer = stackalloc char[2048];
        var length = GetWindowTextW(window, buffer, 2048);
        if (length < 0 || length >= 2047)
            throw new InvalidOperationException("Owned text exceeds the observation bound.");
        return new string(buffer, 0, length);
    }

    internal static void Post(nint window, uint message, nuint parameter = 0, nint detail = 0)
    {
        RequireOwned(window);
        if (PostMessageW(window, message, parameter, detail) == 0)
            throw new InvalidOperationException("Owned scalar message could not be posted.");
    }

    internal static void CloseOnCreatingThread(nint window)
    {
        if (RequireOwned(window) != GetCurrentThreadId())
            throw new InvalidOperationException("Synchronous closure requires the creating thread.");
        _ = SendMessageW(window, 0x0010, 0, 0); // WM_CLOSE, same-thread owned parent only.
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ObserveChild(nint window, nint context)
    {
        Observation? observation = null;
        try
        {
            observation = (Observation)GCHandle.FromIntPtr(context).Target!;
            if (observation.Controls.Count >= 16)
                throw new InvalidOperationException("Owned control count exceeds the observation bound.");
            RequireOwned(window);
            char* className = stackalloc char[256];
            var length = GetClassNameW(window, className, 256);
            if (length is <= 0 or >= 255)
                throw new InvalidOperationException("Owned control class could not be observed.");
            observation.Controls.Add(new(window, new string(className, 0, length), ReadText(window)));
            return 1;
        }
        catch (Exception)
        {
            if (observation is not null) observation.Failed = true;
            return 0;
        }
    }

    private sealed class Observation
    {
        internal List<Control> Controls { get; } = [];
        internal bool Failed;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, uint* process);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindow(nint window);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int EnumChildWindows(nint parent,
        delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint context);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowTextW(nint window, char* text, int maximum);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetClassNameW(nint window, char* text, int maximum);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int PostMessageW(nint window, uint message, nuint parameter, nint detail);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SendMessageW(nint window, uint message, nuint parameter, nint detail);
}
