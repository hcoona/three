using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetRevisionInputs(GitRevision Revision, DotNetGraphResponse Graph,
    DotNetBoundNode[] Nodes, DotNetBoundInput[] Inputs, NbgvInputs[] Versions,
    IReadOnlyDictionary<string, QualitySelection?> Quality, SharedInput[] SelectionInputs,
    ReleaseUnitDeclaration[] Units);

// Ordinary repository policy adds quality, release and product version consumers.
internal sealed class DotNetRepositoryReader
{
    private readonly GitMaterialization checkout;
    private readonly DotNetNativeGraphReader reader;

    internal DotNetRepositoryReader(GitMaterialization checkout, DotNetReadFiles files,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>>? query = null)
    {
        this.checkout = checkout;
        reader = new(checkout, files, query);
    }

    internal async Task<DotNetRevisionInputs> ReadAsync(CancellationToken token) =>
        await CompleteAsync(await reader.ReadAsync("dirs.proj", new(StringComparer.Ordinal)
        {
            ["Configuration"] = "Debug",
            ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true",
        }, token), token);

    internal Task<DotNetRevisionInputs> BindAsync(DotNetGraphResponse graph,
        CancellationToken token) => CompleteAsync(reader.Bind(graph, "dirs.proj", token), token);

    private async Task<DotNetRevisionInputs> CompleteAsync(DotNetBoundGraph native,
        CancellationToken token)
    {
        DotNetGraphResponse graph = native.Graph;
        string[] projects = native.Nodes.Where(node => node.Project != "dirs.proj")
            .Select(node => node.Project).Distinct(StringComparer.Ordinal).ToArray();
        var quality = new Dictionary<string, QualitySelection?>(StringComparer.Ordinal);
        var shared = new List<SharedInput>();
        var versions = new List<NbgvInputs>();
        var selections = new QualitySelectionReader(checkout.Root,
            checkout.ReadOptionalTextAsync);
        foreach (var group in native.Nodes.Where(node => node.Project != "dirs.proj")
                     .GroupBy(reader.VersionDirectory))
        {
            token.ThrowIfCancellationRequested();
            string[] consumers = group.Select(node => node.Project)
                .Distinct(StringComparer.Ordinal).ToArray();
            NbgvInputs version = new NbgvInputReader(checkout.Root).Read(checkout.Revision,
                group.Key, token);
            if (version.Commit != checkout.Revision.Commit || version.Directory != group.Key)
                throw Unavailable("NBGV", "Different native version subject.");
            versions.Add(version);
            foreach (string path in version.Paths)
                shared.Add(new(reader.RequireFile(Path.Combine(checkout.Root, path), "NBGV"),
                    consumers));
            foreach (string path in version.ConfigurationCandidates)
                shared.Add(new(reader.BindCandidate(path, "NBGV"), consumers));
        }
        foreach (IGrouping<string, string> group in projects.GroupBy(Parent))
        {
            token.ThrowIfCancellationRequested();
            string[] consumers = group.ToArray();
            QualitySelection? selection = await selections.ReadAsync(group.Key, "dotnet", token);
            foreach (string project in consumers) quality.Add(project, selection);
            string directory = group.Key;
            while (true)
            {
                string path = directory == "." ? "workflow-delivery.quality.yml" :
                    directory + "/workflow-delivery.quality.yml";
                shared.Add(new(reader.BindCandidate(path, "QualitySelection"), consumers));
                if (path == selection?.SourcePath || directory == ".") break;
                directory = Parent(directory);
            }
        }
        var units = new List<ReleaseUnitDeclaration>();
        foreach (GitEntry entry in checkout.Revision.Entries.Where(entry =>
                     entry.Path == "workflow-delivery.release-unit.yml" ||
                     entry.Path.EndsWith("/workflow-delivery.release-unit.yml",
                         StringComparison.Ordinal)))
        {
            reader.RequireFile(Path.Combine(checkout.Root, entry.Path), "ReleaseUnit");
            units.Add(ReleaseUnitDeclarationReader.Read(
                await checkout.ReadOptionalTextAsync(entry.Path, token) ??
                    throw Unavailable("ReleaseUnit", "Missing committed release declaration."),
                entry.Path));
        }
        return new(checkout.Revision, graph, native.Nodes, native.Inputs,
            versions.ToArray(), quality, shared.ToArray(), units.ToArray());

    }

    internal static string Parent(string path) => DotNetNativeGraphReader.Parent(path);

    private InvalidDataException Unavailable(string role, string reason,
        DotNetNodeIdentity? consumer = null) => new($"Native .NET fact unavailable at " +
        $"revision {checkout.Revision.Commit}, endpoint {checkout.Root}, " +
        $"consumer {System.Text.Json.JsonSerializer.Serialize(consumer,
            TransferJson.Default.DotNetNodeIdentity)}, role {role}: {reason}");
}
