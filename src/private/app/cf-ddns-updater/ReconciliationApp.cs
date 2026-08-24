using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace Hcoona.CfDdnsUpdater;

internal interface IReconciliationApp
{
    Task<int> RunAsync(CancellationToken cancellationToken);
}

internal sealed partial class ReconciliationApp(
    ILogger<ReconciliationApp> logger,
    CloudflareConfiguration configuration,
    ITraceIpDiscoveryService traceIpDiscoveryService,
    CloudflareZoneResolver zoneResolver,
    CloudflareDnsRecordClient dnsRecordClient) : IReconciliationApp
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        LogStarted(logger, configuration.Domains.Length, configuration.DisableIpv6);

        RunSummary summary = new();
        Dictionary<AddressFamily, IPAddress> discoveredAddresses = [];

        foreach (AddressFamily family in EnumerateEnabledFamilies(configuration.DisableIpv6))
        {
            try
            {
                IPAddress address = await traceIpDiscoveryService.DiscoverAsync(
                        family,
                        cancellationToken)
                    .ConfigureAwait(false);

                discoveredAddresses[family] = address;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                summary.TargetFailures++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                summary.TargetFailures++;
            }
        }

        if (discoveredAddresses.Count == 0)
        {
            LogCompleted(
                logger,
                summary.CreatedTargets,
                summary.UpdatedTargets,
                summary.NoOpTargets,
                summary.TargetFailures);
            return summary.HasFailures ? 1 : 0;
        }

        foreach (string domain in configuration.Domains)
        {
            CloudflareZone zone;
            try
            {
                zone = await zoneResolver.ResolveAsync(domain, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                summary.TargetFailures += discoveredAddresses.Count;
                LogDomainResolutionFailed(
                    logger,
                    domain,
                    GetFailureMessage(ex));
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                summary.TargetFailures += discoveredAddresses.Count;
                LogDomainResolutionFailed(
                    logger,
                    domain,
                    GetFailureMessage(ex));
                continue;
            }

            foreach (AddressFamily family in EnumerateEnabledFamilies(configuration.DisableIpv6))
            {
                if (!discoveredAddresses.TryGetValue(family, out IPAddress? address))
                {
                    continue;
                }

                try
                {
                    ReconciliationOutcome outcome = await ReconcileAsync(
                            zone,
                            domain,
                            family,
                            address,
                            cancellationToken)
                        .ConfigureAwait(false);

                    summary.Add(outcome);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    summary.TargetFailures++;
                    LogTargetFailed(
                        logger,
                        domain,
                        zone.Name,
                        family,
                        GetFailureMessage(ex));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    summary.TargetFailures++;
                    LogTargetFailed(
                        logger,
                        domain,
                        zone.Name,
                        family,
                        GetFailureMessage(ex));
                }
            }
        }

        LogCompleted(
            logger,
            summary.CreatedTargets,
            summary.UpdatedTargets,
            summary.NoOpTargets,
            summary.TargetFailures);

        return summary.HasFailures ? 1 : 0;
    }

    private async Task<ReconciliationOutcome> ReconcileAsync(
        CloudflareZone zone,
        string domain,
        AddressFamily family,
        IPAddress address,
        CancellationToken cancellationToken)
    {
        using Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.ReconciliationTargetActivityName,
            ActivityKind.Internal);

        activity?.SetTag(CloudflareTelemetry.DomainTagName, domain);
        activity?.SetTag(CloudflareTelemetry.ZoneNameTagName, zone.Name);
        activity?.SetTag(CloudflareTelemetry.ZoneIdTagName, zone.Id);
        activity?.SetTag(CloudflareTelemetry.TargetFamilyTagName, family.ToString());
        activity?.SetTag(CloudflareTelemetry.AddressTagName, address.ToString());

        try
        {
            string recordType = GetRecordType(family);
            activity?.SetTag(CloudflareTelemetry.RecordTypeTagName, recordType);

            IReadOnlyList<CloudflareDnsRecord> records =
                await dnsRecordClient.ListExactNameRecordsAsync(
                        zone.Id,
                        domain,
                        cancellationToken)
                    .ConfigureAwait(false);

            LogRecordSnapshot(logger, domain, zone.Name, family, address, records.Count);

            if (
                records.Any(
                    existingRecord => string.Equals(
                        existingRecord.Type,
                        "CNAME",
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"CNAME record exists at \"{domain}\" in zone \"{zone.Name}\".");
            }

            List<CloudflareDnsRecord> matchingTypeRecords = [];
            foreach (CloudflareDnsRecord record in records)
            {
                if (string.Equals(record.Type, recordType, StringComparison.OrdinalIgnoreCase))
                {
                    matchingTypeRecords.Add(record);
                }
            }

            if (matchingTypeRecords.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Cloudflare returned multiple {recordType} records for \"{domain}\" "
                    + $"in zone \"{zone.Name}\".");
            }

            if (matchingTypeRecords.Count == 0)
            {
                await dnsRecordClient.CreateDnsOnlyRecordAsync(
                        zone.Id,
                        domain,
                        recordType,
                        address.ToString(),
                        cancellationToken)
                    .ConfigureAwait(false);

                LogCreated(logger, domain, zone.Name, family);
                CloudflareTelemetry.MarkOutcome(activity, "created");
                return ReconciliationOutcome.Created;
            }

            CloudflareDnsRecord matchingRecord = matchingTypeRecords[0];
            if (matchingRecord.Proxied)
            {
                throw new InvalidOperationException(
                    $"Matching {recordType} record for \"{domain}\" in zone "
                    + $"\"{zone.Name}\" is proxied.");
            }

            if (IsContentEqual(matchingRecord.Content, address))
            {
                LogNoOp(logger, domain, zone.Name, family);
                CloudflareTelemetry.MarkOutcome(activity, "no-op");
                return ReconciliationOutcome.NoOp;
            }

            await dnsRecordClient.UpdateContentAsync(
                    zone.Id,
                    matchingRecord,
                    address.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
            LogUpdated(logger, domain, zone.Name, family);
            CloudflareTelemetry.MarkOutcome(activity, "updated");
            return ReconciliationOutcome.Updated;
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

    private static bool IsContentEqual(string? content, IPAddress address)
        => IPAddress.TryParse(content, out IPAddress? parsedAddress)
        && parsedAddress.Equals(address);

    private static string GetRecordType(AddressFamily family)
        => family switch
        {
            AddressFamily.InterNetwork => "A",
            AddressFamily.InterNetworkV6 => "AAAA",
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null),
        };

    private static IEnumerable<AddressFamily> EnumerateEnabledFamilies(bool disableIpv6)
    {
        yield return AddressFamily.InterNetwork;
        if (!disableIpv6)
        {
            yield return AddressFamily.InterNetworkV6;
        }
    }

    private static string GetFailureMessage(Exception exception)
        => string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message =
            "Cloudflare DDNS updater started for {DomainCount} domain(s); " +
            "IPv6 disabled: {DisableIpv6}.")]
    private static partial void LogStarted(
        ILogger logger,
        int domainCount,
        bool disableIpv6);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message =
            "Cloudflare DDNS updater finished: {CreatedCount} created, "
            + "{UpdatedCount} updated, {NoOpCount} no-op, {FailedCount} failed.")]
    private static partial void LogCompleted(
        ILogger logger,
        int createdCount,
        int updatedCount,
        int noOpCount,
        int failedCount);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message =
            "Prepared reconciliation snapshot for domain {Domain} in zone {ZoneName}: "
            + "{AddressFamily} {Address} with {RecordCount} exact record(s).")]
    private static partial void LogRecordSnapshot(
        ILogger logger,
        string domain,
        string zoneName,
        AddressFamily addressFamily,
        IPAddress address,
        int recordCount);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Warning,
        Message = "Failed to resolve zone for {Domain}: {ErrorMessage}")]
    private static partial void LogDomainResolutionFailed(
        ILogger logger,
        string domain,
        string errorMessage);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Warning,
        Message =
            "Failed to reconcile {Domain} in zone {ZoneName} for {AddressFamily}: "
            + "{ErrorMessage}")]
    private static partial void LogTargetFailed(
        ILogger logger,
        string domain,
        string zoneName,
        AddressFamily addressFamily,
        string errorMessage);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Information,
        Message =
            "Created DNS record for {Domain} in zone {ZoneName} for "
            + "{AddressFamily}.")]
    private static partial void LogCreated(
        ILogger logger,
        string domain,
        string zoneName,
        AddressFamily addressFamily);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Information,
        Message =
            "Updated DNS record for {Domain} in zone {ZoneName} for "
            + "{AddressFamily}.")]
    private static partial void LogUpdated(
        ILogger logger,
        string domain,
        string zoneName,
        AddressFamily addressFamily);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Information,
        Message =
            "No DNS change required for {Domain} in zone {ZoneName} for "
            + "{AddressFamily}.")]
    private static partial void LogNoOp(
        ILogger logger,
        string domain,
        string zoneName,
        AddressFamily addressFamily);

}

internal enum ReconciliationOutcome
{
    NoOp,
    Created,
    Updated,
}

internal sealed class RunSummary
{
    public int CreatedTargets { get; private set; }

    public int UpdatedTargets { get; private set; }

    public int NoOpTargets { get; private set; }

    public int TargetFailures { get; set; }

    public bool HasFailures => TargetFailures > 0;

    public void Add(ReconciliationOutcome outcome)
    {
        switch (outcome)
        {
            case ReconciliationOutcome.Created:
                CreatedTargets++;
                break;
            case ReconciliationOutcome.Updated:
                UpdatedTargets++;
                break;
            case ReconciliationOutcome.NoOp:
                NoOpTargets++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        }
    }
}
