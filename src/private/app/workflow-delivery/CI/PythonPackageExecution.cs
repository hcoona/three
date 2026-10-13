using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record PythonPackageRunRequest(string Checkout, string Scratch, string Uv,
    string Interpreter, string Helper, PythonMetadataMember[] Members, int DeadlineSeconds,
    IReadOnlyDictionary<string, string?> Environment);
internal sealed record PythonPackageCommand(CheckKey Check, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record PythonOriginalDistribution(string Unit, string Build, PackageOutput Output,
    string Path, long Length, string Sha256);
internal sealed record PythonPackageFailure(CheckKey Check, string Error);
internal sealed record PythonPackageRunResult(string Candidate, CheckResult[] Results,
    PythonPackageCommand[] Commands, PythonOriginalDistribution[] Distributions,
    PythonPackageFailure[] Failures);

// Finite orchestration for the adopted contract; no dependency/backend/version evaluator.
internal static class PythonPackageExecution
{
    internal static Task<PythonPackageRunResult> RunAsync(CiPlan plan,
        PythonPackageRunRequest request, CancellationToken token) =>
        RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonPackageRunResult> RunAsync(CiPlan plan,
        PythonPackageRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string checkout = Absolute(request.Checkout), scratch = Absolute(request.Scratch);
        var tools = new PythonPackageTools(checkout, scratch, request.Uv, request.Interpreter,
            request.Helper, request.DeadlineSeconds, request.Environment);
        PythonSmokePackageOperations.Validate(tools);
        ResultCollector.Collect(plan, []);
        if (plan.Scope != PythonFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported Python package execution scope.");
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException(
                "Python package candidate must be a full commit identity.");
        GitMaterialization materialization = await GitMaterialization.BindAsync(checkout,
            revision, token);
        await ReadChecksAsync(plan, request.Members, materialization, token);
        var results = new Dictionary<CheckKey, CheckResult>();
        var commands = new List<PythonPackageCommand>();
        var distributions = new List<PythonOriginalDistribution>();
        var failures = new List<PythonPackageFailure>();
        int ordinal = 0;
        foreach (PlannedCheck pack in plan.Checks.Where(item =>
                     item.Work.Key.Check == PythonPackageChecks.Build))
        {
            PackageTarget subject = pack.Work.Package!;
            string name = request.Members.Single(member =>
                member.Directory == subject.Directory).Name;
            string root = Path.Combine(scratch, "package-" + ordinal++);
            var operations = new PythonSmokePackageOperations(tools, root);
            PythonDistribution[] originals = [];
            foreach (string kind in PythonPackageChecks.Kinds)
            {
                CheckSpec check = plan.Checks.Single(item =>
                    item.Work.Key == (pack.Work.Key with { Check = kind })).Work;
                CheckStatus status;
                if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
                else if (check.Prerequisites.Any(key => results[key].Status != CheckStatus.Passed))
                    status = CheckStatus.Skipped;
                else try
                    {
                        if (kind == PythonPackageChecks.Build)
                        {
                            (string actualName, string actualVersion) =
                                await operations.PrepareAsync(subject.Directory, RunAsync, token);
                            if (actualName != name || actualVersion != subject.ExpectedVersion)
                                throw new InvalidDataException(
                                    "Native Python version projection differs.");
                            originals = await operations.BuildAsync(name, subject.ExpectedVersion,
                                RunAsync, token);
                            distributions.AddRange(originals.Select(original => new
                                PythonOriginalDistribution(subject.Unit, subject.Build,
                                    subject.Outputs.Single(output => output.Kind == original.Kind),
                                    original.Path, original.Length, original.Sha256)));
                        }
                        else if (kind == PythonPackageChecks.Contents)
                            await operations.ContentsAsync(originals, name, subject.ExpectedVersion,
                                RunAsync, token);
                        else
                            await operations.ConsumeAsync(kind == PythonPackageChecks.Wheel
                                    ? "python-wheel" : "python-sdist", originals, name,
                                subject.ExpectedVersion, RunAsync, token);
                        token.ThrowIfCancellationRequested();
                        status = CheckStatus.Passed;
                    }
                    catch (PackageCommandFailure exception)
                    { status = exception.Status; failures.Add(new(check.Key, exception.Message)); }
                    catch (OperationCanceledException) { status = CheckStatus.Cancelled; }
                    catch (Exception exception) when (exception is IOException or
                        InvalidDataException or JsonException or
                        UnauthorizedAccessException or ArgumentException or
                            InvalidOperationException
                            or KeyNotFoundException)
                    { status = CheckStatus.Failed; failures.Add(new(check.Key,
                        exception.Message)); }
                results.Add(check.Key, new(plan.Candidate, check.Key, status));

                async Task<NativeCommandResult> RunAsync(NativeCommand command,
                    CancellationToken cancellation)
                {
                    NativeCommandResult result = await execute(command, cancellation);
                    commands.Add(new(check.Key, command, result));
                    if (!result.Succeeded || result.Error is not null)
                        throw new PackageCommandFailure(result.Termination switch
                        {
                            NativeTermination.Cancelled => CheckStatus.Cancelled,
                            NativeTermination.TimedOut => CheckStatus.TimedOut,
                            _ => CheckStatus.Failed,
                        },
                            result.Error ??
                            $"Native command {result.Termination}, exit {result.ExitCode}.");
                    return result;
                }
            }
        }
        return new(plan.Candidate, plan.Checks.Select(item => results[item.Work.Key]).ToArray(),
            commands.ToArray(), distributions.ToArray(), failures.ToArray());
    }

    internal static async Task ReadChecksAsync(CiPlan plan, PythonMetadataMember[] members,
        GitMaterialization materialization, CancellationToken token)
    {
        foreach (var group in plan.Checks.GroupBy(item =>
                     (item.Work.Key.Target, item.Work.Key.Variant)))
        {
            PackageTarget subject = group.First().Work.Package ??
                throw new InvalidDataException("Missing Python package subject.");
            string text = await materialization.ReadOptionalTextAsync(subject.Declaration, token)
                ?? throw new InvalidDataException("Missing committed Python release declaration.");
            ReleaseUnitDeclaration declaration = ReleaseUnitDeclarationReader.Read(text,
                subject.Declaration);
            CheckSpec[] declared = PythonPackageChecks.Expand(
                declaration with { Builds = declaration.Builds.Where(build =>
                    build.Id == subject.Build).ToArray() }, members,
                new Dictionary<string, string> { [subject.Directory] = subject.ExpectedVersion });
            CheckSpec[] expected = declared.Where(check => check.Package!.Build == subject.Build)
                .ToArray();
            if (group.Count() != 4 || expected.Length != 4 ||
                await materialization.ReadOptionalTextAsync(subject.EntryPoint, token) is null)
                throw new InvalidDataException("Incomplete committed Python package obligations.");
            foreach (PlannedCheck item in group)
            {
                CheckSpec? match = expected.SingleOrDefault(check => check.Key == item.Work.Key);
                if (match is null || !item.Origins.SequenceEqual([CheckOrigin.Preset]) ||
                    !item.QualityPresets.SequenceEqual([PythonPackageChecks.Preset]) ||
                    item.Work.Runner != match.Runner || item.Work.Required != match.Required ||
                    item.Work.Dimensions.Count != 0 ||
                    !item.Work.Prerequisites.SequenceEqual(match.Prerequisites) ||
                    !ImpactPlanner.SamePackage(item.Work.Package, match.Package))
                    throw new InvalidDataException("Conflicting Python package quality contract.");
            }
        }
    }

    private sealed class PackageCommandFailure(CheckStatus status,
        string message) : Exception(message)
    { internal CheckStatus Status { get; } = status; }
    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        : throw new InvalidDataException("Python package paths must be absolute.");
}
