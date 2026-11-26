using System.Globalization;

namespace Authentication.Core;

// Projection of the protocol 1 argument vector, never a JSON input protocol.
public sealed record ParsedRequest(
    string ProfilePath,
    string AccountEmail,
    IReadOnlyList<string> Scopes,
    bool InteractionAllowed,
    string? Tenant,
    int TimeoutSeconds,
    bool CancelOnStdinClose,
    bool TelemetryStderr,
    bool ForceInteractive = false)
{
    public override string ToString() => nameof(ParsedRequest);
}

public static class RequestSyntax
{
    public static ParsedRequest? Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments[0] != "authenticate")
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var scopes = new List<string>();
        var cancelOnClose = false;
        for (var index = 1; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (option == "--cancel-on-stdin-close")
            {
                if (cancelOnClose)
                {
                    return null;
                }

                cancelOnClose = true;
                continue;
            }

            if (option is not ("--protocol" or "--profile" or "--account-email" or "--scope"
                or "--interaction" or "--tenant" or "--timeout-seconds" or "--telemetry")
                || ++index == arguments.Count)
            {
                return null;
            }

            var value = arguments[index];
            if (option == "--scope")
            {
                scopes.Add(value);
            }
            else if (!values.TryAdd(option, value))
            {
                return null;
            }
        }

        if (!values.TryGetValue("--protocol", out var protocol) || protocol != "1"
            || !values.TryGetValue("--profile", out var profile) || !IsWindowsPath(profile)
            || !values.TryGetValue("--account-email", out var email) || !IsEmail(email)
            || !values.TryGetValue("--interaction", out var interaction)
            || interaction is not ("non-interactive-only" or "interactive-if-needed" or "force-interactive")
            || !ValidScopes(scopes))
        {
            return null;
        }

        values.TryGetValue("--tenant", out var tenant);
        if (tenant is not null && tenant != "common" && !TryGuid(tenant, out _))
        {
            return null;
        }

        var timeout = 120;
        if (values.TryGetValue("--timeout-seconds", out var timeoutText)
            && (!IsUnsignedDecimal(timeoutText)
                || !int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out timeout)
                || timeout is < 1 or > 600))
        {
            return null;
        }

        values.TryGetValue("--telemetry", out var telemetry);
        if (telemetry is not (null or "off" or "stderr"))
        {
            return null;
        }

        return new(profile, email, scopes.AsReadOnly(), interaction != "non-interactive-only",
            tenant, timeout, cancelOnClose, telemetry == "stderr", interaction == "force-interactive");
    }

    // Exactly ^[1-9][0-9]{0,2}$: ASCII digits, no sign, whitespace or leading zero.
    private static bool IsUnsignedDecimal(string value) =>
        value.Length is >= 1 and <= 3 && value[0] is >= '1' and <= '9' && value.All(char.IsAsciiDigit);

    // Only the canonical 8-4-4-4-12 hexadecimal spelling. Guid's "D" parser alone
    // also accepts 0x and + segment prefixes, which would silently rewrite input.
    internal static bool TryGuid(string value, out Guid result)
    {
        result = default;
        if (value.Length != 36) return false;
        for (var index = 0; index < value.Length; index++)
        {
            if (index is 8 or 13 or 18 or 23 ? value[index] != '-' : !char.IsAsciiHexDigit(value[index]))
                return false;
        }

        return Guid.TryParseExact(value, "D", out result);
    }

    internal static bool HasCharacterLength(string value, int minimum, int maximum)
    {
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (++index == value.Length || !char.IsLowSurrogate(value[index])) return false;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }

            if (++count > maximum) return false;
        }

        return count >= minimum;
    }

    private static bool IsWindowsPath(string value) =>
        HasCharacterLength(value, 3, 32767) && char.IsAsciiLetter(value[0])
        && value[1] == ':' && value[2] is '\\' or '/'
        && !value.Any(char.IsControl);

    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@');
        return HasCharacterLength(value, 3, 320) && at > 0 && at < value.Length - 1
            && at == value.LastIndexOf('@') && !value.Any(IsWhitespaceOrControl);
    }

    private static bool ValidScopes(List<string> scopes)
    {
        if (scopes.Count is < 1 or > 64)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? resource = null;
        foreach (var scope in scopes)
        {
            if (!HasCharacterLength(scope, 1, 2048) || scope.Any(IsWhitespaceOrControl) || !seen.Add(scope))
            {
                return false;
            }

            var separator = scope.LastIndexOf('/');
            if (separator < 1 || separator == scope.Length - 1)
            {
                return false;
            }

            var prefix = scope[..separator];
            if (!IsResource(prefix) || (resource is not null && prefix != resource)
                || (scope[(separator + 1)..] == ".default" && scopes.Count != 1))
            {
                return false;
            }

            resource = prefix;
        }

        return true;
    }

    private static bool IsResource(string value)
    {
        if (TryGuid(value, out _))
        {
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString()
            || uri.Authority.Length == 0 || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            return false;
        }

        // Inspect only the original authority for an empty userinfo delimiter. URI
        // canonicalization must not change the resource used for ordinal matching.
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 1)
        {
            return false;
        }

        var authority = value.AsSpan(schemeEnd + 3);
        var pathStart = authority.IndexOf('/');
        return !(pathStart < 0 ? authority : authority[..pathStart]).Contains('@');
    }

    // U+FEFF is neither whitespace nor a control in .NET, but ECMAScript \s (used by
    // the request schema) includes it; reject it so both readers agree.
    private static bool IsWhitespaceOrControl(char value) =>
        char.IsWhiteSpace(value) || char.IsControl(value) || value == '\uFEFF';
}
