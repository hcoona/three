using Microsoft.Extensions.Logging;

namespace Hcoona.CfDdnsUpdater;

internal interface IReconciliationApp
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}

internal sealed partial class ReconciliationApp(
    ILogger<ReconciliationApp> logger,
    CloudflareConfiguration configuration) : IReconciliationApp
{
    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        LogStarted(logger, configuration.Domains.Length, configuration.DisableIpv6);

        return Task.FromResult(0);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message =
            "Cloudflare DDNS updater scaffold started for {DomainCount} domain(s); " +
            "IPv6 disabled: {DisableIpv6}.")]
    private static partial void LogStarted(
        ILogger logger,
        int domainCount,
        bool disableIpv6);

}
