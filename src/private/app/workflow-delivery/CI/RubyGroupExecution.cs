using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record RubyGroupRunRequest(string Checkout, string Scratch, RubyFactTools Facts,
    string Gem, string Bundle, string Nbgv, RubyRevisionInputs Candidate);
internal sealed record RubyGroupRSpec(CheckKey Check, RubyRSpecRun Run);
internal sealed record RubyGroupRun(CheckResult[] Results, RubyGroupRSpec[] RSpec,
    RubyPackageRun Packages, CiOutcome Outcome);

// Two adopted recipes consume the original plan; native tools retain their own semantics.
internal static class RubyGroupExecution
{
    internal static Task<RubyGroupRun> RunAsync(CiPlan plan, RubyGroupRunRequest request,
        CancellationToken token) => RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyGroupRun> RunAsync(CiPlan plan, RubyGroupRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ResultCollector.Collect(plan, []);
        if (plan.Scope != RubyFactsAssembler.Scope ||
            request.Candidate.Facts.Revision.Commit != plan.Candidate)
            throw new InvalidDataException("Ruby execution requires its original candidate facts.");
        foreach (string path in new[] { request.Checkout, request.Scratch, request.Gem,
                     request.Bundle, request.Nbgv, request.Facts.Ruby, request.Facts.Helper })
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidDataException("Ruby execution requires prepared native paths.");
        string scratch = Path.GetFullPath(request.Scratch);
        string checkout = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Checkout));
        if (!Directory.Exists(scratch) || Directory.EnumerateFileSystemEntries(scratch).Any() ||
            OwnedPath.Within(scratch, checkout))
            throw new InvalidDataException("Ruby execution requires fresh external scratch.");
        if (!GitReader.IsCommitId(plan.Candidate))
            throw new InvalidDataException("Ruby execution requires the exact original plan.");
        GitMaterialization bound = await GitMaterialization.BindAsync(checkout, plan.Candidate,
            token);
        var suites = new List<(PlannedCheck Check, RubyBundleFacts Bundle)>();
        var packages = new List<PlannedCheck>();
        foreach (PlannedCheck check in plan.Checks)
        {
            if (!check.Origins.SequenceEqual([CheckOrigin.Preset]))
                throw new InvalidDataException("Unsupported Ruby execution origin.");
            if (check.QualityPresets.SequenceEqual([RubyProjectChecks.Preset]) &&
                check.Work.Key.Check == RubyProjectChecks.RSpec &&
                check.Work.Key.Variant == "default" && check.Work.Package is null &&
                check.Work.Dimensions.Count == 0 && check.Work.Prerequisites.Length == 0)
            {
                string directory = Path.GetFullPath(Path.Combine(checkout, check.Work.Key.Target));
                RubyBundleFacts[] contexts = request.Candidate.Bundles.Where(bundle =>
                    Path.GetDirectoryName(Path.GetFullPath(bundle.Gemfile)) == directory).ToArray();
                if (contexts.Length != 1)
                    throw new InvalidDataException("Selected RSpec bundle is unresolved.");
                suites.Add((check, contexts[0]));
            }
            else if (check.QualityPresets.SequenceEqual([RubyPackageChecks.Preset]) &&
                RubyPackageChecks.Kinds.Contains(check.Work.Key.Check, StringComparer.Ordinal))
                packages.Add(check);
            else throw new InvalidDataException("Unsupported adopted Ruby execution recipe.");
        }
        foreach (PlannedCheck check in packages)
        {
            PackageTarget subject = check.Work.Package ?? throw new InvalidDataException(
                "Ruby gem check lacks its original subject.");
            if (subject.Definition != RubyPackageChecks.Build ||
                subject.Outputs is not [{ Role: "primary-package", Kind: "ruby-gem" }] ||
                RubyPackageChecks.Kinds.Any(kind => !packages.Any(other =>
                    other.Work.Key == (check.Work.Key with { Check = kind }) &&
                    ImpactPlanner.SamePackage(other.Work.Package, subject))))
                throw new InvalidDataException("Incomplete original Ruby gem obligations.");
        }
        var results = new List<CheckResult>();
        var runs = new List<RubyGroupRSpec>();
        foreach (var suite in suites)
        {
            CheckStatus status;
            if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
            else
            {
                try
                {
                    RubyRSpecRun run = await RubyRSpecExecution.RunAsync(suite.Check.Work.Key,
                        new(checkout, request.Facts.Ruby, request.Bundle,
                            suite.Bundle.BundlerVersion, suite.Bundle.Gemfile, scratch,
                            request.Facts.DeadlineSeconds, request.Facts.Environment), execute,
                        token);
                    runs.Add(new(suite.Check.Work.Key, run));
                    status = run.Status;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                { status = CheckStatus.Cancelled; }
            }
            results.Add(new(plan.Candidate, suite.Check.Work.Key, status));
        }
        RubyPackageRun gem;
        if (packages.Count == 0) gem = new([], [], [], []);
        else if (token.IsCancellationRequested) gem = new(packages.Select(check =>
            new CheckResult(plan.Candidate, check.Work.Key, CheckStatus.Cancelled)).ToArray(),
            [], [], []);
        else gem = await RubyPackageExecution.RunAsync(plan with { Checks = packages.ToArray() },
            new(checkout, Directory.CreateDirectory(Path.Combine(scratch, "packages")).FullName,
                request.Facts, request.Gem, request.Nbgv), bound, execute, token);
        results.AddRange(gem.Results);
        CheckResult[] observed = results.ToArray();
        return new(observed, runs.ToArray(), gem, ResultCollector.Collect(plan, observed));
    }
}
