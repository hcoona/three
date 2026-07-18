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
    public required IReadOnlyDictionary<string, string> DynamicKeyAliases { get; init; }

    public required IReadOnlyDictionary<string, string> SchemaKeyAliases { get; init; }
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

        return new LocatorAliasMap
        {
            SchemaKeyAliases = CreateAliases(schemaKeys, "schema-key-"),
            DynamicKeyAliases = CreateAliases(dynamicKeys, "dynamic-key-"),
        };
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
            output.Add(segment.Kind switch
            {
                LocatorSegmentKind.DocumentRoleToken => RedactDocumentRole(segment),
                LocatorSegmentKind.ArrayIndex => RedactArrayIndex(segment),
                LocatorSegmentKind.JsonExMarker => RedactJsonExMarker(segment),
                LocatorSegmentKind.SchemaKey => RedactSchemaKey(segment, aliasMap.SchemaKeyAliases),
                LocatorSegmentKind.DynamicKey => RedactDynamicKey(
                    segment,
                    aliasMap.DynamicKeyAliases),
                _ => throw new AtlasSafetyException("The locator segment kind is invalid."),
            });
        }

        return string.Join("/", output);
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

    private static string RedactDocumentRole(LocatorSegment segment)
    {
        ValidateDocumentRole(segment.TextValue);
        return segment.TextValue;
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

    private static string RedactJsonExMarker(LocatorSegment segment)
    {
        ValidateJsonExMarker(segment.TextValue);
        return segment.TextValue;
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
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AtlasSafetyException("The document-role token is invalid.");
        }

        foreach (char character in token)
        {
            if (!(
                    (character is >= 'a' and <= 'z')
                    || char.IsAsciiDigit(character)
                    || character == '-'))
            {
                throw new AtlasSafetyException("The document-role token is invalid.");
            }
        }
    }

    private static void ValidateJsonExMarker(string token)
    {
        if (!AllowedJsonExMarkers.Contains(token))
        {
            throw new AtlasSafetyException("The JsonEx marker is invalid.");
        }
    }

    private static void ValidateKey(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new AtlasSafetyException("The locator key is invalid.");
        }
    }
}
