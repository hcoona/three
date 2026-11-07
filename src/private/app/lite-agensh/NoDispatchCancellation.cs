using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LiteAgensh;

internal static class NoDispatchCancellation
{
    internal const string Contract = "write-ahead-dispatch-v1";
    private const string LegacySubject = "3805a50490afbdd6c53ec4e3c6e8c64a3150858f";
    private static readonly Dictionary<string, string> LegacySources =
        new Dictionary<string, string>
        {
            ["NativeWorker.cs"] =
                "8e9aed624796322a58f4dc4b085c31621be36263a00cc5b30e16c6ec482fa9ee",
            ["CreditLedger.cs"] =
                "0c860209d0eb44cbb0003e80fa3c2404e0d8b5e5c000b73666f072c7785c0b11",
            ["NativeSdkSession.cs"] =
                "3cfc60cc2acfe148d6a928d6952ddad958e05ee37ada1463d0d5dca6b2d197f6",
            ["NativeSdkHost.cs"] =
                "d5dcdfd62234a721ec402282a706f58914c525c949181e53d748b6523428d254",
            ["NativeRuntime.cs"] =
                "97efa1a04c3b6e7b31208bda8e391112fc053e939df7caa738eda0018d1d8355",
            ["NativeWorkerFactory.cs"] =
                "6ec426801be69b76464b882b71bf47f3facdc0595aaa6cdcb75210542ff1ac86",
        };

    internal static void RecordStart(
        RunDatabase database, CreditReservation reservation, JsonObject diagnostic,
        string cwd, string evidenceKind
    )
    {
        if (!IsEmpty(diagnostic))
        {
            return;
        }
        database.WriteMetadata(Marker(reservation.Id), new JsonObject
        {
            ["contract"] = Contract,
            ["applicationBuild"] = typeof(NativeWorker).Assembly.ManifestModule
                .ModuleVersionId.ToString(),
            ["evidenceKind"] = evidenceKind,
            ["runId"] = database.RunId,
            ["reservationId"] = reservation.Id,
            ["sessionId"] = reservation.SessionId,
            ["workingDirectory"] = cwd,
            ["turnBaseline"] = LastWorkerTurn(database, reservation.Worker),
            ["initial"] = diagnostic.DeepClone(),
        }.ToJsonString());
    }

    internal static JsonObject Cancel(
        RunDatabase database, CreditLedger credits, string reservationId,
        bool retained, string? evidencePath = null
    )
    {
        CreditReservation reservation = credits.Reservations(database.RunId)
            .SingleOrDefault(item => item.Id == reservationId)
            ?? throw Unknown("The requested reservation does not belong to this run.");
        Require(reservation.SessionId is not null && reservation.Actual is null,
            "Cancellation requires an unmeasured fresh session.");
        using SqliteConnection connection = database.Open();
        using SqliteTransaction snapshot = connection.BeginTransaction(deferred: true);
        JsonObject association = Single(connection, snapshot,
            "SELECT * FROM native_sessions WHERE reservation_id=$id;", reservation.Id);
        JsonObject attempt = Single(connection, snapshot,
            "SELECT * FROM native_attempts WHERE reservation_id=$id;", reservation.Id);
        Require(
            Text(association, "worker") == reservation.Worker
            && Text(attempt, "worker") == reservation.Worker
            && Text(association, "session_id") == reservation.SessionId
            && Text(attempt, "session_id") == reservation.SessionId
            && Text(attempt, "usage_path") == reservation.UsagePath
            && Text(attempt, "baseline_usage") == "0"
            && (Text(association, "recovery") is "fresh-session-with-durable-summary"
                or "fresh-summary-fallback: exact-session load unavailable or failed")
            && Text(attempt, "initialize_json") == Text(association, "initialize_json")
            && (long)Sql.Scalar(connection,
                "SELECT COUNT(*) FROM native_sessions WHERE session_id=$id;",
                snapshot, ("$id", reservation.SessionId))! == 1
            && (long)Sql.Scalar(connection,
                "SELECT COUNT(*) FROM native_attempts WHERE session_id=$id;",
                snapshot, ("$id", reservation.SessionId))! == 1,
            "Fresh native associations are missing, reused or contradictory."
        );
        JsonObject diagnostic = Metadata(
            connection, snapshot, $"native:session:{reservation.SessionId}");
        RequireZero(diagnostic);
        Require(
            Text(diagnostic, "sessionId") == reservation.SessionId
            && diagnostic["paidReservation"]?.GetValue<bool>() == true
            && diagnostic["alreadyInUse"]?.GetValue<bool>() == false
            && diagnostic["isRemote"]?.GetValue<bool>() == false
            && !string.IsNullOrWhiteSpace(Text(diagnostic, "selectedModel"))
            && (database.Configuration.NativeModel is null
                || Text(diagnostic, "selectedModel").Equals(
                    database.Configuration.NativeModel, StringComparison.OrdinalIgnoreCase))
            && diagnostic["appliedLimit"]?.GetValue<double>() ==
                NativeSdkSession.NativeLimit(reservation.Allowance)
            && Text(association, "initialize_json") == "sdk:" + Text(diagnostic, "sdkVersion"),
            "Observed session identity, locality, model or limit does not match."
        );
        string runtimeId = Text(diagnostic, "runtimeId");
        long leaseId = diagnostic["runtimeLeaseId"]?.GetValue<long>()
            ?? throw Unknown("The native runtime lease identity is missing.");
        JsonObject runtime = Metadata(connection, snapshot, $"native:runtime:{runtimeId}");
        JsonObject hostBinding = Metadata(connection, snapshot,
            $"native:runtime:{runtimeId}:session:{reservation.SessionId}");
        JsonObject lease = Single(connection, snapshot,
            "SELECT * FROM process_leases WHERE id=$id;", leaseId);
        Require(
            Text(runtime, "runtimeId") == runtimeId
            && Text(runtime, "state") == "settled"
            && runtime["runtimeLeaseId"]?.GetValue<long>() == leaseId
            && Text(runtime, "runtimeVersion") == Text(diagnostic, "runtimeVersion")
            && Text(hostBinding, "runtimeId") == runtimeId
            && hostBinding["runtimeLeaseId"]?.GetValue<long>() == leaseId
            && Text(hostBinding, "sessionId") == reservation.SessionId
            && Text(hostBinding, "worker") == reservation.Worker
            && SamePath(Text(hostBinding, "workingDirectory"),
                Text(diagnostic, "workingDirectory"))
            && Text(lease, "kind") == $"native:runtime:{runtimeId}"
            && Text(lease, "state") == "exited"
            && Value(connection, snapshot, $"worker:{reservation.Worker}:lifecycle") == "stopped",
            "Session/handler or common-runtime settlement is not established."
        );
        JsonObject owner = credits.ReadSdkOwner(reservation.Id);
        JsonObject proof = new()
        {
            ["contract"] = Contract,
            ["runId"] = database.RunId,
            ["reservationId"] = reservation.Id,
            ["sessionId"] = reservation.SessionId,
            ["worker"] = reservation.Worker,
            ["association"] = association,
            ["attempt"] = attempt,
            ["diagnostic"] = diagnostic,
            ["runtime"] = runtime,
            ["runtimeAssociation"] = hostBinding,
            ["runtimeLease"] = lease,
            ["owner"] = owner,
        };
        string? marker = Value(connection, snapshot, Marker(reservation.Id));
        JsonObject source;
        if (marker is not null)
        {
            source = Object(JsonNode.Parse(marker));
            Require(
                Text(source, "contract") == Contract
                && Guid.TryParse(Text(source, "applicationBuild"), out _)
                && Text(source, "runId") == database.RunId
                && Text(source, "reservationId") == reservation.Id
                && Text(source, "sessionId") == reservation.SessionId
                && SamePath(Text(source, "workingDirectory"),
                    Text(diagnostic, "workingDirectory"))
                && Text(source, "evidenceKind") is "native-sdk" or "controlled-native-dependency",
                "The persisted dispatch-contract provenance is unsupported."
            );
            long? baseline = source["turnBaseline"]?.GetValue<long>();
            Require(baseline is >= 0
                && (long)Sql.Scalar(connection,
                    "SELECT COALESCE(MAX(batch_id),0) FROM coordinator_turns WHERE worker=$id;",
                    snapshot, ("$id", reservation.Worker))! == baseline,
                "The selected fresh session has new or contradictory completed turns.");
            JsonObject initial = Object(source["initial"]);
            RequireZero(initial);
            foreach (string key in new[]
            {
                "sessionId", "workingDirectory", "selectedModel", "runtimeId",
                "runtimeLeaseId", "sdkVersion", "runtimeVersion", "appliedLimit",
                "alreadyInUse", "isRemote", "paidReservation",
            })
            {
                Require(JsonNode.DeepEquals(initial[key], diagnostic[key]),
                    "Initial and final fresh-session observations contradict each other.");
            }
        }
        else
        {
            Require(retained && evidencePath is not null,
                "Legacy cancellation requires explicit compatible retained lineage evidence.");
            source = LegacyEvidence(database, reservation, diagnostic, evidencePath!, proof);
            Require((long)Sql.Scalar(connection,
                "SELECT COUNT(*) FROM coordinator_turns WHERE worker=$id;",
                snapshot, ("$id", reservation.Worker))! == 0,
                "Legacy evidence does not distinguish this worker's prior completed turns.");
            Require(SamePath(
                Value(connection, snapshot, $"worktree:{reservation.Worker}")
                    ?? throw Unknown("The retained worktree binding is missing."),
                Text(diagnostic, "workingDirectory")),
                "The retained worktree binding contradicts native metadata.");
        }
        if (retained)
        {
            RequireAbsent(owner["pid"]!.GetValue<int>(), owner["startTicks"]!.GetValue<long>());
            RequireAbsent(checked((int)lease["pid"]!.GetValue<long>()),
                lease["start_ticks"]!.GetValue<long>());
        }
        snapshot.Commit();
        proof["source"] = source;
        return credits.CancelBeforeDispatch(
            reservation, proof, retained ? "retained-exact-process-check" : "cohort-barrier"
        );
    }

    internal static bool IsZero(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double total)
        && double.IsFinite(total) && total == 0;

    private static long LastWorkerTurn(RunDatabase database, string worker)
    {
        using SqliteConnection connection = database.Open();
        return (long)Sql.Scalar(connection,
            "SELECT COALESCE(MAX(batch_id),0) FROM coordinator_turns WHERE worker=$id;",
            null, ("$id", worker))!;
    }

    internal static bool IsEmpty(JsonObject diagnostic) =>
        IsZero(diagnostic["reportedNanoAiu"]?.GetValue<string>())
        && diagnostic["totalUserRequests"]?.GetValue<long>() == 0
        && diagnostic["aiCreditsStatus"]?.GetValue<string>() is null or "complete";

    private static void RequireZero(JsonObject diagnostic) =>
        Require(IsEmpty(diagnostic),
            "Missing, positive or incomplete native usage cannot prove cancellation.");

    private static JsonObject LegacyEvidence(
        RunDatabase database, CreditReservation reservation, JsonObject diagnostic, string path,
        JsonObject proof
    )
    {
        byte[] bytes = File.ReadAllBytes(path);
        JsonObject evidence = Object(JsonNode.Parse(bytes));
        Require(
            Text(evidence, "subject") == LegacySubject
            && Text(evidence, "runId") == database.RunId
            && Text(evidence, "reservationId") == reservation.Id
            && Text(evidence, "sessionId") == reservation.SessionId
            && Text(evidence, "worker") == reservation.Worker
            && Text(diagnostic, "sdkVersion") ==
                "1.0.28+845c22879d426535d97901908014b807e0b68d0f"
            && Text(diagnostic, "runtimeVersion") == "1.0.105",
            "Retained evidence does not establish a supported source/session lineage."
        );
        JsonArray sources = Array(evidence["proofPathSources"]);
        Require(sources.Count == LegacySources.Count, "Legacy proof-path sources are incomplete.");
        foreach ((string name, string hash) in LegacySources)
        {
            Require(sources.OfType<JsonObject>().Count(item =>
                Text(item, "path") == $"src/private/app/lite-agensh/{name}"
                && Text(item, "sha256") == hash) == 1,
                "Legacy proof-path source compatibility is unestablished.");
        }
        string root = Path.GetDirectoryName(database.StatePath)
            ?? throw Unknown("The retained evidence root is missing.");
        JsonObject hashes = Object(evidence["inputSha256"]);
        foreach (string name in new[]
        {
            "run-after.evidence-only.sqlite", "campaign-after.evidence-only.sqlite",
            "outcome-private.json", "source-after.json",
        })
        {
            byte[] input = File.ReadAllBytes(Path.Combine(root, name));
            Require(Convert.ToHexStringLower(SHA256.HashData(input)) == Text(hashes, name),
                "A referenced owned observation differs from the retained evidence.");
        }
        JsonObject outcome = Object(JsonNode.Parse(
            File.ReadAllBytes(Path.Combine(root, "outcome-private.json"))));
        Require(Text(outcome, "sourceRevision") == LegacySubject
            && Text(outcome, "runId") == database.RunId,
            "Original source/build outcome does not match the retained run.");
        JsonObject preservation = Object(JsonNode.Parse(
            File.ReadAllBytes(Path.Combine(root, "source-after.json"))));
        JsonObject[] checkouts = Array(preservation["checkouts"]).OfType<JsonObject>()
            .Where(item => SamePath(Text(item, "path"),
                Text(diagnostic, "workingDirectory"))).ToArray();
        Require(Array(preservation["changedOriginalFiles"]).Count == 0
            && checkouts.Length == 1
            && checkouts[0]["headMatches"]?.GetValue<bool>() == true
            && checkouts[0]["indexMatches"]?.GetValue<bool>() == true
            && checkouts[0]["status"]?.GetValue<string>() == "",
            "Retained source/worktree observations contradict the compatible source lineage.");
        using (SqliteConnection frozenRun = OpenSnapshot(
            Path.Combine(root, "run-after.evidence-only.sqlite")))
        using (SqliteTransaction frozen = frozenRun.BeginTransaction(deferred: true))
        {
            Require(Value(frozenRun, frozen, "run_id") == database.RunId
                && Value(frozenRun, frozen, "configuration") ==
                    database.ReadMetadata("configuration")
                && Value(frozenRun, frozen, "native:ledger_binding") ==
                    database.ReadMetadata("native:ledger_binding"),
                "The referenced run snapshot has different ownership or configuration.");
            Require(JsonNode.DeepEquals(proof["association"], Single(frozenRun, frozen,
                    "SELECT * FROM native_sessions WHERE reservation_id=$id;", reservation.Id))
                && JsonNode.DeepEquals(proof["attempt"], Single(frozenRun, frozen,
                    "SELECT * FROM native_attempts WHERE reservation_id=$id;", reservation.Id))
                && JsonNode.DeepEquals(diagnostic, Metadata(frozenRun, frozen,
                    $"native:session:{reservation.SessionId}"))
                && JsonNode.DeepEquals(proof["runtime"], Metadata(frozenRun, frozen,
                    $"native:runtime:{Text(diagnostic, "runtimeId")}"))
                && JsonNode.DeepEquals(proof["runtimeAssociation"], Metadata(frozenRun, frozen,
                    $"native:runtime:{Text(diagnostic, "runtimeId")}"
                        + $":session:{reservation.SessionId}"))
                && JsonNode.DeepEquals(proof["runtimeLease"], Single(frozenRun, frozen,
                    "SELECT * FROM process_leases WHERE id=$id;",
                    diagnostic["runtimeLeaseId"]!.GetValue<long>())),
                "The referenced native observations contradict the live retained associations.");
        }
        using (SqliteConnection frozenCampaign = OpenSnapshot(
            Path.Combine(root, "campaign-after.evidence-only.sqlite")))
        using (SqliteTransaction frozen = frozenCampaign.BeginTransaction(deferred: true))
        {
            JsonObject row = Single(frozenCampaign, frozen,
                "SELECT * FROM credit_reservations WHERE id=$id;", reservation.Id);
            JsonObject usage = Single(frozenCampaign, frozen,
                "SELECT * FROM credit_sdk_usage WHERE reservation_id=$id;", reservation.Id);
            Require(Text(row, "run_id") == database.RunId
                && Text(row, "worker") == reservation.Worker
                && Text(row, "session_id") == reservation.SessionId
                && Text(row, "usage_path") == reservation.UsagePath
                && decimal.TryParse(Text(row, "allowance"), NumberStyles.Number,
                    CultureInfo.InvariantCulture, out decimal allowance)
                && allowance == reservation.Allowance
                && row["shared"]!.GetValue<long>() == (reservation.Shared ? 1 : 0)
                && row["actual"] is null
                && Text(usage, "session_id") == reservation.SessionId
                && Text(usage, "reported_nano_aiu") == Text(diagnostic, "reportedNanoAiu")
                && JsonNode.DeepEquals(usage["status"], diagnostic["aiCreditsStatus"])
                && usage["dispatch_count"]!.GetValue<long>() == 0
                && usage["reported_requests"]?.GetValue<long>() == 0
                && JsonNode.DeepEquals(proof["owner"], Single(frozenCampaign, frozen,
                    "SELECT pid,start_ticks AS startTicks FROM credit_sdk_owners "
                        + "WHERE reservation_id=$id;", reservation.Id)),
                "The referenced campaign observations contradict the live retained evidence.");
        }
        return new()
        {
            ["contract"] = Contract,
            ["legacySubject"] = LegacySubject,
            ["evidenceKind"] = "native-sdk",
            ["evidenceSha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ["proofPathSources"] = sources.DeepClone(),
            ["inputSha256"] = hashes.DeepClone(),
        };
    }

    private static SqliteConnection OpenSnapshot(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?immutable=1",
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void RequireAbsent(int pid, long ticks)
    {
        Require(pid > 0 && ticks > 0, "Original process identity is missing.");
        try
        {
            using Process process = Process.GetProcessById(pid);
            Require(process.StartTime.ToUniversalTime().Ticks != ticks || process.HasExited,
                "An exact original owner/runtime identity is still alive.");
        }
        catch (ArgumentException)
        {
            // GetProcessById reports an absent PID.
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 87)
        {
            // Windows can remove the process between PID lookup and identity inspection.
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw Unknown($"Original process identity could not be inspected: {exception.Message}");
        }
    }

    private static JsonObject Single(
        SqliteConnection connection, SqliteTransaction transaction, string query, object id
    )
    {
        using SqliteCommand command = Sql.Command(connection, query, transaction, ("$id", id));
        using SqliteDataReader reader = command.ExecuteReader();
        Require(reader.Read(), "A required native association is missing.");
        JsonObject result = new();
        for (int index = 0; index < reader.FieldCount; index++)
        {
            result[reader.GetName(index)] = reader.IsDBNull(index)
                ? null : JsonSerializer.SerializeToNode(reader.GetValue(index));
        }
        Require(!reader.Read(), "A required native association is not unique.");
        return result;
    }

    private static JsonObject Metadata(
        SqliteConnection connection, SqliteTransaction transaction, string key
    ) => Object(JsonNode.Parse(Value(connection, transaction, key)
        ?? throw Unknown("Required native settlement metadata is missing.")));

    private static JsonObject Object(JsonNode? value) =>
        value as JsonObject ?? throw Unknown("Required structured native evidence is missing.");

    private static JsonArray Array(JsonNode? value) =>
        value as JsonArray ?? throw Unknown("Required native evidence observations are missing.");

    private static string? Value(
        SqliteConnection connection, SqliteTransaction transaction, string key
    ) => Sql.Scalar(connection, "SELECT value FROM meta WHERE key=$key;",
        transaction, ("$key", key)) as string;

    private static string Text(JsonObject value, string key) =>
        value[key]?.GetValue<string>()
            ?? throw Unknown($"Required evidence field {key} is missing.");

    private static bool SamePath(string first, string second) =>
        Path.GetFullPath(first).Equals(
            Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    internal static string Marker(string id) => $"native:dispatch_contract:{id}";

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw Unknown(message);
        }
    }

    private static OperationException Unknown(string message) => new("credit_unknown", message);
}
