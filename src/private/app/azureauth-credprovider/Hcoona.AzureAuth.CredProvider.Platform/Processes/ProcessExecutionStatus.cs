namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public enum ProcessExecutionStatus
{
    Unspecified = 0,
    Success = 1,
    NonZeroExit = 2,
    TimedOut = 3,
    OutputTooLarge = 4,
    InvalidOutput = 5,
    LaunchFailure = 6,
}
