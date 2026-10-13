using System.Security;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record DotNetRunRequest(string Checkout, string Scratch, int DeadlineSeconds);
internal sealed record DotNetCommandObservation(CheckKey Key, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record DotNetNativeOutput(CheckKey Key, string Path);
internal sealed record DotNetCheckFailure(CheckKey Key, string Error);
internal sealed record DotNetBatchCommandObservation(CheckKey[] Keys, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record DotNetRunResult(string Candidate, CheckResult[] Results,
    DotNetCommandObservation[] Commands, DotNetNativeOutput[] Outputs,
    DotNetCheckFailure[] Failures)
{
    public DotNetBatchCommandObservation? BatchBuild { get; init; }
}

// MSBuild owns the selected build batch and reference scheduling; MTP/VSTest own tests.
internal static class DotNetExecution
{
    internal static Task<DotNetRunResult> RunAsync(CiPlan plan, DotNetRunRequest request,
        CancellationToken token) => RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<DotNetRunResult> RunAsync(CiPlan plan, DotNetRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string checkout = Absolute(request.Checkout);
        string scratch = Absolute(request.Scratch);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            Within(scratch, checkout) || Within(checkout, scratch) ||
            Directory.EnumerateFileSystemEntries(scratch).Any() || request.DeadlineSeconds <= 0)
            throw new InvalidDataException(
                "Native .NET execution requires fresh external scratch.");
        ResultCollector.Collect(plan, []);
        if (plan.Scope != DotNetFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported native .NET execution scope.");
        Dictionary<CheckKey, DotNetGraphNode> nodes = ReadChecks(plan, checkout);
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException("Native .NET execution requires its exact candidate.");
        await GitMaterialization.BindAsync(checkout, revision, token);
        foreach (CheckKey key in nodes.Keys)
            if (!revision.Entries.Any(entry => entry.Path == key.Target &&
                    entry.ObjectType == "blob" && entry.Mode is "100644" or "100755"))
                throw new InvalidDataException("Selected native project is not committed source.");
        var results = new Dictionary<CheckKey, CheckResult>();
        var commands = new List<DotNetCommandObservation>();
        var outputs = new List<DotNetNativeOutput>();
        var failures = new List<DotNetCheckFailure>();
        int phase = 0;
        PlannedCheck[] builds = plan.Checks.Where(item =>
            item.Work.Key.Check == DotNetChecks.Build).ToArray();
        DotNetBatchCommandObservation? batch = null;
        if (builds.Length != 0) await BuildAsync();
        foreach (PlannedCheck test in plan.Checks.Where(item =>
                     item.Work.Key.Check == DotNetChecks.Test))
            await CompleteAsync(test.Work);
        return new(plan.Candidate, plan.Checks.Select(item => results[item.Work.Key]).ToArray(),
            commands.ToArray(), outputs.ToArray(), failures.ToArray()) { BatchBuild = batch };

        async Task BuildAsync()
        {
            CheckStatus fallback = CheckStatus.Cancelled;
            Dictionary<(string Project, string Globals), DotNetBuildEntryResult>? entries = null;
            string? error = null;
            try
            {
                token.ThrowIfCancellationRequested();
                string input = Path.Combine(scratch, "build-request.json");
                string response = Path.Combine(scratch, "build-response.json");
                string wrapper = Path.Combine(scratch, "build.proj");
                var nativeRequest = new DotNetBuildRequest(builds.Select(build =>
                    nodes[build.Work.Key].Identity).ToArray(), response);
                await File.WriteAllTextAsync(input, JsonSerializer.Serialize(nativeRequest,
                    TransferJson.Default.DotNetBuildRequest), token);
                string assembly = SecurityElement.Escape(
                    typeof(DotNetBuildTask).Assembly.Location)!;
                string requestFile = SecurityElement.Escape(input)!;
                await File.WriteAllTextAsync(wrapper, $"""
                    <Project>
                      <UsingTask TaskName="WorkflowDelivery.CI.DotNetBuildTask"
                        AssemblyFile="{assembly}" />
                      <Target Name="BuildSelected">
                        <WorkflowDelivery.CI.DotNetBuildTask RequestFile="{requestFile}" />
                      </Target>
                    </Project>
                    """, token);
                var command = new NativeCommand("dotnet", checkout,
                    ["msbuild", wrapper, "-nologo", "-noAutoResponse", "-target:BuildSelected"],
                    request.DeadlineSeconds);
                NativeCommandResult native = await execute(command, token);
                batch = new(builds.Select(build => build.Work.Key).ToArray(), command, native);
                fallback = native.Termination switch
                {
                    NativeTermination.Cancelled => CheckStatus.Cancelled,
                    NativeTermination.TimedOut => CheckStatus.TimedOut,
                    _ => CheckStatus.Failed,
                };
                // An exited aggregate can contain independent successful native entries.
                if (native.Termination == NativeTermination.Exited)
                {
                    await using var file = File.OpenRead(response);
                    DotNetBuildResponse projection = await JsonSerializer.DeserializeAsync(file,
                        TransferJson.Default.DotNetBuildResponse, token)
                        ?? throw new InvalidDataException("Missing native build response.");
                    if (projection.Entries is null)
                        throw new InvalidDataException("Missing native build entries.");
                    entries = new(new DotNetRepositoryReader.NativeIdentityComparer());
                    foreach (DotNetBuildEntryResult entry in projection.Entries)
                        if (entry is null || !entries.TryAdd(
                            DotNetRepositoryReader.Key(entry.Identity), entry))
                            throw new InvalidDataException("Ambiguous native build identity.");
                    if (entries.Count != builds.Length || builds.Any(build =>
                        !entries.ContainsKey(DotNetRepositoryReader.Key(
                            nodes[build.Work.Key].Identity))))
                        throw new InvalidDataException(
                            "Different selected native build identities.");
                }
                else error = native.Error ??
                    $"Native build {native.Termination}, exit {native.ExitCode}.";
            }
            catch (OperationCanceledException) { fallback = CheckStatus.Cancelled; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                JsonException or UnauthorizedAccessException)
            {
                entries = null;
                fallback = CheckStatus.Failed;
                error = exception.Message;
            }
            foreach (PlannedCheck build in builds)
            {
                CheckKey key = build.Work.Key;
                CheckStatus status = fallback;
                string? failure = error;
                if (entries is not null)
                {
                    DotNetBuildEntryResult entry = entries[
                        DotNetRepositoryReader.Key(nodes[key].Identity)];
                    try
                    {
                        if (entry.Build != "Success" || entry.GetTargetPath != "Success" ||
                            entry.Outputs is null || entry.Outputs.Length != 1 ||
                            entry.Outputs[0] is not { } output ||
                            string.IsNullOrWhiteSpace(output.Identity))
                            throw new InvalidDataException(
                                "Missing or failed native Build/GetTargetPath.");
                        outputs.Add(new(key, RegularOutput(entry.Outputs[0].FullPath)));
                        status = CheckStatus.Passed;
                    }
                    catch (Exception exception) when (exception is IOException or
                        InvalidDataException or UnauthorizedAccessException)
                    {
                        status = CheckStatus.Failed;
                        failure = exception.Message;
                    }
                }
                results.Add(key, new(plan.Candidate, key, status));
                if (status != CheckStatus.Passed)
                    failures.Add(new(key, failure ?? "Native build did not complete."));
            }
        }

        async Task CompleteAsync(CheckSpec check)
        {
            CheckStatus status;
            if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
            else if (check.Prerequisites.Any(key => results[key].Status != CheckStatus.Passed))
                status = CheckStatus.Skipped;
            else
            {
                try
                {
                    DotNetGraphNode node = nodes[check.Key];
                    string testTarget = node.TestCapability == "MTP"
                        ? "InvokeTestingPlatform" : "VSTest";
                    string targets = "Build,GetTargetPath," + testTarget;
                    string queried = "GetTargetPath," + testTarget;
                    string response = Path.Combine(scratch, "phase-" + phase++ + ".json");
                    var command = new NativeCommand("dotnet", checkout,
                        ["msbuild", node.Identity.Project, "-nologo", "-noAutoResponse",
                            "-target:" + targets, "-getTargetResult:" + queried,
                            "-getResultOutputFile:" + response,
                            .. DotNetChecks.Properties(node.Identity.Globals)],
                        request.DeadlineSeconds);
                    NativeCommandResult native = await execute(command, token);
                    commands.Add(new(check.Key, command, native));
                    status = native.Termination switch
                    {
                        NativeTermination.Cancelled => CheckStatus.Cancelled,
                        NativeTermination.TimedOut => CheckStatus.TimedOut,
                        _ => native.Succeeded ? CheckStatus.Passed : CheckStatus.Failed,
                    };
                    if (status == CheckStatus.Passed)
                        outputs.Add(new(check.Key, await ReadOutputAsync(response,
                            testTarget, token)));
                    else failures.Add(new(check.Key, native.Error ??
                        $"Native command {native.Termination}, exit {native.ExitCode}."));
                }
                catch (OperationCanceledException) { status = CheckStatus.Cancelled; }
                catch (Exception exception) when (exception is IOException or
                    InvalidDataException or JsonException or UnauthorizedAccessException)
                {
                    failures.Add(new(check.Key, exception.Message));
                    status = CheckStatus.Failed;
                }
            }
            results.Add(check.Key, new(plan.Candidate, check.Key, status));
        }
    }

    internal static Dictionary<CheckKey, DotNetGraphNode> ReadChecks(CiPlan plan, string checkout)
    {
        var nodes = new Dictionary<CheckKey, DotNetGraphNode>();
        foreach (PlannedCheck item in plan.Checks)
        {
            if (!item.Origins.SequenceEqual([CheckOrigin.NativeRetained]) ||
                item.QualityPresets.Length != 0)
                throw new InvalidDataException("Unsupported selected .NET quality contract.");
            nodes.Add(item.Work.Key, DotNetChecks.Read(item.Work, checkout));
        }
        foreach (var identity in plan.Checks.GroupBy(item =>
                     (item.Work.Key.Target, item.Work.Key.Variant)))
        {
            PlannedCheck build = identity.SingleOrDefault(item =>
                item.Work.Key.Check == DotNetChecks.Build) ??
                throw new InvalidDataException("Missing native Build obligation.");
            CheckSpec[] complete = DotNetChecks.Expand(build.Work.Key.Target,
                nodes[build.Work.Key]);
            if (identity.Count() != complete.Length || complete.Any(check =>
                    !nodes.TryGetValue(check.Key, out DotNetGraphNode? node) ||
                    node.Dimension != nodes[build.Work.Key].Dimension ||
                    node.TestCapability != nodes[build.Work.Key].TestCapability))
                throw new InvalidDataException("Incomplete selected native identity obligations.");
        }
        return nodes;
    }

    private static async Task<string> ReadOutputAsync(string response, string obligation,
        CancellationToken token)
    {
        await using var file = File.OpenRead(response);
        using JsonDocument document = await JsonDocument.ParseAsync(file,
            cancellationToken: token);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("TargetResults", out JsonElement targets) ||
            targets.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing native target results.");
        JsonElement output = Required("GetTargetPath");
        Required(obligation);
        if (!output.TryGetProperty("Items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != 1)
            throw new InvalidDataException("Ambiguous or missing native GetTargetPath output.");
        JsonElement item = items[0];
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("Identity", out JsonElement identity) ||
            identity.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(identity.GetString()) ||
            !item.TryGetProperty("FullPath", out JsonElement fullPath) ||
            fullPath.ValueKind != JsonValueKind.String ||
            !Path.IsPathFullyQualified(fullPath.GetString()!))
            throw new InvalidDataException("Invalid native GetTargetPath coordinate.");
        return RegularOutput(fullPath.GetString()!);

        JsonElement Required(string target)
        {
            if (!targets.TryGetProperty(target, out JsonElement result) ||
                result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("Result", out JsonElement verdict) ||
                verdict.ValueKind != JsonValueKind.String || verdict.GetString() != "Success")
                throw new InvalidDataException("Missing or failed native target: " + target);
            return result;
        }
    }

    private static string RegularOutput(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Invalid native GetTargetPath coordinate.");
        path = Path.GetFullPath(path);
        if (!File.Exists(path) || (File.GetAttributes(path) &
            (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Native GetTargetPath is not a regular file.");
        return path;
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) :
        throw new InvalidDataException("Native .NET paths must be absolute.");

    private static bool Within(string child, string parent) => child.Equals(parent,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
            StringComparison.Ordinal) ||
        child.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
