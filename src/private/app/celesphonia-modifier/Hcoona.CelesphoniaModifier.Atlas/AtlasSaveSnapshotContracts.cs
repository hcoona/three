using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasSaveSnapshotContracts
{
    public const string RequestSchemaVersion = "atlas-save-snapshot-request/v1";
    public const string ReceiptSchemaVersion = "atlas-save-snapshot-receipt/v1";
    public const string ReceiptFileName = "save-snapshot-receipt.json";
    public const int MaximumRequestBytes = 64 * 1024;
    public const int MaximumReceiptBytes = 256 * 1024;
    public const int MaximumStringLength = 32_768;
    public const int MaximumNumericTokenLength = 20;
    public const int MaximumJsonTokens = 512;
    public const int MaximumReceiptEntries = 22;
    public const int MaximumJsonDepth = 8;

    internal static async ValueTask<AtlasSaveSnapshotRequest> ReadRequestAsync(
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
            ValidateJsonEnvelope(bytes, receipt: false);
            AtlasSaveSnapshotRequest? request = JsonSerializer.Deserialize(
                bytes,
                AtlasSaveSnapshotJsonContext.Default.AtlasSaveSnapshotRequest);
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
            throw new AtlasRequestException("The save snapshot request is invalid.", exception);
        }
    }

    internal static async ValueTask<AtlasSaveSnapshotReceipt> ReadReceiptAsync(
        string receiptPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
        ArgumentNullException.ThrowIfNull(io);

        try
        {
            byte[] bytes = await ReadBoundedAsync(
                    receiptPath,
                    MaximumReceiptBytes,
                    io,
                    cancellationToken)
                .ConfigureAwait(false);
            ValidateJsonEnvelope(bytes, receipt: true);
            AtlasSaveSnapshotReceipt? receipt = JsonSerializer.Deserialize(
                bytes,
                AtlasSaveSnapshotJsonContext.Default.AtlasSaveSnapshotReceipt);
            ValidateReceipt(receipt ?? throw new JsonException("The receipt is null."));
            return receipt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
            or ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new AtlasSafetyException(
                "The save snapshot receipt is invalid.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
    }

    internal static byte[] SerializeReceipt(AtlasSaveSnapshotReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateReceipt(receipt);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            receipt,
            AtlasSaveSnapshotJsonContext.Default.AtlasSaveSnapshotReceipt);
        if (bytes.Length > MaximumReceiptBytes)
        {
            throw new AtlasSafetyException("The save snapshot receipt exceeds its size limit.");
        }

        return bytes;
    }

    internal static AtlasSaveSnapshotLayout CreateLayout(AtlasSaveSnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        string repositoryRoot = NormalizeAbsolutePath(request.RepositoryRoot);
        string privateParent = Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier",
                ".private",
                "atlas-save-snapshot"));
        string workspaceRoot = Path.GetFullPath(Path.Combine(privateParent, request.RunId));
        string incompleteRoot = Path.GetFullPath(
            Path.Combine(workspaceRoot, "save-snapshot.incomplete"));
        string finalRoot = Path.GetFullPath(Path.Combine(workspaceRoot, "save-snapshot"));
        return new AtlasSaveSnapshotLayout(
            repositoryRoot,
            privateParent,
            workspaceRoot,
            incompleteRoot,
            finalRoot,
            Path.Combine(incompleteRoot, ReceiptFileName),
            Path.Combine(finalRoot, ReceiptFileName));
    }

    internal static string NormalizeAbsolutePath(string path)
    {
        if (OperatingSystem.IsWindows() && IsWindowsDevicePath(path))
        {
            throw new ArgumentException(
                "Windows device paths are not valid save snapshot paths.",
                nameof(path));
        }

        return AtlasDefinitionIntakeContracts.NormalizeAbsolutePath(path);
    }

    internal static bool PathEquals(string first, string second) =>
        string.Equals(
            NormalizeAbsolutePath(first),
            NormalizeAbsolutePath(second),
            PathComparison);

    internal static bool ContainsPath(string root, string candidate)
    {
        string normalizedRoot = NormalizeAbsolutePath(root);
        string normalizedCandidate = NormalizeAbsolutePath(candidate);
        char finalRootCharacter = normalizedRoot[^1];
        string rootPrefix = finalRootCharacter == Path.DirectorySeparatorChar
            || finalRootCharacter == Path.AltDirectorySeparatorChar
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return string.Equals(normalizedRoot, normalizedCandidate, PathComparison)
            || normalizedCandidate.StartsWith(rootPrefix, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool IsWindowsDevicePath(string path)
    {
        if (path.Length < 4)
        {
            return false;
        }

        bool IsSeparator(char value) => value is '\\' or '/';
        return IsSeparator(path[0])
            && ((IsSeparator(path[1])
                    && path[2] is '?' or '.'
                    && IsSeparator(path[3]))
                || (path[1] == '?'
                    && path[2] == '?'
                    && IsSeparator(path[3])));
    }

    internal static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathFullyQualified(path)
            || path.Contains(':')
            || path.Contains('\\'))
        {
            throw new ArgumentException("The snapshot relative path is invalid.", nameof(path));
        }

        string[] segments = path.Split('/', StringSplitOptions.None);
        if (segments.Any(static segment =>
                segment.Length == 0
                || StringComparer.Ordinal.Equals(segment, ".")
                || StringComparer.Ordinal.Equals(segment, "..")))
        {
            throw new ArgumentException("The snapshot relative path is invalid.", nameof(path));
        }

        return string.Join("/", segments);
    }

    internal static void ValidateRunId(string runId)
    {
        if (runId.Length != 32
            || runId.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("The run ID is invalid.", nameof(runId));
        }
    }

    internal static void ValidateLowerSha256(string sha256)
    {
        if (sha256.Length != 64
            || sha256.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("The SHA-256 value is invalid.", nameof(sha256));
        }
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(
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

    private static void ValidateJsonEnvelope(ReadOnlyMemory<byte> bytes, bool receipt)
    {
        Utf8JsonReader reader = new(
            bytes.Span,
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
            if (++tokens > MaximumJsonTokens)
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
                    string propertyName = GetJsonString(
                        ref reader,
                        "The property name is invalid.");
                    ValidateString(propertyName);
                    if (objects.Count == 0 || !objects.Peek().Add(propertyName))
                    {
                        throw new JsonException("Duplicate JSON properties are invalid.");
                    }

                    break;
                case JsonTokenType.String:
                    ValidateString(
                        GetJsonString(ref reader, "The string value is invalid."));
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

        if (tokens == 0 || objects.Count != 0)
        {
            throw new JsonException("The JSON document is incomplete.");
        }

        using JsonDocument document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MaximumJsonDepth,
            });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The JSON document must be an object.");
        }

        if (receipt
            && document.RootElement.TryGetProperty("entries", out JsonElement entries)
            && (entries.ValueKind != JsonValueKind.Array
                || entries.GetArrayLength() > MaximumReceiptEntries))
        {
            throw new JsonException("The receipt entry count is invalid.");
        }
    }

    private static string GetJsonString(
        ref Utf8JsonReader reader,
        string invalidMessage)
    {
        try
        {
            return reader.GetString() ?? throw new JsonException(invalidMessage);
        }
        catch (InvalidOperationException exception)
        {
            throw new JsonException(invalidMessage, exception);
        }
    }

    private static void ValidateString(string value)
    {
        if (value.Length > MaximumStringLength)
        {
            throw new JsonException("A JSON string exceeds its length limit.");
        }
    }

    private static void ValidateRequest(AtlasSaveSnapshotRequest request)
    {
        if (!StringComparer.Ordinal.Equals(request.SchemaVersion, RequestSchemaVersion))
        {
            throw new ArgumentException("The request schema version is invalid.");
        }

        _ = NormalizeAbsolutePath(request.RepositoryRoot);
        _ = NormalizeAbsolutePath(request.SaveRoot);
        ValidateRunId(request.RunId);
    }

    private static void ValidateReceipt(AtlasSaveSnapshotReceipt receipt)
    {
        if (!StringComparer.Ordinal.Equals(receipt.SchemaVersion, ReceiptSchemaVersion))
        {
            throw new ArgumentException("The receipt schema version is invalid.");
        }

        ValidateRunId(receipt.RunId);
        _ = NormalizeAbsolutePath(receipt.SaveRoot);
        _ = NormalizeAbsolutePath(receipt.FinalSnapshotRoot);
        if (receipt.Entries is null
            || receipt.Entries.Length is < 1 or > MaximumReceiptEntries)
        {
            throw new ArgumentException("The receipt entries are invalid.");
        }

        HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
        int priorOrder = -1;
        foreach (AtlasSaveSnapshotReceiptEntry entry in receipt.Entries)
        {
            if (entry is null)
            {
                throw new ArgumentException("The receipt entries are invalid.");
            }

            string destination = NormalizeRelativePath(entry.DestinationRelativePath);
            if (!AtlasSaveSnapshot.TryGetCanonicalName(
                    entry.SourceFileName,
                    out string canonical,
                    out int order)
                || !StringComparer.Ordinal.Equals(destination, canonical)
                || order <= priorOrder
                || !sources.Add(entry.SourceFileName)
                || !destinations.Add(destination)
                || entry.Length < 0)
            {
                throw new ArgumentException("The receipt entries are invalid.");
            }

            ValidateLowerSha256(entry.Sha256);
            priorOrder = order;
        }
    }
}

public sealed record AtlasSaveSnapshotRequest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string RepositoryRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string RunId { get; init; } = string.Empty;

    [JsonRequired]
    public string SaveRoot { get; init; } = string.Empty;
}

public sealed record AtlasSaveSnapshotReceipt
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string RunId { get; init; } = string.Empty;

    [JsonRequired]
    public string SaveRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string FinalSnapshotRoot { get; init; } = string.Empty;

    [JsonRequired]
    public AtlasSaveSnapshotReceiptEntry[] Entries { get; init; } = [];
}

public sealed record AtlasSaveSnapshotReceiptEntry
{
    [JsonRequired]
    public string SourceFileName { get; init; } = string.Empty;

    [JsonRequired]
    public string DestinationRelativePath { get; init; } = string.Empty;

    [JsonRequired]
    public long Length { get; init; }

    [JsonRequired]
    public string Sha256 { get; init; } = string.Empty;
}

internal sealed record AtlasSaveSnapshotLayout(
    string RepositoryRoot,
    string PrivateParent,
    string WorkspaceRoot,
    string IncompleteRoot,
    string FinalRoot,
    string IncompleteReceiptPath,
    string FinalReceiptPath);

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    WriteIndented = false)]
[JsonSerializable(typeof(AtlasSaveSnapshotRequest))]
[JsonSerializable(typeof(AtlasSaveSnapshotReceipt))]
internal sealed partial class AtlasSaveSnapshotJsonContext : JsonSerializerContext;
