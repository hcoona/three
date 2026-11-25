using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal static class NpmRelease
{
    internal static void VerifyOriginals(PreparedRelease prepared, string root)
    {
        foreach (ReleaseFile file in prepared.Files)
        {
            string path = OwnedPath.ResolveWithin(root, file.Path,
                "Original release directory must be absolute.",
                "Original npm path leaves its transfer root.");
            OwnedFile.Verify(path, file.Size, file.Sha256,
                "Original release transfer differs from preparation.");
        }
    }

    internal static bool Proceeds(ReleaseObservation observation) =>
        observation.Files.All(file => file.State is ReleaseFileState.Exact or
            ReleaseFileState.Absent) &&
        ReleasePolicy.Qualified(observation.RequiredChecks, observation.Checks);

    internal static ReleaseDryRun DescribeNpmjs(PreparedRelease prepared,
        ReleaseObservation observation)
    {
        if (prepared.Subject.Destination != "npmjs" || prepared.Files is not
            [{ Kind: "npm-tarball" }])
            throw new InvalidDataException("Unsupported npmjs hypothetical operation set.");
        return ReleaseCompletion.Describe(prepared, observation,
            observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                .Select(file => file.Output).ToArray());
    }

    internal static ReleaseDryRun DescribeGithubNpm(PreparedRelease prepared,
        ReleaseObservation observation)
    {
        if (prepared.Subject.Destination != "github-packages-npm")
            throw new InvalidDataException("Unsupported GitHub npm dry-run destination.");
        return ReleaseCompletion.Describe(prepared, observation,
            observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                .Select(file => file.Output).ToArray());
    }

    internal static ReleaseOutcome Complete(PreparedRelease prepared,
        ReleaseObservation initial, ReleaseObservation final,
        GithubNpmPublicationResult? published) =>
        CompleteCore(prepared, initial, final, published?.Subject,
            published?.Before.Observation, published?.After.Observation,
            published?.Uploads, published?.Errors);

    internal static ReleaseOutcome Complete(PreparedRelease prepared,
        ReleaseObservation initial, ReleaseObservation final,
        NpmjsPublicationResult? published) =>
        CompleteCore(prepared, initial, final, published?.Subject,
            published?.Before.Observation, published?.After.Observation,
            published?.Uploads, published?.Errors);

    // Completion is the registry observation of the exact coordinate and original bytes.
    private static ReleaseOutcome CompleteCore(PreparedRelease prepared,
        ReleaseObservation initial, ReleaseObservation final, ReleaseSubject? publishedSubject,
        ReleaseObservation? before, ReleaseObservation? after,
        ReleaseUpload[]? uploads, string[]? errors)
    {
        ReleaseCompletion.ValidateObservation(prepared, initial);
        if (!Proceeds(initial))
            throw new InvalidDataException("Initial npm observation could not proceed.");
        bool initiallyMissing = initial.Files.Any(file => file.State == ReleaseFileState.Absent);
        if (publishedSubject is not null)
        {
            if (publishedSubject != prepared.Subject || !initiallyMissing)
                throw new InvalidDataException("Mismatched or unnecessary npm publication result.");
            ReleaseCompletion.ValidateObservation(prepared, before!);
            ReleaseCompletion.ValidateObservation(prepared, after!);
            if (uploads!.Length != 0 && (!Proceeds(before!) ||
                    before!.Files.Any(file => file.State != ReleaseFileState.Absent)))
                throw new InvalidDataException("Unsupported npm write result.");
        }
        ReleaseOutcome result = ReleaseCompletion.Evaluate(prepared, final, uploads ?? []);
        if (publishedSubject is null)
        {
            if (!initiallyMissing) return result;
            return result with
            {
                State = ReleaseBusinessState.Unknown,
                Reasons = [.. result.Reasons,
                    "Required publication result is missing; effects remain unknown."],
            };
        }
        if (result.State != ReleaseBusinessState.Satisfied && errors is { Length: > 0 })
            return result with { Reasons = [.. result.Reasons, .. errors] };
        return result;
    }
}
