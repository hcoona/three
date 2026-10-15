using WorkflowDelivery.Ecosystems;
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
        string checkout = OwnedPath.Absolute(request.Checkout, PathMessage);
        string scratch = OwnedPath.Absolute(request.Scratch, PathMessage);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            OwnedPath.Overlap(scratch, checkout) ||
            Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException("Node execution requires fresh external scratch.");
        if (scratch.Contains("%s", StringComparison.Ordinal) ||
            scratch.Contains("%v", StringComparison.Ordinal))
            throw new InvalidDataException(
                "Node scratch cannot contain native pack output markers.");
        if (!GitReader.IsCommitId(plan.Candidate))
            throw new InvalidDataException(
                "Node execution requires the exact native commit identity.");
        await GitMaterialization.BindAsync(checkout, plan.Candidate, token);
        if (plan.Checks.Length == 0)
            return new(plan.Candidate, [], [], [], [], request.Runtime, null);
        NativeCommandResult node = await execute(new("node", checkout, ["--version"], 30), token);
        NativeCommandResult pnpm = await execute(new("pnpm", checkout, ["--version"], 30), token);
        NodeRuntimeReadback runtimeVersions = ReadRuntime(selected, node, pnpm);
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
                CheckStatus status = await CommandAsync(check,
                    NpmPackageOperations.Pack(checkout, package.Directory, archive));
                if (status != CheckStatus.Passed)
                    return status;
                (long size, string digest) = await NpmPackageOperations.OriginalAsync(archive,
                    NodePackageArchive.CompressedLimit, token);
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
                NativeCommandResult value = await ObserveAsync(check,
                    await NpmPackageOperations.MetadataAsync(archive, metadata, token));
                if (!value.Succeeded)
                    return Status(value);
                AdoptedNpmPackage.ValidateMetadata(value.Stdout, check.Package!.ExpectedVersion);
                if (check.Key.Check == Contents)
                    return CheckStatus.Passed;
                CheckStatus installed = await CommandAsync(check,
                    await NpmPackageOperations.InstallAsync(archive, directory, token));
                if (installed != CheckStatus.Passed)
                    return installed;
                return await CommandAsync(check, AdoptedNpmPackage.Import(directory));
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

    // Pure check of the native runtime answers against the selected runtime context.
    internal static NodeRuntimeReadback ReadRuntime(CiPlan selected, NativeCommandResult node,
        NativeCommandResult pnpm)
    {
        string nodeVersion = ReadVersion(node, "node");
        string pnpmVersion = ReadVersion(pnpm, "pnpm");
        Dictionary<string, string> dimensions = selected.Checks[0].Work.Dimensions;
        if (pnpmVersion != dimensions["pnpm"] ||
            (dimensions.TryGetValue("node-version", out string? exact) ? nodeVersion != exact :
                System.Version.Parse(nodeVersion).Major.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) +
                        ".x" != dimensions["node"]))
            throw new InvalidDataException(
                "Native runtime differs from the selected Node context.");
        return new(nodeVersion, pnpmVersion, node, pnpm);
    }

    internal static CheckStatus Status(NativeCommandResult result) => result.Termination switch
    {
        NativeTermination.Cancelled => CheckStatus.Cancelled,
        NativeTermination.TimedOut => CheckStatus.TimedOut,
        _ => result.Succeeded ? CheckStatus.Passed : CheckStatus.Failed,
    };

    private static bool IsRuntime(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, "^node[1-9][0-9]*$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static CiPlan SelectRuntime(CiPlan plan, string runtime)
    {
        ResultCollector.Collect(plan, []);
        if (plan.Scope != NodeFactsAssembler.Scope || !IsRuntime(runtime))
            throw new InvalidDataException("Unsupported Node execution scope.");
        var work = plan.Checks.ToDictionary(item => item.Work.Key, item => item.Work);
        var outputIds = new HashSet<(string Unit, string Output)>();
        string? pnpmVersion = null;
        var contexts = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        string[] sourceVariants = plan.Checks.Where(item => item.Work.Key.Check is Build or Test)
            .Select(item => item.Work.Key.Variant).Distinct(StringComparer.Ordinal).ToArray();
        foreach (PlannedCheck item in plan.Checks)
        {
            CheckSpec check = item.Work;
            string variant = check.Key.Variant;
            if (!check.Required || check.Runner != "ubuntu-latest" ||
                !IsRuntime(variant) ||
                !check.Dimensions.TryGetValue("node", out string? selector) ||
                selector != variant[4..] + ".x" ||
                !check.Dimensions.TryGetValue("pnpm", out string? pnpm) || !IsVersion(pnpm) ||
                check.Dimensions.Count != (check.Dimensions.ContainsKey("node-version") ? 3 : 2) ||
                item.QualityPresets is not { Length: > 0 } ||
                item.QualityPresets.Any(preset => preset != Preset))
                throw new InvalidDataException(
                    "Unsupported Node execution quality/runner/variant.");
            pnpmVersion ??= pnpm;
            if (pnpmVersion != pnpm)
                throw new InvalidDataException("Conflicting planned PNPM versions.");
            if (check.Dimensions.TryGetValue("node-version", out string? nodeVersion) &&
                (!IsVersion(nodeVersion) ||
                 System.Version.Parse(nodeVersion).Major.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + ".x" != selector))
                throw new InvalidDataException("Missing exact planned packaging Node version.");
            if (contexts.TryGetValue(variant, out Dictionary<string, string>? previous) &&
                (previous.Count != check.Dimensions.Count || previous.Any(pair =>
                    !check.Dimensions.TryGetValue(pair.Key, out string? value) ||
                        value != pair.Value)))
                throw new InvalidDataException("Conflicting planned Node runtime context.");
            contexts[variant] = check.Dimensions;
            if (check.Key.Check is Build or Test)
            {
                ImpactPlanner.ValidatePath(check.Key.Target);
                if (check.Package is not null || check.Prerequisites.Length != 0 ||
                    sourceVariants.Any(sourceVariant =>
                        !work.ContainsKey(new(check.Key.Target, Build, sourceVariant)) ||
                        !work.ContainsKey(new(check.Key.Target, Test, sourceVariant))))
                    throw new InvalidDataException("Unresolved complete Node source checks.");
                continue;
            }
            if (!check.Dimensions.ContainsKey("node-version") ||
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
        if (plan.Checks.Length != 0 && !contexts.ContainsKey(runtime))
            throw new InvalidDataException("Runtime is absent from the original Node plan.");
        return plan with
        {
            Checks = plan.Checks.Where(item =>
            item.Work.Key.Variant == runtime).ToArray()
        };
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

    private const string PathMessage = "Node execution paths must be absolute.";
}
