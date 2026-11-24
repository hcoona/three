using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
[DoNotParallelize]
public sealed class TelemetrySpecs(TestContext context)
{
    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public void OwnedFixtureEvidenceRetainsRealContentionWithoutUnrelatedOrPrivateActivity()
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        using ScratchDirectory results = new();
        using Phase7Evidence evidence = new(
            Guid.NewGuid().ToString("N"), "test-owner", results.PathFor("results"),
            report: message => context.WriteLine(message)
        );
        using Activity scope = evidence.BeginScope();
        evidence.Phase("storage-setup", "started");
        using SqliteScenarioState state = new();
        List<Activity> externallySampled = [];
        using ActivityListener external = Listen(externallySampled);
        const string secret = "private-goal-parameter-and-event";
        string unrelatedTrace;
        using (
            Activity unrelated = new Activity("unrelated")
                .SetParentId(
                    ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
                    ActivityTraceFlags.Recorded
                ).Start()
        )
        {
            unrelatedTrace = unrelated.TraceId.ToString();
            using Activity? sampled = ApplicationTelemetry.Source.StartActivity("coordinator.run");
            Assert.IsNotNull(sampled);
            sampled.SetTag("private.payload", secret);
        }
        string ownedSpan;
        using (Activity? owned = ApplicationTelemetry.Source.StartActivity("coordinator.run"))
        {
            Assert.IsNotNull(owned);
            ownedSpan = owned.SpanId.ToString();
            owned.SetTag("liteagensh.result.code", "redaction-witness");
            owned.SetTag("private.payload", secret);
            owned.AddEvent(new ActivityEvent(secret));
        }
        using SqliteConnection holder = state.Open();
        using SqliteTransactionScope transaction = Sql.BeginTransaction(
            holder, deferred: false, operation: "HeldWrite"
        );
        Sql.Execute(holder, "INSERT INTO entries VALUES('held',$value);", transaction,
            ("$value", secret));
        using SqliteConnection contender = state.Open("worker1");
        contender.DefaultTimeout = 1;

        SqliteException primary = Assert.ThrowsExactly<SqliteException>(
            () => Sql.BeginTransaction(contender, deferred: false, operation: "AcquireWriter")
        );
        string incidentPath = Assert.IsInstanceOfType<string>(
            primary.Data["LiteAgensh.SqliteIncident"]
        );
        JsonNode incident = JsonNode.Parse(File.ReadAllText(incidentPath))!;
        incident["privatePayload"] = secret;
        incident["warning"] = secret;
        File.WriteAllText(incidentPath, incident.ToJsonString());
        evidence.Phase("storage-setup", "failed", error: primary);
        evidence.CaptureSqlite([("run", state.DiagnosticsPath)]);
        evidence.Flush();

        string retained = ReadEvidence(evidence.FilePath);
        JsonNode[] records = retained.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!).ToArray();
        JsonNode capture = Assert.ContainsSingle(records.Where(record =>
            record["kind"]!.GetValue<string>() == "sqlite-source"
            && record["detail"]!["kind"]!.GetValue<string>() == "incident"
        ))["detail"]!;
        Assert.AreEqual("captured", capture["state"]!.GetValue<string>());
        Assert.AreEqual(5, primary.SqliteErrorCode);
        Assert.AreEqual(5, capture["data"]!["failure"]!["primaryCode"]!.GetValue<int>());
        Assert.AreEqual(
            incident["database"]!.GetValue<string>(),
            capture["data"]!["database"]!.GetValue<string>()
        );
        Assert.IsTrue(capture["warningPresent"]!.GetValue<bool>());
        Assert.IsTrue(capture["data"]!["sources"]!.AsArray().Any(source =>
            source?["snapshot"]?["active"]?.AsArray().Any(activity =>
                activity?["writeAcquisitionObserved"]?.GetValue<bool>() == true
            ) == true
        ));
        Assert.IsTrue(records.Any(record =>
            record["kind"]!.GetValue<string>() == "activity"
            && record["detail"]!["operation"]!.GetValue<string>() == "sqlite.acquisition"
            && record["detail"]!["state"]!.GetValue<string>() == "started"
        ));
        JsonNode retainedOwner = Assert.ContainsSingle(records.Where(record =>
            record["kind"]!.GetValue<string>() == "activity"
            && record["detail"]!["spanId"]!.GetValue<string>() == ownedSpan
            && record["detail"]!["state"]!.GetValue<string>() == "stopped"
        ))["detail"]!;
        Assert.AreEqual("coordinator.run", retainedOwner["operation"]!.GetValue<string>());
        Assert.AreEqual(
            "redaction-witness",
            retainedOwner["tags"]!["liteagensh.result.code"]!.GetValue<string>()
        );
        Assert.Contains(scope.TraceId.ToString(), retained);
        Assert.DoesNotContain(unrelatedTrace, retained);
        Assert.DoesNotContain(secret, retained);
        Assert.DoesNotContain("INSERT INTO", retained);
        Assert.DoesNotContain(state.DiagnosticsPath, retained);
    }

    [TestMethod]
    [DataRow(
        "not a JSON snapshot",
        "unavailable-or-malformed",
        DisplayName = "Malformed JSON source"
    )]
    [DataRow(
        """
        {"version":1,"database":"owned-run","kind":"run","processId":10001,
        "processBirthUtc":"2026-01-01T00:00:00+00:00",
        "observedAtUtc":"2026-01-01T00:00:00+00:00",
        "truncated":false,"active":null,"recent":[]}
        """,
        "unavailable",
        DisplayName = "Parsed source with valid identity but missing active observations"
    )]
    public void UnusableSqliteSourceDoesNotClaimAvailableObservations(
        string sourceText,
        string expectedState
    )
    {
        using ScratchDirectory results = new();
        string directory = results.PathFor("sqlite-sources");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "activity-00.json"), sourceText);
        using Phase7Evidence evidence = new(
            Guid.NewGuid().ToString("N"), "test-owner", results.PathFor("results"),
            report: message => context.WriteLine(message)
        );
        evidence.CaptureSqlite([("run", directory)]);
        evidence.Flush();

        JsonNode[] records = ReadEvidence(evidence.FilePath)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!).ToArray();
        JsonNode source = Assert.ContainsSingle(records.Where(record =>
            record["kind"]!.GetValue<string>() == "sqlite-source"
        ))["detail"]!;
        JsonNode coverage = Assert.ContainsSingle(records.Where(record =>
            record["kind"]!.GetValue<string>() == "sqlite-coverage"
        ))["detail"]!;
        Assert.AreEqual(expectedState, source["state"]!.GetValue<string>());
        Assert.AreEqual(0, coverage["availableFiles"]!.GetValue<int>());
        Assert.AreEqual(1, coverage["unavailableFiles"]!.GetValue<int>());
        Assert.AreEqual("unavailable", coverage["state"]!.GetValue<string>());
        Assert.AreEqual(
            "unknown",
            coverage["externalOrUninstrumentedHolder"]!.GetValue<string>()
        );
    }

    [TestMethod]
    public void ExportFailurePreservesTheOriginalPreCheckpointStorageError()
    {
        using ScratchDirectory results = new();
        string blocked = results.PathFor("not-a-directory");
        File.WriteAllText(blocked, "owned export obstruction");
        List<string> warnings = [];
        using Phase7Evidence evidence = new(
            Guid.NewGuid().ToString("N"), "test-owner", blocked, report: warnings.Add
        );
        using Activity scope = evidence.BeginScope();
        using SqliteScenarioState state = new();
        using SqliteConnection connection = state.Open();
        const string secret = "private-storage-error-message";
        SqliteException original = new(secret, 10, 522);
        evidence.Phase("storage-setup", "started");

        SqliteException observed = Assert.ThrowsExactly<SqliteException>(() =>
            SqliteDiagnostics.Observe<int>(
                connection, "PrepareState", "statement", () => throw original
            )
        );
        evidence.Phase("storage-setup", "failed", error: observed);
        evidence.Flush();

        Assert.AreSame(original, observed);
        Assert.AreEqual(10, observed.SqliteErrorCode);
        Assert.IsTrue(warnings.Any(line => line.StartsWith(
            "PHASE7_EVIDENCE_UNAVAILABLE", StringComparison.Ordinal
        )));
        Assert.DoesNotContain(secret, string.Join("\n", warnings));
        Assert.IsTrue(File.Exists(Assert.IsInstanceOfType<string>(
            observed.Data["LiteAgensh.SqliteIncident"]
        )));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task CreatorEvidenceSurvivesDeliberateInterruptionBeforeOwnershipScopeEnds()
    {
        Phase7OwnedCreator creator = await Phase7OwnedCreator.OpenAsync(
            context, false, context.CancellationToken
        );
        await creator.RunOwnedAsync(async () =>
        {
            Phase7Run state = Phase7Run.Attach(creator, context.CancellationToken);
            await state.RunBodyAsync(async () =>
            {
                await creator.CrashAsync(context.CancellationToken);
                string childFile = Path.Combine(
                    creator.Evidence.DirectoryPath,
                    $"original-creator-{creator.Hello["pid"]!.GetValue<int>()}-"
                    + $"{creator.Hello["birthTicks"]!.GetValue<long>()}.jsonl"
                );
                JsonNode[] records = ReadEvidence(childFile)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => JsonNode.Parse(line)!).ToArray();
                Assert.IsTrue(records.Any(record =>
                    record["kind"]!.GetValue<string>() == "phase"
                    && record["detail"]!["phase"]!.GetValue<string>() == "checkpoint"
                    && record["detail"]!["outcome"]!.GetValue<string>() == "prepared"
                ));
                Assert.IsTrue(records.Any(record =>
                    record["kind"]!.GetValue<string>() == "activity"
                    && record["detail"]!["operation"]!.GetValue<string>() == "coordinator.ownership"
                    && record["detail"]!["state"]!.GetValue<string>() == "live-snapshot"
                    && record["detail"]!["tags"]!["liteagensh.ownership.acquired"]?.GetValue<bool>()
                        == true
                ));
                Assert.IsFalse(records.Any(record =>
                    record["kind"]!.GetValue<string>() == "activity"
                    && record["detail"]!["operation"]!.GetValue<string>() == "coordinator.ownership"
                    && record["detail"]!["state"]!.GetValue<string>() == "stopped"
                    && record["detail"]!["tags"]!["liteagensh.ownership.acquired"]?.GetValue<bool>()
                        == true
                ));
                JsonNode[] parent = ReadEvidence(creator.Evidence.FilePath)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => JsonNode.Parse(line)!).ToArray();
                JsonNode joined = Assert.ContainsSingle(parent.Where(record =>
                    record["kind"]!.GetValue<string>() == "owned-process"
                    && record["detail"]!["phase"]!.GetValue<string>() == "creator-crash"
                    && record["detail"]!["outcome"]!.GetValue<string>() == "joined"
                ))["detail"]!;
                Assert.AreEqual(
                    creator.Hello["pid"]!.GetValue<int>(), joined["pid"]!.GetValue<int>()
                );
                Assert.AreEqual(
                    creator.Hello["birthTicks"]!.GetValue<long>(),
                    joined["birthTicks"]!.GetValue<long>()
                );
                Assert.AreNotEqual(0, joined["exitCode"]!.GetValue<int>());
            });
        });
    }

    [TestMethod]
    public void StorageFailureCorrelatesWithItsCallerWithoutExportingPrivateContent()
    {
        using SqliteScenarioState state = new();
        List<Activity> activities = [];
        using ActivityListener listener = Listen(activities);
        using Activity caller = new Activity("storage-caller").Start();
        const string secret = "private-goal-and-sql-parameter";
        using SqliteConnection connection = state.Open("worker1 " + secret);
        Sql.Execute(
            connection,
            "INSERT INTO entries VALUES($id,$value);",
            null,
            ("$id", "private"),
            ("$value", secret)
        );
        SqliteException original = new("disk error " + secret, 10, 522);

        SqliteException error = Assert.ThrowsExactly<SqliteException>(() =>
            SqliteDiagnostics.Observe<int>(
                connection, "Unsafe operation " + secret, "statement", () => throw original
            )
        );
        Activity failure = Assert.ContainsSingle(
            activities.Where(activity => activity.Status == ActivityStatusCode.Error)
        );
        JsonNode incident = JsonNode.Parse(
            File.ReadAllText(Assert.IsInstanceOfType<string>(
                error.Data["LiteAgensh.SqliteIncident"]
            ))
        )!;
        string exported = JsonSerializer.Serialize(activities.Select(activity => new
        {
            activity.OperationName,
            activity.StatusDescription,
            tags = activity.TagObjects,
            events = activity.Events.Select(item => new { item.Name, item.Tags }),
        }));

        Assert.AreSame(original, error);
        Assert.AreEqual(caller.TraceId, failure.TraceId);
        Assert.AreEqual(caller.SpanId, failure.ParentSpanId);
        Assert.AreEqual(
            incident["database"]!.GetValue<string>(),
            failure.GetTagItem("liteagensh.sqlite.database_id")
        );
        Assert.AreEqual(
            incident["failure"]!["connection"]!.GetValue<string>(),
            failure.GetTagItem("liteagensh.sqlite.connection_id")
        );
        Assert.AreEqual(10, failure.GetTagItem("liteagensh.sqlite.primary_code"));
        Assert.AreEqual(522, failure.GetTagItem("liteagensh.sqlite.extended_code"));
        Assert.DoesNotContain(secret, exported);
        Assert.DoesNotContain("INSERT INTO", exported);
        Assert.DoesNotContain(state.DiagnosticsPath, exported);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async Task RejectedRunRetainsItsOperationAndStorageTraceWithoutNativeDispatch()
    {
        using ScratchDirectory scratch = new();
        RunConfiguration configuration = new()
        {
            RepositoryPath = scratch.PathFor("checkout"),
            StatePath = scratch.PathFor("run"),
            CampaignLedgerPath = scratch.PathFor("campaign.sqlite"),
            Goal = "Reject an uninitialized run before native dispatch.",
        };
        RunDatabase database = new(configuration.StatePath, configuration);
        CollaborationOperations collaboration = new(database);
        Coordinator coordinator = new(database, collaboration, new(database, collaboration));
        List<Activity> activities = [];
        using ActivityListener listener = Listen(activities);
        using Activity caller = new Activity("run-caller").Start();

        JsonObject result = await coordinator.RunAsync("run", context.CancellationToken);

        Activity run = Assert.ContainsSingle(
            activities.Where(activity => activity.OperationName == "coordinator.run")
        );
        Activity ownership = Assert.ContainsSingle(
            activities.Where(activity => activity.OperationName == "coordinator.ownership")
        );
        Assert.IsFalse(result["ok"]!.GetValue<bool>());
        Assert.AreEqual(ActivityStatusCode.Error, run.Status);
        Assert.AreEqual(
            result["code"]!.GetValue<string>(), run.GetTagItem("liteagensh.result.code")
        );
        Assert.AreEqual(caller.SpanId, run.ParentSpanId);
        Assert.AreEqual(run.SpanId, ownership.ParentSpanId);
        Assert.AreEqual(true, ownership.GetTagItem("liteagensh.ownership.acquired"));
        Assert.IsTrue(activities.Any(activity =>
            activity.GetTagItem("db.system.name") as string == "sqlite"
            && activity.ParentSpanId == ownership.SpanId
            && activity.TraceId == run.TraceId
        ));
        Assert.AreSame(caller, Activity.Current);
    }

    [TestMethod]
    [Timeout(10_000, CooperativeCancellation = true)]
    public async Task OwnershipConflictRetainsSafeFailureCauseWithoutNativeDispatch()
    {
        using ScratchDirectory scratch = new();
        RunConfiguration configuration = new()
        {
            RepositoryPath = scratch.PathFor("checkout"),
            StatePath = scratch.PathFor("private-lock-path"),
            CampaignLedgerPath = scratch.PathFor("campaign.sqlite"),
            Goal = "Retain the ownership failure without exporting its private path.",
        };
        RunDatabase database = new(configuration.StatePath, configuration);
        CollaborationOperations collaboration = new(database);
        Coordinator coordinator = new(database, collaboration, new(database, collaboration));
        using FileStream ownership = new(
            Path.Combine(configuration.StatePath, "coordinator.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None
        );
        List<Activity> activities = [];
        using ActivityListener listener = Listen(activities);

        JsonObject result = await coordinator.RunAsync("run", context.CancellationToken);

        Activity run = Assert.ContainsSingle(
            activities.Where(activity => activity.OperationName == "coordinator.run")
        );
        Activity ownershipScope = Assert.ContainsSingle(
            activities.Where(activity => activity.OperationName == "coordinator.ownership")
        );
        ActivityEvent failure = Assert.ContainsSingle(
            run.Events.Where(item => item.Name == "coordinator.ownership.failed")
        );
        Dictionary<string, object?> tags = failure.Tags.ToDictionary(
            item => item.Key, item => item.Value
        );
        string exported = JsonSerializer.Serialize(activities.Select(activity => new
        {
            activity.StatusDescription,
            tags = activity.TagObjects,
            events = activity.Events.Select(item => new { item.Name, item.Tags }),
        }));
        Assert.AreEqual("conflict", result["code"]!.GetValue<string>());
        Assert.AreEqual(run.TraceId, ownershipScope.TraceId);
        Assert.AreEqual(run.SpanId, ownershipScope.ParentSpanId);
        Assert.AreEqual(false, ownershipScope.GetTagItem("liteagensh.ownership.acquired"));
        Assert.AreEqual(typeof(IOException).FullName, tags["error.type"]);
        Assert.IsInstanceOfType<int>(tags["liteagensh.ownership.hresult"]);
        Assert.DoesNotContain("private-lock-path", exported);
        Assert.DoesNotContain(configuration.StatePath, exported);
    }

    private static string ReadEvidence(string path)
    {
        using FileStream input = new(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite
        );
        using StreamReader reader = new(input);
        return reader.ReadToEnd();
    }

    private static ActivityListener Listen(List<Activity> activities)
    {
        ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "LiteAgensh",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
