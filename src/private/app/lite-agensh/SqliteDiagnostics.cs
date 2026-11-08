using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

internal static class SqliteDiagnostics
{
    private const int MaximumDatabases = 32;
    private const int MaximumConnections = 128;
    private const int MaximumRecords = 128;
    private const int MaximumActivityBytes = 128 * 1024;
    private const int MaximumCollectionBytes = 256 * 1024;
    private const int MaximumIncidentBytes = 512 * 1024;
    private const int ActivitySlots = 32;
    private const int IncidentSlots = 8;
    private static readonly object Gate = new();
    private static readonly ConditionalWeakTable<SqliteConnection, ConnectionActivity> Connections =
        new();
    private static readonly Dictionary<string, DatabaseActivity> Databases = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal
    );
    private static readonly AsyncLocal<string?> Actor = new();
    private static readonly DateTimeOffset? ProcessBirth = OwnProcessBirth();

    internal static IDisposable UseActor(string actor)
    {
        string? previous = Actor.Value;
        Actor.Value = ValidActor(actor) ? actor : null;
        return new ActorScope(previous);
    }

    internal static void Attach(SqliteConnection connection, string kind)
    {
        string path = Path.GetFullPath(
            new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource
        );
        DatabaseActivity database;
        ConnectionActivity activity;
        lock (Gate)
        {
            if (!Databases.TryGetValue(path, out DatabaseActivity? existing))
            {
                if (Databases.Count >= MaximumDatabases)
                {
                    string? unused = Databases
                        .FirstOrDefault(pair => pair.Value.IsIdle).Key;
                    if (unused is null)
                    {
                        Warn("SQLite diagnostic database capacity exhausted.");
                        return;
                    }

                    Databases.Remove(unused);
                }

                existing = new(path, kind);
                Databases.Add(path, existing);
            }

            database = existing;
            activity = new(database, Actor.Value);
            Connections.Add(connection, activity);
            Interlocked.Increment(ref database.AttachedConnections);
        }

        lock (database.Gate)
        {
            if (database.Active.Count < MaximumConnections)
            {
                database.Active.Add(activity.Id, activity);
            }
            else
            {
                database.Truncated = true;
            }
        }

        connection.StateChange += (_, change) =>
        {
            if (change.CurrentState == ConnectionState.Closed)
            {
                Close(connection);
            }
        };
    }

    internal static T Observe<T>(
        SqliteConnection connection,
        string operation,
        string phase,
        Func<T> action,
        string? transaction = null,
        bool? deferred = null
    )
    {
        Record(connection, operation, phase + "-requested", 0, transaction, deferred);
        long start = Stopwatch.GetTimestamp();
        try
        {
            T result = action();
            Record(
                connection, operation, phase + "-succeeded",
                Stopwatch.GetElapsedTime(start).TotalMilliseconds, transaction, deferred
            );
            return result;
        }
        catch (Exception error)
        {
            Record(
                connection, operation, phase + "-failed",
                Stopwatch.GetElapsedTime(start).TotalMilliseconds, transaction, deferred, error
            );
            throw;
        }
    }

    internal static void Complete(SqliteConnection connection)
    {
        if (!Connections.TryGetValue(connection, out ConnectionActivity? activity))
        {
            return;
        }

        lock (activity.Database.Gate)
        {
            activity.Transaction = null;
            activity.Acquired = false;
            activity.Deferred = null;
            activity.TransactionOperation = null;
        }
    }

    internal static void Close(SqliteConnection connection)
    {
        if (!Connections.TryGetValue(connection, out ConnectionActivity? activity))
        {
            return;
        }

        lock (activity.Database.Gate)
        {
            bool acquired = activity.Acquired;
            Record(connection, "connection", "closed", 0);
            activity.Database.Active.Remove(activity.Id);
            Interlocked.Decrement(ref activity.Database.AttachedConnections);
            if (acquired || activity.Database.IsIdle)
            {
                Persist(activity.Database);
            }
            if (activity.Database.IsIdle)
            {
                try
                {
                    activity.Database.Lease?.Dispose();
                }
                catch (Exception error) when (IsDiagnosticFailure(error))
                {
                    Warn("SQLite activity closure failed; database outcome preserved.");
                }
                activity.Database.Lease = null;
            }

            Connections.Remove(connection);
        }
    }

    private static void Record(
        SqliteConnection connection,
        string operation,
        string phase,
        double elapsed,
        string? transaction = null,
        bool? deferred = null,
        Exception? error = null
    )
    {
        if (!Connections.TryGetValue(connection, out ConnectionActivity? activity))
        {
            return;
        }

        lock (activity.Database.Gate)
        {
            if (transaction is not null)
            {
                activity.Transaction = transaction;
                activity.Deferred = deferred;
                activity.TransactionOperation =
                    SafeIdentifier(operation) ? operation : "UnknownOperation";
            }

            if (phase == "acquisition-succeeded")
            {
                activity.Acquired = true;
            }
            else if (phase is "commit-succeeded" or "rollback-succeeded")
            {
                activity.Acquired = false;
            }

            SqliteException? sqlite = error as SqliteException;
            Activity record = new(
                activity.Id, activity.Transaction, activity.Actor,
                activity.TransactionOperation
                    ?? (SafeIdentifier(operation) ? operation : "UnknownOperation"),
                SafeIdentifier(operation) ? operation : "UnknownOperation", phase,
                DateTimeOffset.UtcNow, Stopwatch.GetTimestamp(), elapsed, activity.Deferred,
                activity.Acquired && activity.Deferred == false,
                sqlite?.SqliteErrorCode, sqlite?.SqliteExtendedErrorCode
            );
            activity.Last = record;
            DatabaseActivity database = activity.Database;
            database.Recent.Enqueue(record);
            if (database.Recent.Count > MaximumRecords)
            {
                database.Recent.Dequeue();
                database.Truncated = true;
            }

            // Flush ownership changes immediately; ordinary observations stay in the ring.
            if (error is not null
                || phase is "acquisition-succeeded" or "commit-succeeded" or "rollback-succeeded")
            {
                Persist(database);
            }
            if (sqlite?.SqliteErrorCode is 5 or 6)
            {
                Capture(database, record, sqlite);
            }
        }
    }

    private static void Persist(DatabaseActivity database)
    {
        try
        {
            Directory.CreateDirectory(database.Directory);
            if (database.Lease is null)
            {
                for (int slot = 0; slot < ActivitySlots; slot++)
                {
                    try
                    {
                        database.Lease = new FileStream(
                            Path.Combine(database.Directory, $"activity-{slot:D2}.lease"),
                            FileMode.OpenOrCreate, FileAccess.Write, FileShare.None
                        );
                        database.ActivityPath = Path.Combine(
                            database.Directory, $"activity-{slot:D2}.json"
                        );
                        break;
                    }
                    catch (IOException)
                    {
                        // Another application process may own this bounded slot.
                    }
                }

                if (database.Lease is null)
                {
                    throw new IOException("No application activity slot is available.");
                }
            }

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(database.Snapshot(), Json.Options);
            if (bytes.Length > MaximumActivityBytes)
            {
                throw new IOException("SQLite activity byte capacity exhausted.");
            }

            using FileStream output = new(
                database.ActivityPath!, FileMode.Create, FileAccess.Write, FileShare.Read
            );
            output.Write(bytes);
            output.Flush();
        }
        catch (Exception error) when (IsDiagnosticFailure(error))
        {
            database.Warning = "SQLite activity persistence is unavailable or incomplete.";
            if (!database.Warned)
            {
                database.Warned = true;
                Warn(database.Warning);
            }
        }
    }

    private static void Capture(DatabaseActivity database, Activity failure, SqliteException error)
    {
        try
        {
            List<Source> sources = [];
            int remaining = MaximumCollectionBytes;
            for (int slot = 0; slot < ActivitySlots; slot++)
            {
                string path = Path.Combine(database.Directory, $"activity-{slot:D2}.json");
                if (!File.Exists(path))
                {
                    continue;
                }

                if (remaining == 0)
                {
                    sources.Add(new(slot, "collection-budget-exhausted", null));
                    break;
                }

                try
                {
                    using FileStream input = new(
                        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete
                    );
                    int limit = Math.Min(MaximumActivityBytes, remaining);
                    byte[] buffer = new byte[limit];
                    int count = input.ReadAtLeast(buffer, limit, throwOnEndOfStream: false);
                    remaining -= count;
                    if (input.ReadByte() != -1)
                    {
                        sources.Add(new(slot, "truncated", null));
                        continue;
                    }

                    Snapshot? snapshot = JsonSerializer.Deserialize<Snapshot>(
                        buffer.AsSpan(0, count), Json.Options
                    );
                    if (
                        snapshot is null || snapshot.Version != 1
                        || snapshot.Database != database.Identity
                        || snapshot.Kind is not ("run" or "campaign")
                        || snapshot.ProcessId <= 0
                        || snapshot.Warning is not (
                            null or "SQLite activity persistence is unavailable or incomplete.")
                        || snapshot.Active is null || snapshot.Recent is null
                        || snapshot.Active.Length > MaximumConnections
                        || snapshot.Recent.Length > MaximumRecords
                        || !snapshot.Active.Concat(snapshot.Recent).All(ValidActivity)
                    )
                    {
                        sources.Add(new(slot, "malformed-or-foreign", null));
                        continue;
                    }

                    string status = DateTimeOffset.UtcNow - snapshot.ObservedAtUtc
                        > TimeSpan.FromSeconds(30)
                        ? "stale" : "available";
                    sources.Add(new(slot, status, snapshot));
                }
                catch (Exception exception) when (IsDiagnosticFailure(exception))
                {
                    sources.Add(new(slot, "unavailable-or-malformed", null));
                }
            }

            Incident incident = new(
                1, database.Identity, Environment.ProcessId, ProcessBirth, failure,
                "Partial application observations; non-atomic; "
                    + "external or uninstrumented holders remain unknown.",
                database.Warning, sources.ToArray()
            );
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(incident, Json.Options);
            if (bytes.Length > MaximumIncidentBytes)
            {
                throw new IOException("SQLite incident byte capacity exhausted.");
            }

            for (int slot = 0; slot < IncidentSlots; slot++)
            {
                string path = Path.Combine(database.Directory, $"incident-{slot:D2}.json");
                try
                {
                    using FileStream output = new(
                        path, FileMode.CreateNew, FileAccess.Write, FileShare.Read
                    );
                    output.Write(bytes);
                    output.Flush();
                    error.Data["LiteAgensh.SqliteIncident"] = path;
                    Warn($"SQLite contention evidence: {path}");
                    return;
                }
                catch (IOException) when (File.Exists(path))
                {
                    // Preserve every previously captured incident, including incomplete files.
                }
            }

            throw new IOException(
                "SQLite incident capacity exhausted; previous evidence retained."
            );
        }
        catch (Exception exception) when (IsDiagnosticFailure(exception))
        {
            const string warning =
                "SQLite incident persistence failed or capacity exhausted; "
                + "original database error preserved.";
            error.Data["LiteAgensh.SqliteDiagnosticWarning"] = warning;
            Warn(warning);
        }
    }

    private static bool IsDiagnosticFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException
            or InvalidOperationException or ArgumentException or NotSupportedException;

    private static bool SafeIdentifier(string text) =>
        text.Length is > 0 and <= 128
        && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.');

    private static bool ValidActor(string actor) =>
        actor == "owner"
        || (actor.Length is > 6 and <= 16
            && actor.StartsWith("worker", StringComparison.Ordinal)
            && actor[6] is >= '1' and <= '9'
            && actor.AsSpan(6).IndexOfAnyExceptInRange('0', '9') == -1);

    private static bool ValidActivity(Activity? activity) =>
        activity is not null
        && Guid.TryParseExact(activity.Connection, "N", out _)
        && (activity.Transaction is null || Guid.TryParseExact(activity.Transaction, "N", out _))
        && activity.Operation is not null && SafeIdentifier(activity.Operation)
        && activity.Boundary is not null && SafeIdentifier(activity.Boundary)
        && (activity.Actor is null || ValidActor(activity.Actor))
        && double.IsFinite(activity.ElapsedMilliseconds)
        && activity.ElapsedMilliseconds >= 0
        && (!activity.WriteAcquisitionObserved || activity.Deferred == false)
        && activity.Phase is
            "open-requested" or "open-succeeded" or "open-failed" or "closed"
            or "acquisition-requested" or "acquisition-succeeded" or "acquisition-failed"
            or "commit-requested" or "commit-succeeded" or "commit-failed"
            or "rollback-requested" or "rollback-succeeded" or "rollback-failed"
            or "transaction-dispose-requested" or "transaction-dispose-succeeded"
            or "transaction-dispose-failed" or "statement-requested" or "statement-succeeded"
            or "statement-failed" or "read-requested" or "read-succeeded" or "read-failed"
            or "reader-dispose-requested" or "reader-dispose-succeeded" or "reader-dispose-failed";

    private static void Warn(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException)
        {
        }
    }

    private static DateTimeOffset? OwnProcessBirth()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return new DateTimeOffset(process.StartTime.ToUniversalTime());
        }
        catch (Exception error) when (
            error is System.ComponentModel.Win32Exception or InvalidOperationException
        )
        {
            return null;
        }
    }

    private sealed class ActorScope(string? previous) : IDisposable
    {
        public void Dispose() => Actor.Value = previous;
    }

    private sealed class ConnectionActivity(DatabaseActivity database, string? actor)
    {
        internal string Id { get; } = Guid.NewGuid().ToString("N");
        internal DatabaseActivity Database { get; } = database;
        internal string? Actor { get; } = actor;
        internal string? Transaction { get; set; }
        internal bool? Deferred { get; set; }
        internal string? TransactionOperation { get; set; }
        internal bool Acquired { get; set; }
        internal Activity? Last { get; set; }
    }

    private sealed class DatabaseActivity(string path, string kind)
    {
        internal object Gate { get; } = new();
        internal int AttachedConnections;
        internal bool IsIdle => Volatile.Read(ref AttachedConnections) == 0;

        internal string Directory { get; } = path + ".diagnostics";
        internal string Identity { get; } = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path
            ))
        );
        internal Dictionary<string, ConnectionActivity> Active { get; } = [];
        internal Queue<Activity> Recent { get; } = [];
        internal FileStream? Lease { get; set; }
        internal string? ActivityPath { get; set; }
        internal bool Truncated { get; set; }
        internal string? Warning { get; set; }
        internal bool Warned { get; set; }

        internal Snapshot Snapshot() => new(
            1, Identity, kind, Environment.ProcessId, ProcessBirth, DateTimeOffset.UtcNow,
            Truncated, Warning,
            Active.Values.Where(value => value.Acquired)
                .Select(value => value.Last).OfType<Activity>().ToArray(),
            Recent.ToArray()
        );
    }

    private sealed record Activity(
        string Connection, string? Transaction, string? Actor, string Operation,
        string Boundary, string Phase,
        DateTimeOffset ObservedAtUtc, long MonotonicTimestamp, double ElapsedMilliseconds,
        bool? Deferred, bool WriteAcquisitionObserved, int? PrimaryCode, int? ExtendedCode
    );

    private sealed record Snapshot(
        int Version, string Database, string Kind, int ProcessId, DateTimeOffset? ProcessBirthUtc,
        DateTimeOffset ObservedAtUtc, bool Truncated, string? Warning,
        Activity[] Active, Activity[] Recent
    );

    private sealed record Source(int Slot, string Status, Snapshot? Snapshot);

    private sealed record Incident(
        int Version, string Database, int ProcessId, DateTimeOffset? ProcessBirthUtc,
        Activity Failure, string Coverage,
        string? Warning, Source[] Sources
    );
}

public sealed class SqliteTransactionScope : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly SqliteTransaction transaction;
    private readonly string operation;
    private readonly bool deferred;

    internal SqliteTransactionScope(
        SqliteConnection connection, SqliteTransaction transaction, string operation,
        string id, bool deferred
    )
    {
        this.connection = connection;
        this.transaction = transaction;
        this.operation = operation;
        this.deferred = deferred;
        Id = id;
    }

    public string Id { get; }

    public static implicit operator SqliteTransaction(SqliteTransactionScope scope) =>
        scope.transaction;

    public void Commit()
    {
        SqliteDiagnostics.Observe(
            connection, operation, "commit",
            () => { transaction.Commit(); return true; }, Id, deferred
        );
        SqliteDiagnostics.Complete(connection);
    }

    public void Rollback()
    {
        SqliteDiagnostics.Observe(
            connection, operation, "rollback",
            () => { transaction.Rollback(); return true; }, Id, deferred
        );
        SqliteDiagnostics.Complete(connection);
    }

    public void Dispose()
    {
        try
        {
            // These are the selected provider's own Dispose rollback conditions.
            if (transaction.Connection is not null && connection.State == ConnectionState.Open)
            {
                Rollback();
            }
        }
        finally
        {
            SqliteDiagnostics.Observe(
                connection, operation, "transaction-dispose",
                () => { transaction.Dispose(); return true; }, Id, deferred
            );
        }
    }
}

public sealed class ObservedSqliteReader : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly SqliteDataReader reader;

    internal ObservedSqliteReader(SqliteConnection connection, SqliteDataReader reader)
    {
        this.connection = connection;
        this.reader = reader;
    }

    public int FieldCount => reader.FieldCount;
    public bool Read() => SqliteDiagnostics.Observe(connection, "reader", "read", reader.Read);
    public bool IsDBNull(int ordinal) => reader.IsDBNull(ordinal);
    public string GetName(int ordinal) => reader.GetName(ordinal);
    public object GetValue(int ordinal) => reader.GetValue(ordinal);
    public int GetInt32(int ordinal) => reader.GetInt32(ordinal);
    public bool GetBoolean(int ordinal) => reader.GetBoolean(ordinal);
    public long GetInt64(int ordinal) => reader.GetInt64(ordinal);
    public string GetString(int ordinal) => reader.GetString(ordinal);
    public double GetDouble(int ordinal) => reader.GetDouble(ordinal);

    public void Dispose() => SqliteDiagnostics.Observe(
        connection, "reader", "reader-dispose",
        () => { reader.Dispose(); return true; }
    );
}
