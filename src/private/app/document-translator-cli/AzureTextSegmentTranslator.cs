using System.Text;

namespace Hcoona.DocumentTranslatorCli;

internal sealed class AzureTextSegmentTranslator : ITextSegmentTranslator, IDisposable
{
    internal const int MaximumItemsPerBatch = 100;
    internal const int MaximumScalarsPerBatch = 50_000;
    private readonly AzureTextTranslationClient client;
    private readonly bool ownsClient;
    private bool disposed;

    public AzureTextSegmentTranslator()
        : this(new AzureTextTranslationClient(), ownsClient: true)
    {
    }

    internal AzureTextSegmentTranslator(AzureTextTranslationClient client)
        : this(client, ownsClient: false)
    {
    }

    internal AzureTextSegmentTranslator(AzureTextTranslationClient client, bool ownsClient)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.ownsClient = ownsClient;
    }

    public async ValueTask<IReadOnlyList<string>> TranslateAsync(
        TranslationOptions options,
        IReadOnlyList<TextSegmentTranslationRequest> segments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(segments);
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (segments.Count == 0)
        {
            return [];
        }

        TextSegmentTranslationRequest[] orderedSegments =
            [.. segments.OrderBy(static s => s.SegmentIndex)];
        ValidateSegmentIndexes(orderedSegments);

        string[] translatedBySegmentIndex = new string[orderedSegments.Length];
        List<TextSegmentTranslationRequest> batch = new(MaximumItemsPerBatch);
        int batchScalars = 0;

        foreach (TextSegmentTranslationRequest segment in orderedSegments)
        {
            int segmentScalars = CountUnicodeScalars(segment.Text);
            if (segmentScalars > MaximumScalarsPerBatch)
            {
                throw new InvalidOperationException(
                    "A text segment exceeds the Azure Text Translation scalar limit.");
            }

            if (batch.Count > 0
                && (batch.Count == MaximumItemsPerBatch
                    || batchScalars + segmentScalars > MaximumScalarsPerBatch))
            {
                await TranslateBatchAsync(
                        options,
                        batch,
                        translatedBySegmentIndex,
                        cancellationToken)
                    .ConfigureAwait(false);
                batch.Clear();
                batchScalars = 0;
            }

            batch.Add(segment);
            batchScalars += segmentScalars;
        }

        if (batch.Count > 0)
        {
            await TranslateBatchAsync(options, batch, translatedBySegmentIndex, cancellationToken)
                .ConfigureAwait(false);
        }

        string[] result = new string[orderedSegments.Length];
        for (int i = 0; i < orderedSegments.Length; i++)
        {
            result[i] = translatedBySegmentIndex[orderedSegments[i].SegmentIndex];
        }

        return result;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (ownsClient)
        {
            client.Dispose();
        }

        disposed = true;
    }

    internal static int CountUnicodeScalars(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int count = 0;
        foreach (Rune _ in text.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private static void ValidateSegmentIndexes(TextSegmentTranslationRequest[] segments)
    {
        for (int i = 0; i < segments.Length; i++)
        {
            TextSegmentTranslationRequest segment = segments[i];
            if (segment.SegmentIndex < 0)
            {
                throw new InvalidOperationException("Segment indexes must be non-negative.");
            }

            if (segment.SegmentIndex != i)
            {
                throw new InvalidOperationException(
                    "Segment indexes must be contiguous and start at zero.");
            }
        }
    }

    private async ValueTask TranslateBatchAsync(
        TranslationOptions options,
        List<TextSegmentTranslationRequest> batch,
        string[] translatedBySegmentIndex,
        CancellationToken cancellationToken)
    {
        string[] texts = new string[batch.Count];
        for (int i = 0; i < batch.Count; i++)
        {
            texts[i] = batch[i].Text;
        }

        IReadOnlyList<string> translations = await client
            .TranslateAsync(options, texts, cancellationToken)
            .ConfigureAwait(false);
        if (translations.Count != batch.Count)
        {
            throw new TextTranslationServiceException(
                "Azure Text Translation service returned an unexpected result count.");
        }

        for (int i = 0; i < translations.Count; i++)
        {
            translatedBySegmentIndex[batch[i].SegmentIndex] = translations[i];
        }
    }
}
