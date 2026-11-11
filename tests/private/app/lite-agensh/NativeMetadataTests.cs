using System.Text.Json.Nodes;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class NativeMetadataTests(TestContext testContext)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SendFailureScopeDoesNotDiscardUnrelatedCancellation(bool callerCanceled)
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        await using NativeSdkSession native = new(database, new ProcessRunner(database),
            fixture.RepositoryPath, isolated: false);
        using CancellationTokenSource caller = new();
        using CancellationTokenSource unrelated = new();
        await unrelated.CancelAsync();
        if (callerCanceled)
        {
            await caller.CancelAsync();
        }
        OperationCanceledException cause = new(
            callerCanceled ? caller.Token : unrelated.Token);
        bool retainedBeforeAbort = false;
        bool cleaned = false;
        OperationCanceledException observed =
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                native.CompleteSendAsync(() => Task.FromException<JsonObject>(cause),
                    () =>
                    {
                        retainedBeforeAbort = ReferenceEquals(cause, native.FirstFailure);
                        return Task.CompletedTask;
                    },
                    () =>
                    {
                        cleaned = true;
                        return Task.CompletedTask;
                    }, caller.Token));
        Assert.AreEqual(callerCanceled ? caller.Token : unrelated.Token,
            observed.CancellationToken);
        Assert.AreEqual(!callerCanceled, retainedBeforeAbort);
        Assert.IsTrue(cleaned);
        if (callerCanceled)
        {
            Assert.IsNull(native.FirstFailure);
        }
        else
        {
            Assert.AreSame(cause, observed);
            Assert.AreSame(cause, native.FirstFailure);
            Assert.AreEqual(false, cause.Data["liteagensh.nativeFailureCancelled"]);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task NativeCleanupRetainsItsCauseWithoutReplacingCallerCancellation(
        bool nativeError
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        await using NativeSdkSession native = new(
            database,
            new ProcessRunner(database),
            fixture.RepositoryPath,
            isolated: false
        );
        if (nativeError)
        {
            native.RecordEvent(SessionEvent.FromJson(
                """
                {
                  "id":"00000000-0000-0000-0000-000000000001",
                  "timestamp":"2026-01-01T00:00:00Z",
                  "type":"session.error",
                  "data":{"errorType":"query","message":"Prompt exceeds model input budget."}
                }
                """
            )!);
        }

        string? failure = native.NativeFailure;
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(
            token
        );
        TaskCompletionSource abortEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseAbort = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task cleanup = native.AbortFailedSendAsync(
            async () =>
            {
                abortEntered.TrySetResult();
                await releaseAbort.Task.WaitAsync(token);
            },
            stop.Token
        );
        try
        {
            await abortEntered.Task.WaitAsync(token);
            await stop.CancelAsync();
            Assert.IsFalse(cleanup.IsCompleted);
        }
        finally
        {
            releaseAbort.TrySetResult();
        }

        var canceled = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => cleanup);
        Assert.AreEqual(stop.Token, canceled.CancellationToken);
        Assert.AreEqual(failure, native.NativeFailure);
        if (nativeError)
        {
            Assert.Contains("Prompt exceeds model input budget.", native.NativeFailure!);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeErrorPreservesItsReasonUnlessTheCallerActuallyCanceled(bool canceled)
    {
        using RunFixture fixture = new();
        RunDatabase database = fixture.CreateDatabase();
        await using NativeSdkSession native = new(
            database,
            new ProcessRunner(database),
            fixture.RepositoryPath,
            isolated: false
        );
        SessionEvent error = SessionEvent.FromJson(
            """
            {
              "id":"00000000-0000-0000-0000-000000000001",
              "timestamp":"2026-01-01T00:00:00Z",
              "type":"session.error",
              "data":{
                "errorType":"query",
                "message":"Prompt exceeds model input budget.",
                "originatingMessageId":"00000000-0000-0000-0000-000000000002"
              }
            }
            """
        )!;
        native.RecordEvent(error);
        native.RecordEvent(SessionEvent.FromJson(
            """
            {
              "id":"00000000-0000-0000-0000-000000000003",
              "timestamp":"2026-01-01T00:00:01Z",
              "type":"session.error",
              "data":{"errorType":"query","message":"Later abort error."}
            }
            """
        )!);
        using CancellationTokenSource stop = new();
        if (canceled)
        {
            await stop.CancelAsync();
            var interrupted = Assert.ThrowsExactly<OperationCanceledException>(
                () => native.CheckEvents(stop.Token)
            );
            Assert.AreEqual(stop.Token, interrupted.CancellationToken);
        }
        else
        {
            OperationException failed = Assert.ThrowsExactly<OperationException>(
                () => native.CheckEvents(stop.Token)
            );
            Assert.AreEqual("native_stopped", failed.Code);
            Assert.Contains("query", failed.Message);
            Assert.Contains("Prompt exceeds model input budget.", failed.Message);
            Assert.Contains("00000000-0000-0000-0000-000000000002", failed.Message);
            Assert.DoesNotContain("Later abort error", failed.Message);
        }
        OperationException original =
            Assert.IsInstanceOfType<OperationException>(native.FirstFailure);
        Assert.Contains("Prompt exceeds model input budget.", original.Message);
        Assert.IsNull(original.Data["liteagensh.nativeFailureCancelled"]);
        await native.DisposeAsync();
        Assert.AreSame(original, native.FirstFailure);
    }

    // Pinned SDK DTOs used by the real application's metadata guard.
#pragma warning disable GHCP001
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExpiredAdmissionCannotIncreaseAnExhaustedNativeCap(bool closeDuringRenewal)
    {
        CancellationToken token = testContext.CancellationToken;
        ScenarioClock clock = new();
        using WorkAdmission admission = new(clock, clock.GetUtcNow().AddSeconds(1), token);
        double? appliedLimit = 100;
        if (!closeDuringRenewal)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
        }
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            NativeSdkSession.HandleExhaustedLimitAsync(
                "controlled-expiry",
                _ =>
                {
                    clock.Advance(TimeSpan.FromSeconds(2));
                    return Task.FromResult(200m);
                },
                (_, response, _) =>
                {
                    appliedLimit = response.MaxAiCredits;
                    return Task.CompletedTask;
                },
                (_, _) => Task.CompletedTask,
                token,
                admission
            )
        );
        Assert.AreEqual(100d, appliedLimit);
    }

    [TestMethod]
    [DataRow("supported")]
    [DataRow("unsupported")]
    [DataRow("mismatched")]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task ExhaustedSendUsesBackedFiniteSetAndRetainsBackingOnApplicationFailure(
        string application
    )
    {
        CancellationToken token = testContext.CancellationToken;
        using RunFixture fixture = new();
        CreditLedger ledger = new(fixture.Configuration.CampaignLedgerPath, 200);
        CreditReservation first = ledger.Reserve(
            "shared", "worker1", 0, 200,
            Path.Combine(fixture.StatePath, "exhausted"), shared: true
        );
        CreditReservation second = ledger.Reserve(
            "shared", "worker2", 0, 200,
            Path.Combine(fixture.StatePath, "waiting"), shared: true
        );
        await ledger.AcquireSharedWindowAsync(first.Id, token);
        ledger.MarkStarted(first.Id);
        ledger.AttachSession(first.Id, first.SessionId!);
        ledger.BeginDispatch("shared", first.Id, 200);
        double? appliedLimit = 100;
        Task handling = NativeSdkSession.HandleExhaustedLimitAsync(
            "controlled-request",
            async cancellationToken =>
            {
                ledger.RecordSdkUsage(
                    first.Id, new(first.SessionId!, 110_000_000_000, "complete", 1)
                );
                CreditReservation renewed = await ledger.AcquireSharedWindowAsync(
                    first.Id, cancellationToken, renew: true
                );
                return renewed.Allowance;
            },
            (requestId, response, _) =>
            {
                Assert.AreEqual("controlled-request", requestId);
                Assert.AreEqual(UISessionLimitsExhaustedResponseAction.Set, response.Action);
                Assert.AreEqual(200d, response.MaxAiCredits);
                if (application == "unsupported")
                {
                    throw new OperationException("unavailable", "Controlled limit rejection.");
                }
                appliedLimit = application == "mismatched" ? 201 : response.MaxAiCredits;
                return Task.CompletedTask;
            },
            (limit, _) =>
            {
                NativeSdkSession.ValidateMetadata(
                    new SessionMetadataSnapshot
                    {
                        SessionId = first.SessionId!,
                        WorkingDirectory = fixture.RepositoryPath,
                        SessionLimits = new SessionLimitsConfig { MaxAiCredits = appliedLimit },
                    },
                    first.SessionId!, first.SessionId!, fixture.RepositoryPath, limit
                );
                return Task.CompletedTask;
            },
            token
        );
        if (application == "supported")
        {
            await handling;
        }
        else
        {
            Assert.AreEqual(
                application == "unsupported" ? "unavailable" : "credit_unknown",
                (await Assert.ThrowsExactlyAsync<OperationException>(() => handling)).Code
            );
        }

        CreditReservation retained = ledger.Reservations().Single(row => row.Id == first.Id);
        Assert.IsTrue(retained.GrantOpen);
        Assert.AreEqual(200m, retained.Allowance);
        Assert.AreEqual(110m, retained.Actual);
        Assert.AreEqual(1L, ledger.ReadCompleteSdkUsage(first.Id)!.Value.Dispatches);
        if (application != "supported")
        {
            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(
                token
            );
            Task<CreditReservation> waiting = ledger.AcquireSharedWindowAsync(
                second.Id, stop.Token
            );
            Assert.IsFalse(waiting.IsCompleted);
            await stop.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => waiting);
            return;
        }

        ledger.RecordSdkUsage(first.Id, new(first.SessionId!, 150_000_000_000, "complete", 1));
        ledger.CloseSharedWindow(first.Id);
        Assert.AreEqual(1L, ledger.ReadCompleteSdkUsage(first.Id)!.Value.Dispatches);
        Assert.AreEqual(50m, (await ledger.AcquireSharedWindowAsync(second.Id, token)).Allowance);
    }

    [TestMethod]
    [DataRow("gpt-6.1-terra", true)]
    [DataRow("Gpt-6.1-Terra", true)]
    [DataRow("another-model", false)]
    [DataRow(null, false)]
    public void RequestedNativeModelRequiresMatchingReadback(string? selected, bool accepted)
    {
        using RunFixture fixture = new();
        SessionMetadataSnapshot metadata = new()
        {
            SessionId = "controlled-session",
            WorkingDirectory = fixture.RepositoryPath,
            SessionLimits = new SessionLimitsConfig { MaxAiCredits = 30 },
            SelectedModel = selected,
        };
        void Validate() => NativeSdkSession.ValidateMetadata(
            metadata, "controlled-session", "controlled-session", fixture.RepositoryPath,
            30, "gpt-6.1-terra"
        );
        if (accepted)
        {
            Validate();
        }
        else
        {
            Assert.AreEqual(
                "credit_unknown",
                Assert.ThrowsExactly<OperationException>(Validate).Code
            );
        }
    }

    [TestMethod]
    [DataRow("metadata-session")]
    [DataRow("active-session")]
    [DataRow("cwd")]
    [DataRow("owned")]
    [DataRow("remote")]
    [DataRow("missing-limits")]
    [DataRow("unknown-limit")]
    [DataRow("lower-limit")]
    [DataRow("higher-limit")]
    [DataRow("adjacent-limit")]
    public void RealMetadataGuardRejectsMismatchAndAcceptsMatchingIdentityAndLimit(string scenario)
    {
        using RunFixture fixture = new();
        SessionMetadataSnapshot metadata = new()
        {
            SessionId = "controlled-session",
            WorkingDirectory = fixture.RepositoryPath,
            SessionLimits = new SessionLimitsConfig { MaxAiCredits = 60 },
        };
        string active = "controlled-session";
        switch (scenario)
        {
            case "metadata-session":
                metadata.SessionId = "other-session";
                break;
            case "active-session":
                active = "other-session";
                break;
            case "cwd":
                metadata.WorkingDirectory = fixture.StatePath;
                break;
            case "owned":
                metadata.AlreadyInUse = true;
                break;
            case "remote":
                metadata.IsRemote = true;
                break;
            case "missing-limits":
                metadata.SessionLimits = null;
                break;
            case "unknown-limit":
                metadata.SessionLimits!.MaxAiCredits = null;
                break;
            case "lower-limit":
                metadata.SessionLimits!.MaxAiCredits = 59;
                break;
            case "higher-limit":
                metadata.SessionLimits!.MaxAiCredits = 61;
                break;
            case "adjacent-limit":
                metadata.SessionLimits!.MaxAiCredits = Math.BitIncrement(60);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.ValidateMetadata(
                metadata,
                active,
                "controlled-session",
                fixture.RepositoryPath,
                60
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
        NativeSdkSession.ValidateMetadata(
            new SessionMetadataSnapshot
            {
                SessionId = "controlled-session",
                WorkingDirectory =
                    fixture.RepositoryPath.ToUpperInvariant() + Path.DirectorySeparatorChar,
                SessionLimits = new SessionLimitsConfig { MaxAiCredits = 60 },
            },
            "controlled-session",
            "controlled-session",
            fixture.RepositoryPath,
            60
        );
    }

    [TestMethod]
    public void FractionalAppliedCapUsesTheRealConservativeConversion()
    {
        using RunFixture fixture = new();
        SessionMetadataSnapshot metadata = new()
        {
            SessionId = "controlled-session",
            WorkingDirectory = fixture.RepositoryPath,
            SessionLimits = new SessionLimitsConfig { MaxAiCredits = 30.12345678901229d },
        };
        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.ValidateMetadata(
                metadata,
                "controlled-session",
                "controlled-session",
                fixture.RepositoryPath,
                30.12345678901229m
            )
        );
        Assert.AreEqual("credit_unknown", error.Code);
        metadata.SessionLimits.MaxAiCredits = 30.123456789012287d;
        NativeSdkSession.ValidateMetadata(
            metadata,
            "controlled-session",
            "controlled-session",
            fixture.RepositoryPath,
            30.12345678901229m
        );
    }

    [TestMethod]
    public void OptionalRecoveryCapDoesNotWaiveIdentityOrOwnershipValidation()
    {
        using RunFixture fixture = new();
        SessionMetadataSnapshot metadata = new()
        {
            SessionId = "controlled-session",
            WorkingDirectory = fixture.RepositoryPath,
            SessionLimits = null,
        };
        NativeSdkSession.ValidateMetadata(
            metadata,
            "controlled-session",
            "controlled-session",
            fixture.RepositoryPath,
            null
        );
        metadata.AlreadyInUse = true;

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            NativeSdkSession.ValidateMetadata(
                metadata,
                "controlled-session",
                "controlled-session",
                fixture.RepositoryPath,
                null
            )
        );

        Assert.AreEqual("credit_unknown", error.Code);
    }
#pragma warning restore GHCP001
}
