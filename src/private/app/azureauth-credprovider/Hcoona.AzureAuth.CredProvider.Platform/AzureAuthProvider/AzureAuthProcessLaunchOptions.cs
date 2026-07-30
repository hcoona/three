using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed record AzureAuthProcessLaunchOptions
{
    private const int DefaultOutputLimit = 8192;

    public required string ExecutablePath { get; init; }

    public required string WorkingDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxStandardOutputBytes { get; init; } = DefaultOutputLimit;

    public int MaxStandardErrorBytes { get; init; } = DefaultOutputLimit;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ExecutablePath) || !Path.IsPathFullyQualified(ExecutablePath))
        {
            throw new ArgumentException(
                "AzureAuth executable path must be absolute.",
                nameof(ExecutablePath)
            );
        }

        if (
            string.IsNullOrWhiteSpace(WorkingDirectory)
            || !Path.IsPathFullyQualified(WorkingDirectory)
        )
        {
            throw new ArgumentException(
                "AzureAuth working directory must be absolute.",
                nameof(WorkingDirectory)
            );
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStandardOutputBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxStandardErrorBytes);
    }

    internal ProcessOutputCaptureOptions ToOutputCaptureOptions() =>
        new()
        {
            StandardOutputByteLimit = MaxStandardOutputBytes,
            StandardErrorByteLimit = MaxStandardErrorBytes,
        };

    public static AzureAuthProcessLaunchOptions? FromInstallation(
        AzureAuthInstallation installation
    )
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!installation.IsAvailable)
        {
            return null;
        }

        string executablePath = installation.HostExecutablePath!;
        string? workingDirectory = Path.GetDirectoryName(executablePath);
        return workingDirectory is null
            ? null
            : new AzureAuthProcessLaunchOptions
            {
                ExecutablePath = executablePath,
                WorkingDirectory = workingDirectory,
            };
    }
}
