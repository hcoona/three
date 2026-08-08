using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public enum AzureAuthHealthProbeStatus
{
    NotRequired = 1,
    Passed = 2,
    Failed = 3,
}

public sealed record AzureAuthHealthProbeResult
{
    public required AzureAuthHealthProbeStatus Status { get; init; }

    public required string Code { get; init; }

    public required string SafeMessage { get; init; }

    public bool Succeeded => Status != AzureAuthHealthProbeStatus.Failed;
}

public static class AzureAuthHealthProbe
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(10);

    public static async ValueTask<AzureAuthHealthProbeResult> RunAsync(
        AzureAuthProviderConfig config,
        AzureAuthProcessLaunchOptions? launchOptions,
        IProcessRunner processRunner,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(processRunner);
        cancellationToken.ThrowIfCancellationRequested();

        if (config.Selection != AzureAuthProviderSelection.AzureAuth)
        {
            return new AzureAuthHealthProbeResult
            {
                Status = AzureAuthHealthProbeStatus.NotRequired,
                Code = "AzureAuthVersionProbeNotRequired",
                SafeMessage = "AzureAuth is not the selected provider.",
            };
        }

        if (launchOptions is null)
        {
            return Failure(
                "AzureAuthVersionProbeUnavailable",
                "AzureAuth launch options are unavailable."
            );
        }

        ProcessResult result;
        try
        {
            result = await processRunner
                .RunAsync(
                    new ProcessStartSpec(
                        launchOptions.ExecutablePath,
                        ["--version"],
                        workingDirectory: launchOptions.WorkingDirectory,
                        timeout: HealthTimeout,
                        outputCaptureOptions: launchOptions.ToOutputCaptureOptions()
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(
                "AzureAuthVersionProbeLaunchFailed",
                "AzureAuth version health probe could not be launched."
            );
        }

        return result.Status switch
        {
            ProcessExecutionStatus.Success => new AzureAuthHealthProbeResult
            {
                Status = AzureAuthHealthProbeStatus.Passed,
                Code = "AzureAuthVersionProbeSucceeded",
                SafeMessage = "AzureAuth version health probe succeeded.",
            },
            ProcessExecutionStatus.NonZeroExit => Failure(
                "AzureAuthVersionProbeExitNonZero",
                "AzureAuth version health probe exited with a nonzero status."
            ),
            ProcessExecutionStatus.TimedOut => Failure(
                "AzureAuthVersionProbeTimedOut",
                "AzureAuth version health probe timed out."
            ),
            ProcessExecutionStatus.LaunchFailure => Failure(
                "AzureAuthVersionProbeLaunchFailed",
                "AzureAuth version health probe could not be launched."
            ),
            ProcessExecutionStatus.OutputTooLarge => Failure(
                "AzureAuthVersionProbeOutputTooLarge",
                "AzureAuth version health probe output exceeded the configured limit."
            ),
            ProcessExecutionStatus.InvalidOutput => Failure(
                "AzureAuthVersionProbeOutputInvalid",
                "AzureAuth version health probe output was invalid."
            ),
            _ => Failure(
                "AzureAuthVersionProbeFailed",
                "AzureAuth version health probe failed."
            ),
        };
    }

    private static AzureAuthHealthProbeResult Failure(string code, string safeMessage) =>
        new()
        {
            Status = AzureAuthHealthProbeStatus.Failed,
            Code = code,
            SafeMessage = safeMessage,
        };
}
