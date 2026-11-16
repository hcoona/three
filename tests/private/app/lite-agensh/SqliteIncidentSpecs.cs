using System.Globalization;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SqliteIncidentSpecs
{
    [TestMethod]
    public void HeldWriterCompetingAcquisitionRetainsCodesTimingAndAvailableHolder()
    {
        using ScratchDirectory scratch = new();
        RunConfiguration configuration = new()
        {
            RepositoryPath = scratch.PathFor("checkout"),
            StatePath = scratch.PathFor("run"),
            CampaignLedgerPath = scratch.PathFor("campaign.sqlite"),
            Goal = "Retain the real competing writer failure without changing storage policy.",
        };
        RunDatabase database = new(configuration.StatePath, configuration);
        using IDisposable owner = SqliteDiagnostics.UseActor("owner");
        using SqliteConnection writer = database.Open();
        using IDisposable worker = SqliteDiagnostics.UseActor("worker1");
        using SqliteConnection contender = database.Open();
        Sql.Execute(writer, "CREATE TABLE entries(id TEXT PRIMARY KEY, value TEXT NOT NULL);");
        using SqliteTransactionScope held = Sql.BeginTransaction(writer, operation: "HeldWriter");
        Sql.Execute(writer, "INSERT INTO entries VALUES('writer','uncommitted');", held);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
            Sql.BeginTransaction(contender, operation: "CompetingWriter")
        );
        DateTimeOffset after = DateTimeOffset.UtcNow;
        JsonObject incident = ReadIncident(error);
        JsonObject failure = incident["failure"]!.AsObject();
        JsonObject holder = Assert.ContainsSingle(
            Active(incident)
                .Where(activity => activity["transaction"]?.GetValue<string>() == held.Id)
        );

        Assert.AreEqual(5, writer.DefaultTimeout);
        Assert.AreEqual(5, contender.DefaultTimeout);
        Assert.AreEqual(5, error.SqliteErrorCode);
        Assert.AreEqual(5, error.SqliteExtendedErrorCode);
        Assert.AreEqual(5, failure["primaryCode"]!.GetValue<int>());
        Assert.AreEqual(5, failure["extendedCode"]!.GetValue<int>());
        Assert.AreEqual("CompetingWriter", failure["operation"]!.GetValue<string>());
        Assert.AreEqual("acquisition-failed", failure["phase"]!.GetValue<string>());
        Assert.AreEqual("worker1", failure["actor"]!.GetValue<string>());
        Assert.IsFalse(failure["writeAcquisitionObserved"]!.GetValue<bool>());
        Assert.AreEqual("HeldWriter", holder["operation"]!.GetValue<string>());
        Assert.AreEqual("owner", holder["actor"]!.GetValue<string>());
        Assert.IsTrue(holder["writeAcquisitionObserved"]!.GetValue<bool>());
        Assert.IsFalse(holder["deferred"]!.GetValue<bool>());
        Assert.AreNotEqual(
            failure["connection"]!.GetValue<string>(),
            holder["connection"]!.GetValue<string>()
        );
        Assert.IsTrue(failure["elapsedMilliseconds"]!.GetValue<double>() >= 0);
        Assert.IsTrue(failure["monotonicTimestamp"]!.GetValue<long>() > 0);
        DateTimeOffset observed = failure["observedAtUtc"]!.GetValue<DateTimeOffset>();
        DateTimeOffset collectionStart = incident[
            "collectionStartedAtUtc"
        ]!.GetValue<DateTimeOffset>();
        DateTimeOffset collectionEnd = incident[
            "collectionCompletedAtUtc"
        ]!.GetValue<DateTimeOffset>();
        Assert.IsTrue(before <= observed && observed <= after);
        Assert.IsTrue(
            observed <= collectionStart
                && collectionStart <= collectionEnd
                && collectionEnd <= after
        );
        Assert.IsTrue(incident["collectionElapsedMilliseconds"]!.GetValue<double>() >= 0);
        Assert.Contains("non-atomic", incident["coverage"]!.GetValue<string>());
        Assert.Contains(
            "uninstrumented holders remain unknown",
            incident["coverage"]!.GetValue<string>()
        );

        held.Rollback();
        Assert.AreEqual(0L, Sql.Scalar(contender, "SELECT COUNT(*) FROM entries;"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TransactionCompletedAndDisposedDoesNotAttributeLaterFailureToRetiredWork(
        bool commit
    )
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        string retired;
        using (
            SqliteTransactionScope transaction = Sql.BeginTransaction(
                connection,
                operation: "RetiredWriter"
            )
        )
        {
            retired = transaction.Id;
            Sql.Execute(connection, "INSERT INTO entries VALUES('retired','value');", transaction);
            if (commit)
            {
                transaction.Commit();
            }
            else
            {
                transaction.Rollback();
            }
        }

        SqliteException error = ObserveIoFailure(connection, "AfterRetirement");
        JsonObject incident = ReadIncident(error);
        JsonObject failure = incident["failure"]!.AsObject();

        Assert.IsEmpty(Active(incident));
        Assert.Contains(
            commit ? "commit-succeeded" : "rollback-succeeded",
            Recent(incident)
                .Where(activity => activity["transaction"]?.GetValue<string>() == retired)
                .Select(activity => activity["phase"]!.GetValue<string>())
        );
        Assert.AreEqual(commit ? 1L : 0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM entries;"));
        // Outside a completed/disposed transaction, a new operation must not inherit its identity.
        Assert.IsNull(failure["transaction"]);
        Assert.AreEqual("AfterRetirement", failure["operation"]!.GetValue<string>());
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public void CompletedScopeDisposalPreservesNewTransactionOwnership(bool commit, bool deferred)
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        using SqliteConnection observer = state.Open("observer");
        using SqliteTransactionScope retired = Sql.BeginTransaction(
            connection,
            operation: "RetiredWriter"
        );
        Sql.Execute(connection, "INSERT INTO entries VALUES('retired','value');", retired);
        if (commit)
        {
            retired.Commit();
        }
        else
        {
            retired.Rollback();
        }

        using SqliteTransactionScope current = Sql.BeginTransaction(
            connection,
            deferred,
            operation: "CurrentWriter"
        );
        Sql.Execute(connection, "INSERT INTO entries VALUES('current','value');", current);
        retired.Dispose();

        JsonObject incident = ReadIncident(ObserveIoFailure(observer, "ObserveCurrent"));
        JsonObject holder = Assert.ContainsSingle(Active(incident));

        Assert.AreEqual(current.Id, holder["transaction"]!.GetValue<string>());
        Assert.AreEqual("CurrentWriter", holder["operation"]!.GetValue<string>());
        Assert.AreEqual(deferred, holder["deferred"]!.GetValue<bool>());
        Assert.AreEqual(!deferred, holder["writeAcquisitionObserved"]!.GetValue<bool>());
        Assert.Contains(
            commit ? "commit-succeeded" : "rollback-succeeded",
            Recent(incident)
                .Where(activity => activity["transaction"]?.GetValue<string>() == retired.Id)
                .Select(activity => activity["phase"]!.GetValue<string>())
        );
        Assert.Contains(
            "transaction-dispose-succeeded",
            Recent(incident)
                .Where(activity => activity["transaction"]?.GetValue<string>() == retired.Id)
                .Select(activity => activity["phase"]!.GetValue<string>())
        );

        JsonObject failure = ReadIncident(ObserveIoFailure(connection, "WhileCurrent"))[
            "failure"
        ]!.AsObject();
        Assert.AreEqual(current.Id, failure["transaction"]!.GetValue<string>());
        Assert.AreEqual("CurrentWriter", failure["operation"]!.GetValue<string>());
        Assert.AreEqual(deferred, failure["deferred"]!.GetValue<bool>());

        current.Rollback();
        JsonObject after = ReadIncident(ObserveIoFailure(connection, "AfterCurrent"));
        Assert.IsEmpty(Active(after));
        Assert.IsNull(after["failure"]!["transaction"]);
        Assert.AreEqual("AfterCurrent", after["failure"]!["operation"]!.GetValue<string>());
        Assert.AreEqual(commit ? 1L : 0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM entries;"));
    }

    [TestMethod]
    public void AutomaticRollbackDisposalRetiresOwnershipAndPreservesHistory()
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        string retired;
        using (
            SqliteTransactionScope transaction = Sql.BeginTransaction(
                connection,
                operation: "AutomaticRollback"
            )
        )
        {
            retired = transaction.Id;
            Sql.Execute(connection, "INSERT INTO entries VALUES('retired','value');", transaction);
        }

        JsonObject incident = ReadIncident(ObserveIoFailure(connection, "AfterAutomaticRollback"));

        Assert.IsEmpty(Active(incident));
        Assert.IsNull(incident["failure"]!["transaction"]);
        Assert.AreEqual(
            "AfterAutomaticRollback",
            incident["failure"]!["operation"]!.GetValue<string>()
        );
        Assert.Contains(
            "rollback-succeeded",
            Recent(incident)
                .Where(activity => activity["transaction"]?.GetValue<string>() == retired)
                .Select(activity => activity["phase"]!.GetValue<string>())
        );
        Assert.Contains(
            "transaction-dispose-succeeded",
            Recent(incident)
                .Where(activity => activity["transaction"]?.GetValue<string>() == retired)
                .Select(activity => activity["phase"]!.GetValue<string>())
        );
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM entries;"));
    }

    [TestMethod]
    public void IdleReadHistoryIsCapturedOnFailureWithoutRewritingRetiredOwnership()
    {
        using SqliteScenarioState state = new();
        using (SqliteConnection writer = state.Open())
        {
            using SqliteTransactionScope transaction = Sql.BeginTransaction(
                writer,
                operation: "InitialWriter"
            );
            Sql.Execute(writer, "INSERT INTO entries VALUES('initial','value');", transaction);
            transaction.Commit();
        }
        string activity = Assert.ContainsSingle(
            Directory.GetFiles(state.DiagnosticsPath, "activity-*.json")
        );
        byte[] retained = File.ReadAllBytes(activity);

        using (SqliteConnection reader = state.Open("worker2"))
        {
            Assert.AreEqual(1L, Sql.Scalar(reader, "SELECT COUNT(*) FROM entries;"));
        }

        Assert.IsTrue(
            retained.AsSpan().SequenceEqual(File.ReadAllBytes(activity)),
            "An unleased idle observation must not rewrite the retired ownership snapshot."
        );
        using SqliteConnection observer = state.Open();
        JsonObject incident = ReadIncident(ObserveIoFailure(observer, "AfterIdleRead"));
        Assert.IsEmpty(Active(incident));
        Assert.IsTrue(
            Recent(incident)
                .Any(record =>
                    record["actor"]?.GetValue<string>() == "worker2"
                    && record["boundary"]?.GetValue<string>() == "Scalar"
                    && record["phase"]?.GetValue<string>() == "statement-succeeded"
                )
        );
        Assert.IsTrue(
            Recent(incident)
                .Any(record =>
                    record["actor"]?.GetValue<string>() == "worker2"
                    && record["phase"]?.GetValue<string>() == "closed"
                )
        );
    }

    [TestMethod]
    public void ConnectionCloseWithOwnedTransactionRemovesHolderAndRollsBackItsWrite()
    {
        using SqliteScenarioState state = new();
        using SqliteConnection writer = state.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(
            writer,
            operation: "ClosingWriter"
        );
        Sql.Execute(writer, "INSERT INTO entries VALUES('closed','uncommitted');", transaction);
        string retired = transaction.Id;

        writer.Close();
        using SqliteConnection observer = state.Open("worker2");
        SqliteException error = ObserveIoFailure(observer, "AfterClose");
        JsonObject incident = ReadIncident(error);

        Assert.IsEmpty(Active(incident));
        Assert.Contains(
            "closed",
            Recent(incident)
                .Where(activity => activity["transaction"]?.GetValue<string>() == retired)
                .Select(activity => activity["phase"]!.GetValue<string>())
        );
        Assert.AreEqual("AfterClose", incident["failure"]!["operation"]!.GetValue<string>());
        Assert.AreEqual(0L, Sql.Scalar(observer, "SELECT COUNT(*) FROM entries;"));
    }

    [TestMethod]
    public void UninstrumentedWriterRemainsUnknownInApplicationCoverage()
    {
        using SqliteScenarioState state = new();
        using SqliteConnection unobserved = new(state.ConnectionString);
        unobserved.Open();
        using SqliteTransaction writer = unobserved.BeginTransaction();
        using (SqliteCommand command = unobserved.CreateCommand())
        {
            command.Transaction = writer;
            command.CommandText = "INSERT INTO entries VALUES('external','uncommitted');";
            command.ExecuteNonQuery();
        }

        using SqliteConnection observer = state.Open();
        // Inject only the failure notification: this case tests coverage, not another busy wait.
        SqliteException error = ObserveIoFailure(observer, "MissingCoverage");
        JsonObject incident = ReadIncident(error);

        Assert.IsEmpty(Active(incident));
        Assert.AreEqual(
            (
                "Partial application observations; non-atomic; external or "
                + "uninstrumented holders remain unknown."
            ),
            incident["coverage"]!.GetValue<string>()
        );
        Assert.AreEqual("MissingCoverage", incident["failure"]!["operation"]!.GetValue<string>());
        writer.Rollback();
        Assert.AreEqual(0L, Sql.Scalar(observer, "SELECT COUNT(*) FROM entries;"));
    }

    [TestMethod]
    public void DiagnosticIoFailurePreservesOriginalExceptionAndSanitizedStderrSummary()
    {
        using SqliteScenarioState state = new();
        // A regular owned file at the directory path makes diagnostic persistence fail.
        File.WriteAllText(state.DiagnosticsPath, "owned-path-blocker");
        const string secret = "password=private-goal-and-sql-parameters";
        using SqliteConnection connection = state.Open(secret);
        SqliteException original = new(
            secret + " " + state.DiagnosticsPath,
            errorCode: 10,
            extendedErrorCode: 522
        );
        original.Data["origin"] = "original-operation";
        TextWriter previous = Console.Error;
        using StringWriter stderr = new(CultureInfo.InvariantCulture);
        SqliteException error;
        try
        {
            Console.SetError(stderr);
            error = Assert.ThrowsExactly<SqliteException>(() =>
                SqliteDiagnostics.Observe<int>(
                    connection,
                    "SELECT " + secret,
                    "statement",
                    () => throw original
                )
            );
        }
        finally
        {
            Console.SetError(previous);
        }

        string output = stderr.ToString();
        const string prefix = "SQLite failure summary: ";
        string summary = Assert.ContainsSingle(
            output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
        );
        JsonObject failure = JsonNode.Parse(summary[prefix.Length..])!["failure"]!.AsObject();

        Assert.AreSame(original, error);
        Assert.AreEqual(10, error.SqliteErrorCode);
        Assert.AreEqual(522, error.SqliteExtendedErrorCode);
        Assert.AreEqual("original-operation", error.Data["origin"]);
        Assert.Contains(secret, error.Message);
        Assert.IsNull(error.Data["LiteAgensh.SqliteIncident"]);
        Assert.AreEqual(
            (
                "SQLite incident persistence failed or capacity exhausted; original "
                + "database error preserved."
            ),
            error.Data["LiteAgensh.SqliteDiagnosticWarning"]
        );
        Assert.Contains("SQLite activity persistence is unavailable or incomplete.", output);
        Assert.Contains("original database error preserved.", output);
        Assert.DoesNotContain(secret, output);
        Assert.DoesNotContain(state.DiagnosticsPath, output);
        Assert.AreEqual("UnknownOperation", failure["operation"]!.GetValue<string>());
        Assert.IsNull(failure["actor"]);
        Assert.AreEqual("statement-failed", failure["phase"]!.GetValue<string>());
        Assert.AreEqual(10, failure["primaryCode"]!.GetValue<int>());
        Assert.AreEqual(522, failure["extendedCode"]!.GetValue<int>());
        Assert.AreEqual("owned-path-blocker", File.ReadAllText(state.DiagnosticsPath));
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM entries;"));
    }

    [TestMethod]
    public void RetainedIncidentExcludesSqlParametersMessagesAndInvalidActor()
    {
        using SqliteScenarioState state = new();
        const string secret = "confidential-goal-token";
        using SqliteConnection connection = state.Open("worker1 " + secret);
        Sql.Execute(
            connection,
            "INSERT INTO entries VALUES($id,$value);",
            null,
            ("$id", "private"),
            ("$value", secret)
        );
        SqliteException error = ObserveIoFailure(
            connection,
            "Unsafe operation " + secret,
            "disk error " + secret
        );
        string path = Assert.IsInstanceOfType<string>(error.Data["LiteAgensh.SqliteIncident"]);
        string artifact = File.ReadAllText(path);
        JsonObject incident = JsonNode.Parse(artifact)!.AsObject();
        string activity = string.Join(
            '\n',
            Directory
                .EnumerateFiles(state.DiagnosticsPath, "activity-*.json")
                .Select(File.ReadAllText)
        );

        Assert.DoesNotContain(secret, artifact);
        Assert.DoesNotContain(secret, activity);
        Assert.DoesNotContain("INSERT INTO", artifact);
        Assert.DoesNotContain("INSERT INTO", activity);
        Assert.IsNull(incident["failure"]!["actor"]);
        Assert.AreEqual("UnknownOperation", incident["failure"]!["boundary"]!.GetValue<string>());
        Assert.AreEqual(522, incident["failure"]!["extendedCode"]!.GetValue<int>());
        Assert.AreEqual(
            secret,
            Sql.Scalar(connection, "SELECT value FROM entries WHERE id='private';")
        );
    }

    [TestMethod]
    public void IncidentCapacityRetainsEightDistinctArtifactsWithoutOverwriteAndReportsOverflow()
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        Dictionary<string, string> originals = [];
        for (int index = 0; index < 8; index++)
        {
            string operation = "Incident" + index.ToString("D2", CultureInfo.InvariantCulture);
            SqliteException error = ObserveIoFailure(connection, operation);
            string path = Assert.IsInstanceOfType<string>(error.Data["LiteAgensh.SqliteIncident"]);
            string content = File.ReadAllText(path);
            Assert.AreEqual(
                operation,
                JsonNode.Parse(content)!["failure"]!["operation"]!.GetValue<string>()
            );
            Assert.IsFalse(originals.ContainsKey(path));
            originals.Add(path, content);
            Assert.IsTrue(new FileInfo(path).Length <= 512 * 1024);
        }

        SqliteException overflow = ObserveIoFailure(connection, "IncidentOverflow");

        Assert.IsNull(overflow.Data["LiteAgensh.SqliteIncident"]);
        Assert.AreEqual(10, overflow.SqliteErrorCode);
        Assert.AreEqual(522, overflow.SqliteExtendedErrorCode);
        Assert.AreEqual(
            (
                "SQLite incident persistence failed or capacity exhausted; original "
                + "database error preserved."
            ),
            overflow.Data["LiteAgensh.SqliteDiagnosticWarning"]
        );
        Assert.HasCount(
            8,
            Directory.EnumerateFiles(state.DiagnosticsPath, "incident-*.json").ToArray()
        );
        foreach ((string path, string content) in originals)
        {
            Assert.AreEqual(content, File.ReadAllText(path));
        }

        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM entries;"));
    }

    [TestMethod]
    public void ActivityCapacityReportsLostCoverageAndKeepsLatestFailureInsteadOfOldestProbe()
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        SqliteDiagnostics.Observe(
            connection,
            "OldestProbe",
            "statement",
            () => Sql.Scalar(connection, "SELECT COUNT(*) FROM entries;")
        );
        for (int index = 0; index < 70; index++)
        {
            Sql.Scalar(connection, "SELECT COUNT(*) FROM entries;");
        }

        SqliteException error = ObserveIoFailure(connection, "LatestProbe");
        JsonObject incident = ReadIncident(error);
        JsonObject snapshot = Assert.ContainsSingle(
            incident["sources"]!
                .AsArray()
                .Where(source => source!["status"]!.GetValue<string>() == "available")
                .Select(source => source!["snapshot"]!.AsObject())
        );
        JsonArray recent = snapshot["recent"]!.AsArray();

        Assert.IsTrue(snapshot["truncated"]!.GetValue<bool>());
        Assert.HasCount(128, recent);
        Assert.DoesNotContain(
            "OldestProbe",
            recent.Select(item => item!["boundary"]!.GetValue<string>())
        );
        Assert.Contains(
            "LatestProbe",
            recent.Select(item => item!["boundary"]!.GetValue<string>())
        );
        Assert.AreEqual("statement-failed", recent[^1]!["phase"]!.GetValue<string>());
        Assert.AreEqual(10, recent[^1]!["primaryCode"]!.GetValue<int>());
        Assert.IsTrue(
            Directory
                .EnumerateFiles(state.DiagnosticsPath, "activity-*.json")
                .All(path => new FileInfo(path).Length <= 128 * 1024)
        );
        Assert.Contains(
            "Partial application observations",
            incident["coverage"]!.GetValue<string>()
        );
    }

    [TestMethod]
    [DataRow(false, "unavailable-or-malformed")]
    [DataRow(true, "truncated")]
    public void ForeignActivityMalformedOrOversizedRemainsExplicitlyUnavailable(
        bool oversized,
        string expectedStatus
    )
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        Directory.CreateDirectory(state.DiagnosticsPath);
        const string secret = "foreign-confidential-content";
        File.WriteAllText(
            Path.Combine(state.DiagnosticsPath, "activity-31.json"),
            oversized ? new string('x', 128 * 1024 + 1) : secret
        );

        SqliteException error = ObserveIoFailure(connection, "ForeignCoverage");
        JsonObject incident = ReadIncident(error);
        JsonObject source = Assert.ContainsSingle(
            incident["sources"]!
                .AsArray()
                .Select(node => node!.AsObject())
                .Where(item => item["slot"]!.GetValue<int>() == 31)
        );

        Assert.AreEqual(expectedStatus, source["status"]!.GetValue<string>());
        Assert.IsNull(source["snapshot"]);
        Assert.Contains("remain unknown", incident["coverage"]!.GetValue<string>());
        Assert.DoesNotContain(secret, incident.ToJsonString());
        Assert.AreEqual("ForeignCoverage", incident["failure"]!["operation"]!.GetValue<string>());
    }

    private static SqliteException ObserveIoFailure(
        SqliteConnection connection,
        string operation,
        string message = "Controlled I/O failure."
    )
    {
        SqliteException original = new(message, errorCode: 10, extendedErrorCode: 522);
        SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
            SqliteDiagnostics.Observe<int>(connection, operation, "statement", () => throw original)
        );
        Assert.AreSame(original, error);
        return error;
    }

    private static JsonObject ReadIncident(SqliteException error)
    {
        string path = Assert.IsInstanceOfType<string>(error.Data["LiteAgensh.SqliteIncident"]);
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static IEnumerable<JsonObject> Active(JsonObject incident) =>
        incident["sources"]!
            .AsArray()
            .Where(source => source!["snapshot"] is not null)
            .SelectMany(source => source!["snapshot"]!["active"]!.AsArray())
            .Select(activity => activity!.AsObject());

    private static IEnumerable<JsonObject> Recent(JsonObject incident) =>
        incident["sources"]!
            .AsArray()
            .Where(source => source!["snapshot"] is not null)
            .SelectMany(source => source!["snapshot"]!["recent"]!.AsArray())
            .Select(activity => activity!.AsObject());
}
