namespace Hcoona.DocumentTranslatorCli;

internal sealed record CommandLineParseResult(
    RawCommandLineOptions Options,
    IReadOnlyList<string> Errors,
    bool ShowHelp,
    string HelpText);

internal sealed record RawCommandLineOptions(
    string? InputPath,
    string? OutputPath,
    string? TargetLanguage,
    string? AuthMode,
    string? Endpoint,
    string? ApiKey,
    string? MarkdownMode,
    bool Force,
    bool TargetLanguageSpecified = false,
    bool AuthModeSpecified = false,
    bool EndpointSpecified = false,
    bool MarkdownModeSpecified = false,
    string? Region = null,
    bool RegionSpecified = false);
