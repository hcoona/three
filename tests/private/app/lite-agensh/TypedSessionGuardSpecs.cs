using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using LiteAgensh.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class TypedSessionGuardSpecs(TestContext context)
{
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
        TaskCompletionSource<NativeUsage> never = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken observed = default;
        Task<NativeUsage> probe = NativeSdkSession.ReadProbeAsync(
            async token =>
            {
                observed = token;
                entered.SetResult();
                return await never.Task.WaitAsync(token);
            },
            context.CancellationToken,
            () => "usage",
            clock
        );
        await entered.Task.WaitAsync(context.CancellationToken);

        clock.Advance(TimeSpan.FromSeconds(30) - TimeSpan.FromTicks(1));
        Assert.IsFalse(observed.IsCancellationRequested);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.IsTrue(observed.IsCancellationRequested);
        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() => probe);

        Assert.AreEqual("native_probe_failed", error.Code);
        Assert.AreEqual("Native usage query exceeded its 30-second deadline.", error.Message);
        Assert.IsFalse(context.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task ReadProbeAsyncCallerCancellationPreservesRequestTokenAndCause()
    {
        ManualScenarioClock clock = new();
        using CancellationTokenSource request = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken
        );
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<NativeUsage> never = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        CancellationToken observed = default;
        Task<NativeUsage> probe = NativeSdkSession.ReadProbeAsync(
            async token =>
            {
                observed = token;
                entered.SetResult();
                return await never.Task.WaitAsync(token);
            },
            request.Token,
            timeProvider: clock
        );
        await entered.Task.WaitAsync(context.CancellationToken);

        request.Cancel();
        var error =
            await Assert.ThrowsExactlyAsync<NativeSdkSession.RequestedReadCanceledException>(() =>
                probe
            );

        Assert.AreEqual(request.Token, error.RequestToken);
        Assert.AreEqual(observed, error.CancellationToken);
        var cause = Assert.IsInstanceOfType<OperationCanceledException>(error.InnerException);
        Assert.AreEqual(observed, cause.CancellationToken);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), clock.GetUtcNow());
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
    public void NativeLimitUnrepresentableIntegerNeverRaisesAuthorizedCeiling()
    {
        double limit = NativeSdkSession.NativeLimit(9_007_199_254_740_993m);

        Assert.AreEqual(9_007_199_254_740_992d, limit);
        Assert.IsTrue((decimal)limit <= 9_007_199_254_740_993m);
        Assert.AreEqual(100.5d, NativeSdkSession.NativeLimit(100.5m));
    }

    [TestMethod]
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
    public async Task NativeUsageFenceWorkChangesInsideReadRejectsStaleCapture()
    {
        NativeUsageFence fence = new();

        OperationException error = await Assert.ThrowsExactlyAsync<OperationException>(() =>
            fence.CaptureAsync(
                _ =>
                {
                    fence.Observe(Start("compact-new"));
                    fence.Observe(Complete("compact-new", success: true));
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
        string? appliedRequest = null;
        // Only the pinned typed exhausted-limit response needs experimental acknowledgement.
#pragma warning disable GHCP001
        UISessionLimitsExhaustedResponse? applied = null;
#pragma warning restore GHCP001
        decimal? verified = null;

        await NativeSdkSession.HandleExhaustedLimitAsync(
            "request-17",
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(125.5m);
            },
            (request, response, token) =>
            {
                token.ThrowIfCancellationRequested();
                appliedRequest = request;
                applied = response;
                return Task.CompletedTask;
            },
            (limit, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (applied is null)
                {
                    throw new InvalidOperationException(
                        "A ceiling must be applied before verification."
                    );
                }

                verified = limit;
                return Task.CompletedTask;
            },
            context.CancellationToken
        );

        Assert.AreEqual("request-17", appliedRequest);
        Assert.IsNotNull(applied);
#pragma warning disable GHCP001
        Assert.AreEqual(UISessionLimitsExhaustedResponseAction.Set, applied.Action);
#pragma warning restore GHCP001
        Assert.AreEqual<double?>(125.5d, applied.MaxAiCredits);
        Assert.AreEqual<decimal?>(125.5m, verified);
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
