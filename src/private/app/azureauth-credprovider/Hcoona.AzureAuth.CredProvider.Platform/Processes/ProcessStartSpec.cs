using System.Collections.ObjectModel;

namespace Hcoona.AzureAuth.CredProvider.Platform.Processes;

public sealed class ProcessStartSpec
{
    public ProcessStartSpec(
        string fileName,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? standardInput = null,
        ProcessEnvironmentMode environmentMode = ProcessEnvironmentMode.Inherit,
        Func<CancellationToken, ValueTask>? preStartValidation = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        FileName = fileName;
        Arguments = CopyArguments(arguments);
        WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory;
        Environment = CopyEnvironment(environment);
        StandardInput = standardInput;
        EnvironmentMode = environmentMode;
        PreStartValidation = preStartValidation;
    }

    public string FileName { get; }

    public IReadOnlyList<string> Arguments { get; }

    public string? WorkingDirectory { get; }

    public IReadOnlyDictionary<string, string?> Environment { get; }

    public string? StandardInput { get; }

    public ProcessEnvironmentMode EnvironmentMode { get; }

    // Runs immediately before Process.Start for last-moment path revalidation.
    // ProcessStartInfo is path-based, so callers must still treat the remaining path TOCTOU
    // window as residual risk.
    public Func<CancellationToken, ValueTask>? PreStartValidation { get; }

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
        IReadOnlyDictionary<string, string?>? environment
    )
    {
        if (environment is null || environment.Count == 0)
        {
            return ReadOnlyDictionary<string, string?>.Empty;
        }

        var copiedEnvironment = new Dictionary<string, string?>(
            environment.Count,
            StringComparer.Ordinal
        );
        foreach (var variable in environment)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(variable.Key, nameof(environment));
            copiedEnvironment.Add(variable.Key, variable.Value);
        }

        return new ReadOnlyDictionary<string, string?>(copiedEnvironment);
    }
}
