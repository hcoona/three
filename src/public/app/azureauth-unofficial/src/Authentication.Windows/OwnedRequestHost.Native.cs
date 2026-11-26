using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Authentication.Core;

namespace Authentication.Windows;

internal sealed partial class OwnedRequestHost
{
    private const uint CloseMessage = 0x8001; // WM_APP + 1; scalar, owned-window only.
    private const uint ParentStyle = 0x00ca0000; // WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX.
    private const int DefaultPlacement = int.MinValue; // CW_USEDEFAULT.
    [ThreadStatic] private static OwnedRequestHost? currentHost;
    // Native resources below belong exclusively to the creating thread.
    private nint nativeParent;
    private nint instance;
    private ushort windowClass;
    private string? className;

    // The parent is a hidden, childless owner handle for the provider display. It
    // has no owned controls, layout, font or keyboard routing; WAM owns all UI.
    private unsafe nint CreateNativeParent()
    {
        instance = GetModuleHandleW(null);
        if (instance == 0) throw new InvalidOperationException("The window module is unavailable.");

        className = "Authentication.OwnedRequest." + Guid.NewGuid().ToString("N");
        fixed (char* name = className)
        {
            var definition = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Procedure = &WindowProcedure,
                Instance = instance,
                ClassName = name,
            };
            windowClass = RegisterClassExW(&definition);
        }
        if (windowClass == 0) throw new InvalidOperationException("Owned window registration failed.");

        // No WS_VISIBLE, and the product never shows it. The creating thread's
        // finally block destroys it.
        nativeParent = CreateWindowExW(0, className, WindowsProcess.DisplayName,
            ParentStyle, DefaultPlacement, DefaultPlacement, DefaultPlacement, DefaultPlacement,
            0, 0, instance, 0);
        if (nativeParent == 0) throw new InvalidOperationException("Owned window creation failed.");
        return nativeParent;
    }

    private unsafe void PumpNativeMessages()
    {
        while (!IsTerminal)
        {
            Message message;
            var result = GetMessageW(&message, 0, 0, 0);
            if (result == -1) throw new InvalidOperationException("Owned message retrieval failed.");
            if (result == 0)
            {
                if (!IsTerminal) FailHost();
                return;
            }
            if (IsTerminal) return;
            _ = TranslateMessage(&message);
            _ = DispatchMessageW(&message);
        }
    }

    private static void PostOwnedClose(nint window)
    {
        if (PostMessageW(window, CloseMessage, 0, 0) == 0)
            throw new InvalidOperationException("Owned window closure could not be posted.");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint parameter, nint detail)
    {
        var host = currentHost;
        try
        {
            if (host is null) return DefWindowProcW(window, message, parameter, detail);
            host.checkpoint?.Invoke(OwnedHostCheckpoint.MessageDispatch, window);
            switch (message)
            {
                case 0x0010: // WM_CLOSE: user/system close is cancellation, never internal closure.
                    host.CancelFromWindow();
                    return 0;
                case CloseMessage:
                    return 0; // The terminal loop exits into creating-thread cleanup.
                case 0x0002: // WM_DESTROY.
                    if (!host.IsTerminal) host.FailHost();
                    PostQuitMessage(0);
                    return 0;
                default:
                    return DefWindowProcW(window, message, parameter, detail);
            }
        }
        catch (Exception)
        {
            // Even a failing notification must never unwind through the native ABI.
            try { host?.FailHost(); }
            catch (Exception) { }
            return message == 0x0001 ? -1 : 0; // Fail WM_CREATE or WM_NCCREATE safely.
        }
    }

    private void CleanupNative()
    {
        // Destruction sends callbacks synchronously. Keep the context alive until
        // it has actually returned successfully.
        DestroyNativeParent();
        if (windowClass != 0)
        {
            if (UnregisterClassW(className!, instance) == 0)
                throw new InvalidOperationException("Owned window class release failed.");
            windowClass = 0;
        }
    }

    private void DestroyNativeParent()
    {
        if (nativeParent != 0)
        {
            if (DestroyWindow(nativeParent) == 0)
                throw new InvalidOperationException("Owned window destruction failed.");
            nativeParent = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WindowClass
    {
        internal uint Size;
        internal uint Style;
        internal delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> Procedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint Background;
        internal char* MenuName;
        internal char* ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        internal nint Window;
        internal uint Id;
        internal nuint Parameter;
        internal nint Detail;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial ushort RegisterClassExW(WindowClass* definition);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateWindowExW(uint extendedStyle, string className, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int GetMessageW(Message* message, nint window, uint minimum, uint maximum);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int TranslateMessage(Message* message);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial nint DispatchMessageW(Message* message);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int PostMessageW(nint window, uint message, nuint parameter, nint detail);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint DefWindowProcW(nint window, uint message, nuint parameter, nint detail);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DestroyWindow(nint window);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int UnregisterClassW(string className, nint instance);
}
