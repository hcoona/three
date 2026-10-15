using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

internal sealed record PythonRemoteCommand(string Stage, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record PythonIndexToolRequest(string Uv, string Interpreter,
    string Requirements, string Scratch);
internal sealed record PythonIndexToolResult(string Interpreter,
    PythonRemoteCommand[] Commands, string[] Errors);

// The producer exports the synchronized native lock before product execution.
// Receiving jobs install that immutable registry-only recipe, never product code.
internal static class PythonIndexToolPreparation
{
    internal static Task<PythonIndexToolResult> PrepareAsync(PythonIndexToolRequest request,
        CancellationToken token) => PrepareAsync(request, NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonIndexToolResult> PrepareAsync(PythonIndexToolRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        foreach (string path in new[] { request.Uv, request.Interpreter, request.Requirements,
                     request.Scratch })
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidDataException("Python index tool inputs must be absolute.");
        if (!Directory.Exists(request.Scratch) ||
            Directory.EnumerateFileSystemEntries(request.Scratch).Any() ||
            !File.Exists(request.Requirements))
            throw new InvalidDataException("Python index tools need a recipe and fresh scratch.");
        var commands = new List<PythonRemoteCommand>();
        var errors = new List<string>();
        string venv = Path.Combine(request.Scratch, "venv");
        string python = Path.Combine(venv, OperatingSystem.IsWindows()
            ? "Scripts/python.exe" : "bin/python");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "PATH", "SystemRoot" })
            environment[name] = Environment.GetEnvironmentVariable(name);
        foreach (string name in new[] { "HOME", "USERPROFILE", "XDG_CONFIG_HOME",
                     "XDG_DATA_HOME", "XDG_CACHE_HOME", "UV_CACHE_DIR", "TMPDIR", "TMP",
                     "TEMP" })
            environment[name] = Directory.CreateDirectory(Path.Combine(request.Scratch,
                name.ToLowerInvariant())).FullName;
        environment["UV_PYTHON_DOWNLOADS"] = "never";
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            await Run("venv", ["venv", venv, "--python", request.Interpreter, "--no-project"]);
            await Run("tools", ["pip", "install", "--python", python, "--no-cache",
                "--require-hashes", "--only-binary", ":all:", "--keyring-provider", "disabled",
                "--index-url", "https://pypi.org/simple/", "--requirements", request.Requirements]);
        }
        catch (ToolStopped)
        { errors.Add("Required native index tool preparation command did not succeed."); }
        catch (OperationCanceledException)
        { errors.Add("Native index tool preparation was interrupted."); }
        return new(python, [.. commands], [.. errors]);

        async Task Run(string stage, string[] arguments)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var command = new NativeCommand(request.Uv, request.Scratch,
                ["--no-config", .. arguments], 180, environment, InheritEnvironment: false,
                CloseStandardInput: true);
            NativeCommandResult result = await execute(command, lifetime.Token);
            commands.Add(new(stage, command, result));
            if (!result.Succeeded || result.Error is not null) throw new ToolStopped();
        }
    }

    private sealed class ToolStopped : Exception;
}
