using System.Text;
using System.Text.Json;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed class AzureAuthProviderConfigPersistence
{
    private readonly IAzureAuthSecureRecordStore _recordStore;

    public AzureAuthProviderConfigPersistence(IAzureAuthSecureRecordStore? recordStore = null)
    {
        _recordStore = recordStore ?? new UnsupportedAzureAuthSecureRecordStore();
    }

    public AzureAuthPersistedRecord<AzureAuthProviderConfig> Read(string recordName) =>
        AzureAuthPersistenceCore.Read(_recordStore, recordName, AzureAuthProviderConfigJson.Deserialize);

    public AzureAuthPersistedWriteResult<AzureAuthProviderConfig> Create(
        string recordName,
        AzureAuthProviderConfig config
    )
    {
        AzureAuthProviderConfigPolicy.EnsureValid(config);
        return AzureAuthPersistenceCore.Write(
            _recordStore,
            AzureAuthPersistedRecord<AzureAuthProviderConfig>.Missing(recordName),
            AzureAuthProviderConfigJson.Serialize(config),
            config
        );
    }

    public AzureAuthPersistedWriteResult<AzureAuthProviderConfig> Replace(
        AzureAuthPersistedRecord<AzureAuthProviderConfig> expected,
        AzureAuthProviderConfig config
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        AzureAuthProviderConfigPolicy.EnsureValid(config);

        return expected.Status switch
        {
            AzureAuthPersistedRecordStatus.Present => AzureAuthPersistenceCore.Write(
                _recordStore,
                expected,
                AzureAuthProviderConfigJson.Serialize(config),
                config
            ),
            AzureAuthPersistedRecordStatus.Unsupported =>
                AzureAuthPersistedWriteResult<AzureAuthProviderConfig>.Unsupported(),
            AzureAuthPersistedRecordStatus.Unsafe =>
                AzureAuthPersistedWriteResult<AzureAuthProviderConfig>.Unsafe(),
            AzureAuthPersistedRecordStatus.Missing => throw new InvalidOperationException(
                "Missing provider configuration records must be created."
            ),
            AzureAuthPersistedRecordStatus.Malformed => throw new InvalidOperationException(
                "Malformed provider configuration records must be repaired."
            ),
            _ => throw new ArgumentException("Unsupported provider configuration record status.", nameof(expected)),
        };
    }

    public AzureAuthPersistedWriteResult<AzureAuthProviderConfig> Repair(
        AzureAuthPersistedRecord<AzureAuthProviderConfig> expected,
        AzureAuthProviderConfig config
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        AzureAuthProviderConfigPolicy.EnsureValid(config);

        return expected.Status switch
        {
            AzureAuthPersistedRecordStatus.Malformed => AzureAuthPersistenceCore.Write(
                _recordStore,
                expected,
                AzureAuthProviderConfigJson.Serialize(config),
                config
            ),
            AzureAuthPersistedRecordStatus.Unsupported =>
                AzureAuthPersistedWriteResult<AzureAuthProviderConfig>.Unsupported(),
            AzureAuthPersistedRecordStatus.Unsafe =>
                AzureAuthPersistedWriteResult<AzureAuthProviderConfig>.Unsafe(),
            AzureAuthPersistedRecordStatus.Missing => throw new InvalidOperationException(
                "Missing provider configuration records must be created."
            ),
            AzureAuthPersistedRecordStatus.Present => throw new InvalidOperationException(
                "Valid provider configuration records must be replaced."
            ),
            _ => throw new ArgumentException("Unsupported provider configuration record status.", nameof(expected)),
        };
    }
}

public sealed class AzureAuthBindingPersistence
{
    private readonly IAzureAuthSecureRecordStore _recordStore;

    public AzureAuthBindingPersistence(IAzureAuthSecureRecordStore? recordStore = null)
    {
        _recordStore = recordStore ?? new UnsupportedAzureAuthSecureRecordStore();
    }

    public AzureAuthPersistedRecord<AzureAuthBinding> Read(string recordName) =>
        AzureAuthPersistenceCore.Read(_recordStore, recordName, AzureAuthBindingJson.Deserialize);

    public AzureAuthPersistedWriteResult<AzureAuthBinding> Bind(
        AzureAuthPersistedRecord<AzureAuthBinding> expected,
        AzureAuthProviderConfig config,
        string accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc,
        AzureAuthTrustResult? trustResult = null
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        AzureAuthBinding binding;
        switch (expected.Status)
        {
            case AzureAuthPersistedRecordStatus.Present:
                binding = AzureAuthBindingPolicy.Bind(
                    AzureAuthPersistenceCore.RequireValue(expected),
                    config,
                    accountId,
                    tenantId,
                    recordedAtUtc,
                    trustResult
                );
                break;
            case AzureAuthPersistedRecordStatus.Missing:
                binding = AzureAuthBindingPolicy.CreateBound(
                    config,
                    accountId,
                    tenantId,
                    recordedAtUtc,
                    trustResult
                );
                break;
            case AzureAuthPersistedRecordStatus.Unsupported:
                return AzureAuthPersistedWriteResult<AzureAuthBinding>.Unsupported();
            case AzureAuthPersistedRecordStatus.Unsafe:
                return AzureAuthPersistedWriteResult<AzureAuthBinding>.Unsafe();
            case AzureAuthPersistedRecordStatus.Malformed:
                throw new InvalidOperationException(
                    "Malformed binding records must be repaired with Rebind or Unbind."
                );
            default:
                throw new ArgumentException("Unsupported binding record status.", nameof(expected));
        }

        if (
            expected.Status == AzureAuthPersistedRecordStatus.Present
            && EqualityComparer<AzureAuthBinding>.Default.Equals(expected.Value, binding)
        )
        {
            return AzureAuthPersistenceCore.ValidateNoOp(_recordStore, expected);
        }

        return AzureAuthPersistenceCore.Write(
            _recordStore,
            expected,
            AzureAuthBindingJson.Serialize(binding),
            binding
        );
    }

    public AzureAuthPersistedWriteResult<AzureAuthBinding> Rebind(
        AzureAuthPersistedRecord<AzureAuthBinding> expected,
        AzureAuthProviderConfig config,
        string accountId,
        string tenantId,
        DateTimeOffset recordedAtUtc,
        AzureAuthTrustResult? trustResult = null
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        AzureAuthBinding binding = AzureAuthBindingPolicy.Rebind(
            config,
            accountId,
            tenantId,
            recordedAtUtc,
            trustResult
        );

        return expected.Status switch
        {
            AzureAuthPersistedRecordStatus.Missing
            or AzureAuthPersistedRecordStatus.Present
            or AzureAuthPersistedRecordStatus.Malformed => AzureAuthPersistenceCore.Write(
                _recordStore,
                expected,
                AzureAuthBindingJson.Serialize(binding),
                binding
            ),
            AzureAuthPersistedRecordStatus.Unsupported =>
                AzureAuthPersistedWriteResult<AzureAuthBinding>.Unsupported(),
            AzureAuthPersistedRecordStatus.Unsafe =>
                AzureAuthPersistedWriteResult<AzureAuthBinding>.Unsafe(),
            _ => throw new ArgumentException("Unsupported binding record status.", nameof(expected)),
        };
    }

    public AzureAuthPersistedWriteResult<AzureAuthBinding> Unbind(
        AzureAuthPersistedRecord<AzureAuthBinding> expected,
        DateTimeOffset recordedAtUtc
    )
    {
        ArgumentNullException.ThrowIfNull(expected);
        AzureAuthBinding binding;
        switch (expected.Status)
        {
            case AzureAuthPersistedRecordStatus.Present:
                binding = AzureAuthBindingPolicy.Unbind(
                    AzureAuthPersistenceCore.RequireValue(expected),
                    recordedAtUtc
                );
                break;
            case AzureAuthPersistedRecordStatus.Missing:
            case AzureAuthPersistedRecordStatus.Malformed:
                binding = AzureAuthBindingPolicy.CreateUnbound(recordedAtUtc);
                break;
            case AzureAuthPersistedRecordStatus.Unsupported:
                return AzureAuthPersistedWriteResult<AzureAuthBinding>.Unsupported();
            case AzureAuthPersistedRecordStatus.Unsafe:
                return AzureAuthPersistedWriteResult<AzureAuthBinding>.Unsafe();
            default:
                throw new ArgumentException("Unsupported binding record status.", nameof(expected));
        }

        if (
            expected.Status == AzureAuthPersistedRecordStatus.Present
            && EqualityComparer<AzureAuthBinding>.Default.Equals(expected.Value, binding)
        )
        {
            return AzureAuthPersistenceCore.ValidateNoOp(_recordStore, expected);
        }

        return AzureAuthPersistenceCore.Write(
            _recordStore,
            expected,
            AzureAuthBindingJson.Serialize(binding),
            binding
        );
    }
}

internal static class AzureAuthPersistenceCore
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    internal static AzureAuthPersistedRecord<T> Read<T>(
        IAzureAuthSecureRecordStore recordStore,
        string recordName,
        Func<string, T> deserialize
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentNullException.ThrowIfNull(deserialize);
        AzureAuthRecordNamePolicy.EnsureValid(recordName);

        AzureAuthSecureRecordReadResult readResult = recordStore.Read(recordName);
        AzureAuthSecureRecordStoreContract.EnsureValid(readResult);

        return readResult.Status switch
        {
            AzureAuthSecureRecordReadStatus.Missing => AzureAuthPersistedRecord<T>.Missing(recordName),
            AzureAuthSecureRecordReadStatus.Unsupported => AzureAuthPersistedRecord<T>.Unsupported(
                recordName
            ),
            AzureAuthSecureRecordReadStatus.Unsafe => AzureAuthPersistedRecord<T>.Unsafe(recordName),
            AzureAuthSecureRecordReadStatus.Present => ReadPresent(
                recordName,
                readResult.Revision!,
                readResult.Content,
                deserialize
            ),
            _ => throw new InvalidOperationException("Unsupported secure-store read status."),
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
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(value);
        AzureAuthRecordNamePolicy.EnsureValid(expected.RecordName);

        string expectedRevision = GetExpectedRevision(expected);
        AzureAuthSecureRecordWriteResult writeResult = recordStore.CompareExchange(
            expected.RecordName,
            expectedRevision,
            StrictUtf8.GetBytes(json)
        );
        AzureAuthSecureRecordStoreContract.EnsureValid(writeResult);
        if (
            writeResult.Status == AzureAuthSecureRecordWriteStatus.Success
            && string.Equals(writeResult.Revision, expectedRevision, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                "Successful secure-store mutations must return a new ABA-safe revision token."
            );
        }

        return writeResult.Status switch
        {
            AzureAuthSecureRecordWriteStatus.Success => AzureAuthPersistedWriteResult<T>.Success(
                AzureAuthPersistedRecord<T>.Present(expected.RecordName, writeResult.Revision!, value)
            ),
            AzureAuthSecureRecordWriteStatus.Conflict => AzureAuthPersistedWriteResult<T>.Conflict(),
            AzureAuthSecureRecordWriteStatus.Unsupported =>
                AzureAuthPersistedWriteResult<T>.Unsupported(),
            AzureAuthSecureRecordWriteStatus.Unsafe => AzureAuthPersistedWriteResult<T>.Unsafe(),
            _ => throw new InvalidOperationException("Unsupported secure-store write status."),
        };
    }

    internal static AzureAuthPersistedWriteResult<T> ValidateNoOp<T>(
        IAzureAuthSecureRecordStore recordStore,
        AzureAuthPersistedRecord<T> expected
    )
        where T : class
    {
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentNullException.ThrowIfNull(expected);
        AzureAuthRecordNamePolicy.EnsureValid(expected.RecordName);

        if (expected.Status != AzureAuthPersistedRecordStatus.Present)
        {
            throw new ArgumentException(
                "Only present records can be validated as unchanged snapshots.",
                nameof(expected)
            );
        }

        AzureAuthSecureRecordRevisionCheckResult revisionCheck = recordStore.CompareRevision(
            expected.RecordName,
            GetExpectedRevision(expected)
        );
        AzureAuthSecureRecordStoreContract.EnsureValid(revisionCheck);

        return revisionCheck.Status switch
        {
            AzureAuthSecureRecordRevisionCheckStatus.Match =>
                AzureAuthPersistedWriteResult<T>.Success(expected),
            AzureAuthSecureRecordRevisionCheckStatus.Conflict =>
                AzureAuthPersistedWriteResult<T>.Conflict(),
            AzureAuthSecureRecordRevisionCheckStatus.Unsupported =>
                AzureAuthPersistedWriteResult<T>.Unsupported(),
            AzureAuthSecureRecordRevisionCheckStatus.Unsafe =>
                AzureAuthPersistedWriteResult<T>.Unsafe(),
            _ => throw new InvalidOperationException(
                "Unsupported secure-store revision-check status."
            ),
        };
    }

    internal static T RequireValue<T>(AzureAuthPersistedRecord<T> record) where T : class
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Value
            ?? throw new ArgumentException("Present records must include a parsed value.", nameof(record));
    }

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
            string json = StrictUtf8.GetString(content.Span);
            return AzureAuthPersistedRecord<T>.Present(recordName, revision, deserialize(json));
        }
        catch (DecoderFallbackException)
        {
            return AzureAuthPersistedRecord<T>.Malformed(recordName, revision);
        }
        catch (JsonException)
        {
            return AzureAuthPersistedRecord<T>.Malformed(recordName, revision);
        }
        catch (FormatException)
        {
            return AzureAuthPersistedRecord<T>.Malformed(recordName, revision);
        }
        catch (ArgumentException)
        {
            return AzureAuthPersistedRecord<T>.Malformed(recordName, revision);
        }
    }

    private static string GetExpectedRevision<T>(AzureAuthPersistedRecord<T> expected) where T : class
    {
        return expected.Status switch
        {
            AzureAuthPersistedRecordStatus.Missing => AzureAuthSecureRecordStoreContract.MissingRevision,
            AzureAuthPersistedRecordStatus.Present or AzureAuthPersistedRecordStatus.Malformed =>
                expected.Revision
                ?? throw new ArgumentException("Expected records must include a revision.", nameof(expected)),
            _ => throw new InvalidOperationException(
                "Only missing, present, or malformed records can be used for compare-exchange."
            ),
        };
    }
}
