namespace Hcoona.DocumentTranslatorCli;

internal static class TranslationCommand
{
    public static async ValueTask<BinaryData> TranslateValidatedInputAsync(
        TranslationOptions options,
        IDocumentTranslator translator,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(translator);
        cancellationToken.ThrowIfCancellationRequested();

        await using FileStream inputStream = new(
            options.InputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await translator
            .TranslateAsync(options, inputStream, cancellationToken)
            .ConfigureAwait(false);
    }
}
