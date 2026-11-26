using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Authentication.Core;

public sealed record ClientProfile(
    string Name,
    Guid ClientId,
    Guid? FixedTenant,
    string Integration,
    string RegistrationOwner,
    string SupportNotice)
{
    public override string ToString() => nameof(ClientProfile);
}

public static class ProfileSyntax
{
    public static ClientProfile? Parse(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length is 0 or > 65536)
        {
            return null;
        }

        // Accept and skip exactly one leading UTF-8 byte order mark, which common
        // Windows editors write. The byte bound above includes it; any further BOM
        // remains invalid JSON for the reader below.
        if (utf8.Span.StartsWith("\uFEFF"u8))
        {
            utf8 = utf8[3..];
        }

        try
        {
            var reader = new Utf8JsonReader(utf8.Span, new JsonReaderOptions { MaxDepth = 2 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? name = null, clientText = null, integration = null, owner = null, notice = null;
            Guid? tenant = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (!ReadProperty(ref reader, seen, out var property))
                {
                    return null;
                }

                switch (property)
                {
                    case "schemaVersion":
                        if (reader.TokenType != JsonTokenType.Number || !IsVersionOne(reader.ValueSpan))
                        {
                            return null;
                        }
                        break;
                    case "name":
                        name = ReadText(ref reader, 128);
                        if (name is null || !IsDisplayable(name)) return null;
                        break;
                    case "clientId":
                        clientText = ReadText(ref reader, 36);
                        if (clientText is null || !RequestSyntax.TryGuid(clientText, out _)) return null;
                        break;
                    case "cloud":
                        if (ReadText(ref reader, 6) != "public") return null;
                        break;
                    case "integration":
                        integration = ReadText(ref reader, 32);
                        if (integration is not ("windows-wam" or "visual-studio-legacy-wam")) return null;
                        break;
                    case "tenantPolicy":
                        if (!ReadTenant(ref reader, out tenant)) return null;
                        break;
                    case "registration":
                        if (!ReadRegistration(ref reader, out owner, out notice)) return null;
                        break;
                    default:
                        return null;
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read() || seen.Count != 7
                || name is null || clientText is null || integration is null || owner is null || notice is null)
            {
                return null;
            }

            if (integration == "visual-studio-legacy-wam"
                && (clientText != "872cd9fa-d31f-45e0-9eab-6e460a02d1f1" || tenant is not null || owner != "Microsoft"))
            {
                return null;
            }

            return new(name, Guid.ParseExact(clientText, "D"), tenant, integration, owner, notice);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // GetString rejects invalid UTF-8 and unpaired escaped UTF-16 surrogates.
            return null;
        }
    }

    public static bool TryResolveTenant(ClientProfile profile, string? selector, out Guid? exactTenant)
    {
        exactTenant = null;
        if (selector is null)
        {
            exactTenant = profile.FixedTenant;
            return true;
        }

        if (selector == "common")
        {
            return profile.FixedTenant is null;
        }

        if (!RequestSyntax.TryGuid(selector, out var requested)
            || (profile.FixedTenant is not null && requested != profile.FixedTenant))
        {
            return false;
        }

        exactTenant = requested;
        return true;
    }

    private static bool ReadProperty(ref Utf8JsonReader reader, HashSet<string> seen, out string? property)
    {
        property = null;
        if (reader.TokenType != JsonTokenType.PropertyName)
        {
            return false;
        }

        property = reader.GetString();
        return property is not null && seen.Add(property) && reader.Read();
    }

    private static string? ReadText(ref Utf8JsonReader reader, int maximum)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            return null;
        }

        var value = reader.GetString()!;
        return RequestSyntax.HasCharacterLength(value, 1, maximum)
            && !value.Any(character => character < ' ' || character == '\u007f') ? value : null;
    }

    // Name and owner appear in the pre-interaction notice. Reject every control
    // (C0, DEL, C1) and Unicode format character, including bidirectional
    // overrides and isolates, zero-width characters and U+FEFF. Supplementary
    // format characters are classified as whole scalar values.
    private static bool IsDisplayable(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format) return false;
        }

        return true;
    }

    private static bool ReadTenant(ref Utf8JsonReader reader, out Guid? tenant)
    {
        tenant = null;
        if (reader.TokenType != JsonTokenType.StartObject) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? kind = null, tenantText = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (!ReadProperty(ref reader, seen, out var property)) return false;
            switch (property)
            {
                case "kind":
                    kind = ReadText(ref reader, 13);
                    if (kind is null) return false;
                    break;
                case "tenantId":
                    tenantText = ReadText(ref reader, 36);
                    if (tenantText is null || !RequestSyntax.TryGuid(tenantText, out _)) return false;
                    break;
                default:
                    return false;
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject) return false;
        if (kind == "multitenant") return seen.Count == 1;
        if (kind != "single-tenant" || tenantText is null || seen.Count != 2) return false;
        tenant = Guid.ParseExact(tenantText, "D");
        return true;
    }

    private static bool ReadRegistration(ref Utf8JsonReader reader, out string? owner, out string? notice)
    {
        owner = notice = null;
        if (reader.TokenType != JsonTokenType.StartObject) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (!ReadProperty(ref reader, seen, out var property)) return false;
            switch (property)
            {
                case "owner":
                    owner = ReadText(ref reader, 128);
                    if (owner is null || !IsDisplayable(owner)) return false;
                    break;
                case "supportNotice":
                    notice = ReadText(ref reader, 1024);
                    if (notice is null) return false;
                    break;
                case "externalDependency":
                    if (reader.TokenType != JsonTokenType.True) return false;
                    break;
                default:
                    return false;
            }
        }

        return reader.TokenType == JsonTokenType.EndObject && seen.Count == 3
            && owner is not null && notice is not null;
    }

    private static bool IsVersionOne(ReadOnlySpan<byte> number)
    {
        // Compare the JSON number exactly with schema const 1. Floating-point or
        // decimal rounding must not admit a different version arbitrarily close to 1.
        if (number[0] == '-') return false;
        var exponentStart = number.IndexOfAny((byte)'e', (byte)'E');
        var exponent = 0;
        if (exponentStart >= 0
            && !int.TryParse(Encoding.UTF8.GetString(number[(exponentStart + 1)..]),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
        var coefficient = exponentStart < 0 ? number : number[..exponentStart];
        var digits = 0;
        var digitsBeforePoint = -1;
        var onePosition = -1;
        foreach (var character in coefficient)
        {
            if (character == '.')
            {
                digitsBeforePoint = digits;
                continue;
            }

            if (character != '0')
            {
                if (character != '1' || onePosition >= 0) return false;
                onePosition = digits;
            }

            digits++;
        }

        if (digitsBeforePoint < 0) digitsBeforePoint = digits;
        return onePosition >= 0 && (long)digitsBeforePoint - onePosition - 1 + exponent == 0;
    }
}
