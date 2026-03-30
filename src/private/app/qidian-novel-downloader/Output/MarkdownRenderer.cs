using System.Text;
namespace Hcoona.QidianNovelDownloader.Output;

internal static class MarkdownRenderer
{
    public static string Render(
        CatalogSnapshot catalog,
        IReadOnlyDictionary<string, RenderedChapter> renderedChapters)
    {
        StringBuilder builder = new();

        foreach (VolumeDescriptor volume in catalog.Volumes)
        {
            bool hasAnyChapter = volume.Chapters.Any(chapter => renderedChapters.ContainsKey(chapter.ChapterId));
            if (!hasAnyChapter)
            {
                continue;
            }

            builder.Append("# ").AppendLine(volume.Title).AppendLine();
            foreach (ChapterDescriptor chapter in volume.Chapters)
            {
                if (!renderedChapters.TryGetValue(chapter.ChapterId, out RenderedChapter? renderedChapter))
                {
                    continue;
                }

                builder.Append("## ").AppendLine(renderedChapter.Title).AppendLine();
                foreach (string paragraph in renderedChapter.Paragraphs)
                {
                    builder.AppendLine(paragraph).AppendLine();
                }
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }
}
