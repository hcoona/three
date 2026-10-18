using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI.Callers;

internal sealed record RubyCiCallerContext(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full, string ScopePath, string Application, string Helper);

internal sealed record RubyCiCallerReadback(RubyRevisionInputs Basis, RubyRevisionInputs Candidate);

internal static class RubyCiCaller
{
    internal const string Scope = "ruby/native-paired-facts-v1";

    internal static async Task<RubyCiCallerContext> MaterializeAsync(string root,
        string scopePath, string transfer, string directory, CancellationToken token)
    {
        string application = CiCallerTransfer.Application(transfer);
        string helper = Path.Combine(transfer, "ruby", "facts.rb");
        if (!File.Exists(helper)) throw new
            InvalidDataException("Candidate Ruby helper is missing.");
        CiCallerEndpoints endpoints = await CiCallerTransfer.MaterializeAsync(root, scopePath,
            directory, true, token);
        var context = new RubyCiCallerContext(endpoints.Basis, endpoints.Candidate, endpoints.Full,
            scopePath, application, helper);
        CiCallerTransfer.Write(Path.Combine(directory, "context.json"), context,
            TransferJson.Default.RubyCiCallerContext);
        CiCallerTransfer.Output("basis_directory", context.Basis.Directory);
        CiCallerTransfer.Output("candidate_directory", context.Candidate.Directory);
        return context;
    }

    internal static async Task<RubyGroupReadback> PlanAsync(string directory, string scopePath,
        RubyQueryTools tools, string probeVersion, CancellationToken token)
    {
        RubyCiCallerContext context = Context(directory);
        // Native setup supplies its selected Bundler probe. No duplicate version roster.
        var request = new RubyGroupPreparationRequest(context.Basis, context.Candidate,
            context.Full, scopePath, context.Helper,
            CiCallerTransfer.Fresh(Path.Combine(directory, "query-preparation")), tools,
            probeVersion, 900);
        CiCallerTransfer.Write(Path.Combine(directory, "preparation-request.json"), request,
            TransferJson.Default.RubyGroupPreparationRequest);
        RubyGroupPreparationResult preparation = await
            RubyGroupPreparation.PrepareAsync(request, token);
        CiCallerTransfer.Write(Path.Combine(directory, "preparation.json"), preparation,
            TransferJson.Default.RubyGroupPreparationResult);
        CiCallerTransfer.Write(Path.Combine(directory, "group-request.json"), preparation.Request,
            TransferJson.Default.RubyGroupRequest);
        RubyGroupReadback readback = await RubyGroupReader.ReadAsync(preparation.Request, token);
        CiCallerTransfer.Plan(readback.Plan, context.Basis.Reference,
            context.Candidate.Reference, Scope);
        if (readback.Basis.Facts.Revision.Commit != context.Basis.Reference ||
            readback.Candidate.Facts.Revision.Commit != context.Candidate.Reference)
            throw new InvalidDataException("Ruby collection returned a different endpoint.");
        CiCallerTransfer.Write(Path.Combine(directory, "readback.json"),
            new RubyCiCallerReadback(readback.Basis, readback.Candidate),
            TransferJson.Default.RubyCiCallerReadback);
        CiCallerTransfer.Write(Path.Combine(directory, "plan.json"), readback.Plan,
            TransferJson.Default.CiPlan);
        CiCallerTransfer.Write(Path.Combine(directory, "node-inputs.json"),
            new RubyNodeContribution(readback.Basis.Facts, readback.Candidate.Facts),
            TransferJson.Default.RubyNodeContribution);
        CiCallerTransfer.Output("selected", (readback.Plan.Checks.Length !=
            0).ToString().ToLowerInvariant());
        CiCallerTransfer.Output("latexmath", readback.Plan.Checks.Any(check =>
            check.Work.Key.Target == "src/public/lib/asciidoctor-latexmath")
                .ToString().ToLowerInvariant());
        return readback;
    }

    internal static async Task<CiOutcome> ExecuteAsync(string directory, CancellationToken token,
        Func<CiPlan, RubyGroupRunRequest, CancellationToken, Task<RubyGroupRun>>? execute = null,
        TextWriter? diagnostics = null)
    {
        RubyCiCallerContext context = Context(directory);
        CiPlan plan = CiCallerTransfer.Read(Path.Combine(directory, "plan.json"),
            TransferJson.Default.CiPlan);
        CiCallerTransfer.Plan(plan, context.Basis.Reference, context.Candidate.Reference, Scope);
        CheckResult[] results;
        CiOutcome outcome;
        CiCallerFailure[] failures = [];
        (CheckKey, Platform.NativeCommand, Platform.NativeCommandResult)[] commands = [];
        if (plan.Checks.Length == 0)
        {
            results = [];
            outcome = ResultCollector.Collect(plan, results);
        }
        else
        {
            RubyCiCallerReadback readback = CiCallerTransfer.Read(Path.Combine(directory,
                "readback.json"),
                TransferJson.Default.RubyCiCallerReadback);
            RubyGroupPreparationResult preparation = CiCallerTransfer.Read(
                Path.Combine(directory, "preparation.json"),
                    TransferJson.Default.RubyGroupPreparationResult);
            RubyGroupPreparationRequest original = CiCallerTransfer.Read(
                Path.Combine(directory, "preparation-request.json"),
                    TransferJson.Default.RubyGroupPreparationRequest);
            RubyGroupEndpoint candidate = preparation.Request.Candidate;
            if (candidate.Checkout != context.Candidate ||
                preparation.Request.Basis.Checkout != context.Basis || preparation.Request.Full
                    != context.Full ||
                readback.Candidate.Facts.Revision.Commit != context.Candidate.Reference)
                throw new InvalidDataException("Prepared Ruby candidate differs from execution.");
            var request = new RubyGroupRunRequest(context.Candidate.Directory,
                CiCallerTransfer.Fresh(Path.Combine(directory, "execution")),
                    candidate.Collection.Tools,
                original.Tools.Gem, original.Tools.Bundle, candidate.Collection.Nbgv,
                    readback.Candidate);
            CiCallerTransfer.Write(Path.Combine(directory, "execution-request.json"), request,
                TransferJson.Default.RubyGroupRunRequest);
            RubyGroupRun result = await (execute ?? RubyGroupExecution.RunAsync)(plan, request,
                token);
            CiCallerTransfer.Write(Path.Combine(directory, "result.json"), result,
                TransferJson.Default.RubyGroupRun);
            results = result.Results;
            outcome = CiCallerTransfer.Outcome(plan, result.Outcome);
            failures = [.. result.RSpec.Where(run => run.Run.ReportError is not null)
                    .Select(run => new CiCallerFailure(run.Check, run.Run.ReportError!)),
                .. result.Packages.Failures.Select(failure => new CiCallerFailure(failure.Check,
                    failure.Error))];
            // A failed install is also the suite's Run; report each native command once.
            commands = [.. result.RSpec.SelectMany(run => run.Installs
                    .Where(install => !ReferenceEquals(install.Command, run.Run.Command))
                    .Select(install => (run.Check, install.Command, install.Result))
                    .Append((run.Check, run.Run.Command, run.Run.Result))),
                .. result.Packages.Commands.Select(command => (command.Check, command.Command,
                    command.Result))];
        }
        CiCallerTransfer.Write(Path.Combine(directory, "results.json"), results,
            TransferJson.Default.CheckResultArray);
        CiCallerTransfer.Write(Path.Combine(directory, "outcome.json"), outcome,
            TransferJson.Default.CiOutcome);
        CiCallerTransfer.Report(outcome, failures, commands, diagnostics);
        return outcome;
    }

    private static RubyCiCallerContext Context(string directory) => CiCallerTransfer.Read(
        Path.Combine(directory, "context.json"), TransferJson.Default.RubyCiCallerContext);
}
