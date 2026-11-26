namespace Authentication.Windows;

internal sealed class WindowsLifetimePipe(Func<Task> cancel)
{
    private readonly TaskCompletionSource<bool> admitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int stopping;

    internal Task<bool> Admission => admitted.Task;
    internal Task Completion => completed.Task;

    internal void Start() => new Thread(Observe) { IsBackground = true }.Start();

    internal void Stop() => Volatile.Write(ref stopping, 1);

    private void Observe()
    {
        Task cancellation = Task.CompletedTask;
        try
        {
            using var handle = WindowsStandardHandles.Borrow(WindowsStandardHandles.Input);
            if (handle.IsInvalid || WindowsStandardHandles.GetFileType(handle) != WindowsStandardHandles.Pipe)
            {
                admitted.TrySetResult(false);
                return;
            }

            var buffer = new byte[4096];
            bool? asynchronous = null;
            while (Volatile.Read(ref stopping) == 0)
            {
                var error = WindowsStandardHandles.Peek(handle, out var available);
                if (error != 0)
                {
                    if (admitted.Task.IsCompletedSuccessfully && admitted.Task.Result
                        || error is WindowsStandardHandles.BrokenPipe or WindowsStandardHandles.NotConnected or WindowsStandardHandles.NoData)
                    {
                        cancellation = cancel();
                    }
                    // False also keeps an initially closed pipe from releasing
                    // Profile work before asynchronous cancellation propagates.
                    admitted.TrySetResult(false);
                    return;
                }

                if (available == 0)
                {
                    admitted.TrySetResult(true);
                    Thread.Sleep(10);
                    continue;
                }

                asynchronous ??= handle.IsAsync;
                error = WindowsStandardHandles.Discard(handle, asynchronous.Value, buffer,
                    Math.Min(available, (uint)buffer.Length), out var count);
                if (error is 0 or WindowsStandardHandles.MoreData or WindowsStandardHandles.NoData)
                {
                    // A zero-length peer write is not EOF. ERROR_NO_DATA can mean
                    // a temporarily empty nonblocking pipe; inspect it again.
                    if (count == 0) Thread.Sleep(10);
                    continue;
                }

                cancellation = cancel();
                admitted.TrySetResult(false);
                return;
            }
        }
        catch (Exception)
        {
            // Native/managed pipe failure contains no caller bytes or raw errors.
            cancellation = cancel();
            admitted.TrySetResult(false);
        }
        finally
        {
            admitted.TrySetResult(false);
            _ = FinishAsync(cancellation);
        }
    }

    private async Task FinishAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception) { }
        completed.TrySetResult();
    }
}
