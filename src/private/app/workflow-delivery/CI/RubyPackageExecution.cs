using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record RubyPackageRequest(string Checkout, string Scratch, RubyFactTools Facts,
    string Gem, string Nbgv);
internal sealed record RubyPackageCommand(CheckKey Check, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record RubyOriginalGem(PackageTarget Subject, string Path, long Length,
    string Sha256);
internal sealed record RubyPackageFailure(CheckKey Check, string Error);
internal sealed record RubyPackageRun(CheckResult[] Results, RubyPackageCommand[] Commands,
    RubyOriginalGem[] Gems, RubyPackageFailure[] Failures);

// Finite adopted gem orchestration. RubyGems owns build/archive/install semantics.
internal static class RubyPackageExecution
{
    internal static Task<RubyPackageRun> RunAsync(CiPlan plan, RubyPackageRequest request,
        CancellationToken token) => RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyPackageRun> RunAsync(CiPlan plan, RubyPackageRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ResultCollector.Collect(plan, []);
        foreach (string path in new[] { request.Checkout, request.Scratch, request.Gem,
                     request.Nbgv })
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidDataException(
                    "Ruby gem execution requires prepared native paths.");
        if (plan.Scope != RubyFactsAssembler.Scope || !Directory.Exists(request.Scratch) ||
            Directory.EnumerateFileSystemEntries(request.Scratch).Any())
            throw new InvalidDataException("Ruby gem execution requires fresh external scratch.");
        string checkoutRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Checkout));
        string scratchRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Scratch));
        if (scratchRoot == checkoutRoot || scratchRoot.StartsWith(
                checkoutRoot + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidDataException("Gem consumer scratch must be outside the checkout.");
        GitRevision revision = await new GitReader(checkoutRoot).ReadAsync(plan.Candidate, token);
        GitMaterialization checkout = await GitMaterialization.BindAsync(checkoutRoot, revision,
            token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException("Ruby gem plan requires its complete native candidate.");
        CheckSpec[] checks = plan.Checks.Where(check => RubyPackageChecks.Kinds.Contains(
            check.Work.Key.Check, StringComparer.Ordinal)).Select(check => check.Work).ToArray();
        foreach (CheckSpec check in checks)
        {
            PackageTarget subject = check.Package ?? throw new InvalidDataException(
                "Ruby gem check lacks its native package subject.");
            if (subject.Definition != RubyPackageChecks.Build ||
                subject.Outputs is not [{ Role: "primary-package", Kind: "ruby-gem" }])
                throw new InvalidDataException("Unsupported Ruby gem package outputs.");
            if (RubyPackageChecks.Kinds.Any(kind => !checks.Any(other =>
                    other.Key == (check.Key with { Check = kind }) &&
                    ImpactPlanner.SamePackage(other.Package, subject))))
                throw new InvalidDataException("Incomplete adopted Ruby gem check set.");
        }
        var results = new Dictionary<CheckKey, CheckResult>();
        var commands = new List<RubyPackageCommand>();
        var gems = new List<RubyOriginalGem>();
        var failures = new List<RubyPackageFailure>();
        int ordinal = 0;
        foreach (CheckSpec build in checks.Where(check =>
                     check.Key.Check == RubyPackageChecks.Build))
        {
            PackageTarget subject = build.Package!;
            string state = Directory.CreateDirectory(Path.Combine(scratchRoot,
                "gem-" + ordinal++)).FullName;
            var operations = new RubyPackageOperations(request.Facts, request.Gem,
                request.Nbgv, state);
            RubyGemArchive? original = null;
            foreach (string kind in RubyPackageChecks.Kinds)
            {
                CheckSpec check = checks.Single(item =>
                    item.Key == (build.Key with { Check = kind }));
                CheckStatus status;
                if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
                else if (check.Prerequisites.Any(key =>
                             !results.TryGetValue(key, out var previous) ||
                             previous.Status != CheckStatus.Passed)) status = CheckStatus.Skipped;
                else try
                    {
                        if (kind == RubyPackageChecks.Build)
                        {
                            original = await operations.BuildAsync(checkout, subject.Directory,
                                subject.EntryPoint, subject.ExpectedVersion, ExecuteAsync, token);
                            gems.Add(new(subject, original.Path, original.Length, original.Sha256));
                        }
                        else if (kind == RubyPackageChecks.Contents)
                            await operations.InspectAsync(original!, subject.ExpectedVersion,
                                ExecuteAsync, token);
                        else
                            await operations.ConsumeAsync(original!, subject.ExpectedVersion,
                                ExecuteAsync, token);
                        status = CheckStatus.Passed;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        status = CheckStatus.Cancelled;
                    }
                    catch (Exception error) when (error is IOException or
                        InvalidDataException or InvalidOperationException)
                    {
                        failures.Add(new(check.Key, error.Message));
                        status = CheckStatus.Failed;
                    }
                results.Add(check.Key, new(plan.Candidate, check.Key, status));

                async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
                    CancellationToken cancellation)
                {
                    NativeCommandResult result = await execute(command, cancellation);
                    commands.Add(new(check.Key, command, result));
                    return result;
                }
            }
        }
        return new(results.Values.ToArray(), commands.ToArray(), gems.ToArray(),
            failures.ToArray());
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
