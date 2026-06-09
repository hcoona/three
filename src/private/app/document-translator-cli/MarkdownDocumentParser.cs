using Markdig;
using Markdig.Syntax;

namespace Hcoona.DocumentTranslatorCli;

internal sealed class MarkdownDocumentParser
{
    private readonly MarkdownPipeline pipeline;

    public MarkdownDocumentParser(MarkdownPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        this.pipeline = pipeline;
    }

    public static MarkdownDocumentParser CreateV1() =>
        new(MarkdownParserFactory.CreateV1Pipeline());

    public MarkdownParseResult Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        MarkdownDocument document = Markdown.Parse(markdown, pipeline);
        return new MarkdownParseResult(document, []);
    }
}
