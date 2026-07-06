namespace Hcoona.DocumentTranslatorCli;

internal sealed record RawTranslationOptions(
    string? InputPath,
    string? OutputPath,
    string? TargetLanguage,
    string? AuthMode,
    string? Endpoint,
    string? ApiKey,
    bool Force);
