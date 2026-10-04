using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PnpmScripts(string Directory, IReadOnlyDictionary<string, string> Scripts);

// Quality contracts select script names. PNPM owns their interpretation and execution.
internal sealed class PnpmScriptReader
{
    private readonly string root;
    private readonly Func<string[], CancellationToken, Task<string>> query;

    internal PnpmScriptReader(string root, string executable = "pnpm")
        : this(root, (arguments, token) => NativeProcess.RunAsync(executable,
            Path.GetFullPath(root), arguments, token))
    { }

    internal PnpmScriptReader(string root, Func<string[], CancellationToken, Task<string>> query)
    {
        this.root = Path.GetFullPath(root);
        this.query = query;
    }

    internal async Task<PnpmScripts> ReadAsync(PnpmProject project, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string directory = project.Directory;
        if (directory != "." && (string.IsNullOrWhiteSpace(directory) ||
            Path.IsPathRooted(directory) || directory.Contains('\\') ||
            directory.Split('/').Any(part => part is "" or "." or "..")))
            throw new InvalidDataException(
                "PNPM requires a discovered canonical project directory.");
        string output = await query(["--dir", Path.GetFullPath(directory, root),
            "pkg", "get", "scripts", "--json"], token);
        token.ThrowIfCancellationRequested();
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(output))
        {
            using JsonDocument value = JsonDocument.Parse(output);
            if (value.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("PNPM scripts must be an object of opaque strings.");
            foreach (JsonProperty script in value.RootElement.EnumerateObject())
            {
                if (script.Value.ValueKind != JsonValueKind.String ||
                    !scripts.TryAdd(script.Name, script.Value.GetString()!))
                    throw new InvalidDataException(
                        "PNPM returned invalid or duplicate script metadata.");
            }
        }
        return new(directory, scripts);
    }
}
