using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonBuildRequirement(string Name, string[] Extras);
// An omitted source table is empty, so consumers need no separate absent case.
internal sealed record PythonPassiveProject(string Directory,
    PythonBuildRequirement[]? BuildRequirements,
    IReadOnlyDictionary<string, string>? Sources = null)
{
    internal IReadOnlyDictionary<string, string> Sources { get; init; } =
        Sources ?? new Dictionary<string, string>();
}
internal sealed record PythonNbgvCoordinates(string WorkingDirectory, string? GeneratedFile);
internal sealed record PythonPassiveInputs(PythonPassiveProject Project,
    PythonNbgvCoordinates? Nbgv);

// PyPA supplies declared build requirements and the maintained NBGV plugin supplies its
// input coordinates. Source entries are classified physically, not lowered.
internal sealed class PythonPassiveReader
{
    private readonly Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute;

    internal PythonPassiveReader() : this(NativeProcess.ExecuteAsync) { }

    internal PythonPassiveReader(
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute) =>
        this.execute = execute;

    internal async Task<PythonPassiveInputs> ReadAsync(string directory, string helperPython,
        string passiveScript, IReadOnlyDictionary<string, string?> environment,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CanonicalEndpoint(directory);
        NativeCommandResult result = await execute(new(helperPython, directory,
            [passiveScript, directory], 30, environment), token);
        if (!result.Succeeded || result.Error is not null)
            throw new InvalidDataException("Python passive extraction failed: " +
                NativeDiagnostic.Describe(result));
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Stdout);
            JsonElement facts = document.RootElement;
            if (facts.GetProperty("directory").GetString() != directory)
                throw new InvalidDataException("Python passive context differs from its request.");
            JsonElement build = facts.GetProperty("build_requirements");
            PythonBuildRequirement[]? requirements = build.ValueKind == JsonValueKind.Null ? null :
                build.EnumerateArray().Select(requirement => new PythonBuildRequirement(
                    Text(requirement.GetProperty("name")), requirement.GetProperty("extras")
                        .EnumerateArray().Select(Text).ToArray())).ToArray();
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty source in facts.GetProperty("sources").EnumerateObject())
                if (Text(source.Value) is not ("workspace" or "unsupported") ||
                    !sources.TryAdd(source.Name, source.Value.GetString()!))
                    throw new InvalidDataException("Invalid Python passive source class.");
            JsonElement plugin = facts.GetProperty("nbgv");
            PythonNbgvCoordinates? coordinates = null;
            if (plugin.ValueKind != JsonValueKind.Null)
            {
                string workingDirectory = Absolute(Text(plugin.GetProperty("working_directory")));
                JsonElement generated = plugin.GetProperty("generated_file");
                coordinates = new(workingDirectory, generated.ValueKind == JsonValueKind.Null
                    ? null : Absolute(Text(generated)));
            }
            return new(new(directory, requirements, sources), coordinates);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("Invalid Python passive response.");
        }
    }

    // Preflight every manifest UV can read for this endpoint, including ancestors.
    internal async Task<string[]> ReadConfigurationAsync(string root, string helperPython,
        string passiveScript, IEnumerable<string> contexts,
        IReadOnlyDictionary<string, string?> environment, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CanonicalEndpoint(root);
        var directories = new HashSet<string>(contexts, StringComparer.Ordinal);
        if (!directories.Contains(root))
            throw new InvalidDataException("Python passive contexts omit the workspace root.");
        for (DirectoryInfo? ancestor = new DirectoryInfo(root); ancestor is not null;
            ancestor = ancestor.Parent)
        {
            // UV project settings discovery reads uv.toml before pyproject.toml. This
            // request admits only pyproject workspace/source settings.
            if (Path.Exists(Path.Combine(ancestor.FullName, "uv.toml")))
                throw new InvalidDataException("Python project uv.toml settings are unsupported.");
            if (ancestor.FullName != root &&
                File.Exists(Path.Combine(ancestor.FullName, "pyproject.toml")))
                directories.Add(ancestor.FullName);
        }
        string[] ordered = directories.Order(StringComparer.Ordinal).ToArray();
        string[] inputs = ordered.Select(directory => Path.Combine(directory, "pyproject.toml"))
            .Order(StringComparer.Ordinal).ToArray();
        NativeCommandResult preflight = await execute(new(helperPython, root,
            [passiveScript, "--configuration-only", .. ordered], 30, environment), token);
        if (!preflight.Succeeded || preflight.Error is not null)
            throw new InvalidDataException("Python configuration preflight failed: " +
                NativeDiagnostic.Describe(preflight));
        try
        {
            using JsonDocument result = JsonDocument.Parse(preflight.Stdout);
            string?[] observed = result.RootElement.GetProperty("configuration_inputs")
                .EnumerateArray().Select(input => input.GetString()).ToArray();
            if (!observed.Order(StringComparer.Ordinal).SequenceEqual(inputs,
                StringComparer.Ordinal))
                throw new InvalidDataException(
                    "Python configuration input identity is incomplete.");
            return inputs;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException)
        {
            throw new InvalidDataException("Python configuration preflight response is invalid.");
        }
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!
            : throw new InvalidDataException("Invalid Python passive input coordinate.");

    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Python plugin coordinates must be absolute paths.");
        return path;
    }

    private static void CanonicalEndpoint(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) != path)
            throw new InvalidDataException(
                "Python passive paths must be canonical absolute paths.");
    }
}
