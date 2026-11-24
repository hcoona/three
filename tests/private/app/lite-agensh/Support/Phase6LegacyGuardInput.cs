using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests.Support;

// SYNTHETIC CONTRACT-GUARD declarations, never historical/native provenance.
// The actual creator owner/runtime IDs, readings and history are not rewritten.
internal sealed class Phase6LegacyGuardInput
{
    private const string Subject = "3805a50490afbdd6c53ec4e3c6e8c64a3150858f";
    private const string Sdk = "1.0.28+845c22879d426535d97901908014b807e0b68d0f";
    private static readonly (string Name, string Hash)[] Declarations =
    [
        ("NativeWorker.cs", "8e9aed624796322a58f4dc4b085c31621be36263a00cc5b30e16c6ec482fa9ee"),
        ("CreditLedger.cs", "0c860209d0eb44cbb0003e80fa3c2404e0d8b5e5c000b73666f072c7785c0b11"),
        ("NativeSdkSession.cs", "3cfc60cc2acfe148d6a928d6952ddad958e05ee37ada1463d0d5dca6b2d197f6"),
        ("NativeSdkHost.cs", "d5dcdfd62234a721ec402282a706f58914c525c949181e53d748b6523428d254"),
        ("NativeRuntime.cs", "97efa1a04c3b6e7b31208bda8e391112fc053e939df7caa738eda0018d1d8355"),
        (
            "NativeWorkerFactory.cs",
            "6ec426801be69b76464b882b71bf47f3facdc0595aaa6cdcb75210542ff1ac86"
        ),
    ];
    private readonly Dictionary<string, byte[]> originals = new(StringComparer.Ordinal);
    private readonly string root;
    private byte[] envelopeBytes = [];
    internal string EvidencePath => Path.Combine(root, "legacy-contract-envelope.json");

    private Phase6LegacyGuardInput(string root) => this.root = root;

    internal static async Task<Phase6LegacyGuardInput> CreateAsync(
        Phase6OwnedCreator creator,
        CancellationToken token
    )
    {
        RunDatabase database = creator.Database;
        CreditReservation row = creator.Target;
        Phase6LegacyGuardInput input = new(Path.GetDirectoryName(database.StatePath)!);
        JsonObject before = Phase6State.History(database);
        JsonObject diagnostic = JsonNode
            .Parse(database.ReadMetadata($"native:session:{row.SessionId}")!)!
            .AsObject();
        string runtimeKey = "native:runtime:" + diagnostic["runtimeId"]!.GetValue<string>();
        JsonObject runtime = JsonNode.Parse(database.ReadMetadata(runtimeKey)!)!.AsObject();
        JsonObject originalLabels = new()
        {
            ["marker"] = database.ReadMetadata(NoDispatchCancellation.Marker(row.Id)),
            ["diagnostic"] = diagnostic.DeepClone(),
            ["runtime"] = runtime.DeepClone(),
        };
        diagnostic["sdkVersion"] = Sdk;
        diagnostic["runtimeVersion"] = "1.0.105";
        runtime["runtimeVersion"] = "1.0.105";
        database.WriteMetadata($"native:session:{row.SessionId}", diagnostic.ToJsonString());
        database.WriteMetadata(runtimeKey, runtime.ToJsonString());
        using (SqliteConnection connection = database.Open())
        {
            Assert.AreEqual(
                1,
                Sql.Execute(
                    connection,
                    "DELETE FROM meta WHERE key=$key;",
                    null,
                    ("$key", NoDispatchCancellation.Marker(row.Id))
                )
            );
            foreach (string table in new[] { "native_sessions", "native_attempts" })
            {
                Assert.AreEqual(
                    1,
                    Sql.Execute(
                        connection,
                        $"UPDATE {table} SET initialize_json=$initialize WHERE reservation_id=$id;",
                        null,
                        ("$initialize", "sdk:" + Sdk),
                        ("$id", row.Id)
                    )
                );
            }
        } // All mutation handles closed before snapshotting/hashing.
        using (SqliteConnection source = database.Open())
        {
            Backup(source, input.PathFor("run-after.evidence-only.sqlite"));
        }
        using (SqliteConnection source = Phase6State.OpenCampaign(database))
        {
            Backup(source, input.PathFor("campaign-after.evidence-only.sqlite"));
        }
        File.WriteAllText(
            input.PathFor("outcome-private.json"),
            new JsonObject
            {
                ["sourceRevision"] = Subject,
                ["runId"] = database.RunId,
            }.ToJsonString(),
            new UTF8Encoding(false)
        );
        File.WriteAllText(
            input.PathFor("source-after.json"),
            (await creator.PreservationAsync(token).ConfigureAwait(false)).ToJsonString(),
            new UTF8Encoding(false)
        );
        JsonObject hashes = new();
        foreach (
            string name in new[]
            {
                "run-after.evidence-only.sqlite",
                "campaign-after.evidence-only.sqlite",
                "outcome-private.json",
                "source-after.json",
            }
        )
        {
            byte[] bytes = File.ReadAllBytes(input.PathFor(name));
            input.originals.Add(name, bytes);
            hashes[name] = Digest(bytes);
        }
        JsonArray sources = [];
        foreach ((string name, string hash) in Declarations)
        {
            sources.Add(
                new JsonObject
                {
                    ["path"] = "src/private/app/lite-agensh/" + name,
                    ["sha256"] = hash,
                }
            );
        }
        JsonObject envelope = new()
        {
            ["fixtureClassification"] = "synthetic-legacy-contract-guard-input",
            ["subject"] = Subject,
            ["runId"] = database.RunId,
            ["reservationId"] = row.Id,
            ["sessionId"] = row.SessionId,
            ["worker"] = row.Worker,
            ["proofPathSources"] = sources,
            ["inputSha256"] = hashes,
        };
        input.envelopeBytes = Encoding.UTF8.GetBytes(envelope.ToJsonString());
        File.WriteAllBytes(input.EvidencePath, input.envelopeBytes);
        Console.WriteLine(
            "PHASE6_SYNTHETIC_CONTRACT_DELTA "
                + new JsonObject
                {
                    ["classification"] = "SYNTHETIC CONTRACT ONLY; NOT HISTORICAL/NATIVE PROOF",
                    ["originalLabels"] = originalLabels,
                    ["historyBefore"] = before,
                    ["historyAfter"] = Phase6State.History(database),
                    ["envelope"] = envelope,
                }.ToJsonString()
        );
        return input;
    }

    private static void Backup(SqliteConnection source, string destinationPath)
    {
        Assert.IsFalse(File.Exists(destinationPath), "A fresh owned reference is required.");
        using SqliteConnection destination = new(
            new SqliteConnectionStringBuilder
            {
                DataSource = destinationPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                DefaultTimeout = 5,
            }.ToString()
        );
        destination.Open();
        source.BackupDatabase(destination);
        Sql.Execute(destination, "PRAGMA wal_checkpoint(TRUNCATE);");
        Sql.Execute(destination, "PRAGMA journal_mode=DELETE;");
    } // Nonpooled destination and any native handles close before returning.

    internal void Damage(string selection)
    {
        JsonObject envelope = JsonNode.Parse(envelopeBytes)!.AsObject();
        if (selection == "C")
        {
            envelope["proofPathSources"]![0]!["sha256"] = "0" + Declarations[0].Hash[1..];
        }
        else if (selection == "H")
        {
            File.WriteAllBytes(
                PathFor("outcome-private.json"),
                [.. originals["outcome-private.json"], (byte)'\n']
            );
        }
        else if (selection == "A")
        {
            string path = PathFor("run-after.evidence-only.sqlite");
            using (SqliteConnection frozen = Phase6State.OpenFile(path))
            {
                Assert.AreEqual(
                    1,
                    Sql.Execute(
                        frozen,
                        "UPDATE native_attempts SET baseline_usage='1' WHERE reservation_id=$id;",
                        null,
                        ("$id", envelope["reservationId"]!.GetValue<string>())
                    )
                );
                Sql.Execute(frozen, "PRAGMA journal_mode=DELETE;");
            }
            envelope["inputSha256"]!["run-after.evidence-only.sqlite"] = Digest(
                File.ReadAllBytes(path)
            );
        }
        else if (selection == "W")
        {
            JsonObject preservation = JsonNode.Parse(originals["source-after.json"])!.AsObject();
            Assert.ContainsSingle(preservation["checkouts"]!.AsArray())!["path"] =
                Path.Combine(root, "mismatched-checkout");
            string path = PathFor("source-after.json");
            File.WriteAllText(path, preservation.ToJsonString(), new UTF8Encoding(false));
            envelope["inputSha256"]!["source-after.json"] = Digest(File.ReadAllBytes(path));
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(selection));
        }
        File.WriteAllText(EvidencePath, envelope.ToJsonString(), new UTF8Encoding(false));
    }

    internal void Restore(string selection)
    {
        if (selection == "H")
        {
            File.WriteAllBytes(PathFor("outcome-private.json"), originals["outcome-private.json"]);
        }
        else if (selection == "A")
        {
            File.WriteAllBytes(
                PathFor("run-after.evidence-only.sqlite"),
                originals["run-after.evidence-only.sqlite"]
            );
        }
        else if (selection == "W")
        {
            File.WriteAllBytes(PathFor("source-after.json"), originals["source-after.json"]);
        }
        File.WriteAllBytes(EvidencePath, envelopeBytes);
        foreach ((string name, byte[] bytes) in originals)
        {
            Assert.AreEqual(
                Digest(bytes),
                Digest(File.ReadAllBytes(PathFor(name))),
                "Restored closed reference bytes differ: " + name
            );
        }
    }

    private string PathFor(string name) => Path.Combine(root, name);

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
