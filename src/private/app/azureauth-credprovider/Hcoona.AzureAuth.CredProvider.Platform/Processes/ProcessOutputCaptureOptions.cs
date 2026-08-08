namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed record ProcessOutputCaptureOptions
{
    public const int DefaultStreamLimit = 1024 * 1024;

    public int StandardOutputByteLimit { get; init; } = DefaultStreamLimit;

    public int StandardErrorByteLimit { get; init; } = DefaultStreamLimit;

    public static ProcessOutputCaptureOptions Default { get; } = new();

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(StandardOutputByteLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(StandardErrorByteLimit);
    }
}
