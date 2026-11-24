using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
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
        using Process ownProcess = Process.GetCurrentProcess();
        DateTimeOffset? ownBirth;
        try
        {
            ownBirth = new(ownProcess.StartTime.ToUniversalTime());
        }
        catch (Exception birthError) when (
            birthError is System.ComponentModel.Win32Exception or InvalidOperationException
        )
        {
            ownBirth = null;
        }
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
        long acquisitionStarted = Stopwatch.GetTimestamp();

        SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
            Sql.BeginTransaction(contender, operation: "CompetingWriter")
        );
        TimeSpan acquisitionElapsed = Stopwatch.GetElapsedTime(acquisitionStarted);
        DateTimeOffset after = DateTimeOffset.UtcNow;
        JsonObject incident = ReadIncident(error);
        JsonObject failure = incident["failure"]!.AsObject();
        JsonObject holder = Assert.ContainsSingle(
            Active(incident)
                .Where(activity => activity["transaction"]?.GetValue<string>() == held.Id)
        );
        JsonObject holderSource = Assert.ContainsSingle(
            incident["sources"]!.AsArray().OfType<JsonObject>()
                .Where(source =>
                    source["snapshot"]?["active"]?.AsArray().OfType<JsonObject>()
                        .Any(activity =>
                            activity["transaction"]?.GetValue<string>() == held.Id
                        ) == true
                )
        );
        Assert.AreEqual("available", holderSource["status"]!.GetValue<string>());
        foreach (JsonObject identity in new[] { incident, holderSource["snapshot"]!.AsObject() })
        {
            Assert.AreEqual(ownProcess.Id, identity["processId"]!.GetValue<int>());
            Assert.IsTrue(identity.ContainsKey("processBirthUtc"));
            if (ownBirth is { } expectedBirth)
            {
                Assert.AreEqual(
                    expectedBirth,
                    identity["processBirthUtc"]!.GetValue<DateTimeOffset>()
                );
            }
            else
            {
                Assert.IsNull(identity["processBirthUtc"]);
            }
        }

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
        double reportedElapsed = failure["elapsedMilliseconds"]!.GetValue<double>();
        Assert.IsGreaterThan(100d, reportedElapsed);
        Assert.IsTrue(reportedElapsed <= acquisitionElapsed.TotalMilliseconds);
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
    public void FailedParameterizedWriteAutomaticallyRetainsSanitizedStatementIncident()
    {
        using SqliteScenarioState state = new();
        using SqliteConnection writer = state.Open("owner");
        using SqliteConnection contender = state.Open("worker1");
        contender.DefaultTimeout = 1;
        using SqliteTransactionScope held = Sql.BeginTransaction(
            writer, operation: "HeldStatementWriter"
        );
        Sql.Execute(writer, "INSERT INTO entries VALUES('writer','uncommitted');", held);
        const string privateSql = "private-statement-marker";
        const string privateValue = "private-statement-value";

        SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
            Sql.Execute(
                contender,
                "INSERT INTO entries VALUES($id,$value); -- " + privateSql,
                null,
                ("$id", "contender"),
                ("$value", privateValue)
            )
        );
        JsonObject incident = ReadIncident(error);
        JsonObject failure = incident["failure"]!.AsObject();

        Assert.AreEqual(5, error.SqliteErrorCode);
        Assert.AreEqual(error.SqliteErrorCode, failure["primaryCode"]!.GetValue<int>());
        Assert.AreEqual(error.SqliteExtendedErrorCode, failure["extendedCode"]!.GetValue<int>());
        Assert.AreEqual("statement-failed", failure["phase"]!.GetValue<string>());
        Assert.AreEqual("Execute", failure["boundary"]!.GetValue<string>());
        Assert.AreEqual("Execute", failure["operation"]!.GetValue<string>());
        string retained = incident.ToJsonString() + string.Join(
            '\n',
            Directory.EnumerateFiles(state.DiagnosticsPath, "activity-*.json")
                .Select(File.ReadAllText)
        );
        Assert.DoesNotContain("INSERT INTO", retained);
        Assert.DoesNotContain(privateSql, retained);
        Assert.DoesNotContain(privateValue, retained);
        held.Rollback();
        Assert.AreEqual(0L, Sql.Scalar(contender, "SELECT COUNT(*) FROM entries;"));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TransactionCompletionRetiresAttributionBeforeScopeDisposal(
        bool commit
    )
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        string retired;
        SqliteException error;
        using (
            SqliteTransactionScope transaction = Sql.BeginTransaction(
                connection,
                operation: "RetiredWriter"
            )
        )
        {
            retired = transaction.Id;
            Sql.Execute(connection, "INSERT INTO entries VALUES('retired','value');", transaction);
            string activityPath = Assert.ContainsSingle(
                Directory.GetFiles(state.DiagnosticsPath, "activity-*.json")
            );
            JsonObject before = JsonNode.Parse(File.ReadAllText(activityPath))!.AsObject();
            Assert.AreEqual(
                retired,
                Assert.ContainsSingle(before["active"]!.AsArray())!["transaction"]!
                    .GetValue<string>()
            );
            if (commit)
            {
                transaction.Commit();
            }
            else
            {
                transaction.Rollback();
            }
            JsonObject after = JsonNode.Parse(File.ReadAllText(activityPath))!.AsObject();
            Assert.IsEmpty(after["active"]!.AsArray());
            Assert.Contains(
                commit ? "commit-succeeded" : "rollback-succeeded",
                after["recent"]!.AsArray()
                    .Where(activity =>
                        activity!["transaction"]?.GetValue<string>() == retired
                    )
                    .Select(activity => activity!["phase"]!.GetValue<string>())
            );
            error = ObserveIoFailure(connection, "AfterRetirement");
        }

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
        Assert.IsNull(failure["transaction"]);
        Assert.AreEqual("AfterRetirement", failure["operation"]!.GetValue<string>());
    }

    [TestMethod]
    public void RejectedCommitRetainsWriterAttributionUntilSuccessfulRollback()
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        using SqliteConnection observer = state.Open("observer");
        Sql.Execute(
            connection,
            """
            PRAGMA foreign_keys=ON;
            CREATE TABLE parents(id INTEGER PRIMARY KEY);
            CREATE TABLE children(
                parent_id INTEGER REFERENCES parents(id) DEFERRABLE INITIALLY DEFERRED);
            """
        );
        using SqliteTransactionScope transaction = Sql.BeginTransaction(
            connection, operation: "RejectedCommitWriter"
        );
        Sql.Execute(connection, "INSERT INTO children VALUES(1);", transaction);

        SqliteException rejected = Assert.ThrowsExactly<SqliteException>(transaction.Commit);
        Assert.AreEqual(19, rejected.SqliteErrorCode);
        JsonObject retained = ReadIncident(ObserveIoFailure(observer, "AfterRejectedCommit"));
        JsonObject writer = Assert.ContainsSingle(Active(retained));
        Assert.AreEqual(transaction.Id, writer["transaction"]!.GetValue<string>());
        JsonObject completion = Assert.ContainsSingle(
            Recent(retained).Where(activity =>
                activity["transaction"]?.GetValue<string>() == transaction.Id
                && activity["phase"]?.GetValue<string>() == "commit-failed"
            )
        );
        Assert.AreEqual(rejected.SqliteErrorCode, completion["primaryCode"]!.GetValue<int>());
        Assert.AreEqual(
            rejected.SqliteExtendedErrorCode, completion["extendedCode"]!.GetValue<int>()
        );

        transaction.Rollback();
        JsonObject retired = ReadIncident(ObserveIoFailure(observer, "AfterRejectedRollback"));
        Assert.IsEmpty(Active(retired));
        Assert.Contains(
            "rollback-succeeded",
            Recent(retired)
                .Where(activity => activity["transaction"]?.GetValue<string>() == transaction.Id)
                .Select(activity => activity["phase"]!.GetValue<string>())
        );
        Assert.AreEqual(0L, Sql.Scalar(observer, "SELECT COUNT(*) FROM children;"));
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
        List<Activity> activities = [];
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "LiteAgensh",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        string transactionId;
        SqliteException error;
        try
        {
            Console.SetError(stderr);
            using SqliteTransactionScope transaction = Sql.BeginTransaction(
                connection, operation: "StderrWriter"
            );
            transactionId = transaction.Id;
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
        JsonObject structuredSummary = JsonNode.Parse(summary[prefix.Length..])!.AsObject();
        JsonObject failure = structuredSummary["failure"]!.AsObject();
        Activity acquisition = Assert.ContainsSingle(
            activities.Where(activity => activity.OperationName == "sqlite.acquisition")
        );
        string databaseId = Assert.IsInstanceOfType<string>(
            acquisition.GetTagItem("liteagensh.sqlite.database_id")
        );
        string connectionId = Assert.IsInstanceOfType<string>(
            acquisition.GetTagItem("liteagensh.sqlite.connection_id")
        );
        Assert.IsNotEmpty(databaseId);
        Assert.IsNotEmpty(connectionId);
        Assert.AreEqual(databaseId, structuredSummary["database"]!.GetValue<string>());
        Assert.AreEqual(Environment.ProcessId, structuredSummary["processId"]!.GetValue<int>());
        Assert.AreEqual(connectionId, failure["connection"]!.GetValue<string>());
        Assert.AreEqual(transactionId, failure["transaction"]!.GetValue<string>());

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
        foreach (string text in SummaryStrings(structuredSummary))
        {
            Assert.DoesNotContain(secret, text);
            Assert.DoesNotContain(state.DiagnosticsPath, text);
        }
        Assert.AreEqual("StderrWriter", failure["operation"]!.GetValue<string>());
        Assert.AreEqual("UnknownOperation", failure["boundary"]!.GetValue<string>());
        Assert.IsNull(failure["actor"]);
        Assert.AreEqual("statement-failed", failure["phase"]!.GetValue<string>());
        Assert.AreEqual(10, failure["primaryCode"]!.GetValue<int>());
        Assert.AreEqual(522, failure["extendedCode"]!.GetValue<int>());
        Assert.AreEqual("owned-path-blocker", File.ReadAllText(state.DiagnosticsPath));
        Assert.AreEqual(0L, Sql.Scalar(connection, "SELECT COUNT(*) FROM entries;"));

        static IEnumerable<string> SummaryStrings(JsonNode? node) => node switch
        {
            JsonObject item => item.SelectMany(property => SummaryStrings(property.Value)),
            JsonArray items => items.SelectMany(SummaryStrings),
            JsonValue value when value.GetValueKind() == JsonValueKind.String =>
                [value.GetValue<string>()],
            _ => [],
        };
    }

    [TestMethod]
    public void FixtureReportingContinuesAfterIncompleteIncidentAndRetainsFailure()
    {
        using ScratchDirectory scratch = new();
        string incomplete = scratch.PathFor("incident-incomplete.json");
        string run = scratch.PathFor("incident-run.json");
        string campaign = scratch.PathFor("incident-campaign.json");
        File.WriteAllText(incomplete, "{\"version\":");
        File.WriteAllText(run, "{\"database\":\"run\"}");
        File.WriteAllText(campaign, "{\"database\":\"campaign\"}");
        string[] paths = [incomplete, run, campaign];
        using StringWriter output = new(CultureInfo.InvariantCulture);
        List<Exception> failures = [];

        SqliteIncidentOutput.ReportFiles(paths, output, failures);

        string[] reportedDatabases = output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
                JsonNode.Parse(line["PHASE5_SQLITE_INCIDENT ".Length..])!["database"]!
                    .GetValue<string>()
            )
            .ToArray();
        Assert.Contains("run", reportedDatabases);
        Assert.Contains("campaign", reportedDatabases);
        Assert.IsInstanceOfType<JsonException>(Assert.ContainsSingle(failures));
    }

    [TestMethod]
    [DataRow(10, 522)]
    [DataRow(6, 262)]
    public void RetainedIncidentExcludesSqlParametersMessagesAndInvalidActor(
        int primaryCode,
        int extendedCode
    )
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
        SqliteException original = new("Storage failure " + secret, primaryCode, extendedCode);
        TextWriter previous = Console.Error;
        using StringWriter stderr = new(CultureInfo.InvariantCulture);
        SqliteException error;
        try
        {
            Console.SetError(stderr);
            error = Assert.ThrowsExactly<SqliteException>(() =>
                SqliteDiagnostics.Observe<int>(
                    connection,
                    "Unsafe operation " + secret,
                    "statement",
                    () => throw original
                )
            );
        }
        finally
        {
            Console.SetError(previous);
        }
        Assert.AreSame(original, error);
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
        string output = stderr.ToString();
        Assert.Contains(
            primaryCode == 10
                ? "SQLite I/O evidence retained locally"
                : "SQLite contention evidence",
            output
        );
        Assert.DoesNotContain(secret, output);
        Assert.DoesNotContain("INSERT INTO", output);
        JsonObject failure = incident["failure"]!.AsObject();
        Assert.IsNull(failure["actor"]);
        Assert.AreEqual("UnknownOperation", failure["boundary"]!.GetValue<string>());
        Assert.AreEqual("statement-failed", failure["phase"]!.GetValue<string>());
        Assert.AreEqual(primaryCode, failure["primaryCode"]!.GetValue<int>());
        Assert.AreEqual(extendedCode, failure["extendedCode"]!.GetValue<int>());
        Assert.AreEqual(
            secret,
            Sql.Scalar(connection, "SELECT value FROM entries WHERE id='private';")
        );
    }

    [TestMethod]
    public void AggregateActivityCollectionRetainsUsableEvidenceAndReportsExhaustion()
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        using (
            SqliteTransactionScope seed = Sql.BeginTransaction(
                connection, operation: "AggregateSeed"
            )
        )
        {
            for (int index = 0; index < 56; index++)
            {
                SqliteDiagnostics.Observe(connection, "ReadEntry", "statement", () => 1);
            }

            seed.Commit();
        }

        string seedPath = Assert.ContainsSingle(
            Directory.EnumerateFiles(state.DiagnosticsPath, "activity-*.json")
        );
        JsonObject seedSnapshot = JsonNode.Parse(File.ReadAllText(seedPath))!.AsObject();
        int seedProcessId = seedSnapshot["processId"]!.GetValue<int>();
        List<string> stagedConnections = [];

        // The distinct near-full rings fit individually but exceed
        // the collection allowance together.
        IEnumerable<string> targetPaths = Enumerable
            .Range(0, 32)
            .Select(slot => Path.Combine(state.DiagnosticsPath, $"activity-{slot:D2}.json"))
            .Where(path => !File.Exists(path))
            .Take(16);
        foreach (string path in targetPaths)
        {
            int writer = stagedConnections.Count + 1;
            string actor = "worker" + writer.ToString(CultureInfo.InvariantCulture);
            string connectionId = Guid.NewGuid().ToString("N");
            string transactionId = Guid.NewGuid().ToString("N");
            JsonObject snapshot = seedSnapshot.DeepClone().AsObject();
            snapshot["processId"] = seedProcessId + writer;
            foreach (JsonObject activity in snapshot["recent"]!.AsArray().OfType<JsonObject>())
            {
                activity["connection"] = connectionId;
                activity["actor"] = actor;
                if (activity["transaction"] is not null)
                {
                    activity["transaction"] = transactionId;
                }
            }
            File.WriteAllText(path, snapshot.ToJsonString());
            stagedConnections.Add(connectionId);
        }

        SqliteException error = ObserveIoFailure(connection, "AggregateCoverage");
        JsonObject incident = ReadIncident(error);
        HashSet<string> retainedConnections = Recent(incident)
            .Select(activity => activity["connection"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        JsonObject[] exhausted = incident["sources"]!
            .AsArray()
            .OfType<JsonObject>()
            .Where(source => source["status"]!.GetValue<string>() == "collection-budget-exhausted")
            .ToArray();

        Assert.AreEqual("AggregateCoverage", incident["failure"]!["operation"]!.GetValue<string>());
        Assert.Contains(
            "Partial application observations",
            incident["coverage"]!.GetValue<string>()
        );
        Assert.IsNotEmpty(stagedConnections.Intersect(retainedConnections));
        Assert.IsNotEmpty(exhausted);
        foreach (JsonObject source in exhausted)
        {
            Assert.IsNull(source["snapshot"]);
            int slot = source["slot"]!.GetValue<int>();
            string path = Path.Combine(state.DiagnosticsPath, $"activity-{slot:D2}.json");
            JsonNode omitted = JsonNode.Parse(File.ReadAllText(path))!;
            Assert.DoesNotContain(
                omitted["recent"]![0]!["connection"]!.GetValue<string>(),
                retainedConnections
            );
        }
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
    [DataRow("malformed", "unavailable-or-malformed")]
    [DataRow("oversized", "truncated")]
    [DataRow("foreign", "malformed-or-foreign")]
    [DataRow("stale", "stale")]
    public void MalformedOversizedForeignOrStaleActivityIsExplicitlyQualified(
        string input,
        string expectedStatus
    )
    {
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        JsonObject? heldSnapshot = null;
        using (
            SqliteTransactionScope localWriter = Sql.BeginTransaction(
                connection, operation: "LocalCoverage"
            )
        )
        {
            if (input == "stale")
            {
                string path = Assert.ContainsSingle(
                    Directory.EnumerateFiles(state.DiagnosticsPath, "activity-*.json")
                );
                heldSnapshot = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
                Assert.IsTrue(heldSnapshot["active"]!.AsArray().Any(activity =>
                    activity?["transaction"]?.GetValue<string>() == localWriter.Id
                ));
                heldSnapshot["observedAtUtc"] = DateTimeOffset.UtcNow.AddDays(-1);
            }
            localWriter.Commit();
        }
        int sourceSlot = Enumerable.Range(0, 32).First(slot =>
            !File.Exists(Path.Combine(state.DiagnosticsPath, $"activity-{slot:D2}.json"))
        );
        string sourcePath = Path.Combine(state.DiagnosticsPath, $"activity-{sourceSlot:D2}.json");
        const string secret = "foreign-confidential-content";
        string? foreignTransaction = null;
        if (input == "stale")
        {
            Assert.IsNotNull(heldSnapshot);
            File.WriteAllText(sourcePath, heldSnapshot.ToJsonString());
        }
        else if (input == "foreign")
        {
            using SqliteScenarioState foreign = new();
            using SqliteConnection other = foreign.Open("foreign-worker");
            using SqliteTransactionScope foreignWriter = Sql.BeginTransaction(
                other, operation: "ForeignWriter"
            );
            foreignTransaction = foreignWriter.Id;
            string snapshot = Assert.ContainsSingle(
                Directory.EnumerateFiles(foreign.DiagnosticsPath, "activity-*.json")
            );
            string local = Assert.ContainsSingle(
                Directory.EnumerateFiles(state.DiagnosticsPath, "activity-*.json")
            );
            Assert.AreNotEqual(
                JsonNode.Parse(File.ReadAllText(local))!["database"]!.GetValue<string>(),
                JsonNode.Parse(File.ReadAllText(snapshot))!["database"]!.GetValue<string>()
            );
            File.Copy(snapshot, sourcePath);
        }
        else
        {
            File.WriteAllText(
                sourcePath,
                input == "oversized" ? new string('x', 128 * 1024 + 1) : secret
            );
        }

        SqliteException error = ObserveIoFailure(connection, "ForeignCoverage");
        JsonObject incident = ReadIncident(error);
        JsonObject source = Assert.ContainsSingle(
            incident["sources"]!
                .AsArray()
                .Select(node => node!.AsObject())
                .Where(item => item["slot"]!.GetValue<int>() == sourceSlot)
        );

        Assert.AreEqual(expectedStatus, source["status"]!.GetValue<string>());
        if (input != "stale")
        {
            Assert.IsNull(source["snapshot"]);
        }
        if (foreignTransaction is not null)
        {
            Assert.IsFalse(Active(incident).Any(activity =>
                activity["transaction"]?.GetValue<string>() == foreignTransaction
            ));
        }
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
