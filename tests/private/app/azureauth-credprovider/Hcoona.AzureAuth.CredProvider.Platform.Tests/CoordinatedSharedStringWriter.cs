using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

internal sealed class CoordinatedSharedStringWriter : StringWriter
{
    private readonly CoordinatedSharedStringWriterCoordinator _coordinator;
    private readonly bool _coordinateAfterFlush;

    public CoordinatedSharedStringWriter(
        StringBuilder builder,
        CoordinatedSharedStringWriterCoordinator coordinator,
        bool coordinateAfterFlush = false)
        : base(builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _coordinateAfterFlush = coordinateAfterFlush;
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        CoordinatedSharedStringWriterCoordinator.PendingWrite pendingWrite =
            _coordinator.RegisterPendingWrite();
        try
        {
            pendingWrite.WaitForRelease();
            base.Write(value);
        }
        finally
        {
            _coordinator.CompletePendingWrite(pendingWrite);
        }
    }

    public override void Flush()
    {
        base.Flush();
        if (!_coordinateAfterFlush)
        {
            return;
        }

        CoordinatedSharedStringWriterCoordinator.PendingWrite pendingWrite =
            _coordinator.RegisterPendingWrite();
        try
        {
            pendingWrite.WaitForRelease();
        }
        finally
        {
            _coordinator.CompletePendingWrite(pendingWrite);
        }
    }
}

internal sealed class CoordinatedSharedStringWriterCoordinator
{
    private readonly List<PendingWrite> _pendingWrites = [];
    private readonly object _syncRoot = new();

    public bool WaitForPendingWrite(TimeSpan timeout)
    {
        return WaitUntil(HasUnreleasedPendingWrite, timeout);
    }

    public bool WaitForDistinctPendingThreadCount(
        int minimumDistinctPendingThreadCount,
        TimeSpan timeout)
    {
        return WaitUntil(
            () => GetDistinctPendingThreadCount() >= minimumDistinctPendingThreadCount,
            timeout);
    }

    public bool TryReleaseNextPendingWrite(
        int? preferredDifferentThreadId,
        out int releasedThreadId)
    {
        lock (_syncRoot)
        {
            PendingWrite? pendingWrite =
                FindPendingWrite(preferredDifferentThreadId, requireDifferentThreadId: true)
                ?? FindPendingWrite(
                    preferredDifferentThreadId,
                    requireDifferentThreadId: false);
            if (pendingWrite is null)
            {
                releasedThreadId = 0;
                return false;
            }

            pendingWrite.IsReleased = true;
            releasedThreadId = pendingWrite.ThreadId;
            pendingWrite.ReleaseWrite();
            return true;
        }
    }

    internal PendingWrite RegisterPendingWrite()
    {
        var pendingWrite = new PendingWrite(Environment.CurrentManagedThreadId);
        lock (_syncRoot)
        {
            _pendingWrites.Add(pendingWrite);
            Monitor.PulseAll(_syncRoot);
        }

        return pendingWrite;
    }

    internal void CompletePendingWrite(PendingWrite pendingWrite)
    {
        lock (_syncRoot)
        {
            _pendingWrites.Remove(pendingWrite);
            Monitor.PulseAll(_syncRoot);
        }

        pendingWrite.Dispose();
    }

    private PendingWrite? FindPendingWrite(
        int? preferredDifferentThreadId,
        bool requireDifferentThreadId)
    {
        foreach (PendingWrite pendingWrite in _pendingWrites)
        {
            if (pendingWrite.IsReleased)
            {
                continue;
            }

            if (requireDifferentThreadId
                && preferredDifferentThreadId.HasValue
                && pendingWrite.ThreadId == preferredDifferentThreadId.Value)
            {
                continue;
            }

            return pendingWrite;
        }

        return null;
    }

    private int GetDistinctPendingThreadCount()
    {
        int? firstThreadId = null;
        var distinctThreadCount = 0;
        foreach (PendingWrite pendingWrite in _pendingWrites)
        {
            if (pendingWrite.IsReleased)
            {
                continue;
            }

            if (firstThreadId is null)
            {
                firstThreadId = pendingWrite.ThreadId;
                distinctThreadCount = 1;
                continue;
            }

            if (pendingWrite.ThreadId != firstThreadId.Value)
            {
                return 2;
            }
        }

        return distinctThreadCount;
    }

    private bool HasUnreleasedPendingWrite()
    {
        foreach (PendingWrite pendingWrite in _pendingWrites)
        {
            if (!pendingWrite.IsReleased)
            {
                return true;
            }
        }

        return false;
    }

    private bool WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        lock (_syncRoot)
        {
            while (!predicate())
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    return false;
                }

                Monitor.Wait(_syncRoot, remaining);
            }

            return true;
        }
    }

    internal sealed class PendingWrite : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);

        public PendingWrite(int threadId)
        {
            ThreadId = threadId;
        }

        public bool IsReleased { get; set; }

        public int ThreadId { get; }

        public void ReleaseWrite()
        {
            _release.Set();
        }

        public void WaitForRelease()
        {
            if (!_release.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException(
                    "Timed out waiting to release a coordinated shared StringWriter chunk.");
            }
        }

        public void Dispose()
        {
            _release.Dispose();
        }
    }
}
