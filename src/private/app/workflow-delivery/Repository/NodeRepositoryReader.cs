using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed class NodeRepositoryReader
{
    private const string Fixture = "src/public/lib/hcoona-release-smoke-npm";
    private const string Hexo = "src/public/lib/hexo-renderer-asciidoc";
    private const string Steam = "src/public/lib/steam-account-history-to-csv";
    private readonly GitMaterialization checkout;
    private readonly Func<string[], CancellationToken, Task<string>> query;

    internal NodeRepositoryReader(GitMaterialization checkout)
        : this(checkout, (arguments, token) => NativeProcess.RunAsync("pnpm", checkout.Root,
            arguments, token))
    { }

    internal NodeRepositoryReader(GitMaterialization checkout,
        Func<string[], CancellationToken, Task<string>> query)
    {
        this.checkout = checkout;
        this.query = query;
    }

    internal async Task<NodeRevisionInputs> ReadAsync(CancellationToken token)
    {
        PnpmGraph graph = await new PnpmGraphReader(checkout.Root, query).ReadAsync(token);
        string[] directories = graph.Projects.Where(project => project.Directory != ".")
            .Select(project => project.Directory).ToArray();
        var quality = new Dictionary<string, QualitySelection?>(StringComparer.Ordinal);
        var selections = new QualitySelectionReader(checkout.Root, checkout.ReadOptionalTextAsync);
        var versions = new List<NbgvInputs>();
        var nbgv = new NbgvInputReader(checkout.Root);
        var committed = checkout.Revision.Entries.ToDictionary(entry => entry.Path,
            StringComparer.Ordinal);
        foreach (string directory in directories)
        {
            // These are input contracts for existing opaque scripts, not membership
            // declarations. PNPM still supplies the complete actual project roster.
            if (directory is not (Fixture or Hexo or Steam or
                "src/private/app/im-acp-gateway/poc/telegram-bot-verifier" or
                "src/private/app/im-acp-gateway/poc/telegram-topic-session-bridge" or
                "src/private/app/im-acp-gateway/poc/wechat-ilink-verifier"))
                throw new InvalidDataException("Unresolved Node script input scope: " + directory);
            quality.Add(directory, await selections.ReadAsync(directory, "node", token));
            // The current versioned products declare native version files. Unversioned
            // tooling membership remains complete without fabricating an NBGV answer.
            if (committed.ContainsKey(directory + "/version.json") ||
                committed.ContainsKey(directory + "/version.txt") ||
                directory is Fixture or Hexo or Steam)
            {
                NbgvInputs version = nbgv.Read(checkout.Revision, directory, token);
                foreach (string path in version.ConfigurationCandidates)
                    if (!committed.ContainsKey(path) &&
                        File.Exists(Path.Combine(checkout.Root, path)))
                        throw new InvalidDataException("Uncommitted native version configuration: "
                            + path);
                versions.Add(version);
            }
        }
        var units = new List<ReleaseUnitDeclaration>();
        foreach (GitEntry entry in checkout.Revision.Entries.Where(entry =>
            entry.Path == "workflow-delivery.release-unit.yml" ||
            entry.Path.EndsWith("/workflow-delivery.release-unit.yml", StringComparison.Ordinal)))
            units.Add(ReleaseUnitDeclarationReader.Read(
                (await checkout.ReadOptionalTextAsync(entry.Path, token))!, entry.Path));

        var shared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string path in new[] { "package.json", "pnpm-workspace.yaml", "pnpm-lock.yaml",
            "mise.toml", "mise.lock" })
        {
            RequireFile(path);
            Add(path, directories);
        }
        foreach (string directory in directories)
        {
            string ancestor = directory;
            while (true)
            {
                foreach (string file in new[] { ".npmrc", ".pnpmfile.cjs", "pnpmfile.cjs" })
                {
                    string path = ancestor.Length == 0 ? file : ancestor + "/" + file;
                    // Unknown hooks can read arbitrary external inputs; the current
                    // contract has none. Do not silently call that bounded coverage.
                    if (File.Exists(Path.Combine(checkout.Root, path)))
                        throw new InvalidDataException("Unsupported additional Node configuration: "
                            + path);
                    Add(path, [directory]);
                }
                if (ancestor.Length == 0)
                    break;
                ancestor = NodePackageChecks.Parent(ancestor);
            }
        }
        // Native pack injects matching regular root licenses. This recognizes an input
        // role; PNPM still decides the actual package contents and transformations.
        foreach (GitEntry entry in checkout.Revision.Entries.Where(entry =>
            !entry.Path.Contains('/') && IsLicense(entry.Path) &&
            entry.ObjectType == "blob" && entry.Mode is "100644" or "100755"))
            Add(entry.Path, directories.Where(directory => directory == Fixture).ToArray());
        foreach (string path in new[] { "LICENSE", "COPYING", "COPYING.LESSER",
            "LICENSES/LGPL-3.0-linking-exception.txt" })
            Add(path, directories.Where(directory => directory == Hexo).ToArray());
        foreach (string path in new[] { "COPYING", "COPYING.LESSER" })
            Add(path, directories.Where(directory => directory == Steam).ToArray());
        foreach (GitEntry entry in checkout.Revision.Entries.Where(entry =>
            entry.Path.StartsWith("LICENSES/", StringComparison.Ordinal) &&
            !entry.Path["LICENSES/".Length..].Contains('/') &&
            entry.ObjectType == "blob" && entry.Mode is "100644" or "100755"))
            Add(entry.Path, directories.Where(directory => directory == Steam).ToArray());
        string[] biomeConsumers = directories.Where(directory =>
            directory is not (Fixture or Steam)).ToArray();
        foreach (string path in new[] { "biome.jsonc", ".gitignore", ".ignore" })
            Add(path, biomeConsumers);
        Add(".editorconfig", directories.Where(directory => directory == Hexo).ToArray());
        return new(checkout.Revision, graph, [.. versions], quality, [.. units],
            shared.OrderBy(input => input.Key, StringComparer.Ordinal)
                .Select(input => new SharedInput(input.Key,
                    input.Value.Order(StringComparer.Ordinal).ToArray())).ToArray());

        void RequireFile(string path)
        {
            if (!committed.TryGetValue(path, out GitEntry? entry) ||
                entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
                throw new InvalidDataException("Missing committed Node setup input: " + path);
        }

        void Add(string path, string[] consumers)
        {
            if (consumers.Length == 0)
                return;
            if (!shared.TryGetValue(path, out HashSet<string>? values))
                shared.Add(path, values = new(StringComparer.Ordinal));
            values.UnionWith(consumers);
        }
    }

    internal Task<PnpmScripts> ScriptsAsync(PnpmProject project, CancellationToken token) =>
        new PnpmScriptReader(checkout.Root, query).ReadAsync(project, token);

    internal Task<string> NpmVersionAsync(PnpmProject project, CancellationToken token) =>
        Task.FromResult(new NbgvInputReader(checkout.Root).NpmVersion(checkout.Revision,
            project.Directory, token));

    private static bool IsLicense(string path)
    {
        string name = path.ToLowerInvariant();
        return name is "license" or "licence" ||
            name.StartsWith("license.", StringComparison.Ordinal) ||
            name.StartsWith("licence.", StringComparison.Ordinal);
    }
}
