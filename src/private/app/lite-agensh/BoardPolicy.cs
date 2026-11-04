using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteAgensh;

// Derived from the pinned Agensh board-service policies; see THIRD-PARTY-NOTICES.txt.
internal static partial class BoardPolicy
{
    internal static readonly string[] Types =
    [
        "FACT",
        "OBSERVED",
        "FAIL",
        "CLAIM",
        "PATCH_SUMMARY",
    ];
    private static readonly string[] InvalidEvidence =
    [
        "tbd",
        "pending",
        "not verified",
        "unverified",
        "should work",
        "should pass",
        "looks right",
        "looks correct",
        "seems to work",
        "to be verified",
        "will verify",
        "n/a",
    ];
    private static readonly string[] WeakEvidence =
    [
        "pending",
        "not verified",
        "unverified",
        "should",
        "will verify",
        "to be verified",
        "tbd",
        "looks",
        "seems",
    ];

    internal static string Admit(string type, string content)
    {
        if (!Types.Contains(type, StringComparer.Ordinal))
        {
            throw new OperationException("invalid_arguments", "Unknown board type.");
        }

        content = content.Trim();
        var cap = type == "PATCH_SUMMARY" ? 300 : 100;
        // Python counts Unicode code points, not UTF-16 code units.
        content = string.Concat(content.EnumerateRunes().Take(cap).Select(rune => rune.ToString()));
        if (content.Length == 0 || content.ToLowerInvariant() is "(none)" or "none" or "-" or "—")
        {
            throw new OperationException(
                "invalid_arguments",
                "Board admission rejected an empty/placeholder gist."
            );
        }

        if (type == "PATCH_SUMMARY")
        {
            var evidence = Field(content, "evidence")?.ToLowerInvariant();
            if (
                string.IsNullOrWhiteSpace(evidence)
                || InvalidEvidence.Any(phrase =>
                    evidence.Equals(phrase, StringComparison.Ordinal)
                    || (
                        evidence.StartsWith(phrase, StringComparison.Ordinal)
                        && (
                            evidence.Length == phrase.Length
                            || !char.IsLetter(evidence[phrase.Length])
                        )
                    )
                )
            )
            {
                throw new OperationException(
                    "invalid_arguments",
                    "Board admission rejected placeholder patch evidence."
                );
            }
        }

        return content;
    }

    internal static bool IsVerifiedCheckpoint(string content)
    {
        var evidence = Field(content, "evidence");
        return !string.IsNullOrWhiteSpace(evidence)
            && !WeakEvidence.Any(phrase =>
                evidence.Contains(phrase, StringComparison.OrdinalIgnoreCase)
            )
            && SolidEvidence().IsMatch(evidence);
    }

    internal static long? ParseLabel(string label)
    {
        label = label.Trim();
        return
            label.Length > 1
            && label[0] == 'G'
            && label.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0
            && long.TryParse(
                label.AsSpan(1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id
            )
            && id > 0
            ? id
            : null;
    }

    internal static (string Sql, (string Name, object? Value)[] Parameters) CompileQuery(
        string query
    )
    {
        var parser = new QueryParser(query);
        return parser.Compile();
    }

    internal static string Normalize(string content) =>
        string.Join(' ', content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static bool CitesTask(string content, long id) =>
        TaskReference()
            .Matches(content)
            .Any(match =>
                long.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var reference
                )
                && reference == id
            );

    [GeneratedRegex(@"(?<![\w/])#(\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex TaskReference();

    internal static string? SemanticKey(string type, string content)
    {
        var files = Captures(FileAnchor(), content);
        var symbols = Captures(SymbolAnchor(), content);
        var lines = LineAnchor()
            .Matches(content)
            .Select(match =>
                (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).TrimStart(
                    '0'
                )
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var errors = Captures(ErrorAnchor(), content);
        var tests = Captures(TestAnchor(), content);
        var strong =
            (files.Length > 0 && (symbols.Length > 0 || lines.Length > 0 || tests.Length > 0))
            || (errors.Length > 0 && (files.Length > 0 || lines.Length > 0 || tests.Length > 0))
            || tests.Length > 0;
        return strong
            ? string.Join(
                '\0',
                type,
                string.Join('|', files),
                string.Join('|', symbols),
                string.Join('|', lines),
                string.Join('|', errors),
                string.Join('|', tests)
            )
            : null;
    }

    private static string[] Captures(Regex regex, string content) =>
        regex
            .Matches(content)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    [GeneratedRegex(@"\b([A-Za-z0-9_./\-]+\.py)\b", RegexOptions.CultureInvariant)]
    private static partial Regex FileAnchor();

    [GeneratedRegex(@"\b(?:def|class)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolAnchor();

    [GeneratedRegex(
        @"(?:\bline\s+(\d+)\b|\.py:(\d+)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex LineAnchor();

    [GeneratedRegex(
        @"\b([A-Z][A-Za-z]*(?:Error|Exception|Warning))\b",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex ErrorAnchor();

    [GeneratedRegex(
        @"\b(test_[A-Za-z0-9_]+(?:\.py)?(?:::[A-Za-z0-9_:]+)?)\b",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex TestAnchor();

    private static string? Field(string content, string name)
    {
        var prefix = name + "=";
        foreach (var part in content.Split('|'))
        {
            var field = part.Trim();
            if (field.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return field[prefix.Length..].Trim();
            }
        }

        return null;
    }

    [GeneratedRegex(
        @"\b(pass(?:ed|es)?|verified|confirmed|no longer|now returns|now produces)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SolidEvidence();

    private sealed class QueryParser
    {
        private readonly List<string> tokens = [];
        private readonly List<(string Name, object? Value)> parameters = [];
        private int position;
        private int depth;

        internal QueryParser(string query)
        {
            var word = new StringBuilder();
            foreach (var character in query)
            {
                if (character is '|' or '&' or '(' or ')')
                {
                    Flush();
                    tokens.Add(character.ToString());
                }
                else
                {
                    word.Append(character);
                }
            }

            Flush();
            return;

            void Flush()
            {
                var term = word.ToString().Trim();
                if (term.Length > 0)
                {
                    tokens.Add(term);
                }

                word.Clear();
            }
        }

        internal (string Sql, (string Name, object? Value)[] Parameters) Compile()
        {
            var sql = Or();
            if (position != tokens.Count)
            {
                throw InvalidQuery();
            }

            return (sql, [.. parameters]);
        }

        private string Or()
        {
            var parts = new List<string> { And() };
            while (Take("|"))
            {
                parts.Add(And());
            }

            return "(" + string.Join(" OR ", parts) + ")";
        }

        private string And()
        {
            var parts = new List<string> { Factor() };
            while (Take("&"))
            {
                parts.Add(Factor());
            }

            return "(" + string.Join(" AND ", parts) + ")";
        }

        private string Factor()
        {
            if (Take("("))
            {
                if (++depth > 50)
                {
                    throw InvalidQuery();
                }

                var inner = Or();
                if (!Take(")"))
                {
                    throw InvalidQuery();
                }

                depth--;
                return inner;
            }

            if (position == tokens.Count || tokens[position] is ")" or "|" or "&")
            {
                throw InvalidQuery();
            }

            if (parameters.Count == 50)
            {
                throw new OperationException(
                    "invalid_arguments",
                    "Board queries allow at most 50 literal terms."
                );
            }

            var parameter = "$term" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add((parameter, tokens[position++]));
            return $"lite_contains(content,{parameter})";
        }

        private bool Take(string token)
        {
            if (
                position == tokens.Count
                || !tokens[position].Equals(token, StringComparison.Ordinal)
            )
            {
                return false;
            }

            position++;
            return true;
        }

        private static OperationException InvalidQuery() =>
            new(
                "invalid_arguments",
                "Invalid board query: use literal terms, &, | and balanced parentheses."
            );
    }
}
