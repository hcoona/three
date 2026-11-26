using Authentication.Windows;

namespace Authentication.Cli;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        var entry = TimeProvider.System.GetTimestamp();
        return WindowsProcess.Run(arguments, entry);
    }
}
