using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonRepositoryRequest(PythonGraphRequest Graph,
    string PytestHelper, string PackageHelper, PythonPytestOperation Operation);
internal sealed record PythonRevisionInputs(PythonFactsEndpoint Facts,
    PythonNativeGraph Graph, PythonPytestTargetSet Targets);

// Compose prepared endpoint tools and existing readers; native tools retain their semantics.
internal sealed class PythonRepositoryReader
{
    private readonly GitMaterialization checkout;
    private readonly Func<PythonGraphRequest, Func<PythonMetadata, PythonGroupOperation[]>,
        IReadOnlyDictionary<string, string?>, CancellationToken, Task<PythonNativeGraph>> graph;
    private readonly Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute;

    internal PythonRepositoryReader(GitMaterialization checkout) : this(checkout,
        new PythonNativeGraphReader().ReadAsync, NativeProcess.ExecuteAsync) { }

    internal PythonRepositoryReader(GitMaterialization checkout,
        Func<PythonGraphRequest, Func<PythonMetadata, PythonGroupOperation[]>,
            IReadOnlyDictionary<string, string?>, CancellationToken,
            Task<PythonNativeGraph>> graph,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute)
    {
        this.checkout = checkout;
        this.graph = graph;
        this.execute = execute;
    }

    internal async Task<PythonRevisionInputs> ReadAsync(PythonRepositoryRequest request,
        IReadOnlyDictionary<string, string?> environment, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Graph.Root != checkout.Root)
            throw new InvalidDataException("Python collection requires its exact endpoint.");
        PythonNativeGraph native = await graph(request.Graph, Operations, environment, token);
        token.ThrowIfCancellationRequested();
        if (native.Metadata.Root != checkout.Root ||
            native.Interpreter != request.Graph.Interpreter)
            throw new InvalidDataException("Python native graph has a different query context.");
        PythonPytestConfiguration configuration = await new PythonPytestConfigurationReader(
            execute).ReadAsync(checkout.Root, request.Graph.HelperPython, request.PytestHelper,
                environment, token);
        token.ThrowIfCancellationRequested();
        PythonPytestTargetSet targets = PythonPytestTargets.Bind(configuration,
            checkout.Revision, native.Projects);
        var quality = new Dictionary<string, QualitySelection?>(StringComparer.Ordinal);
        var selections = new QualitySelectionReader(checkout.Root, checkout.ReadOptionalTextAsync);
        foreach (PythonProjectDependencies project in native.Projects)
        {
            QualitySelection? selection = await selections.ReadAsync(project.Directory,
                "python", token);
            if (selection is not null && selection.Preset != PythonPackageChecks.Preset)
                throw new InvalidDataException("Unsupported effective Python quality preset.");
            quality.Add(project.Directory, selection);
        }
        var versions = new List<NbgvInputs>();
        foreach (string directory in native.Passive.Where(input => input.Nbgv is not null)
                     .Select(input => PythonMetadataReader.Relative(checkout.Root,
                         Path.GetFullPath(input.Nbgv!.WorkingDirectory)))
                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            versions.Add(new NbgvInputReader(checkout.Root).Read(checkout.Revision, directory,
                token));
        var packages = new List<ProjectFacts>();
        var identities = new Dictionary<string, string>(StringComparer.Ordinal);
        var units = new HashSet<string>(StringComparer.Ordinal);
        foreach (GitEntry entry in checkout.Revision.Entries.Where(entry =>
                     entry.Path == "workflow-delivery.release-unit.yml" ||
                     entry.Path.EndsWith("/workflow-delivery.release-unit.yml",
                         StringComparison.Ordinal)))
        {
            ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
                await checkout.ReadOptionalTextAsync(entry.Path, token) ??
                    throw new InvalidDataException("Missing committed Python release declaration."),
                entry.Path);
            if (!unit.Builds.Any(build => build.Definition == PythonPackageChecks.Build)) continue;
            if (!units.Add(unit.Id) || unit.Builds.Any(build =>
                    build.Definition != PythonPackageChecks.Build))
                throw new InvalidDataException("Unsupported or duplicate Python release unit.");
            string parent = NodePackageChecks.Parent(unit.SourcePath);
            foreach (ReleaseBuild build in unit.Builds)
            {
                string entryPoint = parent.Length == 0 ? build.EntryPoint :
                    parent + "/" + build.EntryPoint;
                string directory = NodePackageChecks.Parent(entryPoint);
                if (directory.Length == 0) directory = ".";
                PythonMetadataMember member = native.Metadata.Members.SingleOrDefault(member =>
                    member.Directory == directory) ??
                    throw new InvalidDataException("Python distribution has no native member.");
                if (!quality.TryGetValue(directory, out QualitySelection? selection) ||
                    selection?.Preset != PythonPackageChecks.Preset)
                    throw new InvalidDataException("Python distribution lacks adopted quality.");
                if (!identities.ContainsKey(directory))
                    identities.Add(directory, await VersionAsync(member, request, environment,
                        token));
            }
            CheckSpec[] checks = PythonPackageChecks.Expand(unit, native.Metadata.Members,
                identities);
            foreach (IGrouping<string, CheckSpec> group in checks.GroupBy(check =>
                         check.Package!.Directory, StringComparer.Ordinal))
            {
                if (packages.Any(package => package.Directory == group.Key))
                    throw new InvalidDataException("Python member has multiple release units.");
                packages.Add(new("python-package:" + group.Key, group.Key, [], [], unit.Id,
                    PythonPackageChecks.Preset, group.ToArray(), [unit.SourcePath]));
            }
        }
        if (!quality.Where(pair => pair.Value is not null).Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal).SetEquals(packages.Select(package =>
                    package.Directory)))
            throw new InvalidDataException("Adopted Python quality lacks a complete distribution.");
        PythonInputBinding[] inputs = [.. PythonInputConsumers.Map(checkout.Revision,
            checkout.Root, native.Projects, native.Passive, versions.ToArray(), quality,
            native.ConfigurationInputs), .. PythonRepositoryInputs.Map(checkout.Revision,
                native.Projects, targets, packages.ToArray())];
        token.ThrowIfCancellationRequested();
        return new(new(checkout.Revision, native.Projects, configuration, request.Operation,
            packages.ToArray(), inputs), native, targets);
    }

    private async Task<string> VersionAsync(PythonMetadataMember member,
        PythonRepositoryRequest request, IReadOnlyDictionary<string, string?> environment,
        CancellationToken token)
    {
        NativeCommandResult result = await execute(new(request.Graph.HelperPython, checkout.Root,
            ["-I", request.PackageHelper, "identity",
                Path.Combine(checkout.Root, member.Directory)],
            30, environment), token);
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded || result.Error is not null)
            throw new InvalidDataException("Native Python distribution identity failed.");
        try
        {
            using JsonDocument identity = JsonDocument.Parse(result.Stdout);
            if (identity.RootElement.GetProperty("name").GetString() != member.Name ||
                identity.RootElement.GetProperty("version").GetString() is not { Length: > 0 }
                    version || string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException("Native Python distribution identity differs.");
            return version;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException)
        {
            throw new InvalidDataException("Invalid native Python distribution identity.",
                exception);
        }
    }

    private static PythonGroupOperation[] Operations(PythonMetadata metadata) =>
        metadata.Members.Select(member => member.Directory).Append(".")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(directory => new PythonGroupOperation(directory,
                Path.GetFullPath(Path.Combine(metadata.Root, directory)), [], false)).ToArray();
}
