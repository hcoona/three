using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonGraphRequest(string Root, string Uv, string Interpreter,
    string HelperPython, string PassiveScript, string SupplementExecutable, string Cache,
    string BootstrapScratch, string ActivityScratch, PythonGroupOperation[] Operations);
internal sealed record PythonNativeGraph(PythonMetadata Metadata, string Interpreter,
    PythonPassiveInputs[] Passive, PythonGroupOperation[] Operations,
    PythonProjectDependencies[] Projects, string[] ConfigurationInputs);

// Compose native answers; preparation, isolation and cleanup belong to the caller.
internal sealed class PythonNativeGraphReader
{
    private readonly PythonMetadataReader metadataReader;
    private readonly PythonPassiveReader passiveReader;
    private readonly PythonNativeSupplementReader supplementReader;

    internal PythonNativeGraphReader() : this(NativeProcess.ExecuteAsync) { }

    internal PythonNativeGraphReader(
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute)
    {
        metadataReader = new(execute);
        passiveReader = new(execute);
        supplementReader = new(execute);
    }

    internal async Task<PythonNativeGraph> ReadAsync(PythonGraphRequest request,
        IReadOnlyDictionary<string, string?> environment, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Path.GetFullPath(request.BootstrapScratch) ==
            Path.GetFullPath(request.ActivityScratch))
            throw new InvalidDataException("Python graph requests require distinct scratch paths.");
        PythonMetadata metadata = await metadataReader.ReadAsync(request.Root, request.Uv,
            request.Interpreter, environment, token);
        var passive = new List<PythonPassiveInputs>();
        foreach (string directory in metadata.Members.Select(member =>
            Path.GetFullPath(Path.Combine(metadata.Root, member.Directory))).Append(metadata.Root)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            passive.Add(await passiveReader.ReadAsync(directory, request.HelperPython,
                request.PassiveScript, environment, token));
        }
        PythonPassiveProject[] projects = passive.Select(input => input.Project).ToArray();
        token.ThrowIfCancellationRequested();
        PythonSupplementResult bootstrap = await supplementReader.ReadAsync(new(metadata.Root,
            request.Interpreter, request.Cache, projects, [], [], []), request.HelperPython,
            request.PassiveScript, request.SupplementExecutable, request.BootstrapScratch,
            environment, token);
        PythonBuildAssociation[] builds = PythonDependencyProjection.ReadBuildAssociations(
            metadata, bootstrap, projects);
        var activities = new PythonPlanningActivities(metadata, builds, request.Interpreter,
            request.Cache, projects, request.Operations);
        token.ThrowIfCancellationRequested();
        PythonSupplementResult result = await supplementReader.ReadAsync(activities.Request,
            request.HelperPython, request.PassiveScript, request.SupplementExecutable,
            request.ActivityScratch, environment, token);
        PythonProjectDependencies[] graph = activities.Project(result);
        token.ThrowIfCancellationRequested();
        return new(metadata, request.Interpreter, passive.ToArray(), request.Operations, graph,
            bootstrap.ConfigurationInputs.Concat(result.ConfigurationInputs)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
