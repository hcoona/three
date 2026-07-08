namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public static class StandardConsoleTextWriters
{
    public static TextWriter StandardOutput() => StandardConsoleTextWriter.StandardOutput();

    public static TextWriter StandardError() => StandardConsoleTextWriter.StandardError();
}
