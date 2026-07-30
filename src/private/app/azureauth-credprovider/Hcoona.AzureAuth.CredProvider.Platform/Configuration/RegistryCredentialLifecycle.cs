using System.Globalization;
using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

public enum RegistryCredentialLifecycleState
{
    Missing = 0,
    Fresh = 1,
    RefreshRecommended = 2,
    Expired = 3,
    Invalid = 4,
}

public sealed record RegistryCredentialLifecycleMetadata
{
    public required DateTimeOffset IssuedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public DateTimeOffset? RefreshBefore { get; init; }
}

public sealed record RegistryCredentialExpiryPolicyOptions
{
    public TimeSpan RefreshLeadTime { get; init; } = TimeSpan.FromMinutes(15);
}

public sealed class RegistryCredentialExpiryPolicy
{
    private readonly RegistryCredentialExpiryPolicyOptions options;
    private readonly TimeProvider timeProvider;

    public RegistryCredentialExpiryPolicy(
        TimeProvider? timeProvider = null,
        RegistryCredentialExpiryPolicyOptions? options = null
    )
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.options = options ?? new RegistryCredentialExpiryPolicyOptions();
        if (this.options.RefreshLeadTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    public RegistryCredentialLifecycleState Evaluate(
        RegistryCredentialLifecycleMetadata? metadata,
        ConfigurationScope scope = ConfigurationScope.User
    )
    {
        if (metadata is null)
        {
            return RegistryCredentialLifecycleState.Missing;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (
            RegistryCredentialLifecycleMetadataCodec.GetViolation(metadata) is not null
            || metadata.IssuedAt > now.AddMinutes(2)
        )
        {
            return RegistryCredentialLifecycleState.Invalid;
        }

        if (metadata.ExpiresAt is not { } expiresAt)
        {
            return scope == ConfigurationScope.CiTemporary
                ? RegistryCredentialLifecycleState.Fresh
                : RegistryCredentialLifecycleState.Invalid;
        }

        if (now >= expiresAt)
        {
            return RegistryCredentialLifecycleState.Expired;
        }

        return now >= (metadata.RefreshBefore ?? expiresAt - options.RefreshLeadTime)
            ? RegistryCredentialLifecycleState.RefreshRecommended
            : RegistryCredentialLifecycleState.Fresh;
    }

    public RegistryCredentialLifecycleMetadata Create(
        ConfigurationScope scope,
        DateTimeOffset? expiresAt
    )
    {
        DateTimeOffset issuedAt = Truncate(timeProvider.GetUtcNow());
        DateTimeOffset? expiry = expiresAt is null ? null : Truncate(expiresAt.Value);
        if (scope != ConfigurationScope.CiTemporary && expiry is null)
        {
            throw new InvalidOperationException("User registry credentials require an expiry.");
        }

        RegistryCredentialLifecycleMetadata metadata = new()
        {
            IssuedAt = issuedAt,
            ExpiresAt = expiry,
            RefreshBefore =
                expiry is null ? null : Max(issuedAt, expiry.Value - options.RefreshLeadTime),
        };
        if (RegistryCredentialLifecycleMetadataCodec.GetViolation(metadata) is { } violation)
        {
            throw new InvalidOperationException(violation);
        }

        return metadata;
    }

    private static DateTimeOffset Truncate(DateTimeOffset value) =>
        DateTimeOffset.FromUnixTimeSeconds(value.ToUniversalTime().ToUnixTimeSeconds());

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;
}

public static class RegistryCredentialLifecycleMetadataCodec
{
    private const string Prefix = "hcoona.azureAuthCredProvider.registryCredential.";
    private const string SchemaKey = Prefix + "schema";
    private const string IssuedAtKey = Prefix + "issuedAtUtc";
    private const string ExpiresAtKey = Prefix + "expiresAtUtc";
    private const string RefreshBeforeKey = Prefix + "refreshBeforeUtc";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly HashSet<string> KnownKeys =
        [SchemaKey, IssuedAtKey, ExpiresAtKey, RefreshBeforeKey];

    public static IReadOnlyDictionary<string, string> Write(
        IReadOnlyDictionary<string, string> existing,
        RegistryCredentialLifecycleMetadata metadata
    )
    {
        ArgumentNullException.ThrowIfNull(existing);
        if (GetViolation(metadata) is { } violation)
        {
            throw new ArgumentException(violation, nameof(metadata));
        }

        Dictionary<string, string> result = existing
            .Where(pair => !IsLifecycleKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        result[SchemaKey] = "1";
        result[IssuedAtKey] = Format(metadata.IssuedAt);
        if (metadata.ExpiresAt is { } expiresAt)
        {
            result[ExpiresAtKey] = Format(expiresAt);
        }
        if (metadata.RefreshBefore is { } refreshBefore)
        {
            result[RefreshBeforeKey] = Format(refreshBefore);
        }

        return result;
    }

    public static bool TryRead(
        IReadOnlyDictionary<string, string>? values,
        out RegistryCredentialLifecycleMetadata? metadata
    )
    {
        metadata = null;
        if (
            values is null
            || !values.Keys.Any(IsLifecycleKey)
            || values.Keys.Any(key => IsLifecycleKey(key) && !KnownKeys.Contains(key))
            || !TryGet(values, SchemaKey, out string schema)
            || schema != "1"
            || !TryGet(values, IssuedAtKey, out string issuedAtText)
            || !TryParse(issuedAtText, out DateTimeOffset issuedAt)
            || !TryParseOptional(values, ExpiresAtKey, out DateTimeOffset? expiresAt)
            || !TryParseOptional(values, RefreshBeforeKey, out DateTimeOffset? refreshBefore)
        )
        {
            return false;
        }

        RegistryCredentialLifecycleMetadata candidate = new()
        {
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            RefreshBefore = refreshBefore,
        };
        if (GetViolation(candidate) is not null)
        {
            return false;
        }

        metadata = candidate;
        return true;
    }

    public static bool ContainsLifecycleMetadata(IReadOnlyDictionary<string, string>? values) =>
        values is not null && values.Keys.Any(IsLifecycleKey);

    public static string? GetViolation(RegistryCredentialLifecycleMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        bool invalidExpiry = metadata.ExpiresAt is { } expiresAt && expiresAt <= metadata.IssuedAt;
        bool invalidRefresh =
            metadata.RefreshBefore is { } refreshBefore
            && (
                metadata.ExpiresAt is not { } expiry
                || refreshBefore < metadata.IssuedAt
                || refreshBefore > expiry
            );
        return invalidExpiry || invalidRefresh
            ? "Registry credential lifecycle metadata is invalid."
            : null;
    }

    private static bool TryGet(
        IReadOnlyDictionary<string, string> values,
        string key,
        out string value
    ) => values.TryGetValue(key, out value!) && !string.IsNullOrWhiteSpace(value);

    private static bool TryParseOptional(
        IReadOnlyDictionary<string, string> values,
        string key,
        out DateTimeOffset? timestamp
    )
    {
        timestamp = null;
        if (!values.TryGetValue(key, out string? text))
        {
            return true;
        }

        if (!TryParse(text, out DateTimeOffset parsed))
        {
            return false;
        }

        timestamp = parsed;
        return true;
    }

    private static bool IsLifecycleKey(string key) =>
        key.StartsWith(Prefix, StringComparison.Ordinal);

    private static bool TryParse(string value, out DateTimeOffset timestamp) =>
        DateTimeOffset.TryParseExact(
            value,
            TimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out timestamp
        );

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);
}
