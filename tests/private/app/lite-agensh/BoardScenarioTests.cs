using System.Text.Json.Nodes;
using LiteAgensh;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class BoardScenarioTests(TestContext testContext)
{
    [TestMethod]
    [DataRow("FACT", 100)]
    [DataRow("PATCH_SUMMARY", 300)]
    public void AdmissionTruncatesUnicodeGistsWithoutExecutingReportedChecks(string type, int cap)
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        LocalTools tools = new(database);
        string prefix =
            type == "PATCH_SUMMARY" ? "files=a.cs | idea=fix | evidence=passed tests " : "finding ";
        string suffix = string.Concat(Enumerable.Repeat("😀", cap));
        string expected = prefix + string.Concat(Enumerable.Repeat("😀", cap - prefix.Length));

        JsonObject admitted = Scenario.Call(
            tools.Collaboration,
            "worker1",
            "board_publish",
            new
            {
                type,
                content = prefix + suffix,
                detail = "Retained long evidence.",
            }
        );

        Assert.AreEqual(expected, admitted["content"]!.GetValue<string>());
        Assert.AreEqual("Retained long evidence.", admitted["detail"]!.GetValue<string>());
        JsonObject recent = Assert.ContainsSingle(
            Scenario.Items(Scenario.Call(tools.Collaboration, "worker2", "board_recent", new { }))
        );
        Assert.AreEqual(Scenario.Id(admitted), Scenario.Id(recent));
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM checks;"));
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM process_leases;"));
    }

    [TestMethod]
    [DataRow("FACT", "none", null)]
    [DataRow("PATCH_SUMMARY", "files=a.cs | idea=fix | evidence=pending", null)]
    [DataRow("CLAIM", "working on #1", 0)]
    [DataRow("FACT", "an observed behavior", 60)]
    public void InvalidAdmissionDoesNotAddHistoryOrRecipientEvents(
        string type,
        string content,
        int? ttl
    )
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        CollaborationOperations board = new(database);
        JsonObject request = new() { ["type"] = type, ["content"] = content };
        if (ttl is not null)
        {
            request["ttlSeconds"] = ttl.Value;
        }

        Scenario.Failure(board.Invoke("worker1", "board_publish", request), "invalid_arguments");

        Assert.IsEmpty(Scenario.Items(Scenario.Call(board, "worker2", "board_recent", new { })));
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM board_history;"));
        Assert.AreEqual(0L, Sql.Scalar(retained, "SELECT COUNT(*) FROM events;"));
    }

    [TestMethod]
    public void ExpiryReplacementAndFailureInvalidateRecentCheckpointButRetainHistory()
    {
        using RunFixture fixture = new();
        ScenarioClock clock = new();
        RunDatabase database = new(fixture.StatePath, fixture.Configuration, clock);
        CollaborationOperations board = new(database);
        JsonObject first = Scenario.Call(
            board,
            "worker1",
            "board_publish",
            new
            {
                type = "PATCH_SUMMARY",
                content = "files=a.py | idea=first | evidence=test_alpha passed",
                detail = "Original evidence remains expandable.",
                workLabel = "alpha",
            }
        );
        Assert.IsTrue(first["verifiedCheckpoint"]!.GetValue<bool>());
        Scenario.Call(
            board,
            "worker1",
            "board_publish",
            new
            {
                type = "FAIL",
                content = "test_alpha now fails",
                workLabel = "alpha",
            }
        );
        JsonObject invalidated = Assert.ContainsSingle(
            Scenario.Items(
                Scenario.Call(board, "worker2", "board_search", new { query = "idea=first" })
            )
        );
        Assert.IsFalse(invalidated["verifiedCheckpoint"]!.GetValue<bool>());
        Assert.AreEqual(
            "test_alpha now fails",
            invalidated["invalidatedReason"]!.GetValue<string>()
        );
        Assert.AreEqual(1L, invalidated["localFailures"]!.GetValue<long>());
        JsonObject replacement = Scenario.Call(
            board,
            "worker1",
            "board_publish",
            new
            {
                type = "PATCH_SUMMARY",
                content = "files=a.py | idea=second | evidence=test_alpha passed",
                workLabel = "alpha",
            }
        );
        JsonObject claim = Scenario.Call(
            board,
            "worker2",
            "board_publish",
            new { type = "CLAIM", content = "working on #1" }
        );
        Assert.AreEqual(
            clock.GetUtcNow().AddHours(1),
            DateTimeOffset.Parse(
                claim["expiresAt"]!.GetValue<string>(),
                System.Globalization.CultureInfo.InvariantCulture
            )
        );
        clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(
            Scenario
                .Items(Scenario.Call(board, "worker2", "board_recent", new { }))
                .Any(row => Scenario.Id(row) == Scenario.Id(claim))
        );
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.IsTrue(
            Scenario
                .Items(Scenario.Call(board, "worker2", "board_recent", new { }))
                .Any(row => Scenario.Id(row) == Scenario.Id(claim))
        );
        clock.Advance(TimeSpan.FromMilliseconds(1));

        JsonObject[] recent = Scenario.Items(
            Scenario.Call(board, "worker2", "board_recent", new { })
        );
        Assert.IsFalse(
            recent.Any(row =>
                Scenario.Id(row) == Scenario.Id(first) || Scenario.Id(row) == Scenario.Id(claim)
            )
        );
        Assert.IsTrue(recent.Any(row => Scenario.Id(row) == Scenario.Id(replacement)));
        Assert.HasCount(
            2,
            Scenario.Items(
                Scenario.Call(board, "worker2", "board_search", new { query = "files=a.py" })
            )
        );
        JsonObject expanded = Scenario.Call(
            board,
            "worker2",
            "board_expand",
            new
            {
                labels = new[] { first["label"]!.GetValue<string>(), "G999999", "../not-a-path" },
            }
        );
        Assert.AreEqual(
            "Original evidence remains expandable.",
            Assert.ContainsSingle(Scenario.Items(expanded))["detail"]!.GetValue<string>()
        );
        Assert.HasCount(2, expanded["unknownLabels"]!.AsArray());
        Assert.IsFalse(expanded["truncated"]!.GetValue<bool>());
        JsonObject truncated = Scenario.Call(
            board,
            "worker2",
            "board_expand",
            new { labels = Enumerable.Repeat(first["label"]!.GetValue<string>(), 101).ToArray() }
        );
        Assert.HasCount(100, Scenario.Items(truncated));
        Assert.IsTrue(truncated["truncated"]!.GetValue<bool>());
    }

    [TestMethod]
    public void BooleanHistorySearchAndSemanticCollapseRetainLiteralMeaning()
    {
        using RunFixture fixture = new();
        CollaborationOperations board = new(fixture.CreateDatabase());
        JsonObject alpha = Scenario.Call(
            board,
            "worker1",
            "board_publish",
            new { type = "FACT", content = "alpha literal%_ token" }
        );
        JsonObject betaGamma = Scenario.Call(
            board,
            "worker2",
            "board_publish",
            new { type = "OBSERVED", content = "beta gamma" }
        );
        Scenario.Call(
            board,
            "worker1",
            "board_publish",
            new { type = "FACT", content = "beta only" }
        );
        Scenario.Call(
            board,
            "worker1",
            "board_publish",
            new { type = "FACT", content = "a.py def fix line 12 first observation" }
        );
        Scenario.Call(
            board,
            "worker2",
            "board_publish",
            new { type = "FACT", content = "a.py def fix line 12 second observation" }
        );

        long[] disjunction = Scenario
            .Items(
                Scenario.Call(
                    board,
                    "owner",
                    "board_search",
                    new { query = "alpha | beta & gamma" }
                )
            )
            .Select(Scenario.Id)
            .ToArray();
        Assert.HasCount(2, disjunction);
        Assert.Contains(Scenario.Id(alpha), disjunction);
        Assert.Contains(Scenario.Id(betaGamma), disjunction);
        Assert.AreEqual(
            Scenario.Id(betaGamma),
            Scenario.Id(
                Assert.ContainsSingle(
                    Scenario.Items(
                        Scenario.Call(
                            board,
                            "owner",
                            "board_search",
                            new { query = "(alpha | beta) & gamma" }
                        )
                    )
                )
            )
        );
        Assert.AreEqual(
            Scenario.Id(alpha),
            Scenario.Id(
                Assert.ContainsSingle(
                    Scenario.Items(
                        Scenario.Call(board, "worker2", "board_search", new { query = "literal%_" })
                    )
                )
            )
        );
        Scenario.Failure(
            board.Invoke(
                "worker1",
                "board_search",
                Json.SerializePayload(new { query = "alpha & (" })
            ),
            "invalid_arguments"
        );
        JsonObject collapsed = Assert.ContainsSingle(
            Scenario
                .Items(Scenario.Call(board, "owner", "board_recent", new { }))
                .Where(row =>
                    row["content"]!.GetValue<string>().StartsWith("a.py", StringComparison.Ordinal)
                )
        );
        Assert.AreEqual(2, collapsed["duplicateCount"]!.GetValue<int>());
        Assert.HasCount(
            2,
            Scenario.Items(Scenario.Call(board, "owner", "board_search", new { query = "a.py" }))
        );
    }

    [TestMethod]
    [Timeout(180000, CooperativeCancellation = true)]
    public void RecentCapacityEvictsOnlyRecentRowsNotAdmittedHistory()
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        CollaborationOperations board = new(database);
        long first = 0;
        using (SqliteConnection anchor = database.Open())
        {
            _ = Sql.Scalar(anchor, "SELECT COUNT(*) FROM sqlite_schema;");
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int index = 0; index <= 2000; index++)
            {
                testContext.CancellationToken.ThrowIfCancellationRequested();
                JsonObject admitted = Scenario.Call(
                    board,
                    "worker1",
                    "board_publish",
                    new { type = "OBSERVED", content = $"unique-finding-{index:D4}" }
                );
                if (index == 0)
                {
                    first = Scenario.Id(admitted);
                }

                if (index % 500 == 0)
                {
                    TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
                    testContext.WriteLine($"Board admissions: {index + 1}; elapsed: {elapsed}.");
                }
            }
        }

        Assert.AreEqual(
            first,
            Scenario.Id(
                Assert.ContainsSingle(
                    Scenario.Items(
                        Scenario.Call(
                            board,
                            "worker2",
                            "board_search",
                            new { query = "unique-finding-0000" }
                        )
                    )
                )
            )
        );
        using SqliteConnection retained = database.Open();
        Assert.AreEqual(2000L, Sql.Scalar(retained, "SELECT COUNT(*) FROM board_recent;"));
        Assert.AreEqual(2001L, Sql.Scalar(retained, "SELECT COUNT(*) FROM board_history;"));
        Assert.AreEqual(
            0L,
            Sql.Scalar(
                retained,
                "SELECT COUNT(*) FROM board_recent WHERE revision_id=$id;",
                null,
                ("$id", first)
            )
        );
    }
}
