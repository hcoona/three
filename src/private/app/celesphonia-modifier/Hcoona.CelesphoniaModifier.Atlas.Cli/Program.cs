namespace Hcoona.CelesphoniaModifier.Atlas.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using CancellationTokenSource cancellationTokenSource = new();
        ConsoleCancelEventHandler cancelKeyPressHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        Console.CancelKeyPress += cancelKeyPressHandler;
        try
        {
            Stream standardOutput = Console.OpenStandardOutput();
            Stream standardError = Console.OpenStandardError();
            return AtlasCliApplication.RunAsync(
                    args,
                    standardOutput,
                    standardError,
                    cancellationTokenSource.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= cancelKeyPressHandler;
        }
    }
}
