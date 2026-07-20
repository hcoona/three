using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Contracts;

/// <summary>
/// Strict public JSON facade for the v2 credential request contract.
/// This is the only supported public JSON entry point for <see cref="CredentialRequestV2" />.
/// Direct generic System.Text.Json use of <see cref="CredentialRequestV2" /> is intentionally
/// unsupported and throws.
/// </summary>
public static class CredentialRequestV2Json
{
    private static readonly JsonSerializerOptions SerializerOptions =
        CredentialRequestV2JsonContext.CreateSerializerOptions();

    /// <summary>
    /// Serializes a semantically valid v2 credential request to the strict public wire format.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="request" /> violates the v2 contract policy.
    /// </exception>
    public static string Serialize(CredentialRequestV2 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CredentialRequestV2Policy.EnsureValid(request);
        return JsonSerializer.Serialize(ToWire(request), SerializerOptions);
    }

    /// <summary>
    /// Deserializes the strict public wire format for a v2 credential request.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="json" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="JsonException">
    /// Thrown when <paramref name="json" /> is malformed or violates the strict wire syntax.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="json" /> is syntactically valid but violates the v2 contract
    /// policy.
    /// </exception>
    public static CredentialRequestV2 Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        CredentialRequestV2Wire wireRequest =
            JsonSerializer.Deserialize<CredentialRequestV2Wire>(json, SerializerOptions)
            ?? throw new JsonException("Credential request v2 JSON did not contain a request.");
        CredentialRequestV2 request = FromWire(wireRequest);
        CredentialRequestV2Policy.EnsureValid(request);
        return request;
    }

    private static CredentialRequestV2Wire ToWire(CredentialRequestV2 request) =>
        new()
        {
            ContractMajor = request.ContractMajor,
            Ecosystem = request.Ecosystem,
            Operation = request.Operation,
            Resource = request.Resource!,
            ServiceIdentity = request.ServiceIdentity!,
            AccountHint = request.AccountHint,
            TenantHint = request.TenantHint,
            RequestedAudience = request.RequestedAudience,
            CredentialKind = request.CredentialKind,
            IdentityFlow = request.IdentityFlow,
            InteractivePolicy = request.InteractivePolicy,
            AcquisitionMode = request.AcquisitionMode,
            CachePolicy = request.CachePolicy,
            CiContext = request.CiContext,
            ExtensionData = request.ExtensionData ?? ContractMetadata.Empty,
        };

    private static CredentialRequestV2 FromWire(CredentialRequestV2Wire request) =>
        new()
        {
            ContractMajor = request.ContractMajor,
            Ecosystem = request.Ecosystem,
            Operation = request.Operation,
            Resource = request.Resource!,
            ServiceIdentity = request.ServiceIdentity!,
            AccountHint = request.AccountHint,
            TenantHint = request.TenantHint,
            RequestedAudience = request.RequestedAudience,
            CredentialKind = request.CredentialKind,
            IdentityFlow = request.IdentityFlow,
            InteractivePolicy = request.InteractivePolicy,
            AcquisitionMode = request.AcquisitionMode,
            CachePolicy = request.CachePolicy,
            CiContext = request.CiContext,
            ExtensionData = request.ExtensionData ?? ContractMetadata.Empty,
        };
}

[JsonSerializable(typeof(CredentialRequestV2Wire))]
[JsonSerializable(typeof(AcquisitionMode))]
[JsonSerializable(typeof(CachePolicyMode))]
[JsonSerializable(typeof(CanonicalResourceIdentity))]
[JsonSerializable(typeof(CiContext))]
[JsonSerializable(typeof(CredentialEcosystem))]
[JsonSerializable(typeof(CredentialKind))]
[JsonSerializable(typeof(CredentialOperation))]
[JsonSerializable(typeof(IdentityFlow))]
[JsonSerializable(typeof(InteractivePolicy))]
[JsonSerializable(typeof(TokenAudience))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
internal sealed partial class CredentialRequestV2JsonContext : JsonSerializerContext
{
    internal static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = ContractJson.CreateSerializerOptions();
        options.TypeInfoResolver = Default;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.PropertyNameCaseInsensitive = false;
        options.AllowDuplicateProperties = false;
        return options;
    }
}

internal sealed class CredentialRequestV2DirectJsonConverter : JsonConverter<CredentialRequestV2>
{
    private const string DirectUseMessage =
        "Direct System.Text.Json use of CredentialRequestV2 is intentionally unsupported. "
        + "Use CredentialRequestV2Json.Serialize(...) and "
        + "CredentialRequestV2Json.Deserialize(...).";

    public override bool HandleNull => true;

    public override CredentialRequestV2 Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => throw new NotSupportedException(DirectUseMessage);

    public override void Write(
        Utf8JsonWriter writer,
        CredentialRequestV2 value,
        JsonSerializerOptions options
    ) => throw new NotSupportedException(DirectUseMessage);
}

internal sealed record CredentialRequestV2Wire : IJsonOnDeserialized
{
    [JsonRequired]
    public int ContractMajor { get; init; } = ContractVersions.CredentialContractV2Major;
    public required CredentialEcosystem Ecosystem { get; init; }
    public required CredentialOperation Operation { get; init; }
    public required CanonicalResourceIdentity Resource { get; init; }

    [JsonRequired]
    public required string ServiceIdentity { get; init; }
    public string? AccountHint { get; init; }
    public string? TenantHint { get; init; }
    public required TokenAudience RequestedAudience { get; init; }
    public required CredentialKind CredentialKind { get; init; }
    public required IdentityFlow IdentityFlow { get; init; }
    public required InteractivePolicy InteractivePolicy { get; init; }

    [JsonRequired]
    public required AcquisitionMode AcquisitionMode { get; init; }

    public required CachePolicyMode CachePolicy { get; init; }
    public CiContext? CiContext { get; init; }
    public IReadOnlyDictionary<string, string> ExtensionData { get; init; } =
        ContractMetadata.Empty;

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (ContractMajor != ContractVersions.CredentialContractV2Major)
        {
            throw new ArgumentException(
                "Protocol violation: credential request v2 contract major must be 2.",
                nameof(ContractMajor)
            );
        }
    }
}
