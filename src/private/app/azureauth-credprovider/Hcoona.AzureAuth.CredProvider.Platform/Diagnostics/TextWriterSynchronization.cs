using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

internal static class TextWriterSynchronization
{
    private const string SyncTextWriterTypeName = "System.IO.TextWriter+SyncTextWriter";
    private const string BufferedStreamInnerStreamFieldName = "_stream";
    private static readonly ConditionalWeakTable<TextWriter, object> WriterSyncRoots = new();
    private static readonly ConditionalWeakTable<StringBuilder, object> StringBuilderSyncRoots =
        new();
    private static readonly ConditionalWeakTable<Stream, object> StreamSyncRoots = new();
    private static readonly FieldInfo? BufferedStreamInnerStreamField = typeof(BufferedStream)
        .GetField(
            BufferedStreamInnerStreamFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Type? SyncTextWriterType = typeof(TextWriter)
        .Assembly
        .GetType(SyncTextWriterTypeName, throwOnError: false);
    private static readonly FieldInfo? SyncTextWriterInnerWriterField =
        SyncTextWriterType?.GetField(
            "_out",
            BindingFlags.Instance | BindingFlags.NonPublic);

    internal static WriterLockScope AcquireWriteLock(TextWriter writer, object sharedSyncRoot)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(sharedSyncRoot);

        return new WriterLockScope(
            GetSupportedSyncWrapperMonitorsOrEmpty(writer),
            sharedSyncRoot);
    }

    public static object GetWriterSyncRoot(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer = GetSupportedWrappedWriterOrSelf(writer);
        if (writer is ITextWriterSyncRootProvider syncRootProvider)
        {
            return syncRootProvider.SyncRoot;
        }

        if (writer is StringWriter stringWriter)
        {
            return StringBuilderSyncRoots.GetValue(
                stringWriter.GetStringBuilder(),
                static _ => new object());
        }

        if (writer is StreamWriter streamWriter)
        {
            return GetStreamSyncRoot(streamWriter.BaseStream);
        }

        return WriterSyncRoots.GetValue(writer, static _ => new object());
    }

    public static object GetStreamSyncRoot(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream = GetSupportedWrappedStreamOrSelf(stream);
        return StreamSyncRoots.GetValue(stream, static _ => new object());
    }

    public static void FlushUnderSharedLockIfNeeded(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer = GetSupportedWrappedWriterOrSelf(writer);
        if (writer is StreamWriter or IFlushRequiredTextWriter)
        {
            // Exact StreamWriter instances can retain encoder state even when AutoFlush is
            // enabled, and opt-in writers can still leave bytes in intermediate buffers such
            // as BufferedStream, so the shared lock must cover a real Flush before returning.
            writer.Flush();
        }
    }

    internal static TextWriter GetSupportedWrappedWriterOrSelf(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        return TryGetSupportedWrappedWriter(writer, out _)
            ?? writer;
    }

    internal static Stream GetSupportedWrappedStreamOrSelf(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Stream currentStream = stream;
        while (currentStream.GetType() == typeof(BufferedStream))
        {
            FieldInfo innerStreamField = BufferedStreamInnerStreamField
                ?? throw CreateUnsupportedBufferedStreamException();
            currentStream = innerStreamField.GetValue(currentStream) as Stream
                ?? throw CreateUnsupportedBufferedStreamException();
        }

        return currentStream;
    }

    private static object[] GetSupportedSyncWrapperMonitorsOrEmpty(TextWriter writer)
    {
        _ = TryGetSupportedWrappedWriter(writer, out object[] wrapperMonitors);
        return wrapperMonitors;
    }

    private static TextWriter? TryGetSupportedWrappedWriter(
        TextWriter writer,
        out object[] wrapperMonitors)
    {
        TextWriter currentWriter = writer;
        List<object>? collectedWrapperMonitors = null;
        while (
            SyncTextWriterType is not null
            && currentWriter.GetType() == SyncTextWriterType
        )
        {
            collectedWrapperMonitors ??= [];
            collectedWrapperMonitors.Add(currentWriter);
            FieldInfo innerWriterField = SyncTextWriterInnerWriterField
                ?? throw CreateUnsupportedSyncTextWriterException();
            currentWriter = innerWriterField.GetValue(currentWriter) as TextWriter
                ?? throw CreateUnsupportedSyncTextWriterException();
        }

        if (collectedWrapperMonitors is not null && currentWriter is StringWriter or StreamWriter)
        {
            wrapperMonitors = [.. collectedWrapperMonitors];
            return currentWriter;
        }

        wrapperMonitors = [];
        return null;
    }

    private static NotSupportedException CreateUnsupportedSyncTextWriterException()
    {
        return new NotSupportedException(
            $"Built-in synchronized {nameof(TextWriter)} wrappers are unsupported "
                + "because the wrapped writer cannot be inspected safely.");
    }

    private static NotSupportedException CreateUnsupportedBufferedStreamException()
    {
        return new NotSupportedException(
            $"Built-in {nameof(BufferedStream)} wrappers are unsupported because the "
                + "wrapped stream cannot be inspected safely.");
    }
}

// Writers with non-public shared targets can opt in to shared-target serialization
// without relying on brittle reflection-based target discovery.
internal interface ITextWriterSyncRootProvider
{
    object SyncRoot { get; }
}

// Writers that can report committed output before their final downstream target observes the
// bytes can opt in to a shared-lock flush before commit-sensitive paths return.
internal interface IFlushRequiredTextWriter
{
}

internal readonly struct WriterLockScope : IDisposable
{
    private readonly object _sharedSyncRoot;
    private readonly object[] _wrapperMonitors;

    public WriterLockScope(object[] wrapperMonitors, object sharedSyncRoot)
    {
        _wrapperMonitors = wrapperMonitors;
        _sharedSyncRoot = sharedSyncRoot;

        // Mirror the built-in synchronized wrapper nesting order before taking the shared-target
        // lock so wrapper-based callers and shared-target callers cannot deadlock each other.
        var acquiredWrapperCount = 0;
        var sharedSyncRootAcquired = false;
        try
        {
            foreach (object wrapperMonitor in _wrapperMonitors)
            {
                Monitor.Enter(wrapperMonitor);
                acquiredWrapperCount++;
            }

            Monitor.Enter(_sharedSyncRoot);
            sharedSyncRootAcquired = true;
        }
        catch
        {
            if (sharedSyncRootAcquired)
            {
                Monitor.Exit(_sharedSyncRoot);
            }

            for (int index = acquiredWrapperCount - 1; index >= 0; index--)
            {
                Monitor.Exit(_wrapperMonitors[index]);
            }

            throw;
        }
    }

    public void Dispose()
    {
        Monitor.Exit(_sharedSyncRoot);
        for (int index = _wrapperMonitors.Length - 1; index >= 0; index--)
        {
            Monitor.Exit(_wrapperMonitors[index]);
        }
    }
}
