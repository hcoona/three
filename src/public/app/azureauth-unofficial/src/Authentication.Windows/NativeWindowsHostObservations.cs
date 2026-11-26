using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Authentication.Core;

namespace Authentication.Windows;

// Request-local observations only. Returned facts never contain a native identity,
// borrowed pointer, handle, account name, or diagnostic. Construction is inert.
internal sealed unsafe partial class NativeWindowsHostObservations : IWindowsHostObservations
{
    internal const int MaximumDesktopNameBytes = 512;
    private const uint TokenQuery = 0x0008;
    private const int NoThreadToken = 1008; // ERROR_NO_TOKEN.
    private const uint TokenStatisticsClass = 10;
    private const uint TokenLogonSidClass = 28;
    private const int LogonIdsSidType = 21;
    private const int MaximumSidBytes = 68;
    private const int SingleGroupPrefixBytes = 24; // x64 TOKEN_GROUPS with one SID_AND_ATTRIBUTES.
    private const int MaximumLogonSidBufferBytes = SingleGroupPrefixBytes + MaximumSidBytes;

    public WindowsHostPlatform ReadPlatform(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var windows = OperatingSystem.IsWindows();
        cancellationToken.ThrowIfCancellationRequested();
        var process = RuntimeInformation.ProcessArchitecture;
        cancellationToken.ThrowIfCancellationRequested();
        var operatingSystem = RuntimeInformation.OSArchitecture;
        cancellationToken.ThrowIfCancellationRequested();
        var version = Environment.OSVersion.Version;
        cancellationToken.ThrowIfCancellationRequested();
        return new(windows, process, operatingSystem, version);
    }

    public bool? ReadWorkstationProduct(CancellationToken cancellationToken)
    {
        RequireWindows(cancellationToken);
        var version = new VersionInfo { Size = (uint)sizeof(VersionInfo), ProductType = 1 };
        cancellationToken.ThrowIfCancellationRequested();
        var mask = VerSetConditionMask(0, 0x80, 1); // VER_PRODUCT_TYPE, VER_EQUAL.
        cancellationToken.ThrowIfCancellationRequested();
        var matched = VerifyVersionInfoW(&version, 0x80, mask);
        cancellationToken.ThrowIfCancellationRequested();
        return matched != 0;
    }

    public WindowsThreadIdentity ReadThreadIdentity(CancellationToken cancellationToken)
    {
        RequireWindows(cancellationToken);
        var thread = GetCurrentThread(); // Borrowed pseudo-handle.
        cancellationToken.ThrowIfCancellationRequested();
        nint token = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var opened = OpenThreadToken(thread, TokenQuery, 1, out token);
            var error = Marshal.GetLastPInvokeError();
            cancellationToken.ThrowIfCancellationRequested();
            if (opened != 0) return WindowsThreadIdentity.Impersonating;
            return error == NoThreadToken
                ? WindowsThreadIdentity.NoToken : WindowsThreadIdentity.Unavailable;
        }
        finally { CloseToken(token); }
    }

    public WindowsLocalLogon? ReadOwnLogonAndStation(CancellationToken cancellationToken)
    {
        RequireWindows(cancellationToken);
        var process = GetCurrentProcess(); // Borrowed pseudo-handle.
        cancellationToken.ThrowIfCancellationRequested();
        nint token = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var opened = OpenProcessToken(process, TokenQuery, out token);
            cancellationToken.ThrowIfCancellationRequested();
            if (opened == 0 || token == 0) return UnavailableLogon(WindowsLogonReadFailure.ProcessToken);

            TokenStatistics statistics = default;
            cancellationToken.ThrowIfCancellationRequested();
            var queried = GetTokenInformation(token, TokenStatisticsClass, &statistics,
                (uint)sizeof(TokenStatistics), out var returned);
            cancellationToken.ThrowIfCancellationRequested();
            if (queried == 0 || returned != sizeof(TokenStatistics)) return UnavailableLogon(WindowsLogonReadFailure.TokenStatistics);

            nint allocation = 0;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = LsaGetLogonSessionData(&statistics.AuthenticationId, out allocation);
                cancellationToken.ThrowIfCancellationRequested();
                if (status != 0 || allocation == 0) return UnavailableLogon(WindowsLogonReadFailure.SessionData);
                var data = (LogonPrefix*)allocation;
                // Read only Size until the returned prefix covers every selected field.
                if (data->Size < sizeof(LogonPrefix)) return UnavailableLogon(WindowsLogonReadFailure.SessionDataSize);
                if (data->LogonId.Low != statistics.AuthenticationId.Low
                    || data->LogonId.High != statistics.AuthenticationId.High) return UnavailableLogon(WindowsLogonReadFailure.SessionId);
                if (!ValidSid(data->Sid, cancellationToken)) return UnavailableLogon(WindowsLogonReadFailure.SessionSid);

                var identity = ClassifySid(data->Sid, cancellationToken);
                if (identity != WindowsLogonIdentity.User)
                    return new(data->LogonType, identity, false, null);

                var station = ReadStation(token, data->Sid, cancellationToken);
                return new(data->LogonType, identity, station.Visible, station.UserMatches);
            }
            finally
            {
                // The SID is borrowed from this allocation through the last comparison.
                if (allocation != 0 && LsaFreeReturnBuffer(allocation) != 0)
                    throw new InvalidOperationException("Local logon observation cleanup failed.");
            }
        }
        finally { CloseToken(token); }
    }

    private static WindowsLocalLogon UnavailableLogon(WindowsLogonReadFailure failure) =>
        new(0, WindowsLogonIdentity.Invalid, false, null, failure);

    public WindowsSessionConnection ReadSessionConnection(CancellationToken cancellationToken)
    {
        RequireWindows(cancellationToken);
        var process = GetCurrentProcessId();
        cancellationToken.ThrowIfCancellationRequested();
        var identified = ProcessIdToSessionId(process, out var session);
        cancellationToken.ThrowIfCancellationRequested();
        if (identified == 0) return WindowsSessionConnection.SessionIdUnavailable;
        if (session == 0) return WindowsSessionConnection.ZeroSession;

        nint allocation = 0;
        try
        {
            // WTS_CURRENT_SERVER_HANDLE and only this process's WTSConnectState.
            cancellationToken.ThrowIfCancellationRequested();
            var queried = WTSQuerySessionInformationW(0, session, 8, out allocation, out var bytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (queried == 0) return WindowsSessionConnection.QueryUnavailable;
            if (allocation == 0 || bytes != sizeof(int)) return WindowsSessionConnection.Malformed;
            var state = *(int*)allocation;
            if (state is < 0 or > 9) return WindowsSessionConnection.Malformed;
            return state == 0 ? WindowsSessionConnection.Active : WindowsSessionConnection.Inactive;
        }
        finally
        {
            if (allocation != 0) WTSFreeMemory(allocation);
        }
    }

    public bool? ReadInputDesktop(CancellationToken cancellationToken)
    {
        RequireWindows(cancellationToken);
        var thread = GetCurrentThreadId();
        cancellationToken.ThrowIfCancellationRequested();
        var calling = GetThreadDesktop(thread); // Borrowed; never CloseDesktop.
        cancellationToken.ThrowIfCancellationRequested();
        if (calling == 0) return null;

        nint input = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            input = OpenInputDesktop(0, 0, 1); // Non-inheritable DESKTOP_READOBJECTS only.
            cancellationToken.ThrowIfCancellationRequested();
            if (input == 0) return null;
            var callingName = stackalloc char[MaximumDesktopNameBytes / sizeof(char)];
            var inputName = stackalloc char[MaximumDesktopNameBytes / sizeof(char)];
            var callingBuffer = new Span<char>(callingName, MaximumDesktopNameBytes / sizeof(char));
            var inputBuffer = new Span<char>(inputName, MaximumDesktopNameBytes / sizeof(char));
            callingBuffer.Clear();
            inputBuffer.Clear();

            cancellationToken.ThrowIfCancellationRequested();
            var queried = GetUserObjectInformationW(calling, 2, callingName,
                MaximumDesktopNameBytes, out var callingBytes); // UOI_NAME.
            cancellationToken.ThrowIfCancellationRequested();
            if (queried == 0 || !TryGetDesktopNameLength(callingBuffer, callingBytes, out var callingLength))
                return null;
            cancellationToken.ThrowIfCancellationRequested();
            queried = GetUserObjectInformationW(input, 2, inputName,
                MaximumDesktopNameBytes, out var inputBytes);
            cancellationToken.ThrowIfCancellationRequested();
            if (queried == 0 || !TryGetDesktopNameLength(inputBuffer, inputBytes, out var inputLength))
                return null;
            // Both objects belong to the unchanged current process station. Compare
            // complete returned names exactly; never normalize or export them.
            return callingBuffer[..callingLength].SequenceEqual(inputBuffer[..inputLength]);
        }
        finally
        {
            if (input != 0 && CloseDesktop(input) == 0)
                throw new InvalidOperationException("Input desktop observation cleanup failed.");
        }
    }

    internal static bool TryGetDesktopNameLength(ReadOnlySpan<char> buffer, uint returnedBytes, out int length)
    {
        length = 0;
        if (returnedBytes < 4 || returnedBytes > MaximumDesktopNameBytes
            || returnedBytes % sizeof(char) != 0 || returnedBytes / sizeof(char) > buffer.Length)
            return false;
        var count = (int)(returnedBytes / sizeof(char));
        if (buffer[count - 1] != '\0') return false;
        for (var index = 0; index < count - 1; index++)
        {
            var character = buffer[index];
            if (character == '\0' || char.IsLowSurrogate(character)) return false;
            if (char.IsHighSurrogate(character))
            {
                if (++index >= count - 1 || !char.IsLowSurrogate(buffer[index])) return false;
            }
        }
        length = count - 1;
        return true;
    }

    private static (bool Visible, bool? UserMatches) ReadStation(nint token, nint accountSid,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var station = GetProcessWindowStation(); // Borrowed; never CloseWindowStation.
        cancellationToken.ThrowIfCancellationRequested();
        if (station == 0) return (false, null);
        UserObjectFlags flags = default;
        cancellationToken.ThrowIfCancellationRequested();
        var queried = GetUserObjectInformationW(station, 1, &flags,
            (uint)sizeof(UserObjectFlags), out var bytes); // UOI_FLAGS.
        cancellationToken.ThrowIfCancellationRequested();
        if (queried == 0 || bytes != sizeof(UserObjectFlags) || (flags.Flags & 1) == 0)
            return (false, null); // WSF_VISIBLE applies to the station.

        var sid = stackalloc byte[MaximumSidBytes];
        new Span<byte>(sid, MaximumSidBytes).Clear();
        cancellationToken.ThrowIfCancellationRequested();
        queried = GetUserObjectInformationW(station, 4, sid,
            MaximumSidBytes, out bytes); // UOI_USER_SID, one bounded query without retry.
        cancellationToken.ThrowIfCancellationRequested();
        if (queried == 0 || bytes is < 8 or > MaximumSidBytes
            || sid[0] != 1 || sid[1] > 15 || 8 + 4 * sid[1] != bytes)
            return (true, null);
        if (!ValidSid((nint)sid, cancellationToken)) return (true, null);
        cancellationToken.ThrowIfCancellationRequested();
        var same = EqualSid(accountSid, (nint)sid);
        cancellationToken.ThrowIfCancellationRequested();
        if (same != 0) return (true, true);

        // A station can be associated with the logon-session identity rather than
        // the LSA account identity. Only compare that representation to the same token.
        var logonIdentity = IsWellKnownSid((nint)sid, LogonIdsSidType);
        cancellationToken.ThrowIfCancellationRequested();
        if (logonIdentity == 0) return (true, false);

        var groups = stackalloc byte[MaximumLogonSidBufferBytes];
        new Span<byte>(groups, MaximumLogonSidBufferBytes).Clear();
        cancellationToken.ThrowIfCancellationRequested();
        queried = GetTokenInformation(token, TokenLogonSidClass, groups,
            MaximumLogonSidBufferBytes, out bytes);
        cancellationToken.ThrowIfCancellationRequested();
        if (queried == 0 || bytes > MaximumLogonSidBufferBytes
            || !TryReadLogonSidOffset(new ReadOnlySpan<byte>(groups, (int)bytes),
                (ulong)groups, out var offset)) return (true, null);
        var ownLogonSid = (nint)(groups + offset);
        if (!ValidSid(ownLogonSid, cancellationToken)) return (true, null);
        cancellationToken.ThrowIfCancellationRequested();
        logonIdentity = IsWellKnownSid(ownLogonSid, LogonIdsSidType);
        cancellationToken.ThrowIfCancellationRequested();
        if (logonIdentity == 0) return (true, null);
        same = EqualSid(ownLogonSid, (nint)sid);
        cancellationToken.ThrowIfCancellationRequested();
        return (true, same != 0);
    }

    // Parse only the admitted x64 ABI. Check the complete pointed-to SID before
    // calling native SID functions; no pointer or identity leaves this observation.
    internal static bool TryReadLogonSidOffset(ReadOnlySpan<byte> buffer, ulong address,
        out int offset)
    {
        offset = 0;
        if (buffer.Length is < SingleGroupPrefixBytes or > MaximumLogonSidBufferBytes
            || address == 0 || address > ulong.MaxValue - (ulong)buffer.Length
            || BinaryPrimitives.ReadUInt32LittleEndian(buffer) != 1
            || (BinaryPrimitives.ReadUInt32LittleEndian(buffer[16..]) & 0xC0000000) != 0xC0000000)
            return false;
        var pointer = BinaryPrimitives.ReadUInt64LittleEndian(buffer[8..]);
        if (pointer < address) return false;
        var relative = pointer - address;
        if (relative < SingleGroupPrefixBytes || relative % 4 != 0
            || relative > (ulong)(buffer.Length - 8))
            return false;
        var sid = buffer[(int)relative..];
        if (sid[0] != 1 || sid[1] > 15 || 8 + 4 * sid[1] > sid.Length) return false;
        offset = (int)relative;
        return true;
    }

    private static bool ValidSid(nint sid, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sid == 0) return false;
        var valid = IsValidSid(sid);
        cancellationToken.ThrowIfCancellationRequested();
        return valid != 0;
    }

    private static WindowsLogonIdentity ClassifySid(nint sid, CancellationToken cancellationToken)
    {
        // These fixed well-known SID classes contain no account-specific data.
        foreach (var service in new[] { 22, 23, 24 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matched = IsWellKnownSid(sid, service);
            cancellationToken.ThrowIfCancellationRequested();
            if (matched != 0) return service switch
            {
                22 => WindowsLogonIdentity.LocalSystem,
                23 => WindowsLogonIdentity.LocalService,
                _ => WindowsLogonIdentity.NetworkService,
            };
        }
        return WindowsLogonIdentity.User;
    }

    private static void RequireWindows(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var windows = OperatingSystem.IsWindows();
        cancellationToken.ThrowIfCancellationRequested();
        if (!windows) throw new ProviderFailureException(AuthenticationFailure.MechanismUnavailable);
    }

    private static void CloseToken(nint token)
    {
        // Cleanup must still run after cancellation and must never export native text.
        if (token != 0 && CloseHandle(token) == 0)
            throw new InvalidOperationException("Local token observation cleanup failed.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { internal uint Low; internal int High; }

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    private struct TokenStatistics { [FieldOffset(8)] internal Luid AuthenticationId; }

    // The native structure is larger. Only this checked prefix is ever read; all
    // username/domain/package/string descriptors remain untouched.
    [StructLayout(LayoutKind.Explicit, Size = 80)]
    private struct LogonPrefix
    {
        [FieldOffset(0)] internal uint Size;
        [FieldOffset(4)] internal Luid LogonId;
        [FieldOffset(64)] internal uint LogonType;
        [FieldOffset(72)] internal nint Sid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 12)]
    private struct UserObjectFlags { [FieldOffset(8)] internal uint Flags; }

    [StructLayout(LayoutKind.Explicit, Size = 284)]
    private struct VersionInfo
    {
        [FieldOffset(0)] internal uint Size;
        [FieldOffset(282)] internal byte ProductType;
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial ulong VerSetConditionMask(ulong mask, uint type, byte condition);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int VerifyVersionInfoW(VersionInfo* version, uint type, ulong condition);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetCurrentThread();

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetCurrentProcessId();

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ProcessIdToSessionId(uint process, out uint session);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int OpenThreadToken(nint thread, uint access, int openAsSelf, out nint token);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetTokenInformation(nint token, uint informationClass,
        void* information, uint bytes, out uint returned);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsValidSid(nint sid);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWellKnownSid(nint sid, int type);

    [LibraryImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int EqualSid(nint first, nint second);

    [LibraryImport("secur32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int LsaGetLogonSessionData(Luid* logon, out nint data);

    [LibraryImport("secur32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int LsaFreeReturnBuffer(nint data);

    [LibraryImport("wtsapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WTSQuerySessionInformationW(nint server, uint session,
        int informationClass, out nint buffer, out uint bytes);

    [LibraryImport("wtsapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void WTSFreeMemory(nint buffer);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetProcessWindowStation();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetThreadDesktop(uint thread);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint OpenInputDesktop(uint flags, int inherit, uint access);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CloseDesktop(nint desktop);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetUserObjectInformationW(nint handle, int index,
        void* information, uint bytes, out uint returned);
}
