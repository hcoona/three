namespace Hcoona.QidianNovelDownloader;

internal interface IInteractiveConsole
{
    Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken);
}

internal sealed class SystemInteractiveConsole : IInteractiveConsole
{
    public Task<bool> ConfirmAsync(string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Console.IsInputRedirected)
        {
            return Task.FromResult(false);
        }

        Console.Write($"{prompt} [y/N]: ");
        string? response = Console.ReadLine();
        return Task.FromResult(
            string.Equals(response, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase));
    }
}
