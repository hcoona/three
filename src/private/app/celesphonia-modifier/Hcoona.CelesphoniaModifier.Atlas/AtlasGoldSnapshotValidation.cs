using System.Buffers;
using System.Security.Cryptography;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasGoldSnapshotValidation
{
    public static ValueTask<AtlasGoldSnapshotValidationSummary> RunAsync(
        string requestFilePath,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            requestFilePath,
            AtlasIoSeams.Default,
            AtlasSaveReaderLimits.Default,
            AtlasGoldSnapshotValidationSeams.Default,
            cancellationToken);

    internal static async ValueTask<AtlasGoldSnapshotValidationSummary> RunAsync(
        string requestPath,
        AtlasIoSeams io,
        AtlasSaveReaderLimits readerLimits,
        CancellationToken cancellationToken) =>
        await RunAsync(
                requestPath,
                io,
                readerLimits,
                AtlasGoldSnapshotValidationSeams.Default,
                cancellationToken)
            .ConfigureAwait(false);

    internal static async ValueTask<AtlasGoldSnapshotValidationSummary> RunAsync(
        string requestPath,
        AtlasIoSeams io,
        AtlasSaveReaderLimits readerLimits,
        AtlasGoldSnapshotValidationSeams seams,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(readerLimits);
        ArgumentNullException.ThrowIfNull(seams);
        readerLimits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        AtlasGoldSnapshotValidationRequest request =
            await AtlasGoldSnapshotValidationContracts.ReadRequestAsync(
                    requestPath,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        AtlasValidatedSaveSnapshot snapshot =
            await AtlasFinalizedSaveSnapshot.OpenBoundedAsync(
                    request.RepositoryRoot,
                    request.SnapshotReceiptPath,
                    io,
                    readerLimits.MaximumEncodedBytes,
                    cancellationToken)
                .ConfigureAwait(false);

        int totalSlots = 0;
        int consistent = 0;
        int disagree = 0;
        int incomplete = 0;
        foreach (AtlasValidatedSaveSnapshotEntry entry in snapshot.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSlotEntry(entry))
            {
                continue;
            }

            byte[] bytes = await ReadVerifiedBytesAsync(
                    entry,
                    io,
                    readerLimits.MaximumEncodedBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            AtlasSaveReadResult source = seams.ReadSave(
                bytes,
                readerLimits,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            AtlasGoldReadModelResult result = seams.ReadGold(
                source,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            switch (result.Aggregate)
            {
                case AtlasGoldAggregateState.Consistent:
                    consistent = checked(consistent + 1);
                    break;
                case AtlasGoldAggregateState.Disagree:
                    disagree = checked(disagree + 1);
                    break;
                case AtlasGoldAggregateState.Incomplete:
                    incomplete = checked(incomplete + 1);
                    break;
                default:
                    throw new InvalidOperationException(
                        "The Gold aggregate state is unsupported.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            totalSlots = checked(totalSlots + 1);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (totalSlots == 0)
        {
            throw new AtlasSafetyException(
                "The finalized snapshot contains no slot saves.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldSnapshotValidationSummary summary = seams.CreateSummary(
            totalSlots,
            consistent,
            disagree,
            incomplete);
        cancellationToken.ThrowIfCancellationRequested();
        return summary;
    }

    private static bool IsSlotEntry(AtlasValidatedSaveSnapshotEntry entry) =>
        entry.Order is >= 2 and <= 21
        && AtlasSaveSnapshot.TryGetCanonicalName(
            entry.RelativePath,
            out string canonicalName,
            out int order)
        && order == entry.Order
        && StringComparer.Ordinal.Equals(canonicalName, entry.RelativePath);

    private static async ValueTask<byte[]> ReadVerifiedBytesAsync(
        AtlasValidatedSaveSnapshotEntry entry,
        AtlasIoSeams io,
        int maximumEncodedBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using Stream stream = io.OpenFile(
            entry.AbsolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using MemoryStream destination = new(Math.Min(maximumEncodedBytes, 64 * 1024));
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long length = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long remaining = maximumEncodedBytes - length;
                int requestedBytes = remaining >= buffer.Length
                    ? buffer.Length
                    : checked((int)remaining + 1);
                int read = await stream.ReadAsync(
                        buffer.AsMemory(0, requestedBytes),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                length = checked(length + read);
                if (length > maximumEncodedBytes)
                {
                    throw new AtlasSafetyException(
                        "A validated snapshot copy changed during reading.");
                }

                hash.AppendData(buffer, 0, read);
                destination.Write(buffer, 0, read);
            }
        }

        catch (OverflowException exception)
        {
            throw new AtlasSafetyException(
                "A validated snapshot copy changed during reading.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
        long postReadLength = io.GetLength(entry.AbsolutePath);
        if (length != entry.Length
            || postReadLength != entry.Length
            || stream.CanSeek && stream.Length != length
            || !StringComparer.Ordinal.Equals(sha256, entry.Sha256))
        {
            throw new AtlasSafetyException(
                "A validated snapshot copy changed during reading.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return destination.ToArray();
    }
}

internal sealed class AtlasGoldSnapshotValidationSeams
{
    public static AtlasGoldSnapshotValidationSeams Default { get; } = new();

    public Func<
        ReadOnlyMemory<byte>,
        AtlasSaveReaderLimits,
        CancellationToken,
        AtlasSaveReadResult> ReadSave
    { get; init; } =
        static (bytes, limits, cancellationToken) =>
            AtlasSaveReader.Read(bytes, limits, cancellationToken);

    public Func<
        AtlasSaveReadResult,
        CancellationToken,
        AtlasGoldReadModelResult> ReadGold
    { get; init; } =
        static (source, cancellationToken) =>
            AtlasGoldReadModel.Read(source, cancellationToken);

    public Func<
        int,
        int,
        int,
        int,
        AtlasGoldSnapshotValidationSummary> CreateSummary
    { get; init; } =
        AtlasGoldSnapshotValidationSummary.Create;
}
