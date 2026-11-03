using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

// Validates the provisioned tools of one group and removes every other external tool from the
// session PATH. This covers PATH resolution only, not absolute paths or registry discovery.
internal static class DotNetToolEnvironment
{
    private static readonly string[] Removed = ["python", "python3", "node", "npm"];

    internal static async Task<string> PathAsync(Dictionary<string, string> tools,
        string checkout, string? path, string? pathExtensions,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string[] directories = (path ?? "").Split(System.IO.Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries);
        string[] extensions = OperatingSystem.IsWindows()
            ? (string.IsNullOrWhiteSpace(pathExtensions) ? ".COM;.EXE;.BAT;.CMD" :
                pathExtensions).Split(';', StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
            : [""];
        var kept = new List<string>();
        string? node = null;
        foreach (string tool in DotNetTools.Names(tools.Keys))
        {
            // Sorted names validate node before npm, which must resolve beside it.
            string expected = tool switch
            {
                "python" => "Python " + tools[tool],
                "node" => "v" + tools[tool],
                _ => node!,
            };
            string? file = Resolve(tool);
            if (file is null) throw Failure(tool, expected, "missing", "PATH");
            string directory = System.IO.Path.GetDirectoryName(file)!;
            if (tool == "npm")
            {
                if (!Same(directory, expected)) throw Failure(tool, expected, directory, file);
            }
            else
            {
                NativeCommandResult result = await execute(new(file, checkout, ["--version"],
                    30), token);
                string actual = result.Succeeded ? result.Stdout.Trim() :
                    result.Error ?? result.Stderr.Trim();
                if (!result.Succeeded || actual != expected)
                    throw Failure(tool, expected, actual, file);
                if (tool == "node") node = directory;
            }
            kept.Add(directory);
        }
        return string.Join(System.IO.Path.PathSeparator, directories.Where(directory =>
            kept.Any(item => Same(item, directory)) ||
            !Removed.Any(name => Candidates(directory, name).Any(File.Exists))));

        string? Resolve(string name) => directories.SelectMany(directory =>
            Candidates(directory, name)).FirstOrDefault(File.Exists);

        IEnumerable<string> Candidates(string directory, string name) =>
            extensions.Select(extension => System.IO.Path.Combine(directory, name + extension));
    }

    // Commands that inherit the job environment receive the group PATH unless they own PATH.
    internal static Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> Scoped(
        string path, Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute) =>
        (command, token) =>
        {
            if (!command.InheritEnvironment || (command.Environment?.Keys.Any(name =>
                    name.Equals("PATH", StringComparison.OrdinalIgnoreCase)) ?? false))
                return execute(command, token);
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach ((string name, string? value) in command.Environment ??
                     new Dictionary<string, string?>())
                environment.Add(name, value);
            environment.Add("PATH", path);
            return execute(command with { Environment = environment }, token);
        };

    private static bool Same(string left, string right) => string.Equals(
        System.IO.Path.TrimEndingDirectorySeparator(left),
        System.IO.Path.TrimEndingDirectorySeparator(right),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
            StringComparison.Ordinal);

    private static InvalidDataException Failure(string tool, string expected, string actual,
        string path) => new("External tool " + tool + " expected '" + expected +
            "', actual '" + actual + "', path '" + path + "'.");
}
