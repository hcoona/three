using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.CI.Callers;

internal sealed record CiCallerEndpoints(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full);
internal sealed record CiCallerControl(string Application, string Root, string Transfer);
internal sealed record CiCallerCommand(NativeCommand Command, NativeCommandResult Result);

// Shared process/transfer mechanics for the four concrete callers, not a scheduler.
internal static class CiCallerTransfer
{
    internal static T Read<T>(string path, JsonTypeInfo<T> type) => ReleaseCli.Read(path, type);

    internal static void Write<T>(string path, T value, JsonTypeInfo<T> type) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, type) + "\n",
            new System.Text.UTF8Encoding(false, true));

    internal static void Output(string key, string value)
    {
        if (key.Contains('\n') || value.Contains('\n') || value.Contains('\r'))
            throw new InvalidDataException("Invalid single-line CI output.");
        if (Environment.GetEnvironmentVariable("GITHUB_OUTPUT") is { Length: > 0 } path)
            File.AppendAllText(path, key + "=" + value + "\n");
    }

    internal static string Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("CI caller paths must be absolute.");
        // GetFullPath preserves shim spelling; resolving symbolic links would change identity.
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static void External(string root, string directory)
    {
        root = Absolute(root); directory = Absolute(directory);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (root.Equals(directory, comparison) ||
            root.StartsWith(directory + Path.DirectorySeparatorChar, comparison) ||
            directory.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidDataException("CI caller state must be outside the source checkout.");
    }

    internal static string Fresh(string directory)
    {
        directory = Absolute(directory);
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new InvalidDataException("CI caller state already exists.");
        return Directory.CreateDirectory(directory).FullName;
    }

    internal static string Application(string transfer)
    {
        string control = Path.Combine(Absolute(transfer), "control");
        foreach (string file in new[] { "WorkflowDelivery.dll", "WorkflowDelivery.deps.json",
                     "WorkflowDelivery.runtimeconfig.json" })
            if (!File.Exists(Path.Combine(control, file)))
                throw new
                    InvalidDataException("The complete candidate control distribution is missing.");
        return Path.Combine(control, "WorkflowDelivery.dll");
    }

    internal static async Task<GitComparison> ComparisonAsync(string root, string scopePath,
        CancellationToken token)
    {
        JsonElement scope = Read(scopePath, TransferJson.Default.JsonElement);
        string basis = ScopeCommit(scope, "base");
        string candidate = ScopeCommit(scope, "candidate");
        bool full = Full(scope);
        Commit(basis); Commit(candidate);
        if (full && basis != candidate)
            throw new InvalidDataException("Explicit full has a different basis.");
        var reader = new GitReader(root);
        GitComparison comparison = await reader.CompareAsync(basis, candidate, token);
        if (comparison.Basis.Commit != basis || comparison.Candidate.Commit != candidate ||
            (await reader.ReadAsync("HEAD", token)).Commit != candidate)
            throw new
                InvalidDataException("Scope does not name exact available candidate commits.");
        _ = ScopeInputs.Read(scope.GetRawText(), comparison, full);
        return comparison;
    }

    internal static async Task<CiCallerEndpoints> MaterializeAsync(string root, string scopePath,
        string directory, bool separate, CancellationToken token)
    {
        External(root, directory);
        GitComparison comparison = await ComparisonAsync(root, scopePath, token);
        bool full = Full(Read(scopePath, TransferJson.Default.JsonElement));
        Fresh(directory);
        var basis = new MaterializedEndpoint(Path.Combine(directory, "basis"),
            comparison.Basis.Commit);
        var candidate = new MaterializedEndpoint(!separate &&
            comparison.Basis.Commit == comparison.Candidate.Commit ? basis.Directory :
                Path.Combine(directory, "candidate"), comparison.Candidate.Commit);
        foreach (MaterializedEndpoint endpoint in new[] { basis, candidate }.Distinct())
        {
            await RunAsync(new("git", root, ["worktree", "add", "--detach", endpoint.Directory,
                endpoint.Reference], 900), directory, Path.GetFileName(endpoint.Directory) +
                "-checkout", token);
            await GitMaterialization.BindAsync(endpoint.Directory,
                endpoint == basis ? comparison.Basis : comparison.Candidate, token);
        }
        return new(basis, candidate, full);
    }

    internal static bool Full(JsonElement scope)
    {
        if (scope.ValueKind != JsonValueKind.Object ||
            !scope.TryGetProperty("full", out JsonElement full) ||
            full.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Missing or malformed scope comparison mode.");
        return full.GetBoolean();
    }

    private static string ScopeCommit(JsonElement scope, string name)
    {
        if (scope.ValueKind != JsonValueKind.Object ||
            !scope.TryGetProperty(name, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String || value.GetString() is not { } commit)
            throw new InvalidDataException("Missing or malformed scope commit.");
        Commit(commit);
        return commit;
    }

    internal static void Commit(string value)
    {
        if (value.Length != 40 || value.Any(c => c is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f')))
            throw new InvalidDataException("Scope requires full native commit identities.");
    }

    internal static void Plan(CiPlan plan, string basis, string candidate, string scope)
    {
        Commit(basis); Commit(candidate);
        if (plan.Comparison != basis || plan.Candidate != candidate || plan.Scope != scope)
            throw new InvalidDataException("The original CI plan has a different binding.");
        _ = ResultCollector.Collect(plan, []);
    }

    internal static CiOutcome Outcome(CiPlan plan, CiOutcome reported)
    {
        if (reported is null || reported.Checks is null || reported.Errors is null ||
            reported.Comparison != plan.Comparison || reported.Candidate != plan.Candidate ||
            reported.Scope != plan.Scope)
            throw new InvalidDataException("Group outcome lacks its original plan binding.");
        return reported;
    }

    internal static async Task<NativeCommandResult> RunAsync(NativeCommand command,
        string directory, string label, CancellationToken token,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>>? execute = null)
    {
        NativeCommandResult result = await (execute ?? NativeProcess.ExecuteAsync)(command, token);
        File.WriteAllText(Path.Combine(directory, label + ".stdout"), result.Stdout);
        File.WriteAllText(Path.Combine(directory, label + ".stderr"), result.Stderr);
        Write(Path.Combine(directory, label + ".command.json"), new CiCallerCommand(command,
            result),
            TransferJson.Default.CiCallerCommand);
        if (!result.Succeeded || result.Error is not null)
            throw new
                IOException($"CI native prerequisite failed: {label}, {result.Termination}, " +
                $"exit {result.ExitCode}. Original diagnostics retained in {directory}.");
        return result;
    }
}
