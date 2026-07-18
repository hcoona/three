namespace Hcoona.CelesphoniaModifier.Atlas;

public static class EmptyAtlasSurvey
{
    public const string SchemaVersion = "atlas-empty-survey/v1";

    private static readonly byte[] DocumentBytes =
        "{\"schemaVersion\":\"atlas-empty-survey/v1\",\"observations\":[]}\n"u8.ToArray();

    public static async ValueTask WriteAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        if (!destination.CanWrite)
        {
            throw new NotSupportedException("The destination stream does not support writing.");
        }

        await destination.WriteAsync(DocumentBytes, cancellationToken).ConfigureAwait(false);
    }
}
