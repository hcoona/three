using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

/// <summary>
/// Trusted secure persistence seam. The implementation owns safe-root, no-follow, ownership,
/// permissions, linearizable compare-revision checks, atomic compare-exchange, and durable-write
/// guarantees. Revisions are opaque ABA-safe version tokens: every successful committed
/// <see cref="CompareExchange" /> returns a new nonblank token that is never reused for a later
/// state at the same record path.
/// </summary>
public interface IAzureAuthSecureRecordStore
{
    /// <summary>Reads the current record snapshot.</summary>
    AzureAuthSecureRecordReadResult Read(string path);

    /// <summary>
    /// Compares the current record revision against an expected opaque version token.
    /// </summary>
    AzureAuthSecureRecordRevisionCheckResult CompareRevision(string path, string expectedRevision);

    /// <summary>
    /// Atomically commits new content when <paramref name="expectedRevision" /> matches the
    /// current opaque version token and returns the new committed token on success.
    /// </summary>
    AzureAuthSecureRecordWriteResult CompareExchange(
        string path,
        string expectedRevision,
        ReadOnlyMemory<byte> newContent
    );
}

public enum AzureAuthSecureRecordReadStatus
{
    Unspecified = 0,
    Missing = 1,
    Present = 2,
    Unsupported = 3,
    Unsafe = 4,
    Unavailable = 5,
}

public enum AzureAuthSecureRecordWriteStatus
{
    Unspecified = 0,
    Success = 1,
    Conflict = 2,
    Unsupported = 3,
    Unsafe = 4,
    Unavailable = 5,
}

public enum AzureAuthSecureRecordRevisionCheckStatus
{
    Unspecified = 0,
    Match = 1,
    Conflict = 2,
    Unsupported = 3,
    Unsafe = 4,
    Unavailable = 5,
}

public enum AzureAuthPersistedRecordStatus
{
    Unspecified = 0,
    Missing = 1,
    Present = 2,
    Malformed = 3,
    Unsupported = 4,
    Unsafe = 5,
    Unavailable = 6,
}

public sealed record AzureAuthSecureRecordReadResult(
    AzureAuthSecureRecordReadStatus Status,
    string? Revision = null,
    ReadOnlyMemory<byte> Content = default)
{
    public string GetUtf8String() => Encoding.UTF8.GetString(Content.Span);

    public static AzureAuthSecureRecordReadResult Missing() =>
        new(AzureAuthSecureRecordReadStatus.Missing);

    public static AzureAuthSecureRecordReadResult Present(
        string revision,
        ReadOnlyMemory<byte> content
    ) => new(AzureAuthSecureRecordReadStatus.Present, revision, content);

    public static AzureAuthSecureRecordReadResult Unsupported() =>
        new(AzureAuthSecureRecordReadStatus.Unsupported);

    public static AzureAuthSecureRecordReadResult Unsafe() =>
        new(AzureAuthSecureRecordReadStatus.Unsafe);

    public static AzureAuthSecureRecordReadResult Unavailable() =>
        new(AzureAuthSecureRecordReadStatus.Unavailable);
}

public sealed record AzureAuthSecureRecordWriteResult(
    AzureAuthSecureRecordWriteStatus Status,
    string? Revision = null)
{
    public static AzureAuthSecureRecordWriteResult Success(string revision) =>
        new(AzureAuthSecureRecordWriteStatus.Success, revision);

    public static AzureAuthSecureRecordWriteResult Conflict() =>
        new(AzureAuthSecureRecordWriteStatus.Conflict);

    public static AzureAuthSecureRecordWriteResult Unsupported() =>
        new(AzureAuthSecureRecordWriteStatus.Unsupported);

    public static AzureAuthSecureRecordWriteResult Unsafe() =>
        new(AzureAuthSecureRecordWriteStatus.Unsafe);

    public static AzureAuthSecureRecordWriteResult Unavailable() =>
        new(AzureAuthSecureRecordWriteStatus.Unavailable);
}

public sealed record AzureAuthSecureRecordRevisionCheckResult(
    AzureAuthSecureRecordRevisionCheckStatus Status)
{
    public static AzureAuthSecureRecordRevisionCheckResult Match() =>
        new(AzureAuthSecureRecordRevisionCheckStatus.Match);

    public static AzureAuthSecureRecordRevisionCheckResult Conflict() =>
        new(AzureAuthSecureRecordRevisionCheckStatus.Conflict);

    public static AzureAuthSecureRecordRevisionCheckResult Unsupported() =>
        new(AzureAuthSecureRecordRevisionCheckStatus.Unsupported);

    public static AzureAuthSecureRecordRevisionCheckResult Unsafe() =>
        new(AzureAuthSecureRecordRevisionCheckStatus.Unsafe);

    public static AzureAuthSecureRecordRevisionCheckResult Unavailable() =>
        new(AzureAuthSecureRecordRevisionCheckStatus.Unavailable);
}

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
public sealed record AzureAuthPersistedRecord<T> where T : class
{
    public required string RecordName { get; init; }

    public required AzureAuthPersistedRecordStatus Status { get; init; }

    public string? Revision { get; init; }

    public T? Value { get; init; }

    public static AzureAuthPersistedRecord<T> Missing(string recordName)
    {
        AzureAuthRecordNamePolicy.EnsureValid(recordName);
        return new AzureAuthPersistedRecord<T>
        {
            RecordName = recordName,
            Status = AzureAuthPersistedRecordStatus.Missing,
        };
    }

    public static AzureAuthPersistedRecord<T> Present(string recordName, string revision, T value)
    {
        AzureAuthRecordNamePolicy.EnsureValid(recordName);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        ArgumentNullException.ThrowIfNull(value);

        return new AzureAuthPersistedRecord<T>
        {
            RecordName = recordName,
            Status = AzureAuthPersistedRecordStatus.Present,
            Revision = revision,
            Value = value,
        };
    }

    public static AzureAuthPersistedRecord<T> Malformed(string recordName, string revision)
    {
        AzureAuthRecordNamePolicy.EnsureValid(recordName);
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);

        return new AzureAuthPersistedRecord<T>
        {
            RecordName = recordName,
            Status = AzureAuthPersistedRecordStatus.Malformed,
            Revision = revision,
        };
    }

    public static AzureAuthPersistedRecord<T> Unsupported(string recordName)
    {
        AzureAuthRecordNamePolicy.EnsureValid(recordName);
        return new AzureAuthPersistedRecord<T>
        {
            RecordName = recordName,
            Status = AzureAuthPersistedRecordStatus.Unsupported,
        };
    }

    public static AzureAuthPersistedRecord<T> Unsafe(string recordName)
    {
        AzureAuthRecordNamePolicy.EnsureValid(recordName);
        return new AzureAuthPersistedRecord<T>
        {
            RecordName = recordName,
            Status = AzureAuthPersistedRecordStatus.Unsafe,
        };
    }

    public static AzureAuthPersistedRecord<T> Unavailable(string recordName)
    {
        AzureAuthRecordNamePolicy.EnsureValid(recordName);
        return new AzureAuthPersistedRecord<T>
        {
            RecordName = recordName,
            Status = AzureAuthPersistedRecordStatus.Unavailable,
        };
    }
}

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types")]
public sealed record AzureAuthPersistedWriteResult<T> where T : class
{
    public required AzureAuthSecureRecordWriteStatus Status { get; init; }

    public AzureAuthPersistedRecord<T>? Record { get; init; }

    public static AzureAuthPersistedWriteResult<T> Success(AzureAuthPersistedRecord<T> record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new AzureAuthPersistedWriteResult<T>
        {
            Status = AzureAuthSecureRecordWriteStatus.Success,
            Record = record,
        };
    }

    public static AzureAuthPersistedWriteResult<T> Conflict() =>
        new() { Status = AzureAuthSecureRecordWriteStatus.Conflict };

    public static AzureAuthPersistedWriteResult<T> Unsupported() =>
        new() { Status = AzureAuthSecureRecordWriteStatus.Unsupported };

    public static AzureAuthPersistedWriteResult<T> Unsafe() =>
        new() { Status = AzureAuthSecureRecordWriteStatus.Unsafe };

    public static AzureAuthPersistedWriteResult<T> Unavailable() =>
        new() { Status = AzureAuthSecureRecordWriteStatus.Unavailable };
}

public static class AzureAuthSecureRecordStoreContract
{
    public const string MissingRevision = "<missing>";

    public static void EnsureValid(AzureAuthSecureRecordReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Status)
        {
            case AzureAuthSecureRecordReadStatus.Missing:
            case AzureAuthSecureRecordReadStatus.Unsupported:
            case AzureAuthSecureRecordReadStatus.Unsafe:
            case AzureAuthSecureRecordReadStatus.Unavailable:
                EnsureNoRevision(result.Revision, nameof(result.Revision));
                if (!result.Content.IsEmpty)
                {
                    throw new ArgumentException(
                        "Non-present read results must not include content.",
                        nameof(result)
                    );
                }

                return;
            case AzureAuthSecureRecordReadStatus.Present:
                EnsureRevision(result.Revision, nameof(result.Revision));
                return;
            default:
                throw new ArgumentException("Unsupported secure-store read status.", nameof(result));
        }
    }

    public static void EnsureValid(AzureAuthSecureRecordWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Status)
        {
            case AzureAuthSecureRecordWriteStatus.Success:
                EnsureRevision(result.Revision, nameof(result.Revision));
                return;
            case AzureAuthSecureRecordWriteStatus.Conflict:
            case AzureAuthSecureRecordWriteStatus.Unsupported:
            case AzureAuthSecureRecordWriteStatus.Unsafe:
            case AzureAuthSecureRecordWriteStatus.Unavailable:
                EnsureNoRevision(result.Revision, nameof(result.Revision));
                return;
            default:
                throw new ArgumentException("Unsupported secure-store write status.", nameof(result));
        }
    }

    public static void EnsureValid(AzureAuthSecureRecordRevisionCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        switch (result.Status)
        {
            case AzureAuthSecureRecordRevisionCheckStatus.Match:
            case AzureAuthSecureRecordRevisionCheckStatus.Conflict:
            case AzureAuthSecureRecordRevisionCheckStatus.Unsupported:
            case AzureAuthSecureRecordRevisionCheckStatus.Unsafe:
            case AzureAuthSecureRecordRevisionCheckStatus.Unavailable:
                return;
            default:
                throw new ArgumentException(
                    "Unsupported secure-store revision-check status.",
                    nameof(result)
                );
        }
    }

    private static void EnsureRevision(string? revision, string paramName)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision == MissingRevision)
        {
            throw new ArgumentException("A nonblank revision is required.", paramName);
        }
    }

    private static void EnsureNoRevision(string? revision, string paramName)
    {
        if (revision is not null)
        {
            throw new ArgumentException("This status must not include a revision.", paramName);
        }
    }
}

internal sealed class UnsupportedAzureAuthSecureRecordStore : IAzureAuthSecureRecordStore
{
    public AzureAuthSecureRecordReadResult Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return AzureAuthSecureRecordReadResult.Unsupported();
    }

    public AzureAuthSecureRecordRevisionCheckResult CompareRevision(
        string path,
        string expectedRevision
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        return AzureAuthSecureRecordRevisionCheckResult.Unsupported();
    }

    public AzureAuthSecureRecordWriteResult CompareExchange(
        string path,
        string expectedRevision,
        ReadOnlyMemory<byte> newContent
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedRevision);
        return AzureAuthSecureRecordWriteResult.Unsupported();
    }
}

internal static class AzureAuthRecordNamePolicy
{
    internal static void EnsureValid(string recordName)
    {
        if (
            string.IsNullOrWhiteSpace(recordName)
            || !string.Equals(recordName, recordName.Trim(), StringComparison.Ordinal)
            || recordName[0] == '/'
            || recordName[^1] == '/'
            || recordName.Contains('\\')
            || recordName.Contains('%')
            || recordName.Any(static character => !IsAllowedRecordCharacter(character))
        )
        {
            throw new ArgumentException("Record names must be safe relative paths.", nameof(recordName));
        }

        string[] segments = recordName.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(IsForbiddenSegment))
        {
            throw new ArgumentException("Record names must be safe relative paths.", nameof(recordName));
        }
    }

    private static bool IsAllowedRecordCharacter(char value) => value is >= '!' and <= '~';

    private static bool IsForbiddenSegment(string segment)
    {
        return string.IsNullOrEmpty(segment)
            || segment is "." or ".."
            || segment[0] == '.'
            || segment[^1] == '.'
            || segment.Any(static character =>
                !(character >= 'a' && character <= 'z')
                && !char.IsAsciiDigit(character)
                && character is not ('-' or '_' or '.'));
    }
}
