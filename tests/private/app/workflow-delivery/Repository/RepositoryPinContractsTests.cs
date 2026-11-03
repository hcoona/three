using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

// Committed pin files must agree wherever the application consumes one fact from two pins.
// Each test reads the root configuration, so a pin change that breaks the agreement fails here.
[TestClass]
public sealed class RepositoryPinContractsTests
{
    private const string Candidate = "cccccccccccccccccccccccccccccccccccccccc";

    // The Ruby preparation installs the nbgv CLI from the tool manifest, while .NET evaluation
    // restores the NBGV package; both must project the same version semantics.
    [TestMethod]
    public void NbgvToolPinEqualsTheNbgvPackagePin()
    {
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepositoryToolSelection.SourceRoot, ".config", "dotnet-tools.json")))!;
        Assert.AreEqual(RepositoryToolSelection.GlobalPackageVersion("Nerdbank.GitVersioning"),
            manifest["tools"]!["nbgv"]!["version"]!.GetValue<string>());
    }

    // Runs the application's runtime rules over the committed ci.yml, release schedule,
    // engines manifests, mise.toml and mise.lock: the planned matrix must carry packaging.
    [TestMethod]
    public void LockedNodeRuntimeHasItsPackagingMatrixEntry()
    {
        string root = RepositoryToolSelection.SourceRoot;
        string node = RepositoryToolSelection.Locked("node");
        NodeRuntimeInputs inputs = NodeRuntimeReader.Read(new(Candidate, []), root,
            File.ReadAllText(Path.Combine(root, NodeRuntimeReader.Workflow)),
            File.ReadAllText(Path.Combine(root, NodeRuntimeReader.Schedule)),
            RepositoryToolSelection.MiseToml, RepositoryToolSelection.MiseLock,
            NodeManifests(root), MiseAnswer(root, "node", node), MiseAnswer(root, "pnpm",
                RepositoryToolSelection.Locked("pnpm")));

        NodeRuntimeVariant packaging = inputs.Variants.Single(variant =>
            variant.Key == inputs.PackagingVariant);
        Assert.AreEqual(node, packaging.ExactVersion);
        Assert.AreEqual(System.Version.Parse(node).Major + ".x", packaging.Selector);
        Assert.AreEqual(System.Version.Parse(inputs.NewestRelease).Major + ".x",
            inputs.Variants[^1].Selector);
        Assert.AreEqual(inputs.LowerBound + ".x", inputs.Variants[0].Selector);
    }

    // Committed engines manifests of the root and every pnpm-workspace.yaml importer.
    internal static NodeEnginesManifest[] NodeManifests(string root)
    {
        var importers = new List<string>();
        foreach (string line in File.ReadLines(Path.Combine(root, "pnpm-workspace.yaml"))
            .SkipWhile(line => line != "packages:").Skip(1).TakeWhile(line =>
                line.StartsWith(' ')))
        {
            string pattern = line.Trim();
            if (!pattern.StartsWith("- ", StringComparison.Ordinal)) continue;
            pattern = pattern[2..];
            importers.AddRange(!pattern.EndsWith("/*", StringComparison.Ordinal) ? [pattern]
                : Directory.GetDirectories(Path.Combine(root, pattern[..^2]))
                    .Where(path => File.Exists(Path.Combine(path, "package.json")))
                    .Select(path => pattern[..^1] + Path.GetFileName(path)));
        }
        return NodeRuntimeReader.ManifestPaths(importers).Select(path => new NodeEnginesManifest(
            path, File.ReadAllText(Path.Combine(root, path)))).ToArray();
    }

    // The shape of `mise ls --json --current --locked <tool>` for one committed selection.
    private static string MiseAnswer(string root, string tool, string version) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                version,
                requested_version = RepositoryToolSelection.Requested(
                    RepositoryToolSelection.MiseToml, tool),
                active = true,
                source = new { type = "mise.toml", path = Path.Combine(root, "mise.toml") },
            },
        });
}
