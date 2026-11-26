using System.Runtime.InteropServices;
using Authentication.Core;

namespace Authentication.Windows.Scenarios;

// Child-only native checks. The child changes only its own loader and console
// state, and writes files only into the fresh working directory it was given.
internal static partial class NativeProcessChild
{
    internal const string RestrictSearch = "native-restrict-search";
    internal const string ConsoleBreak = "native-console-break";
    internal const int ProbeUnreachable = 11;
    private const string Probe = "synthetic-search-probe.dll";
    private const int ModuleNotFound = 126, BadImage = 193;

    internal static bool Supports(string scenario) => scenario is RestrictSearch or ConsoleBreak;

    internal static int Run(string scenario, string[] arguments, long entry)
    {
        if (arguments.Length != 1 || !Path.IsPathFullyQualified(arguments[0])) return 90;
        var directory = arguments[0];
        return scenario switch
        {
            RestrictSearch => RunRestrictSearch(directory),
            ConsoleBreak => RunConsoleBreak(directory, entry),
            _ => 90,
        };
    }

    private static int RunRestrictSearch(string directory)
    {
        // Suppress any loader error box; this child owns no window or dialog.
        _ = SetThreadErrorMode(0x0001 | 0x8000, out _);
        File.WriteAllText(Path.Combine(directory, Probe), "SYNTHETIC NON-IMAGE PROBE\n");
        if (TryLoad() != BadImage) return ProbeUnreachable;
        try { WindowsLoader.RestrictSearch(CancellationToken.None); }
        catch (Exception) { return 12; }
        return TryLoad() == ModuleNotFound ? 0 : 13;
    }

    private static int RunConsoleBreak(string directory, long entry)
    {
        var profile = Path.Combine(directory, "profile.json");
        File.WriteAllText(profile, ProcessChild.Profile);
        var provider = new PendingProvider();
        new Thread(() =>
        {
            // CTRL_BREAK_EVENT to every process on this child's own console.
            if (provider.Ready.Wait(TimeSpan.FromSeconds(10))) _ = GenerateConsoleCtrlEvent(1, 0);
        }) { IsBackground = true }.Start();
        return WindowsProcess.Run([
            "authenticate", "--protocol", "1", "--profile", profile,
            "--account-email", ProcessChild.Email, "--scope", ProcessChild.Scope,
            "--interaction", "non-interactive-only", "--tenant", ProcessChild.Tenant,
            "--timeout-seconds", "20",
        ], entry, new Host(), _ => provider);
    }

    private static int TryLoad()
    {
        var module = LoadLibraryW(Probe);
        if (module == 0) return Marshal.GetLastPInvokeError();
        FreeLibrary(module);
        return 0;
    }

    private sealed class Host : IRequestHost
    {
        public TimeProvider Clock => TimeProvider.System;
        public Task<nint> OpenInteractionAsync(CancellationToken cancellationToken) => Task.FromResult<nint>(0);
        public Task CloseInteractionAsync() => Task.CompletedTask;
    }

    private sealed class PendingProvider : IAuthenticationProvider
    {
        internal ManualResetEventSlim Ready { get; } = new();

        public Task<IReadOnlyList<ProviderAccount>> GetAccountsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProviderAccount>>([new(ProcessChild.Email, new object())]);

        public async Task<TokenCandidate> AcquireSilentAsync(AuthenticationRequest request, ProviderAccount account,
            Guid operationId, CancellationToken cancellationToken)
        {
            Ready.Set();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable synthetic completion.");
        }

        public Task<TokenCandidate> AcquireInteractiveAsync(AuthenticationRequest request, ProviderAccount? account,
            string? claims, nint parentWindow, Guid operationId, CancellationToken cancellationToken) =>
            Task.FromException<TokenCandidate>(new ProviderFailureException(AuthenticationFailure.MechanismUnavailable));
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint LoadLibraryW(string name);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FreeLibrary(nint module);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetThreadErrorMode(uint mode, out uint previous);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GenerateConsoleCtrlEvent(uint controlEvent, uint processGroup);
}
