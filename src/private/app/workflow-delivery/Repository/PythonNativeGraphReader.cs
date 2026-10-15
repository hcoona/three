using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonGraphRequest(string Root, string Uv, string Interpreter,
    string HelperPython, string PassiveScript);
internal sealed record PythonNativeGraph(PythonMetadata Metadata, string Interpreter,
    PythonPassiveInputs[] Passive, PythonGroupOperation[] Operations,
    PythonProjectDependencies[] Projects, string[] ConfigurationInputs);

// Compose native answers; preparation, isolation and cleanup belong to the caller.
internal sealed class PythonNativeGraphReader
{
    private readonly PythonMetadataReader metadataReader;
    private readonly PythonPassiveReader passiveReader;

    internal PythonNativeGraphReader() : this(NativeProcess.ExecuteAsync) { }

    internal PythonNativeGraphReader(
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute)
    {
        async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            NativeCommandResult result = await execute(command, token);
            token.ThrowIfCancellationRequested();
            return result;
        }
        metadataReader = new(ExecuteAsync);
        passiveReader = new(ExecuteAsync);
    }

    internal async Task<PythonNativeGraph> ReadAsync(PythonGraphRequest request,
        Func<PythonMetadata, PythonGroupOperation[]> createOperations,
        IReadOnlyDictionary<string, string?> environment, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        PythonMetadata metadata = await metadataReader.ReadAsync(request.Root, request.Uv,
            request.Interpreter, environment, token);
        token.ThrowIfCancellationRequested();
        PythonGroupOperation[] operations = createOperations(metadata);
        token.ThrowIfCancellationRequested();
        string[] contexts = metadata.Members.Select(member =>
            Path.GetFullPath(Path.Combine(metadata.Root, member.Directory))).Append(metadata.Root)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] configuration = await passiveReader.ReadConfigurationAsync(metadata.Root,
            request.HelperPython, request.PassiveScript, contexts, environment, token);
        var passive = new List<PythonPassiveInputs>();
        foreach (string directory in contexts)
        {
            token.ThrowIfCancellationRequested();
            passive.Add(await passiveReader.ReadAsync(directory, request.HelperPython,
                request.PassiveScript, environment, token));
        }
        token.ThrowIfCancellationRequested();
        PythonBuildAssociation[] builds = PythonDependencyProjection.ReadBuildAssociations(
            metadata, passive.Select(input => input.Project).ToArray());
        PythonProjectDependencies[] graph = PythonDependencyProjection.Project(metadata,
            PythonDependencyProjection.SelectGroups(metadata, operations), builds);
        return new(metadata, request.Interpreter, passive.ToArray(), operations, graph,
            configuration);
    }
}
