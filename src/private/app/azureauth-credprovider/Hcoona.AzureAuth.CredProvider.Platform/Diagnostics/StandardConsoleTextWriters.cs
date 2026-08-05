using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public static class StandardConsoleTextWriters
{
    public static TextWriter StandardOutput()
    {
        Console.OutputEncoding = Encoding.UTF8;
        return TextWriter.Synchronized(Console.Out);
    }

    public static TextWriter StandardError()
    {
        Console.OutputEncoding = Encoding.UTF8;
        return TextWriter.Synchronized(Console.Error);
    }
}
