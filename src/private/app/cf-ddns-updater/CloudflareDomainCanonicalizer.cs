using System.Globalization;
using System.Net;

namespace Hcoona.CfDdnsUpdater;

internal static class CloudflareDomainCanonicalizer
{
    private static readonly IdnMapping IdnMapping = new()
    {
        AllowUnassigned = false,
        UseStd3AsciiRules = true,
    };

    public static bool TryCanonicalize(
        string rawDomain,
        out string canonicalDomain,
        out string? error)
    {
        canonicalDomain = string.Empty;
        error = null;

        string domain = rawDomain.Trim();
        if (domain.Length == 0)
        {
            error = "Domains must not contain empty entries.";
            return false;
        }

        if (domain.EndsWith('.'))
        {
            domain = domain[..^1];
        }

        if (domain.Length == 0)
        {
            error = "Domains must not contain empty entries.";
            return false;
        }

        if (IPAddress.TryParse(domain, out _))
        {
            error = $"\"{rawDomain}\" is not a valid DNS hostname.";
            return false;
        }

        string asciiDomain;
        try
        {
            asciiDomain = IdnMapping.GetAscii(domain);
        }
        catch (ArgumentException)
        {
            error = $"\"{rawDomain}\" is not a valid DNS hostname.";
            return false;
        }

        asciiDomain = asciiDomain.ToLowerInvariant();
        if (!IsValidDnsHostname(asciiDomain))
        {
            error = $"\"{rawDomain}\" is not a valid DNS hostname.";
            return false;
        }

        canonicalDomain = asciiDomain;
        return true;
    }

    public static IEnumerable<string> EnumerateSuffixes(string canonicalDomain)
    {
        string remaining = canonicalDomain;
        while (remaining.Length > 0)
        {
            yield return remaining;

            int dotIndex = remaining.IndexOf('.');
            if (dotIndex < 0)
            {
                yield break;
            }

            remaining = remaining[(dotIndex + 1)..];
        }
    }

    private static bool IsValidDnsHostname(string value)
    {
        if (value.Length is < 1 or > 253)
        {
            return false;
        }

        ReadOnlySpan<char> remaining = value.AsSpan();
        int labelCount = 0;
        while (remaining.Length > 0)
        {
            int dotIndex = remaining.IndexOf('.');
            ReadOnlySpan<char> label = dotIndex >= 0 ? remaining[..dotIndex] : remaining;
            if (!IsValidDnsLabel(label))
            {
                return false;
            }

            labelCount++;
            if (dotIndex < 0)
            {
                break;
            }

            remaining = remaining[(dotIndex + 1)..];
            if (remaining.Length == 0)
            {
                return false;
            }
        }

        return labelCount >= 2;
    }

    private static bool IsValidDnsLabel(ReadOnlySpan<char> label)
    {
        if (label.Length is < 1 or > 63)
        {
            return false;
        }

        if (label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        foreach (char value in label)
        {
            if (value is >= 'a' and <= 'z')
            {
                continue;
            }

            if (value is >= '0' and <= '9')
            {
                continue;
            }

            if (value == '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
