using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;

namespace Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;

public sealed record AzureAuthProcessLaunchOptions
{
    private const int DefaultOutputLimit = 8192;

    public string? SystemRoot { get; init; }

    public string? Windir { get; init; }

    public string? Temp { get; init; }

    public string? Tmp { get; init; }

    public string? LocalAppData { get; init; }

    public string? UserProfile { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(15);

    public int MaxStandardOutputBytes { get; init; } = DefaultOutputLimit;

    public int MaxStandardOutputCharacters { get; init; } = DefaultOutputLimit;

    public int MaxStandardErrorBytes { get; init; } = DefaultOutputLimit;

    public int MaxStandardErrorCharacters { get; init; } = DefaultOutputLimit;

    internal void Validate()
    {
        ValidateOptionalDirectory(SystemRoot, nameof(SystemRoot));
        ValidateOptionalDirectory(Windir, nameof(Windir));
        ValidateOptionalDirectory(Temp, nameof(Temp));
        ValidateOptionalDirectory(Tmp, nameof(Tmp));
        ValidateOptionalDirectory(LocalAppData, nameof(LocalAppData));
        ValidateOptionalDirectory(UserProfile, nameof(UserProfile));

        if (Timeout <= TimeSpan.Zero || Timeout > ProcessStartSpec.MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Timeout),
                Timeout,
                $"AzureAuth process timeout must be positive and cannot exceed {ProcessStartSpec.MaximumTimeout}."
            );
        }

        EnsurePositive(MaxStandardOutputBytes, nameof(MaxStandardOutputBytes));
        EnsurePositive(MaxStandardOutputCharacters, nameof(MaxStandardOutputCharacters));
        EnsurePositive(MaxStandardErrorBytes, nameof(MaxStandardErrorBytes));
        EnsurePositive(MaxStandardErrorCharacters, nameof(MaxStandardErrorCharacters));

    }

    internal ProcessOutputCaptureOptions ToOutputCaptureOptions() =>
        new()
        {
            StandardOutputByteLimit = MaxStandardOutputBytes,
            StandardOutputCharacterLimit = MaxStandardOutputCharacters,
            StandardErrorByteLimit = MaxStandardErrorBytes,
            StandardErrorCharacterLimit = MaxStandardErrorCharacters,
        };

    internal IReadOnlyDictionary<string, string?> CreateEnvironment(
        IReadOnlyList<string> trustedPathEntries,
        bool disableMsalCache
    )
    {
        ArgumentNullException.ThrowIfNull(trustedPathEntries);
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddIfNotNull(environment, "SystemRoot", SystemRoot);
        AddIfNotNull(environment, "WINDIR", Windir);
        AddIfNotNull(environment, "TEMP", Temp);
        AddIfNotNull(environment, "TMP", Tmp);
        AddIfNotNull(environment, "LOCALAPPDATA", LocalAppData);
        AddIfNotNull(environment, "USERPROFILE", UserProfile);

        if (trustedPathEntries.Count > 0)
        {
            environment["PATH"] = string.Join(';', trustedPathEntries);
        }

        if (disableMsalCache)
        {
            environment["OEAUTH_MSAL_DISABLE_CACHE"] = "1";
        }

        return environment;
    }

    private static void AddIfNotNull(Dictionary<string, string?> environment, string key, string? value)
    {
        if (value is not null)
        {
            environment[key] = value;
        }
    }

    private static void ValidateOptionalDirectory(string? path, string paramName)
    {
        if (path is not null)
        {
            AzureAuthWindowsDirectoryPathPolicy.Validate(path, paramName);
        }
    }

    private static void EnsurePositive(int value, string paramName)
    {
        if (value <= 0 || value > ProcessOutputCaptureOptions.MaximumStreamLimit)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"AzureAuth output limits must be positive and cannot exceed {ProcessOutputCaptureOptions.MaximumStreamLimit}."
            );
        }
    }
}

internal static class AzureAuthWindowsDirectoryPathPolicy
{
    internal static void Validate(string path, string paramName)
    {
        if (
            string.IsNullOrWhiteSpace(path)
            || !string.Equals(path, path.Trim(), StringComparison.Ordinal)
            || path.Contains(';')
        )
        {
            throw new ArgumentException(
                "Configured AzureAuth directories must use canonical absolute local Windows paths.",
                paramName
            );
        }

        try
        {
            WindowsPathPolicy.ValidateDirectoryPath(path);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "Configured AzureAuth directories must use canonical absolute local Windows paths.",
                paramName,
                exception
            );
        }
    }
}
