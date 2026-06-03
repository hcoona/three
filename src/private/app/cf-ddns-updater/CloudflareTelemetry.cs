using System.Diagnostics;
using System.Net.Http.Headers;

namespace Hcoona.CfDdnsUpdater;

internal static class CloudflareTelemetry
{
    public const string ActivitySourceName = "Hcoona.CfDdnsUpdater";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public const string RunActivityName = "cf-ddns.run";
    public const string ConfigurationLoadActivityName = "cf-ddns.config.load";
    public const string ReconciliationTargetActivityName = "cf-ddns.target.reconcile";
    public const string IpDiscoveryActivityName = "cf-ddns.ip.discover";
    public const string ZoneResolutionActivityName = "cf-ddns.zone.resolve";
    public const string CloudflareZoneListingActivityName = "cf-ddns.cloudflare.zones.list";
    public const string CloudflareZoneListingPageActivityName =
        "cf-ddns.cloudflare.zones.list.page";
    public const string CloudflareDnsRecordListingActivityName =
        "cf-ddns.cloudflare.dns.records.list";
    public const string CloudflareDnsRecordListingPageActivityName =
        "cf-ddns.cloudflare.dns.records.list.page";
    public const string CloudflareDnsRecordCreateActivityName =
        "cf-ddns.cloudflare.dns.record.create";
    public const string CloudflareDnsRecordUpdateActivityName =
        "cf-ddns.cloudflare.dns.record.update";

    public const string DomainTagName = "cf.ddns.domain";
    public const string ZoneNameTagName = "cf.ddns.zone_name";
    public const string ZoneIdTagName = "cf.ddns.zone_id";
    public const string RecordTypeTagName = "cf.ddns.record_type";
    public const string TargetFamilyTagName = "cf.ddns.target_family";
    public const string OutcomeTagName = "cf.ddns.outcome";
    public const string PageTagName = "cf.ddns.page";
    public const string AddressTagName = "cf.ddns.address";
    public const string CloudflareRayIdTagName = "cloudflare.ray_id";
    public const string CloudflareRequestIdTagName = "cloudflare.request_id";
    public const string HttpStatusCodeTagName = "http.response.status_code";

    public static void MarkOutcome(Activity? activity, string outcome)
        => activity?.SetTag(OutcomeTagName, outcome);

    public static void MarkFailure(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag(OutcomeTagName, "failure");
    }

    public static void MarkRunExitCode(Activity? activity, int exitCode)
    {
        if (exitCode != 0)
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"Exited with code {exitCode}.");
        }
    }

    public static void CaptureCloudflareResponseMetadata(
        Activity? activity,
        HttpResponseMessage response)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(HttpStatusCodeTagName, (int)response.StatusCode);

        if (TryGetHeaderValue(response.Headers, "cf-ray", out string? rayId))
        {
            SetCloudflareResponseTag(activity, CloudflareRayIdTagName, rayId!);
        }

        if (TryGetHeaderValue(response.Headers, "cf-request-id", out string? requestId))
        {
            SetCloudflareResponseTag(activity, CloudflareRequestIdTagName, requestId!);
        }
    }

    public static void PropagateCloudflareResponseMetadata(Activity? activity)
    {
        if (activity is null)
        {
            return;
        }

        if (activity.GetTagItem(CloudflareRayIdTagName) is string rayId &&
            rayId.Length > 0)
        {
            PropagateTagToAncestors(activity, CloudflareRayIdTagName, rayId);
        }

        if (activity.GetTagItem(CloudflareRequestIdTagName) is string requestId &&
            requestId.Length > 0)
        {
            PropagateTagToAncestors(activity, CloudflareRequestIdTagName, requestId);
        }
    }

    private static void SetCloudflareResponseTag(
        Activity activity,
        string tagName,
        string value)
    {
        SetCloudflareResponseTag(activity, tagName, value, includeAncestors: true);
    }

    private static void SetCloudflareResponseTag(
        Activity activity,
        string tagName,
        string value,
        bool includeAncestors)
    {
        if (activity.GetTagItem(tagName) is string existingValue &&
            existingValue.Length > 0)
        {
            if (!ContainsTagValue(existingValue, value))
            {
                activity.SetTag(tagName, $"{existingValue}, {value}");
            }
        }
        else
        {
            activity.SetTag(tagName, value);
        }

        if (!includeAncestors)
        {
            return;
        }

        for (Activity? parent = activity.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.GetTagItem(tagName) is string parentValue &&
                parentValue.Length > 0)
            {
                if (!ContainsTagValue(parentValue, value))
                {
                    parent.SetTag(tagName, $"{parentValue}, {value}");
                }
            }
            else
            {
                parent.SetTag(tagName, value);
            }
        }
    }

    private static bool ContainsTagValue(string existingValue, string value)
    {
        foreach (string candidate in existingValue.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(candidate, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void PropagateTagToAncestors(
        Activity activity,
        string tagName,
        string value)
    {
        for (Activity? parent = activity.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.GetTagItem(tagName) is null)
            {
                parent.SetTag(tagName, value);
            }
        }
    }

    private static bool TryGetHeaderValue(
        HttpHeaders headers,
        string headerName,
        out string? value)
    {
        if (headers.TryGetValues(headerName, out IEnumerable<string>? values))
        {
            foreach (string candidate in values)
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    value = candidate;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }
}
