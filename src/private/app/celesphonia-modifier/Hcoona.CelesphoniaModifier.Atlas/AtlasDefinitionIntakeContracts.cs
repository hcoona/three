using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasDefinitionIntakeContracts
{
    public const string RequestSchemaVersion = "atlas-definition-intake-request/v1";
    public const string ReceiptSchemaVersion = "atlas-definition-copy-receipt/v1";

    internal static async ValueTask<AtlasDefinitionIntakeRequest> ReadRequestAsync(
        string requestPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentNullException.ThrowIfNull(io);

        try
        {
            byte[] bytes = await io.ReadAllBytesAsync(
                    Path.GetFullPath(requestPath),
                    cancellationToken)
                .ConfigureAwait(false);
            AtlasDefinitionIntakeRequest request = Deserialize(
                bytes,
                AtlasDefinitionIntakeJsonContext.Default.AtlasDefinitionIntakeRequest);
            ValidateRequest(request);
            return request;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
            or NotSupportedException
            or ArgumentException)
        {
            throw new AtlasRequestException("The definition intake request is invalid.", exception);
        }
    }

    internal static async ValueTask<AtlasDefinitionCopyReceipt> ReadReceiptAsync(
        string receiptPath,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(receiptPath);
        ArgumentNullException.ThrowIfNull(io);

        try
        {
            byte[] bytes = await io.ReadAllBytesAsync(receiptPath, cancellationToken)
                .ConfigureAwait(false);
            AtlasDefinitionCopyReceipt receipt = Deserialize(
                bytes,
                AtlasDefinitionIntakeJsonContext.Default.AtlasDefinitionCopyReceipt);
            ValidateReceipt(receipt);
            return receipt;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
            or NotSupportedException
            or ArgumentException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new AtlasSafetyException(
                "The definition copy receipt is invalid.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
    }

    internal static byte[] SerializeReceipt(AtlasDefinitionCopyReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateReceipt(receipt);
        return JsonSerializer.SerializeToUtf8Bytes(
            receipt,
            AtlasDefinitionIntakeJsonContext.Default.AtlasDefinitionCopyReceipt);
    }

    internal static AtlasDefinitionIntakeLayout CreateLayout(
        AtlasDefinitionIntakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        string repositoryRoot = NormalizeAbsolutePath(request.RepositoryRoot);
        string privateParent = Path.Combine(
            repositoryRoot,
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            ".private",
            "atlas-definition-intake");
        string workspaceRoot = Path.Combine(privateParent, request.RunId);
        string incompleteRoot = Path.Combine(workspaceRoot, "definition-snapshot.incomplete");
        string finalRoot = Path.Combine(workspaceRoot, "definition-snapshot");
        return new AtlasDefinitionIntakeLayout(
            repositoryRoot,
            Path.GetFullPath(privateParent),
            Path.GetFullPath(workspaceRoot),
            Path.GetFullPath(incompleteRoot),
            Path.GetFullPath(finalRoot),
            Path.GetFullPath(Path.Combine(incompleteRoot, "definition-copy-receipt.json")),
            Path.GetFullPath(Path.Combine(finalRoot, "definition-copy-receipt.json")));
    }

    internal static string NormalizeAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute path is required.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The path root is invalid.", nameof(path));
        return fullPath.Length == root.Length
            ? fullPath
            : fullPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
    }

    internal static bool PathEquals(string first, string second) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            NormalizeAbsolutePath(first),
            NormalizeAbsolutePath(second));

    internal static bool ContainsPath(string root, string candidate)
    {
        string normalizedRoot = NormalizeAbsolutePath(root);
        string normalizedCandidate = NormalizeAbsolutePath(candidate);
        char finalRootCharacter = normalizedRoot[^1];
        string rootPrefix = finalRootCharacter == Path.DirectorySeparatorChar
            || finalRootCharacter == Path.AltDirectorySeparatorChar
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return StringComparer.OrdinalIgnoreCase.Equals(normalizedRoot, normalizedCandidate)
            || normalizedCandidate.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathFullyQualified(relativePath)
            || relativePath.Contains(':'))
        {
            throw new AtlasSafetyException("A definition relative path is invalid.");
        }

        string[] segments = relativePath.Split(
            ['\\', '/'],
            StringSplitOptions.None);
        if (segments.Any(static segment =>
                segment.Length == 0
                || StringComparer.Ordinal.Equals(segment, ".")
                || StringComparer.Ordinal.Equals(segment, "..")))
        {
            throw new AtlasSafetyException("A definition relative path is invalid.");
        }

        return string.Join("/", segments);
    }

    internal static void ValidateLowerHexSha256(string value, string name)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A lowercase SHA-256 value is required.", name);
        }
    }

    internal static void ValidateSourceAlias(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(static character =>
                character is not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                and not '-'))
        {
            throw new AtlasSafetyException("A definition source alias is invalid.");
        }
    }

    private static T Deserialize<T>(
        ReadOnlySpan<byte> bytes,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        RejectDuplicateProperties(bytes);
        T? document = JsonSerializer.Deserialize(bytes, typeInfo);
        return document ?? throw new JsonException("The JSON document is null.");
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> bytes)
    {
        Utf8JsonReader reader = new(
            bytes,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = AtlasIntakeContracts.MaxJsonDepth,
            });
        Stack<HashSet<string>> objects = [];
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                objects.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (objects.Count == 0 || !objects.Peek().Add(reader.GetString()!))
                {
                    throw new JsonException("Duplicate JSON properties are invalid.");
                }
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                objects.Pop();
            }
        }

        if (objects.Count != 0)
        {
            throw new JsonException("The JSON document is incomplete.");
        }
    }

    private static void ValidateRequest(AtlasDefinitionIntakeRequest request)
    {
        if (!StringComparer.Ordinal.Equals(request.SchemaVersion, RequestSchemaVersion))
        {
            throw new ArgumentException("The request schema version is invalid.");
        }

        _ = NormalizeAbsolutePath(request.RepositoryRoot);
        _ = NormalizeAbsolutePath(request.DefinitionRoot);
        if (string.IsNullOrEmpty(request.RunId)
            || request.RunId.Length != 32
            || request.RunId.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("The run ID is invalid.");
        }

        ValidateLowerHexSha256(
            request.ExpectedHistoricalAuthoritySha256,
            nameof(request.ExpectedHistoricalAuthoritySha256));
        if (request.ExpectedHistoricalAuthorityRevision < 1
            || request.ApplicationId < 1
            || request.BuildId < 1)
        {
            throw new ArgumentException("The historical binding is invalid.");
        }
    }

    private static void ValidateReceipt(AtlasDefinitionCopyReceipt receipt)
    {
        if (!StringComparer.Ordinal.Equals(receipt.SchemaVersion, ReceiptSchemaVersion))
        {
            throw new ArgumentException("The receipt schema version is invalid.");
        }

        ValidateLowerHexSha256(
            receipt.HistoricalAuthoritySha256,
            nameof(receipt.HistoricalAuthoritySha256));
        if (receipt.HistoricalAuthorityRevision < 1
            || receipt.ApplicationId < 1
            || receipt.BuildId < 1)
        {
            throw new ArgumentException("The receipt historical binding is invalid.");
        }

        if (string.IsNullOrEmpty(receipt.RunId)
            || receipt.RunId.Length != 32
            || receipt.RunId.Any(static character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("The receipt run ID is invalid.");
        }

        _ = NormalizeAbsolutePath(receipt.DefinitionRoot);
        _ = NormalizeAbsolutePath(receipt.FinalCopyRoot);

        if (receipt.Entries is null)
        {
            throw new ArgumentException("The receipt entries are invalid.");
        }

        string? priorAlias = null;
        HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
        foreach (AtlasDefinitionCopyReceiptEntry entry in receipt.Entries)
        {
            if (entry is null)
            {
                throw new ArgumentException("The receipt entries are invalid.");
            }

            ValidateSourceAlias(entry.SourceAlias);
            string destination = NormalizeRelativePath(entry.DestinationRelativePath);
            ValidateLowerHexSha256(entry.Sha256, nameof(entry.Sha256));
            if (entry.Length < 0
                || !aliases.Add(entry.SourceAlias)
                || !destinations.Add(destination)
                || (priorAlias is not null
                    && StringComparer.Ordinal.Compare(priorAlias, entry.SourceAlias) >= 0))
            {
                throw new ArgumentException("The receipt entries are invalid.");
            }

            priorAlias = entry.SourceAlias;
        }
    }
}

public sealed record AtlasDefinitionIntakeRequest
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string RepositoryRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string RunId { get; init; } = string.Empty;

    [JsonRequired]
    public string DefinitionRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string ExpectedHistoricalAuthoritySha256 { get; init; } = string.Empty;

    [JsonRequired]
    public int ExpectedHistoricalAuthorityRevision { get; init; }

    [JsonRequired]
    public int ApplicationId { get; init; }

    [JsonRequired]
    public int BuildId { get; init; }
}

public sealed record AtlasDefinitionCopyReceipt
{
    [JsonRequired]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonRequired]
    public string HistoricalAuthoritySha256 { get; init; } = string.Empty;

    [JsonRequired]
    public int HistoricalAuthorityRevision { get; init; }

    [JsonRequired]
    public int ApplicationId { get; init; }

    [JsonRequired]
    public int BuildId { get; init; }

    [JsonRequired]
    public string RunId { get; init; } = string.Empty;

    [JsonRequired]
    public string DefinitionRoot { get; init; } = string.Empty;

    [JsonRequired]
    public string FinalCopyRoot { get; init; } = string.Empty;

    [JsonRequired]
    public AtlasDefinitionCopyReceiptEntry[] Entries { get; init; } = [];
}

public sealed record AtlasDefinitionCopyReceiptEntry
{
    [JsonRequired]
    public string SourceAlias { get; init; } = string.Empty;

    [JsonRequired]
    public string DestinationRelativePath { get; init; } = string.Empty;

    [JsonRequired]
    public long Length { get; init; }

    [JsonRequired]
    public string Sha256 { get; init; } = string.Empty;
}

internal sealed record AtlasDefinitionIntakeLayout(
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
[JsonSerializable(typeof(AtlasDefinitionIntakeRequest))]
[JsonSerializable(typeof(AtlasDefinitionCopyReceipt))]
internal sealed partial class AtlasDefinitionIntakeJsonContext : JsonSerializerContext;
