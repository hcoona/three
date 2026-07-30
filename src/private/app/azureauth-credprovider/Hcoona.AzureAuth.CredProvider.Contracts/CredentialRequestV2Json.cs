using System.Text.Json;

namespace Hcoona.AzureAuth.CredProvider.Contracts;

/// <summary>JSON helpers for the v2 credential request contract.</summary>
public static class CredentialRequestV2Json
{
    private static readonly JsonSerializerOptions SerializerOptions =
        ContractJson.CreateSerializerOptions();

    public static string Serialize(CredentialRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CredentialRequestV2Policy.EnsureValid(request);
        return JsonSerializer.Serialize(request, SerializerOptions);
    }

    public static CredentialRequestV2 Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        CredentialRequestV2 request =
            JsonSerializer.Deserialize<CredentialRequestV2>(json, SerializerOptions)
            ?? throw new JsonException("Credential request v2 JSON did not contain a request.");
        CredentialRequestV2Policy.EnsureValid(request);
        return request;
    }
}
