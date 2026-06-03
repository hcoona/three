using System.Collections.Immutable;
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

            if (!CloudflareDomainCanonicalizer.TryCanonicalize(
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
