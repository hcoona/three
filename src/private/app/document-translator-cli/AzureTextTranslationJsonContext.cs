using System.Text.Json.Serialization;

namespace Hcoona.DocumentTranslatorCli;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AzureTextTranslationRequest[]))]
[JsonSerializable(typeof(AzureTextTranslationResult[]))]
internal sealed partial class AzureTextTranslationJsonContext : JsonSerializerContext;

internal sealed record AzureTextTranslationRequest(
    [property: JsonPropertyName("Text")] string Text);

internal sealed record AzureTextTranslationResult(
    [property: JsonPropertyName("translations")] AzureTextTranslation[]? Translations);

internal sealed record AzureTextTranslation(
    [property: JsonPropertyName("text")] string? Text);
