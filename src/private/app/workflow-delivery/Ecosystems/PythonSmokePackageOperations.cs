using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

internal sealed record PythonPackageTools(string Checkout, string Scratch, string Uv,
    string Interpreter, string Helper, int DeadlineSeconds,
    IReadOnlyDictionary<string, string?> Environment, bool InheritEnvironment = true);
internal sealed record PythonDistribution(string Kind, string Path, long Length, string Sha256);

// Concrete registered Python smoke recipe using native package operations; no CI planning or
// Release completion. Other registered recipes extend its native operations.
internal class PythonSmokePackageOperations(PythonPackageTools tools, string root)
{
    internal const string Preset = "python/hcoona-release-smoke-python-v1";
    private readonly string archives = Path.Combine(root, "archives");
    private protected readonly PythonPackageTools tools = tools;
    private protected readonly string root = root;
    private readonly string python = Python(Path.Combine(root, "tools"));
    private protected readonly Dictionary<string, string?> environment = new(tools.Environment,
        StringComparer.Ordinal)
    {
        ["UV_PROJECT_ENVIRONMENT"] = Path.Combine(root, "tools"),
        ["UV_CACHE_DIR"] = Path.Combine(tools.Scratch, "uv-cache"),
        ["UV_PYTHON_DOWNLOADS"] = "never",
        ["PYTHONPATH"] = null,
        ["PYTHONHOME"] = null,
        ["VIRTUAL_ENV"] = null,
    };

    internal static void Validate(PythonPackageTools tools)
    {
        string checkout = Absolute(tools.Checkout), scratch = Absolute(tools.Scratch);
        _ = Absolute(tools.Interpreter);
        _ = Absolute(tools.Helper);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            OwnedPath.Overlap(scratch, checkout) ||
            Directory.EnumerateFileSystemEntries(scratch).Any() || tools.DeadlineSeconds <= 0)
            throw new InvalidDataException(
                "Python package execution requires fresh external scratch.");
        for (DirectoryInfo? parent = new(scratch); parent is not null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, ".git")) ||
                Directory.Exists(Path.Combine(parent.FullName, ".git")))
                throw new InvalidDataException("Python consumers require Git-free scratch.");
    }

    // The caller records each command and stops on its actual unsuccessful result.
    internal async Task<(string Name, string Version)> PrepareAsync(string directory,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        Directory.CreateDirectory(archives);
        await PrepareToolsAsync(execute, token);
        NativeCommandResult result = await execute(Helper(python, tools.Checkout,
            ["identity", Path.Combine(tools.Checkout, directory)]), token);
        using JsonDocument identity = JsonDocument.Parse(result.Stdout);
        return (identity.RootElement.GetProperty("name").GetString()!,
            identity.RootElement.GetProperty("version").GetString()!);
    }

    internal async Task<PythonDistribution[]> BuildAsync(string name, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        await execute(Uv(tools.Checkout, ["build", "--package", name, "--no-create-gitignore",
            "--no-build-isolation", "--python", python, "--out-dir", archives,
            "--preview-features", "build-dependency-check"]), token);
        NativeCommandResult result = await execute(Helper(python, root,
            ["outputs", archives, name, version, Preset]), token);
        using JsonDocument receipt = JsonDocument.Parse(result.Stdout);
        var originals = new List<PythonDistribution>();
        foreach (JsonElement value in receipt.RootElement.EnumerateArray())
        {
            string kind = value.GetProperty("kind").GetString()!;
            string path = Absolute(value.GetProperty("path").GetString()!);
            if (kind is not ("python-wheel" or "python-sdist") ||
                Path.GetDirectoryName(path) != archives ||
                originals.Any(item => item.Kind == kind))
                throw new InvalidDataException("Ambiguous original Python output.");
            var original = new PythonDistribution(kind, path,
                value.GetProperty("length").GetInt64(), value.GetProperty("sha256").GetString()!);
            RequireOriginal(original);
            originals.Add(original);
        }
        if (originals.Count != 2 || Directory.GetFileSystemEntries(archives).Length != 2)
            throw new InvalidDataException("Incomplete original Python output set.");
        return [.. originals];
    }

    internal virtual async Task<JsonDocument> VersionFactsAsync(string directory,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => JsonDocument.Parse((await execute(Helper(python,
            tools.Checkout, ["version-facts", Path.Combine(tools.Checkout, directory)]), token))
                .Stdout);

    internal string ToolInterpreter => python;

    internal Task<NativeCommandResult> PrepareToolsAsync(
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => execute(Uv(tools.Checkout,
            ["sync", "--frozen", "--only-group", "dev", "--only-group",
                "workflow-delivery-python", "--python", tools.Interpreter]), token);

    internal Task<NativeCommandResult> PrepareNoneditableToolsAsync(
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => execute(Uv(tools.Checkout,
            ["sync", "--frozen", "--only-group", "dev", "--only-group",
                "workflow-delivery-python", "--no-build-isolation", "--no-editable",
                "--python", tools.Interpreter]), token);

    internal async Task ContentsAsync(PythonDistribution[] originals, string name, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        foreach (PythonDistribution original in originals)
        {
            RequireOriginal(original);
            await execute(Helper(python, root, ["archive", original.Path, name, version, Preset]),
                token);
            RequireOriginal(original);
        }
    }

    internal async Task ConsumeAsync(string kind, PythonDistribution[] originals, string name,
        string version, Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        if (kind is not ("python-wheel" or "python-sdist"))
            throw new InvalidDataException("Unsupported Python distribution consumer.");
        PythonDistribution original = originals.Single(item => item.Kind == kind);
        RequireOriginal(original);
        string consumer = Directory.CreateDirectory(Path.Combine(root,
            kind + "-consumer")).FullName;
        string install = original.Path;
        if (kind == "python-sdist")
        {
            await PrepareNoneditableToolsAsync(execute, token);
            foreach (PythonDistribution retained in originals) RequireOriginal(retained);
            var neutral = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
            foreach (System.Collections.DictionaryEntry variable in
                     System.Environment.GetEnvironmentVariables())
                if (((string)variable.Key).StartsWith("GIT_", StringComparison.Ordinal))
                    neutral[(string)variable.Key] = null;
            foreach (string key in tools.Environment.Keys.Where(key =>
                         key.StartsWith("GIT_", StringComparison.Ordinal))) neutral[key] = null;
            string rebuilt = Directory.CreateDirectory(Path.Combine(consumer, "rebuilt")).FullName;
            await execute(new(tools.Uv, consumer, ["build", original.Path, "--wheel",
                "--no-create-gitignore", "--no-build-isolation", "--python", python,
                "--out-dir", rebuilt, "--preview-features", "build-dependency-check", "--offline"],
                tools.DeadlineSeconds, neutral, tools.InheritEnvironment), token);
            string[] outputs = Directory.GetFileSystemEntries(rebuilt);
            if (outputs.Length != 1 || !outputs[0].EndsWith(".whl", StringComparison.Ordinal))
                throw new InvalidDataException("Incomplete Git-free native wheel output.");
            install = outputs[0];
            await execute(Helper(python, consumer, ["archive", install, name, version, Preset]),
                token);
        }
        string venv = Path.Combine(consumer, "venv");
        await execute(Uv(consumer, ["venv", venv, "--python", tools.Interpreter, "--no-project"]),
            token);
        await InstallRuntimeAsync(name, consumer, Python(venv), execute, token);
        await execute(Uv(consumer, ["pip", "install", "--python", Python(venv), "--no-deps",
            "--no-cache", "--offline", install]), token);
        await execute(Helper(Python(venv), consumer, ["consume", name, version, Preset]), token);
        RequireOriginal(original);
    }

    // The smoke distribution declares no runtime dependencies.
    private protected virtual Task InstallRuntimeAsync(string name, string consumer,
        string venv, Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => Task.CompletedTask;

    internal static void RequireOriginal(PythonDistribution original)
    {
        if (!File.Exists(original.Path))
            throw new InvalidDataException("Original Python distribution changed.");
        using FileStream stream = File.OpenRead(original.Path);
        if (stream.Length != original.Length)
            throw new InvalidDataException("Original Python distribution changed.");
        if (Convert.ToHexStringLower(SHA256.HashData(stream)) != original.Sha256)
            throw new InvalidDataException("Original Python distribution bytes changed.");
    }

    private protected NativeCommand Uv(string directory, string[] arguments) => new(tools.Uv,
        directory, arguments, tools.DeadlineSeconds, environment, tools.InheritEnvironment);
    private protected NativeCommand Helper(string executable, string directory,
        string[] arguments) => new(executable, directory, ["-I", tools.Helper, .. arguments],
            tools.DeadlineSeconds, environment, tools.InheritEnvironment);
    private protected static string Python(string venv) => Path.Combine(venv,
        OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
    private static string Absolute(string path) =>
        OwnedPath.Absolute(path, "Python package paths must be absolute.");
}
