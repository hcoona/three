using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

/// <summary>Persists which provider WP5 should eventually compose.</summary>
public enum AzureAuthProviderSelection
{
    Unspecified = 0,
    DirectMsal = 1,
    AzureAuth = 2,
}

/// <summary>Strict persisted provider selection for WP2.</summary>
[JsonConverter(typeof(AzureAuthProviderConfigDirectJsonConverter))]
public sealed record AzureAuthProviderConfig
{
    public required int SchemaVersion { get; init; }

    public required AzureAuthProviderSelection Selection { get; init; }

    public AzureAuthDeploymentConfig? DeploymentConfig { get; init; }

    public static AzureAuthProviderConfig CreateDefault() => CreateDirectMsal();

    public static AzureAuthProviderConfig CreateDirectMsal() =>
        new()
        {
            SchemaVersion = ContractVersions.AzureAuthProviderConfigSchemaMajor,
            Selection = AzureAuthProviderSelection.DirectMsal,
        };

    public static AzureAuthProviderConfig CreateAzureAuth(AzureAuthDeploymentConfig deploymentConfig)
    {
        ArgumentNullException.ThrowIfNull(deploymentConfig);
        AzureAuthDeploymentConfigPolicy.EnsureValid(deploymentConfig);

        return new AzureAuthProviderConfig
        {
            SchemaVersion = ContractVersions.AzureAuthProviderConfigSchemaMajor,
            Selection = AzureAuthProviderSelection.AzureAuth,
            DeploymentConfig = deploymentConfig,
        };
    }
}

public static class AzureAuthProviderConfigPolicy
{
    public static void EnsureValid(AzureAuthProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.SchemaVersion != ContractVersions.AzureAuthProviderConfigSchemaMajor)
        {
            throw new ArgumentException(
                "Provider configuration schema version must be 1.",
                nameof(config)
            );
        }

        switch (config.Selection)
        {
            case AzureAuthProviderSelection.DirectMsal when config.DeploymentConfig is null:
                return;
            case AzureAuthProviderSelection.AzureAuth when config.DeploymentConfig is not null:
                AzureAuthDeploymentConfigPolicy.EnsureValid(config.DeploymentConfig);
                return;
            case AzureAuthProviderSelection.Unspecified:
                throw new ArgumentException(
                    "Provider selection is required.",
                    nameof(config)
                );
            case AzureAuthProviderSelection.DirectMsal:
                throw new ArgumentException(
                    "DirectMsal must not include deployment configuration.",
                    nameof(config)
                );
            case AzureAuthProviderSelection.AzureAuth:
                throw new ArgumentException(
                    "AzureAuth requires deployment configuration.",
                    nameof(config)
                );
            default:
                throw new ArgumentException(
                    "Unknown provider selection.",
                    nameof(config)
                );
        }
    }
}

/// <summary>Strict JSON facade for <see cref="AzureAuthProviderConfig" />.</summary>
public static class AzureAuthProviderConfigJson
{
    private static readonly JsonSerializerOptions SerializerOptions =
        AzureAuthProviderConfigJsonContext.CreateSerializerOptions();

    public static string Serialize(AzureAuthProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        AzureAuthProviderConfigPolicy.EnsureValid(config);

        return JsonSerializer.Serialize(
            new AzureAuthProviderConfigWire
            {
                SchemaVersion = config.SchemaVersion,
                Selection = config.Selection,
                DeploymentConfig = config.DeploymentConfig is null
                    ? null
                    : ToWire(config.DeploymentConfig),
            },
            SerializerOptions
        );
    }

    public static AzureAuthProviderConfig Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        AzureAuthProviderConfigWire wire =
            JsonSerializer.Deserialize<AzureAuthProviderConfigWire>(json, SerializerOptions)
            ?? throw new JsonException("Provider configuration JSON was empty.");
        AzureAuthProviderConfig config = new()
        {
            SchemaVersion = wire.SchemaVersion,
            Selection = wire.Selection,
            DeploymentConfig = wire.DeploymentConfig is null ? null : FromWire(wire.DeploymentConfig),
        };
        AzureAuthProviderConfigPolicy.EnsureValid(config);
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

internal static class AzureAuthProviderSelectionWire
{
    internal static AzureAuthProviderSelection Parse(string? value) =>
        value switch
        {
            "unspecified" => AzureAuthProviderSelection.Unspecified,
            "directMsal" => AzureAuthProviderSelection.DirectMsal,
            "azureAuth" => AzureAuthProviderSelection.AzureAuth,
            _ => throw new JsonException("Unsupported provider selection."),
        };

    internal static string Format(AzureAuthProviderSelection value) =>
        value switch
        {
            AzureAuthProviderSelection.Unspecified => "unspecified",
            AzureAuthProviderSelection.DirectMsal => "directMsal",
            AzureAuthProviderSelection.AzureAuth => "azureAuth",
            _ => throw new JsonException("Unsupported provider selection."),
        };
}

[JsonSerializable(typeof(AzureAuthProviderConfigWire))]
[JsonSerializable(typeof(AzureAuthDeploymentConfigWire))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
internal sealed partial class AzureAuthProviderConfigJsonContext : JsonSerializerContext
{
    internal static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = ContractJson.CreateSerializerOptions();
        options.TypeInfoResolver = Default;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.PropertyNameCaseInsensitive = false;
        options.AllowDuplicateProperties = false;
        options.Converters.Add(new AzureAuthProviderSelectionJsonConverter());
        return options;
    }
}

internal sealed class AzureAuthProviderConfigDirectJsonConverter
    : JsonConverter<AzureAuthProviderConfig>
{
    private const string DirectUseMessage =
        "Direct System.Text.Json use of AzureAuthProviderConfig is intentionally unsupported. "
        + "Use AzureAuthProviderConfigJson.Serialize(...) and "
        + "AzureAuthProviderConfigJson.Deserialize(...).";

    public override bool HandleNull => true;

    public override AzureAuthProviderConfig Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => throw new NotSupportedException(DirectUseMessage);

    public override void Write(
        Utf8JsonWriter writer,
        AzureAuthProviderConfig value,
        JsonSerializerOptions options
    ) => throw new NotSupportedException(DirectUseMessage);
}

internal sealed record AzureAuthProviderConfigWire
{
    [JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonRequired]
    public required AzureAuthProviderSelection Selection { get; init; }

    public AzureAuthDeploymentConfigWire? DeploymentConfig { get; init; }
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

internal sealed class AzureAuthProviderSelectionJsonConverter
    : JsonConverter<AzureAuthProviderSelection>
{
    public override AzureAuthProviderSelection Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.String
            ? AzureAuthProviderSelectionWire.Parse(reader.GetString())
            : throw new JsonException("Provider selection must be a string.");

    public override void Write(
        Utf8JsonWriter writer,
        AzureAuthProviderSelection value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(AzureAuthProviderSelectionWire.Format(value));
}
