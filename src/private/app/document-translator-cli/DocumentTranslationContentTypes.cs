namespace Hcoona.DocumentTranslatorCli;

internal static class DocumentTranslationContentTypes
{
    private static readonly Dictionary<string, string> ContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"] = "text/csv",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".htm"] = "text/html",
            [".html"] = "text/html",
            [".mht"] = "message/rfc822",
            [".mhtml"] = "message/rfc822",
            [".msg"] = "application/vnd.ms-outlook",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".tab"] = "text/tab-separated-values",
            [".tsv"] = "text/tab-separated-values",
            [".txt"] = "text/plain",
            [".xlf"] = "application/xliff+xml",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        };

    public static bool TryGetContentType(string extension, out string contentType)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return ContentTypes.TryGetValue(extension.ToLowerInvariant(), out contentType!);
    }
}
