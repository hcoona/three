namespace Hcoona.DocumentTranslatorCli;

internal static class Program
{
    private const int PlaceholderFailureExitCode = 1;

    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        Console.Error.WriteLine("Document Translator CLI placeholder is not available yet.");
        return PlaceholderFailureExitCode;
    }
}
