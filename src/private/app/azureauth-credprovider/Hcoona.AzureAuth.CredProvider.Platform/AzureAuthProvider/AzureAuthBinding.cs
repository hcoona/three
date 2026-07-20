using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public enum AzureAuthBindingState
{
    /// <summary>Fail-closed sentinel. Persisted bindings must use only unbound or bound.</summary>
    Unspecified = 0,
    Unbound = 1,
    Bound = 2,
}

/// <summary>Strict persisted account binding state.</summary>
[JsonConverter(typeof(AzureAuthBindingDirectJsonConverter))]
public sealed record AzureAuthBinding
{
    public required int SchemaVersion { get; init; }

    public required AzureAuthBindingState State { get; init; }

    public required AzureAuthProviderSelection ProviderSelection { get; init; }

    public string? DeploymentKey { get; init; }

    public string? AccountId { get; init; }

    public string? TenantId { get; init; }

    public required DateTimeOffset RecordedAtUtc { get; init; }
}

public sealed class AzureAuthBindingMismatchException()
    : InvalidOperationException(
        "Binding already exists for a different provider, deployment, account, or tenant. "
            + "Use Rebind to replace it."
    );

public static class AzureAuthBindingPolicy
{
    public static void EnsureValid(AzureAuthBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.SchemaVersion != ContractVersions.AzureAuthAccountBindingSchemaMajor)
        {
            throw new ArgumentException("Binding schema version must be 1.", nameof(binding));
        }

        EnsureUtc(binding.RecordedAtUtc, nameof(binding.RecordedAtUtc));

        switch (binding.State)
        {
            case AzureAuthBindingState.Unbound:
                if (
                    binding.ProviderSelection != AzureAuthProviderSelection.Unspecified
                    || binding.DeploymentKey is not null
                    || binding.AccountId is not null
                    || binding.TenantId is not null
                )
                {
                    throw new ArgumentException(
                        "Unbound records must not carry provider, deployment, or account state.",
                        nameof(binding)
                    );
                }

                return;
            case AzureAuthBindingState.Bound:
                EnsureBoundState(binding);
                return;
            default:
                throw new ArgumentException("Binding state is required.", nameof(binding));
        }
    }

    public static AzureAuthBinding CreateUnbound(DateTimeOffset recordedAtUtc)
    {
        EnsureUtc(recordedAtUtc, nameof(recordedAtUtc));
        return new AzureAuthBinding
        {
            SchemaVersion = ContractVersions.AzureAuthAccountBindingSchemaMajor,
            State = AzureAuthBindingState.Unbound,
            ProviderSelection = AzureAuthProviderSelection.Unspecified,
            RecordedAtUtc = recordedAtUtc,
        };
    }

    public static AzureAuthBinding CreateBound(
        AzureAuthProviderConfig config,
        string accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc,
        AzureAuthTrustResult? trustResult = null
    )
    {
        AzureAuthProviderConfigPolicy.EnsureValid(config);
        EnsureUtc(recordedAtUtc, nameof(recordedAtUtc));

        string normalizedAccount = NormalizeObservedIdentifier(accountId, nameof(accountId));
        string normalizedTenant = NormalizeObservedIdentifier(tenantId, nameof(tenantId));
        string? deploymentKey = null;

        if (config.Selection == AzureAuthProviderSelection.AzureAuth)
        {
            ArgumentNullException.ThrowIfNull(trustResult);
            AzureAuthTrustResult currentTrust = AzureAuthTrustPolicy.Revalidate(
                config.DeploymentConfig!,
                trustResult
            );
            if (!currentTrust.IsReady || string.IsNullOrWhiteSpace(currentTrust.DeploymentKey))
            {
                throw new InvalidOperationException(
                    "AzureAuth bindings require a trusted deployment result."
                );
            }

            deploymentKey = currentTrust.DeploymentKey;
        }

        return new AzureAuthBinding
        {
            SchemaVersion = ContractVersions.AzureAuthAccountBindingSchemaMajor,
            State = AzureAuthBindingState.Bound,
            ProviderSelection = config.Selection,
            DeploymentKey = deploymentKey,
            AccountId = normalizedAccount,
            TenantId = normalizedTenant,
            RecordedAtUtc = recordedAtUtc,
        };
    }

    public static AzureAuthBinding Bind(
        AzureAuthBinding current,
        AzureAuthProviderConfig config,
        string accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc,
        AzureAuthTrustResult? trustResult = null
    )
    {
        ArgumentNullException.ThrowIfNull(current);
        EnsureValid(current);

        if (current.State == AzureAuthBindingState.Unbound)
        {
            return CreateBound(config, accountId, tenantId, recordedAtUtc, trustResult);
        }

        AzureAuthBinding target = CreateBound(config, accountId, tenantId, recordedAtUtc, trustResult);
        if (MatchesBoundIdentity(current, target))
        {
            return current;
        }

        throw new AzureAuthBindingMismatchException();
    }

    public static AzureAuthBinding Rebind(
        AzureAuthProviderConfig config,
        string accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc,
        AzureAuthTrustResult? trustResult = null
    ) => CreateBound(config, accountId, tenantId, recordedAtUtc, trustResult);

    public static AzureAuthBinding Unbind(AzureAuthBinding? current, DateTimeOffset recordedAtUtc)
    {
        EnsureUtc(recordedAtUtc, nameof(recordedAtUtc));

        if (current is null)
        {
            return CreateUnbound(recordedAtUtc);
        }

        EnsureValid(current);
        return current.State == AzureAuthBindingState.Unbound ? current : CreateUnbound(recordedAtUtc);
    }

    internal static bool MatchesBoundIdentity(AzureAuthBinding left, AzureAuthBinding right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.State == AzureAuthBindingState.Bound
            && right.State == AzureAuthBindingState.Bound
            && left.ProviderSelection == right.ProviderSelection
            && string.Equals(left.DeploymentKey, right.DeploymentKey, StringComparison.Ordinal)
            && string.Equals(left.AccountId, right.AccountId, StringComparison.Ordinal)
            && string.Equals(left.TenantId, right.TenantId, StringComparison.Ordinal);
    }

    internal static string NormalizeObservedIdentifier(string? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentException("Observed identifier is required.", paramName);
        }

        if (value.Any(static character => character is < ' ' or > '~'))
        {
            throw new ArgumentException(
                "Observed identifiers must use printable non-whitespace ASCII.",
                paramName
            );
        }

        string trimmed = value.Trim(' ');
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Observed identifier is required.", paramName);
        }

        if (trimmed.Contains(' '))
        {
            throw new ArgumentException(
                "Observed identifiers must use printable non-whitespace ASCII.",
                paramName
            );
        }

        return NormalizeAsciiLetters(trimmed);
    }

    internal static void EnsureUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero || value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException(
                "Binding timestamps must be canonical whole-second UTC.",
                paramName
            );
        }
    }

    private static void EnsureBoundState(AzureAuthBinding binding)
    {
        if (
            binding.ProviderSelection
                is not (
                    AzureAuthProviderSelection.DirectMsal or AzureAuthProviderSelection.AzureAuth
                )
        )
        {
            throw new ArgumentException("Bound records require a concrete provider.", nameof(binding));
        }

        EnsureObservedIdentifier(binding.AccountId, nameof(binding.AccountId));
        EnsureObservedIdentifier(binding.TenantId, nameof(binding.TenantId));

        if (binding.ProviderSelection == AzureAuthProviderSelection.AzureAuth)
        {
            AzureAuthDeploymentKey.EnsureValid(binding.DeploymentKey, nameof(binding.DeploymentKey));
            return;
        }

        if (binding.DeploymentKey is not null)
        {
            throw new ArgumentException(
                "DirectMsal bindings must not carry a deployment key.",
                nameof(binding)
            );
        }
    }

    private static void EnsureObservedIdentifier(string? value, string paramName)
    {
        string normalized = NormalizeObservedIdentifier(value, paramName);
        if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Observed identifiers must already be stored in canonical lowercase form.",
                paramName
            );
        }
    }

    private static string NormalizeAsciiLetters(string value)
    {
        char[]? buffer = null;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character is < 'A' or > 'Z')
            {
                continue;
            }

            buffer ??= value.ToCharArray();
            buffer[index] = (char)(character + ('a' - 'A'));
        }

        return buffer is null ? value : new string(buffer);
    }
}

/// <summary>Strict JSON facade for <see cref="AzureAuthBinding" />.</summary>
public static class AzureAuthBindingJson
{
    private static readonly JsonSerializerOptions SerializerOptions =
        AzureAuthBindingJsonContext.CreateSerializerOptions();

    public static string Serialize(AzureAuthBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        AzureAuthBindingPolicy.EnsureValid(binding);

        return JsonSerializer.Serialize(
            new AzureAuthBindingWire
            {
                SchemaVersion = binding.SchemaVersion,
                State = binding.State,
                ProviderSelection = binding.ProviderSelection,
                DeploymentKey = binding.DeploymentKey,
                AccountId = binding.AccountId,
                TenantId = binding.TenantId,
                RecordedAtUtc = AzureAuthBindingRecordedAtUtcWire.Format(binding.RecordedAtUtc),
            },
            SerializerOptions
        );
    }

    public static AzureAuthBinding Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        AzureAuthBindingWire wire =
            JsonSerializer.Deserialize<AzureAuthBindingWire>(json, SerializerOptions)
            ?? throw new JsonException("Binding JSON was empty.");
        AzureAuthBinding binding = new()
        {
            SchemaVersion = wire.SchemaVersion,
            State = wire.State,
            ProviderSelection = wire.ProviderSelection,
            DeploymentKey = wire.DeploymentKey,
            AccountId = wire.AccountId,
            TenantId = wire.TenantId,
            RecordedAtUtc = AzureAuthBindingRecordedAtUtcWire.Parse(wire.RecordedAtUtc),
        };
        AzureAuthBindingPolicy.EnsureValid(binding);
        return binding;
    }
}

internal static class AzureAuthBindingStateWire
{
    internal static AzureAuthBindingState Parse(string? value) =>
        value switch
        {
            "unspecified" => AzureAuthBindingState.Unspecified,
            "unbound" => AzureAuthBindingState.Unbound,
            "bound" => AzureAuthBindingState.Bound,
            _ => throw new JsonException("Unsupported binding state."),
        };

    internal static string Format(AzureAuthBindingState value) =>
        value switch
        {
            AzureAuthBindingState.Unspecified => "unspecified",
            AzureAuthBindingState.Unbound => "unbound",
            AzureAuthBindingState.Bound => "bound",
            _ => throw new JsonException("Unsupported binding state."),
        };
}

internal static class AzureAuthBindingRecordedAtUtcWire
{
    internal const string FormatString = "yyyy-MM-dd'T'HH:mm:ss'Z'";
    internal const string DisplayFormat = "yyyy-MM-ddTHH:mm:ssZ";

    internal static DateTimeOffset Parse(string? value)
    {
        if (
            value is null
            || !DateTimeOffset.TryParseExact(
                value,
                FormatString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsed
            )
            || parsed.Offset != TimeSpan.Zero
        )
        {
            throw new JsonException(
                $"Binding recordedAtUtc must use exact UTC format {DisplayFormat}."
            );
        }

        return parsed;
    }

    internal static string Format(DateTimeOffset value)
    {
        AzureAuthBindingPolicy.EnsureUtc(value, nameof(value));
        return value.ToString(FormatString, CultureInfo.InvariantCulture);
    }
}

[JsonSerializable(typeof(AzureAuthBindingWire))]
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    NumberHandling = JsonNumberHandling.Strict,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
internal sealed partial class AzureAuthBindingJsonContext : JsonSerializerContext
{
    internal static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = ContractJson.CreateSerializerOptions();
        options.TypeInfoResolver = Default;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.PropertyNameCaseInsensitive = false;
        options.AllowDuplicateProperties = false;
        options.Converters.Add(new AzureAuthBindingStateJsonConverter());
        options.Converters.Add(new AzureAuthProviderSelectionJsonConverter());
        return options;
    }
}

internal sealed class AzureAuthBindingDirectJsonConverter : JsonConverter<AzureAuthBinding>
{
    private const string DirectUseMessage =
        "Direct System.Text.Json use of AzureAuthBinding is intentionally unsupported. "
        + "Use AzureAuthBindingJson.Serialize(...) and AzureAuthBindingJson.Deserialize(...).";

    public override bool HandleNull => true;

    public override AzureAuthBinding Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => throw new NotSupportedException(DirectUseMessage);

    public override void Write(
        Utf8JsonWriter writer,
        AzureAuthBinding value,
        JsonSerializerOptions options
    ) => throw new NotSupportedException(DirectUseMessage);
}

internal sealed record AzureAuthBindingWire
{
    [JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonRequired]
    public required AzureAuthBindingState State { get; init; }

    [JsonRequired]
    public required AzureAuthProviderSelection ProviderSelection { get; init; }

    public string? DeploymentKey { get; init; }

    public string? AccountId { get; init; }

    public string? TenantId { get; init; }

    [JsonRequired]
    public required string RecordedAtUtc { get; init; }
}

internal sealed class AzureAuthBindingStateJsonConverter : JsonConverter<AzureAuthBindingState>
{
    public override AzureAuthBindingState Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.String
            ? AzureAuthBindingStateWire.Parse(reader.GetString())
            : throw new JsonException("Binding state must be a string.");

    public override void Write(
        Utf8JsonWriter writer,
        AzureAuthBindingState value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(AzureAuthBindingStateWire.Format(value));
}
