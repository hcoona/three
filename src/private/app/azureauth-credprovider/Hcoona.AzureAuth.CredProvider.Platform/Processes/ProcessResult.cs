namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class ProcessResult
{
    public ProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public int ExitCode { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public bool Succeeded => ExitCode == 0;
}
