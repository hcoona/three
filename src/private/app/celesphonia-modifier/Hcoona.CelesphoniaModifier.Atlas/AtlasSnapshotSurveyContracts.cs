using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.CelesphoniaModifier.Atlas;

public sealed record AtlasSnapshotSurveyRequest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string RepositoryRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string RunId { get; init; } = string.Empty;

    [JsonRequired]
    public string SnapshotReceiptPath { get; init; } = string.Empty;
}

public sealed record AtlasSnapshotSurveyDocument(
    string CopiedSaveRelativePath,
    AtlasDocumentRole DocumentRole,
    string ScanRelativePath,
    long CopiedSourceByteLength,
    string CopiedSourceSha256,
    long PersistedScanByteLength,
    string PersistedScanSha256,
    AtlasStructuralScanCensus Census);

public sealed record AtlasSnapshotSurveyTotals(
    long DocumentCount,
    long CopiedSourceBytes,
    long CanonicalScanBytes,
    long NodeOccurrences,
    long ObjectOccurrences,
    long ArrayOccurrences,
    long ScalarOccurrences,
    long ReferenceOccurrences,
    long OrdinaryMemberEdges,
    long ArrayElementEdges,
    long IdentityDefinitions,
    long ClassMarkers,
    long IdentityArrayWrappers,
    long DistinctReferencedDefinitions);

public sealed class AtlasSnapshotSurveyManifest
{
    public const string CurrentSchemaVersion = "atlas-snapshot-survey/v1";

    private readonly AtlasSnapshotSurveyDocument[] documents;
    private readonly IReadOnlyList<AtlasSnapshotSurveyDocument> readOnlyDocuments;

    public AtlasSnapshotSurveyManifest(
        IEnumerable<AtlasSnapshotSurveyDocument> documents,
        AtlasSnapshotSurveyTotals totals)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Totals = totals ?? throw new ArgumentNullException(nameof(totals));
        this.documents = [.. documents];
        readOnlyDocuments = Array.AsReadOnly(this.documents);
    }

    public string SchemaVersion { get; } = CurrentSchemaVersion;

    public IReadOnlyList<AtlasSnapshotSurveyDocument> Documents => readOnlyDocuments;

    public AtlasSnapshotSurveyTotals Totals { get; }
}

public sealed record AtlasSnapshotSurveyLimits
{
    public static AtlasSnapshotSurveyLimits Default { get; } = new();

    public int MaximumDocuments { get; init; } = 22;

    public long MaximumObservations { get; init; } = 2_000_000;

    public long MaximumCanonicalScanBytes { get; init; } = 512L * 1024 * 1024;

    public int MaximumManifestBytes { get; init; } = 256 * 1024;

    internal void Validate()
    {
        if (MaximumDocuments < 1
            || MaximumDocuments > AtlasSaveSnapshotContracts.MaximumReceiptEntries
            || MaximumObservations < 1
            || MaximumCanonicalScanBytes < 1
            || MaximumManifestBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AtlasSnapshotSurveyLimits),
                "Snapshot survey limits are out of range.");
        }
    }
}

public static class AtlasSnapshotSurveyContracts
{
    public const string RequestSchemaVersion = "atlas-snapshot-survey-request/v1";
    public const string ManifestFileName = "snapshot-survey-manifest.json";
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumRequestTokens = 256;
    public const int MaximumJsonDepth = 8;
    public const int MaximumStringLength = 32_768;
    public const int MaximumNumericTokenLength = 20;

    internal static async ValueTask<AtlasSnapshotSurveyRequest> ReadRequestAsync(
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
                    MaximumRequestBytes,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateJsonEnvelope(bytes, MaximumRequestTokens, cancellationToken);
            AtlasSnapshotSurveyRequest? request = JsonSerializer.Deserialize(
                bytes,
                AtlasSnapshotSurveyJsonContext.Default.AtlasSnapshotSurveyRequest);
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
            throw new AtlasRequestException("The snapshot survey request is invalid.", exception);
        }
    }

    internal static AtlasSnapshotSurveyLayout CreateLayout(
        AtlasSnapshotSurveyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        string repositoryRoot =
            AtlasSaveSnapshotContracts.NormalizeAbsolutePath(request.RepositoryRoot);
        string privateParent = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier",
                ".private",
                "atlas-snapshot-survey"));
        string workspaceRoot = Path.GetFullPath(Path.Combine(privateParent, request.RunId));
        string incompleteRoot = Path.GetFullPath(
            Path.Combine(workspaceRoot, "survey.incomplete"));
        string finalRoot = Path.GetFullPath(Path.Combine(workspaceRoot, "survey"));
        return new AtlasSnapshotSurveyLayout(
            repositoryRoot,
            privateParent,
            workspaceRoot,
            incompleteRoot,
            finalRoot,
            Path.Combine(incompleteRoot, ManifestFileName),
            Path.Combine(finalRoot, ManifestFileName));
    }

    internal static async ValueTask<byte[]> ReadBoundedAsync(
        string path,
        int maximumBytes,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        await using Stream stream = io.OpenFile(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using MemoryStream destination = new(Math.Min(maximumBytes, 16 * 1024));
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
            if (total > maximumBytes)
            {
                throw new JsonException("The JSON document exceeds its byte limit.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    internal static void ValidateJsonEnvelope(
        ReadOnlySpan<byte> bytes,
        int maximumTokens,
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
            if (++tokens > maximumTokens)
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

    internal static void ValidateRequest(AtlasSnapshotSurveyRequest request)
    {
        if (!StringComparer.Ordinal.Equals(request.SchemaVersion, RequestSchemaVersion))
        {
            throw new ArgumentException("The request schema version is invalid.");
        }

        _ = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(request.RepositoryRoot);
        _ = AtlasSaveSnapshotContracts.NormalizeAbsolutePath(request.SnapshotReceiptPath);
        AtlasSaveSnapshotContracts.ValidateRunId(request.RunId);
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

internal sealed record AtlasSnapshotSurveyLayout(
    string RepositoryRoot,
    string PrivateParent,
    string WorkspaceRoot,
    string IncompleteRoot,
    string FinalRoot,
    string IncompleteManifestPath,
    string FinalManifestPath);

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = false)]
[JsonSerializable(typeof(AtlasSnapshotSurveyRequest))]
internal sealed partial class AtlasSnapshotSurveyJsonContext : JsonSerializerContext;
