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
        Assert.IsFalse(result["ok"]!.GetValue<bool>());
        Assert.AreEqual(ActivityStatusCode.Error, run.Status);
        Assert.AreEqual(
            result["code"]!.GetValue<string>(), run.GetTagItem("liteagensh.result.code")
        );
        Assert.AreEqual(caller.SpanId, run.ParentSpanId);
        Assert.IsTrue(activities.Any(activity =>
            activity.GetTagItem("db.system.name") as string == "sqlite"
            && activity.ParentSpanId == run.SpanId
            && activity.TraceId == run.TraceId
        ));
        Assert.AreSame(caller, Activity.Current);
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
