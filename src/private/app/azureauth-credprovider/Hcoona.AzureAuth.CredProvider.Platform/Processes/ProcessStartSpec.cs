using System.Collections.ObjectModel;

namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class ProcessStartSpec
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    public ProcessStartSpec(
        string fileName,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? standardInput = null,
        TimeSpan? timeout = null,
        ProcessOutputCaptureOptions? outputCaptureOptions = null,
        TextWriter? standardErrorTee = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        FileName = fileName;
        Arguments = CopyArguments(arguments);
        WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
        Environment = CopyEnvironment(environment);
        StandardInput = standardInput;
        Timeout = timeout ?? DefaultTimeout;
        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                Timeout,
                "Process timeout must be positive."
            );
        }
        OutputCaptureOptions = outputCaptureOptions ?? ProcessOutputCaptureOptions.Default;
        OutputCaptureOptions.Validate();
        StandardErrorTee = standardErrorTee;
    }

    public string FileName { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string? WorkingDirectory { get; }

    public IReadOnlyDictionary<string, string?> Environment { get; }

    public string? StandardInput { get; }

    public TimeSpan Timeout { get; }

    public ProcessOutputCaptureOptions OutputCaptureOptions { get; }

    public TextWriter? StandardErrorTee { get; }

    private static ReadOnlyCollection<string> CopyArguments(IEnumerable<string>? arguments)
    {
        if (arguments is null)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        string[] copiedArguments = arguments.ToArray();
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
        IReadOnlyDictionary<string, string?>? environment
    )
    {
        if (environment is null || environment.Count == 0)
        {
            return ReadOnlyDictionary<string, string?>.Empty;
        }

        var copiedEnvironment = new Dictionary<string, string?>(environment.Count);
        foreach ((string key, string? value) in environment)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(environment));
            copiedEnvironment.Add(key, value);
        }

        return new ReadOnlyDictionary<string, string?>(copiedEnvironment);
    }
}
