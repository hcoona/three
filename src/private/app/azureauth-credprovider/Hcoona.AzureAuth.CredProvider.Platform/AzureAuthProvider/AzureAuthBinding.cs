using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed record AzureAuthBinding
{
    [JsonPropertyName("provider")]
    public required AzureAuthProviderSelection ProviderSelection { get; init; }

    [JsonPropertyName("account")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccountId { get; init; }

    [JsonPropertyName("tenant")]
    public required string TenantId { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed class AzureAuthBindingMismatchException()
    : InvalidOperationException(
        "Binding already exists for a different provider, account, or tenant. "
            + "Use Rebind to replace it."
    );

public static class AzureAuthBindingPolicy
{
    public static void EnsureValid(AzureAuthBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.RecordedAtUtc == default || binding.RecordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Binding timestamp must be a non-default UTC value.",
                nameof(binding)
            );
        }

        if (
            binding.ProviderSelection
            is not (AzureAuthProviderSelection.DirectMsal or AzureAuthProviderSelection.AzureAuth)
        )
        {
            throw new ArgumentException("Binding provider is invalid.", nameof(binding));
        }

        EnsureStoredOptionalIdentifier(binding.AccountId, nameof(binding.AccountId));
        EnsureStoredRequiredIdentifier(binding.TenantId, nameof(binding.TenantId));
    }

    public static AzureAuthBinding CreateBound(
        AzureAuthProviderConfig config,
        string? accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc
    )
    {
        AzureAuthProviderConfigPolicy.EnsureValid(config);
        return new AzureAuthBinding
        {
            ProviderSelection = config.Selection,
            AccountId = NormalizeOptionalIdentifier(accountId),
            TenantId = NormalizeRequiredIdentifier(tenantId, nameof(tenantId)),
            RecordedAtUtc = recordedAtUtc.ToUniversalTime(),
        };
    }

    public static AzureAuthBinding Bind(
        AzureAuthBinding current,
        AzureAuthProviderConfig config,
        string? accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc
    )
    {
        EnsureValid(current);
        AzureAuthBinding target = CreateBound(config, accountId, tenantId, recordedAtUtc);
        return MatchesBoundIdentity(current, target)
            ? current
            : throw new AzureAuthBindingMismatchException();
    }

    public static AzureAuthBinding Rebind(
        AzureAuthProviderConfig config,
        string? accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc
    ) => CreateBound(config, accountId, tenantId, recordedAtUtc);

    internal static bool MatchesBoundIdentity(AzureAuthBinding left, AzureAuthBinding right) =>
        left.ProviderSelection == right.ProviderSelection
        && string.Equals(left.AccountId, right.AccountId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.TenantId, right.TenantId, StringComparison.OrdinalIgnoreCase);

    internal static string? NormalizeOptionalIdentifier(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    internal static string NormalizeRequiredIdentifier(string? value, string paramName)
    {
        string? trimmed = NormalizeOptionalIdentifier(value);
        return trimmed ?? throw new ArgumentException("Identifier is required.", paramName);
    }

    private static void EnsureStoredOptionalIdentifier(string? value, string paramName)
    {
        if (value is not null && !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Stored identifier must be trimmed.", paramName);
        }
    }

    private static void EnsureStoredRequiredIdentifier(string? value, string paramName)
    {
        if (
            string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
        )
        {
            throw new ArgumentException(
                "Stored identifier is required and must be trimmed.",
                paramName
            );
        }
    }
}

public static class AzureAuthBindingJson
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static string Serialize(AzureAuthBinding binding)
    {
        AzureAuthBindingPolicy.EnsureValid(binding);
        return JsonSerializer.Serialize(binding, SerializerOptions);
    }

    public static AzureAuthBinding Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        AzureAuthBinding binding =
            JsonSerializer.Deserialize<AzureAuthBinding>(json, SerializerOptions)
            ?? throw new JsonException("Binding JSON was empty.");
        binding = binding with { RecordedAtUtc = binding.RecordedAtUtc.ToUniversalTime() };
        AzureAuthBindingPolicy.EnsureValid(binding);
        return binding;
    }

    private static JsonSerializerOptions CreateSerializerOptions() =>
        new(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = AzureAuthBindingJsonContext.Default,
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

[JsonSerializable(typeof(AzureAuthBinding))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
internal sealed partial class AzureAuthBindingJsonContext : JsonSerializerContext;
