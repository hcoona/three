using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Hcoona.CfDdnsUpdater;

internal sealed partial class CloudflareZoneResolver(
    CloudflareApiClient apiClient,
    ILogger<CloudflareZoneResolver> logger)
{
    public async Task<CloudflareZone> ResolveAsync(
        string rawDomain,
        CancellationToken cancellationToken)
    {
        using Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.ZoneResolutionActivityName,
            ActivityKind.Client);

        try
        {
            activity?.SetTag(CloudflareTelemetry.DomainTagName, rawDomain);

            if (!CloudflareDomainCanonicalizer.TryCanonicalize(
                    rawDomain,
                    out string canonicalDomain,
                    out string? error))
            {
                CloudflareZoneResolutionException exception = new(error!);
                CloudflareTelemetry.MarkFailure(activity, exception);
                throw exception;
            }

            activity?.SetTag(CloudflareTelemetry.DomainTagName, canonicalDomain);

            foreach (
                string candidate in
                CloudflareDomainCanonicalizer.EnumerateSuffixes(canonicalDomain))
            {
                IReadOnlyList<CloudflareZone> zones = await apiClient.ListZonesByExactNameAsync(
                        candidate,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (zones.Count == 0)
                {
                    continue;
                }

                if (zones.Count > 1)
                {
                    throw new CloudflareZoneResolutionException(
                        $"Cloudflare returned multiple exact matches for zone "
                        + $"\"{candidate}\".");
                }

                CloudflareZone zone = zones[0];
                LogZoneResolved(logger, canonicalDomain, zone.Name, zone.Id);
                activity?.SetTag(CloudflareTelemetry.ZoneNameTagName, zone.Name);
                activity?.SetTag(CloudflareTelemetry.ZoneIdTagName, zone.Id);
                CloudflareTelemetry.MarkOutcome(activity, "success");
                return zone;
            }

            CloudflareZoneResolutionException noZoneException =
                new($"No Cloudflare zone was visible for \"{canonicalDomain}\".");
            CloudflareTelemetry.MarkFailure(activity, noZoneException);
            throw noZoneException;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CloudflareTelemetry.MarkFailure(activity, ex);
            throw;
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Resolved domain {Domain} to zone {ZoneName} ({ZoneId}).")]
    private static partial void LogZoneResolved(
        ILogger logger,
        string domain,
        string zoneName,
        string zoneId);
}
