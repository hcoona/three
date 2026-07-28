using System.Security.Cryptography;
using System.Text.Json;

namespace Hcoona.CelesphoniaModifier.Atlas;

internal static class HistoricalAtlasDefinitionIngress
{
    internal static async ValueTask<HistoricalDefinitionAuthority> ReadAsync(
        AtlasDefinitionIntakeRequest request,
        AtlasDefinitionIntakeLayout layout,
        AtlasIoSeams io,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(io);

        string requestPath = GetHistoricalRequestPath(layout.RepositoryRoot);
        string authorityPath = GetHistoricalAuthorityPath(layout.RepositoryRoot);
        ValidateOrdinaryFile(requestPath, io);
        ValidateOrdinaryFile(authorityPath, io);

        byte[] historicalRequestBytes = await io.ReadAllBytesAsync(requestPath, cancellationToken)
            .ConfigureAwait(false);
        HistoricalAnchor anchor = ParseHistoricalAnchor(historicalRequestBytes);

        byte[] authorityBytes = await io.ReadAllBytesAsync(authorityPath, cancellationToken)
            .ConfigureAwait(false);
        string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(authorityBytes));
        if (!StringComparer.Ordinal.Equals(
                request.ExpectedHistoricalAuthoritySha256,
                anchor.ExpectedBaselineSha256)
            || !StringComparer.Ordinal.Equals(
                request.ExpectedHistoricalAuthoritySha256,
                actualSha256))
        {
            throw new AtlasSafetyException(
                "The historical definition authority digest is invalid.");
        }

        HistoricalDefinitionAuthority authority = ParseAuthority(authorityBytes, actualSha256);
        if (request.ExpectedHistoricalAuthorityRevision != authority.Revision
            || request.ApplicationId != authority.ApplicationId
            || request.BuildId != authority.BuildId)
        {
            throw new AtlasSafetyException(
                "The historical definition authority binding is invalid.");
        }

        return authority;
    }

    internal static string GetHistoricalRequestPath(string repositoryRoot) =>
        Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier",
                ".private",
                "atlas-v0",
                AtlasIntakeContracts.ExactSurveyAlias,
                "intake",
                "requests",
                "discover.json"));

    internal static string GetHistoricalAuthorityPath(string repositoryRoot) =>
        Path.GetFullPath(
            Path.Combine(
                repositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier",
                ".private",
                "atlas-v0",
                AtlasIntakeContracts.ExactSurveyAlias,
                "intake",
                "corpus-intake-manifest.json"));

    private static HistoricalAnchor ParseHistoricalAnchor(ReadOnlySpan<byte> bytes)
    {
        try
        {
            using JsonDocument document = ParseDocument(bytes);
            JsonElement root = RequireObject(document.RootElement);
            RejectDuplicateRelevantProperties(
                root,
                "schemaVersion",
                "expectedBaselineSha256",
                "expectedSteamAppId",
                "expectedBuildId");
            string schemaVersion = GetRequiredString(root, "schemaVersion");
            string expectedSha256 = GetRequiredString(root, "expectedBaselineSha256");
            int applicationId = GetRequiredInt32(root, "expectedSteamAppId");
            int buildId = GetRequiredInt32(root, "expectedBuildId");
            AtlasDefinitionIntakeContracts.ValidateLowerHexSha256(
                expectedSha256,
                nameof(expectedSha256));
            if (!StringComparer.Ordinal.Equals(
                    schemaVersion,
                    AtlasIntakeContracts.DiscoveryRequestSchemaVersion)
                || applicationId != AtlasIntakeContracts.ExactSteamAppId
                || buildId != AtlasIntakeContracts.ExactBuildId)
            {
                throw new JsonException("The historical request anchor is invalid.");
            }

            return new HistoricalAnchor(expectedSha256, applicationId, buildId);
        }
        catch (Exception exception) when (
            exception is JsonException
            or ArgumentException
            or InvalidOperationException)
        {
            throw new AtlasSafetyException(
                "The historical request anchor is invalid.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
    }

    private static HistoricalDefinitionAuthority ParseAuthority(
        ReadOnlySpan<byte> bytes,
        string sha256)
    {
        try
        {
            using JsonDocument document = ParseDocument(bytes);
            JsonElement root = RequireObject(document.RootElement);
            RejectDuplicateRelevantProperties(
                root,
                "schemaVersion",
                "surveyAlias",
                "manifestRevision",
                "definitionGroups",
                "definitionEntries");
            if (!StringComparer.Ordinal.Equals(
                    GetRequiredString(root, "schemaVersion"),
                    AtlasIntakeContracts.IntakeManifestSchemaVersion)
                || !StringComparer.Ordinal.Equals(
                    GetRequiredString(root, "surveyAlias"),
                    AtlasIntakeContracts.ExactSurveyAlias))
            {
                throw new JsonException("The historical definition authority is invalid.");
            }

            int revision = GetRequiredInt32(root, "manifestRevision");
            if (revision < 1)
            {
                throw new JsonException("The historical definition authority is invalid.");
            }

            List<HistoricalDefinitionGroup> groups = ParseGroups(
                GetRequiredProperty(root, "definitionGroups"));
            List<HistoricalDefinitionEntry> entries = ParseEntries(
                GetRequiredProperty(root, "definitionEntries"));
            ValidateDefinitionProjection(groups, entries);
            return new HistoricalDefinitionAuthority(
                AtlasIntakeContracts.ExactSurveyAlias,
                AtlasIntakeContracts.ExactSteamAppId,
                AtlasIntakeContracts.ExactBuildId,
                sha256,
                revision,
                groups.AsReadOnly(),
                entries.AsReadOnly());
        }
        catch (Exception exception) when (
            exception is JsonException
            or ArgumentException
            or InvalidOperationException
            or AtlasSafetyException)
        {
            throw new AtlasSafetyException(
                "The historical definition authority is invalid.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
    }

    private static List<HistoricalDefinitionGroup> ParseGroups(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The historical definition groups are invalid.");
        }

        List<HistoricalDefinitionGroup> groups = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            JsonElement group = RequireObject(item);
            RejectDuplicateRelevantProperties(
                group,
                "groupId",
                "selectionRule",
                "discoveredCount",
                "decision");
            groups.Add(
                new HistoricalDefinitionGroup(
                    GetRequiredString(group, "groupId"),
                    GetRequiredString(group, "selectionRule"),
                    GetRequiredInt32(group, "discoveredCount"),
                    GetRequiredString(group, "decision")));
        }

        if (groups.Count == 0)
        {
            throw new JsonException("The historical definition groups are invalid.");
        }

        return groups;
    }

    private static List<HistoricalDefinitionEntry> ParseEntries(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The historical definition entries are invalid.");
        }

        List<HistoricalDefinitionEntry> entries = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            JsonElement entry = RequireObject(item);
            RejectDuplicateRelevantProperties(
                entry,
                "sourceAlias",
                "relativePath",
                "groupId",
                "decision");
            entries.Add(
                new HistoricalDefinitionEntry(
                    GetRequiredString(entry, "sourceAlias"),
                    AtlasDefinitionIntakeContracts.NormalizeRelativePath(
                        GetRequiredString(entry, "relativePath")),
                    GetRequiredString(entry, "groupId"),
                    GetRequiredString(entry, "decision")));
        }

        if (entries.Count == 0)
        {
            throw new JsonException("The historical definition entries are invalid.");
        }

        return entries;
    }

    private static void ValidateDefinitionProjection(
        IReadOnlyList<HistoricalDefinitionGroup> groups,
        IReadOnlyList<HistoricalDefinitionEntry> entries)
    {
        Dictionary<string, HistoricalDefinitionGroup> groupsById =
            new(StringComparer.Ordinal);
        foreach (HistoricalDefinitionGroup group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.GroupId)
                || !AtlasIntakeContracts.TrySplitDefinitionSelectionRule(
                    group.SelectionRule,
                    out _)
                || group.DiscoveredCount < 0
                || !IsDefinitionDecision(group.Decision)
                || !groupsById.TryAdd(group.GroupId, group))
            {
                throw new JsonException("A historical definition group is invalid.");
            }
        }

        HashSet<string> aliases = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> relativePaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (HistoricalDefinitionEntry entry in entries)
        {
            AtlasDefinitionIntakeContracts.ValidateSourceAlias(entry.SourceAlias);
            if (!aliases.Add(entry.SourceAlias)
                || !relativePaths.Add(entry.RelativePath)
                || !groupsById.TryGetValue(entry.GroupId, out HistoricalDefinitionGroup? group)
                || !StringComparer.Ordinal.Equals(entry.Decision, group.Decision))
            {
                throw new JsonException("A historical definition entry is invalid.");
            }

            HistoricalDefinitionGroup? matchedGroup = DefinitionRuleMatcher.FindFirstMatch(
                groups,
                entry.RelativePath);
            if (matchedGroup is null
                || !StringComparer.Ordinal.Equals(matchedGroup.GroupId, entry.GroupId))
            {
                throw new JsonException("A historical definition entry mapping is invalid.");
            }
        }

        foreach (HistoricalDefinitionGroup group in groups)
        {
            int count = entries.Count(entry =>
                StringComparer.Ordinal.Equals(entry.GroupId, group.GroupId));
            if (count != group.DiscoveredCount)
            {
                throw new JsonException("A historical definition group count is invalid.");
            }
        }
    }

    private static bool IsDefinitionDecision(string decision) =>
        StringComparer.Ordinal.Equals(
            decision,
            AtlasIntakeContracts.IncludeDefinitionDecision)
        || StringComparer.Ordinal.Equals(
            decision,
            AtlasIntakeContracts.ExcludeDefinitionDecision);

    private static JsonDocument ParseDocument(ReadOnlySpan<byte> bytes)
        => JsonDocument.Parse(
            bytes.ToArray(),
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = AtlasIntakeContracts.MaxJsonDepth,
            });

    private static void RejectDuplicateRelevantProperties(
        JsonElement element,
        params string[] relevantNames)
    {
        HashSet<string> relevant = new(relevantNames, StringComparer.Ordinal);
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (relevant.Contains(property.Name) && !seen.Add(property.Name))
            {
                throw new JsonException("A relevant historical property is duplicated.");
            }
        }
    }

    private static JsonElement RequireObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A JSON object is required.");
        }

        return element;
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value)
            ? value
            : throw new JsonException($"The '{propertyName}' property is required.");

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        JsonElement value = GetRequiredProperty(element, propertyName);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new JsonException($"The '{propertyName}' property must be a string.");
    }

    private static int GetRequiredInt32(JsonElement element, string propertyName)
    {
        JsonElement value = GetRequiredProperty(element, propertyName);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int result)
            ? result
            : throw new JsonException($"The '{propertyName}' property must be an integer.");
    }

    private static void ValidateOrdinaryFile(string path, AtlasIoSeams io)
    {
        if (!io.FileExists(path))
        {
            throw new AtlasSafetyException("A historical definition authority file is missing.");
        }

        FileAttributes attributes = io.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new AtlasSafetyException("A historical definition authority file is invalid.");
        }
    }

    private sealed record HistoricalAnchor(
        string ExpectedBaselineSha256,
        int ApplicationId,
        int BuildId);
}

internal sealed record HistoricalDefinitionAuthority(
    string SurveyAlias,
    int ApplicationId,
    int BuildId,
    string Sha256,
    int Revision,
    IReadOnlyList<HistoricalDefinitionGroup> Groups,
    IReadOnlyList<HistoricalDefinitionEntry> Entries);

internal sealed record HistoricalDefinitionGroup(
    string GroupId,
    string SelectionRule,
    int DiscoveredCount,
    string Decision);

internal sealed record HistoricalDefinitionEntry(
    string SourceAlias,
    string RelativePath,
    string GroupId,
    string Decision);
