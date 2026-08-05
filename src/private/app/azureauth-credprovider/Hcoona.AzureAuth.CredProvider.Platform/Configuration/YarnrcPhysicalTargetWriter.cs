using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class YarnrcPhysicalTargetWriter(IFileSystem fileSystem)
    : IConfigurationPhysicalTargetWriter
{
    private const string NpmRegistryServerKey = "npmRegistryServer";
    private const string NpmRegistriesPrefix = "npmRegistries.";
    private const string NpmRegistriesBracketPrefix = "npmRegistries[\"";
    private const string NpmAuthTokenKey = "npmAuthToken";
    private const string NpmAlwaysAuthKey = "npmAlwaysAuth";
    private const string NpmAuthIdentKey = "npmAuthIdent";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);
    private static readonly UnixFileMode OwnerOnlyMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        YarnDocument document = ReadDocument(GetTargetPath(request));
        _ = Apply(document, request, mutate: false);
    }

    public void Write(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        string targetPath = GetTargetPath(request);
        YarnDocument document = ReadDocument(targetPath);
        string original = document.Text;
        string updated = Apply(document, request, mutate: true);
        bool containsSecret = request.Changes.Any(change => change.IsSecretValue);
        if (string.Equals(original, updated, StringComparison.Ordinal))
        {
            if (
                containsSecret
                && fileSystem.FileExists(targetPath)
                && !FileSystemPathSemantics.UsesWindowsPaths(fileSystem)
                && fileSystem.GetUnixFileMode(targetPath) != OwnerOnlyMode
            )
            {
                fileSystem.SetUnixFileMode(targetPath, OwnerOnlyMode);
            }
            return;
        }

        fileSystem.AtomicWriteAllBytes(
            targetPath,
            document.Encode(updated),
            containsSecret
                ? AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
                : AtomicWriteOptions.None
        );
    }

    public bool IsSatisfied(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        YarnDocument document = ReadDocument(GetTargetPath(request));
        return request.Changes.All(change =>
        {
            string? value = document.GetValue(change.Key);
            return change.IsSecretValue
                ? value is not null
                : string.Equals(
                    NormalizeScalar(value),
                    NormalizeScalar(change.Value),
                    StringComparison.Ordinal
                );
        });
    }

    internal static string? GetPlanningValidationViolation(
        ConfigurationChange change,
        CanonicalResourceIdentity? resourceIdentity = null
    )
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.TargetKind != ConfigurationTargetKind.Yarnrc)
        {
            return null;
        }

        if (
            change.Operation
            is not (
                ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
                or ConfigurationChangeOperation.Remove
            )
        )
        {
            return "The Yarn writer supports value-writing and remove operations only.";
        }

        bool remove = change.Operation == ConfigurationChangeOperation.Remove;
        if (
            remove
                ? change.Value is not null
                : change.Value is null
                    || change.Value.Contains('\r')
                    || change.Value.Contains('\n')
                    || change.Value.Any(char.IsControl)
        )
        {
            return "The Yarn writer requires a valid single-line value.";
        }

        if (string.Equals(change.Key, NpmRegistryServerKey, StringComparison.Ordinal))
        {
            if (change.IsSecretValue)
            {
                return "Yarn npmRegistryServer is not secret.";
            }
            return
                remove
                || (
                    resourceIdentity is not null
                    && string.Equals(
                        change.Value,
                        resourceIdentity.ServiceEndpoint.AbsoluteUri,
                        StringComparison.Ordinal
                    )
                )
                ? null
                : "Yarn npmRegistryServer must match the canonical registry identity.";
        }

        if (!TryParseRegistryLeafKey(change.Key, out string registry, out string leaf))
        {
            return "The Yarn writer supports only npmRegistryServer and npmRegistries auth keys.";
        }

        if (
            resourceIdentity is null
            || !MatchesRegistrySelector(change.Key, registry, resourceIdentity)
        )
        {
            return "The Yarn registry selector must match the canonical registry identity.";
        }

        if (string.Equals(leaf, NpmAuthTokenKey, StringComparison.Ordinal))
        {
            return remove || change.IsSecretValue
                ? null
                : "Yarn npmAuthToken values must be marked as secret.";
        }

        if (string.Equals(leaf, NpmAlwaysAuthKey, StringComparison.Ordinal))
        {
            return
                !change.IsSecretValue
                && (
                    remove
                    || string.Equals(change.Value, "true", StringComparison.OrdinalIgnoreCase)
                )
                ? null
                : "Yarn npmAlwaysAuth must be the non-secret value true.";
        }

        return "The Yarn writer does not support npmAuthIdent.";
    }

    private static bool MatchesRegistrySelector(
        string key,
        string registry,
        CanonicalResourceIdentity resourceIdentity
    )
    {
        if (
            RegistryKeysEqual(registry, resourceIdentity.ServiceEndpoint.AbsoluteUri)
        )
        {
            return true;
        }

        NpmCompatibleAuthSelectors selectors = NpmCompatibleAuthSelectorPolicy.Create(
            resourceIdentity
        );
        return string.Equals(key, selectors.YarnAuthTokenKey, StringComparison.Ordinal)
            || string.Equals(key, selectors.YarnAlwaysAuthKey, StringComparison.Ordinal);
    }

    private static bool RegistryKeysEqual(string left, string right) =>
        string.Equals(left.TrimEnd('/'), right.TrimEnd('/'), StringComparison.Ordinal);

    private void ValidateRequest(ConfigurationPhysicalTargetWriterRequest request)
    {
        if (request.TargetKind != ConfigurationTargetKind.Yarnrc)
        {
            throw new NotSupportedException("The Yarn writer received another target kind.");
        }

        _ = GetTargetPath(request);
        if (
            request
                .Changes.GroupBy(change => change.Key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            throw new InvalidOperationException(
                "A Yarn request contains duplicate managed selectors."
            );
        }

        foreach (ConfigurationChange change in request.Changes)
        {
            string? violation = GetPlanningValidationViolation(change, request.ResourceIdentity);
            if (violation is not null)
            {
                throw new NotSupportedException(violation);
            }
        }
    }

    private string GetTargetPath(ConfigurationPhysicalTargetWriterRequest request)
    {
        string[] paths = request
            .Changes.Select(change =>
            {
                if (!fileSystem.IsPathFullyQualified(change.TargetPathOrName))
                {
                    throw new ArgumentException(
                        "The Yarn configuration path must be fully qualified."
                    );
                }
                return fileSystem.GetFullPath(change.TargetPathOrName);
            })
            .Distinct(FileSystemPathSemantics.GetComparer(fileSystem))
            .ToArray();
        return paths.Length == 1
            ? paths[0]
            : throw new NotSupportedException("A Yarn request must target one file.");
    }

    private YarnDocument ReadDocument(string targetPath)
    {
        if (!fileSystem.FileExists(targetPath))
        {
            if (fileSystem.DirectoryExists(targetPath))
            {
                throw new InvalidOperationException(
                    "The Yarn configuration target is a directory."
                );
            }
            return YarnDocument.Missing();
        }

        return YarnDocument.Parse(fileSystem.ReadAllBytes(targetPath));
    }

    private string Apply(
        YarnDocument document,
        ConfigurationPhysicalTargetWriterRequest request,
        bool mutate
    )
    {
        YarnDocument working = mutate ? document : document.Clone();
        foreach (ConfigurationChange change in OrderChanges(request.Changes))
        {
            string? existing = working.GetValue(change.Key);
            bool remove =
                request.PlanOperation == ConfigurationPlanOperation.Remove
                || change.Operation == ConfigurationChangeOperation.Remove;
            if (remove)
            {
                if (!request.IsOwned(change, fileSystem))
                {
                    throw new InvalidOperationException(
                        "Yarn configuration removal requires a recognized owned selector."
                    );
                }
                working.Remove(change.Key);
                continue;
            }

            if (existing is not null && !request.IsOwned(change, fileSystem))
            {
                throw new InvalidOperationException(
                    "The Yarn selector already exists without recognized ownership."
                );
            }

            if (
                TryParseRegistryLeafKey(change.Key, out string registry, out string leaf)
                && !string.Equals(leaf, NpmAuthIdentKey, StringComparison.Ordinal)
                && working.HasAuthIdent(registry)
            )
            {
                throw new InvalidOperationException(
                    "Yarn npmAuthIdent conflicts with product-owned npmAuthToken."
                );
            }

            working.Set(change.Key, change.Value!);
        }

        return working.Render();
    }

    private static ConfigurationChange[] OrderChanges(IReadOnlyList<ConfigurationChange> changes) =>
        changes
            .OrderBy(change =>
                string.Equals(change.Key, NpmRegistryServerKey, StringComparison.Ordinal) ? 0
                : change.Key.EndsWith("." + NpmAlwaysAuthKey, StringComparison.Ordinal) ? 1
                : 2
            )
            .ToArray();

    private static bool TryParseRegistryLeafKey(string key, out string registry, out string leaf)
    {
        registry = string.Empty;
        leaf = string.Empty;
        if (key.StartsWith(NpmRegistriesBracketPrefix, StringComparison.Ordinal))
        {
            int bracketSeparator = key.LastIndexOf("\"].", StringComparison.Ordinal);
            if (bracketSeparator <= NpmRegistriesBracketPrefix.Length)
            {
                return false;
            }

            registry = key[NpmRegistriesBracketPrefix.Length..bracketSeparator];
            leaf = key[(bracketSeparator + 3)..];
            return leaf is NpmAuthTokenKey or NpmAlwaysAuthKey;
        }

        if (!key.StartsWith(NpmRegistriesPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        int separator = key.LastIndexOf('.');
        if (separator <= NpmRegistriesPrefix.Length)
        {
            return false;
        }

        registry = key[NpmRegistriesPrefix.Length..separator];
        leaf = key[(separator + 1)..];
        return leaf is NpmAuthTokenKey or NpmAlwaysAuthKey or NpmAuthIdentKey;
    }

    private static string? NormalizeScalar(string? value) =>
        value is null ? null : Unquote(value).Trim().ToLowerInvariant();

    private sealed class YarnDocument
    {
        private readonly List<string> lines;
        private readonly bool hadBom;
        private readonly string newLine;
        private readonly bool trailingNewLine;

        private YarnDocument(List<string> lines, bool hadBom, string newLine, bool trailingNewLine)
        {
            this.lines = lines;
            this.hadBom = hadBom;
            this.newLine = newLine;
            this.trailingNewLine = trailingNewLine;
        }

        public string Text => Render();

        public static YarnDocument Missing() =>
            new([], hadBom: false, "\n", trailingNewLine: true);

        public static YarnDocument Parse(byte[] bytes)
        {
            bool bom = bytes is [0xEF, 0xBB, 0xBF, ..];
            string text = Utf8NoBom.GetString(bom ? bytes[3..] : bytes);
            return new YarnDocument(
                SplitLines(text),
                bom,
                text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
                text.EndsWith('\n')
            );
        }

        public YarnDocument Clone() => new([.. lines], hadBom, newLine, trailingNewLine);

        public string? GetValue(string key)
        {
            if (string.Equals(key, NpmRegistryServerKey, StringComparison.Ordinal))
            {
                int topLevelIndex = FindTopLevelKey(key);
                return topLevelIndex < 0 ? null : ParseValue(lines[topLevelIndex]);
            }

            if (!TryParseRegistryLeafKey(key, out string registry, out string leaf))
            {
                return null;
            }

            int index = FindRegistryLeaf(registry, leaf);
            return index < 0 ? null : ParseValue(lines[index]);
        }

        public bool HasAuthIdent(string registry) =>
            FindRegistryLeaf(registry, NpmAuthIdentKey) >= 0
            || FindTopLevelKey(NpmAuthIdentKey) >= 0;

        public void Set(string key, string value)
        {
            if (string.Equals(key, NpmRegistryServerKey, StringComparison.Ordinal))
            {
                int topLevelIndex = FindTopLevelKey(key);
                string topLevelRendered = key + ": " + Quote(value);
                if (topLevelIndex >= 0)
                {
                    lines[topLevelIndex] = topLevelRendered;
                }
                else
                {
                    lines.Insert(0, topLevelRendered);
                }
                return;
            }

            if (!TryParseRegistryLeafKey(key, out string registry, out string leaf))
            {
                throw new NotSupportedException("Unsupported Yarn selector.");
            }

            int existing = FindRegistryLeaf(registry, leaf);
            string rendered =
                "    "
                + leaf
                + ": "
                + (
                    string.Equals(leaf, NpmAlwaysAuthKey, StringComparison.Ordinal)
                        ? value.ToLowerInvariant()
                        : Quote(value)
                );
            if (existing >= 0)
            {
                lines[existing] = rendered;
                return;
            }

            int registryLine = EnsureRegistryBlock(registry);
            int insert = registryLine + 1;
            while (insert < lines.Count)
            {
                if (IsCommentOnly(lines[insert]))
                {
                    insert++;
                    continue;
                }
                if (CountIndent(lines[insert]) < 4)
                {
                    break;
                }
                insert++;
            }
            lines.Insert(insert, rendered);
        }

        public void Remove(string key)
        {
            if (string.Equals(key, NpmRegistryServerKey, StringComparison.Ordinal))
            {
                int topLevelIndex = FindTopLevelKey(key);
                if (topLevelIndex >= 0)
                {
                    lines.RemoveAt(topLevelIndex);
                }
                return;
            }

            if (!TryParseRegistryLeafKey(key, out string registry, out string leaf))
            {
                return;
            }

            int index = FindRegistryLeaf(registry, leaf);
            if (index >= 0)
            {
                lines.RemoveAt(index);
            }
            RemoveEmptyRegistryBlock(registry);
            RemoveEmptyRegistriesSection();
        }

        public string Render()
        {
            string text = string.Join(newLine, lines);
            return trailingNewLine && lines.Count > 0 ? text + newLine : text;
        }

        public byte[] Encode(string value)
        {
            byte[] bytes = Utf8NoBom.GetBytes(value);
            return hadBom ? [0xEF, 0xBB, 0xBF, .. bytes] : bytes;
        }

        private int EnsureRegistryBlock(string registry)
        {
            int existing = FindRegistryHeader(registry);
            if (existing >= 0)
            {
                return existing;
            }

            int registries = FindTopLevelKey("npmRegistries");
            if (registries < 0)
            {
                lines.Add("npmRegistries:");
                registries = lines.Count - 1;
            }

            int insert = registries + 1;
            while (insert < lines.Count)
            {
                if (IsCommentOnly(lines[insert]))
                {
                    insert++;
                    continue;
                }
                if (CountIndent(lines[insert]) == 0)
                {
                    break;
                }
                insert++;
            }
            lines.Insert(insert, "  " + Quote(registry) + ":");
            return insert;
        }

        private int FindTopLevelKey(string key)
        {
            var matches = new List<int>();
            for (var index = 0; index < lines.Count; index++)
            {
                if (
                    CountIndent(lines[index]) == 0
                    && TryParseYamlKey(lines[index], out string? parsed)
                    && string.Equals(parsed, key, StringComparison.Ordinal)
                )
                {
                    matches.Add(index);
                }
            }
            return SingleOrMissing(matches, key);
        }

        private int FindRegistryHeader(string registry)
        {
            int section = FindTopLevelKey("npmRegistries");
            if (section < 0)
            {
                return -1;
            }

            var matches = new List<int>();
            for (var index = section + 1; index < lines.Count; index++)
            {
                if (IsCommentOnly(lines[index]))
                {
                    continue;
                }
                int indent = CountIndent(lines[index]);
                if (indent == 0 && !string.IsNullOrWhiteSpace(lines[index]))
                {
                    break;
                }
                if (
                    indent == 2
                    && TryParseYamlKey(lines[index], out string? parsed)
                    && RegistryKeysEqual(Unquote(parsed!), registry)
                )
                {
                    matches.Add(index);
                }
            }
            return SingleOrMissing(matches, registry);
        }

        private int FindRegistryLeaf(string registry, string leaf)
        {
            int header = FindRegistryHeader(registry);
            if (header < 0)
            {
                return -1;
            }

            var matches = new List<int>();
            for (var index = header + 1; index < lines.Count; index++)
            {
                if (IsCommentOnly(lines[index]))
                {
                    continue;
                }
                int indent = CountIndent(lines[index]);
                if (indent <= 2 && !string.IsNullOrWhiteSpace(lines[index]))
                {
                    break;
                }
                if (
                    indent == 4
                    && TryParseYamlKey(lines[index], out string? parsed)
                    && string.Equals(parsed, leaf, StringComparison.Ordinal)
                )
                {
                    matches.Add(index);
                }
            }
            return SingleOrMissing(matches, leaf);
        }

        private void RemoveEmptyRegistryBlock(string registry)
        {
            int header = FindRegistryHeader(registry);
            if (header < 0)
            {
                return;
            }

            int next = header + 1;
            while (
                next < lines.Count
                && (string.IsNullOrWhiteSpace(lines[next]) || IsCommentOnly(lines[next]))
            )
            {
                next++;
            }
            if (next >= lines.Count || CountIndent(lines[next]) <= 2)
            {
                lines.RemoveAt(header);
            }
        }

        private void RemoveEmptyRegistriesSection()
        {
            int section = FindTopLevelKey("npmRegistries");
            if (section < 0)
            {
                return;
            }

            int next = section + 1;
            while (
                next < lines.Count
                && (string.IsNullOrWhiteSpace(lines[next]) || IsCommentOnly(lines[next]))
            )
            {
                next++;
            }
            if (next >= lines.Count || CountIndent(lines[next]) == 0)
            {
                lines.RemoveAt(section);
            }
        }

        private static int SingleOrMissing(List<int> matches, string selector) =>
            matches.Count switch
            {
                0 => -1,
                1 => matches[0],
                _ => throw new InvalidOperationException(
                    "The managed Yarn selector is declared more than once: " + selector
                ),
            };

        private static bool TryParseYamlKey(string line, out string? key)
        {
            key = null;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                return false;
            }
            int colon = FindKeyValueSeparator(trimmed);
            if (colon <= 0)
            {
                return false;
            }
            key = Unquote(trimmed[..colon].Trim());
            return key.Length > 0;
        }

        private static string ParseValue(string line)
        {
            string trimmed = line.Trim();
            int colon = FindKeyValueSeparator(trimmed);
            return colon < 0 ? string.Empty : Unquote(trimmed[(colon + 1)..].Trim());
        }

        private static int FindKeyValueSeparator(string value)
        {
            char quote = '\0';
            for (var index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (quote == '\0' && character is '\'' or '"')
                {
                    quote = character;
                    continue;
                }
                if (quote != '\0' && character == quote)
                {
                    if (quote == '\'' && index + 1 < value.Length && value[index + 1] == '\'')
                    {
                        index++;
                        continue;
                    }
                    quote = '\0';
                    continue;
                }
                if (
                    quote == '\0'
                    && character == ':'
                    && (index == value.Length - 1 || char.IsWhiteSpace(value[index + 1]))
                )
                {
                    return index;
                }
            }
            return -1;
        }

        private static int CountIndent(string line)
        {
            int count = 0;
            while (count < line.Length && line[count] == ' ')
            {
                count++;
            }
            return count;
        }

        private static bool IsCommentOnly(string line) => line.TrimStart().StartsWith('#');

        private static List<string> SplitLines(string text)
        {
            if (text.Length == 0)
            {
                return [];
            }

            List<string> result = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .ToList();
            if (text.EndsWith('\n'))
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        if (
            trimmed.Length >= 2
            && (
                (trimmed[0] == '\'' && trimmed[^1] == '\'')
                || (trimmed[0] == '"' && trimmed[^1] == '"')
            )
        )
        {
            string inner = trimmed[1..^1];
            return trimmed[0] == '\''
                ? inner.Replace("''", "'", StringComparison.Ordinal)
                : inner
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal);
        }
        return trimmed;
    }
}
