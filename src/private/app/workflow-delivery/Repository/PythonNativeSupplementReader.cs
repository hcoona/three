using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonPassiveProject(string Directory, string[]? BuildRequirements);
internal sealed record PythonGroupOperation(string Id, string Directory,
    string[] Packages, bool NoDev);
internal sealed record PythonMarkerInput(string Id, string Expression, string[] Extras);
internal sealed record PythonConstraintInput(string Id, string Specifier);
internal sealed record PythonSupplementRequest(string WorkspaceRoot, string Interpreter,
    string Cache, PythonPassiveProject[] Projects, PythonGroupOperation[] GroupOperations,
    PythonMarkerInput[] Markers, PythonConstraintInput[] PythonConstraints);
internal sealed record PythonSupplementResult(JsonElement Facts, string[] ConfigurationInputs);

// This ordered transfer supplies native supplement facts, not a complete Python impact graph.
internal sealed class PythonNativeSupplementReader
{
    private readonly Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute;

    internal PythonNativeSupplementReader()
        : this(NativeProcess.ExecuteAsync) { }

    internal PythonNativeSupplementReader(
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute) =>
        this.execute = execute;

    internal async Task<PythonSupplementResult> ReadAsync(PythonSupplementRequest request,
        string helperPython, string passiveScript, string supplementExecutable, string scratch,
        IReadOnlyDictionary<string, string?> environment, CancellationToken token)
    {
        // The caller supplies exact endpoints/tools and owned configuration/cache/temp roots.
        string root = Absolute(request.WorkspaceRoot);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (PythonPassiveProject project in request.Projects)
        {
            string directory = Absolute(project.Directory);
            string relative = Path.GetRelativePath(root, directory);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || Path.IsPathFullyQualified(relative) ||
                !directories.Add(directory))
                throw new InvalidDataException("Python passive context identity is invalid.");
        }
        if (!directories.Contains(root))
            throw new InvalidDataException("Python passive contexts omit the workspace root.");
        for (DirectoryInfo? ancestor = Directory.GetParent(root); ancestor is not null;
            ancestor = ancestor.Parent)
        {
            try
            {
                _ = File.GetAttributes(Path.Combine(ancestor.FullName, "pyproject.toml"));
                directories.Add(ancestor.FullName);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        string[] contexts = directories.Order(StringComparer.Ordinal).ToArray();
        string[] inputs = contexts.Select(directory => Path.Combine(directory, "pyproject.toml"))
            .ToArray();
        NativeCommandResult preflight = await execute(new(helperPython, root,
            [passiveScript, "--configuration-only", .. contexts], 30, environment), token);
        if (!preflight.Succeeded)
            throw new InvalidDataException("Python configuration preflight failed.");
        try
        {
            using JsonDocument result = JsonDocument.Parse(preflight.Stdout);
            string?[] observed = result.RootElement.GetProperty("configuration_inputs")
                .EnumerateArray().Select(input => input.GetString()).ToArray();
            if (observed.Length != inputs.Length ||
                !observed.Order(StringComparer.Ordinal).SequenceEqual(
                    inputs.Order(StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException(
                    "Python configuration input identity is incomplete.");
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException)
        {
            throw new InvalidDataException("Python configuration preflight response is invalid.");
        }

        string requestPath = Path.Combine(Absolute(scratch), "python-supplement-request.json");
        await using (var stream = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 4096, useAsync: true))
            await JsonSerializer.SerializeAsync(stream, request,
                PythonSupplementJson.Default.PythonSupplementRequest, token);
        NativeCommandResult native = await execute(new(supplementExecutable, root,
            [requestPath], 30, environment), token);
        if (!native.Succeeded)
            throw new InvalidDataException("Python native supplementation failed.");
        try
        {
            using JsonDocument result = JsonDocument.Parse(native.Stdout);
            if (result.RootElement.GetProperty("workspace_root").GetString() != root ||
                result.RootElement.GetProperty("interpreter").GetString() != request.Interpreter)
                throw new InvalidDataException("Python native supplement subject is inconsistent.");
            return new(result.RootElement.Clone(), inputs);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException)
        {
            throw new InvalidDataException("Python native supplement response is invalid.");
        }
    }

    private static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path) ||
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) != path)
            throw new InvalidDataException("Python native paths must be canonical absolute paths.");
        return path;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(PythonSupplementRequest))]
internal partial class PythonSupplementJson : JsonSerializerContext;
