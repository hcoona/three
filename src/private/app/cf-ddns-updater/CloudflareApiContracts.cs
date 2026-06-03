using System.Text.Json.Serialization;

namespace Hcoona.CfDdnsUpdater;

internal sealed record CloudflareZone(string Id, string Name);

internal sealed record CloudflareDnsRecord(
    string Id,
    string Name,
    string Type,
    string? Content,
    bool Proxied,
    int Ttl,
    string? Comment,
    string[]? Tags,
    CloudflareDnsRecordSettingsDto? Settings);

internal abstract class CloudflareApiResponseBase
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("errors")]
    public List<CloudflareApiErrorDto>? Errors { get; set; }

    [JsonPropertyName("messages")]
    public List<CloudflareApiErrorDto>? Messages { get; set; }
}

internal sealed class CloudflareZonesResponseDto : CloudflareApiResponseBase
{
    [JsonPropertyName("result")]
    public List<CloudflareZoneDto>? Result { get; set; }

    [JsonPropertyName("result_info")]
    public CloudflareResultInfoDto? ResultInfo { get; set; }
}

internal sealed class CloudflareDnsRecordsResponseDto : CloudflareApiResponseBase
{
    [JsonPropertyName("result")]
    public List<CloudflareDnsRecordDto>? Result { get; set; }

    [JsonPropertyName("result_info")]
    public CloudflareResultInfoDto? ResultInfo { get; set; }
}

internal sealed class CloudflareResultInfoDto
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("total_pages")]
    public int TotalPages { get; set; }
}

internal sealed class CloudflareApiErrorDto
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class CloudflareZoneDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

internal sealed class CloudflareDnsRecordDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("proxied")]
    public bool Proxied { get; set; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonPropertyName("settings")]
    public CloudflareDnsRecordSettingsDto? Settings { get; set; }
}

internal sealed class CloudflareDnsRecordMutationRequestDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tags")]
    public string[]? Tags { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("settings")]
    public CloudflareDnsRecordSettingsDto? Settings { get; set; }

    [JsonPropertyName("proxied")]
    public bool Proxied { get; set; }

    [JsonPropertyName("ttl")]
    public int Ttl { get; set; }
}

internal sealed class CloudflareDnsRecordSettingsDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("flatten_cname")]
    public bool? FlattenCname { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ipv4_only")]
    public bool? Ipv4Only { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("ipv6_only")]
    public bool? Ipv6Only { get; set; }
}

internal sealed class CloudflareDnsRecordMutationResponseDto : CloudflareApiResponseBase
{
    [JsonPropertyName("result")]
    public CloudflareDnsRecordDto? Result { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CloudflareApiErrorDto))]
[JsonSerializable(typeof(CloudflareDnsRecordDto))]
[JsonSerializable(typeof(CloudflareDnsRecordSettingsDto))]
[JsonSerializable(typeof(CloudflareResultInfoDto))]
[JsonSerializable(typeof(CloudflareZoneDto))]
[JsonSerializable(typeof(List<CloudflareApiErrorDto>))]
[JsonSerializable(typeof(List<CloudflareDnsRecordDto>))]
[JsonSerializable(typeof(List<CloudflareZoneDto>))]
[JsonSerializable(typeof(CloudflareDnsRecordMutationRequestDto))]
[JsonSerializable(typeof(CloudflareDnsRecordMutationResponseDto))]
[JsonSerializable(typeof(CloudflareZonesResponseDto))]
[JsonSerializable(typeof(CloudflareDnsRecordsResponseDto))]
internal sealed partial class CloudflareJsonContext : JsonSerializerContext
{
}

internal sealed class CloudflareApiException(string message) : InvalidOperationException(message);

internal sealed class CloudflareZoneResolutionException(string message) : InvalidOperationException(message);
