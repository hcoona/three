using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class TypedSessionGuardSpecs(TestContext context)
{
    private const string ToolReceiptSource = "typed-history-only-tool-source";

    public static IEnumerable<(double? total, string? status, long requests)> IncompleteUsage =>
        [
            (null, "complete", 1),
            (0d, "complete", 0),
            (1d, null, 1),
            (1d, "partial", 1),
            (1d, "Complete", 1),
            (double.NaN, "complete", 1),
            (double.PositiveInfinity, "complete", 1),
            (double.NegativeInfinity, "complete", 1),
            (-1d, "complete", 1),
            (12_500_000_000d, "complete", -1),
        ];

    [TestMethod]
    [DataRow(12_500_000_000d, 1L, "12.5")]
    [DataRow(1d, 1L, "0.000000001")]
    [DataRow(0.5d, 1L, "0.0000000005")]
    [DataRow(0d, 2L, "0")]
    [DataRow(125_000_000d, 0L, "0.125")]
    public void RequireCompleteFiniteMeasuredUsageConvertsExactNanoCredits(
        double nanoCredits,
        long requests,
        string expectedCredits
    )
    {
        NativeUsage usage = new("session-measured", nanoCredits, "complete", requests);

        decimal credits = usage.RequireComplete();

        Assert.AreEqual(
            decimal.Parse(expectedCredits, System.Globalization.CultureInfo.InvariantCulture),
            credits
        );
    }

    [TestMethod]
    [DynamicData(nameof(IncompleteUsage))]
    public void RequireCompleteAbsentPartialInvalidOrEmptyUsageRetainsUncertainty(
        double? total,
        string? status,
        long requests
    )
    {
        NativeUsage usage = new("session-unresolved", total, status, requests);

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            usage.RequireComplete()
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual(
            "Native credit usage is absent, invalid or incomplete; reservation retained.",
            error.Message
        );
    }

    [TestMethod]
    public void RequireCompleteFiniteButUnrepresentableSubtotalReportsOverflowNotZero()
    {
        NativeUsage usage = new("session-overflow", double.MaxValue, "complete", 1);

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            usage.RequireComplete()
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual("Native credit subtotal overflowed.", error.Message);
    }

    [TestMethod]
    public void ValidateMetadataLocalNormalizedDirectoryAndModelAcceptsOnlyMatchingIdentityAndCap()
    {
        string cwd = MetadataDirectory();
        // The accepted adapter contract uses this pinned SDK experimental RPC DTO.
#pragma warning disable GHCP001
        SessionMetadataSnapshot metadata = Metadata(cwd + Path.DirectorySeparatorChar);
#pragma warning restore GHCP001
        metadata.SelectedModel = "MODEL-A";

        NativeSdkSession.ValidateMetadata(
            metadata,
            "session-local",
            "session-local",
            cwd,
            100m,
            "model-a"
        );
        NativeSdkSession.ValidateMetadata(metadata, "session-local", "session-local", cwd, null);
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.ValidateMetadata(
                metadata,
                "session-local",
                "session-local",
                cwd,
                99m,
                "model-a"
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual(
            "Native identity, working directory, ownership, model or applied limit mismatched.",
            error.Message
        );
    }

    [TestMethod]
    [DataRow("reported-session")]
    [DataRow("active-session")]
    [DataRow("directory")]
    [DataRow("relative-directory")]
    [DataRow("already-in-use")]
    [DataRow("remote")]
    [DataRow("model")]
    [DataRow("missing-limit")]
    [DataRow("changed-limit")]
    [DataRow("nonfinite-limit")]
    public void ValidateMetadataIdentityOwnershipModelOrCapMismatchRejects(string scenario)
    {
        string cwd = MetadataDirectory();
        // The accepted adapter contract uses this pinned SDK experimental RPC DTO.
#pragma warning disable GHCP001
        SessionMetadataSnapshot metadata = Metadata(cwd);
#pragma warning restore GHCP001
        string active = "session-local";
        switch (scenario)
        {
            case "reported-session":
                metadata.SessionId = "session-other";
                break;
            case "active-session":
                active = "session-other";
                break;
            case "directory":
                metadata.WorkingDirectory = Path.Combine(cwd, "other");
                break;
            case "relative-directory":
                const string child = "lite-agensh-relative-metadata";
                cwd = Path.GetFullPath(child);
                metadata.WorkingDirectory = child;
                break;
            case "already-in-use":
                metadata.AlreadyInUse = true;
                break;
            case "remote":
                metadata.IsRemote = true;
                break;
            case "model":
                metadata.SelectedModel = "model-b";
                break;
            case "missing-limit":
                metadata.SessionLimits = null;
                break;
            case "changed-limit":
                metadata.SessionLimits!.MaxAiCredits = 101;
                break;
            case "nonfinite-limit":
                metadata.SessionLimits!.MaxAiCredits = double.NaN;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.ValidateMetadata(
                metadata,
                active,
                "session-local",
                cwd,
                100m,
                "model-a"
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual(
            "Native identity, working directory, ownership, model or applied limit mismatched.",
            error.Message
        );
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    [DataRow(true, true, true)]
    public void RequireIdleRecoveryWorkAnyWorkIndicatorRejects(
        bool processing,
        bool active,
        bool abortable
    )
    {
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.RequireIdleRecoveryWork(
                new() { Processing = processing },
                new() { HasActiveWork = active, Abortable = abortable }
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual(
            "New native recovery boundary still has processing or active work.",
            error.Message
        );
    }

    [TestMethod]
    public async Task ReadIdleRecoveryUsageAsyncWorkAppearsDuringReadRejectsReturnedSubtotal()
    {
        bool processing = false;
        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            NativeSdkSession.ReadIdleRecoveryUsageAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    processing = true;
                    return Task.FromResult(
                        new NativeUsage("session-local", 12_500_000_000, "complete", 1)
                    );
                },
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    NativeSdkSession.RequireIdleRecoveryWork(
                        new() { Processing = processing },
                        new() { HasActiveWork = false, Abortable = false }
                    );
                    return Task.CompletedTask;
                },
                context.CancellationToken
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.Contains("processing or active work", error.Message);
        Assert.IsTrue(processing);
    }

    [TestMethod]
    public async Task ReadIdleRecoveryUsageAsyncAlreadyActiveDoesNotEnterUsageRead()
    {
        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            NativeSdkSession.ReadIdleRecoveryUsageAsync(
                _ =>
                    throw new InvalidOperationException(
                        "Usage cannot be read before the idle guard."
                    ),
                _ =>
                {
                    NativeSdkSession.RequireIdleRecoveryWork(
                        new() { Processing = false },
                        new() { HasActiveWork = true, Abortable = true }
                    );
                    return Task.CompletedTask;
                },
                context.CancellationToken
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.Contains("processing or active work", error.Message);
    }

    [TestMethod]
    public async Task ReadProbeAsyncManualDeadlineCancelsReadAndNamesFailedQuery()
    {
        ManualScenarioClock clock = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<NativeUsage> pendingRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken observed = default;
        Task<NativeUsage> probe = NativeSdkSession.ReadProbeAsync(
            async token =>
            {
                observed = token;
                entered.SetResult();
                return await pendingRead.Task.WaitAsync(token);
            },
            context.CancellationToken,
            () => "usage",
            clock
        );
        try
        {
            await ObserveReadProbeEntryAsync(entered.Task, probe);

            clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
            Assert.IsFalse(observed.IsCancellationRequested);
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.IsTrue(observed.IsCancellationRequested);
            _ = await Task.WhenAny(probe).WaitAsync(ReadProbeObservationFuse);
            OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
                probe
            );

            Assert.AreEqual("native_probe_failed", error.Code);
            Assert.AreEqual("Native usage query exceeded its 30-second deadline.", error.Message);
            Assert.IsFalse(context.CancellationToken.IsCancellationRequested);
        }
        catch (Exception primary)
        {
            await EndFailedReadProbeAsync(pendingRead, probe, primary);
            throw;
        }
    }

    [TestMethod]
    public async Task ReadProbeAsyncCallerCancellationPreservesRequestTokenAndCause()
    {
        ManualScenarioClock clock = new();
        using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken
        );
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<NativeUsage> pendingRead = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken observed = default;
        Task<NativeUsage> probe = NativeSdkSession.ReadProbeAsync(
            async token =>
            {
                observed = token;
                entered.SetResult();
                return await pendingRead.Task.WaitAsync(token);
            },
            request.Token,
            timeProvider: clock
        );
        try
        {
            await ObserveReadProbeEntryAsync(entered.Task, probe);

            request.Cancel();
            _ = await Task.WhenAny(probe).WaitAsync(ReadProbeObservationFuse);
            var error =
                await Assert.ThrowsExactlyAsync<NativeSdkSession.RequestedReadCanceledException>(
                    () => probe
                );

            Assert.AreEqual(request.Token, error.RequestToken);
            Assert.AreEqual(observed, error.CancellationToken);
            var cause = Assert.IsInstanceOfType<OperationCanceledException>(error.InnerException);
            Assert.AreEqual(observed, cause.CancellationToken);
            Assert.AreEqual(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                clock.GetUtcNow()
            );
        }
        catch (Exception primary)
        {
            await EndFailedReadProbeAsync(pendingRead, probe, primary);
            throw;
        }
    }

    private static readonly TimeSpan ReadProbeObservationFuse = TimeSpan.FromSeconds(5);

    private async Task ObserveReadProbeEntryAsync(Task entered, Task probe)
    {
        Task first = await Task.WhenAny(entered, probe)
            .WaitAsync(ReadProbeObservationFuse, context.CancellationToken);
        if (first == probe && !entered.IsCompletedSuccessfully)
        {
            await probe;
            Assert.Fail("The native probe returned without entering its controlled read.");
        }
        await entered;
    }

    private async Task EndFailedReadProbeAsync<T>(
        TaskCompletionSource<T> pendingRead,
        Task probe,
        Exception primary
    )
    {
        pendingRead.TrySetCanceled();
        try
        {
            _ = await Task.WhenAny(probe).WaitAsync(ReadProbeObservationFuse);
            await probe;
        }
        catch (Exception secondary)
        {
            if (!ReferenceEquals(secondary, primary))
            {
                context.WriteLine("Secondary read-probe completion failure: {0}", secondary);
            }
        }
    }

    [TestMethod]
    public async Task ReadProbeAsyncUnrequestedCancellationIsFailureNotCallerCancellation()
    {
        ManualScenarioClock clock = new();

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            NativeSdkSession.ReadProbeAsync(
                _ => Task.FromCanceled<NativeUsage>(new CancellationToken(canceled: true)),
                context.CancellationToken,
                timeProvider: clock
            )
        );

        Assert.AreEqual("native_probe_failed", error.Code);
        Assert.AreEqual(
            "Native metadata query was canceled before caller cancellation.",
            error.Message
        );
        Assert.IsFalse(context.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task ReadProbeAsyncUnsupportedTypedReadPreservesOriginalFailure()
    {
        ManualScenarioClock clock = new();
        NotSupportedException original = new("Typed usage is unavailable.");

        NotSupportedException error = await Assert.ThrowsExactlyAsync<NotSupportedException>(() =>
            NativeSdkSession.ReadProbeAsync(
                _ => Task.FromException<NativeUsage>(original),
                context.CancellationToken,
                timeProvider: clock
            )
        );

        Assert.AreSame(original, error);
        Assert.AreEqual("Typed usage is unavailable.", error.Message);
        Assert.IsFalse(context.CancellationToken.IsCancellationRequested);
    }

#pragma warning disable GHCP001 // Selected public typed history and accounting contracts.
    [TestMethod]
    [DataRow(
        "wrong-shutdown-start",
        "Original routine termination or accounting frontier is invalid"
    )]
    [DataRow(
        "shutdown-after-segment-end",
        "Matching original routine native termination is missing"
    )]
    [DataRow("error-shutdown", "Original routine termination or accounting frontier is invalid")]
    [DataRow(
        "assistant-frontier",
        "Original routine termination or accounting frontier is invalid"
    )]
    [DataRow("tool-frontier", "Original routine termination or accounting frontier is invalid")]
    [DataRow("blank-cursor", "Original native history is expired, incomplete or unsupported")]
    [DataRow("expired", "Original native history is expired, incomplete or unsupported")]
    [DataRow("incomplete", "Matching original routine native termination is missing")]
    [DataRow("reused-cursor", "Native history cursor was reused")]
    [DataRow("page-error", "Later typed history page failed")]
    [DataRow(
        "resumed-lifetime",
        "A resumed session cannot substitute for the original native lifetime"
    )]
    [DataRow("later-work", "New native work followed the selected original termination")]
    [DataRow(
        "later-tool-receipt",
        "Unproved coverage for an original tool usage receipt "
        + "beyond the selected termination frontier"
    )]
    [DataRow(
        "later-unmarked-tool-source",
        "Unproved coverage for an original tool usage receipt "
        + "beyond the selected termination frontier"
    )]
    [DataRow(
        "later-tool-without-request",
        "Unproved coverage for an original tool usage receipt "
        + "beyond the selected termination frontier"
    )]
    [DataRow(
        "later-missing-tool-accounting",
        "Unproved coverage for an original tool usage receipt "
        + "beyond the selected termination frontier"
    )]
    [DataRow(
        "later-partial-tool-accounting",
        "Unproved coverage for an original tool usage receipt "
        + "beyond the selected termination frontier"
    )]
    public async Task ReadOriginalShutdownTypedHistoryRejectsOneFaultAfterHealthyControl(
        string fault,
        string reason
    )
    {
        var (origin, history) = TypedOriginalHistory();
        const string pendingToolCall = "typed-history-pending-tool-call";
        bool laterReceipt = fault is "later-tool-receipt" or "later-unmarked-tool-source"
            or "later-tool-without-request" or "later-missing-tool-accounting"
            or "later-partial-tool-accounting";
        var coveredTool = (ToolExecutionCompleteEvent)history[^2];
        string[] cursors = ["snapshot/Aq", "r%2FQh", "keep+case:Jz", "tail/Us", "end:Kb"];
        OperationException pageFailure = new(
            "native_history_failed",
            "Later typed history page failed."
        );
        async Task<OriginalNativeShutdown> ReadAsync(string selection)
        {
            int nextPage = 0;
            OriginalNativeShutdown shutdown = await NativeSdkSession.ReadOriginalShutdownAsync(
                origin,
                (cursor, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    int index = nextPage++;
                    Assert.AreEqual(index == 0 ? null : cursors[index - 1], cursor);
                    if (selection == "page-error" && index == 1)
                    {
                        throw pageFailure;
                    }
                    SessionEvent[] events = index < history.Length ? [history[index]] : [];
                    if (selection == "resumed-lifetime" && index == history.Length - 1)
                    {
                        events =
                        [
                            new SessionResumeEvent
                            {
                                Id = Guid.NewGuid(),
                                Timestamp = history[index].Timestamp.AddMilliseconds(-1),
                                Data = new()
                                {
                                    ResumeTime = history[index].Timestamp.AddMilliseconds(-1),
                                    EventCount = index,
                                },
                            },
                            history[index],
                        ];
                    }
                    else if (laterReceipt && index == 1)
                    {
                        // Use durable starts instead of the live assistant usage DTO in this row.
                        events =
                        [
                            TypedToolStart(history[index].Timestamp, coveredTool.Data.ToolCallId),
                            TypedToolStart(
                                history[index].Timestamp.AddMilliseconds(100), pendingToolCall
                            ),
                        ];
                    }
                    else if (laterReceipt && index == history.Length)
                    {
                        bool distinct = selection == fault;
                        ToolExecutionUsage covered = coveredTool.Data.ToolUsage!;
                        bool withoutRequest = fault == "later-tool-without-request";
                        bool missingAccounting = fault == "later-missing-tool-accounting";
                        bool partialAccounting = fault == "later-partial-tool-accounting";
                        bool unmarkedSource = distinct && fault == "later-unmarked-tool-source";
                        if (partialAccounting)
                        {
                            events =
                            [
                                TypedToolReceipt(
                                    origin.SegmentEndTime.AddMilliseconds(-500),
                                    pendingToolCall,
                                    "typed-history-pending-operation",
                                    distinct ? "" : "typed-history-pending-usage",
                                    sequence: 5
                                ),
                            ];
                        }
                        else
                        {
                            events =
                            [
                                TypedToolReceipt(
                                    origin.SegmentEndTime.AddMilliseconds(-500),
                                    distinct ? pendingToolCall : coveredTool.Data.ToolCallId,
                                    withoutRequest || (!distinct && fault == "later-tool-receipt")
                                        ? ""
                                        : distinct
                                            ? "typed-history-pending-operation"
                                            : covered.RequestId!,
                                    distinct || withoutRequest
                                        ? "typed-history-pending-usage"
                                        : covered.Accounting!.UsageId,
                                    sequence: (withoutRequest && !distinct) || unmarkedSource
                                        ? 5 : 6,
                                    source: unmarkedSource
                                        ? "typed-history-unrepresented-source"
                                        : ToolReceiptSource,
                                    includeAccounting: !missingAccounting
                                ),
                            ];
                        }
                    }
                    else if (selection == "later-work" && index == history.Length)
                    {
                        events =
                        [
                            new AssistantTurnStartEvent
                            {
                                Id = Guid.NewGuid(),
                                Timestamp = origin.SegmentEndTime.AddMilliseconds(-500),
                                Data = new() { TurnId = "typed-history-later-turn" },
                            },
                        ];
                    }
                    return Task.FromResult(
                        new EventsReadResult
                        {
                            CursorStatus = selection == "expired" && index == history.Length
                                ? EventsCursorStatus.Expired
                                : EventsCursorStatus.Ok,
                            Cursor = selection == "blank-cursor" && index == history.Length
                                ? ""
                                : selection == "reused-cursor" && index > 0
                                    ? cursors[0]
                                    : cursors[index],
                            Events = events,
                            HasMore = selection != "incomplete" && index < history.Length,
                        }
                    );
                },
                context.CancellationToken
            );
            Assert.AreEqual(cursors.Length, nextPage);
            return shutdown;
        }

        OriginalNativeShutdown healthy = await ReadAsync("");
        Assert.AreEqual(230_000_000_000d, healthy.TotalNanoAiu);
        Assert.AreEqual(history[^1].Id, healthy.EventId);
        Assert.AreEqual(history[^1].Timestamp, healthy.Timestamp);
        Assert.AreEqual(3L, healthy.Watermarks[origin.SessionId]);
        Assert.AreEqual(5L, healthy.Watermarks[ToolReceiptSource]);
        if (fault is "wrong-shutdown-start" or "shutdown-after-segment-end" or "error-shutdown"
            or "assistant-frontier" or "tool-frontier")
        {
            var terminal = (SessionShutdownEvent)history[^1];
            Dictionary<string, long> marks = new(
                terminal.Data.UsageAccountingWatermarks!,
                StringComparer.Ordinal
            );
            if (fault == "assistant-frontier")
            {
                marks[origin.SessionId] = 2;
            }
            else if (fault == "tool-frontier")
            {
                marks[ToolReceiptSource] = 4;
            }
            history[^1] = new SessionShutdownEvent
            {
                Id = terminal.Id,
                Timestamp = fault == "shutdown-after-segment-end"
                    ? origin.SegmentEndTime.AddMilliseconds(1)
                    : terminal.Timestamp,
                Data = new()
                {
                    CodeChanges = terminal.Data.CodeChanges,
                    ModelMetrics = terminal.Data.ModelMetrics,
                    TotalApiDuration = terminal.Data.TotalApiDuration,
                    SessionStartTime = terminal.Data.SessionStartTime
                        + (fault == "wrong-shutdown-start" ? 1 : 0),
                    ShutdownType = fault == "error-shutdown"
                        ? ShutdownType.Error
                        : terminal.Data.ShutdownType,
                    TotalNanoAiu = terminal.Data.TotalNanoAiu,
                    UsageAccountingWatermarks = marks,
                },
            };
        }

        OperationException failure = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            ReadAsync(fault)
        );

        Assert.AreEqual(
            fault == "page-error" ? "native_history_failed" : "credit_unknown",
            failure.Code
        );
        Assert.Contains(reason, failure.Message);
        if (fault == "page-error")
        {
            Assert.AreSame(pageFailure, failure);
        }
    }

    [TestMethod]
    public async Task ReadOriginalShutdownPagesShareOneThirtySecondProbeBudgetAndToken()
    {
        var (origin, history) = TypedOriginalHistory();
        ManualScenarioClock clock = new();
        TaskCompletionSource<(CancellationToken Token, Task<EventsReadResult> Read)> secondPage =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<EventsReadResult> pendingPage = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken acquisitionToken = default;
        CancellationToken firstPageToken = default;
        bool persisted = false;

        Task<EventsReadResult> ReadPage(string? cursor, CancellationToken token)
        {
            if (cursor is null)
            {
                firstPageToken = token;
                clock.Advance(TimeSpan.FromSeconds(20));
                return Task.FromResult(
                    new EventsReadResult
                    {
                        CursorStatus = EventsCursorStatus.Ok,
                        Cursor = "second-page",
                        Events = history,
                        HasMore = true,
                    }
                );
            }

            Assert.AreEqual("second-page", cursor);
            Task<EventsReadResult> blocked = pendingPage.Task.WaitAsync(token);
            secondPage.SetResult((token, blocked));
            return blocked;
        }

        Task<OriginalNativeShutdown> probe = NativeSdkSession.ReadBoundedOriginalShutdownAsync(
            origin,
            token =>
            {
                acquisitionToken = token;
                return Task.FromResult<NativeSdkSession.ReadOriginalHistoryPage>(ReadPage);
            },
            (_, _) =>
            {
                persisted = true;
                return Task.CompletedTask;
            },
            clock,
            context.CancellationToken
        );
        try
        {
            await ObserveReadProbeEntryAsync(secondPage.Task, probe);
            var (secondPageToken, blocked) = await secondPage.Task;
            Assert.AreEqual(acquisitionToken, firstPageToken);
            Assert.AreEqual(firstPageToken, secondPageToken);

            clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
            Assert.IsFalse(blocked.IsCompleted);
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.IsTrue(secondPageToken.IsCancellationRequested);
            Assert.IsFalse(context.CancellationToken.IsCancellationRequested);

            _ = await Task.WhenAny(probe).WaitAsync(ReadProbeObservationFuse);
            var failure = await Assert.ThrowsAsync<OperationCanceledException>(() => probe);
            Assert.AreEqual(secondPageToken, failure.CancellationToken);
            Assert.IsTrue(blocked.IsCanceled);
            Assert.IsFalse(persisted);
        }
        catch (Exception primary)
        {
            await EndFailedReadProbeAsync(pendingPage, probe, primary);
            throw;
        }
    }

    private static (NativeRecoveryOrigin Origin, SessionEvent[] History) TypedOriginalHistory()
    {
        DateTimeOffset segment = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset start = segment.AddSeconds(1);
        const string session = "typed-history-only-session";
        const string version = "typed-history-only-runtime";
        NativeRecoveryOrigin origin = new(
            session,
            version,
            "typed-history-only-origin",
            0,
            0,
            start,
            segment,
            segment,
            segment.AddSeconds(4)
        );
        SessionEvent[] history =
        [
            new SessionStartEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = start,
                Data = new()
                {
                    Producer = "typed-history-predicate",
                    Version = 1,
                    SessionId = session,
                    CopilotVersion = version,
                    AlreadyInUse = false,
                    StartTime = start,
                },
            },
            new AssistantUsageEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = segment.AddSeconds(2),
                Data = new()
                {
                    Model = "controlled-model",
                    Accounting = new()
                    {
                        SourceSessionId = session,
                        Sequence = 3,
                        UsageId = "typed-history-receipt",
                    },
                },
            },
            TypedToolReceipt(
                segment.AddMilliseconds(2_500),
                "typed-history-tool-call",
                "typed-history-tool-operation",
                "typed-history-tool-receipt",
                sequence: 5
            ),
            new SessionShutdownEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = segment.AddSeconds(3),
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
                    SessionStartTime = start.ToUnixTimeMilliseconds(),
                    ShutdownType = ShutdownType.Routine,
                    TotalNanoAiu = 230_000_000_000d,
                    UsageAccountingWatermarks = new Dictionary<string, long>
                    {
                        [session] = 3,
                        [ToolReceiptSource] = 5,
                    },
                },
            },
        ];
        return (origin, history);
    }

    private static ToolExecutionStartEvent TypedToolStart(
        DateTimeOffset timestamp,
        string callId
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp,
            Data = new() { ToolCallId = callId, ToolName = "typed-history-metered-tool" },
        };

    private static ToolExecutionCompleteEvent TypedToolReceipt(
        DateTimeOffset timestamp,
        string callId,
        string requestId,
        string usageId,
        long sequence,
        string source = ToolReceiptSource,
        bool includeAccounting = true
    )
    {
        const string toolModel = "typed-history-tool-model";
        return new()
        {
            Id = Guid.NewGuid(),
            Timestamp = timestamp,
            Data = new()
            {
                ToolCallId = callId,
                Success = true,
                ToolUsage = new()
                {
                    Model = toolModel,
                    RequestId = requestId,
                    CopilotUsage = new() { Model = toolModel, TotalNanoAiu = 1_000_000_000 },
                    Accounting = !includeAccounting ? null : new()
                    {
                        SourceSessionId = source,
                        Sequence = sequence,
                        UsageId = usageId,
                    },
                },
            },
        };
    }
#pragma warning restore GHCP001

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void NativeLimitNonpositiveWindowRejectsRatherThanRemovingCap(int allowance)
    {
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.NativeLimit(allowance)
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual("Native credit window is invalid.", error.Message);
    }

    [TestMethod]
    public void NativeLimitOrdinaryCreditCeilingRoundsDownToBackedBinaryValue()
    {
        const long unitsPerCredit = 1L << 46;
        // 100.2 = 501/5 lies four fifths of a binary64 step above the backed value.
        const long backedUnits = 501 * unitsPerCredit / 5;

        double limit = NativeSdkSession.NativeLimit(100.2m);

        Assert.AreEqual((double)backedUnits, limit * unitsPerCredit);
    }

    [TestMethod]
    public void NativeLimitPreservesExactAndDownwardRoundedCeilings()
    {
        Assert.AreEqual(
            9_007_199_254_740_992d,
            NativeSdkSession.NativeLimit(9_007_199_254_740_993m)
        );
        Assert.AreEqual(100.5d, NativeSdkSession.NativeLimit(100.5m));
    }

    [TestMethod]
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task NativeUsageFencePendingCompactionRequiresMatchingTerminalBeforeRead()
    {
        NativeUsageFence fence = new();
        fence.Observe(Start("compact-one"));
        bool terminal = false;
        Task<NativeUsage> capture = fence.CaptureAsync(
            _ =>
                terminal
                    ? Task.FromResult(
                        new NativeUsage("session-local", 47_000_000_000, "complete", 2)
                    )
                    : throw new InvalidOperationException(
                        "A compaction subtotal is not final usage."
                    ),
            context.CancellationToken
        );

        terminal = true;
        fence.Observe(Complete("compact-one", success: true));
        NativeUsage usage = await capture;
        decimal? publishedCredits = null;
        fence.Use(usage, () => publishedCredits = usage.RequireComplete());

        Assert.AreEqual<decimal?>(47m, publishedCredits);
        Assert.IsNull(fence.Failure);
    }

    [TestMethod]
    [DataRow("missing-start")]
    [DataRow("wrong-attempt")]
    [DataRow("failed")]
    [DataRow("overlap")]
    [Timeout(5_000, CooperativeCancellation = true)]
    public async Task NativeUsageFenceContradictoryCompactionPermanentlyRejectsCapture(
        string scenario
    )
    {
        NativeUsageFence fence = new();
        if (scenario != "missing-start")
        {
            fence.Observe(Start("compact-one"));
        }

        if (scenario == "overlap")
        {
            fence.Observe(Start("compact-two"));
        }
        else
        {
            fence.Observe(
                Complete(
                    scenario == "wrong-attempt" ? "compact-other" : "compact-one",
                    success: scenario != "failed"
                )
            );
        }

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            fence.CaptureAsync(
                _ => throw new InvalidOperationException("Unhealthy work cannot reach usage read."),
                context.CancellationToken
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual(fence.Failure, error.Message);
        Assert.AreEqual(
            scenario == "overlap"
                ? "Native compaction started before its prior attempt settled."
                : "Native compaction settlement failed or contradicted its start.",
            error.Message
        );
        fence.Observe(Start("later-valid-attempt"));
        fence.Observe(Complete("later-valid-attempt", success: true));
        OperationException later = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            fence.CaptureAsync(
                _ =>
                    throw new InvalidOperationException(
                        "Later events cannot erase the prior failure."
                    ),
                context.CancellationToken
            )
        );
        Assert.AreEqual("credit_unknown", later.Code);
        Assert.AreEqual(error.Message, later.Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeUsageFenceWorkChangesInsideReadRejectsStaleCapture(bool assistantUsage)
    {
        NativeUsageFence fence = new();

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            fence.CaptureAsync(
                _ =>
                {
                    if (assistantUsage)
                    {
                        fence.Observe(
                            new AssistantUsageEvent
                            {
                                Id = Guid.NewGuid(),
                                Timestamp = DateTimeOffset.UtcNow,
                                Data = new() { Model = "controlled-model" },
                            }
                        );
                    }
                    else
                    {
                        fence.Observe(Start("compact-new"));
                        fence.Observe(Complete("compact-new", success: true));
                    }
                    return Task.FromResult(
                        new NativeUsage("session-local", 40_000_000_000, "complete", 1)
                    );
                },
                context.CancellationToken
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual("Native work changed during the final usage query.", error.Message);
        Assert.IsNull(fence.Failure);
    }

    [TestMethod]
    public async Task NativeUsageFenceWorkAfterCaptureBlocksPublicationEvenAfterTerminal()
    {
        NativeUsageFence fence = new();
        NativeUsage usage = await fence.CaptureAsync(
            _ => Task.FromResult(new NativeUsage("session-local", 40_000_000_000, "complete", 1)),
            context.CancellationToken
        );
        fence.Observe(Start("compact-later"));
        fence.Observe(Complete("compact-later", success: true));
        string publication = "retained";

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            fence.Use(usage, () => publication = "released")
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual("Native work invalidated the staged usage reading.", error.Message);
        Assert.AreEqual("retained", publication);
    }

    [TestMethod]
    public async Task NativeUsageFenceEquivalentButUncapturedReadingCannotBePublished()
    {
        NativeUsageFence fence = new();
        NativeUsage captured = await fence.CaptureAsync(
            _ => Task.FromResult(new NativeUsage("session-local", 12_500_000_000, "complete", 1)),
            context.CancellationToken
        );
        NativeUsage other = captured with { };
        decimal? publication = null;

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            fence.Use(other, () => publication = 0m)
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.IsNull(publication);
        fence.Use(captured, () => publication = captured.RequireComplete());
        Assert.AreEqual<decimal?>(12.5m, publication);
    }

    [TestMethod]
    public async Task NativeUsageFenceUndeliveredTurnBlocksUsageUntilTerminalBoundary()
    {
        NativeUsageFence fence = new();
        fence.SetTurnPending(true);

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            fence.CaptureAsync(
                _ => throw new InvalidOperationException("An undelivered turn cannot be measured."),
                context.CancellationToken
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        Assert.AreEqual(
            "Native send has no delivered terminal boundary; usage remains unresolved.",
            error.Message
        );
        fence.SetTurnPending(false);
        NativeUsage usage = await fence.CaptureAsync(
            _ => Task.FromResult(new NativeUsage("session-local", 12_500_000_000, "complete", 1)),
            context.CancellationToken
        );
        decimal? publication = null;
        fence.Use(usage, () => publication = usage.RequireComplete());
        Assert.AreEqual<decimal?>(12.5m, publication);
    }

    [TestMethod]
    public async Task ReadProbeAsyncCompletedReadReturnsMeasuredUsageWithoutLateCancellation()
    {
        ManualScenarioClock clock = new();
        CancellationToken observed = default;

        NativeUsage usage = await NativeSdkSession.ReadProbeAsync(
            token =>
            {
                observed = token;
                return Task.FromResult(
                    new NativeUsage("session-local", 12_500_000_000, "complete", 1)
                );
            },
            context.CancellationToken,
            timeProvider: clock
        );
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.AreEqual(12.5m, usage.RequireComplete());
        Assert.AreEqual("session-local", usage.SessionId);
        Assert.IsFalse(observed.IsCancellationRequested);
    }

    [TestMethod]
    public async Task HandleExhaustedLimitAsyncRenewalAppliesAndVerifiesExactBackedCeiling()
    {
        const decimal backedCeiling = 100.2m;
        const long unitsPerCredit = 1L << 46;
        const long backedUnits = 501 * unitsPerCredit / 5;
        string? appliedRequest = null;
        // Only the pinned typed exhausted-limit response needs experimental acknowledgement.
#pragma warning disable GHCP001
        UISessionLimitsExhaustedResponse? applied = null;
#pragma warning restore GHCP001
        decimal? verified = null;
        TaskCompletionSource applyEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource completeApply = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task applyOperation = Task.CompletedTask;

        Task renewal = NativeSdkSession.HandleExhaustedLimitAsync(
            "request-17",
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(backedCeiling);
            },
            (request, response, token) =>
            {
                token.ThrowIfCancellationRequested();
                appliedRequest = request;
                applied = response;
                applyOperation = completeApply.Task.WaitAsync(token);
                applyEntered.TrySetResult();
                return applyOperation;
            },
            (limit, token) =>
            {
                token.ThrowIfCancellationRequested();
                Assert.IsTrue(
                    applyOperation.IsCompletedSuccessfully,
                    "The Set operation must complete before verification."
                );
                Assert.IsNotNull(applied);
                verified = limit;
                return Task.CompletedTask;
            },
            context.CancellationToken
        );

        Exception? primaryFailure = null;
        try
        {
            await applyEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(5), context.CancellationToken
            );
            Assert.IsFalse(applyOperation.IsCompleted);
            Assert.IsFalse(renewal.IsCompleted);
            Assert.IsNotNull(applied);
            completeApply.TrySetResult();
            await renewal.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);

            Assert.AreEqual("request-17", appliedRequest);
#pragma warning disable GHCP001
            Assert.AreEqual(UISessionLimitsExhaustedResponseAction.Set, applied.Action);
#pragma warning restore GHCP001
            Assert.AreEqual<double?>((double)backedUnits / unitsPerCredit, applied.MaxAiCredits);
            Assert.AreEqual<decimal?>(backedCeiling, verified);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            completeApply.TrySetResult();
            try
            {
                await Task.WhenAll(renewal, applyOperation).WaitAsync(
                    TimeSpan.FromSeconds(5), CancellationToken.None
                );
            }
            catch (Exception exception) when (primaryFailure is not null)
            {
                context.WriteLine("Exhausted-limit renewal join failed: " + exception);
            }
        }
    }

    [TestMethod]
    public async Task HandleExhaustedLimitAsyncAdmissionClosesDuringRenewalDoesNotApplyNewCeiling()
    {
        ManualScenarioClock clock = new();
        using WorkAdmission admission = new(
            clock,
            clock.GetUtcNow().AddHours(1),
            context.CancellationToken
        );
        // Only the pinned typed exhausted-limit response needs experimental acknowledgement.
#pragma warning disable GHCP001
        UISessionLimitsExhaustedResponse? applied = null;
#pragma warning restore GHCP001
        decimal? verified = null;

        var error = await Assert.ThrowsExactlyAsync<NativeWorker.RenewalAdmissionCanceledException>(
            () =>
                NativeSdkSession.HandleExhaustedLimitAsync(
                    "request-closed",
                    async token =>
                    {
                        token.ThrowIfCancellationRequested();
                        await admission.CloseAsync();
                        return 125.5m;
                    },
                    (_, response, _) =>
                    {
                        applied = response;
                        return Task.CompletedTask;
                    },
                    (limit, _) =>
                    {
                        verified = limit;
                        return Task.CompletedTask;
                    },
                    context.CancellationToken,
                    admission
                )
        );

        Assert.AreEqual(admission.Token, error.CancellationToken);
        Assert.IsTrue(admission.IsClosed);
        Assert.IsNull(applied);
        Assert.IsNull(verified);
    }

    private static string MetadataDirectory() =>
        Path.Combine(Path.GetTempPath(), "lite-agensh-metadata-no-files");

    // This helper supplies the official pinned metadata DTO, not a second SDK transport.
#pragma warning disable GHCP001
    private static SessionMetadataSnapshot Metadata(string cwd) =>
        new()
        {
            SessionId = "session-local",
            WorkingDirectory = cwd,
            SelectedModel = "model-a",
            AlreadyInUse = false,
            IsRemote = false,
            SessionLimits = new() { MaxAiCredits = 100 },
        };

#pragma warning restore GHCP001

    private static SessionCompactionStartEvent Start(string attempt) =>
        new()
        {
            Id = Guid.Parse("80e25d83-2175-4a56-a813-2cf371600b91"),
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Data = new() { CompactionAttemptId = attempt },
        };

    private static SessionCompactionCompleteEvent Complete(string attempt, bool success) =>
        new()
        {
            Id = Guid.Parse("dd6179f0-07a1-4ce0-aec1-9ae87c711648"),
            Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero),
            Data = new() { CompactionAttemptId = attempt, Success = success },
        };
}
