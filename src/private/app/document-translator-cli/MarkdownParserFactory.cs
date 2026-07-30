using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;

namespace Hcoona.DocumentTranslatorCli;

internal static class MarkdownParserFactory
{
    public static MarkdownPipeline CreateV1Pipeline() =>
        new MarkdownPipelineBuilder()
            .UsePipeTables(new PipeTableOptions())
            .UseTaskLists()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseFootnotes()
            .UseYamlFrontMatter()
            .UsePreciseSourceLocation()
            .Build();
}
