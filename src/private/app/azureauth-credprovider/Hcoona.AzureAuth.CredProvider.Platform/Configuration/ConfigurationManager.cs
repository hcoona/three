using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

public interface IConfigurationManager : IConfigurationChangePlanSink
{
    ConfigurationPlanValidationResult ValidatePlan(ConfigurationChangePlan plan);

    ValueTask<ConfigurationPlanResult> DryRunAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );

    ValueTask<ConfigurationPlanResult> ApplyAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );

    ValueTask<ConfigurationPlanResult> RemoveAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );
}

public interface IConfigurationChangePlanSink
{
    ValueTask<ConfigurationPlanValidationResult> AcceptPlanAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    );
}

public interface IConfigurationChangePlanFactory<in TRequest>
{
    ConfigurationChangePlan CreatePlan(TRequest request);
}

public sealed class ConfigurationManager : IConfigurationManager
{
    public ConfigurationPlanValidationResult ValidatePlan(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetValidationViolation(plan);
        return new ConfigurationPlanValidationResult
        {
            Plan = plan,
            IsValid = violation is null,
            Violation = violation,
        };
    }

    public ValueTask<ConfigurationPlanValidationResult> AcceptPlanAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ValidatePlan(plan));
    }

    public ValueTask<ConfigurationPlanResult> DryRunAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureValid(plan);
        return ValueTask.FromResult(CreatePlannedResult(plan, ConfigurationPlanOperation.DryRun));
    }

    public ValueTask<ConfigurationPlanResult> ApplyAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ValidateAndDefer(plan, ConfigurationPlanOperation.Apply, cancellationToken);

    public ValueTask<ConfigurationPlanResult> RemoveAsync(
        ConfigurationChangePlan plan,
        CancellationToken cancellationToken = default
    ) => ValidateAndDefer(plan, ConfigurationPlanOperation.Remove, cancellationToken);

    private static ValueTask<ConfigurationPlanResult> ValidateAndDefer(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureValid(plan);
        return ValueTask.FromException<ConfigurationPlanResult>(
            new NotImplementedException(
                $"{operation} execution is owned by later Phase 4 "
                    + "configuration-manager implementation."
            )
        );
    }

    private static void EnsureValid(ConfigurationChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        string? violation = GetValidationViolation(plan);
        if (violation is not null)
        {
            throw new ArgumentException(violation, nameof(plan));
        }
    }

    private static string? GetValidationViolation(ConfigurationChangePlan plan)
    {
        string? contractViolation = ConfigurationChangePlanPolicy.GetViolation(plan);
        if (contractViolation is not null)
        {
            return contractViolation;
        }

        ConfigurationPlannedOperation[] plannedOperations =
            ConfigurationPlanProjector.CreatePlannedOperations(plan);
        ConfigurationOwnershipManifest manifest =
            ConfigurationPlanProjector.CreateOwnershipManifest(plan, plannedOperations);
        return ConfigurationOwnershipManifestPolicy.GetViolation(manifest);
    }

    private static ConfigurationPlanResult CreatePlannedResult(
        ConfigurationChangePlan plan,
        ConfigurationPlanOperation operation
    )
    {
        ConfigurationPlannedOperation[] plannedOperations =
            ConfigurationPlanProjector.CreatePlannedOperations(plan);

        ConfigurationOwnershipManifest ownershipManifest =
            ConfigurationPlanProjector.CreateOwnershipManifest(plan, plannedOperations);
        ConfigurationOwnershipManifestPolicy.EnsureValid(ownershipManifest);

        return new ConfigurationPlanResult
        {
            Plan = ConfigurationPlanProjector.CreateDryRunPlan(plan, plannedOperations),
            Operation = operation,
            State = ConfigurationPlanState.Planned,
            Changes = plannedOperations
                .Select(plannedOperation => plannedOperation.Change)
                .ToArray(),
            PlannedOperations = plannedOperations,
            OwnershipManifest = ownershipManifest,
        };
    }
}

public sealed record ConfigurationPlanValidationResult
{
    public required ConfigurationChangePlan Plan { get; init; }
    public required bool IsValid { get; init; }
    public string? Violation { get; init; }
}

public sealed record ConfigurationPlanResult
{
    public required ConfigurationDryRunPlan Plan { get; init; }

    [JsonConverter(typeof(ConfigurationPlanOperationJsonConverter))]
    public required ConfigurationPlanOperation Operation { get; init; }
    public required ConfigurationPlanState State { get; init; }
    public IReadOnlyList<ConfigurationPlannedChange> Changes { get; init; } =
        Array.Empty<ConfigurationPlannedChange>();
    public IReadOnlyList<ConfigurationPlannedOperation> PlannedOperations { get; init; } =
        Array.Empty<ConfigurationPlannedOperation>();
    public ConfigurationOwnershipManifest? OwnershipManifest { get; init; }
}

public sealed record ConfigurationDryRunPlan
{
    public required int ContractMajor { get; init; }
    public required string PlanId { get; init; }
    public required string ChangeSetId { get; init; }
    public required string OwnerProductId { get; init; }
    public required ConfigurationScope Scope { get; init; }
    public required ConfigurationAtomicityPolicy AtomicityPolicy { get; init; }
    public required ConfigurationRollbackPolicy RollbackPolicy { get; init; }
    public required ConfigurationPlanState State { get; init; }
    public required ConfigurationManifestCommitPolicy ManifestCommitPolicy { get; init; }
    public required ConfigurationManifestMetadata Manifest { get; init; }
    public ConfigurationTemporaryContainer? TemporaryContainer { get; init; }
    public required ConfigurationDeclarationPreservation DeclarationPreservation { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required bool ContainsCredentialMaterial { get; init; }
    public IReadOnlyDictionary<string, string> ExtensionData { get; init; } =
        ContractMetadata.Empty;
    public IReadOnlyList<ConfigurationPlannedChange> Changes { get; init; } =
        Array.Empty<ConfigurationPlannedChange>();
}

public sealed record ConfigurationPlannedChange
{
    public required int Sequence { get; init; }
    public required ConfigurationChangeOperation Operation { get; init; }
    public required ConfigurationTargetKind TargetKind { get; init; }
    public required string TargetPathOrName { get; init; }
    public required string Key { get; init; }
    public required bool RequiresOwnershipRecord { get; init; }
    public required bool PreserveDeclarationsAndComments { get; init; }
    public required bool HasPlannedValue { get; init; }
    public required bool IsSecretValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlannedValueSha256 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousOwnedEntryMetadata { get; init; }
}

public enum ConfigurationPlanOperation
{
    DryRun,
    Apply,
    Remove,
}

public sealed class ConfigurationPlanOperationJsonConverter
    : JsonConverter<ConfigurationPlanOperation>
{
    private static readonly Dictionary<string, ConfigurationPlanOperation> ValuesByWireName =
        Enum.GetValues<ConfigurationPlanOperation>()
            .ToDictionary(
                value =>
                    JsonNamingPolicy.CamelCase.ConvertName(
                        Enum.GetName(value)
                            ?? throw new JsonException(
                                "Unnamed enum value in ConfigurationPlanOperation."
                            )
                    ),
                value => value,
                StringComparer.Ordinal
            );

    private static readonly Dictionary<ConfigurationPlanOperation, string> WireNamesByValue =
        ValuesByWireName.ToDictionary(pair => pair.Value, pair => pair.Key);

    public override ConfigurationPlanOperation Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected string enum value for ConfigurationPlanOperation.");
        }

        string? value = reader.GetString();
        if (
            value is null
            || !ValuesByWireName.TryGetValue(value, out ConfigurationPlanOperation enumValue)
        )
        {
            throw new JsonException(
                $"Unsupported enum value '{value}' for ConfigurationPlanOperation."
            );
        }

        return enumValue;
    }

    public override void Write(
        Utf8JsonWriter writer,
        ConfigurationPlanOperation value,
        JsonSerializerOptions options
    )
    {
        if (!WireNamesByValue.TryGetValue(value, out string? wireName))
        {
            long numericValue = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            throw new JsonException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unsupported enum value '{numericValue}' for ConfigurationPlanOperation."
                )
            );
        }

        writer.WriteStringValue(wireName);
    }
}

public sealed record ConfigurationPlannedOperation
{
    public required int Sequence { get; init; }
    public required ConfigurationPlannedChange Change { get; init; }
    public required ConfigurationOwnershipManifestEntry? OwnershipEntry { get; init; }
}

public sealed record ConfigurationOwnershipManifest
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string ManifestId { get; init; }
    public required string PlanId { get; init; }
    public required string ChangeSetId { get; init; }
    public required string OwnerProductId { get; init; }

    public required ConfigurationScope Scope { get; init; }
    public required string EntrySelector { get; init; }
    public string? ProductVersion { get; init; }
    public string? PreviousOwnedEntryHash { get; init; }
    public bool ContainsCredentialMaterial { get; init; }
    public IReadOnlyDictionary<string, string> SafeMetadata { get; init; } =
        ContractMetadata.Empty;

    [JsonRequired]
    public IReadOnlyList<ConfigurationOwnershipManifestEntry> Entries { get; init; } =
        Array.Empty<ConfigurationOwnershipManifestEntry>();
}

public sealed record ConfigurationOwnershipManifestEntry
{
    public required int Sequence { get; init; }

    public required ConfigurationChangeOperation Operation { get; init; }

    public required ConfigurationTargetKind TargetKind { get; init; }
    public required string TargetPathOrName { get; init; }
    public required string Key { get; init; }
    public required bool PreserveDeclarationsAndComments { get; init; }
    public bool HasPlannedValue { get; init; }
    public bool IsSecretValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlannedValueSha256 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousOwnedEntryMetadata { get; init; }
}

public static class ConfigurationOwnershipManifestSerializer
{
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions =
        ConfigurationOwnershipManifestJson.CreateSerializerOptions();

    public static string Serialize(ConfigurationOwnershipManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        return System.Text.Json.JsonSerializer.Serialize(manifest, SerializerOptions);
    }

    public static ConfigurationOwnershipManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ConfigurationOwnershipManifest manifest =
            System.Text.Json.JsonSerializer.Deserialize<ConfigurationOwnershipManifest>(
                json,
                SerializerOptions
            )
            ?? throw new System.Text.Json.JsonException(
                "Configuration ownership manifest JSON did not contain a manifest."
            );
        ConfigurationOwnershipManifestPolicy.EnsureValid(manifest);
        return manifest;
    }
}

public sealed class ConfigurationOwnershipManifestStore
{
    private readonly IFileSystem fileSystem;

    public ConfigurationOwnershipManifestStore(IFileSystem fileSystem)
    {
        this.fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public void Save(string path, ConfigurationOwnershipManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);
        fileSystem.AtomicWriteAllText(
            path,
            ConfigurationOwnershipManifestSerializer.Serialize(manifest)
        );
    }

    public ConfigurationOwnershipManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ConfigurationOwnershipManifestSerializer.Deserialize(fileSystem.ReadAllText(path));
    }
}
