using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Tests.Repository;

// Runs the mise-locked Node and PNPM the way the Node group does, but with a constructed
// environment: no inherited npm/PNPM configuration, user configuration, store or proxy can
// reach a contract row. Every result must end on its own (no deadline kill or start failure).
internal sealed class PnpmNativeTool : IDisposable
{
    private readonly string state = Directory.CreateTempSubdirectory("pnpm-contract-state-")
        .FullName;

    internal string Pnpm { get; private init; } = "";
    internal string Node { get; private init; } = "";

    internal static async Task<PnpmNativeTool> CreateAsync(CancellationToken token)
    {
        string pnpm = await WhichAsync("pnpm", token);
        string node = await WhichAsync("node", token);
        return new PnpmNativeTool { Pnpm = pnpm, Node = node };
    }

    // Extra variables for one row, such as a user-level PNPM setting.
    internal Dictionary<string, string?> Extra { get; } = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, string?> Environment
    {
        get
        {
            string home = Directory.CreateDirectory(Path.Combine(state, "home")).FullName;
            var values = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = string.Join(Path.PathSeparator, Path.GetDirectoryName(Node),
                    "/usr/bin", "/bin"),
                ["HOME"] = home,
                ["XDG_CONFIG_HOME"] = Path.Combine(home, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(home, "data"),
                ["XDG_CACHE_HOME"] = Path.Combine(home, "cache"),
                ["XDG_STATE_HOME"] = Path.Combine(home, "state"),
                ["TMPDIR"] = Directory.CreateDirectory(Path.Combine(state, "tmp")).FullName,
                ["LANG"] = "C.UTF-8",
                ["CI"] = "true",
                // Any registry or proxied request fails instead of leaving the host.
                ["npm_config_registry"] = "http://127.0.0.1:9/",
                ["HTTPS_PROXY"] = "http://127.0.0.1:9",
                ["HTTP_PROXY"] = "http://127.0.0.1:9",
            };
            foreach ((string name, string? value) in Extra) values[name] = value;
            return values;
        }
    }

    internal Task<NativeCommandResult> RunAsync(string executable, string directory,
        string[] arguments, CancellationToken token) => NativeProcess.ExecuteAsync(new(
            executable == "pnpm" ? Pnpm : executable == "node" ? Node : executable, directory,
            arguments, 120, Environment, InheritEnvironment: false, CloseStandardInput: true),
            token);

    // The production command shape with this tool's executables and environment.
    internal Task<NativeCommandResult> RunAsync(NativeCommand command, CancellationToken token)
        => RunAsync(command.Executable, command.Directory, command.Arguments, token);

    internal static void AssertExited(NativeCommandResult result, bool succeeded)
    {
        Assert.AreEqual(NativeTermination.Exited, result.Termination,
            NativeDiagnostic.Describe(result, true));
        Assert.AreEqual(succeeded, result.ExitCode == 0, NativeDiagnostic.Describe(result,
            true));
    }

    // A reader query: exited successfully, answered on stdout.
    internal async Task<string> QueryAsync(string directory, string[] arguments,
        CancellationToken token)
    {
        NativeCommandResult result = await RunAsync("pnpm", directory, arguments, token);
        AssertExited(result, true);
        return result.Stdout;
    }

    // A disposable copy of a committed fixture, under a path with a space.
    internal static string Copy(string fixture, string destination)
    {
        string source = Path.Combine(RepositoryToolSelection.SourceRoot,
            "tests/private/app/workflow-delivery/fixtures", fixture);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return destination;
    }

    private static async Task<string> WhichAsync(string tool, CancellationToken token)
    {
        string path = (await NativeProcess.RunAsync("mise", RepositoryToolSelection.SourceRoot,
            ["which", tool], token)).Trim();
        Assert.IsTrue(Path.IsPathFullyQualified(path) && File.Exists(path), tool);
        return path;
    }

    public void Dispose() => ScratchRoots.TryDelete(state);
}
