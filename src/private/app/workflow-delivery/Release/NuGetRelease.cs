using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record GithubNuGetPublicationResult(ReleaseSubject Subject,
    GithubNuGetObservationResult Before, NuGetPushResult? Invocation,
    GithubNuGetObservationResult After, string[] Errors);
internal sealed record GithubNuGetCompletionResult(ReleaseOutcome Outcome,
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
        if (prepared.Subject.Channel != "buddy" ||
            prepared.Subject.Destination != "github-packages-nuget" ||
            prepared.Files is not [{ Kind: "nuget-package", Size: <= 25 * 1024 * 1024 }] ||
            prepared.Subject.Unit != "hcoona-release-smoke-github-packages" ||
            !prepared.RequiredChecks.SequenceEqual(NuGetPreparation.Required) ||
            prepared.RequiredChecks.Any(id => !prepared.Checks.Any(check => check.Id == id &&
                check.Status == ReleaseCheckStatus.Passed)) || preparation.Errors.Length != 0 ||
            preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded))
            throw new InvalidDataException("Incomplete or unsupported NuGet preparation.");
        DotNetPackageResults.ValidateExpectations("net10.0", "", preparation.Native);
        ReleaseFile file = prepared.Files[0];
        if (file.Name != preparation.Native["PackageId"] ||
            file.Version != preparation.Native["NuGetPackageVersion"] ||
            !StringComparer.OrdinalIgnoreCase.Equals(file.Name, GithubNuGetObservation.Package))
            throw new InvalidDataException("NuGet file differs from native preparation.");
    }

    internal static async Task<GithubNuGetPublicationResult> PublishAsync(
        NuGetPreparationResult preparation, string originals, string credential,
        Func<PreparedRelease, string, CancellationToken, Task<GithubNuGetObservationResult>>
            observe,
        Func<PreparedRelease, string, string, string, CancellationToken, Task<NuGetPushResult>>
            push,
        CancellationToken token)
    {
        _ = Original(preparation, originals);
        PreparedRelease prepared = preparation.Prepared;
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot invoke NuGet publication.");
        GithubNuGetObservationResult before = await observe(prepared, credential, token);
        ValidateObservation(prepared, before);
        if (before.Observation.Files[0].State == ReleaseFileState.Exact &&
            Qualified(before.Observation))
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
        GithubNuGetObservationResult after = await observe(prepared, credential, token);
        ValidateObservation(prepared, after);
        return new(prepared.Subject, before, invocation, after, []);
    }

    internal static GithubNuGetCompletionResult Complete(PreparedRelease prepared,
        GithubNuGetObservationResult initial, GithubNuGetObservationResult final,
        GithubNuGetPublicationResult? published)
    {
        ValidateObservation(prepared, initial);
        ValidateObservation(prepared, final);
        // Completion is the registry observation of the exact coordinate and original bytes.
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(prepared, final.Observation, []);
        if (!Qualified(initial.Observation) || initial.Observation.Files[0].State is not
                (ReleaseFileState.Exact or ReleaseFileState.Absent))
            throw new InvalidDataException("Initial NuGet observation could not proceed.");
        if (published is null)
        {
            if (initial.Observation.Files[0].State == ReleaseFileState.Exact)
                return new(outcome, false, null);
            return new(outcome with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. outcome.Reasons,
                    "Required publication result is missing; effects remain unknown."] }, null,
                null);
        }
        if (published.Subject != prepared.Subject)
            throw new InvalidDataException("Mismatched NuGet publication result.");
        if (initial.Observation.Files[0].State == ReleaseFileState.Exact ||
            published.Invocation is { } invocation &&
                (!double.IsFinite(invocation.DurationSeconds) || invocation.DurationSeconds < 0 ||
                    invocation.ReturnedSuccessfully && invocation.FailureType is not null ||
                    !invocation.ReturnedSuccessfully &&
                        string.IsNullOrWhiteSpace(invocation.FailureType)))
            throw new InvalidDataException("Unsupported or incomplete NuGet invocation result.");
        ValidateObservation(prepared, published.Before);
        ValidateObservation(prepared, published.After);
        bool allowed = Qualified(published.Before.Observation) &&
            published.Before.Observation.Files[0].State == (published.Invocation is null
                ? ReleaseFileState.Exact : ReleaseFileState.Absent);
        if (!allowed || published.Errors.Length != 0)
            return new(outcome with { State = ReleaseBusinessState.Unsatisfied,
                Reasons = [.. outcome.Reasons, "Publication did not establish a supported path."] },
                published.Invocation is not null, published.Invocation);
        return new(outcome, published.Invocation is not null, published.Invocation);
    }

    internal static void ValidateObservation(PreparedRelease prepared,
        GithubNuGetObservationResult result)
    {
        if (!result.Observation.RequiredChecks.SequenceEqual(
                ["native-package-control", "native-metadata-visibility"]) ||
            result.Resources is { } resources &&
                resources.PackagePublish != GithubNuGetPublisher.Endpoint ||
            Qualified(result.Observation) && result.Resources is null)
            throw new InvalidDataException("Incomplete native NuGet observation.");
        // Reuse the common complete-set and exact-byte validation in either mode.
        if (prepared.Subject.DryRun)
            _ = ReleaseCompletion.Describe(prepared, result.Observation,
                result.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
        else _ = ReleaseCompletion.Evaluate(prepared, result.Observation, []);
    }

    private static bool Qualified(ReleaseObservation observation) =>
        observation.RequiredChecks.All(id => observation.Checks.Any(check => check.Id == id &&
            check.Status == ReleaseCheckStatus.Passed));
}
