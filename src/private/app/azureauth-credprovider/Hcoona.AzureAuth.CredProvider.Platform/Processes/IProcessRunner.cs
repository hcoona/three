namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessStartSpec startSpec,
        CancellationToken cancellationToken = default
    );
}
