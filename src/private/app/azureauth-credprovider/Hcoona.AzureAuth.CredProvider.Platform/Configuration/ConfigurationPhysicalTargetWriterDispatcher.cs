using System.Globalization;
using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal interface IConfigurationPhysicalTargetWriterDispatcher
{
    void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );

    ValueTask Dispatch(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );

    bool IsSatisfied(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed class ConfigurationPhysicalTargetWriterDispatcher(IFileSystem fileSystem)
    : IConfigurationPhysicalTargetWriterDispatcher
{
    private readonly GitConfigPhysicalTargetWriter gitConfigWriter = new(fileSystem);
    private readonly NpmrcPhysicalTargetWriter npmrcWriter = new(fileSystem);
    private readonly NuGetPluginLayoutPhysicalTargetWriter nuGetPluginLayoutWriter = new(
        fileSystem
    );
    private readonly PythonKeyringPhysicalTargetWriter pythonKeyringWriter = new(fileSystem);
    private readonly YarnrcPhysicalTargetWriter yarnrcWriter = new(fileSystem);

    public void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    ) => GetWriter(request.TargetKind).Validate(request, cancellationToken);

    public ValueTask Dispatch(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        GetWriter(request.TargetKind).Write(request, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public bool IsSatisfied(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    ) => GetWriter(request.TargetKind).IsSatisfied(request, cancellationToken);

    private IConfigurationPhysicalTargetWriter GetWriter(ConfigurationTargetKind targetKind) =>
        targetKind switch
        {
            ConfigurationTargetKind.GitConfig => gitConfigWriter,
            ConfigurationTargetKind.Npmrc => npmrcWriter,
            ConfigurationTargetKind.Yarnrc => yarnrcWriter,
            ConfigurationTargetKind.NuGetPluginLayout => nuGetPluginLayoutWriter,
            ConfigurationTargetKind.PythonKeyringBackend or ConfigurationTargetKind.KeyringShim =>
                pythonKeyringWriter,
            _ => throw new NotSupportedException(
                "No physical configuration writer is registered for this target kind."
            ),
        };
}

internal interface IConfigurationPhysicalTargetWriter
{
    void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );

    void Write(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );

    bool IsSatisfied(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed record ConfigurationPhysicalTargetWriterRequest
{
    public ConfigurationPhysicalTargetWriterRequest(
        ConfigurationPlanOperation planOperation,
        ConfigurationTargetKind targetKind,
        ConfigurationChangeOperation changeOperation,
        ConfigurationChange change,
        IReadOnlyList<ConfigurationOwnershipManifestEntry>? existingOwnershipEntries = null
    )
        : this(planOperation, targetKind, [change], existingOwnershipEntries)
    {
        if (changeOperation != change.Operation)
        {
            throw new ArgumentException(
                "The request operation must match the change operation.",
                nameof(changeOperation)
            );
        }
    }

    public ConfigurationPhysicalTargetWriterRequest(
        ConfigurationPlanOperation planOperation,
        ConfigurationTargetKind targetKind,
        IReadOnlyList<ConfigurationChange> changes,
        IReadOnlyList<ConfigurationOwnershipManifestEntry>? existingOwnershipEntries = null
    )
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0 || changes.Any(change => change.TargetKind != targetKind))
        {
            throw new ArgumentException(
                "A physical writer request requires changes for one target kind.",
                nameof(changes)
            );
        }

        PlanOperation = planOperation;
        TargetKind = targetKind;
        Changes = changes.ToArray();
        ExistingOwnershipEntries = existingOwnershipEntries?.ToArray() ?? [];
    }

    public ConfigurationPlanOperation PlanOperation { get; }

    public ConfigurationTargetKind TargetKind { get; }

    public IReadOnlyList<ConfigurationChange> Changes { get; }

    public IReadOnlyList<ConfigurationOwnershipManifestEntry> ExistingOwnershipEntries { get; }

    public CanonicalResourceIdentity? ResourceIdentity { get; init; }

    public ConfigurationChangeOperation ChangeOperation => Change.Operation;

    public ConfigurationChange Change =>
        Changes.Count == 1
            ? Changes[0]
            : throw new InvalidOperationException(
                "The physical writer request contains multiple changes."
            );

    public bool IsOwned(ConfigurationChange change, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        string key =
            change.TargetKind == ConfigurationTargetKind.GitConfig
                ? GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(change.Key)
                : change.Key;
        return ExistingOwnershipEntries.Any(entry =>
            entry.TargetKind == change.TargetKind
            && string.Equals(
                fileSystem.GetFullPath(entry.TargetPathOrName),
                fileSystem.GetFullPath(change.TargetPathOrName),
                FileSystemPathSemantics.GetComparison(fileSystem)
            )
            && string.Equals(
                entry.TargetKind == ConfigurationTargetKind.GitConfig
                    ? GitConfigPhysicalTargetWriter.CanonicalizeSupportedConfigurationKey(entry.Key)
                    : entry.Key,
                key,
                StringComparison.Ordinal
            )
        );
    }
}

internal sealed class GitConfigPhysicalTargetWriter(IFileSystem fileSystem)
    : IConfigurationPhysicalTargetWriter
{
    private const string HelperKey = "credential.helper";
    private const string UseHttpPathKey = "credential.https://dev.azure.com.useHttpPath";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);

    public void Validate(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        GitDocument document = ReadDocument(GetTargetPath(request));
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
        GitDocument document = ReadDocument(targetPath);
        string original = document.Text;
        string updated = Apply(document, request, mutate: true);
        if (string.Equals(updated, original, StringComparison.Ordinal))
        {
            return;
        }

        fileSystem.AtomicWriteAllBytes(targetPath, document.Encode(updated));
    }

    public bool IsSatisfied(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequest(request);
        GitDocument document = ReadDocument(GetTargetPath(request));
        return request.Changes.All(change =>
        {
            string? current = document.GetValue(CanonicalizeSupportedConfigurationKey(change.Key));
            return change.IsSecretValue
                ? current is not null
                : string.Equals(
                    current,
                    RenderValue(change.Key, change.Value),
                    StringComparison.Ordinal
                );
        });
    }

    internal static string? GetPlanningValidationViolation(ConfigurationChange change)
    {
        if (change.TargetKind != ConfigurationTargetKind.GitConfig)
        {
            return null;
        }

        if (!TryCanonicalizeSupportedConfigurationKey(change.Key, out string canonicalKey))
        {
            return "The Git config writer supports only credential.helper and the canonical "
                + "dev.azure.com useHttpPath selector.";
        }

        if (
            change.Operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh
        )
        {
            if (change.Value is null || change.Value.Contains('\r') || change.Value.Contains('\n'))
            {
                return "Git config value-writing changes require a single-line value.";
            }

            if (
                string.Equals(canonicalKey, UseHttpPathKey, StringComparison.Ordinal)
                && !string.Equals(change.Value, "true", StringComparison.OrdinalIgnoreCase)
            )
            {
                return "The Git config writer requires useHttpPath=true.";
            }

            if (
                string.Equals(canonicalKey, HelperKey, StringComparison.Ordinal)
                && !TryRenderCredentialHelperCommandValue(change.Value, out _)
            )
            {
                return "The Git credential helper value cannot be represented safely.";
            }
        }

        return null;
    }

    internal static bool TryCanonicalizeSupportedConfigurationKey(
        string key,
        out string canonicalKey
    )
    {
        canonicalKey = string.Empty;
        if (string.Equals(key, HelperKey, StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = HelperKey;
            return true;
        }

        if (
            string.Equals(key, UseHttpPathKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                key,
                "credential.\"https://dev.azure.com\".useHttpPath",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            canonicalKey = UseHttpPathKey;
            return true;
        }

        return false;
    }

    internal static string CanonicalizeSupportedConfigurationKey(string key) =>
        TryCanonicalizeSupportedConfigurationKey(key, out string canonicalKey)
            ? canonicalKey
            : throw new NotSupportedException("Unsupported Git configuration key.");

    internal static string RenderCredentialHelperCommandValue(string value) =>
        TryRenderCredentialHelperCommandValue(value, out string? rendered)
            ? rendered
            : throw new NotSupportedException(
                "The Git credential helper value cannot be represented safely."
            );

    private static bool TryRenderCredentialHelperCommandValue(
        string value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? rendered
    )
    {
        rendered = null;
        if (
            value.Length == 0
            || value.Contains('"', StringComparison.Ordinal)
            || value.Any(char.IsControl)
        )
        {
            return false;
        }

        string normalized = IsWindowsAbsolutePath(value) ? value.Replace('\\', '/') : value;
        if (normalized.All(IsUnquotedShellPathCharacter))
        {
            rendered = normalized;
            return true;
        }

        rendered =
            "\""
            + normalized
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("$", "\\$", StringComparison.Ordinal)
                .Replace("`", "\\`", StringComparison.Ordinal)
            + "\"";
        return true;
    }

    private static bool IsWindowsAbsolutePath(string value) =>
        value.Length >= 3
            && char.IsAsciiLetter(value[0])
            && value[1] == ':'
            && value[2] is '\\' or '/'
        || value.StartsWith(@"\\", StringComparison.Ordinal);

    private static bool IsUnquotedShellPathCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '/' or ':' or '.' or '_' or '-';

    private void ValidateRequest(ConfigurationPhysicalTargetWriterRequest request)
    {
        if (request.TargetKind != ConfigurationTargetKind.GitConfig)
        {
            throw new NotSupportedException("The Git config writer received another target kind.");
        }

        string path = GetTargetPath(request);
        if (!fileSystem.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The Git config target path must be fully qualified.");
        }

        foreach (ConfigurationChange change in request.Changes)
        {
            string? violation = GetPlanningValidationViolation(change);
            if (violation is not null)
            {
                throw new NotSupportedException(violation);
            }
        }
    }

    private string GetTargetPath(ConfigurationPhysicalTargetWriterRequest request)
    {
        string[] paths = request
            .Changes.Select(change => fileSystem.GetFullPath(change.TargetPathOrName))
            .Distinct(FileSystemPathSemantics.GetComparer(fileSystem))
            .ToArray();
        return paths.Length == 1
            ? paths[0]
            : throw new NotSupportedException("A Git config writer request must target one file.");
    }

    private GitDocument ReadDocument(string path)
    {
        if (!fileSystem.FileExists(path))
        {
            if (fileSystem.DirectoryExists(path))
            {
                throw new InvalidOperationException("The Git config target is a directory.");
            }
            return GitDocument.Missing();
        }

        return GitDocument.Parse(fileSystem.ReadAllBytes(path));
    }

    private string Apply(
        GitDocument document,
        ConfigurationPhysicalTargetWriterRequest request,
        bool mutate
    )
    {
        GitDocument working = mutate ? document : document.Clone();
        foreach (ConfigurationChange change in request.Changes)
        {
            string key = CanonicalizeSupportedConfigurationKey(change.Key);
            string? existing = working.GetValue(key);
            bool remove =
                request.PlanOperation == ConfigurationPlanOperation.Remove
                || change.Operation == ConfigurationChangeOperation.Remove;
            if (remove)
            {
                if (!request.IsOwned(change, fileSystem))
                {
                    throw new InvalidOperationException(
                        "Git configuration removal requires recognized ownership."
                    );
                }
                working.Remove(key);
                continue;
            }

            if (existing is not null && !request.IsOwned(change, fileSystem))
            {
                throw new InvalidOperationException(
                    "The Git configuration selector already exists without recognized ownership."
                );
            }

            working.Set(key, RenderValue(key, change.Value));
        }

        return working.Render();
    }

    private static string? RenderValue(string key, string? value) =>
        value is null ? null
        : string.Equals(
            CanonicalizeSupportedConfigurationKey(key),
            HelperKey,
            StringComparison.Ordinal
        )
            ? RenderCredentialHelperCommandValue(value)
        : value.ToLowerInvariant();

    private sealed class GitDocument
    {
        private readonly List<string> lines;
        private readonly bool hadBom;
        private readonly string newLine;
        private readonly bool trailingNewLine;

        private GitDocument(List<string> lines, bool hadBom, string newLine, bool trailingNewLine)
        {
            this.lines = lines;
            this.hadBom = hadBom;
            this.newLine = newLine;
            this.trailingNewLine = trailingNewLine;
        }

        public string Text => Render();

        public static GitDocument Missing() =>
            new([], hadBom: false, "\n", trailingNewLine: true);

        public static GitDocument Parse(byte[] bytes)
        {
            bool bom = bytes is [0xEF, 0xBB, 0xBF, ..];
            string text = Utf8NoBom.GetString(bom ? bytes[3..] : bytes);
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            bool trailing = text.EndsWith('\n');
            return new GitDocument(SplitLines(text), bom, newline, trailing);
        }

        public GitDocument Clone() => new([.. lines], hadBom, newLine, trailingNewLine);

        public string? GetValue(string canonicalKey)
        {
            (string section, string? subsection, string variable) = ParseKey(canonicalKey);
            string? currentSection = null;
            string? currentSubsection = null;
            string? value = null;
            var matches = 0;
            foreach (string line in lines)
            {
                if (TryParseSection(line, out string? parsedSection, out string? parsedSubsection))
                {
                    currentSection = parsedSection;
                    currentSubsection = parsedSubsection;
                    continue;
                }

                if (
                    string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        currentSubsection,
                        subsection,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && TryParseVariable(line, out string? parsedVariable, out string? parsedValue)
                    && string.Equals(parsedVariable, variable, StringComparison.OrdinalIgnoreCase)
                )
                {
                    value = Unquote(parsedValue!);
                    matches++;
                }
            }

            return matches switch
            {
                0 => null,
                1 => value,
                _ => throw new InvalidOperationException(
                    "The managed Git configuration selector is declared more than once."
                ),
            };
        }

        public void Set(string canonicalKey, string? value)
        {
            (string section, string? subsection, string variable) = ParseKey(canonicalKey);
            int existing = FindVariableLine(section, subsection, variable);
            string rendered = "\t" + variable + " = " + Quote(value ?? string.Empty);
            if (existing >= 0)
            {
                lines[existing] = rendered;
                return;
            }

            int sectionLine = FindSectionLine(section, subsection);
            if (sectionLine < 0)
            {
                lines.Add(RenderSection(section, subsection));
                lines.Add(rendered);
                return;
            }

            int insert = sectionLine + 1;
            while (insert < lines.Count && !TryParseSection(lines[insert], out _, out _))
            {
                insert++;
            }
            lines.Insert(insert, rendered);
        }

        public void Remove(string canonicalKey)
        {
            (string section, string? subsection, string variable) = ParseKey(canonicalKey);
            int index = FindVariableLine(section, subsection, variable);
            if (index >= 0)
            {
                lines.RemoveAt(index);
            }
        }

        public string Render()
        {
            string text = string.Join(newLine, lines);
            return trailingNewLine && lines.Count > 0 ? text + newLine : text;
        }

        public byte[] Encode(string value)
        {
            byte[] contents = Utf8NoBom.GetBytes(value);
            return hadBom ? [0xEF, 0xBB, 0xBF, .. contents] : contents;
        }

        private int FindSectionLine(string section, string? subsection)
        {
            for (var index = 0; index < lines.Count; index++)
            {
                if (
                    TryParseSection(lines[index], out string? parsed, out string? parsedSubsection)
                    && string.Equals(parsed, section, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        parsedSubsection,
                        subsection,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return index;
                }
            }
            return -1;
        }

        private int FindVariableLine(string section, string? subsection, string variable)
        {
            string? currentSection = null;
            string? currentSubsection = null;
            var matchingLine = -1;
            for (var index = 0; index < lines.Count; index++)
            {
                if (
                    TryParseSection(
                        lines[index],
                        out string? parsedSection,
                        out string? parsedSubsection
                    )
                )
                {
                    currentSection = parsedSection;
                    currentSubsection = parsedSubsection;
                    continue;
                }

                if (
                    !string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        currentSubsection,
                        subsection,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || !TryParseVariable(lines[index], out string? parsedVariable, out _)
                    || !string.Equals(parsedVariable, variable, StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                if (matchingLine >= 0)
                {
                    throw new InvalidOperationException(
                        "The managed Git configuration selector is declared more than once."
                    );
                }

                matchingLine = index;
            }

            return matchingLine;
        }

        private static (string Section, string? Subsection, string Variable) ParseKey(
            string canonicalKey
        ) =>
            canonicalKey == HelperKey
                ? ("credential", null, "helper")
                : ("credential", "https://dev.azure.com", "useHttpPath");

        private static bool TryParseSection(
            string line,
            out string? section,
            out string? subsection
        )
        {
            section = null;
            subsection = null;
            string trimmed = line.Trim();
            if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
            {
                return false;
            }

            string body = trimmed[1..^1].Trim();
            int quote = body.IndexOf('"');
            if (quote < 0)
            {
                section = body;
                return true;
            }

            section = body[..quote].Trim();
            subsection = body[(quote + 1)..].TrimEnd('"').Trim();
            return true;
        }

        private static bool TryParseVariable(string line, out string? variable, out string? value)
        {
            variable = null;
            value = null;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                return false;
            }

            int equals = trimmed.IndexOf('=');
            if (equals < 0)
            {
                variable = trimmed;
                value = "true";
                return true;
            }

            variable = trimmed[..equals].Trim();
            value = trimmed[(equals + 1)..].Trim();
            return variable.Length > 0;
        }

        private static string RenderSection(string section, string? subsection) =>
            subsection is null
                ? "[" + section + "]"
                : "["
                    + section
                    + " \""
                    + subsection.Replace("\"", "\\\"", StringComparison.Ordinal)
                    + "\"]";

        private static string Quote(string value) =>
            "\""
            + value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
            + "\"";

        private static string Unquote(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            {
                return trimmed[1..^1]
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal);
            }
            return trimmed;
        }

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
}
