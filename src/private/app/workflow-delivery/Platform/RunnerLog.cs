namespace WorkflowDelivery.Platform;

// Writes text that may carry native output to a runner step log. GitHub Actions parses a
// line as a workflow command when it starts with "::" after leading whitespace, and matches
// the legacy "##[" form anywhere in a line. Every line therefore gets a non-whitespace
// prefix, and each "##[" becomes "#\#[" (a backslash inserted), so no line can start or
// contain a command. The retained artifacts keep the original text.
internal static class RunnerLog
{
    internal static void Write(TextWriter writer, string text, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Contains('\n') || prefix.Contains('\r'))
            throw new ArgumentException("A runner log prefix must be visible single-line text.",
                nameof(prefix));
        foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
            writer.WriteLine(prefix + Neutralize(line));
    }

    internal static string Neutralize(string line) =>
        line.Replace("##[", "#\\#[", StringComparison.Ordinal);
}
