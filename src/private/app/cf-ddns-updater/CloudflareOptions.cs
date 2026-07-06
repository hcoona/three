using Microsoft.Extensions.Configuration;

namespace Hcoona.CfDdnsUpdater;

internal sealed class CloudflareOptions
{
    [ConfigurationKeyName("API_TOKEN")]
    public string? ApiToken { get; set; }

    [ConfigurationKeyName("DOMAINS")]
    public string? DomainsCsv { get; set; }

    [ConfigurationKeyName("DISABLE_IPV6")]
    public string? DisableIpv6Raw { get; set; }
}
