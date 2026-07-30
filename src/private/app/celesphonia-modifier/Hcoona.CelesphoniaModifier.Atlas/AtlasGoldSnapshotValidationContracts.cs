using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.CelesphoniaModifier.Atlas;

public sealed record AtlasGoldSnapshotValidationRequest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string RepositoryRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string SnapshotReceiptPath { get; init; } = string.Empty;
}

public enum AtlasGoldSnapshotValidationState
{
    AllConsistent,
    DisagreementObserved,
    IncompleteObserved,
    DisagreementAndIncompleteObserved,
}

public sealed class AtlasGoldSnapshotValidationSummary
{
    private AtlasGoldSnapshotValidationSummary(
        int totalSlots,
        int consistent,
        int disagree,
        int incomplete)
    {
        TotalSlots = totalSlots;
        Consistent = consistent;
        Disagree = disagree;
        Incomplete = incomplete;
        State = disagree > 0
            ? incomplete > 0
                ? AtlasGoldSnapshotValidationState.DisagreementAndIncompleteObserved
                : AtlasGoldSnapshotValidationState.DisagreementObserved
            : incomplete > 0
                ? AtlasGoldSnapshotValidationState.IncompleteObserved
                : AtlasGoldSnapshotValidationState.AllConsistent;
    }

    public int TotalSlots { get; }

    public int Consistent { get; }

    public int Disagree { get; }

    public int Incomplete { get; }

    public AtlasGoldSnapshotValidationState State { get; }

    internal static AtlasGoldSnapshotValidationSummary Create(
        int totalSlots,
        int consistent,
        int disagree,
        int incomplete)
    {
        if (totalSlots is < 1 or > 20
            || consistent is < 0 or > 20
            || disagree is < 0 or > 20
            || incomplete is < 0 or > 20
            || totalSlots != consistent + disagree + incomplete)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalSlots),
                "Gold snapshot validation counts are invalid.");
        }

        return new AtlasGoldSnapshotValidationSummary(
            totalSlots,
            consistent,
            disagree,
            incomplete);
    }
}

public static class AtlasGoldSnapshotValidationContracts
{
    public const string RequestSchemaVersion =
        "atlas-gold-snapshot-validation-request/v1";
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumRequestTokens = 256;
    public const int MaximumJsonDepth = 8;
    public const int MaximumStringLength = 32_768;
    public const int MaximumNumericTokenLength = 20;

    internal static async ValueTask<AtlasGoldSnapshotValidationRequest> ReadRequestAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);

        try
        {
            byte[] bytes = await ReadBoundedAsync(
                    Path.GetFullPath(requestPath),
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateJsonEnvelope(bytes, cancellationToken);
            AtlasGoldSnapshotValidationRequest? request = JsonSerializer.Deserialize(
                bytes,
                AtlasGoldSnapshotValidationJsonContext
                    .Default
                    .AtlasGoldSnapshotValidationRequest);
            ValidateRequest(request ?? throw new JsonException("The request is null."));
            return request;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
            or ArgumentException
            or NotSupportedException)
        {
            throw new AtlasRequestException(
                "The Gold snapshot validation request is invalid.",
                exception);
        }
    }

    internal static void ValidateRequest(AtlasGoldSnapshotValidationRequest request)
    {
        if (!StringComparer.Ordinal.Equals(request.SchemaVersion, RequestSchemaVersion))
        {
            throw new ArgumentException("The request schema version is invalid.");
        }

        _ = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(request.RepositoryRoot);
        _ = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(request.SnapshotReceiptPath);
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(
        string path,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        await using Stream stream = io.OpenFile(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using MemoryStream destination = new(Math.Min(MaximumRequestBytes, 16 * 1024));
        byte[] buffer = new byte[8192];
        int total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            total = checked(total + read);
            if (total > MaximumRequestBytes)
            {
                throw new JsonException("The JSON document exceeds its byte limit.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static void ValidateJsonEnvelope(
        ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken)
    {
        Utf8JsonReader reader = new(
            bytes,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumJsonDepth,
            });
        Stack<HashSet<string>> objects = [];
        int tokens = 0;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++tokens > MaximumRequestTokens)
            {
                throw new JsonException("The JSON document exceeds its token limit.");
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objects.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    if (objects.Count == 0)
                    {
                        throw new JsonException("The JSON object is invalid.");
                    }

                    objects.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    string propertyName = GetString(ref reader);
                    ValidateString(propertyName);
                    if (objects.Count == 0 || !objects.Peek().Add(propertyName))
                    {
                        throw new JsonException("Duplicate JSON properties are invalid.");
                    }

                    break;
                case JsonTokenType.String:
                    ValidateString(GetString(ref reader));
                    break;
                case JsonTokenType.Number:
                    long numericLength = reader.HasValueSequence
                        ? reader.ValueSequence.Length
                        : reader.ValueSpan.Length;
                    if (numericLength > MaximumNumericTokenLength)
                    {
                        throw new JsonException("A numeric token exceeds its length limit.");
                    }

                    break;
                case JsonTokenType.Null:
                    throw new JsonException("Explicit null is invalid.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (tokens == 0 || objects.Count != 0)
        {
            throw new JsonException("The JSON document is incomplete.");
        }
    }

    private static string GetString(ref Utf8JsonReader reader)
    {
        try
        {
            return reader.GetString() ?? throw new JsonException("A JSON string is invalid.");
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException("A JSON string is invalid.", exception);
        }
    }

    private static void ValidateString(string value)
    {
        if (value.Length > MaximumStringLength)
        {
            throw new JsonException("A JSON string exceeds its length limit.");
        }
    }
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = false)]
[JsonSerializable(typeof(AtlasGoldSnapshotValidationRequest))]
internal sealed partial class AtlasGoldSnapshotValidationJsonContext : JsonSerializerContext;
