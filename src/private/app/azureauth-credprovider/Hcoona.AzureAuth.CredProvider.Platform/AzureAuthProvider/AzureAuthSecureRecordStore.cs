using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public interface IAzureAuthSecureRecordStore
{
    AzureAuthSecureRecordReadResult Read(string path);

    AzureAuthSecureRecordWriteResult CompareExchange(
        string path,
        string expectedRevision,
        ReadOnlyMemory<byte> newContent
    );

    AzureAuthSecureRecordWriteResult CompareDelete(string path, string expectedRevision);
}

internal interface IAzureAuthSecureRecordStoreOperationScope
{
    TResult Execute<TResult>(Func<IAzureAuthSecureRecordStore, TResult> operation);
}

internal static class AzureAuthSecureRecordStoreOperationScope
{
    internal static TResult Execute<TResult>(
        IAzureAuthSecureRecordStore store,
        Func<IAzureAuthSecureRecordStore, TResult> operation
    )
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(operation);
        return store is IAzureAuthSecureRecordStoreOperationScope scopedStore
            ? scopedStore.Execute(operation)
            : operation(store);
    }
}

public enum AzureAuthSecureRecordReadStatus
{
    Missing = 1,
    Present = 2,
}

public enum AzureAuthSecureRecordWriteStatus
{
    Success = 1,
    Conflict = 2,
}

public enum AzureAuthPersistedRecordStatus
{
    Missing = 1,
    Present = 2,
    Malformed = 3,
}

public sealed record AzureAuthSecureRecordReadResult(
    AzureAuthSecureRecordReadStatus Status,
    string? Revision = null,
    ReadOnlyMemory<byte> Content = default
)
{
    public string GetUtf8String() => Encoding.UTF8.GetString(Content.Span);

    public static AzureAuthSecureRecordReadResult Missing() =>
        new(AzureAuthSecureRecordReadStatus.Missing);

    public static AzureAuthSecureRecordReadResult Present(
        string revision,
        ReadOnlyMemory<byte> content
    ) => new(AzureAuthSecureRecordReadStatus.Present, revision, content);
}

public sealed record AzureAuthSecureRecordWriteResult(
    AzureAuthSecureRecordWriteStatus Status,
    string? Revision = null
)
{
    public static AzureAuthSecureRecordWriteResult Success(string revision) =>
        new(AzureAuthSecureRecordWriteStatus.Success, revision);

    public static AzureAuthSecureRecordWriteResult Conflict() =>
        new(AzureAuthSecureRecordWriteStatus.Conflict);
}

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
public sealed record AzureAuthPersistedRecord<T>
    where T : class
{
    public required string RecordName { get; init; }

    public required AzureAuthPersistedRecordStatus Status { get; init; }

    public string? Revision { get; init; }

    public T? Value { get; init; }

    public static AzureAuthPersistedRecord<T> Missing(string recordName) =>
        new() { RecordName = recordName, Status = AzureAuthPersistedRecordStatus.Missing };

    public static AzureAuthPersistedRecord<T> Present(
        string recordName,
        string revision,
        T value
    ) =>
        new()
        {
            RecordName = recordName,
            Status = AzureAuthPersistedRecordStatus.Present,
            Revision = revision,
            Value = value,
        };

    public static AzureAuthPersistedRecord<T> Malformed(string recordName, string revision) =>
        new()
        {
            RecordName = recordName,
            Status = AzureAuthPersistedRecordStatus.Malformed,
            Revision = revision,
        };
}

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
public sealed record AzureAuthPersistedWriteResult<T>
    where T : class
{
    public required AzureAuthSecureRecordWriteStatus Status { get; init; }

    public AzureAuthPersistedRecord<T>? Record { get; init; }

    public static AzureAuthPersistedWriteResult<T> Success(AzureAuthPersistedRecord<T> record) =>
        new() { Status = AzureAuthSecureRecordWriteStatus.Success, Record = record };

    public static AzureAuthPersistedWriteResult<T> Conflict() =>
        new() { Status = AzureAuthSecureRecordWriteStatus.Conflict };
}

public static class AzureAuthSecureRecordStoreContract
{
    public const string MissingRevision = "<missing>";

    internal static void EnsureKnownRecordName(string recordName)
    {
        if (
            recordName is not ("azureauth/provider-config.json" or "azureauth/account-binding.json")
        )
        {
            throw new ArgumentException("Unknown AzureAuth record name.", nameof(recordName));
        }
    }
}
