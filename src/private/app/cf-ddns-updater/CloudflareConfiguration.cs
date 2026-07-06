using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace Hcoona.CfDdnsUpdater;

internal sealed record CloudflareConfiguration(
    string ApiToken,
    ImmutableArray<string> Domains,
    bool DisableIpv6)
{
    public static CloudflareConfiguration Create(CloudflareOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!CloudflareConfigurationValidator.TryCreate(
                options,
                out CloudflareConfiguration? configuration,
                out string[] errors))
        {
            throw new OptionsValidationException(
                nameof(CloudflareOptions),
                typeof(CloudflareOptions),
                errors);
        }

        return configuration!;
    }
}
