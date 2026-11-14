using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace LiteAgensh;

internal interface INativeSdkSession : IAsyncDisposable
{
    string SessionId { get; }

    bool HasSession { get; }

    string? NativeFailure { get; }

    Exception? FirstFailure => null;

    IDisposable? ObserveRequest(CancellationToken cancellationToken) => null;

    void MarkDispatchPending() { }

    JsonObject? Metadata { get; }

    Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
        NativeRecoveryOrigin origin, string evidencePath, CancellationToken cancellationToken
    );

    Task OpenAsync(
        string worker, string sessionId, decimal? limit, string? exactSession,
        string evidencePath, CancellationToken cancellationToken
    );

    Task<NativeUsage> ReadAsync(
        string expectedSession, decimal? expectedLimit, CancellationToken cancellationToken,
        bool validateEvents = true
    );

    Task<NativeUsage> ReadSettledAsync(
        string expectedSession, decimal? expectedLimit, CancellationToken cancellationToken,
        bool validateEvents = true, bool verifyLocalWork = false
    );

    void WithSettledUsage(NativeUsage usage, Action record);

    Task<NativeUsage> UpdateLimitAsync(
        string expectedSession, decimal limit, CancellationToken cancellationToken
    );

    Task<JsonObject> SendAsync(
        string text, TimeSpan timeout, CancellationToken cancellationToken,
        Func<CancellationToken, Task<decimal>>? renewLimit = null,
        WorkAdmission? admission = null
    );

    Task AbortAsync(CancellationToken cancellationToken);

    void Quiesce();

    void RequireHealthy();
}

// These pinned, typed native metadata/limits/usage APIs are experimental in SDK 1.0.28.
#pragma warning disable GHCP001
internal sealed class NativeSdkSession : INativeSdkSession
{
    private const int ProbeTimeoutSeconds = 30;
    private readonly RunDatabase database;
    private readonly NativeSdkHost host;
    private readonly bool ownsHost;
    private readonly string cwd;
    private readonly bool isolated;
    private readonly object callbacks = new();
    private readonly NativeUsageFence usageFence = new();
    private readonly CancellationTokenSource eventStop = new();
    private CopilotClient? client;
    private CopilotSession? session;
    private bool acceptingCallbacks = true;
    private string? eventFailure;
    private Exception? firstFailure;
    private CancellationToken? sendCancellation;
    private string? actualRuntimeVersion;
    private string? recoveryEvidencePath;
    private bool disposed;
    private Channel<SessionLimitsExhaustedRequestedData>? limitRequests;

    public NativeSdkSession(
        RunDatabase database,
        ProcessRunner processes,
        string cwd,
        bool isolated
    )
        : this(database, new NativeSdkHost(database, processes), cwd, isolated)
    {
        ownsHost = true;
    }

    public NativeSdkSession(
        RunDatabase database,
        NativeSdkHost host,
        string cwd,
        bool isolated
    )
    {
        this.database = database;
        this.host = host;
        this.cwd = Path.GetFullPath(cwd);
        this.isolated = isolated;
    }

    public static string RuntimeVersion =>
        typeof(NativeSdkSession)
            .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "CopilotCliVersion")
            .Value
        ?? throw new InvalidOperationException("The SDK runtime pin is missing.");

    public static string SdkVersion =>
        typeof(CopilotClient)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? throw new InvalidOperationException("The SDK package identity is missing.");

    public string SessionId =>
        session?.SessionId ?? throw new InvalidOperationException("No native session.");

    public bool HasSession => session is not null;

    public string? NativeFailure
    {
        get
        {
            lock (callbacks)
            {
                return eventFailure;
            }
        }
    }

    public Exception? FirstFailure
    {
        get
        {
            lock (callbacks)
            {
                return firstFailure;
            }
        }
    }

    public JsonObject? Metadata { get; private set; }

    public async Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
        NativeRecoveryOrigin origin, string evidencePath, CancellationToken cancellationToken
    )
    {
        if (HasSession)
        {
            throw new OperationException("credit_unknown",
                "Original native history must be checked before exact session activation.");
        }
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));
        client = await host.GetClientAsync(cwd, evidencePath, deadline.Token).ConfigureAwait(false);
        OriginalNativeShutdown original = await ReadOriginalShutdownAsync(
            origin,
            (cursor, token) => client.Rpc.Sessions.ReadPersistedEventsAsync(
                origin.SessionId, cursor, max: 200, direction: EventsReadDirection.Forward,
                cancellationToken: token),
            deadline.Token
        ).ConfigureAwait(false);
        using FileStream evidence = new(
            evidencePath + ".original-shutdown.json", FileMode.CreateNew,
            FileAccess.Write, FileShare.Read);
        await System.Text.Json.JsonSerializer.SerializeAsync(
            evidence, original.Evidence(origin), cancellationToken: deadline.Token
        ).ConfigureAwait(false);
        evidence.Flush(flushToDisk: true);
        recoveryEvidencePath = evidencePath + ".recovery-usage.jsonl";
        return original;
    }

    internal static async Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
        NativeRecoveryOrigin origin,
        Func<string?, CancellationToken, Task<EventsReadResult>> read,
        CancellationToken cancellationToken
    )
    {
        SessionStartEvent? start = null;
        SessionShutdownEvent? shutdown = null;
        Dictionary<string, long> receipts = new(StringComparer.Ordinal);
        HashSet<string> cursors = new(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cursor is not null && !cursors.Add(cursor))
            {
                throw new OperationException("credit_unknown", "Native history cursor was reused.");
            }
            EventsReadResult page = await read(cursor, cancellationToken)
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            if (page.CursorStatus != EventsCursorStatus.Ok
                || string.IsNullOrWhiteSpace(page.Cursor))
            {
                throw new OperationException("credit_unknown",
                    "Original native history is expired, incomplete or unsupported.");
            }
            foreach (SessionEvent update in page.Events)
            {
                if (update.Timestamp < origin.AssociationTime
                    || update.Timestamp > origin.SegmentEndTime)
                {
                    continue;
                }
                if (update is SessionStartEvent opening)
                {
                    if (start is not null || shutdown is not null
                        || opening.Id == Guid.Empty
                        || opening.Data.SessionId != origin.SessionId
                        || opening.Data.CopilotVersion != origin.RuntimeVersion
                        || opening.Data.AlreadyInUse == true
                        || opening.Data.StartTime < origin.RuntimeStartTime
                        || opening.Data.StartTime < origin.AssociationTime
                        || opening.Data.StartTime > opening.Timestamp)
                    {
                        throw new OperationException("credit_unknown",
                            "Original native session startup disagrees with its durable lifetime.");
                    }
                    start = opening;
                }
                else if (update is SessionShutdownEvent terminal)
                {
                    if (start is null || shutdown is not null
                        || terminal.Id == Guid.Empty
                        || terminal.Timestamp < start.Timestamp
                        || terminal.Data.SessionStartTime
                            != start.Data.StartTime.ToUnixTimeMilliseconds()
                        || terminal.Data.ShutdownType != ShutdownType.Routine
                        || terminal.Data.TotalNanoAiu is not double total
                        || !double.IsFinite(total) || total < 0
                        || terminal.Data.UsageAccountingWatermarks is not { Count: > 0 } marks
                        || marks.Any(mark => string.IsNullOrWhiteSpace(mark.Key) || mark.Value < 0)
                        || receipts.Any(receipt =>
                            !marks.TryGetValue(receipt.Key, out long frontier)
                            || frontier < receipt.Value))
                    {
                        throw new OperationException("credit_unknown",
                            "Original routine termination or accounting frontier is invalid.");
                    }
                    shutdown = terminal;
                }
                else if (shutdown is null && (update is AssistantUsageEvent
                    || update is ToolExecutionCompleteEvent { Data.ToolUsage: not null }))
                {
                    UsageAccountingIdentity? identity = update is AssistantUsageEvent receipt
                        ? receipt.Data.Accounting
                        : ((ToolExecutionCompleteEvent)update).Data.ToolUsage!.Accounting;
                    if (identity is null
                        || string.IsNullOrWhiteSpace(identity.SourceSessionId)
                        || string.IsNullOrWhiteSpace(identity.UsageId) || identity.Sequence < 0)
                    {
                        throw new OperationException("credit_unknown",
                            "Original native usage receipt lacks a subsumed accounting identity.");
                    }
                    receipts[identity.SourceSessionId] = Math.Max(identity.Sequence,
                        receipts.GetValueOrDefault(identity.SourceSessionId));
                }
                else if (update is SessionResumeEvent)
                {
                    throw new OperationException("credit_unknown",
                        "A resumed session cannot substitute for the original native lifetime.");
                }
                else if (shutdown is not null && update is UserMessageEvent
                    or AssistantTurnStartEvent or ToolExecutionStartEvent
                    or SessionLimitsExhaustedRequestedEvent)
                {
                    throw new OperationException("credit_unknown",
                        "New native work followed the selected original termination.");
                }
            }
            if (!page.HasMore)
            {
                break;
            }
            cursor = page.Cursor;
        } while (true);

        if (shutdown is null)
        {
            throw new OperationException("credit_unknown",
                "Matching original routine native termination is missing.");
        }
        return new(shutdown.Id, shutdown.Timestamp, shutdown.Data.SessionStartTime,
            shutdown.Data.TotalNanoAiu!.Value,
            new Dictionary<string, long>(shutdown.Data.UsageAccountingWatermarks!,
                StringComparer.Ordinal));
    }

    public async Task OpenAsync(
        string worker,
        string sessionId,
        decimal? limit,
        string? exactSession,
        string evidencePath,
        CancellationToken cancellationToken
    )
    {
        client = await host
            .GetClientAsync(cwd, evidencePath, cancellationToken)
            .ConfigureAwait(false);
        host.Associate(worker, sessionId, cwd);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        actualRuntimeVersion = host.ActualRuntimeVersion;

        SessionConfigBase settings = exactSession is null
            ? new SessionConfig { SessionId = sessionId }
            : new ResumeSessionConfig
            {
                ContinuePendingWork = false,
                AllowTranscriptRecovery = false,
            };
        settings.WorkingDirectory = cwd;
        settings.Model = database.Configuration.NativeModel;
        settings.ReasoningEffort = database.Configuration.GetReasoningEffort();
        settings.SessionLimits = limit is null
            ? null
            : new SessionLimitsConfig { MaxAiCredits = NativeLimit(limit.Value) };
        settings.AvailableTools = isolated ? [] : new ToolSet().AddBuiltIn("*").AddMcp("*");
        settings.ExcludedTools = new ToolSet().AddBuiltIn("task");
        settings.EnableConfigDiscovery = false;
        settings.SkipCustomInstructions = isolated;
        settings.EnableOnDemandInstructionDiscovery = !isolated;
        settings.EnableHostGitOperations = !isolated;
        settings.EnableFileHooks = false;
        settings.EnableSkills = false;
        settings.EnableSessionStore = false;
        settings.CustomAgents = [];
        settings.AgentDirectories = [];
        settings.PluginDirectories = [];
        settings.HookDirectories = [];
        settings.SkillDirectories = [];
        settings.IncludedBuiltinSkills = [];
        settings.OrganizationCustomInstructions = "";
        settings.Tools = [];
        settings.McpServers = isolated ? [] : McpServers(database, worker, cwd);
        settings.OnPermissionRequest = PermissionAsync;
        settings.OnEvent = RecordEvent;
        try
        {
            session = settings is SessionConfig create
                ? await client.CreateSessionAsync(create, deadline.Token).ConfigureAwait(false)
                : await client
                    .ResumeSessionAsync(
                        exactSession!,
                        (ResumeSessionConfig)settings,
                        deadline.Token
                    )
                    .ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exactSession is not null && exception is not OperationCanceledException)
        {
            throw new OperationException(
                "session_load_failed",
                $"Exact native session resume failed: {exception.Message}"
            );
        }
    }

    public async Task<NativeUsage> ReadAsync(
        string expectedSession,
        decimal? expectedLimit,
        CancellationToken cancellationToken,
        bool validateEvents = true
    )
    {
        CopilotSession active =
            session ?? throw new InvalidOperationException("No native session.");
        string phase = "metadata";
        return await ReadProbeAsync(async probe =>
        {
            SessionMetadataSnapshot metadata = await active
                .Rpc.Metadata.SnapshotAsync(probe)
                .ConfigureAwait(false);
            ValidateMetadata(
                metadata,
                active.SessionId,
                expectedSession,
                cwd,
                expectedLimit,
                database.Configuration.NativeModel
            );

            phase = "usage";
            UsageGetMetricsResult usage = await active
                .Rpc.Usage.GetMetricsAsync(probe)
                .ConfigureAwait(false);
            Metadata = new()
            {
                ["sessionId"] = metadata.SessionId,
                ["workingDirectory"] = metadata.WorkingDirectory,
                ["appliedLimit"] = metadata.SessionLimits?.MaxAiCredits,
                ["selectedModel"] = metadata.SelectedModel,
                ["alreadyInUse"] = metadata.AlreadyInUse,
                ["isRemote"] = metadata.IsRemote,
                ["runtimeVersion"] = actualRuntimeVersion,
                ["sdkVersion"] = SdkVersion,
                ["runtimeId"] = host.Id,
                ["runtimeLeaseId"] = host.LeaseId,
            };
            if (validateEvents)
            {
                CheckEvents(CancellationToken.None);
            }
            NativeUsage reading = new(
                expectedSession,
                usage.TotalNanoAiu,
                usage.AiCreditsStatus?.Value,
                usage.TotalUserRequests
            );
            if (recoveryEvidencePath is not null)
            {
                JsonObject evidence = new()
                {
                    ["sessionId"] = reading.SessionId,
                    ["reportedNanoAiu"] = reading.TotalNanoAiu?.ToString(
                        "R", System.Globalization.CultureInfo.InvariantCulture),
                    ["aiCreditsStatus"] = reading.Status,
                    ["totalUserRequests"] = reading.TotalUserRequests,
                    ["metadata"] = Metadata.DeepClone(),
                    ["evidenceLevel"] = "fresh-native-reading-not-yet-settled",
                };
                await File.AppendAllTextAsync(recoveryEvidencePath,
                    evidence.ToJsonString() + Environment.NewLine, probe)
                    .ConfigureAwait(false);
            }
            return reading;
        }, cancellationToken, () => phase).ConfigureAwait(false);
    }

    internal static async Task<NativeUsage> ReadProbeAsync(
        Func<CancellationToken, Task<NativeUsage>> read,
        CancellationToken cancellationToken,
        Func<string>? phase = null,
        TimeProvider? timeProvider = null
    )
    {
        using CancellationTokenSource timeout = new(
            TimeSpan.FromSeconds(ProbeTimeoutSeconds), timeProvider ?? TimeProvider.System);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);
        try
        {
            return await read(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            string query = phase?.Invoke() ?? "metadata";
            throw new OperationException(
                "native_probe_failed",
                deadline.IsCancellationRequested
                    ? $"Native {query} query exceeded its {ProbeTimeoutSeconds}-second deadline."
                    : $"Native {query} query was canceled before caller cancellation."
            );
        }
        catch (OperationCanceledException exception) when (
            exception.CancellationToken == deadline.Token
            && cancellationToken.IsCancellationRequested
        )
        {
            throw new RequestedReadCanceledException(exception, cancellationToken);
        }
    }

    internal sealed class RequestedReadCanceledException(
        OperationCanceledException cause, CancellationToken requestToken
    ) : OperationCanceledException(cause.Message, cause, cause.CancellationToken)
    {
        public CancellationToken RequestToken { get; } = requestToken;
    }

    public async Task<NativeUsage> ReadSettledAsync(
        string expectedSession, decimal? expectedLimit, CancellationToken cancellationToken,
        bool validateEvents = true, bool verifyLocalWork = false
    )
    {
        // Keep callback provenance on the request, not the local usage-query deadline.
        using IDisposable observation = ObserveSend(cancellationToken);
        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(database.Configuration.DurationSeconds));
        return await CaptureUsageAsync(
            token => verifyLocalWork
                ? ReadIdleRecoveryUsageAsync(
                    next => ReadAsync(expectedSession, expectedLimit, next, validateEvents),
                    async next =>
                    {
                        CopilotSession active = session
                            ?? throw new InvalidOperationException("No native session.");
                        SessionMetadataSnapshot metadata =
                            await active.Rpc.Metadata.SnapshotAsync(next).ConfigureAwait(false);
                        ValidateMetadata(metadata, active.SessionId, expectedSession,
                            cwd, expectedLimit, database.Configuration.NativeModel);
                        RequireIdleRecoveryWork(
                            await active.Rpc.Metadata.IsProcessingAsync(next).ConfigureAwait(false),
                            await active.Rpc.Metadata.ActivityAsync(next).ConfigureAwait(false));
                    },
                    token)
                : ReadAsync(expectedSession, expectedLimit, token, validateEvents),
            deadline.Token
        ).ConfigureAwait(false);
    }

    internal static async Task<NativeUsage> ReadIdleRecoveryUsageAsync(
        Func<CancellationToken, Task<NativeUsage>> read,
        Func<CancellationToken, Task> requireIdle,
        CancellationToken cancellationToken
    )
    {
        await requireIdle(cancellationToken).ConfigureAwait(false);
        NativeUsage usage = await read(cancellationToken).ConfigureAwait(false);
        await requireIdle(cancellationToken).ConfigureAwait(false);
        return usage;
    }

    internal static void RequireIdleRecoveryWork(
        MetadataIsProcessingResult processing, SessionActivity activity)
    {
        if (processing.Processing || activity.HasActiveWork || activity.Abortable)
        {
            throw new OperationException("credit_unknown",
                "New native recovery boundary still has processing or active work.");
        }
    }

    public void WithSettledUsage(NativeUsage usage, Action record) => usageFence.Use(usage, record);

    internal Task<NativeUsage> CaptureUsageAsync(
        Func<CancellationToken, Task<NativeUsage>> read, CancellationToken cancellationToken
    ) => usageFence.CaptureAsync(read, cancellationToken);

    internal static void ValidateMetadata(
        SessionMetadataSnapshot metadata,
        string activeSessionId,
        string expectedSession,
        string cwd,
        decimal? expectedLimit,
        string? expectedModel = null
    )
    {
        if (
            metadata.SessionId != expectedSession
            || activeSessionId != expectedSession
            || !Path.GetFullPath(metadata.WorkingDirectory)
                .TrimEnd(Path.DirectorySeparatorChar)
                .Equals(
                    cwd.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase
                )
            || metadata.AlreadyInUse
            || metadata.IsRemote
            || (
                expectedModel is not null
                && !expectedModel.Equals(metadata.SelectedModel, StringComparison.OrdinalIgnoreCase)
            )
            || (
                expectedLimit is decimal limit
                && metadata.SessionLimits?.MaxAiCredits != NativeLimit(limit)
            )
        )
        {
            throw new OperationException(
                "credit_unknown",
                "Native identity, working directory, ownership, model or applied limit mismatched."
            );
        }
    }

    public async Task<NativeUsage> UpdateLimitAsync(
        string expectedSession,
        decimal limit,
        CancellationToken cancellationToken
    )
    {
        CopilotSession active =
            session ?? throw new InvalidOperationException("No native session.");
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));
        await active.Rpc.Options.UpdateAsync(
            sessionLimits: new SessionLimitsConfig { MaxAiCredits = NativeLimit(limit) },
            cancellationToken: deadline.Token
        ).ConfigureAwait(false);
        return await ReadAsync(expectedSession, limit, deadline.Token).ConfigureAwait(false);
    }

    public async Task<JsonObject> SendAsync(
        string text,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<decimal>>? renewLimit = null,
        WorkAdmission? admission = null
    )
    {
        using IDisposable observation = ObserveSend(cancellationToken);
        CheckEvents(cancellationToken);
        CopilotSession active =
            session ?? throw new InvalidOperationException("No native session.");
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            eventStop.Token
        );
        Channel<SessionLimitsExhaustedRequestedData>? requests = renewLimit is null
            ? null
            : Channel.CreateBounded<SessionLimitsExhaustedRequestedData>(
                new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true }
            );
        lock (callbacks)
        {
            limitRequests = requests;
        }
        Task<AssistantMessageEvent?>? sending = null;
        Task<SessionLimitsExhaustedRequestedData>? pending = null;
        return await CompleteSendAsync(async () =>
        {
            sending = StartSend(
                () => active.SendAndWaitAsync(text, timeout, linked.Token), admission);
            while (requests is not null && !sending.IsCompleted)
            {
                pending = requests.Reader.ReadAsync(linked.Token).AsTask();
                if (await Task.WhenAny(sending, pending).ConfigureAwait(false) == sending)
                {
                    break;
                }

                SessionLimitsExhaustedRequestedData request = await pending.ConfigureAwait(false);
                await HandleExhaustedLimitAsync(
                    request.RequestId,
                    renewLimit!,
                    active.Rpc.Ui.HandlePendingSessionLimitsExhaustedAsync,
                    async (limit, token) =>
                    {
                        await ReadAsync(SessionId, limit, token).ConfigureAwait(false);
                    },
                    linked.Token,
                    admission
                ).ConfigureAwait(false);
            }
            AssistantMessageEvent? response = await sending.ConfigureAwait(false);
            CheckEvents(cancellationToken);
            return new() { ["stopReason"] = "end_turn", ["text"] = response?.Data.Content };
        }, () => AbortAsync(CancellationToken.None), async () =>
        {
            lock (callbacks)
            {
                limitRequests = null;
            }
            await linked.CancelAsync().ConfigureAwait(false);
            if (sending is not null)
            {
                await SettleSendAsync(sending).ConfigureAwait(false);
            }
            if (pending is not null)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    internal Task<AssistantMessageEvent?> StartSend(
        Func<Task<AssistantMessageEvent?>> send, WorkAdmission? admission
    )
    {
        NativeWorker.CheckRenewalAdmission(admission);
        MarkDispatchPending();
        return send();
    }

    public void MarkDispatchPending() => usageFence.SetTurnPending(true);

    internal async Task CompleteTerminalAsync(Task sending)
    {
        await sending.ConfigureAwait(false);
        usageFence.SetTurnPending(false);
    }

    internal async Task SettleSendAsync(Task sending)
    {
        try
        {
            await CompleteTerminalAsync(sending).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The send's original failure is preserved above.
        }
    }

    internal async Task<JsonObject> CompleteSendAsync(
        Func<Task<JsonObject>> send, Func<Task> abort, Func<Task> cleanup,
        CancellationToken cancellationToken
    )
    {
        using IDisposable observation = ObserveSend(cancellationToken);
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RetainFailure(exception, cancellationToken);
            await AbortFailedSendAsync(abort, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await cleanup().ConfigureAwait(false);
        }
    }

    internal static async Task HandleExhaustedLimitAsync(
        string requestId,
        Func<CancellationToken, Task<decimal>> renewLimit,
        Func<string, UISessionLimitsExhaustedResponse, CancellationToken, Task> apply,
        Func<decimal, CancellationToken, Task> verify,
        CancellationToken cancellationToken,
        WorkAdmission? admission = null
    )
    {
        NativeWorker.CheckRenewalAdmission(admission);
        using CancellationTokenSource renewing = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, admission?.Token ?? CancellationToken.None
        );
        decimal limit = await renewLimit(renewing.Token).ConfigureAwait(false);
        NativeWorker.CheckRenewalAdmission(admission);
        try
        {
            await apply(
                requestId,
                new UISessionLimitsExhaustedResponse
                {
                    Action = UISessionLimitsExhaustedResponseAction.Set,
                    MaxAiCredits = NativeLimit(limit),
                },
                renewing.Token
            ).ConfigureAwait(false);
            await verify(limit, renewing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (
            admission?.Token.IsCancellationRequested == true
            && !cancellationToken.IsCancellationRequested
            && (exception.CancellationToken == renewing.Token
                || exception is RequestedReadCanceledException read
                    && read.RequestToken == renewing.Token)
        )
        {
            throw new NativeWorker.RenewalAdmissionCanceledException(exception);
        }
    }

    public IDisposable ObserveRequest(CancellationToken cancellationToken) =>
        ObserveSend(cancellationToken);

    internal IDisposable ObserveSend(CancellationToken cancellationToken)
    {
        lock (callbacks)
        {
            SendObservation observation = new(this, sendCancellation);
            sendCancellation = cancellationToken;
            return observation;
        }
    }

    private void RetainFailure(Exception failure, CancellationToken? cancellationToken)
    {
        lock (callbacks)
        {
            if (firstFailure is not null
                || failure is NativeWorker.RenewalAdmissionCanceledException
                || (failure is OperationCanceledException
                    && cancellationToken?.IsCancellationRequested == true))
            {
                return;
            }
            if (failure.Data["liteagensh.nativeFailureObserved"] is not long)
            {
                if (cancellationToken is { } supplied)
                {
                    failure.Data["liteagensh.nativeFailureCancelled"] =
                        supplied.IsCancellationRequested;
                }
                failure.Data["liteagensh.nativeFailureObserved"] = Stopwatch.GetTimestamp();
            }
            firstFailure = failure;
        }
    }

    private sealed class SendObservation(
        NativeSdkSession owner, CancellationToken? previous
    ) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.callbacks)
            {
                owner.sendCancellation = previous;
            }
        }
    }

    internal async Task AbortFailedSendAsync(
        Func<Task> abort,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await abort().ConfigureAwait(false);
        }
        finally
        {
            CheckEvents(cancellationToken);
        }
    }

    public async Task AbortAsync(CancellationToken cancellationToken)
    {
        if (session is not null && !disposed)
        {
            using CancellationTokenSource deadline =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await session.AbortAsync(deadline.Token).ConfigureAwait(false);
        }
    }

    public void Quiesce()
    {
        lock (callbacks)
        {
            acceptingCallbacks = false;
        }
    }

    public void RequireHealthy() => CheckEvents();

    internal static double NativeLimit(decimal allowance)
    {
        double result = (double)allowance;
        try
        {
            if ((decimal)result > allowance)
            {
                result = Math.BitDecrement(result);
            }
        }
        catch (OverflowException)
        {
            result = Math.BitDecrement(result);
        }

        if (!double.IsFinite(result) || result <= 0)
        {
            throw new OperationException("credit_unknown", "Native credit window is invalid.");
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Quiesce();

        try
        {
            if (session is not null)
            {
                await session
                    .DisposeAsync()
                    .AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownsHost)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private Task<PermissionDecision> PermissionAsync(
        PermissionRequest request,
        PermissionInvocation invocation
    )
    {
        lock (callbacks)
        {
            bool approve =
                acceptingCallbacks
                && !isolated
                && database.Configuration.NativePermissionPolicy == "trusted"
                && request.ManagedApprovalRequired != true
                && request
                    is PermissionRequestShell
                        or PermissionRequestRead
                        or PermissionRequestWrite
                        or PermissionRequestMcp
                        or PermissionRequestUrl;
            return Task.FromResult<PermissionDecision>(
                approve
                    ? new PermissionDecisionApproveOnce { ApprovedInteractively = false }
                    : new PermissionDecisionReject()
            );
        }
    }

    internal void RecordEvent(SessionEvent update)
    {
        lock (callbacks)
        {
            // Permission/tool handlers stop at quiescence; late work still invalidates usage.
            usageFence.Observe(update);
            if (!acceptingCallbacks || eventFailure is not null)
            {
                return;
            }

            if (usageFence.Failure is { } settlementFailure)
            {
                eventFailure = settlementFailure;
            }
            else if (isolated && update is ToolExecutionStartEvent)
            {
                eventFailure = "Unexpected tool execution in an isolated native session.";
            }
            else if (update is SessionErrorEvent error)
            {
                eventFailure =
                    $"Native session {session?.SessionId ?? "(opening)"} "
                    + $"reported {error.Data.ErrorType}"
                    + (
                        error.Data.OriginatingMessageId is { } origin
                            ? $" for message {origin}"
                            : ""
                    )
                    + $": {error.Data.Message}";
            }
            else if (update is SessionLimitsExhaustedRequestedEvent exhausted)
            {
                if (
                    isolated
                    || limitRequests is null
                    || !limitRequests.Writer.TryWrite(exhausted.Data)
                )
                {
                    eventFailure = "Native session credit limit exhausted "
                        + "without a backed renewal.";
                }
            }

            if (eventFailure is not null)
            {
                RetainFailure(new OperationException("native_stopped", eventFailure),
                    sendCancellation);
                eventStop.Cancel();
            }
        }
    }

    internal void CheckEvents(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (callbacks)
        {
            if (eventFailure is not null)
            {
                throw new OperationException("native_stopped", eventFailure);
            }
        }
    }

    private static Dictionary<string, McpServerConfig> McpServers(
        RunDatabase database,
        string worker,
        string cwd
    )
    {
        string executable =
            Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        string assembly = typeof(RunDatabase).Assembly.Location;
        List<string> arguments = [];
        if (
            !Path.GetFileNameWithoutExtension(executable)
                .Equals(
                    Path.GetFileNameWithoutExtension(assembly),
                    StringComparison.OrdinalIgnoreCase
                )
        )
        {
            executable = Path.GetFileNameWithoutExtension(executable)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? executable
                : Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            if (string.IsNullOrEmpty(assembly))
            {
                throw new OperationException("unavailable", "No managed MCP entry assembly.");
            }

            arguments.Add(Path.GetFullPath(assembly));
        }

        arguments.AddRange(["mcp", "--state", database.StatePath, "--actor", worker]);
        return new()
        {
            ["lite-agensh"] = new McpStdioServerConfig
            {
                Command = executable,
                Args = arguments,
                WorkingDirectory = cwd,
                Tools = ["*"],
            },
        };
    }
}
#pragma warning restore GHCP001
