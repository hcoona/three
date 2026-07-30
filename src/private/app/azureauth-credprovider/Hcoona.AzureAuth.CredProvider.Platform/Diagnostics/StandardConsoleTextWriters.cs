namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public static class StandardConsoleTextWriters
{
    public static TextWriter StandardOutput() => TextWriter.Synchronized(Console.Out);

    public static TextWriter StandardError() => TextWriter.Synchronized(Console.Error);
}
