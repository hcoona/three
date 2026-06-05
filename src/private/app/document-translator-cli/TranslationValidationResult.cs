namespace Hcoona.DocumentTranslatorCli;

internal sealed record TranslationValidationResult(
    TranslationOptions? Options,
    IReadOnlyList<string> Errors);
