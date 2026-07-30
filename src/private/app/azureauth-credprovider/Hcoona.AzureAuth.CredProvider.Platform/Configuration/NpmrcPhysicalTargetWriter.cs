using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal sealed class NpmrcPhysicalTargetWriter(IFileSystem fileSystem)
    : IConfigurationPhysicalTargetWriter
{
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
        NpmrcDocument document = ReadDocument(GetTargetPath(request));
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
        NpmrcDocument document = ReadDocument(targetPath);
        string original = document.Text;
        string updated = Apply(document, request, mutate: true);
        bool containsSecret = request.Changes.Any(change => change.IsSecretValue);
        if (string.Equals(original, updated, StringComparison.Ordinal))
        {
            if (
                containsSecret
                && fileSystem.FileExists(targetPath)
                && !OperatingSystem.IsWindows()
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
        NpmrcDocument document = ReadDocument(GetTargetPath(request));
        return request.Changes.All(change =>
        {
            string? value = document.GetValue(change.Key);
            return change.IsSecretValue
                ? value is not null
                : string.Equals(value, change.Value, StringComparison.Ordinal);
        });
    }

    internal static string? GetPlanningValidationViolation(
        ConfigurationChange change,
        CanonicalResourceIdentity? resourceIdentity = null
    )
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.TargetKind != ConfigurationTargetKind.Npmrc)
        {
            return null;
        }

        if (
            string.IsNullOrWhiteSpace(change.Key)
            || change.Key.Contains('\r')
            || change.Key.Contains('\n')
            || change.Key.Contains('=')
            || change.Key.StartsWith('#')
            || change.Key.StartsWith(';')
        )
        {
            return "The npmrc writer requires a valid single-line key.";
        }

        bool writesValue =
            change.Operation
            is ConfigurationChangeOperation.Set
                or ConfigurationChangeOperation.Create
                or ConfigurationChangeOperation.Update
                or ConfigurationChangeOperation.Refresh;
        if (
            writesValue
            && (change.Value is null || change.Value.Contains('\r') || change.Value.Contains('\n'))
        )
        {
            return "The npmrc writer requires a single-line value.";
        }

        bool authToken = IsAuthTokenKey(change.Key);
        if (writesValue && authToken != change.IsSecretValue)
        {
            return authToken
                ? "npm auth token values must be marked as secret."
                : "npmrc secret values are supported only for auth token selectors.";
        }

        if (writesValue && authToken)
        {
            if (
                resourceIdentity is null
                || !CanonicalResourceIdentityPolicy.IsServiceEndpointCompatibleWithEcosystem(
                    resourceIdentity.ServiceEndpoint,
                    CredentialEcosystem.Npm
                )
                || !string.Equals(
                    NpmCompatibleAuthSelectorPolicy.Create(resourceIdentity).NpmAuthTokenKey,
                    change.Key,
                    StringComparison.Ordinal
                )
            )
            {
                return "The npm auth token selector must match the canonical registry identity.";
            }
        }

        return change.Operation switch
        {
            ConfigurationChangeOperation.Set
            or ConfigurationChangeOperation.Create
            or ConfigurationChangeOperation.Update
            or ConfigurationChangeOperation.Refresh
            or ConfigurationChangeOperation.Remove => null,
            _ => "The npmrc writer supports value-writing and remove operations only.",
        };
    }

    private void ValidateRequest(ConfigurationPhysicalTargetWriterRequest request)
    {
        if (request.TargetKind != ConfigurationTargetKind.Npmrc)
        {
            throw new NotSupportedException("The npmrc writer received another target kind.");
        }

        _ = GetTargetPath(request);
        if (
            request
                .Changes.GroupBy(change => change.Key, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            throw new InvalidOperationException(
                "An npmrc request contains duplicate managed selectors."
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
                    throw new ArgumentException("The npmrc target path must be fully qualified.");
                }
                return fileSystem.GetFullPath(change.TargetPathOrName);
            })
            .Distinct(
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal
            )
            .ToArray();
        return paths.Length == 1
            ? paths[0]
            : throw new NotSupportedException("An npmrc request must target one file.");
    }

    private NpmrcDocument ReadDocument(string targetPath)
    {
        if (!fileSystem.FileExists(targetPath))
        {
            if (fileSystem.DirectoryExists(targetPath))
            {
                throw new InvalidOperationException("The npmrc target is a directory.");
            }
            return NpmrcDocument.Missing();
        }

        return NpmrcDocument.Parse(fileSystem.ReadAllBytes(targetPath));
    }

    private static string Apply(
        NpmrcDocument document,
        ConfigurationPhysicalTargetWriterRequest request,
        bool mutate
    )
    {
        NpmrcDocument working = mutate ? document : document.Clone();
        foreach (ConfigurationChange change in request.Changes)
        {
            string? existing = working.GetValue(change.Key);
            bool remove =
                request.PlanOperation == ConfigurationPlanOperation.Remove
                || change.Operation == ConfigurationChangeOperation.Remove;
            if (remove)
            {
                if (!request.IsOwned(change))
                {
                    throw new InvalidOperationException(
                        "npmrc removal requires a recognized owned selector."
                    );
                }
                working.Remove(change.Key);
                continue;
            }

            if (existing is not null && !request.IsOwned(change))
            {
                throw new InvalidOperationException(
                    "The npmrc selector already exists without recognized ownership."
                );
            }

            working.Set(change.Key, change.Value!);
        }

        return working.Render();
    }

    private static bool IsAuthTokenKey(string key) =>
        string.Equals(key, "_authToken", StringComparison.Ordinal)
        || key.EndsWith(":_authToken", StringComparison.Ordinal);

    private sealed class NpmrcDocument
    {
        private readonly List<string> lines;
        private readonly bool hadBom;
        private readonly string newLine;
        private readonly bool trailingNewLine;

        private NpmrcDocument(List<string> lines, bool hadBom, string newLine, bool trailingNewLine)
        {
            this.lines = lines;
            this.hadBom = hadBom;
            this.newLine = newLine;
            this.trailingNewLine = trailingNewLine;
        }

        public string Text => Render();

        public static NpmrcDocument Missing() =>
            new([], hadBom: false, Environment.NewLine, trailingNewLine: true);

        public static NpmrcDocument Parse(byte[] bytes)
        {
            bool bom = bytes is [0xEF, 0xBB, 0xBF, ..];
            string text = Utf8NoBom.GetString(bom ? bytes[3..] : bytes);
            return new NpmrcDocument(
                SplitLines(text),
                bom,
                text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n",
                text.EndsWith('\n')
            );
        }

        public NpmrcDocument Clone() => new([.. lines], hadBom, newLine, trailingNewLine);

        public string? GetValue(string key)
        {
            NpmrcEntry[] entries = Find(key).ToArray();
            return entries.Length switch
            {
                0 => null,
                1 => entries[0].Value,
                _ => throw new InvalidOperationException(
                    "The managed npmrc selector is declared more than once."
                ),
            };
        }

        public void Set(string key, string value)
        {
            NpmrcEntry[] entries = Find(key).ToArray();
            if (entries.Length > 1)
            {
                throw new InvalidOperationException(
                    "The managed npmrc selector is declared more than once."
                );
            }

            string rendered = key + "=" + EscapeValue(value);
            if (entries.Length == 1)
            {
                lines[entries[0].Index] = rendered;
            }
            else
            {
                lines.Add(rendered);
            }
        }

        public void Remove(string key)
        {
            NpmrcEntry[] entries = Find(key).ToArray();
            if (entries.Length > 1)
            {
                throw new InvalidOperationException(
                    "The managed npmrc selector is declared more than once."
                );
            }
            if (entries.Length == 1)
            {
                lines.RemoveAt(entries[0].Index);
            }
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

        private IEnumerable<NpmrcEntry> Find(string key)
        {
            for (var index = 0; index < lines.Count; index++)
            {
                if (
                    TryParseEntry(lines[index], out string? parsedKey, out string? value)
                    && string.Equals(parsedKey, key, StringComparison.Ordinal)
                )
                {
                    yield return new NpmrcEntry(index, value);
                }
            }
        }

        private static bool TryParseEntry(string line, out string? key, out string value)
        {
            key = null;
            value = string.Empty;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                return false;
            }

            int equals = trimmed.IndexOf('=');
            if (equals <= 0)
            {
                return false;
            }

            key = trimmed[..equals].Trim();
            value = UnescapeValue(trimmed[(equals + 1)..].Trim());
            return key.Length > 0;
        }

        private static string EscapeValue(string value) =>
            value.Replace("\\", "\\\\", StringComparison.Ordinal);

        private static string UnescapeValue(string value) =>
            value.Replace("\\\\", "\\", StringComparison.Ordinal);

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

        private sealed record NpmrcEntry(int Index, string Value);
    }
}
