using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record NodeRunRequest(string Checkout, string Scratch, string Runtime);
internal sealed record NodeRuntimeReadback(string NodeVersion, string PnpmVersion,
    NativeCommandResult Node, NativeCommandResult Pnpm);
internal sealed record NodeCommandObservation(
    CheckKey Key, NativeCommand Command, NativeCommandResult Result
);
internal sealed record NodeOriginalOutput(
    string Unit, string Build, string Output, string Role, string Kind,
    string Path, long Size, string Sha256
);
internal sealed record NodeCheckFailure(CheckKey Key, string Error);
internal sealed record NodeRunResult(
    string Candidate, CheckResult[] Results, NodeCommandObservation[] Commands,
    NodeOriginalOutput[] Outputs, NodeCheckFailure[] Failures,
    string Runtime, NodeRuntimeReadback? RuntimeVersions
);

// This is one registered quality recipe, not a runtime graph scheduler.
internal static class NodeExecution
{
    private const string Preset = "node/hcoona-release-smoke-npm-v1";
    private const string PackageName = "@hcoona/hcoona-release-smoke-npm";
    private const string Build = "node/project-build-v1";
    private const string Test = "node/project-test-v1";
    private const string Pack = "node/npm-artifact-v1";
    private const string Contents = "node/npm-artifact-contents-v1";
    private const string Consumer = "node/npm-install-import-v1";

    internal static Task<NodeRunResult> RunAsync(CiPlan plan, NodeRunRequest request,
        CancellationToken token) => RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<NodeRunResult> RunAsync(CiPlan plan, NodeRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        CiPlan selected = SelectRuntime(plan, request.Runtime);
        string checkout = Absolute(request.Checkout);
        string scratch = Absolute(request.Scratch);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            scratch == checkout || Within(scratch, checkout) || Within(checkout, scratch) ||
            HasLinkedAncestor(checkout) || HasLinkedAncestor(scratch) ||
            Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException("Node execution requires fresh external scratch.");
        if (scratch.Contains("%s", StringComparison.Ordinal) ||
            scratch.Contains("%v", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Node scratch cannot contain native pack output markers.");
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException(
                "Node execution requires the exact native commit identity.");
        await GitMaterialization.BindAsync(checkout, revision, token);
        if (plan.Checks.Length == 0)
            return new(plan.Candidate, [], [], [], [], request.Runtime, null);
        NativeCommandResult node = await execute(new("node", checkout, ["--version"], 30), token);
        NativeCommandResult pnpm = await execute(new("pnpm", checkout, ["--version"], 30), token);
        string nodeVersion = ReadVersion(node, "node");
        string pnpmVersion = ReadVersion(pnpm, "pnpm");
        Dictionary<string, string> dimensions = selected.Checks[0].Work.Dimensions;
        if (pnpmVersion != dimensions["pnpm"] ||
            (request.Runtime == "node24" ? nodeVersion != dimensions["node-version"] :
                System.Version.Parse(nodeVersion).Major != 22))
            throw new InvalidDataException(
                "Native runtime differs from the selected Node context.");
        var runtimeVersions = new NodeRuntimeReadback(nodeVersion, pnpmVersion, node, pnpm);
        var results = new Dictionary<CheckKey, CheckResult>();
        var commands = new List<NodeCommandObservation>();
        var outputs = new List<NodeOriginalOutput>();
        var failures = new List<NodeCheckFailure>();
        var archives = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (PlannedCheck item in selected.Checks.Where(item =>
            item.Work.Key.Check is Build or Test))
        {
            CheckSpec check = item.Work;
            string script = check.Key.Check == Build ? "build" : "test";
            await CompleteAsync(check, () => CommandAsync(check,
                new("pnpm", checkout, ["--dir", Path.Combine(checkout, check.Key.Target),
                    "--config.verify-deps-before-run=false", "run", script], 300)));
        }
        int index = 0;
        foreach (PlannedCheck item in selected.Checks.Where(item => item.Work.Key.Check == Pack))
        {
            CheckSpec check = item.Work;
            PackageTarget package = check.Package!;
            string directory = Path.Combine(scratch, "build-" + index++);
            string archive = Path.Combine(directory, "package.tgz");
            archives.Add(check.Key.Target, directory);
            await CompleteAsync(check, async () =>
            {
                Directory.CreateDirectory(directory);
                CheckStatus status = await CommandAsync(check, new("pnpm", checkout,
                    ["--dir", Path.Combine(checkout, package.Directory), "pack", "--out", archive],
                    300));
                if (status != CheckStatus.Passed)
                    return status;
                NodePackageArchive.RequireRegularFile(archive);
                using var file = File.OpenRead(archive);
                long size = file.Length;
                if (size is <= 0 or > NodePackageArchive.CompressedLimit)
                    throw new InvalidDataException("Native npm output exceeds its size bound.");
                string digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token));
                PackageOutput output = package.Outputs.Single();
                outputs.Add(new(package.Unit, package.Build, output.Id, output.Role, output.Kind,
                    archive, size, digest));
                return CheckStatus.Passed;
            });
        }
        foreach (PlannedCheck item in selected.Checks.Where(item =>
            item.Work.Key.Check is Contents or Consumer))
        {
            CheckSpec check = item.Work;
            await CompleteAsync(check, async () =>
            {
                string directory = archives[check.Key.Target];
                string archive = Path.Combine(directory, "package.tgz");
                string metadata = Path.Combine(directory, check.Key.Check == Contents
                    ? "contents-metadata" : "consumer-metadata");
                Directory.CreateDirectory(metadata);
                await NodePackageArchive.ReadAsync(archive, metadata, token);
                string metadataAuth = Path.Combine(metadata, "auth.npmrc");
                await File.WriteAllTextAsync(metadataAuth, "", token);
                NativeCommandResult value = await ObserveAsync(check, new("pnpm", metadata,
                    ["--dir", metadata, "--ignore-workspace", "--npmrc-auth-file", metadataAuth,
                        "pkg", "get", "--json", "name", "version",
                        "type", "main", "dependencies", "optionalDependencies", "peerDependencies",
                        "peerDependenciesMeta", "bundledDependencies", "bundleDependencies",
                        "devDependencies", "scripts", "engines.runtime", "gypfile"], 30));
                if (!value.Succeeded)
                    return Status(value);
                ValidateMetadata(value.Stdout, check.Package!.ExpectedVersion);
                if (check.Key.Check == Contents)
                    return CheckStatus.Passed;
                string consumer = Path.Combine(directory, "consumer");
                string store = Path.Combine(directory, "store");
                string state = Path.Combine(directory, "state");
                string config = Path.Combine(directory, "config");
                foreach (string path in new[] { consumer, store, state, config })
                    Directory.CreateDirectory(path);
                string auth = Path.Combine(config, "auth.npmrc");
                await File.WriteAllTextAsync(auth, "", token);
                CheckStatus installed = await CommandAsync(check, new("pnpm", consumer,
                    ["--dir", consumer, "--ignore-workspace", "--store-dir", store,
                        "--state-dir", state, "--npmrc-auth-file", auth, "install", archive,
                        "--offline", "--ignore-scripts"], 300));
                if (installed != CheckStatus.Passed)
                    return installed;
                const string assertion = "import { strict as assert } from 'node:assert';"
                    + "import { smokeMessage } from '@hcoona/hcoona-release-smoke-npm';"
                    + "assert.equal(smokeMessage(), 'hcoona-release-smoke-npm');";
                return await CommandAsync(check, new("node", consumer,
                    ["--input-type=module", "--eval", assertion], 30));
            });
        }
        return new(plan.Candidate, selected.Checks.Select(item => results[item.Work.Key]).ToArray(),
            [.. commands], [.. outputs], [.. failures], request.Runtime, runtimeVersions);

        async Task CompleteAsync(CheckSpec check, Func<Task<CheckStatus>> action)
        {
            CheckStatus status;
            if (token.IsCancellationRequested)
                status = CheckStatus.Cancelled;
            else if (check.Prerequisites.Any(key => results[key].Status != CheckStatus.Passed))
                status = CheckStatus.Skipped;
            else
            {
                try
                {
                    status = await action();
                }
                catch (OperationCanceledException)
                {
                    status = CheckStatus.Cancelled;
                }
                catch (Exception exception) when (exception is IOException or
                    InvalidDataException or JsonException or UnauthorizedAccessException or
                    DecoderFallbackException)
                {
                    failures.Add(new(check.Key, exception.Message));
                    status = CheckStatus.Failed;
                }
            }
            results.Add(check.Key, new(plan.Candidate, check.Key, status));
        }

        async Task<NativeCommandResult> ObserveAsync(CheckSpec check, NativeCommand command)
        {
            NativeCommandResult result = await execute(command, token);
            commands.Add(new(check.Key, command, result));
            return result;
        }

        async Task<CheckStatus> CommandAsync(CheckSpec check, NativeCommand command) =>
            Status(await ObserveAsync(check, command));
    }

    private static CheckStatus Status(NativeCommandResult result) => result.Termination switch
    {
        NativeTermination.Cancelled => CheckStatus.Cancelled,
        NativeTermination.TimedOut => CheckStatus.TimedOut,
        _ => result.Succeeded ? CheckStatus.Passed : CheckStatus.Failed,
    };

    internal static CiPlan SelectRuntime(CiPlan plan, string runtime)
    {
        ResultCollector.Collect(plan, []);
        if (plan.Scope != NodeFactsAssembler.Scope || runtime is not ("node22" or "node24"))
            throw new InvalidDataException("Unsupported Node execution scope.");
        var work = plan.Checks.ToDictionary(item => item.Work.Key, item => item.Work);
        var outputIds = new HashSet<(string Unit, string Output)>();
        string? pnpmVersion = null;
        string? node24Version = null;
        foreach (PlannedCheck item in plan.Checks)
        {
            CheckSpec check = item.Work;
            string variant = check.Key.Variant;
            if (!check.Required || check.Runner != "ubuntu-latest" ||
                variant is not ("node22" or "node24") ||
                !check.Dimensions.TryGetValue("node", out string? selector) ||
                selector != (variant == "node22" ? "22.x" : "24.x") ||
                !check.Dimensions.TryGetValue("pnpm", out string? pnpm) || !IsVersion(pnpm) ||
                check.Dimensions.Count != (variant == "node22" ? 2 : 3) ||
                item.QualityPresets is not { Length: > 0 } ||
                item.QualityPresets.Any(preset => preset != Preset))
                throw new InvalidDataException(
                    "Unsupported Node execution quality/runner/variant.");
            pnpmVersion ??= pnpm;
            if (pnpmVersion != pnpm)
                throw new InvalidDataException("Conflicting planned PNPM versions.");
            if (variant == "node24")
            {
                if (!check.Dimensions.TryGetValue("node-version", out string? nodeVersion) ||
                    !IsVersion(nodeVersion) || System.Version.Parse(nodeVersion).Major != 24)
                    throw new InvalidDataException("Missing exact planned Node24 version.");
                node24Version ??= nodeVersion;
                if (node24Version != nodeVersion)
                    throw new InvalidDataException("Conflicting planned Node24 versions.");
            }
            if (check.Key.Check is Build or Test)
            {
                ImpactPlanner.ValidatePath(check.Key.Target);
                if (check.Package is not null || check.Prerequisites.Length != 0 ||
                    !work.ContainsKey(new(check.Key.Target, Build, "node22")) ||
                    !work.ContainsKey(new(check.Key.Target, Test, "node22")) ||
                    !work.ContainsKey(new(check.Key.Target, Build, "node24")) ||
                    !work.ContainsKey(new(check.Key.Target, Test, "node24")))
                    throw new InvalidDataException("Unresolved complete Node source checks.");
                continue;
            }
            if (variant != "node24" ||
                check.Key.Check is not (Pack or Contents or Consumer) || check.Package is null)
                throw new InvalidDataException("Unsupported Node execution check.");
            PackageTarget package = check.Package;
            string target = "release/" + package.Unit + "/" + package.Build;
            var pack = new CheckKey(target, Pack, variant);
            CheckKey prerequisite = check.Key.Check == Pack
                ? new(package.Directory, Build, variant) : pack;
            if (check.Key.Target != target || package.Definition != "node/npm-package-v1" ||
                package.PublishDirectory is not null ||
                package.EntryPoint != package.Directory + "/package.json" ||
                package.Outputs is not [{ Role: "primary-package", Kind: "npm-tarball" }] ||
                !check.Prerequisites.SequenceEqual([prerequisite]))
                throw new InvalidDataException("Unsupported complete Node package association.");
            if (check.Key.Check == Pack &&
                !outputIds.Add((package.Unit, package.Outputs.Single().Id)))
                throw new InvalidDataException("Conflicting unit-wide Node output identity.");
            foreach (string operation in new[] { Pack, Contents, Consumer })
                if (!work.TryGetValue(new(target, operation, variant), out CheckSpec? related) ||
                    !ImpactPlanner.SamePackage(package, related.Package))
                    throw new InvalidDataException(
                        "Incomplete or conflicting Node package checks.");
        }
        return plan with { Checks = plan.Checks.Where(item =>
            item.Work.Key.Variant == runtime).ToArray() };
    }

    private static bool IsVersion(string value) =>
        System.Version.TryParse(value, out System.Version? version) && version.Build >= 0 &&
        version.Revision == -1 && version.ToString() == value;

    private static string ReadVersion(NativeCommandResult result, string tool)
    {
        string value = result.Stdout.Trim();
        if (tool == "node" && value.StartsWith('v'))
            value = value[1..];
        else if (tool == "node")
            throw new InvalidDataException("Malformed native Node version.");
        if (!result.Succeeded || !IsVersion(value))
            throw new InvalidDataException("Unavailable native runtime version: " + tool);
        return value;
    }

    private static void ValidateMetadata(string text, string version)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement metadata = document.RootElement;
        if (metadata.ValueKind != JsonValueKind.Object ||
            metadata.EnumerateObject().Select(field => field.Name).Distinct(StringComparer.Ordinal)
                .Count() != metadata.EnumerateObject().Count())
            throw new InvalidDataException("Native packed metadata must be an unambiguous object.");
        Require("name", PackageName);
        Require("version", version);
        Require("type", "module");
        Require("main", "./dist/index.js");
        foreach (string field in new[] { "dependencies", "optionalDependencies", "peerDependencies",
            "peerDependenciesMeta" })
            if (metadata.TryGetProperty(field, out JsonElement value) &&
                (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any()))
                throw new InvalidDataException(
                    "Unsupported installed npm dependency shape: " + field);
        foreach (string field in new[] { "bundledDependencies", "bundleDependencies" })
            if (metadata.TryGetProperty(field, out JsonElement value) &&
                value.ValueKind != JsonValueKind.False &&
                (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 0))
                throw new InvalidDataException("Unsupported bundled npm dependency shape.");
        if (metadata.TryGetProperty("devDependencies", out JsonElement development) &&
            development.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported npm development dependency metadata.");
        if (metadata.TryGetProperty("engines.runtime", out _) ||
            (metadata.TryGetProperty("gypfile", out JsonElement gypfile) &&
                gypfile.ValueKind != JsonValueKind.False))
            throw new InvalidDataException("Unsupported npm runtime/native-build metadata.");
        if (metadata.TryGetProperty("scripts", out JsonElement scripts))
        {
            if (scripts.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Unsupported packed npm script metadata.");
            foreach (string script in new[] { "preinstall", "install", "postinstall" })
                if (scripts.TryGetProperty(script, out _))
                    throw new InvalidDataException(
                        "Unsupported installed npm lifecycle: " + script);
        }

        void Require(string field, string expected)
        {
            if (!metadata.TryGetProperty(field, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String || value.GetString() != expected)
                throw new InvalidDataException("Unexpected native packed npm " + field + ".");
        }
    }

    private static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Node execution paths must be absolute.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool Within(string path, string directory) =>
        path.StartsWith(Path.EndsInDirectorySeparator(directory)
            ? directory : directory + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool HasLinkedAncestor(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null;
            directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;
        return false;
    }
}
