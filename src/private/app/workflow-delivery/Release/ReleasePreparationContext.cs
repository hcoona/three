using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal static class ReleasePreparationContext
{
    internal static Dictionary<string, string?> CreateHomeEnvironment(string scratch,
        IEnumerable<string> inheritedVariables,
        IEnumerable<(string Key, string? Value)>? overrides = null, bool createTemp = false) =>
        CreateHomeEnvironment(inheritedVariables, overrides, Path.Combine(scratch, "home"),
            createTemp ? Path.Combine(scratch, "temp") : null);

    internal static Dictionary<string, string?> CreateHomeEnvironment(
        IEnumerable<string> inheritedVariables, IEnumerable<(string Key, string? Value)>? overrides,
        string? home, string? temp = null, bool createProfileDirectories = false)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in inheritedVariables)
            if (Environment.GetEnvironmentVariable(name) is { } value)
                values[name] = value;
        if (home is not null)
        {
            home = Directory.CreateDirectory(home).FullName;
            values["HOME"] = values["USERPROFILE"] = values["DOTNET_CLI_HOME"] = home;
            values["XDG_CONFIG_HOME"] = HomeDirectory("config");
            values["XDG_CACHE_HOME"] = HomeDirectory("cache");
            values["XDG_DATA_HOME"] = HomeDirectory("data");
            if (createProfileDirectories)
            {
                values["APPDATA"] = HomeDirectory("appdata");
                values["LOCALAPPDATA"] = HomeDirectory("localappdata");
            }
        }
        if (temp is not null)
        {
            temp = Directory.CreateDirectory(temp).FullName;
            values["TEMP"] = values["TMP"] = temp;
            // The Windows runner profile uses its inherited home and sets only TEMP and TMP.
            if (home is not null)
                values["TMPDIR"] = temp;
        }
        if (overrides is not null)
            foreach ((string key, string? value) in overrides)
                values[key] = value;
        return values;

        string HomeDirectory(string name)
        {
            string path = Path.Combine(home!, name);
            return createProfileDirectories ? Directory.CreateDirectory(path).FullName : path;
        }
    }

    internal static IEnumerable<(string Key, string? Value)> GithubVersioningEnvironment(
        string scratch, ReleaseSubject subject)
    {
        // NBGV reads the requested ref and writes cloud-build variables only to owned scratch.
        string output = Path.Combine(scratch, "nbgv-environment");
        File.WriteAllText(output, "");
        yield return ("GITHUB_ACTIONS", "true");
        yield return ("GITHUB_REF", subject.SelectedRef);
        yield return ("GITHUB_SHA", subject.Revision);
        yield return ("GITHUB_ENV", output);
    }

    internal static Task RequireCommittedInputsAsync(GitMaterialization bound,
        IEnumerable<string> paths, CancellationToken token) =>
        RequireCommittedInputsAsync(bound.ReadOptionalTextAsync, paths, token);

    internal static async Task RequireCommittedInputsAsync(
        Func<string, CancellationToken, Task<string?>> read, IEnumerable<string> paths,
        CancellationToken token)
    {
        foreach (string input in paths)
            if (await read(input, token) is null)
                throw new InvalidDataException("Missing committed native input: " + input);
    }

    internal static async Task RequireCompleteHistoryAsync(string checkout,
        Func<NativeCommand, Task<NativeCommandResult>> run)
    {
        if ((await run(new("git", checkout,
            ["rev-parse", "--is-shallow-repository"], 30))).Stdout.Trim() != "false")
            throw new InvalidDataException("Native versioning requires complete Git history.");
    }

    internal static async Task<string> RequirePinnedSdkAsync(GitMaterialization bound,
        string checkout, string dotnetExecutable,
        Func<NativeCommand, Task<NativeCommandResult>> run, CancellationToken token,
        string missingSelection = "Missing SDK selection.")
    {
        using JsonDocument global = JsonDocument.Parse(await bound.ReadOptionalTextAsync(
            "global.json", token) ?? throw new InvalidDataException(missingSelection));
        JsonElement sdk = global.RootElement.GetProperty("sdk");
        string? selected = sdk.GetProperty("version").GetString();
        const string mismatch = "Actual SDK differs from committed selection.";
        if (sdk.GetProperty("rollForward").GetString() != "disable" ||
            string.IsNullOrWhiteSpace(selected) ||
            (await run(new(dotnetExecutable, checkout, ["--version"], 30))).Stdout.Trim() !=
                selected)
            throw new InvalidDataException(mismatch);
        return selected;
    }

    internal static async Task<string> SelectLockedMiseToolAsync(string checkout, string tool,
        Func<NativeCommand, Task<NativeCommandResult>> run,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        NativeCommandResult result = await run(new("mise", checkout,
            LockedToolSelection.Query(tool), 30, environment));
        return LockedToolSelection.Selected(tool, checkout, result.Stdout.Trim());
    }
}
