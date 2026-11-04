namespace LiteAgensh;

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
