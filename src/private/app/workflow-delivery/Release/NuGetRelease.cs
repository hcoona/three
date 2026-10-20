using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record NuGetPublicationResult(ReleaseSubject Subject,
    NuGetObservationResult Before, NuGetPushResult? Invocation,
    NuGetObservationResult After, string[] Errors);
internal sealed record NuGetCompletionResult(ReleaseOutcome Outcome,
    bool? MutationAttempted, NuGetPushResult? Invocation);

// These values cross ordinary NuGet jobs; official library calls remain library results.
internal static class NuGetRelease
{
    internal static DotNetOriginalPackage Original(NuGetPreparationResult preparation,
        string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("Original NuGet directory must be absolute.");
        PreparedRelease prepared = preparation.Prepared;
        ReleaseFile file = prepared.Files[0];
        var original = new DotNetOriginalPackage(prepared.Subject.Unit, file.Build, file.Output,
            "primary-package", file.Kind, Path.GetFullPath(Path.Combine(directory, file.Path)),
            file.Size, file.Sha256);
        DotNetPackageConsumer.RequireFile(original.Path);
        DotNetPackageArchive.RequireOriginal(original);
        DotNetPackageArchive.RequireIdentity(DotNetPackageArchive.Identity(original.Path),
            preparation.Native);
        return original;
    }

    // The single check of a received preparation in each job.
    internal static void Validate(NuGetPreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        NuGetReleaseUnit unit = NuGetReleaseUnits.Admit(prepared.Subject);
        ReleasePolicy.RequireEligible(prepared.Subject);
        if (prepared.Files is not [{ Kind: "nuget-package" }] ||
            !prepared.RequiredChecks.SequenceEqual(NuGetPreparation.Required) ||
            prepared.RequiredChecks.Any(id => !prepared.Checks.Any(check => check.Id == id &&
                check.Status == ReleaseCheckStatus.Passed)) || preparation.Errors.Length != 0 ||
            preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded))
            throw new InvalidDataException("Incomplete or unsupported NuGet preparation.");
        DotNetPackageResults.ValidateExpectations(unit.Library ? "" : unit.Frameworks[0], "",
            preparation.Native);
        ReleaseFile file = prepared.Files[0];
        if (file.Name != preparation.Native["PackageId"] ||
            file.Version != preparation.Native["NuGetPackageVersion"] ||
            file.Name != unit.PackageId)
            throw new InvalidDataException("NuGet file differs from native preparation.");
    }

    internal static async Task<NuGetPublicationResult> PublishAsync(
        NuGetPreparationResult preparation, string originals, string credential,
        Func<PreparedRelease, string, CancellationToken, Task<NuGetObservationResult>>
            observe,
        Func<PreparedRelease, string, string, string, CancellationToken, Task<NuGetPushResult>>
            push,
        CancellationToken token)
    {
        _ = Original(preparation, originals);
        PreparedRelease prepared = preparation.Prepared;
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot invoke NuGet publication.");
        NuGetObservationResult before = await observe(prepared, credential, token);
        ValidateObservation(prepared, before);
        if (Present(before.Observation) && Qualified(before.Observation))
            return new(prepared.Subject, before, null, before, []);
        if (before.Observation.Files[0].State != ReleaseFileState.Absent ||
            !Qualified(before.Observation))
            return new(prepared.Subject, before, null, before,
                ["Current destination state does not permit native creation."]);
        _ = Original(preparation, originals);
        token.ThrowIfCancellationRequested();
        NuGetPushResult invocation = await push(prepared, originals,
            before.Resources!.PackagePublish, credential, token);
        // Handled upload abnormality permits bounded reads, never application resend.
        NuGetObservationResult after = await observe(prepared, credential, token);
        ValidateObservation(prepared, after);
        return new(prepared.Subject, before, invocation, after, []);
    }

    // About 15 minutes: 30 reads separated by 29 intervals of 30 seconds.
    internal const int SettleAttempts = 30;
    internal static readonly TimeSpan SettleInterval = TimeSpan.FromSeconds(30);

    // Native validation and indexing can delay visibility after an accepted upload; only an
    // absent coordinate is read again, and every other state is the observation result.
    internal static async Task<NuGetObservationResult> SettleAsync(PreparedRelease prepared,
        Func<PreparedRelease, string, CancellationToken, Task<NuGetObservationResult>> observe,
        string credential, int attempts, TimeSpan interval, CancellationToken token)
    {
        for (int attempt = 1; ; attempt++)
        {
            NuGetObservationResult result = await observe(prepared, credential, token);
            if (attempt >= attempts ||
                result.Observation.Files[0].State != ReleaseFileState.Absent)
                return result;
            await Task.Delay(interval, token);
        }
    }

    internal static NuGetCompletionResult Complete(PreparedRelease prepared,
        NuGetObservationResult initial, NuGetObservationResult final,
        NuGetPublicationResult? published)
    {
        ValidateObservation(prepared, initial);
        ValidateObservation(prepared, final);
        // Completion is the registry observation of the exact coordinate and original bytes,
        // or for nuget.org of the exact coordinate available (WD-REL-009).
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(prepared, final.Observation, []);
        if (!Qualified(initial.Observation) || !Present(initial.Observation) &&
                initial.Observation.Files[0].State != ReleaseFileState.Absent)
            throw new InvalidDataException("Initial NuGet observation could not proceed.");
        if (published is null)
        {
            if (Present(initial.Observation)) return new(outcome, false, null);
            return new(outcome with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. outcome.Reasons,
                    "Required publication result is missing; effects remain unknown."] }, null,
                null);
        }
        if (published.Subject != prepared.Subject)
            throw new InvalidDataException("Mismatched NuGet publication result.");
        if (Present(initial.Observation) ||
            published.Invocation is { } invocation &&
                (!double.IsFinite(invocation.DurationSeconds) || invocation.DurationSeconds < 0 ||
                    invocation.ReturnedSuccessfully && invocation.FailureType is not null ||
                    !invocation.ReturnedSuccessfully &&
                        string.IsNullOrWhiteSpace(invocation.FailureType)))
            throw new InvalidDataException("Unsupported or incomplete NuGet invocation result.");
        ValidateObservation(prepared, published.Before);
        ValidateObservation(prepared, published.After);
        bool allowed = Qualified(published.Before.Observation) &&
            (published.Invocation is null ? Present(published.Before.Observation)
                : published.Before.Observation.Files[0].State == ReleaseFileState.Absent);
        if (!allowed || published.Errors.Length != 0)
            return new(outcome with { State = ReleaseBusinessState.Unsatisfied,
                Reasons = [.. outcome.Reasons, "Publication did not establish a supported path."] },
                published.Invocation is not null, published.Invocation);
        // An attempted upload whose coordinate is still absent after the bounded wait has
        // unresolved effects; the owner re-dispatches the same revision later to observe it.
        if (published.Invocation is not null &&
            final.Observation.Files[0].State == ReleaseFileState.Absent)
            return new(outcome with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. outcome.Reasons, "Uploaded coordinate was not observed within " +
                    "the bounded wait; re-dispatch the same revision later."] }, true,
                published.Invocation);
        return new(outcome, published.Invocation is not null, published.Invocation);
    }

    internal static void ValidateObservation(PreparedRelease prepared,
        NuGetObservationResult result)
    {
        bool github = prepared.Subject.Destination == "github-packages-nuget";
        if (!result.Observation.RequiredChecks.SequenceEqual(github
                ? GithubNuGetObservation.RequiredChecks : NuGetOrgObservation.RequiredChecks) ||
            result.Resources is { } resources && resources.PackagePublish != (github
                ? NuGetPublisher.GithubEndpoint : NuGetPublisher.NuGetOrgEndpoint) ||
            Qualified(result.Observation) && result.Resources is null)
            throw new InvalidDataException("Incomplete native NuGet observation.");
        // Reuse the common complete-set and exact-byte validation in either mode.
        if (prepared.Subject.DryRun)
            _ = ReleaseCompletion.Describe(prepared, result.Observation,
                result.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
        else _ = ReleaseCompletion.Evaluate(prepared, result.Observation, []);
    }

    // A present coordinate is never created again; nuget.org reports it without byte proof.
    internal static bool Present(ReleaseObservation observation) =>
        observation.Files[0].State is ReleaseFileState.Exact or ReleaseFileState.Available;

    private static bool Qualified(ReleaseObservation observation) =>
        observation.RequiredChecks.All(id => observation.Checks.Any(check => check.Id == id &&
            check.Status == ReleaseCheckStatus.Passed));
}
