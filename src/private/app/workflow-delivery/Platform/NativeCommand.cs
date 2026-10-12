namespace WorkflowDelivery.Platform;

internal sealed record NativeCommand(
    string Executable,
    string Directory,
    string[] Arguments,
    int DeadlineSeconds,
    IReadOnlyDictionary<string, string?>? Environment = null,
    bool InheritEnvironment = true,
    bool CloseStandardInput = false
);

internal enum NativeTermination
{
    Exited,
    StartFailed,
    Cancelled,
    TimedOut,
    InvalidOutput,
    CleanupFailed,
    InputFailed,
}

internal sealed record NativeCommandResult(
    NativeTermination Termination,
    int? ExitCode,
    string Stdout,
    string Stderr,
    double DurationSeconds,
    string? Error
)
{
    internal bool Succeeded => Termination == NativeTermination.Exited && ExitCode == 0;
}
