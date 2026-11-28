using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetRevisionInputs(GitRevision Revision, DotNetGraphResponse Graph,
    DotNetBoundNode[] Nodes, DotNetBoundInput[] Inputs, NbgvInputs[] Versions,
    IReadOnlyDictionary<string, QualitySelection?> Quality, SharedInput[] SelectionInputs,
    ReleaseUnitDeclaration[] Units);

// Compose the single-endpoint native facts with the application's committed declarations.
internal static class DotNetSelectionInputs
{
    internal static async Task<DotNetRevisionInputs> ReadAsync(this DotNetRepositoryReader reader,
        CancellationToken token) => await WithDeclarationsAsync(reader,
            await reader.ReadEndpointAsync(token), token);

    internal static Task<DotNetRevisionInputs> BindAsync(this DotNetRepositoryReader reader,
        DotNetGraphResponse graph, CancellationToken token) =>
        WithDeclarationsAsync(reader, reader.BindEndpoint(graph, token), token);

    private static async Task<DotNetRevisionInputs> WithDeclarationsAsync(
        DotNetRepositoryReader reader,
        DotNetEndpointFacts native, CancellationToken token)
    {
        GitMaterialization checkout = reader.Checkout;
        var selections = new QualitySelectionReader(checkout.Root, checkout.ReadOptionalTextAsync);
        var quality = new Dictionary<string, QualitySelection?>(StringComparer.Ordinal);
        var shared = native.Versions.SelectMany(scope =>
            scope.Paths.Select(path => new SharedInput(path, scope.Consumers))).ToList();
        foreach (var group in native.Nodes.Select(node => node.Project)
                     .Where(project => project != "dirs.proj").Distinct(StringComparer.Ordinal)
                     .GroupBy(DotNetRepositoryReader.Parent))
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
                directory = DotNetRepositoryReader.Parent(directory);
            }
        }
        var units = new List<ReleaseUnitDeclaration>();
        foreach (GitEntry entry in native.Revision.Entries.Where(entry =>
                     entry.Path == "workflow-delivery.release-unit.yml" ||
                     entry.Path.EndsWith(
                         "/workflow-delivery.release-unit.yml", StringComparison.Ordinal)))
        {
            reader.RequireFile(Path.Combine(checkout.Root, entry.Path), "ReleaseUnit");
            units.Add(ReleaseUnitDeclarationReader.Read(
                await checkout.ReadOptionalTextAsync(entry.Path, token) ??
                    throw new InvalidDataException(
                        "Missing committed release declaration: " + entry.Path),
                entry.Path));
        }
        return new(native.Revision, native.Graph, native.Nodes, native.Inputs,
            [.. native.Versions.Select(scope => scope.Version)], quality, [.. shared], [.. units]);
    }
}
