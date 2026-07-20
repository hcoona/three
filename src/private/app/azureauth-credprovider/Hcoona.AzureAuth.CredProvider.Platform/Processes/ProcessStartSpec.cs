using System.Collections.ObjectModel;

namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class ProcessStartSpec
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromHours(1);

    public ProcessStartSpec(
        string fileName,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? standardInput = null,
        ProcessEnvironmentMode environmentMode = ProcessEnvironmentMode.Inherit,
        Func<CancellationToken, ValueTask>? preStartValidation = null,
        TimeSpan? timeout = null,
        ProcessOutputCaptureOptions? outputCaptureOptions = null,
        bool? useWindowsEnvironmentVariableSemantics = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (timeout.HasValue && (timeout.Value <= TimeSpan.Zero || timeout.Value > MaximumTimeout))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                $"Process timeout must be positive and cannot exceed {MaximumTimeout}."
            );
        }

        FileName = fileName;
        Arguments = CopyArguments(arguments);
        WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
        UseWindowsEnvironmentVariableSemantics =
            useWindowsEnvironmentVariableSemantics ?? OperatingSystem.IsWindows();
        Environment = CopyEnvironment(environment, UseWindowsEnvironmentVariableSemantics);
        StandardInput = standardInput;
        EnvironmentMode = environmentMode;
        PreStartValidation = preStartValidation;
        Timeout = timeout ?? DefaultTimeout;
        OutputCaptureOptions = outputCaptureOptions ?? ProcessOutputCaptureOptions.Default;
        OutputCaptureOptions.Validate();
    }

    public string FileName { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string? WorkingDirectory { get; }

    public IReadOnlyDictionary<string, string?> Environment { get; }

    public string? StandardInput { get; }

    public ProcessEnvironmentMode EnvironmentMode { get; }

    public TimeSpan? Timeout { get; }

    public ProcessOutputCaptureOptions OutputCaptureOptions { get; }

    public bool UseWindowsEnvironmentVariableSemantics { get; }

    // Runs immediately before Process.Start for last-moment path revalidation.
    // ProcessStartInfo is path-based, so callers must still treat the remaining path TOCTOU
    // window as residual risk.
    public Func<CancellationToken, ValueTask>? PreStartValidation { get; }

    internal void ValidateForRun()
    {
        if (Timeout is null || Timeout <= TimeSpan.Zero || Timeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Timeout),
                Timeout,
                $"Process timeout is required and cannot exceed {MaximumTimeout}."
            );
        }

        OutputCaptureOptions.Validate();
        if (EnvironmentMode is not ProcessEnvironmentMode.Inherit and not ProcessEnvironmentMode.ExplicitOnly)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EnvironmentMode),
                EnvironmentMode,
                "Unsupported process environment mode."
            );
        }
    }

    private static ReadOnlyCollection<string> CopyArguments(IEnumerable<string>? arguments)
    {
        if (arguments is null)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        var copiedArguments = arguments.ToArray();
        if (Array.Exists(copiedArguments, static argument => argument is null))
        {
            throw new ArgumentException(
                "Process arguments must not contain null values.",
                nameof(arguments)
            );
        }

        return Array.AsReadOnly(copiedArguments);
    }

    private static ReadOnlyDictionary<string, string?> CopyEnvironment(
        IReadOnlyDictionary<string, string?>? environment,
        bool useWindowsEnvironmentVariableSemantics
    )
    {
        if (environment is null || environment.Count == 0)
        {
            return ReadOnlyDictionary<string, string?>.Empty;
        }

        var copiedEnvironment = new Dictionary<string, string?>(
            environment.Count,
            useWindowsEnvironmentVariableSemantics
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal
        );
        foreach (var variable in environment)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(variable.Key, nameof(environment));
            copiedEnvironment.Add(variable.Key, variable.Value);
        }

        return new ReadOnlyDictionary<string, string?>(copiedEnvironment);
    }
}
