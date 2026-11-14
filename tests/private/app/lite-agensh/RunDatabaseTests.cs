using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class RunDatabaseTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [DoNotParallelize]
    public void IoFailureRetainsOriginalErrorAndSafeSummaryWhenLocalEvidenceIsUnavailable(
        bool blockEvidence
    )
    {
        using RunFixture fixture = new();
        string directory = Path.Combine(fixture.StatePath, "run.sqlite.diagnostics");
        if (blockEvidence)
        {
            Directory.CreateDirectory(fixture.StatePath);
            File.WriteAllText(directory, "Owned fixture blocks diagnostic persistence.");
        }

        RunDatabase database = fixture.CreateDatabase();
        database.WriteMetadata("io-preserved", "before");
        using SqliteConnection connection = database.Open();
        string secret = "private-io-message-" + Guid.NewGuid().ToString("N");
        SqliteException original = new(secret, 10, 778);
        int calls = 0;
        using StringWriter output = new();
        TextWriter previous = Console.Error;
        SqliteException error;
        try
        {
            Console.SetError(output);
            error = Assert.ThrowsExactly<SqliteException>(() =>
                SqliteDiagnostics.Observe<int>(
                    connection, "IoProbe", "statement",
                    () => { calls++; throw original; }
                )
            );
        }
        finally
        {
            Console.SetError(previous);
        }

        Assert.AreSame(original, error);
        Assert.AreEqual(1, calls);
        Assert.AreEqual("before", database.ReadMetadata("io-preserved"));
        string captured = output.ToString();
        const string prefix = "SQLite failure summary: ";
        string summary = Assert.ContainsSingle(
            captured.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
        );
        using JsonDocument document = JsonDocument.Parse(summary[prefix.Length..]);
        JsonElement failure = document.RootElement.GetProperty("failure");
        Assert.AreEqual(10, failure.GetProperty("primaryCode").GetInt32());
        Assert.AreEqual(778, failure.GetProperty("extendedCode").GetInt32());
        Assert.AreEqual("IoProbe", failure.GetProperty("operation").GetString());
        Assert.AreEqual("statement-failed", failure.GetProperty("phase").GetString());
        Assert.DoesNotContain(secret, captured);
        Assert.DoesNotContain(fixture.StatePath, captured);
        if (blockEvidence)
        {
            Assert.IsNull(error.Data["LiteAgensh.SqliteIncident"]);
            Assert.Contains(
                "original database error preserved",
                Assert.IsInstanceOfType<string>(
                    error.Data["LiteAgensh.SqliteDiagnosticWarning"])
            );
        }
        else
        {
            string path = Assert.IsInstanceOfType<string>(error.Data["LiteAgensh.SqliteIncident"]);
            using JsonDocument incident = JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual(
                778,
                incident.RootElement.GetProperty("failure").GetProperty("extendedCode").GetInt32()
            );
            Assert.DoesNotContain(secret, File.ReadAllText(path));
        }
    }

    [TestMethod]
    public void BusyClaimRetainsAvailableWriterAndUnknownCoverageWithoutSqlOrPayloads()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        CollaborationOperations collaboration = new(database);
        collaboration.QueueKickoff("worker1", "initial");
        string secret = "private-sql-parameter-" + Guid.NewGuid().ToString("N");
        const string statement =
            "INSERT INTO meta(key,value) VALUES('private-diagnostic',$secret);";
        using SqliteConnection writer = database.Open();
        using SqliteTransactionScope held = Sql.BeginTransaction(writer);
        Sql.Execute(writer, statement, held, ("$secret", secret));
        using SqliteConnection reader = database.Open();
        using SqliteTransactionScope deferred = Sql.BeginTransaction(reader, deferred: true);
        string directory = Path.Combine(fixture.StatePath, "run.sqlite.diagnostics");
        File.WriteAllText(Path.Combine(directory, "activity-01.json"), "{malformed");
        File.WriteAllBytes(Path.Combine(directory, "activity-29.json"), new byte[128 * 1024]);
        File.WriteAllBytes(Path.Combine(directory, "activity-30.json"), new byte[128 * 1024]);
        File.WriteAllText(Path.Combine(directory, "activity-31.json"), "{}");

        SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
            collaboration.ClaimBatch("worker1")
        );

        Assert.AreEqual(5, error.SqliteErrorCode);
        Assert.AreEqual(5, error.SqliteExtendedErrorCode);
        string path = Assert.IsInstanceOfType<string>(error.Data["LiteAgensh.SqliteIncident"]);
        string text = File.ReadAllText(path);
        using JsonDocument incident = JsonDocument.Parse(text);
        AssertCollectionInterval(incident.RootElement);
        JsonElement failure = incident.RootElement.GetProperty("failure");
        Assert.AreEqual("ClaimBatch", failure.GetProperty("operation").GetString());
        Assert.AreEqual("worker1", failure.GetProperty("actor").GetString());
        Assert.AreEqual("acquisition-failed", failure.GetProperty("phase").GetString());
        Assert.AreEqual(5, failure.GetProperty("primaryCode").GetInt32());
        Assert.AreEqual(5, failure.GetProperty("extendedCode").GetInt32());
        Assert.IsGreaterThan(4000, failure.GetProperty("elapsedMilliseconds").GetDouble());
        JsonElement[] active = incident.RootElement.GetProperty("sources")
            .EnumerateArray()
            .Where(source => source.GetProperty("status").GetString() == "available")
            .SelectMany(source =>
                source.GetProperty("snapshot").GetProperty("active").EnumerateArray())
            .ToArray();
        JsonElement knownWriter = Assert.ContainsSingle(
            active.Where(activity => activity.GetProperty("transaction").GetString() == held.Id)
        );
        Assert.IsTrue(knownWriter.GetProperty("writeAcquisitionObserved").GetBoolean());
        JsonElement readTransaction = Assert.ContainsSingle(
            active.Where(activity => activity.GetProperty("transaction").GetString() == deferred.Id)
        );
        Assert.IsFalse(readTransaction.GetProperty("writeAcquisitionObserved").GetBoolean());
        Assert.Contains(
            "unavailable-or-malformed",
            incident.RootElement.GetProperty("sources").EnumerateArray()
                .Select(source => source.GetProperty("status").GetString())
        );
        Assert.Contains(
            "collection-budget-exhausted",
            incident.RootElement.GetProperty("sources").EnumerateArray()
                .Select(source => source.GetProperty("status").GetString())
        );
        Assert.Contains("unknown", incident.RootElement.GetProperty("coverage").GetString()!);
        foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
        {
            string contents = File.ReadAllText(file);
            Assert.DoesNotContain(secret, contents);
            Assert.DoesNotContain(statement, contents);
        }

        Assert.AreEqual(0L, Sql.Scalar(reader, "SELECT COUNT(*) FROM delivery_batches;", deferred));
    }

    [TestMethod]
    public async Task GitContextAndNoticeFailureRetainValidatedActors()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        LocalTools tools = new(database);
        using SqliteConnection writer = database.Open();
        using SqliteTransactionScope held = Sql.BeginTransaction(writer);

        JsonObject changes = await tools.Git.InvokeAsync("worker1", "change_list", new());
        Assert.IsTrue(changes["ok"]!.GetValue<bool>());
        _ = tools.Collaboration.ReadSharedContext("worker2");
        SqliteException error = await Assert.ThrowsExactlyAsync<SqliteException>(async () =>
            await tools.InvokeAsync("worker1", "run_info", new())
        );

        Assert.AreEqual(5, error.SqliteErrorCode);
        string path = Assert.IsInstanceOfType<string>(error.Data["LiteAgensh.SqliteIncident"]);
        using JsonDocument incident = JsonDocument.Parse(File.ReadAllText(path));
        AssertCollectionInterval(incident.RootElement);
        JsonElement failure = incident.RootElement.GetProperty("failure");
        Assert.AreEqual("AddNotices", failure.GetProperty("operation").GetString());
        Assert.AreEqual("worker1", failure.GetProperty("actor").GetString());
        JsonElement[] recent = incident.RootElement.GetProperty("sources").EnumerateArray()
            .Where(source => source.GetProperty("status").GetString() == "available")
            .SelectMany(source =>
                source.GetProperty("snapshot").GetProperty("recent").EnumerateArray())
            .ToArray();
        Assert.Contains(
            "worker1",
            recent.Where(record => record.GetProperty("operation").GetString() == "Reader")
                .Select(record => record.GetProperty("actor").GetString())
        );
        Assert.Contains(
            "worker2",
            recent.Where(record =>
                record.GetProperty("operation").GetString() == "ReadSharedContext")
                .Select(record => record.GetProperty("actor").GetString())
        );
    }

    [TestMethod]
    public void LockedStatementCapturesCodesAndPreservesPriorIncidentAtCapacity()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        using SqliteConnection connection = database.Open();
        connection.DefaultTimeout = 1;
        using SqliteCommand command = Sql.Command(connection, "SELECT key FROM meta;");
        using ObservedSqliteReader reader = Sql.Reader(command);
        Assert.IsTrue(reader.Read());
        string? firstPath = null;
        string? firstContents = null;

        for (int attempt = 0; attempt < 9; attempt++)
        {
            SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
                Sql.Execute(connection, "DROP TABLE meta;")
            );
            Assert.AreEqual(6, error.SqliteErrorCode);
            Assert.AreEqual(6, error.SqliteExtendedErrorCode);
            if (attempt == 0)
            {
                firstPath =
                    Assert.IsInstanceOfType<string>(error.Data["LiteAgensh.SqliteIncident"]);
                firstContents = File.ReadAllText(firstPath);
                using JsonDocument incident = JsonDocument.Parse(firstContents);
                JsonElement failure = incident.RootElement.GetProperty("failure");
                Assert.AreEqual("statement-failed", failure.GetProperty("phase").GetString());
                Assert.AreEqual(6, failure.GetProperty("primaryCode").GetInt32());
            }
            else if (attempt == 8)
            {
                Assert.IsNull(error.Data["LiteAgensh.SqliteIncident"]);
                Assert.Contains(
                    "capacity exhausted",
                    Assert.IsInstanceOfType<string>(
                        error.Data["LiteAgensh.SqliteDiagnosticWarning"])
                );
            }
        }

        Assert.IsNotNull(firstPath);
        Assert.AreEqual(firstContents, File.ReadAllText(firstPath));
        Assert.HasCount(
            8,
            Directory.EnumerateFiles(
                Path.Combine(fixture.StatePath, "run.sqlite.diagnostics"), "incident-*.json"
            )
        );
    }

    [TestMethod]
    public void SuccessfulCommitAndRollbackRetireAcquisitionBeforeScopeEnds()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope committed = Sql.BeginTransaction(connection);
        Sql.Execute(
            connection, "INSERT INTO meta(key,value) VALUES('checked','committed');", committed
        );
        committed.Commit();
        string directory = Path.Combine(fixture.StatePath, "run.sqlite.diagnostics");
        AssertRetired(directory, committed.Id);

        using SqliteConnection other = database.Open();
        using SqliteTransactionScope rolledBack = Sql.BeginTransaction(other);
        Sql.Execute(
            other, "UPDATE meta SET value='not-committed' WHERE key='checked';", rolledBack
        );
        rolledBack.Rollback();
        AssertRetired(directory, rolledBack.Id);
        Assert.AreEqual("committed", database.ReadMetadata("checked"));
        using SqliteTransactionScope next = Sql.BeginTransaction(connection);
        Sql.Execute(
            connection, "UPDATE meta SET value='next' WHERE key='checked';", next
        );
        next.Commit();
        Assert.AreEqual("next", database.ReadMetadata("checked"));
    }

    [TestMethod]
    public void FailedDiagnosticPersistencePreservesBusyAndDoesNotMutateDelivery()
    {
        using RunFixture fixture = new();
        Directory.CreateDirectory(fixture.StatePath);
        File.WriteAllText(
            Path.Combine(fixture.StatePath, "run.sqlite.diagnostics"),
            "Owned fixture intentionally blocks diagnostic directory creation."
        );
        RunDatabase database = fixture.CreateDatabase();
        CollaborationOperations collaboration = new(database);
        collaboration.QueueKickoff("worker1", "initial");
        using SqliteConnection connection = database.Open();
        using SqliteTransactionScope held = Sql.BeginTransaction(connection);

        SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
            collaboration.ClaimBatch("worker1")
        );

        Assert.AreEqual(5, error.SqliteErrorCode);
        Assert.AreEqual(5, error.SqliteExtendedErrorCode);
        Assert.IsNull(error.Data["LiteAgensh.SqliteIncident"]);
        Assert.Contains(
            "original database error preserved",
            Assert.IsInstanceOfType<string>(error.Data["LiteAgensh.SqliteDiagnosticWarning"])
        );
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM delivery_batches;", held));
    }

    private static void AssertRetired(string directory, string transaction)
    {
        foreach (string path in Directory.EnumerateFiles(directory, "activity-*.json"))
        {
            using JsonDocument activity = JsonDocument.Parse(File.ReadAllText(path));
            Assert.IsEmpty(
                activity.RootElement.GetProperty("active").EnumerateArray()
                    .Where(item => item.GetProperty("transaction").GetString() == transaction)
            );
        }
    }

    private static void AssertCollectionInterval(JsonElement incident)
    {
        DateTimeOffset started = incident.GetProperty("collectionStartedAtUtc").GetDateTimeOffset();
        DateTimeOffset completed =
            incident.GetProperty("collectionCompletedAtUtc").GetDateTimeOffset();
        double elapsed = incident.GetProperty("collectionElapsedMilliseconds").GetDouble();
        Assert.IsTrue(completed >= started);
        Assert.IsTrue(double.IsFinite(elapsed));
        Assert.IsInRange(0, double.MaxValue, elapsed);
    }

    [TestMethod]
    public void OpenFutureSchemaRejectsWithoutLosingRunOrDomainState()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        string runId = database.RunId;
        string? configuration = database.ReadMetadata("configuration");
        using (SqliteConnection connection = database.Open())
        {
            // A future application owns this table. Older code must not recreate its state.
            Sql.Execute(
                connection,
                """
                CREATE TABLE future_delivery(id INTEGER PRIMARY KEY, commit_id TEXT NOT NULL);
                INSERT INTO future_delivery VALUES(17, 'retained-candidate');
                UPDATE meta SET value='999' WHERE key='schema_version';
                """
            );
        }

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new RunDatabase(fixture.StatePath)
        );

        Assert.Contains("999", error.Message);
        Assert.AreEqual("999", database.ReadMetadata("schema_version"));
        Assert.AreEqual(runId, database.RunId);
        Assert.AreEqual(configuration, database.ReadMetadata("configuration"));
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(
            "retained-candidate",
            Sql.Scalar(retained, "SELECT commit_id FROM future_delivery WHERE id=17;")
        );
        Assert.AreEqual(1L, Sql.Scalar(retained, "SELECT COUNT(*) FROM future_delivery;"));
    }

    [TestMethod]
    public void InitializeExistingRunRejectsReplacementAndPreservesIdentity()
    {
        using RunFixture fixture = new();
        RunDatabase original = fixture.CreateDatabase();
        string runId = original.RunId;
        RunConfiguration replacement = fixture.Configuration with
        {
            Goal = "An unrelated replacement goal must not destroy retained work.",
            WorkerCount = 4,
        };

        InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
            new RunDatabase(fixture.StatePath, replacement)
        );

        Assert.Contains("overwritten", error.Message);
        RunDatabase recovered = new(fixture.StatePath);
        Assert.AreEqual(runId, recovered.RunId);
        Assert.AreEqual(fixture.Configuration.Goal, recovered.Configuration.Goal);
        Assert.AreEqual(2, recovered.Configuration.WorkerCount);
        Assert.HasCount(3, recovered.Members);
    }

    [TestMethod]
    public void OpenMissingRunDoesNotCreateState()
    {
        using RunFixture fixture = new();

        FileNotFoundException error = Assert.ThrowsExactly<FileNotFoundException>(() =>
            new RunDatabase(fixture.StatePath)
        );

        Assert.AreEqual(Path.Combine(fixture.StatePath, "run.sqlite"), error.FileName);
        Assert.IsFalse(Directory.Exists(fixture.StatePath));
        Assert.IsTrue(Directory.Exists(fixture.RepositoryPath));
    }
}
