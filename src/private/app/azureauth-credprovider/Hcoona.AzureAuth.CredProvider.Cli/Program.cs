namespace Hcoona.AzureAuth.CredProvider.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        return CliApplication.Run(args, Console.Out, Console.Error);
    }
}
