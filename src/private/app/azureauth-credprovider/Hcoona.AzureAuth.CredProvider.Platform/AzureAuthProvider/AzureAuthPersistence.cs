using System.Text;
using System.Text.Json;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed class AzureAuthProviderConfigPersistence
{
    private readonly IAzureAuthSecureRecordStore recordStore;

    public AzureAuthProviderConfigPersistence(IAzureAuthSecureRecordStore? recordStore = null)
    {
        this.recordStore = recordStore ?? new SystemAzureAuthSecureRecordStore();
    }

    public AzureAuthPersistedRecord<AzureAuthProviderConfig> Read(string recordName) =>
        AzureAuthPersistenceCore.Read(
            recordStore,
            recordName,
            AzureAuthProviderConfigJson.Deserialize
        );

    public AzureAuthPersistedWriteResult<AzureAuthProviderConfig> Create(
        string recordName,
        AzureAuthProviderConfig config
    ) => Save(AzureAuthPersistedRecord<AzureAuthProviderConfig>.Missing(recordName), config);

    public AzureAuthPersistedWriteResult<AzureAuthProviderConfig> Replace(
        AzureAuthPersistedRecord<AzureAuthProviderConfig> expected,
        AzureAuthProviderConfig config
    ) => Save(expected, config);

    public AzureAuthPersistedWriteResult<AzureAuthProviderConfig> Repair(
        AzureAuthPersistedRecord<AzureAuthProviderConfig> expected,
        AzureAuthProviderConfig config
    ) => Save(expected, config);

    private AzureAuthPersistedWriteResult<AzureAuthProviderConfig> Save(
        AzureAuthPersistedRecord<AzureAuthProviderConfig> expected,
        AzureAuthProviderConfig config
    )
    {
        AzureAuthProviderConfigPolicy.EnsureValid(config);
        return AzureAuthPersistenceCore.Write(
            recordStore,
            expected,
            AzureAuthProviderConfigJson.Serialize(config),
            config
        );
    }
}

public sealed class AzureAuthBindingPersistence
{
    private readonly IAzureAuthSecureRecordStore recordStore;

    public AzureAuthBindingPersistence(IAzureAuthSecureRecordStore? recordStore = null)
    {
        this.recordStore = recordStore ?? new SystemAzureAuthSecureRecordStore();
    }

    public AzureAuthPersistedRecord<AzureAuthBinding> Read(string recordName) =>
        AzureAuthPersistenceCore.Read(recordStore, recordName, AzureAuthBindingJson.Deserialize);

    public AzureAuthPersistedWriteResult<AzureAuthBinding> Bind(
        AzureAuthPersistedRecord<AzureAuthBinding> expected,
        AzureAuthProviderConfig config,
        string? accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc
    )
    {
        AzureAuthBinding binding = expected.Status switch
        {
            AzureAuthPersistedRecordStatus.Present => AzureAuthBindingPolicy.Bind(
                AzureAuthPersistenceCore.RequireValue(expected),
                config,
                accountId,
                tenantId,
                recordedAtUtc
            ),
            AzureAuthPersistedRecordStatus.Missing => AzureAuthBindingPolicy.CreateBound(
                config,
                accountId,
                tenantId,
                recordedAtUtc
            ),
            AzureAuthPersistedRecordStatus.Malformed => throw new InvalidOperationException(
                "Malformed binding records must be repaired with Rebind."
            ),
            _ => throw new ArgumentException(
                "Unsupported binding record status.",
                nameof(expected)
            ),
        };

        return AzureAuthPersistenceCore.Write(
            recordStore,
            expected,
            AzureAuthBindingJson.Serialize(binding),
            binding
        );
    }

    public AzureAuthPersistedWriteResult<AzureAuthBinding> Rebind(
        AzureAuthPersistedRecord<AzureAuthBinding> expected,
        AzureAuthProviderConfig config,
        string? accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc
    )
    {
        AzureAuthBinding binding = AzureAuthBindingPolicy.Rebind(
            config,
            accountId,
            tenantId,
            recordedAtUtc
        );
        return AzureAuthPersistenceCore.Write(
            recordStore,
            expected,
            AzureAuthBindingJson.Serialize(binding),
            binding
        );
    }

    public AzureAuthSecureRecordWriteResult Unbind(
        AzureAuthPersistedRecord<AzureAuthBinding> expected
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        AzureAuthSecureRecordStoreContract.EnsureKnownRecordName(expected.RecordName);
        return recordStore.CompareDelete(
            expected.RecordName,
            AzureAuthPersistenceCore.GetExpectedRevision(expected)
        );
    }
}

internal static class AzureAuthPersistenceCore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static AzureAuthPersistedRecord<T> Read<T>(
        IAzureAuthSecureRecordStore recordStore,
        string recordName,
        Func<string, T> deserialize
    )
        where T : class
    {
        AzureAuthSecureRecordStoreContract.EnsureKnownRecordName(recordName);
        AzureAuthSecureRecordReadResult result = recordStore.Read(recordName);
        return result.Status switch
        {
            AzureAuthSecureRecordReadStatus.Missing => AzureAuthPersistedRecord<T>.Missing(
                recordName
            ),
            AzureAuthSecureRecordReadStatus.Present => ReadPresent(
                recordName,
                result.Revision!,
                result.Content,
                deserialize
            ),
            _ => throw new InvalidOperationException("Unknown AzureAuth record status."),
        };
    }

    internal static AzureAuthPersistedWriteResult<T> Write<T>(
        IAzureAuthSecureRecordStore recordStore,
        AzureAuthPersistedRecord<T> expected,
        string json,
        T value
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(expected);
        string expectedRevision = GetExpectedRevision(expected);
        AzureAuthSecureRecordWriteResult result = recordStore.CompareExchange(
            expected.RecordName,
            expectedRevision,
            StrictUtf8.GetBytes(json)
        );
        return result.Status == AzureAuthSecureRecordWriteStatus.Success
            ? AzureAuthPersistedWriteResult<T>.Success(
                AzureAuthPersistedRecord<T>.Present(expected.RecordName, result.Revision!, value)
            )
            : AzureAuthPersistedWriteResult<T>.Conflict();
    }

    internal static string GetExpectedRevision<T>(AzureAuthPersistedRecord<T> expected)
        where T : class =>
        expected.Status switch
        {
            AzureAuthPersistedRecordStatus.Missing =>
                AzureAuthSecureRecordStoreContract.MissingRevision,
            AzureAuthPersistedRecordStatus.Present or AzureAuthPersistedRecordStatus.Malformed =>
                expected.Revision
                    ?? throw new ArgumentException(
                        "Expected record revision is missing.",
                        nameof(expected)
                    ),
            _ => throw new ArgumentException(
                "Unsupported expected record status.",
                nameof(expected)
            ),
        };

    internal static T RequireValue<T>(AzureAuthPersistedRecord<T> record)
        where T : class =>
        record.Value
        ?? throw new ArgumentException("Present record value is missing.", nameof(record));

    private static AzureAuthPersistedRecord<T> ReadPresent<T>(
        string recordName,
        string revision,
        ReadOnlyMemory<byte> content,
        Func<string, T> deserialize
    )
        where T : class
    {
        try
        {
            return AzureAuthPersistedRecord<T>.Present(
                recordName,
                revision,
                deserialize(StrictUtf8.GetString(content.Span))
            );
        }
        catch (Exception exception)
            when (exception
                    is DecoderFallbackException
                        or JsonException
                        or FormatException
                        or ArgumentException
            )
        {
            return AzureAuthPersistedRecord<T>.Malformed(recordName, revision);
        }
    }
}
