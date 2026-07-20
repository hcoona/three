namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed record ProcessOutputCaptureOptions
{
    public const int DefaultStreamLimit = 1024 * 1024;

    public const int MaximumStreamLimit = 16 * 1024 * 1024;

    public int? StandardOutputByteLimit { get; init; }

    public int? StandardOutputCharacterLimit { get; init; }

    public int? StandardErrorByteLimit { get; init; }

    public int? StandardErrorCharacterLimit { get; init; }

    public static ProcessOutputCaptureOptions Default { get; } =
        new()
        {
            StandardOutputByteLimit = DefaultStreamLimit,
            StandardOutputCharacterLimit = DefaultStreamLimit,
            StandardErrorByteLimit = DefaultStreamLimit,
            StandardErrorCharacterLimit = DefaultStreamLimit,
        };

    // Retained for source compatibility. Process output is always hard-bounded.
    public static ProcessOutputCaptureOptions Unbounded => Default;

    internal void Validate()
    {
        EnsurePositive(StandardOutputByteLimit, nameof(StandardOutputByteLimit));
        EnsurePositive(StandardOutputCharacterLimit, nameof(StandardOutputCharacterLimit));
        EnsurePositive(StandardErrorByteLimit, nameof(StandardErrorByteLimit));
        EnsurePositive(StandardErrorCharacterLimit, nameof(StandardErrorCharacterLimit));
        EnsureWithinMaximum(StandardOutputByteLimit, nameof(StandardOutputByteLimit));
        EnsureWithinMaximum(StandardOutputCharacterLimit, nameof(StandardOutputCharacterLimit));
        EnsureWithinMaximum(StandardErrorByteLimit, nameof(StandardErrorByteLimit));
        EnsureWithinMaximum(StandardErrorCharacterLimit, nameof(StandardErrorCharacterLimit));
    }

    private static void EnsurePositive(int? value, string paramName)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                "Capture limits must be positive when specified."
            );
        }
    }

    private static void EnsureWithinMaximum(int? value, string paramName)
    {
        if (value is null || value > MaximumStreamLimit)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Capture limits are required and cannot exceed {MaximumStreamLimit}."
            );
        }
    }
}
