using System.Text.Json;
using System.Text.Json.Nodes;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class SerializedToolSpecs(TestContext context)
{
    [TestMethod]
    [TestCategory("AllSourceToolsBoundAndTruthful")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AllSourceToolsBoundAndTruthful()
    {
        CancellationToken token = context.CancellationToken;
        using GitScenarioState state = await GitScenarioState
            .CreateAsync(
                token,
                integrationChecks: [GitScenarioState.ExitCheck(0)],
                configuredCheck: (_, _, _, _, checkToken) =>
                {
                    checkToken.ThrowIfCancellationRequested();
                    return Task.FromResult(new ProcessResult(0, "", "", false));
                }
            )
            .ConfigureAwait(false);
        await state.Operations.InitializeAsync(token).ConfigureAwait(false);
        await SerializedToolHost
            .RunAsync(
                state.Database,
                "owner",
                async owner =>
                    await SerializedToolHost
                        .RunAsync(
                            state.Database,
                            "worker1",
                            async first =>
                                await SerializedToolHost
                                    .RunAsync(
                                        state.Database,
                                        "worker2",
                                        async second =>
                                        {
                                            HashSet<string> exercised = new(StringComparer.Ordinal);
                                            async Task<JsonObject> Call(
                                                SerializedToolHost host,
                                                string name,
                                                JsonObject arguments
                                            )
                                            {
                                                JsonObject data = await host.DataAsync(
                                                        name,
                                                        arguments,
                                                        token
                                                    )
                                                    .ConfigureAwait(false);
                                                exercised.Add(name);
                                                return data;
                                            }
                                            var catalog = await owner
                                                .Client.ListToolsAsync(cancellationToken: token)
                                                .ConfigureAwait(false);
                                            Assert.HasCount(ToolCatalog.Definitions.Count, catalog);
                                            foreach (var definition in ToolCatalog.Definitions)
                                            {
                                                var wire = Assert.ContainsSingle(
                                                    catalog.Where(tool =>
                                                        tool.Name == definition.Name
                                                    )
                                                );
                                                Assert.AreEqual(
                                                    definition.Description,
                                                    wire.Description
                                                );
                                            }
                                            var searchTool = Assert.ContainsSingle(
                                                catalog.Where(tool => tool.Name == "work_search")
                                            );
                                            AssertWorkSearchInputContract(searchTool.JsonSchema);
                                            JsonObject run = await Call(owner, "run_info", new())
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                state.Database.Configuration.Goal,
                                                Text(run, "goal")
                                            );
                                            Assert.AreEqual(
                                                state.Repository,
                                                Text(run, "repositoryPath")
                                            );
                                            Assert.AreEqual(
                                                state.BaseCommit,
                                                Text(run, "baseCommit")
                                            );
                                            Assert.AreEqual(
                                                state.Database.StatePath,
                                                Text(run, "statePath")
                                            );
                                            JsonObject self = await Call(
                                                    first,
                                                    "agent_info",
                                                    new() { ["id"] = "worker1" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("worker1", Text(self, "id"));
                                            JsonObject member = await Call(
                                                    first,
                                                    "agent_info",
                                                    new() { ["id"] = "worker2" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("worker2", Text(member, "id"));
                                            Assert.AreEqual(
                                                state.Operations.GetWorkerPath("worker2"),
                                                Text(member, "worktreePath")
                                            );
                                            JsonObject agents = await Call(
                                                    second,
                                                    "agent_list",
                                                    new() { ["id"] = "worker1" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                "worker1",
                                                Text(Assert.ContainsSingle(Items(agents)), "id")
                                            );

                                            JsonObject task = await Call(
                                                    first,
                                                    "task_write",
                                                    new()
                                                    {
                                                        ["action"] = "create",
                                                        ["title"] = "Wire task",
                                                        ["body"] = "Retain the original body.",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            long taskId = Id(task);
                                            Assert.AreEqual("worker1", Text(task, "author"));
                                            JsonObject updated = await Call(
                                                    first,
                                                    "task_write",
                                                    new()
                                                    {
                                                        ["action"] = "update",
                                                        ["id"] = taskId,
                                                        ["title"] = "Wire task revised",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                "Retain the original body.",
                                                Text(updated, "body")
                                            );
                                            await Call(
                                                    second,
                                                    "task_write",
                                                    new()
                                                    {
                                                        ["action"] = "comment",
                                                        ["id"] = taskId,
                                                        ["body"] = "Peer reviewed the task.",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            JsonObject read = await Call(
                                                    owner,
                                                    "task_read",
                                                    new() { ["id"] = taskId }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                "Wire task revised",
                                                Text(read, "title")
                                            );
                                            JsonObject comment = Assert.ContainsSingle(
                                                read["comments"]!.AsArray().OfType<JsonObject>()
                                            );
                                            Assert.AreEqual("worker2", Text(comment, "actor"));
                                            Assert.AreEqual(
                                                "Peer reviewed the task.",
                                                Text(comment, "body")
                                            );
                                            JsonObject closed = await Call(
                                                    first,
                                                    "task_write",
                                                    new()
                                                    {
                                                        ["action"] = "close",
                                                        ["id"] = taskId,
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("closed", Text(closed, "state"));
                                            Assert.AreEqual("worker1", Text(closed, "closedBy"));
                                            JsonObject tasks = await Call(
                                                    owner,
                                                    "task_list",
                                                    new() { ["state"] = "closed" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                taskId,
                                                Id(Assert.ContainsSingle(Items(tasks)))
                                            );
                                            JsonObject openTasks = await Call(
                                                    owner,
                                                    "task_list",
                                                    new() { ["state"] = "open" }
                                                )
                                                .ConfigureAwait(false);
                                            JsonObject rootTask = Assert.ContainsSingle(
                                                Items(openTasks)
                                            );
                                            Assert.AreEqual(1L, Id(rootTask));
                                            Assert.AreEqual("open", Text(rootTask, "state"));
                                            Assert.IsNull(rootTask["closedBy"]);

                                            JsonObject root = await Call(
                                                    first,
                                                    "message_send",
                                                    new()
                                                    {
                                                        ["recipient"] = "team",
                                                        ["body"] = "wire team finding",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            JsonObject direct = await Call(
                                                    first,
                                                    "message_send",
                                                    new()
                                                    {
                                                        ["recipient"] = "worker2",
                                                        ["body"] = "wire private finding",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            JsonObject reply = await Call(
                                                    second,
                                                    "message_send",
                                                    new()
                                                    {
                                                        ["recipient"] = "team",
                                                        ["threadId"] = Id(root),
                                                        ["body"] = "wire peer reply",
                                                        ["mentions"] = new JsonArray("worker1"),
                                                        ["urgent"] = true,
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("worker2", Text(reply, "actor"));
                                            Assert.AreEqual(
                                                Id(root),
                                                reply["threadId"]!.GetValue<long>()
                                            );
                                            Assert.IsTrue(reply["urgent"]!.GetValue<bool>());
                                            Assert.Contains(
                                                "worker1",
                                                reply["mentions"]!
                                                    .AsArray()
                                                    .Select(value => value!.GetValue<string>())
                                            );
                                            JsonObject history = await Call(
                                                    second,
                                                    "message_read",
                                                    new()
                                                    {
                                                        ["channelId"] = Text(direct, "channelId"),
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                "wire private finding",
                                                Text(Assert.ContainsSingle(Items(history)), "body")
                                            );
                                            JsonObject thread = await Call(
                                                    first,
                                                    "message_thread",
                                                    new() { ["threadId"] = Id(root) }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.HasCount(2, Items(thread));
                                            Assert.AreEqual(Id(reply), Id(Items(thread)[1]));
                                            JsonObject search = await Call(
                                                    second,
                                                    "message_search",
                                                    new() { ["query"] = "private finding" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                Id(direct),
                                                Id(Assert.ContainsSingle(Items(search)))
                                            );
                                            JsonObject channel = await Call(
                                                    second,
                                                    "channel_read",
                                                    new() { ["id"] = Text(direct, "channelId") }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("direct", Text(channel, "kind"));
                                            Assert.HasCount(2, channel["members"]!.AsArray());
                                            Assert.Contains(
                                                "worker1",
                                                channel["members"]!
                                                    .AsArray()
                                                    .Select(value => value!.GetValue<string>())
                                            );
                                            Assert.Contains(
                                                "worker2",
                                                channel["members"]!
                                                    .AsArray()
                                                    .Select(value => value!.GetValue<string>())
                                            );
                                            JsonObject team = await Call(
                                                    second,
                                                    "channel_read",
                                                    new() { ["id"] = "team" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("team", Text(team, "id"));
                                            Assert.AreEqual("team", Text(team, "kind"));
                                            JsonObject channels = await Call(
                                                    first,
                                                    "channel_list",
                                                    new()
                                                )
                                                .ConfigureAwait(false);
                                            Assert.Contains(
                                                Text(direct, "channelId"),
                                                Items(channels).Select(item => Text(item, "id"))
                                            );
                                            Assert.DoesNotContain(
                                                "dm:owner:worker2",
                                                Items(channels).Select(item => Text(item, "id"))
                                            );

                                            JsonObject gist = await Call(
                                                    first,
                                                    "board_publish",
                                                    new()
                                                    {
                                                        ["type"] = "FACT",
                                                        ["content"] =
                                                            "wire parser.cs line 7 has "
                                                            + "a bounded reader",
                                                        ["detail"] =
                                                            "The reader preserves the "
                                                            + "complete request.",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("worker1", Text(gist, "actor"));
                                            Assert.AreEqual("FACT", Text(gist, "type"));
                                            JsonObject recent = await Call(
                                                    second,
                                                    "board_recent",
                                                    new()
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                Id(gist),
                                                Id(Assert.ContainsSingle(Items(recent)))
                                            );
                                            JsonObject found = await Call(
                                                    second,
                                                    "board_search",
                                                    new() { ["query"] = "bounded reader" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                Id(gist),
                                                Id(Assert.ContainsSingle(Items(found)))
                                            );
                                            JsonObject expanded = await Call(
                                                    second,
                                                    "board_expand",
                                                    new()
                                                    {
                                                        ["labels"] = new JsonArray(
                                                            Text(gist, "label")
                                                        ),
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                "The reader preserves the complete request.",
                                                Text(
                                                    Assert.ContainsSingle(Items(expanded)),
                                                    "detail"
                                                )
                                            );
                                            Assert.IsEmpty(expanded["unknownLabels"]!.AsArray());

                                            string head = await state
                                                .CommitAsync(
                                                    state.Operations.GetWorkerPath("worker1"),
                                                    "wire.txt",
                                                    "serialized local candidate\n",
                                                    token
                                                )
                                                .ConfigureAwait(false);
                                            JsonObject change = await Call(
                                                    first,
                                                    "change_write",
                                                    new()
                                                    {
                                                        ["action"] = "submit",
                                                        ["title"] = "Wire candidate",
                                                        ["body"] = "Preserve exact head.",
                                                        ["taskIds"] = new JsonArray(taskId),
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            long changeId = Id(change);
                                            Assert.AreEqual(head, Text(change, "head"));
                                            Assert.AreEqual("worker1", Text(change, "author"));
                                            JsonObject changed = await Call(
                                                    first,
                                                    "change_write",
                                                    new()
                                                    {
                                                        ["action"] = "update",
                                                        ["id"] = changeId,
                                                        ["body"] = "Wire candidate updated.",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                "Wire candidate",
                                                Text(changed, "title")
                                            );
                                            Assert.AreEqual(
                                                "Wire candidate updated.",
                                                Text(changed, "body")
                                            );
                                            await Call(
                                                    second,
                                                    "change_write",
                                                    new()
                                                    {
                                                        ["action"] = "comment",
                                                        ["id"] = changeId,
                                                        ["body"] = "Peer inspected wire.txt.",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            JsonObject review = await Call(
                                                    second,
                                                    "change_review",
                                                    new()
                                                    {
                                                        ["id"] = changeId,
                                                        ["head"] = head,
                                                        ["decision"] = "approve",
                                                        ["body"] = "Approve exact wire candidate.",
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("worker2", Text(review, "actor"));
                                            Assert.AreEqual(head, Text(review, "head"));
                                            Assert.AreEqual("approve", Text(review, "decision"));
                                            JsonObject queued = await Call(
                                                    first,
                                                    "change_write",
                                                    new()
                                                    {
                                                        ["action"] = "integrate",
                                                        ["id"] = changeId,
                                                        ["head"] = head,
                                                    }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("pending", Text(queued, "state"));
                                            await state
                                                .Operations.ProcessPendingAsync(token)
                                                .ConfigureAwait(false);
                                            JsonObject candidate = await Call(
                                                    owner,
                                                    "change_read",
                                                    new() { ["id"] = changeId }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual("integrated", Text(candidate, "state"));
                                            Assert.AreEqual(head, Text(candidate, "head"));
                                            string diff = Text(candidate, "diff");
                                            Assert.Contains("wire.txt", diff);
                                            Assert.Contains("+serialized local candidate", diff);
                                            Assert.Contains(
                                                "Peer inspected wire.txt.",
                                                candidate["comments"]!.ToJsonString()
                                            );
                                            JsonObject changes = await Call(
                                                    second,
                                                    "change_list",
                                                    new() { ["state"] = "integrated" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.AreEqual(
                                                changeId,
                                                Id(Assert.ContainsSingle(Items(changes)))
                                            );
                                            JsonObject checks = await Call(
                                                    owner,
                                                    "check_read",
                                                    new() { ["changeId"] = changeId }
                                                )
                                                .ConfigureAwait(false);
                                            JsonObject candidateCheck =
                                                await AssertChangeChecksAsync(
                                                        state,
                                                        changeId,
                                                        candidate,
                                                        checks,
                                                        token
                                                    )
                                                    .ConfigureAwait(false);
                                            long healthCheckId =
                                                Id(candidateCheck) == 1L ? 2L : 1L;
                                            JsonObject healthResult = await Call(
                                                    owner,
                                                    "check_read",
                                                    new() { ["id"] = healthCheckId }
                                                )
                                                .ConfigureAwait(false);
                                            JsonObject health = Assert.ContainsSingle(
                                                Items(healthResult)
                                            );
                                            Assert.AreEqual(healthCheckId, Id(health));
                                            Assert.IsTrue(health.ContainsKey("changeId"));
                                            Assert.IsNull(health["changeId"]);
                                            Assert.AreEqual("integration", Text(health, "kind"));
                                            Assert.AreEqual("passed", Text(health, "status"));
                                            Assert.AreEqual(
                                                "serialized local candidate\n",
                                                await state
                                                    .GitAsync(
                                                        state.Repository,
                                                        token,
                                                        "show",
                                                        state.IntegrationRef + ":wire.txt"
                                                    )
                                                    .ConfigureAwait(false)
                                            );
                                            JsonObject work = await Call(
                                                    owner,
                                                    "work_search",
                                                    new() { ["query"] = "Wire" }
                                                )
                                                .ConfigureAwait(false);
                                            Assert.Contains(
                                                "task",
                                                Items(work).Select(item => Text(item, "kind"))
                                            );
                                            Assert.Contains(
                                                "change",
                                                Items(work).Select(item => Text(item, "kind"))
                                            );
                                            Assert.HasCount(catalog.Count, exercised);
                                        },
                                        token
                                    )
                                    .ConfigureAwait(false),
                            token
                        )
                        .ConfigureAwait(false),
                token
            )
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("AllSourceToolsBoundAndTruthful")]
    [Timeout(120_000, CooperativeCancellation = true)]
    [DataRow("actor", "invalid_arguments")]
    [DataRow("runId", "invalid_arguments")]
    [DataRow("missing", "invalid_arguments")]
    [DataRow("limit", "invalid_arguments")]
    [DataRow("private-channel", "not_visible")]
    [DataRow("gitea.list_my_repos", "unavailable")]
    [DataRow("gitea.search_repos", "unavailable")]
    [DataRow("mattermost.create_direct_channel", "unavailable")]
    [DataRow("mattermost.list_teams", "unavailable")]
    public async Task BoundFailuresHaveExplicitEnvelopesWithoutUnauthorizedEffects(
        string scenario,
        string code
    )
    {
        using BoardDeliveryState state = new();
        using BoardDeliveryState other = new();
        await SerializedToolHost
            .RunAsync(
                state.Database,
                "worker1",
                async host =>
                {
                    string operation = "task_write";
                    JsonObject arguments = new()
                    {
                        ["action"] = "create",
                        ["title"] = "Spoofed",
                        ["body"] = "Must not exist.",
                    };
                    if (scenario is "actor" or "runId")
                    {
                        arguments[scenario] =
                            scenario == "actor" ? "owner" : other.Database.RunId;
                    }
                    else if (scenario == "missing")
                    {
                        arguments.Remove("title");
                    }
                    else if (scenario == "limit")
                    {
                        operation = "task_list";
                        arguments = new() { ["limit"] = 0 };
                    }
                    else if (scenario == "private-channel")
                    {
                        operation = "channel_read";
                        arguments = new() { ["id"] = "dm:owner:worker2" };
                    }
                    else
                    {
                        operation = scenario;
                        arguments = new();
                    }
                    JsonObject result = await host.InvokeAsync(
                            operation,
                            arguments,
                            context.CancellationToken
                        )
                        .ConfigureAwait(false);
                    Assert.IsFalse(result["ok"]!.GetValue<bool>());
                    Assert.AreEqual(code, Text(result, "code"));
                    Assert.IsNotEmpty(Text(result, "message"));
                    Assert.IsTrue(result.TryGetPropertyValue("data", out JsonNode? data));
                    Assert.IsNull(data);
                    Assert.IsEmpty(result["notices"]!["items"]!.AsArray());
                    JsonObject tasks = await host.DataAsync(
                            "task_list",
                            new(),
                            context.CancellationToken
                        )
                        .ConfigureAwait(false);
                    JsonObject rootTask = Assert.ContainsSingle(Items(tasks));
                    Assert.AreEqual(1L, Id(rootTask));
                    Assert.AreEqual("open", Text(rootTask, "state"));
                    Assert.IsNull(rootTask["closedBy"]);
                    Assert.IsNull(state.Operations.ClaimBatch("worker1", false));
                    Assert.IsNull(state.Operations.ClaimBatch("worker2", false));
                    JsonObject otherTasks = state.Database.RunId == other.Database.RunId
                        ? tasks
                        : other.Invoke("owner", "task_list", new());
                    JsonObject otherRoot = Assert.ContainsSingle(Items(otherTasks));
                    Assert.AreEqual(1L, Id(otherRoot));
                    Assert.AreEqual("open", Text(otherRoot, "state"));
                    Assert.IsNull(other.Operations.ClaimBatch("worker1", false));
                    Assert.IsNull(other.Operations.ClaimBatch("worker2", false));
                },
                context.CancellationToken
            )
            .ConfigureAwait(false);
    }

    [TestMethod]
    [TestCategory("NoticeSnapshotAtToolBoundary")]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task NoticeSnapshotAtToolBoundary()
    {
        using BoardDeliveryState state = new();
        JsonObject first = state.Publish(
            "worker2",
            "FACT",
            "snapshot alpha at parser.cs line 1"
        );
        JsonObject message = state.Send("worker2", "worker1", "private alpha");
        JsonObject snapshot = state.Operations.ReadSharedContext("worker1");
        Assert.AreEqual(
            Id(first),
            Id(Assert.ContainsSingle(snapshot["board"]!.AsArray().OfType<JsonObject>()))
        );
        Assert.AreEqual(
            "private alpha",
            Text(
                Assert.ContainsSingle(snapshot["messages"]!.AsArray().OfType<JsonObject>()),
                "body"
            )
        );
        await SerializedToolHost
            .RunAsync(
                state.Database,
                "worker1",
                async worker =>
                    await SerializedToolHost
                        .RunAsync(
                            state.Database,
                            "worker2",
                            async peer =>
                            {
                                JsonObject displayed = await worker
                                    .InvokeAsync(
                                        "message_read",
                                        new() { ["channelId"] = Text(message, "channelId") },
                                        context.CancellationToken
                                    )
                                    .ConfigureAwait(false);
                                Assert.AreEqual(
                                    "private alpha",
                                    Text(
                                        Assert.ContainsSingle(Items(displayed["data"]!.AsObject())),
                                        "body"
                                    )
                                );
                                JsonObject displayedNotices = displayed["notices"]!.AsObject();
                                JsonObject[] notices = displayedNotices["items"]!
                                    .AsArray()
                                    .OfType<JsonObject>()
                                    .ToArray();
                                string firstLease = Text(displayedNotices, "leaseToken");
                                Assert.IsNotEmpty(firstLease);
                                Assert.AreEqual("worker1", Text(displayedNotices, "recipient"));
                                Assert.Contains(
                                    "g" + Id(first),
                                    notices.Select(item => Text(item, "id"))
                                );
                                Assert.Contains(
                                    "private alpha",
                                    notices.Single(item => Text(item, "kind") == "dm")[
                                        "data"
                                    ]!.ToJsonString()
                                );
                                JsonObject repeated = await worker
                                    .InvokeAsync("run_info", new(), context.CancellationToken)
                                    .ConfigureAwait(false);
                                JsonObject repeatedNotices = repeated["notices"]!.AsObject();
                                Assert.IsEmpty(repeatedNotices["items"]!.AsArray());
                                Assert.AreNotEqual(
                                    firstLease,
                                    Text(repeatedNotices, "leaseToken")
                                );
                                Assert.IsEmpty(
                                    state.Operations.ReadSharedContext("worker1")["board"]!
                                        .AsArray()
                                );
                                Assert.IsNull(state.Operations.ClaimBatch("worker1", false));

                                JsonObject old = state.Publish(
                                    "worker2",
                                    "FACT",
                                    "decoder.cs line 22 handles partial frames"
                                );
                                JsonObject oldMessage = state.Send(
                                    "worker2",
                                    "worker1",
                                    "private beta"
                                );
                                DeliveryBatch batch =
                                    state.Operations.ClaimBatch("worker1", false)
                                    ?? throw new AssertFailedException(
                                        "Expected a genuine old delivery snapshot."
                                    );
                                Assert.Contains("private beta", batch.Prompt);
                                JsonObject fresh = state.Publish(
                                    "worker2",
                                    "FACT",
                                    "encoder.cs line 33 records the checksum"
                                );
                                JsonObject freshMessage = state.Send(
                                    "worker2",
                                    "worker1",
                                    "private gamma"
                                );
                                JsonObject other = await peer.InvokeAsync(
                                        "run_info",
                                        new(),
                                        context.CancellationToken
                                    )
                                    .ConfigureAwait(false);
                                Assert.AreEqual(
                                    "worker2",
                                    Text(other["notices"]!.AsObject(), "recipient")
                                );
                                Assert.IsEmpty(other["notices"]!["items"]!.AsArray());
                                Assert.DoesNotContain("private gamma", other.ToJsonString());
                                // Turn completion acknowledges old batch items without consuming
                                // fresh items created after the batch snapshot.
                                state.Operations.CompleteBatch(batch.Id);
                                JsonObject newer = await worker
                                    .InvokeAsync(
                                        "board_recent",
                                        new(),
                                        context.CancellationToken
                                    )
                                    .ConfigureAwait(false);
                                CollectionAssert.AreEqual(
                                    new[] { Id(first), Id(old), Id(fresh) },
                                    Items(newer["data"]!.AsObject()).Select(Id).ToArray()
                                );
                                string[] ids = newer["notices"]!["items"]!
                                    .AsArray()
                                    .OfType<JsonObject>()
                                    .Select(item => Text(item, "id"))
                                    .ToArray();
                                Assert.Contains("g" + Id(fresh), ids);
                                Assert.DoesNotContain("g" + Id(first), ids);
                                Assert.DoesNotContain("g" + Id(old), ids);
                                Assert.Contains("private gamma", newer["notices"]!.ToJsonString());
                                Assert.DoesNotContain(
                                    "private beta",
                                    newer["notices"]!.ToJsonString()
                                );
                                // LA-REQ-008: notice display and turn acknowledgement have no
                                // public event-state or cursor reader once shown receipts suppress
                                // redelivery.
                                Assert.AreEqual(
                                    "sent",
                                    state.Text(
                                        "SELECT state FROM events WHERE recipient='worker1' "
                                            + "AND dedup_key=$key;",
                                        ("$key", "message:" + Id(oldMessage))
                                    )
                                );
                                Assert.AreEqual(
                                    "pending",
                                    state.Text(
                                        "SELECT state FROM events WHERE recipient='worker1' "
                                            + "AND dedup_key=$key;",
                                        ("$key", "message:" + Id(freshMessage))
                                    )
                                );
                                Assert.AreEqual(
                                    Id(fresh),
                                    state.Count(
                                        "SELECT gist_id FROM notice_cursors "
                                            + "WHERE recipient='worker1';"
                                    )
                                );
                                JsonObject last = await worker
                                    .InvokeAsync("run_info", new(), context.CancellationToken)
                                    .ConfigureAwait(false);
                                Assert.IsEmpty(last["notices"]!["items"]!.AsArray());
                                Assert.IsNull(state.Operations.ClaimBatch("worker1", false));
                            },
                            context.CancellationToken
                        )
                        .ConfigureAwait(false),
                context.CancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<JsonObject> AssertChangeChecksAsync(
        GitScenarioState state,
        long changeId,
        JsonObject candidate,
        JsonObject result,
        CancellationToken token
    )
    {
        Assert.IsTrue(JsonNode.DeepEquals(candidate["checks"], result["items"]));
        JsonObject check = Assert.ContainsSingle(Items(result));
        string[] identity = (
            await state
                .GitAsync(
                    state.Repository,
                    token,
                    "rev-parse",
                    state.IntegrationRef,
                    state.IntegrationRef + "^{tree}"
                )
                .ConfigureAwait(false)
        ).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.HasCount(2, identity);
        Assert.AreEqual<long?>(changeId, check["changeId"]?.GetValue<long>());
        Assert.AreEqual("candidate", Text(check, "kind"));
        Assert.AreEqual("passed", Text(check, "status"));
        Assert.AreEqual(identity[0], Text(check, "commit"));
        Assert.AreEqual(identity[1], Text(check, "tree"));
        return check;
    }

    private static void AssertWorkSearchInputContract(JsonElement schema)
    {
        Assert.AreEqual("object", schema.GetProperty("type").GetString());
        Assert.IsFalse(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.AreEqual(
            "query",
            Assert.ContainsSingle(schema.GetProperty("required").EnumerateArray()).GetString()
        );
        JsonElement properties = schema.GetProperty("properties");
        Assert.AreEqual("string", properties.GetProperty("query").GetProperty("type").GetString());
        JsonElement cursor = properties.GetProperty("cursor");
        Assert.AreEqual("integer", cursor.GetProperty("type").GetString());
        Assert.AreEqual(0, cursor.GetProperty("minimum").GetInt32());
        JsonElement limit = properties.GetProperty("limit");
        Assert.AreEqual("integer", limit.GetProperty("type").GetString());
        Assert.AreEqual(1, limit.GetProperty("minimum").GetInt32());
        Assert.AreEqual(200, limit.GetProperty("maximum").GetInt32());
        Assert.IsFalse(properties.TryGetProperty("actor", out _));
        Assert.IsFalse(properties.TryGetProperty("runId", out _));
    }

    private static long Id(JsonObject value) => value["id"]!.GetValue<long>();

    private static string Text(JsonObject value, string key) => value[key]!.GetValue<string>();

    private static JsonObject[] Items(JsonObject value) =>
        value["items"]!.AsArray().OfType<JsonObject>().ToArray();
}
