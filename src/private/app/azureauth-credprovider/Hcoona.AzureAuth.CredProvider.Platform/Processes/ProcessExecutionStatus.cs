namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public enum ProcessExecutionStatus
{
    Unspecified = 0,
    Success = 1,
    NonZeroExit = 2,
    TimedOut = 3,
    Canceled = 4,
    OutputTooLarge = 5,
    InvalidOutput = 6,
    LaunchFailure = 7,
}
