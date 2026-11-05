using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace LiteAgensh;

// These pinned, typed native metadata/limits/usage APIs are experimental in SDK 1.0.28.
#pragma warning disable GHCP001
internal sealed class NativeSdkSession : IAsyncDisposable
{
    private const int ProbeTimeoutSeconds = 30;
    private readonly RunDatabase database;
    private readonly NativeRuntime runtime;
    private readonly string cwd;
    private readonly bool isolated;
    private readonly object callbacks = new();
    private readonly CancellationTokenSource eventStop = new();
    private CopilotClient? client;
    private CopilotSession? session;
    private bool acceptingCallbacks = true;
    private string? eventFailure;
    private string? actualRuntimeVersion;
    private bool disposed;
    private Channel<SessionLimitsExhaustedRequestedData>? limitRequests;

    public NativeSdkSession(
        RunDatabase database,
        ProcessRunner processes,
        string cwd,
        bool isolated
    )
    {
        this.database = database;
        this.cwd = Path.GetFullPath(cwd);
        this.isolated = isolated;
        runtime = new(database, processes, this.cwd);
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

    public JsonObject? Metadata { get; private set; }

    public async Task OpenAsync(
        string worker,
        string sessionId,
        decimal? limit,
        string? exactSession,
        string evidencePath,
        CancellationToken cancellationToken
    )
    {
        string endpoint = await runtime
            .StartAsync($"native:{worker}:{sessionId}", evidencePath, cancellationToken)
            .ConfigureAwait(false);
        client = new(
            new CopilotClientOptions
            {
                Connection = RuntimeConnection.ForUri(endpoint, runtime.ConnectionToken),
                Mode = CopilotClientMode.Empty,
            }
        );
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await client.StartAsync(deadline.Token).ConfigureAwait(false);
        GetStatusResponse status = await client
            .GetStatusAsync(deadline.Token)
            .ConfigureAwait(false);
        if (status.Version != RuntimeVersion)
        {
            throw new OperationException(
                "unavailable",
                "The installed native runtime does not match the SDK package's CLI pin."
            );
        }
        actualRuntimeVersion = status.Version;

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
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));
        string phase = "metadata";
        try
        {
            SessionMetadataSnapshot metadata = await active
                .Rpc.Metadata.SnapshotAsync(deadline.Token)
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
                .Rpc.Usage.GetMetricsAsync(deadline.Token)
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
            };
            if (validateEvents)
            {
                CheckEvents();
            }
            return new(
                expectedSession,
                usage.TotalNanoAiu,
                usage.AiCreditsStatus?.Value,
                usage.TotalUserRequests
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OperationException(
                "native_probe_failed",
                deadline.IsCancellationRequested
                    ? $"Native {phase} query exceeded its {ProbeTimeoutSeconds}-second deadline."
                    : $"Native {phase} query was canceled before caller cancellation."
            );
        }
    }

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
        Func<CancellationToken, Task<decimal>>? renewLimit = null
    )
    {
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
        try
        {
            sending = active.SendAndWaitAsync(text, timeout, linked.Token);
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
                    linked.Token
                ).ConfigureAwait(false);
            }
            AssistantMessageEvent? response = await sending.ConfigureAwait(false);
            CheckEvents(cancellationToken);
            return new() { ["stopReason"] = "end_turn", ["text"] = response?.Data.Content };
        }
        catch
        {
            await AbortFailedSendAsync(
                    () => AbortAsync(CancellationToken.None),
                    cancellationToken
                )
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            lock (callbacks)
            {
                limitRequests = null;
            }
            await linked.CancelAsync().ConfigureAwait(false);
            if (sending is not null)
            {
                try
                {
                    await sending.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The send's original failure is preserved above.
                }
            }
            if (pending is not null)
            {
                try
                {
                    await pending.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }
    }

    internal static async Task HandleExhaustedLimitAsync(
        string requestId,
        Func<CancellationToken, Task<decimal>> renewLimit,
        Func<string, UISessionLimitsExhaustedResponse, CancellationToken, Task> apply,
        Func<decimal, CancellationToken, Task> verify,
        CancellationToken cancellationToken
    )
    {
        decimal limit = await renewLimit(cancellationToken).ConfigureAwait(false);
        await apply(
            requestId,
            new UISessionLimitsExhaustedResponse
            {
                Action = UISessionLimitsExhaustedResponseAction.Set,
                MaxAiCredits = NativeLimit(limit),
            },
            cancellationToken
        ).ConfigureAwait(false);
        await verify(limit, cancellationToken).ConfigureAwait(false);
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

    private static double NativeLimit(decimal allowance)
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
            try
            {
                if (client is not null)
                {
                    await client
                        .DisposeAsync()
                        .AsTask()
                        .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await runtime.DisposeAsync().ConfigureAwait(false);
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
            if (!acceptingCallbacks || eventFailure is not null)
            {
                return;
            }

            if (isolated && update is ToolExecutionStartEvent)
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
