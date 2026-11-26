using System.Runtime.InteropServices;
using Authentication.Core;

namespace Authentication.Windows;

internal static partial class WindowsLoader
{
    internal static void RestrictSearch(CancellationToken cancellationToken) => RestrictSearch(cancellationToken, null);

    internal static void RestrictSearch(CancellationToken cancellationToken, WindowsMechanismTrace? trace)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var succeeded = SetDefaultDllDirectories(0x00000200 | 0x00000800);
        cancellationToken.ThrowIfCancellationRequested();
        if (succeeded == 0)
        {
            trace?.Record(WindowsMechanismFailure.DllSearch);
            throw new ProviderFailureException(AuthenticationFailure.MechanismUnavailable);
        }
    }

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetDefaultDllDirectories(uint directoryFlags);
}
