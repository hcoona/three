using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonPytestConfiguration(string ConfigurationFile, string[] TestPaths,
    string[] Options);

// Pytest owns configuration precedence/parsing. These are finite request shape guards.
internal sealed class PythonPytestConfigurationReader
{
    private readonly Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute;

    internal PythonPytestConfigurationReader() : this(NativeProcess.ExecuteAsync) { }

    internal PythonPytestConfigurationReader(
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute) =>
        this.execute = execute;

    internal async Task<PythonPytestConfiguration> ReadAsync(string directory, string helperPython,
        string queryScript, IReadOnlyDictionary<string, string?> environment,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(directory) ||
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) != directory)
            throw new InvalidDataException("Pytest endpoint must be a canonical absolute path.");
        var child = new Dictionary<string, string?>(environment, StringComparer.Ordinal)
        {
            ["PYTEST_ADDOPTS"] = null,
            ["PYTEST_PLUGINS"] = null,
            ["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] = "1"
        };
        NativeCommandResult result = await execute(new(helperPython, directory,
            [queryScript, directory], 30, child), token);
        if (!result.Succeeded || result.Error is not null)
            throw new InvalidDataException("Native pytest configuration extraction failed.");
        try
        {
            using JsonDocument document = JsonDocument.Parse(result.Stdout);
            JsonElement facts = document.RootElement;
            if (facts.GetProperty("directory").GetString() != directory ||
                facts.GetProperty("root").GetString() != directory)
                throw new InvalidDataException("Native pytest configuration has a different root.");
            string configuration = Text(facts.GetProperty("configuration_file"));
            if (!Path.IsPathFullyQualified(configuration))
                throw new InvalidDataException(
                    "Native pytest configuration input is not absolute.");
            string input = Path.GetRelativePath(directory, configuration).Replace('\\', '/');
            ImpactPlanner.ValidatePath(input);
            string[] targets = facts.GetProperty("testpaths").EnumerateArray().Select(Text)
                .ToArray();
            if (targets.Length == 0 || targets.Distinct(StringComparer.Ordinal).Count() !=
                targets.Length)
                throw new InvalidDataException(
                    "Native pytest requires explicit unique test paths.");
            foreach (string target in targets)
            {
                ImpactPlanner.ValidatePath(target);
                if (target.IndexOfAny(['*', '?', '[', ']']) >= 0)
                    throw new InvalidDataException("Unsupported native pytest target shape.");
            }
            string[] options = facts.GetProperty("addopts").EnumerateArray().Select(Text)
                .ToArray();
            if (options.Length != 0 &&
                (options.Length != 1 || options[0] != "--import-mode=importlib"))
                throw new InvalidDataException("Unsupported native pytest option scope.");
            if (facts.GetProperty("pythonpath").GetArrayLength() != 0 ||
                facts.GetProperty("required_plugins").GetArrayLength() != 0)
                throw new InvalidDataException("Unsupported native pytest path or plugin scope.");
            return new(input, targets, options);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("Invalid native pytest configuration response.");
        }
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!
            : throw new InvalidDataException("Invalid native pytest configuration field.");
}
