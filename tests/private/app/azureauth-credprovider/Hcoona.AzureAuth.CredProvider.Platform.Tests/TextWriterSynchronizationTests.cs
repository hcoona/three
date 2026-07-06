using System.Text;
using System.Threading;
using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class TextWriterSynchronizationTests
{
    [Fact]
    public void WriterLockScopeAcquiresAllMonitorsAndDisposeReleasesThem()
    {
        object firstWrapperMonitor = new();
        object secondWrapperMonitor = new();
        object sharedSyncRoot = new();

        Assert.False(Monitor.IsEntered(firstWrapperMonitor));
        Assert.False(Monitor.IsEntered(secondWrapperMonitor));
        Assert.False(Monitor.IsEntered(sharedSyncRoot));

        using (new WriterLockScope(
            [firstWrapperMonitor, secondWrapperMonitor],
            sharedSyncRoot))
        {
            Assert.True(Monitor.IsEntered(firstWrapperMonitor));
            Assert.True(Monitor.IsEntered(secondWrapperMonitor));
            Assert.True(Monitor.IsEntered(sharedSyncRoot));
        }

        Assert.False(Monitor.IsEntered(firstWrapperMonitor));
        Assert.False(Monitor.IsEntered(secondWrapperMonitor));
        Assert.False(Monitor.IsEntered(sharedSyncRoot));
    }

    [Fact]
    public void WriterLockScopeReleasesAcquiredWrapperMonitorsWhenLaterWrapperAcquireThrows()
    {
        object firstWrapperMonitor = new();
        object sharedSyncRoot = new();

        Assert.Throws<ArgumentNullException>(
            () => _ = new WriterLockScope([firstWrapperMonitor, null!], sharedSyncRoot));

        Assert.False(Monitor.IsEntered(firstWrapperMonitor));
        Assert.False(Monitor.IsEntered(sharedSyncRoot));
    }

    [Fact]
    public void WriterLockScopeReleasesAcquiredWrapperMonitorsWhenSharedLockAcquireThrows()
    {
        object firstWrapperMonitor = new();
        object secondWrapperMonitor = new();

        Assert.Throws<ArgumentNullException>(
            () => _ = new WriterLockScope([firstWrapperMonitor, secondWrapperMonitor], null!));

        Assert.False(Monitor.IsEntered(firstWrapperMonitor));
        Assert.False(Monitor.IsEntered(secondWrapperMonitor));
    }

    [Fact]
    public void GetWriterSyncRootCanonicalizesExactBufferedStreamTargets()
    {
        using var stream = new MemoryStream();
        using var firstBufferedStream = new BufferedStream(stream, bufferSize: 1024);
        using var secondBufferedStream = new BufferedStream(stream, bufferSize: 1024);
        using var firstWriter = new StreamWriter(
            firstBufferedStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true);
        using var secondWriter = new StreamWriter(
            secondBufferedStream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true);

        Assert.Same(
            TextWriterSynchronization.GetWriterSyncRoot(firstWriter),
            TextWriterSynchronization.GetWriterSyncRoot(secondWriter));
    }
}
