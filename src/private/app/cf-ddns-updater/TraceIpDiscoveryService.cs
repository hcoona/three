using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Hcoona.CfDdnsUpdater;

internal interface ITraceIpDiscoveryService
{
    ValueTask<IPAddress> DiscoverAsync(AddressFamily family, CancellationToken cancellationToken);
}

internal sealed partial class TraceIpDiscoveryService(
    HttpClient httpClient,
    ILogger<TraceIpDiscoveryService> logger) : ITraceIpDiscoveryService
{
    private static readonly Uri TraceIpv4Uri =
        new("https://1.1.1.1/cdn-cgi/trace", UriKind.Absolute);
    private static readonly Uri TraceIpv6Uri =
        new("https://[2606:4700:4700::1111]/cdn-cgi/trace", UriKind.Absolute);

    public async ValueTask<IPAddress> DiscoverAsync(
        AddressFamily family,
        CancellationToken cancellationToken)
    {
        using Activity? activity = CloudflareTelemetry.ActivitySource.StartActivity(
            CloudflareTelemetry.IpDiscoveryActivityName,
            ActivityKind.Client);

        activity?.SetTag(CloudflareTelemetry.TargetFamilyTagName, family.ToString());

        string traceResponse;
        try
        {
            traceResponse = await ReadTraceResponseAsync(
                    activity,
                    family,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CloudflareTelemetry.MarkFailure(activity, ex);
            LogDiscoveryFailed(logger, family, ex.Message);
            throw;
        }

        if (!TryParseTraceResponse(
                traceResponse,
                family,
                out IPAddress? address,
                out string error))
        {
            TraceIpDiscoveryException exception = new(error);
            CloudflareTelemetry.MarkFailure(activity, exception);
            LogDiscoveryFailed(logger, family, error);
            throw exception;
        }

        ArgumentNullException.ThrowIfNull(address);
        IPAddress discoveredAddress = address;
        activity?.SetTag("cf.ddns.address", discoveredAddress.ToString());
        CloudflareTelemetry.MarkOutcome(activity, "success");
        LogDiscoverySucceeded(logger, family, discoveredAddress);
        return discoveredAddress;
    }

    private async Task<string> ReadTraceResponseAsync(
        Activity? activity,
        AddressFamily family,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(family);
        using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        CloudflareTelemetry.CaptureCloudflareResponseMetadata(activity, response);

        if (!response.IsSuccessStatusCode)
        {
            string responseText = await response.Content.ReadAsStringAsync(
                    cancellationToken)
                .ConfigureAwait(false);

            string error =
                $"Cloudflare Trace returned HTTP {(int)response.StatusCode} for {family}.";
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                error += $" Response: {responseText.Trim()}";
            }

            throw new TraceIpDiscoveryException(error);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HttpRequestMessage CreateRequest(AddressFamily family)
    {
        HttpRequestMessage request = new(HttpMethod.Get, GetTraceUri(family));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return request;
    }

    private static Uri GetTraceUri(AddressFamily family)
        => family switch
        {
            AddressFamily.InterNetwork => TraceIpv4Uri,
            AddressFamily.InterNetworkV6 => TraceIpv6Uri,
            _ => throw new ArgumentOutOfRangeException(
                nameof(family),
                family,
                "Only IPv4 and IPv6 address families are supported."),
        };

    private static bool TryParseTraceResponse(
        string traceResponse,
        AddressFamily family,
        out IPAddress? address,
        out string error)
    {
        address = null;
        error = string.Empty;

        bool sawIpEntry = false;
        foreach (string rawLine in traceResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("ip=", StringComparison.Ordinal))
            {
                continue;
            }

            if (sawIpEntry)
            {
                error = "Cloudflare Trace response contained multiple ip= entries.";
                return false;
            }

            sawIpEntry = true;
            string addressText = line[3..].Trim();
            if (addressText.Length == 0)
            {
                error = "Cloudflare Trace response contained an empty ip= entry.";
                return false;
            }

            if (!IPAddress.TryParse(addressText, out IPAddress? parsedAddress))
            {
                error = $"Cloudflare Trace returned an invalid IP address: {addressText}.";
                return false;
            }

            if (parsedAddress.AddressFamily != family)
            {
                error =
                    $"Cloudflare Trace returned a {parsedAddress.AddressFamily} "
                    + $"address for {family}.";
                return false;
            }

            if (family == AddressFamily.InterNetworkV6 &&
                IsIPv4EmbeddedOrMappedIpv6(parsedAddress))
            {
                error =
                    $"Cloudflare Trace returned an IPv4-embedded IPv6 address: "
                    + $"{parsedAddress}.";
                return false;
            }

            if (!IsPublicIpAddress(parsedAddress))
            {
                error = $"Cloudflare Trace returned a non-public IP address: {parsedAddress}.";
                return false;
            }

            address = parsedAddress;
        }

        if (sawIpEntry)
        {
            return true;
        }

        error = "Cloudflare Trace response did not contain an ip= entry.";
        return false;
    }

    private static bool IsPublicIpAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> ipv4Bytes = stackalloc byte[4];
            address.TryWriteBytes(ipv4Bytes, out _);
            return IsPublicIpv4(ipv4Bytes);
        }

        Span<byte> ipv6Bytes = stackalloc byte[16];
        address.TryWriteBytes(ipv6Bytes, out _);
        return IsPublicIpv6(ipv6Bytes);
    }

    private static bool IsPublicIpv4(ReadOnlySpan<byte> bytes)
    {
        byte first = bytes[0];
        byte second = bytes[1];
        byte third = bytes[2];
        byte fourth = bytes[3];

        if (first is 0 or 10 or 127 or >= 224)
        {
            return false;
        }

        if (first == 100 && second >= 64 && second <= 127)
        {
            return false;
        }

        if (first == 169 && second == 254)
        {
            return false;
        }

        if (first == 172 && second >= 16 && second <= 31)
        {
            return false;
        }

        if (first == 192 && second == 0 && third == 0 && fourth is not 9 and not 10)
        {
            return false;
        }

        if (first == 192 && second == 0 && third == 2)
        {
            return false;
        }

        if (first == 192 && second == 31 && third == 196)
        {
            return false;
        }

        if (first == 192 && second == 52 && third == 193)
        {
            return false;
        }

        if (first == 192 && second == 88 && third == 99)
        {
            return false;
        }

        if (first == 192 && second == 168)
        {
            return false;
        }

        if (first == 192 && second == 175 && third == 48)
        {
            return false;
        }

        if (first == 198 && (second == 18 || second == 19))
        {
            return false;
        }

        if (first == 198 && second == 51 && third == 100)
        {
            return false;
        }

        if (first == 203 && second == 0 && third == 113)
        {
            return false;
        }

        return true;
    }

    private static bool IsPublicIpv6(ReadOnlySpan<byte> bytes)
    {
        if (IsIPv6Unspecified(bytes) ||
            IsIPv6Loopback(bytes) ||
            IsIPv4CompatibleIpv6(bytes) ||
            bytes[0] == 0xFF ||
            IsIPv6LinkLocal(bytes) ||
            IsIPv6SiteLocal(bytes) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x00 }, 23) ||
            HasPrefix(
                bytes,
                stackalloc byte[] { 0x00, 0x64, 0xFF, 0x9B, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00 },
                96) ||
            HasPrefix(
                bytes,
                stackalloc byte[] { 0x00, 0x64, 0xFF, 0x9B, 0x00, 0x01 },
                48) ||
            HasPrefix(
                bytes,
                stackalloc byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00 },
                64) ||
            HasPrefix(
                bytes,
                stackalloc byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x01 },
                64) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x00, 0x00 }, 32) ||
            HasPrefix(
                bytes,
                stackalloc byte[] { 0x20, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 },
                128) ||
            HasPrefix(
                bytes,
                stackalloc byte[] { 0x20, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 },
                128) ||
            HasPrefix(
                bytes,
                stackalloc byte[] { 0x20, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03 },
                128) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x00, 0x02, 0x00, 0x00 }, 48) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x00, 0x03 }, 32) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x00, 0x04, 0x01, 0x12 }, 48) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x00, 0x10 }, 28) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x00, 0x20 }, 28) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x00, 0x30 }, 28) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x01, 0x0D, 0xB8 }, 32) ||
            HasPrefix(bytes, stackalloc byte[] { 0x20, 0x02 }, 16) ||
            HasPrefix(bytes, stackalloc byte[] { 0x26, 0x20, 0x00, 0x4F, 0x80, 0x00 }, 48) ||
            HasPrefix(bytes, stackalloc byte[] { 0x3F, 0xFE }, 16) ||
            HasPrefix(bytes, stackalloc byte[] { 0x3F, 0xFF, 0x00 }, 20) ||
            HasPrefix(bytes, stackalloc byte[] { 0x5F, 0x00 }, 16) ||
            HasPrefix(bytes, stackalloc byte[] { 0xFC }, 7))
        {
            return false;
        }

        return true;
    }

    private static bool IsIPv4CompatibleIpv6(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x00 &&
           bytes[1] == 0x00 &&
           bytes[2] == 0x00 &&
           bytes[3] == 0x00 &&
           bytes[4] == 0x00 &&
           bytes[5] == 0x00 &&
           bytes[6] == 0x00 &&
           bytes[7] == 0x00 &&
           bytes[8] == 0x00 &&
           bytes[9] == 0x00 &&
           bytes[10] == 0x00 &&
           bytes[11] == 0x00;

    private static bool IsIPv4EmbeddedOrMappedIpv6(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        return IsIPv4CompatibleIpv6(bytes);
    }

    private static bool IsIPv6Unspecified(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x00 &&
           bytes[1] == 0x00 &&
           bytes[2] == 0x00 &&
           bytes[3] == 0x00 &&
           bytes[4] == 0x00 &&
           bytes[5] == 0x00 &&
           bytes[6] == 0x00 &&
           bytes[7] == 0x00 &&
           bytes[8] == 0x00 &&
           bytes[9] == 0x00 &&
           bytes[10] == 0x00 &&
           bytes[11] == 0x00 &&
           bytes[12] == 0x00 &&
           bytes[13] == 0x00 &&
           bytes[14] == 0x00 &&
           bytes[15] == 0x00;

    private static bool IsIPv6Loopback(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0x00 &&
           bytes[1] == 0x00 &&
           bytes[2] == 0x00 &&
           bytes[3] == 0x00 &&
           bytes[4] == 0x00 &&
           bytes[5] == 0x00 &&
           bytes[6] == 0x00 &&
           bytes[7] == 0x00 &&
           bytes[8] == 0x00 &&
           bytes[9] == 0x00 &&
           bytes[10] == 0x00 &&
           bytes[11] == 0x00 &&
           bytes[12] == 0x00 &&
           bytes[13] == 0x00 &&
           bytes[14] == 0x00 &&
           bytes[15] == 0x01;

    private static bool IsIPv6LinkLocal(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;

    private static bool IsIPv6SiteLocal(ReadOnlySpan<byte> bytes)
        => bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0xC0;

    private static bool HasPrefix(
        ReadOnlySpan<byte> address,
        ReadOnlySpan<byte> prefix,
        int prefixLengthBits)
    {
        int fullBytes = prefixLengthBits / 8;
        for (int index = 0; index < fullBytes; index++)
        {
            if (address[index] != prefix[index])
            {
                return false;
            }
        }

        int remainingBits = prefixLengthBits % 8;
        if (remainingBits == 0)
        {
            return true;
        }

        byte mask = (byte)(0xFF << (8 - remainingBits));
        return (address[fullBytes] & mask) == (prefix[fullBytes] & mask);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Cloudflare Trace discovered {AddressFamily} address {Address}.")]
    private static partial void LogDiscoverySucceeded(
        ILogger logger,
        AddressFamily addressFamily,
        IPAddress address);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Cloudflare Trace discovery failed for {AddressFamily}: {ErrorMessage}")]
    private static partial void LogDiscoveryFailed(
        ILogger logger,
        AddressFamily addressFamily,
        string errorMessage);
}

internal sealed class CloudflareTraceRetryHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan MinimumJitterDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan MaximumJitterDelay = TimeSpan.FromMilliseconds(350);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            HttpRequestMessage retryRequest = attempt == 1 ? request : CloneRequest(request);
            HttpResponseMessage? response = null;
            bool returnResponse = false;
            try
            {
                response = await base.SendAsync(
                        retryRequest,
                        cancellationToken)
                    .ConfigureAwait(false);

                await response.Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
                CloudflareTelemetry.CaptureCloudflareResponseMetadata(
                    Activity.Current,
                    response);

                if (!IsRetryableStatusCode(response.StatusCode) || attempt == MaxAttempts)
                {
                    returnResponse = true;
                    return response;
                }

                TimeSpan delay = GetDelay(response.Headers.RetryAfter, attempt);
                response.Dispose();
                response = null;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                lastException = ex;
                response?.Dispose();
                response = null;
                await Task.Delay(GetJitterDelay(attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                response?.Dispose();
                response = null;
                HttpRequestException wrapped = new(
                    "Cloudflare Trace request failed while reading the response body.",
                    ex);

                if (attempt < MaxAttempts)
                {
                    lastException = wrapped;
                    await Task.Delay(GetJitterDelay(attempt), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                throw wrapped;
            }
            catch (OperationCanceledException ex) when (
                attempt < MaxAttempts &&
                !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                response?.Dispose();
                response = null;
                await Task.Delay(GetJitterDelay(attempt), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (!returnResponse)
                {
                    response?.Dispose();
                }
            }
        }

        throw lastException ?? new HttpRequestException(
            "Cloudflare Trace request failed after retries.");
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        HttpRequestMessage clone = new(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
           ((int)statusCode >= 500 && (int)statusCode <= 599);

    internal static TimeSpan GetDelay(RetryConditionHeaderValue? retryAfter, int attempt)
    {
        if (retryAfter?.Delta is TimeSpan delta && delta >= TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is DateTimeOffset retryAfterDate)
        {
            TimeSpan serverDelay = retryAfterDate - DateTimeOffset.UtcNow;
            if (serverDelay > TimeSpan.Zero)
            {
                return serverDelay;
            }
        }

        return GetJitterDelay(attempt);
    }

    private static TimeSpan GetJitterDelay(int attempt)
    {
        double baseDelayMilliseconds = MinimumJitterDelay.TotalMilliseconds * attempt;
        double jitterMilliseconds = Random.Shared.NextDouble() *
            (MaximumJitterDelay.TotalMilliseconds - MinimumJitterDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(baseDelayMilliseconds + jitterMilliseconds);
    }
}

internal sealed class TraceIpDiscoveryException(string message)
    : InvalidOperationException(message);
