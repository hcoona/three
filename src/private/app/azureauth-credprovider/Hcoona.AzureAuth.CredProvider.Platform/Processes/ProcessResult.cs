namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class ProcessResult
{
    public ProcessResult(int exitCode, string standardOutput, string standardError)
        : this(
            exitCode == 0 ? ProcessExecutionStatus.Success : ProcessExecutionStatus.NonZeroExit,
            exitCode,
            hasExitCode: true,
            standardOutput,
            standardError
        )
    { }

    private ProcessResult(
        ProcessExecutionStatus status,
        int exitCode,
        bool hasExitCode,
        string standardOutput,
        string standardError
    )
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        Status = status;
        ExitCode = exitCode;
        HasExitCode = hasExitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public ProcessExecutionStatus Status { get; }

    public int ExitCode { get; }

    public bool HasExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public bool Succeeded => Status == ProcessExecutionStatus.Success;

    public static ProcessResult TimedOut(
        string standardOutput,
        string standardError,
        int? exitCode = null
    ) =>
        Create(ProcessExecutionStatus.TimedOut, standardOutput, standardError, exitCode);

    public static ProcessResult Canceled(
        string standardOutput,
        string standardError,
        int? exitCode = null
    ) =>
        Create(ProcessExecutionStatus.Canceled, standardOutput, standardError, exitCode);

    public static ProcessResult OutputTooLarge(
        string standardOutput,
        string standardError,
        int? exitCode = null
    ) =>
        Create(ProcessExecutionStatus.OutputTooLarge, standardOutput, standardError, exitCode);

    public static ProcessResult InvalidOutput(
        string standardOutput,
        string standardError,
        int? exitCode = null
    ) =>
        Create(ProcessExecutionStatus.InvalidOutput, standardOutput, standardError, exitCode);

    public static ProcessResult LaunchFailure(string standardOutput = "", string standardError = "") =>
        Create(ProcessExecutionStatus.LaunchFailure, standardOutput, standardError, exitCode: null);

    private static ProcessResult Create(
        ProcessExecutionStatus status,
        string standardOutput,
        string standardError,
        int? exitCode
    )
    {
        if (
            status
                is ProcessExecutionStatus.Success
                    or ProcessExecutionStatus.NonZeroExit
                    or ProcessExecutionStatus.Unspecified
        )
        {
            throw new ArgumentException(
                "Factory status must be a non-exit failure status.",
                nameof(status)
            );
        }

        return new ProcessResult(
            status,
            exitCode ?? -1,
            exitCode.HasValue,
            standardOutput,
            standardError
        );
    }
}
