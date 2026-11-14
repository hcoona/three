namespace LiteAgensh;

internal sealed class WorkAdmission : IDisposable
{
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource deadlineStop;
    private readonly CancellationTokenSource stop;

    public WorkAdmission(
        TimeProvider clock, DateTimeOffset deadline, CancellationToken cancellationToken
    )
    {
        this.clock = clock;
        Deadline = deadline;
        TimeSpan remaining = deadline - clock.GetUtcNow();
        deadlineStop = new(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, clock);
        stop = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, deadlineStop.Token
        );
    }

    public DateTimeOffset Deadline { get; }

    public bool IsClosed
    {
        get
        {
            if (!stop.IsCancellationRequested && clock.GetUtcNow() >= Deadline)
            {
                stop.Cancel();
            }
            return stop.IsCancellationRequested;
        }
    }

    public CancellationToken Token
    {
        get
        {
            _ = IsClosed;
            return stop.Token;
        }
    }

    public void ThrowIfClosed() => Token.ThrowIfCancellationRequested();

    public Task CloseAsync() => stop.CancelAsync();

    public void Dispose()
    {
        stop.Dispose();
        deadlineStop.Dispose();
    }
}
