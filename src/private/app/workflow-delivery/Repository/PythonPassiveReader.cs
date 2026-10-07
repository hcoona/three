using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonNbgvCoordinates(string WorkingDirectory, string? GeneratedFile);
internal sealed record PythonPassiveInputs(PythonPassiveProject Project,
    PythonNbgvCoordinates? Nbgv);

// PyPA supplies build strings and the maintained NBGV plugin supplies its input coordinates.
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
            throw new InvalidDataException("Python passive extraction failed.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Stdout);
            JsonElement facts = document.RootElement;
            if (facts.GetProperty("directory").GetString() != directory)
                throw new InvalidDataException("Python passive context differs from its request.");
            JsonElement build = facts.GetProperty("build_requirements");
            string[]? requirements = build.ValueKind == JsonValueKind.Null ? null :
                build.EnumerateArray().Select(Text).ToArray();
            JsonElement plugin = facts.GetProperty("nbgv");
            PythonNbgvCoordinates? coordinates = null;
            if (plugin.ValueKind != JsonValueKind.Null)
            {
                string workingDirectory = Absolute(Text(plugin.GetProperty("working_directory")));
                JsonElement generated = plugin.GetProperty("generated_file");
                coordinates = new(workingDirectory, generated.ValueKind == JsonValueKind.Null
                    ? null : Absolute(Text(generated)));
            }
            return new(new(directory, requirements), coordinates);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("Invalid Python passive response.");
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
