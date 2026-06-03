using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace Hcoona.CfDdnsUpdater;

internal sealed class CloudflareOptionsValidator : IValidateOptions<CloudflareOptions>
{
    public ValidateOptionsResult Validate(string? name, CloudflareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return CloudflareConfigurationValidator.TryCreate(
                options,
                out _,
                out string[] errors)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

internal static class CloudflareConfigurationValidator
{
    private static readonly IdnMapping IdnMapping = new()
    {
        AllowUnassigned = false,
        UseStd3AsciiRules = true,
    };

    public static bool TryCreate(
        CloudflareOptions options,
        out CloudflareConfiguration? configuration,
        out string[] errors)
    {
        List<string> failures = [];

        string? apiToken = NormalizeRequiredValue(options.ApiToken);
        if (apiToken is null)
        {
            failures.Add("The API token is required.");
        }

        bool disableIpv6 = false;
        if (!TryParseStrictBoolean(options.DisableIpv6Raw, out disableIpv6))
        {
            failures.Add(
                "DISABLE_IPV6 must be a strict boolean value of true " +
                "or false when specified.");
        }

        ImmutableArray<string> domains = ParseDomains(options.DomainsCsv, failures);
        if (domains.IsDefaultOrEmpty)
        {
            failures.Add("At least one usable domain is required.");
        }

        if (failures.Count > 0 || apiToken is null)
        {
            configuration = null;
            errors = [.. failures];
            return false;
        }

        configuration = new CloudflareConfiguration(apiToken, domains, disableIpv6);
        errors = [];
        return true;
    }

    private static ImmutableArray<string> ParseDomains(
        string? domainsCsv,
        List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(domainsCsv))
        {
            return [];
        }

        ImmutableArray<string>.Builder domains = ImmutableArray.CreateBuilder<string>();
        HashSet<string> seenDomains = new(StringComparer.OrdinalIgnoreCase);

        foreach (string rawDomain in domainsCsv.Split(','))
        {
            if (string.IsNullOrWhiteSpace(rawDomain))
            {
                continue;
            }

            if (!TryCanonicalizeDomain(
                    rawDomain,
                    out string canonicalDomain,
                    out string? error))
            {
                failures.Add(error!);
                continue;
            }

            if (seenDomains.Add(canonicalDomain))
            {
                domains.Add(canonicalDomain);
            }
        }

        return domains.ToImmutable();
    }

    private static bool TryCanonicalizeDomain(
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

    private static string? NormalizeRequiredValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryParseStrictBoolean(string? value, out bool result)
    {
        if (value is null)
        {
            result = false;
            return true;
        }

        string trimmedValue = value.Trim();
        if (trimmedValue.Length == 0)
        {
            result = false;
            return false;
        }

        if (string.Equals(trimmedValue, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(
                trimmedValue,
                bool.FalseString,
                StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }
}
