using System.Collections.ObjectModel;

namespace Hcoona.CelesphoniaModifier.Atlas;

public enum LocatorSegmentKind
{
    DocumentRoleToken,
    ArrayIndex,
    JsonExMarker,
    SchemaKey,
    DynamicKey,
}

public readonly record struct LocatorSegment
{
    private LocatorSegment(
        LocatorSegmentKind kind,
        string textValue,
        int? numericValue)
    {
        Kind = kind;
        TextValue = textValue;
        NumericValue = numericValue;
    }

    public LocatorSegmentKind Kind { get; }

    public string TextValue { get; } = string.Empty;

    public int? NumericValue { get; }

    public static LocatorSegment DocumentRole(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new LocatorSegment(LocatorSegmentKind.DocumentRoleToken, token, null);
    }

    public static LocatorSegment ArrayIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new LocatorSegment(LocatorSegmentKind.ArrayIndex, string.Empty, index);
    }

    public static LocatorSegment JsonExMarker(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new LocatorSegment(LocatorSegmentKind.JsonExMarker, token, null);
    }

    public static LocatorSegment SchemaKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new LocatorSegment(LocatorSegmentKind.SchemaKey, key, null);
    }

    public static LocatorSegment DynamicKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new LocatorSegment(LocatorSegmentKind.DynamicKey, key, null);
    }
}

public sealed class LocatorAliasMap
{
    internal LocatorAliasMap(
        IReadOnlyDictionary<string, string> dynamicKeyAliases,
        IReadOnlyDictionary<string, string> schemaKeyAliases)
    {
        DynamicKeyAliases = FreezeAliases(dynamicKeyAliases, "dynamic-key-");
        SchemaKeyAliases = FreezeAliases(schemaKeyAliases, "schema-key-");
    }

    public IReadOnlyDictionary<string, string> DynamicKeyAliases { get; }

    public IReadOnlyDictionary<string, string> SchemaKeyAliases { get; }

    private static ReadOnlyDictionary<string, string> FreezeAliases(
        IReadOnlyDictionary<string, string> source,
        string prefix)
    {
        ArgumentNullException.ThrowIfNull(source);

        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        HashSet<string> values = new(StringComparer.Ordinal);
        HashSet<int> ordinals = [];
        foreach (KeyValuePair<string, string> pair in source)
        {
            LocatorSegmentRedactor.ValidateKey(pair.Key);
            LocatorSegmentRedactor.ValidateAliasValue(pair.Value, prefix);
            if (!aliases.TryAdd(pair.Key, pair.Value) || !values.Add(pair.Value))
            {
                throw new AtlasSafetyException("The locator alias map is invalid.");
            }

            ordinals.Add(int.Parse(
                pair.Value[prefix.Length..],
                System.Globalization.CultureInfo.InvariantCulture));
        }

        string[] sortedKeys = aliases.Keys
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        for (int index = 0; index < sortedKeys.Length; index++)
        {
            string expectedKey = sortedKeys[index];
            string expectedAlias = $"{prefix}{index + 1:000000}";
            if (!aliases.TryGetValue(expectedKey, out string? alias)
                || !StringComparer.Ordinal.Equals(alias, expectedAlias))
            {
                throw new AtlasSafetyException("The locator alias map is invalid.");
            }
        }

        for (int ordinal = 1; ordinal <= aliases.Count; ordinal++)
        {
            if (!ordinals.Contains(ordinal))
            {
                throw new AtlasSafetyException("The locator alias map is invalid.");
            }
        }

        return new ReadOnlyDictionary<string, string>(aliases);
    }
}

public static class LocatorSegmentRedactor
{
    private static readonly HashSet<string> AllowedJsonExMarkers =
    [
        "@",
        "@a",
        "@c",
        "@r",
    ];
    private static readonly HashSet<string> AllowedDocumentRoles =
        new(StringComparer.Ordinal)
        {
            "slot-save",
            "global-save",
            "config-save",
            "definition-source",
        };

    public static LocatorAliasMap CreateAliasMap(IEnumerable<LocatorSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        SortedSet<string> schemaKeys = new(StringComparer.Ordinal);
        SortedSet<string> dynamicKeys = new(StringComparer.Ordinal);
        foreach (LocatorSegment segment in segments)
        {
            switch (segment.Kind)
            {
                case LocatorSegmentKind.DocumentRoleToken:
                    ValidateDocumentRole(segment.TextValue);
                    break;
                case LocatorSegmentKind.ArrayIndex:
                    ArgumentOutOfRangeException.ThrowIfNegative(segment.NumericValue ?? -1);
                    break;
                case LocatorSegmentKind.JsonExMarker:
                    ValidateJsonExMarker(segment.TextValue);
                    break;
                case LocatorSegmentKind.SchemaKey:
                    ValidateKey(segment.TextValue);
                    schemaKeys.Add(segment.TextValue);
                    break;
                case LocatorSegmentKind.DynamicKey:
                    ValidateKey(segment.TextValue);
                    dynamicKeys.Add(segment.TextValue);
                    break;
                default:
                    throw new AtlasSafetyException("The locator segment kind is invalid.");
            }
        }

        return new LocatorAliasMap(
            CreateAliases(dynamicKeys, "dynamic-key-"),
            CreateAliases(schemaKeys, "schema-key-"));
    }

    public static string Redact(
        IEnumerable<LocatorSegment> segments,
        LocatorAliasMap aliasMap)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(aliasMap);

        List<string> output = [];
        foreach (LocatorSegment segment in segments)
        {
            switch (segment.Kind)
            {
                case LocatorSegmentKind.DocumentRoleToken:
                    ValidateDocumentRole(segment.TextValue);
                    break;
                case LocatorSegmentKind.ArrayIndex:
                    ArgumentOutOfRangeException.ThrowIfNegative(segment.NumericValue ?? -1);
                    break;
                case LocatorSegmentKind.JsonExMarker:
                    ValidateJsonExMarker(segment.TextValue);
                    break;
                case LocatorSegmentKind.SchemaKey:
                    ValidateKey(segment.TextValue);
                    break;
                case LocatorSegmentKind.DynamicKey:
                    ValidateKey(segment.TextValue);
                    break;
                default:
                    throw new AtlasSafetyException("The locator segment kind is invalid.");
            }

            output.Add(segment.Kind switch
            {
                LocatorSegmentKind.DocumentRoleToken => segment.TextValue,
                LocatorSegmentKind.ArrayIndex => RedactArrayIndex(segment),
                LocatorSegmentKind.JsonExMarker => segment.TextValue,
                LocatorSegmentKind.SchemaKey => RedactSchemaKey(segment, aliasMap.SchemaKeyAliases),
                LocatorSegmentKind.DynamicKey => RedactDynamicKey(
                    segment,
                    aliasMap.DynamicKeyAliases),
                _ => throw new AtlasSafetyException("The locator segment kind is invalid."),
            });
        }

        return string.Join("/", output);
    }

    internal static void ValidateAliasValue(string alias, string prefix)
    {
        if (!alias.StartsWith(prefix, StringComparison.Ordinal)
            || alias.Length != prefix.Length + 6
            || !int.TryParse(
                alias.AsSpan(prefix.Length),
                System.Globalization.CultureInfo.InvariantCulture,
                out int ordinal)
            || ordinal <= 0)
        {
            throw new AtlasSafetyException("The locator alias map is invalid.");
        }
    }

    private static Dictionary<string, string> CreateAliases(
        IEnumerable<string> keys,
        string prefix)
    {
        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        int ordinal = 0;
        foreach (string key in keys)
        {
            ordinal++;
            if (ordinal > 999999)
            {
                throw new AtlasSafetyException("The locator alias range is exhausted.");
            }

            aliases.Add(key, $"{prefix}{ordinal:000000}");
        }

        return aliases;
    }

    private static string RedactArrayIndex(LocatorSegment segment)
    {
        if (segment.NumericValue is null or < 0)
        {
            throw new AtlasSafetyException("The array index is invalid.");
        }

        return segment.NumericValue.Value.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string RedactSchemaKey(
        LocatorSegment segment,
        IReadOnlyDictionary<string, string> aliases)
    {
        ValidateKey(segment.TextValue);
        return aliases.TryGetValue(segment.TextValue, out string? alias)
            ? alias
            : throw new AtlasSafetyException("The schema-key alias is missing.");
    }

    private static string RedactDynamicKey(
        LocatorSegment segment,
        IReadOnlyDictionary<string, string> aliases)
    {
        ValidateKey(segment.TextValue);
        return aliases.TryGetValue(segment.TextValue, out string? alias)
            ? alias
            : throw new AtlasSafetyException("The dynamic-key alias is missing.");
    }

    private static void ValidateDocumentRole(string token)
    {
        if (!AllowedDocumentRoles.Contains(token))
        {
            throw new AtlasSafetyException("The document-role token is invalid.");
        }
    }

    private static void ValidateJsonExMarker(string token)
    {
        if (!AllowedJsonExMarkers.Contains(token))
        {
            throw new AtlasSafetyException("The JsonEx marker is invalid.");
        }
    }

    internal static void ValidateKey(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new AtlasSafetyException("The locator key is invalid.");
        }
    }
}
