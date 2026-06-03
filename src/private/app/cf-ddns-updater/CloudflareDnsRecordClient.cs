namespace Hcoona.CfDdnsUpdater;

internal sealed class CloudflareDnsRecordClient(CloudflareApiClient apiClient)
{
    public Task<IReadOnlyList<CloudflareDnsRecord>> ListExactNameRecordsAsync(
        string zoneId,
        string exactName,
        CancellationToken cancellationToken)
        => apiClient.ListDnsRecordsByExactNameAsync(zoneId, exactName, cancellationToken);

    public Task<CloudflareDnsRecord> CreateDnsOnlyRecordAsync(
        string zoneId,
        string name,
        string type,
        string content,
        CancellationToken cancellationToken)
        => apiClient.CreateDnsRecordAsync(
            zoneId,
            new CloudflareDnsRecordMutationRequestDto
            {
                Name = name,
                Type = type,
                Content = content,
                Proxied = false,
                Ttl = 1,
            },
            cancellationToken);

    public Task<CloudflareDnsRecord> UpdateContentAsync(
        string zoneId,
        CloudflareDnsRecord record,
        string content,
        CancellationToken cancellationToken)
        => apiClient.UpdateDnsRecordAsync(
            zoneId,
            record.Id,
            new CloudflareDnsRecordMutationRequestDto
            {
                Name = record.Name,
                Type = record.Type,
                Content = content,
                Comment = record.Comment,
                Tags = record.Tags,
                Settings = record.Settings,
                Proxied = record.Proxied,
                Ttl = record.Ttl,
            },
            cancellationToken);
}
