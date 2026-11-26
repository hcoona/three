using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static LiteAgensh.Json;
using static LiteAgensh.Operation;

namespace LiteAgensh;

internal sealed class NativeQualification(
    RunDatabase database,
    IGitWorkspace git,
    CreditLedger credits,
    INativeWorkerFactory nativeWorkers,
    CoordinatorRecovery recovery,
    string nativeEvidenceKind
)
{
    private string PreflightReceiptKey =>
        nativeEvidenceKind == "native-sdk"
            ? RunMetadata.SdkPreflightReceiptKey
            : RunMetadata.ControlledPreflightReceiptKey;

    private string BillingReceiptKey =>
        nativeEvidenceKind == "native-sdk"
            ? RunMetadata.SdkBillingReceiptKey
            : RunMetadata.ControlledBillingReceiptKey;

    public async Task<JsonObject> PreflightAsync(CancellationToken cancellationToken)
    {
        JsonObject? diagnostic = null;
        JsonObject result;
        bool settlementFailed = false;
        Exception? persistenceError = null;
        try
        {
            await recovery.SettleAsync(cancellationToken, allowUnknownUsage: true)
                .ConfigureAwait(false);
            database.Meta.SetLifecycle("preflighting");
            await git.InitializeAsync(cancellationToken).ConfigureAwait(false);
            diagnostic = await nativeWorkers
                .PreflightAsync(
                    database.Configuration.WorkerIds.Take(2)
                        .Select(id => (id, git.GetWorkerPath(id))).ToArray(),
                    cancellationToken
                )
                .ConfigureAwait(false);
            result = Success(
                diagnostic,
                "Native peer identity, cwd, independent limits and session closure verified "
                    + "without model sends. "
                    + "Empty-session metrics are not measured zero-cost usage."
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            result = ExpectedFailure(exception);
        }
        finally
        {
            try
            {
                await nativeWorkers.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                settlementFailed = true;
                persistenceError = TryPersistUnresolved(
                    exception.Message,
                    nativeWorkers.SettlementFailures
                );
            }
        }

        if (!string.IsNullOrEmpty(database.Meta.OwnershipAmbiguity))
        {
            persistenceError ??= TryPersistUnresolved(database.Meta.OwnershipAmbiguity);
            settlementFailed = true;
        }

        if (
            settlementFailed
            || persistenceError is not null
            || database.Meta.RawLifecycle == "unresolved"
        )
        {
            string detail = persistenceError is null ? "" : $" ({persistenceError.Message})";
            return Failure(
                "unavailable",
                "Preflight did not establish native metadata and settled ownership together. "
                    + "No model prompt was sent; diagnostics and paid associations are retained."
                    + detail
            );
        }

        database.Meta.SetLifecycle("stopped");
        if (result["ok"]?.GetValue<bool>() == true)
        {
            database.Meta.WriteJson(
                PreflightReceiptKey,
                new JsonObject
                {
                    ["nativeEvidenceKind"] = nativeEvidenceKind,
                    ["binding"] = CreditPreflightBinding(),
                    ["sessionId"] = diagnostic?["sessionId"]?.DeepClone(),
                    ["sessions"] = diagnostic?["sessions"]?.DeepClone(),
                    ["verifiedAt"] = database.Now.ToString("O"),
                }
            );
        }

        return result;
    }

    public async Task<JsonObject> CalibrateAsync(CancellationToken cancellationToken)
    {
        INativeWorker? native = null;
        JsonObject result;
        CreditReservation? reservation = null;
        bool settled = false;
        bool disposalFailed = false;
        Exception? persistenceError = null;
        try
        {
            await recovery.SettleAsync(cancellationToken).ConfigureAwait(false);
            credits.RequirePaidAdmission();
            RequirePreflight();
            if (HasBillingReceipt())
            {
                return Success(
                    ReadBillingReceipt(),
                    "This exact subject already has a complete billing calibration."
                );
            }

            await git.InitializeAsync(cancellationToken).ConfigureAwait(false);
            reservation = credits.Reserve(
                database.RunId,
                "calibration",
                30,
                database.Configuration.RunAiCreditAllowance,
                database.CreateSdkEvidencePath("calibration")
            );
            native = nativeWorkers.Create(reservation, calibration: true);
            database.Meta.SetLifecycle("calibrating");
            await native
                .StartAsync(git.GetWorkerPath("worker1"), null, cancellationToken)
                .ConfigureAwait(false);
            credits.RequirePromptAllowance(
                database.RunId,
                native.ReservationId,
                database.Configuration.RunAiCreditAllowance
            );
            JsonObject response = await native
                .PromptAsync("Reply with exactly READY and nothing else.", cancellationToken)
                .ConfigureAwait(false);
            await native.VerifyCreditsAsync(cancellationToken).ConfigureAwait(false);
            if (response["text"]?.GetValue<string>().Trim() != "READY")
            {
                throw new OperationException(
                    "calibration_failed",
                    "The single bounded calibration response was not READY."
                );
            }

            result = Success(new { sessionId = native.SessionId });
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            result = ExpectedFailure(exception);
        }
        finally
        {
            try
            {
                if (native is not null)
                {
                    try
                    {
                        await native.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (IsExpected(exception))
                    {
                        disposalFailed = true;
                        persistenceError ??= TryPersistUnresolved(exception.Message);
                    }
                }
            }
            finally
            {
                try
                {
                    await nativeWorkers.CompleteAsync().ConfigureAwait(false);
                    settled =
                        native is not null
                        && !disposalFailed
                        && persistenceError is null
                        && database.Meta.RawLifecycle != "unresolved";
                }
                catch (Exception exception) when (IsExpected(exception))
                {
                    settled = false;
                    persistenceError ??= TryPersistUnresolved(
                        exception.Message,
                        nativeWorkers.SettlementFailures
                    );
                }
            }
        }

        if ((reservation is not null && !settled) || persistenceError is not null)
        {
            string detail = persistenceError is null ? "" : $" ({persistenceError.Message})";
            return Failure(
                "credit_or_ownership_unknown",
                "Calibration did not reconcile complete usage and settle its native process."
                    + detail
            );
        }

        if (
            result["ok"]?.GetValue<bool>() == true
            && reservation is not null
            && credits.HasCompleteSdkUsage(reservation.Id)
        )
        {
            database.Meta.WriteJson(
                BillingReceiptKey,
                new JsonObject
                {
                    ["nativeEvidenceKind"] = nativeEvidenceKind,
                    ["binding"] = CreditPreflightBinding(),
                    ["reservationId"] = reservation.Id,
                    ["sessionId"] = native?.SessionId,
                    ["verifiedAt"] = database.Now.ToString("O"),
                }
            );
        }

        if (settled)
        {
            database.Meta.SetLifecycle("stopped");
        }

        return result;
    }

    public JsonNode? ReadPreflightReceipt() => database.Meta.ReadJson(PreflightReceiptKey);

    public JsonNode? ReadBillingReceipt() => database.Meta.ReadJson(BillingReceiptKey);

    public void RequireDispatchQualification()
    {
        credits.RequirePaidAdmission();
        RequirePreflight();
        if (!HasBillingReceipt())
        {
            throw new OperationException(
                "unavailable",
                "A complete paid calibration for this exact build/SDK/runtime/configuration "
                    + "is required before ordinary model dispatch."
            );
        }
    }

    private Exception? TryPersistUnresolved(
        string failureMessage,
        JsonNode? settlementFailures = null
    )
    {
        Exception? captured = null;
        try
        {
            database.Meta.SetLifecycle("unresolved");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            captured ??= exception;
        }

        try
        {
            database.Meta.Failure = failureMessage;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            captured ??= exception;
        }

        if (settlementFailures is not null)
        {
            try
            {
                database.Meta.WriteJson(RunMetadata.SettlementFailuresKey, settlementFailures);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                captured ??= exception;
            }
        }

        return captured;
    }

    private string CreditPreflightBinding()
    {
        string subject = Json.SerializePayload(
                new
                {
                    sdk = NativeSdkSession.SdkVersion,
                    accounting = "sdk-session-cumulative-nano-aiu-complete-v1",
                    nativeEvidenceKind,
                    build = typeof(Coordinator).Assembly.ManifestModule.ModuleVersionId,
                    database.Configuration.NativeExecutable,
                    database.Configuration.NativeArguments,
                    database.Configuration.NativeModel,
                    database.Configuration.NativePermissionPolicy,
                    database.Configuration.CreditAllocation,
                    database.Configuration.RunAiCreditAllowance,
                    database.Configuration.CampaignAiCreditAllowance,
                    database.Configuration.CampaignLedgerPath,
                    database.Configuration.WorkerCount,
                    database.Configuration.WorkerStartIntervalSeconds,
                    database.Configuration.RepositoryPath,
                    campaignIdentity = credits.Identity,
                }
            )
            .ToJsonString();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
    }

    private void RequirePreflight()
    {
        JsonObject? receipt = ReadPreflightReceipt()?.AsObject();
        if (
            receipt?["binding"]?.GetValue<string>() != CreditPreflightBinding()
            || receipt?["nativeEvidenceKind"]?.GetValue<string>() != nativeEvidenceKind
        )
        {
            throw new OperationException(
                "unavailable",
                "A successful non-model SDK metadata preflight is required for this exact "
                    + "build, SDK, native invocation and accounting contract."
            );
        }
    }

    private bool HasBillingReceipt()
    {
        JsonObject? receipt = ReadBillingReceipt() as JsonObject;
        string? reservationId = receipt?["reservationId"]?.GetValue<string>();
        return receipt?["binding"]?.GetValue<string>() == CreditPreflightBinding()
            && receipt?["nativeEvidenceKind"]?.GetValue<string>() == nativeEvidenceKind
            && reservationId is not null
            && credits.HasCompleteSdkUsage(reservationId)
            && credits
                .Reservations(database.RunId)
                .Any(row =>
                    row.Id == reservationId
                    && row.Worker == "calibration"
                    && row.Allowance == 30
                    && row.SessionId == receipt?["sessionId"]?.GetValue<string>()
                );
    }
}
