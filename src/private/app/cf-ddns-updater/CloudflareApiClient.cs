using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Hcoona.CfDdnsUpdater;

internal sealed class CloudflareApiClient(
    HttpClient httpClient,
    CloudflareConfiguration configuration)
{
    private const int DefaultPageSize = 100;

    public async Task<IReadOnlyList<CloudflareZone>> ListZonesByExactNameAsync(
        string exactName,
        CancellationToken cancellationToken)
    {
        using Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.CloudflareZoneListingActivityName,
            ActivityKind.Client);

        activity?.SetTag(CloudflareTelemetry.ZoneNameTagName, exactName);

        try
        {
            List<CloudflareZone> zones = [];
            int page = 1;
            while (true)
            {
                using Activity? pageActivity = CloudflareTelemetry.ActivitySource.StartActivity(
                    CloudflareTelemetry.CloudflareZoneListingPageActivityName,
                    ActivityKind.Client);
                pageActivity?.SetTag(CloudflareTelemetry.PageTagName, page);

                CloudflareZonesResponseDto response;
                try
                {
                    response = await SendAsync(
                            HttpMethod.Get,
                            $"zones?name={Uri.EscapeDataString(exactName)}"
                            + $"&page={page}&per_page={DefaultPageSize}",
                            CloudflareJsonContext.Default.CloudflareZonesResponseDto,
                            pageActivity,
                            cancellationToken)
                        .ConfigureAwait(false);

                    ThrowIfFailed(response, $"zone lookup for \"{exactName}\"");
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        throw;
                    }

                    CloudflareTelemetry.MarkFailure(pageActivity, ex);
                    throw;
                }

                foreach (CloudflareZoneDto? zoneDto in response.Result ?? [])
                {
                    if (zoneDto is null || zoneDto.Id is null || zoneDto.Name is null)
                    {
                        continue;
                    }

                    zones.Add(new CloudflareZone(zoneDto.Id, zoneDto.Name));
                }

                int totalPages = response.ResultInfo?.TotalPages ?? 1;
                CloudflareTelemetry.MarkOutcome(pageActivity, "success");
                if (page >= totalPages)
                {
                    break;
                }

                page++;
            }

            CloudflareTelemetry.PropagateCloudflareResponseMetadata(activity);
            CloudflareTelemetry.MarkOutcome(activity, "success");
            return zones;
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

    public async Task<IReadOnlyList<CloudflareDnsRecord>> ListDnsRecordsByExactNameAsync(
        string zoneId,
        string exactName,
        CancellationToken cancellationToken)
    {
        using Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.CloudflareDnsRecordListingActivityName,
            ActivityKind.Client);

        activity?.SetTag(CloudflareTelemetry.ZoneIdTagName, zoneId);
        activity?.SetTag(CloudflareTelemetry.DomainTagName, exactName);

        try
        {
            List<CloudflareDnsRecord> records = [];

            int page = 1;
            while (true)
            {
                using Activity? pageActivity = CloudflareTelemetry.ActivitySource.StartActivity(
                    CloudflareTelemetry.CloudflareDnsRecordListingPageActivityName,
                    ActivityKind.Client);
                pageActivity?.SetTag(CloudflareTelemetry.PageTagName, page);

                CloudflareDnsRecordsResponseDto response;
                try
                {
                    string relativeUri =
                        $"zones/{Uri.EscapeDataString(zoneId)}/dns_records"
                        + $"?name={Uri.EscapeDataString(exactName)}"
                        + $"&page={page}&per_page={DefaultPageSize}";

                    response = await SendAsync(
                            HttpMethod.Get,
                            relativeUri,
                            CloudflareJsonContext.Default.CloudflareDnsRecordsResponseDto,
                            pageActivity,
                            cancellationToken)
                        .ConfigureAwait(false);

                    ThrowIfFailed(
                        response,
                        $"DNS record lookup for \"{exactName}\" in zone \"{zoneId}\"");
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        throw;
                    }

                    CloudflareTelemetry.MarkFailure(pageActivity, ex);
                    throw;
                }

                foreach (CloudflareDnsRecordDto? recordDto in response.Result ?? [])
                {
                    if (recordDto is null ||
                        recordDto.Id is null ||
                        recordDto.Name is null ||
                        recordDto.Type is null)
                    {
                        continue;
                    }

                    records.Add(new CloudflareDnsRecord(
                        recordDto.Id,
                        recordDto.Name,
                        recordDto.Type,
                        recordDto.Content,
                        recordDto.Proxied,
                        recordDto.Ttl,
                        recordDto.Comment,
                        recordDto.Tags,
                        recordDto.Settings));
                }

                int totalPages = response.ResultInfo?.TotalPages ?? 1;
                CloudflareTelemetry.MarkOutcome(pageActivity, "success");
                if (page >= totalPages)
                {
                    break;
                }

                page++;
            }

            CloudflareTelemetry.PropagateCloudflareResponseMetadata(activity);
            CloudflareTelemetry.MarkOutcome(activity, "success");
            return records;
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

    public async Task<CloudflareDnsRecord> CreateDnsRecordAsync(
        string zoneId,
        CloudflareDnsRecordMutationRequestDto request,
        CancellationToken cancellationToken)
    {
        using Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.CloudflareDnsRecordCreateActivityName,
            ActivityKind.Client);

        activity?.SetTag(CloudflareTelemetry.ZoneIdTagName, zoneId);
        activity?.SetTag(CloudflareTelemetry.DomainTagName, request.Name);
        activity?.SetTag(CloudflareTelemetry.RecordTypeTagName, request.Type);

        try
        {
            CloudflareDnsRecordMutationResponseDto response = await SendAsync(
                    HttpMethod.Post,
                    $"zones/{Uri.EscapeDataString(zoneId)}/dns_records",
                    JsonContent.Create(
                        request,
                        CloudflareJsonContext.Default.CloudflareDnsRecordMutationRequestDto),
                    CloudflareJsonContext.Default.CloudflareDnsRecordMutationResponseDto,
                    activity,
                    cancellationToken)
            .ConfigureAwait(false);

            ThrowIfFailed(
            response,
            $"DNS record create in zone \"{zoneId}\"");

            CloudflareDnsRecord record = ToCloudflareDnsRecord(response.Result);
            CloudflareTelemetry.MarkOutcome(activity, "created");
            return record;
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

    public async Task<CloudflareDnsRecord> UpdateDnsRecordAsync(
        string zoneId,
        string recordId,
        CloudflareDnsRecordMutationRequestDto request,
        CancellationToken cancellationToken)
    {
        using Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.CloudflareDnsRecordUpdateActivityName,
            ActivityKind.Client);

        activity?.SetTag(CloudflareTelemetry.ZoneIdTagName, zoneId);
        activity?.SetTag(CloudflareTelemetry.DomainTagName, request.Name);
        activity?.SetTag(CloudflareTelemetry.RecordTypeTagName, request.Type);

        try
        {
            string zonePath = $"zones/{Uri.EscapeDataString(zoneId)}/dns_records";
            string relativeUri = zonePath + "/" + Uri.EscapeDataString(recordId);

            CloudflareDnsRecordMutationResponseDto response = await SendAsync(
                    HttpMethod.Put,
                    relativeUri,
                    JsonContent.Create(
                        request,
                        CloudflareJsonContext.Default.CloudflareDnsRecordMutationRequestDto),
                    CloudflareJsonContext.Default.CloudflareDnsRecordMutationResponseDto,
                    activity,
                    cancellationToken)
            .ConfigureAwait(false);

            ThrowIfFailed(
            response,
            $"DNS record update for \"{recordId}\" in zone \"{zoneId}\"");

            CloudflareDnsRecord record = ToCloudflareDnsRecord(response.Result);
            CloudflareTelemetry.MarkOutcome(activity, "updated");
            return record;
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

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string relativeUri,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        Activity? activity,
        CancellationToken cancellationToken)
        where TResponse : CloudflareApiResponseBase
        => await SendAsync(
                method,
                relativeUri,
                null,
                jsonTypeInfo,
                activity,
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<TResponse> SendAsync<TResponse>(
        HttpMethod method,
        string relativeUri,
        HttpContent? content,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        Activity? activity,
        CancellationToken cancellationToken)
        where TResponse : CloudflareApiResponseBase
    {
        using HttpRequestMessage request = new(method, relativeUri);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", configuration.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = content;

        using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        CloudflareTelemetry.CaptureCloudflareResponseMetadata(activity, response);

        if (!response.IsSuccessStatusCode)
        {
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            throw new CloudflareApiException(
                BuildHttpFailureMessage(relativeUri, response.StatusCode, responseText));
        }

        await using Stream responseStream =
            await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        TResponse? payload = await JsonSerializer.DeserializeAsync(
                responseStream,
                jsonTypeInfo,
                cancellationToken)
            .ConfigureAwait(false);

        return payload ?? throw new CloudflareApiException(
            $"Cloudflare API returned an empty response for {relativeUri}.");
    }

    private static CloudflareDnsRecord ToCloudflareDnsRecord(CloudflareDnsRecordDto? recordDto)
    {
        if (recordDto is null ||
            recordDto.Id is null ||
            recordDto.Name is null ||
            recordDto.Type is null)
        {
            throw new CloudflareApiException(
                "Cloudflare API returned an invalid DNS record payload.");
        }

        return new CloudflareDnsRecord(
            recordDto.Id,
            recordDto.Name,
            recordDto.Type,
            recordDto.Content,
            recordDto.Proxied,
            recordDto.Ttl,
            recordDto.Comment,
            recordDto.Tags,
            recordDto.Settings);
    }

    private static void ThrowIfFailed(
        CloudflareApiResponseBase response,
        string operation)
    {
        if (response.Success)
        {
            return;
        }

        string errorText = BuildApiErrorText(response);
        throw new CloudflareApiException($"Cloudflare API failed during {operation}.{errorText}");
    }

    private static string BuildApiErrorText(CloudflareApiResponseBase response)
    {
        List<string> messages = [];
        foreach (CloudflareApiErrorDto? error in response.Errors ?? [])
        {
            if (error?.Message is { Length: > 0 } message)
            {
                messages.Add(message);
            }
        }

        foreach (CloudflareApiErrorDto? message in response.Messages ?? [])
        {
            if (message?.Message is { Length: > 0 } text)
            {
                messages.Add(text);
            }
        }

        return messages.Count == 0 ? string.Empty : " " + string.Join(" ", messages);
    }

    private static string BuildHttpFailureMessage(
        string relativeUri,
        System.Net.HttpStatusCode statusCode,
        string responseText)
    {
        string message = $"Cloudflare API returned HTTP {(int)statusCode} for {relativeUri}.";
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            message += $" Response: {responseText.Trim()}";
        }

        return message;
    }
}
