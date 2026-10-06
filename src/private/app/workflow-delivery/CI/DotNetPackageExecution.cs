using System.Text.Json;
using System.Xml;
using Microsoft.Build.Evaluation;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record DotNetPackageRunResult(string Candidate, CheckResult[] Results,
    DotNetCommandObservation[] Commands, DotNetOriginalPackage[] Packages,
    DotNetNativeOutput[] ConsumerOutputs, DotNetCheckFailure[] Failures);

// Exactly the adopted package partition: Pack, contents, external consumption.
internal static class DotNetPackageExecution
{
    private static readonly string[] Projections = ["PackageId", "NuGetPackageVersion",
        "PackageVersion", "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion"];

    internal static Task<DotNetPackageRunResult> RunAsync(CiPlan plan,
        DotNetRunRequest request, CancellationToken token) =>
        RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<DotNetPackageRunResult> RunAsync(CiPlan plan,
        DotNetRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string checkout = Absolute(request.Checkout);
        string scratch = Absolute(request.Scratch);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            Within(scratch, checkout) || Within(checkout, scratch) ||
            Directory.EnumerateFileSystemEntries(scratch).Any() || request.DeadlineSeconds <= 0)
            throw new InvalidDataException(
                "Native package execution requires fresh external scratch.");
        ResultCollector.Collect(plan, []);
        if (plan.Scope != DotNetFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported native package execution scope.");
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException(
                "Native package execution requires its exact candidate.");
        GitMaterialization materialization = await GitMaterialization.BindAsync(checkout,
            revision, token);
        var facts = new Dictionary<CheckKey, (DotNetGraphNode Node,
            Dictionary<string, string> Values)>();
        foreach (PlannedCheck item in plan.Checks)
        {
            if (!item.Origins.SequenceEqual([CheckOrigin.Preset]) ||
                !item.QualityPresets.SequenceEqual([DotNetPackageChecks.Preset]))
                throw new InvalidDataException(
                    "Unsupported selected native package quality contract.");
            PackageTarget subject = item.Work.Package ??
                throw new InvalidDataException("Missing selected native package association.");
            string declaration = await materialization.ReadOptionalTextAsync(subject.Declaration,
                token) ?? throw new InvalidDataException(
                    "Missing committed release-unit declaration.");
            if (!revision.Entries.Any(entry => entry.Path == subject.EntryPoint &&
                    entry.ObjectType == "blob" && entry.Mode is "100644" or "100755"))
                throw new InvalidDataException(
                    "Selected native package project is not committed source.");
            facts.Add(item.Work.Key, DotNetPackageChecks.Read(item.Work, checkout,
                ReleaseUnitDeclarationReader.Read(declaration, subject.Declaration)));
        }
        foreach (var group in plan.Checks.GroupBy(item =>
                     (item.Work.Key.Target, item.Work.Key.Variant)))
            if (group.Count() != 3 || group.Select(item => item.Work.Key.Check).ToHashSet()
                .SetEquals([DotNetPackageChecks.Pack, DotNetPackageChecks.Contents,
                    DotNetPackageChecks.Consumer]) is false ||
                group.Any(item => !item.Work.Dimensions.OrderBy(pair => pair.Key)
                    .SequenceEqual(group.First().Work.Dimensions.OrderBy(pair => pair.Key)) ||
                    !ImpactPlanner.SamePackage(item.Work.Package, group.First().Work.Package)))
                throw new InvalidDataException(
                    "Incomplete or conflicting native package obligations.");
        var results = new Dictionary<CheckKey, CheckResult>();
        var commands = new List<DotNetCommandObservation>();
        var packages = new List<DotNetOriginalPackage>();
        var outputs = new List<DotNetNativeOutput>();
        var failures = new List<DotNetCheckFailure>();
        int phase = 0;
        foreach (PlannedCheck pack in plan.Checks.Where(item =>
                     item.Work.Key.Check == DotNetPackageChecks.Pack))
        {
            string root = Path.Combine(scratch, "package-" + phase++);
            DotNetOriginalPackage? original = null;
            Dictionary<string, string>? post = null;
            foreach (string kind in new[] { DotNetPackageChecks.Pack, DotNetPackageChecks.Contents,
                         DotNetPackageChecks.Consumer })
            {
                CheckSpec check = plan.Checks.Single(item =>
                    item.Work.Key == (pack.Work.Key with { Check = kind })).Work;
                CheckStatus status;
                if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
                else if (check.Prerequisites.Any(key => results[key].Status != CheckStatus.Passed))
                    status = CheckStatus.Skipped;
                else try
                    {
                        if (kind == DotNetPackageChecks.Pack)
                        {
                            string destination = Directory.CreateDirectory(Path.Combine(root,
                                "archives")).FullName;
                            string response = Path.Combine(root, "pack.json");
                            var storage = new Dictionary<string, string>(StringComparer.Ordinal)
                            { ["PackageOutputPath"] = ProjectCollection.Escape(destination) };
                            var (node, expected) = facts[check.Key];
                            await RunAsync(DotNetPackageChecks.Query(node, checkout, response,
                                "Pack,_GetOutputItemsFromPack", "Pack,_GetOutputItemsFromPack",
                                request.DeadlineSeconds, DotNetChecks.Properties(storage)), token);
                            using JsonDocument document = await DotNetPackageChecks.ResponseAsync(
                                response, token);
                            DotNetPackageChecks.Target(document.RootElement, "Pack");
                            post = DotNetPackageChecks.Properties(document.RootElement);
                            DotNetPackageChecks.Validate(node, post);
                            if (Projections.Any(name => post[name] != expected[name]))
                                throw new InvalidDataException(
                                    "Native Pack recomputation differs from NBGV expectations.");
                            original = Archive(check.Package!, document.RootElement,
                                destination, post);
                            packages.Add(original);
                        }
                        else if (kind == DotNetPackageChecks.Contents)
                        {
                            DotNetPackageArchive.RequireOriginal(original!);
                            DotNetPackageArchive.Contents(original!.Path, post!);
                        }
                        else outputs.Add(new(check.Key, await DotNetPackageConsumer.RunAsync(
                            original!, post!, Path.Combine(root, "probe"), request.DeadlineSeconds,
                            RunAsync, token)));
                        token.ThrowIfCancellationRequested();
                        status = CheckStatus.Passed;
                    }
                    catch (NativePackageFailure failure)
                    { status = failure.Status; failures.Add(new(check.Key, failure.Message)); }
                    catch (OperationCanceledException) { status = CheckStatus.Cancelled; }
                    catch (Exception exception) when (exception is IOException or
                        InvalidDataException or JsonException or UnauthorizedAccessException or
                        ArgumentException or FormatException or BadImageFormatException
                            or XmlException)
                    {
                        status = CheckStatus.Failed;
                        failures.Add(new(check.Key, exception.Message));
                    }
                results.Add(check.Key, new(plan.Candidate, check.Key, status));

                async Task<NativeCommandResult> RunAsync(NativeCommand command,
                    CancellationToken cancellation)
                {
                    NativeCommandResult result = await execute(command, cancellation);
                    commands.Add(new(check.Key, command, result));
                    if (!result.Succeeded)
                        throw new NativePackageFailure(result.Termination switch
                        {
                            NativeTermination.Cancelled => CheckStatus.Cancelled,
                            NativeTermination.TimedOut => CheckStatus.TimedOut,
                            _ => CheckStatus.Failed,
                        }, result.Error ??
                            $"Native command {result.Termination}, exit {result.ExitCode}.");
                    return result;
                }
            }
        }
        return new(plan.Candidate, plan.Checks.Select(item => results[item.Work.Key]).ToArray(),
            commands.ToArray(), packages.ToArray(), outputs.ToArray(), failures.ToArray());
    }

    internal static DotNetOriginalPackage Archive(PackageTarget subject, JsonElement root,
        string destination, Dictionary<string, string> expected)
    {
        JsonElement receipt = DotNetPackageChecks.Target(root, "_GetOutputItemsFromPack");
        if (!receipt.TryGetProperty("Items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Missing native package output items.");
        var archives = new HashSet<string>(OperatingSystem.IsWindows() ?
            StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("FullPath", out JsonElement fullPath) ||
                fullPath.ValueKind != JsonValueKind.String ||
                !Path.IsPathFullyQualified(fullPath.GetString()!))
                throw new InvalidDataException("Invalid native package output coordinate.");
            string path = Path.GetFullPath(fullPath.GetString()!);
            string extension = Path.GetExtension(path);
            if (extension.Equals(".nuspec", StringComparison.OrdinalIgnoreCase)) continue;
            if (!extension.Equals(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                !Within(path, destination) || !archives.Add(path))
                throw new InvalidDataException("Undeclared or ambiguous native package output.");
            DotNetPackageConsumer.RequireFile(path);
        }
        var actual = new HashSet<string>(archives.Comparer);
        foreach (string path in Directory.EnumerateFileSystemEntries(destination))
        {
            DotNetPackageConsumer.RequireFile(path);
            if (Path.GetExtension(path).Equals(".nupkg", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".snupkg", StringComparison.OrdinalIgnoreCase))
                actual.Add(Path.GetFullPath(path));
        }
        if (archives.Count != 1 || !actual.SetEquals(archives))
            throw new InvalidDataException(
                "The complete original native archive set is missing or undeclared.");
        string archive = archives.Single();
        DotNetPackageArchive.RequireIdentity(DotNetPackageArchive.Identity(archive), expected);
        PackageOutput output = subject.Outputs.Single();
        return new(subject.Unit, subject.Build, output.Id, output.Role, output.Kind, archive,
            new FileInfo(archive).Length, DotNetPackageArchive.Digest(archive));
    }

    private sealed class NativePackageFailure(CheckStatus status, string message) :
        Exception(message)
    { internal CheckStatus Status { get; } = status; }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) :
        throw new InvalidDataException("Native package paths must be absolute.");

    private static bool Within(string child, string parent) => child.Equals(parent,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ||
        child.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
