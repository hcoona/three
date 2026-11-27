using System.Text;
using System.Text.RegularExpressions;
using WorkflowDelivery.Destinations;

namespace WorkflowDelivery.Release;

// Extracts a bounded 1-2 section Keep a Changelog body for GitHub Releases, failing closed
// when the required release section is missing or carries no substantive content.
internal static partial class ReleaseChangelog
{
    internal static string Extract(string markdown, string version, string revision,
        string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidDataException("Missing release changelog version.");
        if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(repositoryPath))
            throw new InvalidDataException("Missing release changelog provenance.");

        bool stable = StableVersion().IsMatch(version);
        List<Section> sections = ParseSections(markdown, repositoryPath);

        int targetIndex = sections.FindIndex(section =>
            string.Equals(section.Id, version, StringComparison.Ordinal));
        if (targetIndex < 0 && !stable)
            targetIndex = sections.FindIndex(section => IsUnreleased(section.Id));
        if (targetIndex < 0)
            throw new InvalidDataException(
                $"Missing required CHANGELOG.md section for {version} in {repositoryPath}.");

        Section primary = sections[targetIndex];
        if (!HasSubstantiveContent(primary.Lines))
            throw new InvalidDataException(
                $"Empty required CHANGELOG.md section for {version} in {repositoryPath}.");

        var selected = new List<string> { RenderSection(primary) };
        Section? previous = sections.Skip(targetIndex + 1)
            .FirstOrDefault(section => section.Id is not null && !IsUnreleased(section.Id));
        if (previous is not null && HasSubstantiveContent(previous.Lines))
            selected.Add(RenderSection(previous));

        string escapedPath = string.Join('/', repositoryPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));
        string url = $"https://github.com/{GithubApi.Repository}/blob/{revision}/{escapedPath}";
        return $"{string.Join("\n\n", selected)}\n\nFull changelog: {url}";
    }

    internal static bool IsStableVersion(string version) =>
        !string.IsNullOrWhiteSpace(version) && StableVersion().IsMatch(version);

    private static bool IsUnreleased(string? id) =>
        string.Equals(id, "Unreleased", StringComparison.OrdinalIgnoreCase);

    private sealed record Section(string? Id, string Heading, List<string> Lines);

    private static List<Section> ParseSections(string markdown, string repositoryPath)
    {
        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        var sections = new List<Section>();
        var seenVersions = new HashSet<string>(StringComparer.Ordinal);
        bool seenUnreleased = false;
        Section? current = null;
        char fenceChar = '\0';
        int fenceLength = 0;

        foreach (string line in lines)
        {
            if (TryMatchFence(line, out char ch, out int len))
            {
                if (fenceLength == 0)
                {
                    fenceChar = ch;
                    fenceLength = len;
                }
                else if (ch == fenceChar && len >= fenceLength &&
                    line.AsSpan().TrimStart()[len..].Trim().IsEmpty)
                {
                    fenceChar = '\0';
                    fenceLength = 0;
                }
                current?.Lines.Add(line);
                continue;
            }

            if (fenceLength == 0 && LevelTwoHeading().Match(line) is { Success: true } heading)
            {
                if (current is not null) TrimLinkReferences(current.Lines);
                string title = heading.Groups[1].Value.Trim();
                string? id = BracketedId().Match(title) is { Success: true } bracketed
                    ? bracketed.Groups[1].Value.Trim() : null;
                if (id is { Length: > 0 })
                {
                    if (IsUnreleased(id))
                    {
                        if (seenUnreleased)
                            throw new InvalidDataException(
                                $"Duplicate [Unreleased] section in {repositoryPath}.");
                        seenUnreleased = true;
                    }
                    else if (!seenVersions.Add(id))
                        throw new InvalidDataException(
                            $"Duplicate [{id}] section in {repositoryPath}.");
                }
                current = new(id, line.TrimEnd(), []);
                sections.Add(current);
                continue;
            }

            if (current is not null)
                current.Lines.Add(line);
        }

        if (current is not null) TrimLinkReferences(current.Lines);
        return sections;
    }

    private static void TrimLinkReferences(List<string> lines)
    {
        while (lines.Count > 0)
        {
            string tail = lines[^1];
            if (string.IsNullOrWhiteSpace(tail) || LinkDefinition().IsMatch(tail))
                lines.RemoveAt(lines.Count - 1);
            else
                break;
        }
    }

    private static string RenderSection(Section section)
    {
        int start = 0;
        while (start < section.Lines.Count && string.IsNullOrWhiteSpace(section.Lines[start]))
            start++;
        int end = section.Lines.Count - 1;
        while (end >= start && string.IsNullOrWhiteSpace(section.Lines[end]))
            end--;
        if (start > end) return section.Heading;
        var builder = new StringBuilder(section.Heading);
        builder.Append("\n\n");
        for (int i = start; i <= end; i++)
        {
            builder.Append(section.Lines[i].TrimEnd());
            if (i < end) builder.Append('\n');
        }
        return builder.ToString();
    }

    private static bool HasSubstantiveContent(List<string> lines)
    {
        string joined = HtmlComment().Replace(string.Join('\n', lines), "");
        char fenceChar = '\0';
        int fenceLength = 0;
        foreach (string raw in joined.Split('\n'))
        {
            if (TryMatchFence(raw, out char ch, out int len))
            {
                if (fenceLength == 0) { fenceChar = ch; fenceLength = len; }
                else if (ch == fenceChar && len >= fenceLength &&
                    raw.AsSpan().TrimStart()[len..].Trim().IsEmpty)
                {
                    fenceChar = '\0';
                    fenceLength = 0;
                }
                continue;
            }
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            if (fenceLength == 0 && AtxHeading().IsMatch(trimmed)) continue;
            return true;
        }
        return false;
    }

    private static bool TryMatchFence(string line, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;
        int indent = 0;
        while (indent < line.Length && indent < 4 && line[indent] == ' ') indent++;
        if (indent == 4 || indent >= line.Length) return false;
        char first = line[indent];
        if (first is not ('`' or '~')) return false;
        int count = 0;
        while (indent + count < line.Length && line[indent + count] == first) count++;
        if (count < 3) return false;
        if (first == '`' && line.AsSpan(indent + count).Contains('`')) return false;
        fenceChar = first;
        fenceLength = count;
        return true;
    }

    // Stable versions across SemVer2, PEP 440 (optional epoch), and Ruby Gem::Version consist
    // strictly of dot-separated numeric segments without prerelease/dev letters or hyphens.
    [GeneratedRegex(@"^(?:\d+!)?\d+(?:\.\d+)*$")]
    private static partial Regex StableVersion();

    [GeneratedRegex(@"^ {0,3}##\s+(.+?)\s*$")]
    private static partial Regex LevelTwoHeading();

    [GeneratedRegex(@"^\[([^\]]+)\](?:\s|$|-)")]
    private static partial Regex BracketedId();

    [GeneratedRegex(@"^ {0,3}\[[^\]]+\]:\s+\S+.*$")]
    private static partial Regex LinkDefinition();

    [GeneratedRegex(@"^#{1,6}(?:\s|$)")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex HtmlComment();
}
