namespace Hcoona.DocumentTranslatorCli;

internal static class TranslationOptionResolver
{
    public const string EndpointEnvironmentVariable = "AZURE_TRANSLATOR_ENDPOINT";
    public const string AuthModeEnvironmentVariable = "AZURE_TRANSLATOR_AUTH_MODE";
    public const string ApiKeyEnvironmentVariable = "AZURE_TRANSLATOR_KEY";

    public static RawTranslationOptions Resolve(
        RawCommandLineOptions commandLineOptions,
        Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(commandLineOptions);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        return new RawTranslationOptions(
            commandLineOptions.InputPath,
            commandLineOptions.OutputPath,
            NormalizeCommandLineNonSecretScalar(
                commandLineOptions.TargetLanguage,
                commandLineOptions.TargetLanguageSpecified),
            ResolveAuthMode(commandLineOptions, getEnvironmentVariable),
            ResolveEndpoint(commandLineOptions, getEnvironmentVariable),
            FirstConfigured(
                commandLineOptions.ApiKey,
                getEnvironmentVariable(ApiKeyEnvironmentVariable)),
            commandLineOptions.Force);
    }

    private static string ResolveAuthMode(
        RawCommandLineOptions commandLineOptions,
        Func<string, string?> getEnvironmentVariable) =>
        commandLineOptions.AuthModeSpecified
            ? NormalizeCommandLineNonSecretScalar(commandLineOptions.AuthMode, isSpecified: true)
                ?? string.Empty
            : NormalizeEnvironmentNonSecretScalar(
                getEnvironmentVariable(AuthModeEnvironmentVariable))
                ?? "api-key";

    private static string? ResolveEndpoint(
        RawCommandLineOptions commandLineOptions,
        Func<string, string?> getEnvironmentVariable) =>
        commandLineOptions.EndpointSpecified
            ? NormalizeCommandLineNonSecretScalar(commandLineOptions.Endpoint, isSpecified: true)
            : NormalizeEnvironmentNonSecretScalar(
                getEnvironmentVariable(EndpointEnvironmentVariable));

    private static string? FirstConfigured(string? primary, string? fallback) =>
        primary is null ? fallback : primary;

    private static string? NormalizeCommandLineNonSecretScalar(string? value, bool isSpecified)
    {
        string? trimmedValue = value?.Trim();
        return !isSpecified && string.IsNullOrEmpty(trimmedValue) ? null : trimmedValue;
    }

    private static string? NormalizeEnvironmentNonSecretScalar(string? value)
    {
        string? trimmedValue = value?.Trim();
        return string.IsNullOrEmpty(trimmedValue) ? null : trimmedValue;
    }
}
