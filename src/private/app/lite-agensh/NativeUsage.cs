namespace LiteAgensh;

internal sealed record RecoveredNativeUsage(CreditReservation Reservation, NativeUsage Usage);

internal sealed record NativeRecoveryOrigin(
    string SessionId,
    string RuntimeVersion,
    string RuntimeId,
    long RuntimeLeaseId,
    long SegmentId,
    DateTimeOffset AssociationTime,
    DateTimeOffset RuntimeStartTime,
    DateTimeOffset SegmentStartTime,
    DateTimeOffset SegmentEndTime
);

internal sealed record OriginalNativeShutdown(
    Guid EventId,
    DateTimeOffset Timestamp,
    long SessionStartTime,
    double TotalNanoAiu,
    IReadOnlyDictionary<string, long> Watermarks
)
{
    internal System.Text.Json.Nodes.JsonObject Evidence(NativeRecoveryOrigin origin) => new()
    {
        ["sessionId"] = origin.SessionId,
        ["originalRuntimeVersion"] = origin.RuntimeVersion,
        ["originalRuntimeId"] = origin.RuntimeId,
        ["originalRuntimeLeaseId"] = origin.RuntimeLeaseId,
        ["originalSegmentId"] = origin.SegmentId,
        ["associationTime"] = origin.AssociationTime.ToString("O"),
        ["runtimeStartTime"] = origin.RuntimeStartTime.ToString("O"),
        ["segmentStartTime"] = origin.SegmentStartTime.ToString("O"),
        ["segmentEndTime"] = origin.SegmentEndTime.ToString("O"),
        ["eventId"] = EventId.ToString("D"),
        ["timestamp"] = Timestamp.ToString("O"),
        ["sessionStartTime"] = SessionStartTime,
        ["shutdownType"] = "routine",
        ["totalNanoAiu"] = TotalNanoAiu,
        ["usageAccountingWatermarks"] = System.Text.Json.JsonSerializer.SerializeToNode(Watermarks),
        ["evidenceLevel"] = "original-native-termination-not-provider-final",
    };
}

public sealed record NativeUsage(
    string SessionId,
    double? TotalNanoAiu,
    string? Status,
    long TotalUserRequests = 0
)
{
    public decimal RequireComplete()
    {
        if (
            Status != "complete"
            || TotalNanoAiu is not double total
            || !double.IsFinite(total)
            || total < 0
            || TotalUserRequests < 0
            || (total == 0 && TotalUserRequests <= 0)
        )
        {
            throw new OperationException(
                "credit_unknown",
                "Native credit usage is absent, invalid or incomplete; reservation retained."
            );
        }

        try
        {
            return checked((decimal)total / 1_000_000_000m);
        }
        catch (OverflowException)
        {
            throw new OperationException("credit_unknown", "Native credit subtotal overflowed.");
        }
    }
}
