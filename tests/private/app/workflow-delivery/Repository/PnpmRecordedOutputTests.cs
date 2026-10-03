using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PnpmRecordedOutputTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("base", "dist output", 27, true)]
    [DataRow("candidate", "build output", 26, false)]
    public async Task ReadRecordedNativeIdentitiesPreservesRelationsAcrossRevisions(
        string revision, string publishDirectory, int projectCount, bool topConsumesPublisher)
    {
        using Stream resource = typeof(PnpmRecordedOutputTests).Assembly.GetManifestResourceStream(
            $"WorkflowDelivery.Tests.Repository.fixtures.{revision}.json")!;
        using var text = new StreamReader(resource);
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "recorded pnpm workspace"));
        string escapedRoot = JsonSerializer.Serialize(root)[1..^1];
        using JsonDocument fixture = JsonDocument.Parse((await text.ReadToEndAsync(
            TestContext.CancellationToken))
            .Replace("__WORKSPACE__", escapedRoot, StringComparison.Ordinal));
        JsonElement recorded = fixture.RootElement;
        var reader = new PnpmGraphReader(root, Query);

        PnpmGraph graph = await reader.ReadAsync(TestContext.CancellationToken);

        Assert.HasCount(projectCount, graph.Projects);
        Assert.AreEqual("packages/publisher/" + publishDirectory,
            graph.Projects.Single(p => p.Directory == "packages/publisher").PublishDirectory);
        foreach (string consumer in new[] { "publish-link", "publish-workspace", "publish-alias" })
            Assert.Contains("packages/publisher", graph.Projects.Single(
                p => p.Directory == "packages/" + consumer).Dependencies);
        foreach (string consumer in new[] { "peer-consumer-one", "peer-consumer-two" })
            Assert.Contains("packages/peer-lib", graph.Projects.Single(
                p => p.Directory == "packages/" + consumer).Dependencies);
        Assert.AreEqual(topConsumesPublisher, graph.Projects.Single(
            p => p.Directory == "packages/top").Dependencies.Contains("packages/publisher"));
        Assert.AreEqual(topConsumesPublisher, graph.Projects.Any(p => p.Directory ==
            "packages/deleted"));
        Assert.IsEmpty(graph.Projects.Single(p => p.Directory ==
            "packages/unrelated").Dependencies);
        Assert.DoesNotContain("packages/publisher", graph.Projects.Single(
            p => p.Directory == "packages/tarball-consumer").Dependencies);
        PnpmLocalInput archive = graph.LocalInputs.Single(p => p.Path == "fixtures/publisher.tgz");
        Assert.IsFalse(archive.IsDirectory);
        Assert.AreEqual("packages/tarball-consumer", Assert.ContainsSingle(archive.Consumers));
        Assert.IsTrue(graph.LocalInputs.Single(p => p.Path == "fixtures/peer-one").IsDirectory);

        Task<string> Query(string[] arguments, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // Unrecorded settings, metadata and selectors use controlled substitutes.
            if (arguments[0] == "config") return Task.FromResult("null");
            if (arguments[0] == "--dir")
            {
                string identity = Path.GetRelativePath(root, arguments[1])
                    .Replace(Path.DirectorySeparatorChar, '/');
                return Task.FromResult(recorded.GetProperty("publishMetadata")
                    .TryGetProperty(identity, out JsonElement metadata)
                    ? metadata.GetString()! : "");
            }
            if (arguments.Contains("--filter"))
            {
                string selector = arguments[Array.IndexOf(arguments, "--filter") + 1];
                bool isClosure = selector.EndsWith("...", StringComparison.Ordinal);
                string identity = isClosure ? selector[3..^4] : selector[3..^1];
                if (isClosure && recorded.GetProperty("closures").TryGetProperty(identity,
                    out JsonElement closure))
                    return Task.FromResult(closure.GetRawText());
                JsonElement project = recorded.GetProperty("listing").EnumerateArray().Single(p =>
                    Path.GetRelativePath(root, p.GetProperty("path").GetString()!)
                        .Replace(Path.DirectorySeparatorChar, '/') == identity);
                return Task.FromResult("[" + project.GetRawText() + "]");
            }
            return Task.FromResult(recorded.GetProperty("listing").GetRawText());
        }
    }
}
