using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public enum AzureAuthProviderSelection
{
    Unspecified = 0,
    DirectMsal = 1,
    AzureAuth = 2,
}

public sealed record AzureAuthProviderConfig
{
    public const string SupportedAzureAuthVersion = "0.9.5";

    public required int SchemaVersion { get; init; }

    public required AzureAuthProviderSelection Selection { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AzureAuthVersion { get; init; }

    public static AzureAuthProviderConfig CreateDefault() => CreateDirectMsal();

    internal static AzureAuthProviderConfig CreateUnconfigured() =>
        new()
        {
            SchemaVersion = ContractVersions.AzureAuthProviderConfigSchemaMajor,
            Selection = AzureAuthProviderSelection.Unspecified,
        };

    public static AzureAuthProviderConfig CreateDirectMsal() =>
        new()
        {
            SchemaVersion = ContractVersions.AzureAuthProviderConfigSchemaMajor,
            Selection = AzureAuthProviderSelection.DirectMsal,
        };

    public static AzureAuthProviderConfig CreateAzureAuth(
        string version = SupportedAzureAuthVersion
    ) =>
        new()
        {
            SchemaVersion = ContractVersions.AzureAuthProviderConfigSchemaMajor,
            Selection = AzureAuthProviderSelection.AzureAuth,
            AzureAuthVersion = version,
        };
}

public static class AzureAuthProviderConfigPolicy
{
    public static void EnsureValid(AzureAuthProviderConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.SchemaVersion != ContractVersions.AzureAuthProviderConfigSchemaMajor)
        {
            throw new ArgumentException(
                "Provider configuration schema version is unsupported.",
                nameof(config)
            );
        }

        switch (config.Selection)
        {
            case AzureAuthProviderSelection.DirectMsal when config.AzureAuthVersion is null:
                return;
            case AzureAuthProviderSelection.AzureAuth
                when string.Equals(
                    config.AzureAuthVersion,
                    AzureAuthProviderConfig.SupportedAzureAuthVersion,
                    StringComparison.Ordinal
                ):
                return;
            case AzureAuthProviderSelection.DirectMsal:
                throw new ArgumentException(
                    "DirectMsal configuration must not include an AzureAuth version.",
                    nameof(config)
                );
            case AzureAuthProviderSelection.AzureAuth:
                throw new ArgumentException(
                    "AzureAuth version must be "
                        + AzureAuthProviderConfig.SupportedAzureAuthVersion
                        + ".",
                    nameof(config)
                );
            default:
                throw new ArgumentException("Provider selection is required.", nameof(config));
        }
    }
}

public static class AzureAuthProviderConfigJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static string Serialize(AzureAuthProviderConfig config)
    {
        AzureAuthProviderConfigPolicy.EnsureValid(config);
        return JsonSerializer.Serialize(config, SerializerOptions);
    }

    public static AzureAuthProviderConfig Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        AzureAuthProviderConfig config =
            JsonSerializer.Deserialize<AzureAuthProviderConfig>(json, SerializerOptions)
            ?? throw new JsonException("Provider configuration JSON was empty.");
        AzureAuthProviderConfigPolicy.EnsureValid(config);
        return config;
    }

    private static JsonSerializerOptions CreateSerializerOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = AzureAuthProviderConfigJsonContext.Default,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowDuplicateProperties = false,
            Converters =
            {
                new JsonStringEnumConverter<AzureAuthProviderSelection>(
                    JsonNamingPolicy.CamelCase,
                    allowIntegerValues: false
                ),
            },
        };
}

[JsonSerializable(typeof(AzureAuthProviderConfig))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
internal sealed partial class AzureAuthProviderConfigJsonContext : JsonSerializerContext;
