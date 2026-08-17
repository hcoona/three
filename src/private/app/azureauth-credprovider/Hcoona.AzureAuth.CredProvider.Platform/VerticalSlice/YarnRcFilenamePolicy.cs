using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.VerticalSlice;

public sealed class YarnRcFilenameConfigurationException : InvalidOperationException
{
    public const string ErrorCode = "invalid-yarn-rc-filename";
    public const string EnvironmentVariableName = "YARN_RC_FILENAME";
    public const string SafeMessage =
        "YARN_RC_FILENAME must be an absolute path or a relative path without traversal.";

    public YarnRcFilenameConfigurationException()
        : base(SafeMessage) { }

    public string Code { get; } = ErrorCode;

    public string SettingName { get; } = EnvironmentVariableName;
}

internal static class YarnRcFilenamePolicy
{
    public static string? ReadValidatedOverride(
        IFileSystem fileSystem,
        Func<string, string?> environmentVariableReader
    )
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(environmentVariableReader);

        string? configuredFilename = NullIfWhiteSpace(
            environmentVariableReader(YarnRcFilenameConfigurationException.EnvironmentVariableName)
        );
        if (
            configuredFilename is not null
            && !fileSystem.IsPathFullyQualified(configuredFilename)
            && (
                IsAmbiguousWindowsPath(fileSystem, configuredFilename)
                || !IsValidRelativeConfigurationPath(configuredFilename)
            )
        )
        {
            throw new YarnRcFilenameConfigurationException();
        }

        return configuredFilename;
    }

    private static bool IsValidRelativeConfigurationPath(string value)
    {
        string[] segments = value
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(segment => segment is not "." and not "..");
    }

    private static bool IsAmbiguousWindowsPath(IFileSystem fileSystem, string value) =>
        FileSystemPathSemantics.UsesWindowsPaths(fileSystem)
        && (
            value[0] is '/' or '\\'
            || (
                value.Length >= 2
                && char.IsAsciiLetter(value[0])
                && value[1] == ':'
            )
        );

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
