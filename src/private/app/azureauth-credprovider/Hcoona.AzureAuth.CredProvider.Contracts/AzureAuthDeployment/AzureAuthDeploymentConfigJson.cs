using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;

/// <summary>Strict JSON facade for <see cref="AzureAuthDeploymentConfig" />.</summary>
public static class AzureAuthDeploymentConfigJson
{
    private static readonly JsonSerializerOptions SerializerOptions =
        AzureAuthDeploymentConfigJsonContext.CreateSerializerOptions();

    public static string Serialize(AzureAuthDeploymentConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        AzureAuthDeploymentConfigPolicy.EnsureValid(config);
        return JsonSerializer.Serialize(ToWire(config), SerializerOptions);
    }

    public static AzureAuthDeploymentConfig Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        AzureAuthDeploymentConfigWire wire =
            JsonSerializer.Deserialize<AzureAuthDeploymentConfigWire>(json, SerializerOptions)
            ?? throw new JsonException("Deployment configuration JSON was empty.");
        AzureAuthDeploymentConfig config = FromWire(wire);
        AzureAuthDeploymentConfigPolicy.EnsureValid(config);
        return config;
    }

    private static AzureAuthDeploymentConfigWire ToWire(AzureAuthDeploymentConfig config) =>
        new()
        {
            SchemaVersion = config.SchemaVersion,
            ExecutablePath = config.ExecutablePath,
            ExecutableSha256 = config.ExecutableSha256,
            SignerIdentity = config.SignerIdentity,
            PublisherName = config.PublisherName,
            ExecutableVersion = config.ExecutableVersion,
            ProvenanceIdentifier = config.ProvenanceIdentifier,
        };

    private static AzureAuthDeploymentConfig FromWire(AzureAuthDeploymentConfigWire wire) =>
        new()
        {
            SchemaVersion = wire.SchemaVersion,
            ExecutablePath = wire.ExecutablePath,
            ExecutableSha256 = wire.ExecutableSha256,
            SignerIdentity = wire.SignerIdentity,
            PublisherName = wire.PublisherName,
            ExecutableVersion = wire.ExecutableVersion,
            ProvenanceIdentifier = wire.ProvenanceIdentifier,
        };
}

[JsonSerializable(typeof(AzureAuthDeploymentConfigWire))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
internal sealed partial class AzureAuthDeploymentConfigJsonContext : JsonSerializerContext
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

internal sealed class AzureAuthDeploymentConfigDirectJsonConverter
    : JsonConverter<AzureAuthDeploymentConfig>
{
    private const string DirectUseMessage =
        "Direct System.Text.Json use of AzureAuthDeploymentConfig is intentionally unsupported. "
        + "Use AzureAuthDeploymentConfigJson.Serialize(...) and "
        + "AzureAuthDeploymentConfigJson.Deserialize(...).";

    public override bool HandleNull => true;

    public override AzureAuthDeploymentConfig Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => throw new NotSupportedException(DirectUseMessage);

    public override void Write(
        Utf8JsonWriter writer,
        AzureAuthDeploymentConfig value,
        JsonSerializerOptions options
    ) => throw new NotSupportedException(DirectUseMessage);
}

internal sealed record AzureAuthDeploymentConfigWire
{
    [JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonRequired]
    public required string ExecutablePath { get; init; }

    [JsonRequired]
    public required string ExecutableSha256 { get; init; }

    [JsonRequired]
    public required string SignerIdentity { get; init; }

    [JsonRequired]
    public required string PublisherName { get; init; }

    [JsonRequired]
    public required string ExecutableVersion { get; init; }

    [JsonRequired]
    public required string ProvenanceIdentifier { get; init; }
}
