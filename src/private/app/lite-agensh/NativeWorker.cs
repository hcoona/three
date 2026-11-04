using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace LiteAgensh;

internal sealed class NativeWorker : IAsyncDisposable
{
    private readonly RunDatabase database;
    private readonly CreditLedger credits;
    private readonly NativeCreditContract? contract;
    private readonly CreditReservation reservation;
    private readonly ProcessRunner processes;
    private readonly string worker;
    private readonly StringBuilder informationalText = new();
    private readonly object updateLock = new();
    private AcpClient? client;
    private bool capturingInformation;
    private bool verified;
    private decimal lastUsage;
    private bool reconciled;
    private bool started;
    private DateTimeOffset lastStoredUpdate;
    private bool versionVerified;

    public NativeWorker(RunDatabase database, ProcessRunner processes, CreditLedger credits,
        NativeCreditContract? contract, CreditReservation reservation)
    {
        this.database = database;
        this.processes = processes;
        this.credits = credits;
        this.contract = contract;
        this.reservation = reservation;
        worker = reservation.Worker;
    }

    public string? SessionId => client?.SessionId;

    public string ReservationId => reservation.Id;

    public async Task StartAsync(string cwd, string? exactSession, CancellationToken cancellationToken,
        bool informationalDiscovery = false, string? recoveryLabel = null)
    {
        if (contract is null && !informationalDiscovery)
        {
            throw new OperationException("unavailable", "Model-enabled native sessions require a reviewed credit contract.");
        }

        RunConfiguration configuration = database.Configuration;
        ProcessStartInfo start = new(configuration.NativeExecutable) { WorkingDirectory = Path.GetFullPath(cwd) };
        for (int i = 0; i < configuration.NativeArguments.Length; i++)
        {
            string argument = configuration.NativeArguments[i];
            string[] parts = argument.Split('=', 2);
            if (parts[0] is not ("--effort" or "--reasoning-effort"))
            {
                throw new OperationException("invalid_arguments", "Only documented reasoning-effort native options are admitted by this bounded contract.");
            }

            string value = parts.Length == 2 ? parts[1] : ++i < configuration.NativeArguments.Length
                ? configuration.NativeArguments[i] : "";
            if (value is not ("low" or "medium" or "high" or "xhigh" or "max"))
            {
                throw new OperationException("invalid_arguments", "Unsupported documented ACP reasoning-effort value.");
            }

            start.ArgumentList.Add(parts[0]);
            start.ArgumentList.Add(value);
        }

        if (configuration.NativeModel is not null)
        {
            throw new OperationException("unavailable", "This native contract is bound to the existing default model; a model override is not authorized.");
        }

        start.ArgumentList.Add("--acp");
        start.ArgumentList.Add("--stdio");
        start.ArgumentList.Add("--max-ai-credits");
        start.ArgumentList.Add(reservation.Allowance.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--usage-output-file");
        start.ArgumentList.Add(reservation.UsagePath);
        start.ArgumentList.Add("--excluded-tools=task");
        Directory.CreateDirectory(Path.GetDirectoryName(reservation.UsagePath)!);
        if (File.Exists(reservation.UsagePath))
        {
            throw new OperationException("credit_unknown", "A native usage report path cannot be reused.");
        }

        credits.MarkStarted(reservation.Id);
        started = true;
        try
        {
            using (Microsoft.Data.Sqlite.SqliteConnection connection = database.Open())
            {
                Sql.Execute(connection, """
                    INSERT INTO native_attempts(usage_path,reservation_id,worker,baseline_usage,created_at)
                    VALUES($path,$reservation,$worker,$baseline,$time);
                    """, null, ("$path", reservation.UsagePath), ("$reservation", reservation.Id),
                    ("$worker", worker), ("$baseline", (reservation.Actual ?? 0).ToString(CultureInfo.InvariantCulture)),
                    ("$time", database.Now.ToString("O")));
            }

            client = AcpClient.Start(start, processes, $"native:{worker}:{reservation.Id}",
                configuration.NativePermissionPolicy, reservation.UsagePath + ".stderr", RecordUpdate);
            using CancellationTokenSource handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshake.CancelAfter(TimeSpan.FromSeconds(30));
            JsonObject initialize = await client.InitializeAsync(handshake.Token).ConfigureAwait(false);
            using (Microsoft.Data.Sqlite.SqliteConnection connection = database.Open())
            {
                Sql.Execute(connection, "UPDATE native_attempts SET initialize_json=$initialize WHERE usage_path=$path;",
                    null, ("$initialize", initialize.ToJsonString()), ("$path", reservation.UsagePath));
            }

            contract?.VerifyVersion(initialize);
            versionVerified = contract is not null;
            string recovery = recoveryLabel ?? (exactSession is null ? "fresh-session-with-durable-summary" : "exact-session-load");
            if (exactSession is not null && !client.SupportsLoad)
            {
                database.WriteMetadata($"worker:{worker}:recovery", "fresh-summary-fallback-required: loadSession not advertised");
                throw new OperationException("session_load_failed", "Exact-session load is not advertised; settle this process before fresh-summary fallback.");
            }

            try
            {
                await client.OpenSessionAsync(cwd, McpServers(database, worker), exactSession, handshake.Token).ConfigureAwait(false);
            }
            catch (OperationException exception) when (exactSession is not null && exception.Code == "native_error")
            {
                // The caller must settle this failed loader before allocating a fresh replacement.
                database.WriteMetadata($"worker:{worker}:recovery", $"exact-session-load-failed: {exception.Message}");
                throw new OperationException("session_load_failed", "Exact-session loading failed; settle before fresh-summary recovery.");
            }

            credits.AttachSession(reservation.Id, client.SessionId!);
            using (Microsoft.Data.Sqlite.SqliteConnection connection = database.Open())
            {
                Sql.Execute(connection, """
                    INSERT INTO native_sessions(reservation_id,worker,session_id,recovery,initialize_json,created_at)
                    VALUES($reservation,$worker,$session,$recovery,$initialize,$time);
                    """, null, ("$reservation", reservation.Id), ("$worker", worker), ("$session", client.SessionId),
                    ("$recovery", recovery), ("$initialize", initialize.ToJsonString()), ("$time", database.Now.ToString("O")));
                Sql.Execute(connection, "UPDATE native_attempts SET session_id=$session WHERE usage_path=$path;",
                    null, ("$session", client.SessionId), ("$path", reservation.UsagePath));
            }

            database.WriteMetadata($"worker:{worker}:session", client.SessionId!);
            database.WriteMetadata($"worker:{worker}:reservation", reservation.Id);
            database.WriteMetadata($"worker:{worker}:recovery", recovery);
            database.WriteMetadata($"worker:{worker}:initialize", initialize.ToJsonString());
            if (informationalDiscovery)
            {
                await DiscoverInformationalUsageAsync(handshake.Token).ConfigureAwait(false);
                database.WriteMetadata($"worker:{worker}:lifecycle", "preflight-unverified");
                return;
            }

            await VerifyCreditsAsync(handshake.Token).ConfigureAwait(false);
            database.WriteMetadata($"worker:{worker}:lifecycle", "ready");
        }
        catch (Exception exception)
        {
            if (exception is OperationException { Code: "native_start_failed" })
            {
                credits.ReleaseFailedStart(reservation.Id);
                reconciled = true;
                started = false;
                throw;
            }

            credits.MarkUncertain(reservation.Id);
            if (exception is OperationException { Code: "ownership_unresolved" })
            {
                database.WriteMetadata("lifecycle:ownership_ambiguity", exception.Message);
            }

            throw;
        }
    }

    public async Task<JsonObject> PromptAsync(string text, CancellationToken cancellationToken)
    {
        if (!verified || client is null)
        {
            throw new OperationException("credit_unknown", "No model prompt is permitted before verified native limit and usage evidence.");
        }

        if (lastUsage >= reservation.Allowance)
        {
            throw new OperationException("credit_exhausted", "The native session's reported credit allowance is spent.");
        }

        credits.RequirePromptAllowance(database.RunId, reservation.Id, database.Configuration.RunAiCreditAllowance);
        database.WriteMetadata($"worker:{worker}:lifecycle", "prompting");
        JsonObject result;
        try
        {
            result = await client.PromptAsync(text, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            verified = false;
            credits.MarkUncertain(reservation.Id);
            throw;
        }

        database.WriteMetadata($"worker:{worker}:lifecycle", "ready");
        return result;
    }

    public async Task VerifyCreditsAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await VerifyCreditsCoreAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            credits.MarkUncertain(reservation.Id);
            throw new OperationException("credit_unknown", "Native informational credit verification exceeded its finite deadline.");
        }
        catch (OperationException exception) when (exception.Code == "credit_exhausted")
        {
            throw;
        }
        catch
        {
            credits.MarkUncertain(reservation.Id);
            throw;
        }
    }

    private async Task VerifyCreditsCoreAsync(CancellationToken cancellationToken)
    {
        verified = false;
        AcpClient connection = client ?? throw new InvalidOperationException("No ACP connection.");
        NativeCreditContract evidence = contract ?? throw new OperationException("unavailable", "No native credit contract is established.");
        await connection.WaitForCommandsAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        string advertisedName = evidence.InformationalCommand.Split(' ')[0];
        if (!connection.AdvertisedCommands.OfType<JsonObject>().Any(command =>
                command["name"]?.GetValue<string>() == advertisedName))
        {
            throw new OperationException("unavailable", "The documented informational command is not advertised by this ACP session.");
        }

        lock (updateLock)
        {
            informationalText.Clear();
            capturingInformation = true;
        }

        JsonObject response;
        try
        {
            response = await connection.PromptAsync("/" + evidence.InformationalCommand, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (updateLock)
            {
                capturingInformation = false;
            }
        }

        string text;
        lock (updateLock)
        {
            text = informationalText.ToString();
        }

        JsonNode information = text.Length == 0 ? response : JsonNode.Parse(text)
            ?? throw new OperationException("credit_unknown", "The documented informational response did not contain usable JSON.");
        lastUsage = evidence.VerifySession(information, reservation.Allowance);
        if (lastUsage < (reservation.Actual ?? 0))
        {
            throw new OperationException("credit_unknown", "The loaded native session reset or lost previously reported cumulative usage.");
        }

        credits.ObserveUsage(reservation.Id, lastUsage);
        if (lastUsage >= reservation.Allowance)
        {
            throw new OperationException("credit_exhausted", "The native session's reported credit allowance is spent.");
        }

        database.WriteMetadata($"worker:{worker}:credit_preflight", Json.SerializePayload(new
        {
            evidence.EvidenceReference,
            evidence.AgentVersion,
            reservation.Id,
            reservation.Allowance,
            actualReported = lastUsage,
            verifiedAt = database.Now,
        }).ToJsonString());
        verified = true;
    }

    public Task CancelAsync(CancellationToken cancellationToken = default) =>
        client?.CancelAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task StopAsync()
    {
        verified = false;
        if (!started && !reconciled)
        {
            CreditReservation current = credits.Reservations(database.RunId).Single(row => row.Id == reservation.Id);
            if (current.State == "reserved")
            {
                credits.ReleaseUnstarted(reservation.Id);
            }

            reconciled = true;
        }

        if (client is not null)
        {
            try
            {
                await client.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                using Microsoft.Data.Sqlite.SqliteConnection connection = database.Open();
                Sql.Execute(connection, """
                    UPDATE process_leases SET state='unresolved' WHERE kind=$kind AND state<>'exited';
                    """, null, ("$kind", $"native:{worker}:{reservation.Id}"));
                credits.MarkUncertain(reservation.Id);
                throw;
            }
        }

        if (!reconciled)
        {
            try
            {
                NativeCreditContract evidence = contract ??
                    throw new OperationException("credit_unknown", "Native report schema/application evidence remains unverified; reservation retained.");
                if (!versionVerified)
                {
                    throw new OperationException("credit_unknown", "Native report version was not verified; reservation retained.");
                }

                (decimal actual, string identity) = evidence.ReadFinalReport(reservation.UsagePath, SessionId);
                if (SessionId is null && actual != 0)
                {
                    throw new OperationException("credit_unknown", "Native usage before session association was not zero.");
                }

                credits.Reconcile(reservation.Id, actual, identity, unassociatedZero: SessionId is null,
                    processBaseline: evidence.ReportUsageScope == "process-incremental" ? reservation.Actual ?? 0 : null);
                reconciled = true;
            }
            catch
            {
                credits.MarkUncertain(reservation.Id);
                throw;
            }
        }

        database.WriteMetadata($"worker:{worker}:lifecycle", "stopped");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    private void RecordUpdate(JsonObject update)
    {
        lock (updateLock)
        {
            if (database.Now - lastStoredUpdate >= TimeSpan.FromSeconds(1))
            {
                lastStoredUpdate = database.Now;
                database.WriteMetadata($"worker:{worker}:last_update", lastStoredUpdate.ToString("O"));
            }

            if (capturingInformation && update["update"] is JsonObject body &&
                body["sessionUpdate"]?.GetValue<string>() == "agent_message_chunk" &&
                body["content"]?["type"]?.GetValue<string>() == "text")
            {
                informationalText.Append(body["content"]?["text"]?.GetValue<string>());
            }

        }
    }

    private async Task DiscoverInformationalUsageAsync(CancellationToken cancellationToken)
    {
        AcpClient connection = client ?? throw new InvalidOperationException("No ACP connection.");
        await connection.WaitForCommandsAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        database.WriteMetadata($"worker:{worker}:advertised_commands", connection.AdvertisedCommands.ToJsonString());
        if (!connection.AdvertisedCommands.OfType<JsonObject>().Any(command =>
                command["name"]?.GetValue<string>() == "usage"))
        {
            throw new OperationException("unavailable", "The documented informational /usage command is not advertised; no prompt was sent.");
        }

        lock (updateLock)
        {
            informationalText.Clear();
            capturingInformation = true;
        }

        JsonObject response;
        try
        {
            // Official ACP documentation explicitly identifies advertised /usage as non-model.
            response = await connection.PromptAsync("/usage", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (updateLock)
            {
                capturingInformation = false;
            }
        }

        lock (updateLock)
        {
            database.WriteMetadata($"worker:{worker}:credit_discovery", new JsonObject
            {
                ["operation"] = "/usage",
                ["result"] = response,
                ["text"] = informationalText.ToString(),
                ["interpretation"] = "unverified; not used for credit arithmetic or model admission",
            }.ToJsonString());
        }
    }

    private static JsonArray McpServers(RunDatabase database, string worker)
    {
        string executable = Environment.ProcessPath ?? throw new OperationException("unavailable", "Cannot locate the MCP helper executable.");
        string assembly = typeof(RunDatabase).Assembly.Location;
        List<string> arguments = [];
        if (!Path.GetFileNameWithoutExtension(executable).Equals(
                Path.GetFileNameWithoutExtension(assembly), StringComparison.OrdinalIgnoreCase))
        {
            executable = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? executable : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            if (string.IsNullOrEmpty(assembly))
            {
                throw new OperationException("unavailable", "The managed MCP helper entry assembly has no executable location.");
            }

            arguments.Add(Path.GetFullPath(assembly));
        }

        arguments.AddRange(["mcp", "--state", database.StatePath, "--actor", worker]);
        JsonArray args = [];
        foreach (string argument in arguments)
        {
            args.Add(argument);
        }

        return new JsonArray(new JsonObject
        {
            ["name"] = "lite-agensh",
            ["command"] = executable,
            ["args"] = args,
            ["env"] = new JsonArray(),
        });
    }
}
