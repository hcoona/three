using GitHub.Copilot;

namespace LiteAgensh;

internal sealed class NativeUsageFence
{
    private readonly object sync = new();
    private TaskCompletionSource changed = NewSignal();
    private bool pending;
    private bool turnPending;
    private string? attempt;
    private string? failure;
    private long revision;
    private NativeUsage? captured;
    private long capturedRevision;

    public string? Failure
    {
        get
        {
            lock (sync)
            {
                return failure;
            }
        }
    }

    public void Observe(SessionEvent update)
    {
        lock (sync)
        {
            switch (update)
            {
                case SessionCompactionStartEvent start:
                    if (pending)
                    {
                        failure = "Native compaction started before its prior attempt settled.";
                    }
                    pending = true;
                    attempt = start.Data.CompactionAttemptId;
                    break;
                case SessionCompactionCompleteEvent complete:
                    if (!pending || complete.Data.CompactionAttemptId != attempt
                        || !complete.Data.Success)
                    {
                        failure = "Native compaction settlement failed or contradicted its start.";
                    }
                    pending = false;
                    break;
                case AssistantUsageEvent:
                case UserMessageEvent:
                case AssistantTurnStartEvent:
                case AssistantTurnEndEvent:
                case ToolExecutionStartEvent:
                case ToolExecutionCompleteEvent:
                case SessionLimitsExhaustedRequestedEvent:
                    break;
                default:
                    return;
            }
            revision++;
            changed.TrySetResult();
            changed = NewSignal();
        }
    }

    public void SetTurnPending(bool value)
    {
        lock (sync)
        {
            turnPending = value;
            revision++;
            changed.TrySetResult();
            changed = NewSignal();
        }
    }

    public async Task<NativeUsage> CaptureAsync(
        Func<CancellationToken, Task<NativeUsage>> read,
        CancellationToken cancellationToken
    )
    {
        long before;
        while (true)
        {
            Task wait;
            lock (sync)
            {
                RequireHealthy();
                if (!pending)
                {
                    before = revision;
                    break;
                }
                wait = changed.Task;
            }
            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        NativeUsage usage = await read(cancellationToken).ConfigureAwait(false);
        lock (sync)
        {
            RequireHealthy();
            if (pending || revision != before)
            {
                throw new OperationException(
                    "credit_unknown", "Native work changed during the final usage query."
                );
            }
            captured = usage;
            capturedRevision = revision;
        }
        return usage;
    }

    public void Use(NativeUsage usage, Action record)
    {
        lock (sync)
        {
            RequireHealthy();
            if (pending || !ReferenceEquals(captured, usage) || capturedRevision != revision)
            {
                throw new OperationException(
                    "credit_unknown", "Native work invalidated the staged usage reading."
                );
            }
            record();
        }
    }

    private void RequireHealthy()
    {
        if (turnPending)
        {
            throw new OperationException(
                "credit_unknown",
                "Native send has no delivered terminal boundary; usage remains unresolved."
            );
        }
        if (failure is not null)
        {
            throw new OperationException("credit_unknown", failure);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
