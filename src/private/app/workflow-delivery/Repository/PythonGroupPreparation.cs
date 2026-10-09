using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record PythonQueryTools(string Uv, string Interpreter, string Dotnet,
    string Rustup, string Cargo);
internal sealed record PythonGroupPreparationRequest(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full, string ScopePath, string ControlSources,
    string Scratch, PythonQueryTools Tools, PythonPytestOperation Operation, int DeadlineSeconds);
internal sealed record PythonPreparationCommand(NativeCommand Command, NativeCommandResult Result);
internal sealed record PythonGroupPreparationResult(PythonGroupRequest Request,
    PythonPreparationCommand[] Commands);

// Prepare native tools only. The existing group reader selects work and the executor runs it.
internal static class PythonGroupPreparation
{
    internal static Task<PythonGroupPreparationResult> PrepareAsync(
        PythonGroupPreparationRequest request, CancellationToken token) =>
        PrepareAsync(request, NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonGroupPreparationResult> PrepareAsync(
        PythonGroupPreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string scratch = Absolute(request.Scratch), sources = Absolute(request.ControlSources);
        string before = Absolute(request.Basis.Directory),
            after = Absolute(request.Candidate.Directory);
        _ = Absolute(request.ScopePath);
        PythonQueryTools tools = request.Tools;
        foreach (string path in new[] { tools.Uv, tools.Interpreter, tools.Dotnet, tools.Rustup,
                     tools.Cargo }) _ = Absolute(path);
        if (request.DeadlineSeconds <= 0 || !Directory.Exists(sources) ||
            !Directory.Exists(before) || !Directory.Exists(after) ||
            !Directory.Exists(scratch) || Directory.EnumerateFileSystemEntries(scratch).Any() ||
            Within(scratch, before) || Within(scratch, after) || Within(scratch, sources) ||
            Within(before, scratch) || Within(after, scratch) || Within(sources, scratch) ||
            request.Full && request.Basis.Reference != request.Candidate.Reference)
            throw new InvalidDataException(
                "Python preparation requires exact endpoints and fresh external state.");
        foreach (string source in new[] { "passive.py", "pytest_configuration.py", "packages.py",
                     "uv/Cargo.toml", "uv/Cargo.lock", "uv/rust-toolchain.toml" })
            if (!File.Exists(Path.Combine(sources, source)))
                throw new InvalidDataException(
                    "The complete candidate Python control sources are missing.");

        var commands = new List<PythonPreparationCommand>();
        string native = Path.Combine(scratch, "native");
        Directory.CreateDirectory(native);
        Dictionary<string, string?> compilation = EnvironmentFor(native);
        compilation["RUSTUP_HOME"] = Path.Combine(native, "rustup");
        compilation["CARGO_HOME"] = Path.Combine(native, "cargo");
        string output = Path.Combine(native, "output");
        compilation["CARGO_TARGET_DIR"] = output;
        string rust = Path.Combine(sources, "uv");
        await RunAsync(tools.Rustup, rust, ["show"], compilation);
        await RunAsync(tools.Cargo, rust, ["build", "--locked"], compilation);
        string supplement = Path.Combine(output, "debug", "workflow-python-native-facts" +
            (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!File.Exists(supplement))
            throw new InvalidDataException("Native Python preparation omitted its supplement.");

        PythonGroupEndpoint basis = await EndpointAsync(request.Basis, "basis");
        PythonGroupEndpoint candidate = await EndpointAsync(request.Candidate, "candidate");
        token.ThrowIfCancellationRequested();
        return new(new(basis, candidate, request.Full, request.ScopePath), commands.ToArray());

        async Task<PythonGroupEndpoint> EndpointAsync(MaterializedEndpoint endpoint, string name)
        {
            string state = Path.Combine(scratch, name);
            Directory.CreateDirectory(state);
            Dictionary<string, string?> environment = EnvironmentFor(state);
            environment["UV_PROJECT_ENVIRONMENT"] = Path.Combine(state, "query-tools");
            // Keep this same owned tool/HOME context when native backend queries invoke NBGV.
            await RunAsync(tools.Dotnet, endpoint.Directory, ["tool", "restore"], environment);
            await RunAsync(tools.Uv, endpoint.Directory, ["sync", "--frozen", "--only-group",
                "dev", "--only-group", "workflow-delivery-python", "--no-editable", "--python",
                tools.Interpreter], environment);
            string helper = Path.Combine(state, "query-tools", OperatingSystem.IsWindows()
                ? "Scripts/python.exe" : "bin/python");
            if (!File.Exists(helper))
                throw new InvalidDataException(
                    "Python query-tool preparation omitted its interpreter.");
            environment["UV_PROJECT_ENVIRONMENT"] = Path.Combine(state, "activity-environment");
            string bootstrap = Path.Combine(state, "bootstrap"),
                activity = Path.Combine(state, "activity");
            Directory.CreateDirectory(bootstrap);
            Directory.CreateDirectory(activity);
            return new(endpoint, new(new(endpoint.Directory, tools.Uv, tools.Interpreter, helper,
                Path.Combine(sources, "passive.py"), supplement, environment["UV_CACHE_DIR"]!,
                bootstrap, activity), Path.Combine(sources, "pytest_configuration.py"),
                Path.Combine(sources, "packages.py"), request.Operation), environment);
        }

        async Task RunAsync(string executable, string directory, string[] arguments,
            IReadOnlyDictionary<string, string?> environment)
        {
            token.ThrowIfCancellationRequested();
            var command = new NativeCommand(executable, directory, arguments,
                request.DeadlineSeconds, new Dictionary<string, string?>(environment,
                    StringComparer.Ordinal));
            NativeCommandResult result = await execute(command, token);
            commands.Add(new(command, result));
            token.ThrowIfCancellationRequested();
            if (!result.Succeeded || result.Error is not null)
                throw new InvalidDataException(
                    $"Python query preparation failed: {result.Termination}, " +
                    $"exit {result.ExitCode}. {result.Error}");
        }

        Dictionary<string, string?> EnvironmentFor(string state)
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string name in System.Environment.GetEnvironmentVariables().Keys)
                environment[name] = null;
            foreach (string name in new[] { "SystemRoot", "WINDIR", "PATHEXT" })
                environment[name] = System.Environment.GetEnvironmentVariable(name);
            string home = Path.Combine(state, "home"), temporary = Path.Combine(state, "temporary");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(temporary);
            string system = OperatingSystem.IsWindows()
                ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.System)
                : "/usr/bin";
            environment["PATH"] = string.Join(Path.PathSeparator, new[] { tools.Dotnet,
                tools.Interpreter, tools.Uv, tools.Cargo, tools.Rustup }
                .Select(Path.GetDirectoryName).Append(system).Append(OperatingSystem.IsWindows()
                    ? system : "/bin").Distinct(StringComparer.Ordinal));
            environment["HOME"] = home;
            environment["USERPROFILE"] = home;
            environment["DOTNET_CLI_HOME"] = home;
            environment["XDG_CONFIG_HOME"] = Path.Combine(home, "config");
            environment["XDG_CACHE_HOME"] = Path.Combine(home, "cache");
            environment["XDG_DATA_HOME"] = Path.Combine(home, "data");
            environment["TMPDIR"] = temporary;
            environment["TEMP"] = temporary;
            environment["TMP"] = temporary;
            environment["LANG"] = "C.UTF-8";
            environment["LC_ALL"] = "C.UTF-8";
            environment["UV_CACHE_DIR"] = Path.Combine(state, "uv-cache");
            environment["UV_PYTHON_DOWNLOADS"] = "never";
            environment["UV_NO_PROGRESS"] = "1";
            environment["PYTHONNOUSERSITE"] = "1";
            environment["PYTHONSAFEPATH"] = "1";
            environment["PYTHONDONTWRITEBYTECODE"] = "1";
            return environment;
        }
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path) &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) == path ? path :
        throw new InvalidDataException(
            "Python preparation paths must be canonical absolute paths.");

    private static bool Within(string child, string parent) => child.Equals(parent,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
            StringComparison.Ordinal) ||
        child.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
