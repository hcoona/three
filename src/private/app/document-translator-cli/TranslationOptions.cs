namespace Hcoona.DocumentTranslatorCli;

internal sealed record TranslationOptions(
    string InputPath,
    string OutputPath,
    string TargetLanguage,
    Uri Endpoint,
    AuthMode AuthMode,
    string? ApiKey,
    MarkdownMode MarkdownMode,
    TranslationRoute TranslationRoute,
    bool IsMarkdownExtension,
    bool Force,
    string OriginalFileName,
    string? LegacyDocumentContentType);
