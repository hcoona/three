using System.Text.Json.Nodes;
using GitHub.Copilot.Rpc;
using Microsoft.Data.Sqlite;

namespace LiteAgensh.Tests.Support;

// These consumers need session contracts and SQLite, not an operating-system host.
// Lease zero deliberately cannot qualify verified never-dispatched cancellation.
internal sealed class TypedHostControl : INativeSdkHost, IDisposable
{
    private readonly ScratchDirectory scratch = new();
    private readonly TaskCompletionSource release = NewSignal();
    private readonly string? initialUsageStatus;
    private bool disposed;

    internal TypedHostControl(decimal creditAllowance = 400, string? initialUsageStatus = null)
    {
        this.initialUsageStatus = initialUsageStatus;
        CreditAllowance = creditAllowance;
        RunConfiguration configuration = new()
        {
            RepositoryPath = scratch.PathFor("checkout"),
            StatePath = scratch.PathFor("run"),
            CampaignLedgerPath = scratch.PathFor("campaign.sqlite"),
            Goal = "Preserve exact native associations before sending.",
            CreditAllocation = "shared",
            NativeModel = "controlled-model",
            RunAiCreditAllowance = creditAllowance,
            CampaignAiCreditAllowance = creditAllowance,
        };
        Database = new(configuration.StatePath, configuration);
        Ledger = new(configuration.CampaignLedgerPath, configuration.CampaignAiCreditAllowance);
        ProcessRunner processes = new(Database);
        Factory = new(Database, processes, Ledger, () => this);
        CollaborationOperations collaboration = new(Database);
        // Construction creates the application's actual association schema only.
        // No coordinator operation, Git operation or process is executed.
        _ = new Coordinator(
            Database,
            collaboration,
            new GitOperations(Database, collaboration),
            (_, _, _) => Factory
        );
    }

    public string Id { get; } = Guid.NewGuid().ToString("N");

    public long LeaseId => 0;

    internal RunDatabase Database { get; }

    internal CreditLedger Ledger { get; }

    internal decimal CreditAllowance { get; }

    internal NativeWorkerFactory Factory { get; }

    internal List<TypedSession> Sessions { get; } = [];

    internal TaskCompletionSource SettlementEntered { get; } = NewSignal();

    internal bool HoldSettlement { get; set; }

    internal OperationException? NextUnsupportedCap { get; set; }

    internal bool NextMismatchAppliedCap { get; set; }

    internal string Cwd(string worker) => scratch.PathFor(worker);

    internal CreditReservation Reserve(string worker) =>
        Ledger.Reserve(
            Database.RunId,
            worker,
            0,
            CreditAllowance,
            scratch.PathFor(worker + ".usage"),
            shared: true
        );

    internal CreditReservation Read(CreditReservation reservation) =>
        Ledger.Reservations(Database.RunId).Single(row => row.Id == reservation.Id);

    internal JsonObject Accounting => Ledger.Status(Database.RunId, CreditAllowance);

    internal object? RunValue(string statement, string id)
    {
        using SqliteConnection connection = Database.Open();
        return Sql.Scalar(connection, statement, null, ("$id", id));
    }

    internal void RejectAssociationWrite(string table, CreditReservation reservation)
    {
        string id = reservation.Id.Replace("'", "''", StringComparison.Ordinal);
        string path = reservation.UsagePath.Replace("'", "''", StringComparison.Ordinal);
        string worker = reservation.Worker.Replace("'", "''", StringComparison.Ordinal);
        string session = reservation.SessionId!.Replace("'", "''", StringComparison.Ordinal);
        string statement = table switch
        {
            "native_attempts" => $"""
                CREATE TRIGGER reject_owned_association BEFORE INSERT ON native_attempts
                WHEN NEW.reservation_id='{id}'
                BEGIN
                    SELECT RAISE(ABORT, 'Controlled first association write rejected.');
                END;
                """,
            "native_sessions" => $"""
                CREATE TRIGGER reject_owned_association BEFORE INSERT ON native_sessions
                WHEN NEW.reservation_id='{id}'
                BEGIN
                    SELECT RAISE(ABORT,
                        'Controlled second association write rejected after matching attempt.')
                    WHERE EXISTS(
                        SELECT 1 FROM native_attempts
                        WHERE usage_path='{path}' AND reservation_id=NEW.reservation_id
                            AND worker='{worker}' AND session_id='{session}');
                    SELECT RAISE(ABORT, 'Controlled association prerequisite absent.');
                END;
                """,
            "meta" => $"""
                CREATE TRIGGER reject_owned_association BEFORE INSERT ON meta
                WHEN NEW.key='worker:{worker}:session' AND NEW.value='{session}'
                BEGIN
                    SELECT RAISE(ABORT,
                        'Controlled worker binding write rejected after matching associations.')
                    WHERE EXISTS(
                        SELECT 1 FROM native_attempts
                        WHERE usage_path='{path}' AND reservation_id='{id}'
                            AND worker='{worker}' AND session_id='{session}')
                        AND EXISTS(
                            SELECT 1 FROM native_sessions
                            WHERE reservation_id='{id}' AND worker='{worker}'
                                AND session_id='{session}');
                    SELECT RAISE(ABORT, 'Controlled association prerequisite absent.');
                END;
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        using SqliteConnection connection = Database.Open();
        Sql.Execute(connection, statement);
    }

    public INativeSdkSession CreateSession(string cwd, bool isolated)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        TypedSession session = new(this, Path.GetFullPath(cwd))
        {
            UnsupportedCap = NextUnsupportedCap,
            MismatchAppliedCap = NextMismatchAppliedCap,
        };
        NextUnsupportedCap = null;
        NextMismatchAppliedCap = false;
        Sessions.Add(session);
        return session;
    }

    internal void ReleaseSettlement() => release.TrySetResult();

    public async ValueTask DisposeAsync()
    {
        SettlementEntered.TrySetResult();
        if (HoldSettlement)
        {
            await release
                .Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        disposed = true;
    }

    public void Dispose()
    {
        ReleaseSettlement();
        scratch.Dispose();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed class TypedSession(TypedHostControl host, string cwd) : INativeSdkSession
    {
        private readonly NativeUsageFence fence = new();
        private decimal? appliedLimit;
        private NativeUsage? usage;

        public string SessionId { get; private set; } = "";

        public bool HasSession { get; private set; }

        public string? NativeFailure => FirstFailure?.Message;

        public Exception? FirstFailure { get; private set; }

        public JsonObject? Metadata { get; private set; }

        internal OperationException? UnsupportedCap { get; set; }

        internal bool MismatchAppliedCap { get; set; }

        internal bool FailOnlyOnUpdate { get; set; }

        internal bool Closed { get; private set; }

        internal JsonObject? DeliveredOperation { get; private set; }

        public Task OpenAsync(
            string worker,
            string sessionId,
            decimal? limit,
            string? exactSession,
            string evidencePath,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            SessionId = sessionId;
            HasSession = true;
            appliedLimit = limit + (MismatchAppliedCap ? 1 : 0);
            usage = new(sessionId, 0, host.initialUsageStatus, 0);
            host.Database.WriteMetadata(
                $"native:runtime:{host.Id}:session:{sessionId}",
                new JsonObject
                {
                    ["runtimeId"] = host.Id,
                    ["runtimeLeaseId"] = host.LeaseId,
                    ["sessionId"] = sessionId,
                    ["worker"] = worker,
                    ["workingDirectory"] = cwd,
                }.ToJsonString()
            );
            return Task.CompletedTask;
        }

        public Task<NativeUsage> ReadAsync(
            string expectedSession,
            decimal? expectedLimit,
            CancellationToken cancellationToken,
            bool validateEvents = true
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UnsupportedCap is not null && !FailOnlyOnUpdate)
            {
                FirstFailure ??= UnsupportedCap;
                throw UnsupportedCap;
            }

            // Independent applied state is not copied from the read's expectation.
            // The product's actual typed metadata guard runs inside this contract.
#pragma warning disable GHCP001
            SessionMetadataSnapshot snapshot = new()
            {
                SessionId = SessionId,
                WorkingDirectory = cwd,
                SelectedModel = "controlled-model",
                AlreadyInUse = false,
                IsRemote = false,
                SessionLimits = new()
                {
                    MaxAiCredits = appliedLimit is decimal cap
                        ? NativeSdkSession.NativeLimit(cap)
                        : null,
                },
            };
            try
            {
                NativeSdkSession.ValidateMetadata(
                    snapshot,
                    SessionId,
                    expectedSession,
                    cwd,
                    expectedLimit,
                    "controlled-model"
                );
            }
            catch (Exception exception)
            {
                FirstFailure ??= exception;
                throw;
            }
            Metadata = new()
            {
                ["sessionId"] = snapshot.SessionId,
                ["workingDirectory"] = snapshot.WorkingDirectory,
                ["selectedModel"] = snapshot.SelectedModel,
                ["alreadyInUse"] = snapshot.AlreadyInUse,
                ["isRemote"] = snapshot.IsRemote,
                ["appliedLimit"] = snapshot.SessionLimits.MaxAiCredits,
                ["runtimeId"] = host.Id,
                ["runtimeLeaseId"] = host.LeaseId,
                ["runtimeVersion"] = Phase6NativeControl.RuntimeVersion,
                ["sdkVersion"] = NativeSdkSession.SdkVersion,
            };
#pragma warning restore GHCP001
            return Task.FromResult(
                usage ?? throw new InvalidOperationException("Session not opened.")
            );
        }

        public Task<NativeUsage> UpdateLimitAsync(
            string expectedSession,
            decimal limit,
            CancellationToken cancellationToken
        )
        {
            if (UnsupportedCap is not null)
            {
                FirstFailure ??= UnsupportedCap;
                throw UnsupportedCap;
            }
            appliedLimit = limit + (MismatchAppliedCap ? 1 : 0);
            return ReadAsync(expectedSession, limit, cancellationToken);
        }

        public Task<NativeUsage> ReadSettledAsync(
            string expectedSession,
            decimal? expectedLimit,
            CancellationToken cancellationToken,
            bool validateEvents = true,
            bool verifyLocalWork = false
        ) =>
            fence.CaptureAsync(
                token => ReadAsync(expectedSession, expectedLimit, token, validateEvents),
                cancellationToken
            );

        public void WithSettledUsage(NativeUsage reading, Action record) =>
            fence.Use(reading, record);

        public void MarkDispatchPending() => fence.SetTurnPending(true);

        public Task<JsonObject> SendAsync(
            string text,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<decimal>>? renewLimit = null,
            WorkAdmission? admission = null
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            admission?.ThrowIfClosed();
            usage = new(SessionId, 7_250_000_000, "complete", 1);
            DeliveredOperation = new()
            {
                ["sessionId"] = SessionId,
                ["acceptedText"] = text,
                ["result"] = "controlled-peer-completed",
            };
            fence.SetTurnPending(false);
            return Task.FromResult(DeliveredOperation.DeepClone().AsObject());
        }

        public Task AbortAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void Quiesce() { }

        public void RequireHealthy()
        {
            if (FirstFailure is not null)
            {
                throw FirstFailure;
            }
        }

        public ValueTask DisposeAsync()
        {
            Closed = true;
            return ValueTask.CompletedTask;
        }

        public Task<OriginalNativeShutdown> ReadOriginalShutdownAsync(
            NativeRecoveryOrigin origin,
            string evidencePath,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException("Historical recovery is outside these consumers.");
    }
}
