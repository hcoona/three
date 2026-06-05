namespace Hcoona.DocumentTranslatorCli;

internal sealed record TranslationOptions(
    string InputPath,
    string OutputPath,
    string TargetLanguage,
    Uri Endpoint,
    AuthMode AuthMode,
    string? ApiKey,
    bool Force,
    string OriginalFileName,
    string ContentType);
