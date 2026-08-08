using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Redaction;

public sealed class SecretRedactor
{
    public const string DefaultMask = "[REDACTED]";

    public static SecretRedactor Empty { get; } = new([]);

    private readonly string[] _secrets;
    private readonly string _mask;

    public SecretRedactor(IEnumerable<string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        _secrets = secrets
            .Where(static secret => !string.IsNullOrEmpty(secret))
            .Select(static secret => secret!)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static secret => secret.Length)
            .ToArray();

        _mask = GetMask(_secrets);
    }

    public string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value) || _secrets.Length == 0)
        {
            return value;
        }

        var redacted = RedactMatches(value, _mask).Value;
        while (true)
        {
            // Empty replacement shortens every synthesized match, so this loop terminates.
            var sanitized = RedactMatches(redacted, replacement: string.Empty);
            if (!sanitized.Matched)
            {
                return redacted;
            }

            redacted = sanitized.Value;
        }
    }

    private (string Value, bool Matched) RedactMatches(string value, string replacement)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (var secret in _secrets)
        {
            var start = value.IndexOf(secret, StringComparison.Ordinal);
            while (start >= 0)
            {
                ranges.Add((start, start + secret.Length));
                start = value.IndexOf(secret, start + 1, StringComparison.Ordinal);
            }
        }

        if (ranges.Count == 0)
        {
            return (value, Matched: false);
        }

        ranges.Sort(static (left, right) =>
        {
            var startComparison = left.Start.CompareTo(right.Start);
            return startComparison != 0
                ? startComparison
                : left.End.CompareTo(right.End);
        });

        var redacted = new StringBuilder(value.Length);
        var currentStart = ranges[0].Start;
        var currentEnd = ranges[0].End;
        var copiedThrough = 0;

        for (var index = 1; index < ranges.Count; index++)
        {
            var range = ranges[index];
            if (range.Start <= currentEnd)
            {
                currentEnd = Math.Max(currentEnd, range.End);
                continue;
            }

            redacted.Append(value, copiedThrough, currentStart - copiedThrough);
            redacted.Append(replacement);
            copiedThrough = currentEnd;
            currentStart = range.Start;
            currentEnd = range.End;
        }

        redacted.Append(value, copiedThrough, currentStart - copiedThrough);
        redacted.Append(replacement);
        redacted.Append(value, currentEnd, value.Length - currentEnd);

        return (redacted.ToString(), Matched: true);
    }

    private static string GetMask(IEnumerable<string> secrets)
    {
        foreach (var secret in secrets)
        {
            if (DefaultMask.Contains(secret, StringComparison.Ordinal))
            {
                return string.Empty;
            }
        }

        return DefaultMask;
    }
}
