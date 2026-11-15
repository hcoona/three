using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace LiteAgensh.Tests.Support;

// Controlled dependency persistence, NOT an application ledger or native proof.
// The creator and reconnect have disjoint process lifetimes. Records are flushed
// at the operations which produce them; the checkpoint pins the original host.
internal sealed class Phase7Journal(string path)
{
    private readonly object gate = new();
    internal string? PinnedRuntime { get; set; }

    private JsonObject Read() =>
        File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path))!.AsObject()
            : new() { ["version"] = 1, ["sessions"] = new JsonObject() };

    private void Save(JsonObject document)
    {
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, document, Json.Options);
        stream.Flush(flushToDisk: true);
    }

    internal JsonObject Snapshot(string session)
    {
        lock (gate)
        {
            return Read()["sessions"]![session]!.DeepClone().AsObject();
        }
    }

    internal (double Nano, long Requests) Open(
        JsonObject metadata,
        string worker,
        string? exact,
        DateTimeOffset now
    )
    {
        lock (gate)
        {
            JsonObject document = Read();
            JsonObject sessions = document["sessions"]!.AsObject();
            string id = metadata["sessionId"]!.GetValue<string>();
            JsonObject state;
            if (exact is null)
            {
                if (sessions.ContainsKey(id))
                {
                    throw new InvalidOperationException("Fresh dependency session already exists.");
                }
                state = new()
                {
                    ["sessionId"] = id,
                    ["workingDirectory"] = metadata["workingDirectory"]!.DeepClone(),
                    ["model"] = metadata["selectedModel"]!.DeepClone(),
                    ["nano"] = 0d,
                    ["requests"] = 0L,
                    ["pending"] = false,
                    ["generations"] = new JsonArray(),
                };
                sessions.Add(id, state);
            }
            else
            {
                state =
                    sessions[id]?.AsObject()
                    ?? throw new OperationException(
                        "session_load_failed",
                        "Controlled session is absent."
                    );
                JsonObject original = state["generations"]!
                    .AsArray()
                    .Select(item => item!.AsObject())
                    .Single(item => item["runtimeId"]!.GetValue<string>() == PinnedRuntime);
                if (
                    exact != id
                    || state["pending"]!.GetValue<bool>()
                    || original["normalHostExit"]?.GetValue<bool>() != true
                    || original["wrapperDisposed"]?.GetValue<bool>() != true
                    || !JsonNode.DeepEquals(state["workingDirectory"], metadata["workingDirectory"])
                    || !JsonNode.DeepEquals(state["model"], metadata["selectedModel"])
                )
                {
                    throw new OperationException(
                        "credit_unknown",
                        "Exact controlled history is not eligible for pending-free continuation."
                    );
                }
            }
            string runtime = metadata["runtimeId"]!.GetValue<string>();
            JsonObject generation = metadata.DeepClone().AsObject();
            generation["worker"] = worker;
            generation["openedAt"] = now;
            generation["openingNano"] = state["nano"]!.DeepClone();
            generation["openingRequests"] = state["requests"]!.DeepClone();
            generation["exactSession"] = exact;
            generation["events"] = new JsonArray();
            generation["dispatches"] = new JsonArray();
            generation["normalHostExit"] = false;
            generation["wrapperDisposed"] = false;
            state["generations"]!.AsArray().Add(generation);
            AddEvent(
                generation,
                new SessionStartEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = now,
                    Data = new()
                    {
                        Producer = "controlled-session-events",
                        Version = 1,
                        SessionId = id,
                        CopilotVersion = Phase6NativeControl.RuntimeVersion,
                        AlreadyInUse = false,
                        StartTime = now,
                    },
                }
            );
            Save(document);
            Console.WriteLine("PHASE7_JOURNAL_OPEN " + generation.ToJsonString());
            return (state["nano"]!.GetValue<double>(), state["requests"]!.GetValue<long>());
        }
    }

    private static JsonObject Generation(JsonObject state, string runtime) =>
        state["generations"]!
            .AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["runtimeId"]!.GetValue<string>() == runtime);

    private static void AddEvent(JsonObject generation, SessionEvent value) =>
        generation["events"]!
            .AsArray()
            .Add(
                new JsonObject
                {
                    ["kind"] = value.GetType().Name,
                    ["value"] = JsonSerializer.SerializeToNode(
                        value,
                        value.GetType(),
                        Json.Options
                    ),
                }
            );

    internal void Dispatch(string session, string runtime, string text, DateTimeOffset now)
    {
        lock (gate)
        {
            JsonObject document = Read(),
                state = document["sessions"]![session]!.AsObject();
            if (state["pending"]!.GetValue<bool>())
            {
                throw new InvalidOperationException("Controlled pending send would be replayed.");
            }
            state["pending"] = true;
            Generation(state, runtime)["dispatches"]!
                .AsArray()
                .Add(
                    new JsonObject
                    {
                        ["text"] = text,
                        ["at"] = now,
                        ["terminal"] = false,
                    }
                );
            Save(document);
        }
    }

    internal void Complete(
        string session,
        string runtime,
        NativeUsage usage,
        JsonObject response,
        DateTimeOffset now
    )
    {
        lock (gate)
        {
            JsonObject document = Read(),
                state = document["sessions"]![session]!.AsObject();
            JsonObject generation = Generation(state, runtime);
            JsonObject dispatch = generation["dispatches"]!.AsArray().Last()!.AsObject();
            if (!state["pending"]!.GetValue<bool>() || dispatch["terminal"]!.GetValue<bool>())
            {
                throw new InvalidOperationException(
                    "No actual pending controlled send to complete."
                );
            }
            dispatch["terminal"] = true;
            dispatch["response"] = response.DeepClone();
            dispatch["completedAt"] = now;
            state["pending"] = false;
            state["nano"] = usage.TotalNanoAiu;
            state["requests"] = usage.TotalUserRequests;
            AddEvent(
                generation,
                new AssistantUsageEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = now,
                    Data = new()
                    {
                        Model = "controlled-model",
                        Accounting = new()
                        {
                            SourceSessionId = session,
                            Sequence = usage.TotalUserRequests,
                            UsageId = Guid.NewGuid().ToString("N"),
                        },
                    },
                }
            );
            Save(document);
        }
    }

#pragma warning disable GHCP001 // Selected supported typed accounting/history contracts.
    internal void NormalHostExit(
        string session,
        string runtime,
        JsonObject witness,
        DateTimeOffset now
    )
    {
        lock (gate)
        {
            JsonObject document = Read(),
                state = document["sessions"]![session]!.AsObject();
            JsonObject generation = Generation(state, runtime);
            generation["normalHostExit"] = true;
            generation["hostExit"] = witness.DeepClone();
            // A process exit is not complete accounting for an unfinished Send.
            if (!state["pending"]!.GetValue<bool>())
            {
                AddEvent(
                    generation,
                    new SessionShutdownEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = now,
                        Data = new()
                        {
                            CodeChanges = new()
                            {
                                FilesModified = [],
                                LinesAdded = 0,
                                LinesRemoved = 0,
                            },
                            ModelMetrics = new Dictionary<string, ShutdownModelMetric>(),
                            TotalApiDuration = TimeSpan.Zero,
                            SessionStartTime = generation["openedAt"]!
                                .GetValue<DateTimeOffset>()
                                .ToUnixTimeMilliseconds(),
                            ShutdownType = ShutdownType.Routine,
                            TotalNanoAiu = state["nano"]!.GetValue<double>(),
                            UsageAccountingWatermarks = new Dictionary<string, long>
                            {
                                [session] = state["requests"]!.GetValue<long>(),
                            },
                        },
                    }
                );
            }
            Save(document);
        }
#pragma warning restore GHCP001
    }

    internal void WrapperDisposed(string session, string runtime)
    {
        lock (gate)
        {
            JsonObject document = Read();
            Generation(document["sessions"]![session]!.AsObject(), runtime)["wrapperDisposed"] =
                true;
            Save(document);
        }
    }

#pragma warning disable GHCP001 // Selected supported typed persisted-page contract.
    internal Task<OriginalNativeShutdown> ReadOriginalAsync(
        NativeRecoveryOrigin origin,
        CancellationToken token
    )
    {
        JsonObject original = Generation(Snapshot(origin.SessionId), origin.RuntimeId);
        if (
            PinnedRuntime != origin.RuntimeId
            || original["normalHostExit"]?.GetValue<bool>() != true
            || original["wrapperDisposed"]?.GetValue<bool>() != true
        )
        {
            throw new OperationException(
                "credit_unknown",
                "Original controlled runtime is not settled."
            );
        }
        JsonArray events = original["events"]!.AsArray();
        return NativeSdkSession.ReadOriginalShutdownAsync(
            origin,
            (cursor, next) =>
            {
                next.ThrowIfCancellationRequested();
                int index = cursor is null ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);
                JsonObject record = events[index]!.AsObject();
                string json = record["value"]!.ToJsonString();
                SessionEvent value = record["kind"]!.GetValue<string>() switch
                {
                    nameof(SessionStartEvent) => JsonSerializer.Deserialize<SessionStartEvent>(
                        json,
                        Json.Options
                    )!,
                    nameof(AssistantUsageEvent) => JsonSerializer.Deserialize<AssistantUsageEvent>(
                        json,
                        Json.Options
                    )!,
                    nameof(SessionShutdownEvent) =>
                        JsonSerializer.Deserialize<SessionShutdownEvent>(json, Json.Options)!,
                    _ => throw new OperationException(
                        "credit_unknown",
                        "Unsupported controlled event."
                    ),
                };
                return Task.FromResult(
                    new EventsReadResult
                    {
                        CursorStatus = EventsCursorStatus.Ok,
                        Cursor = (index + 1).ToString(CultureInfo.InvariantCulture),
                        Events = [value],
                        HasMore = index + 1 < events.Count,
                    }
                );
            },
            token
        );
    }
#pragma warning restore GHCP001
}
