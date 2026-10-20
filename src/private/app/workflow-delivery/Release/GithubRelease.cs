using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record GithubReleasePublicationResult(ReleaseSubject Subject,
    GithubReleaseObservationResult Before, NativeCommandResult? Creation,
    ReleaseUpload[] Uploads, string[] Errors);
internal sealed record GithubReleaseCompletionResult(ReleaseOutcome Outcome,
    bool? MutationAttempted, GithubReleasePublicationResult? Publication);

// Only concrete ordinary job results are durable; the platform owns approval. A missing
// release is created once, then each missing asset is uploaded once; nothing is replaced.
internal static class GithubRelease
{
    private static readonly string[] Kinds = ["inno-installer", "portable-zip"];

    // The single check of a received preparation in each job.
    internal static void Validate(GithubReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        ReleaseSubject subject = prepared.Subject;
        bool official = GithubReleaseUnits.Admit(subject);
        string version = prepared.Files.Length == 0 ? "" : prepared.Files[0].Version;
        if (prepared.Files.Length != 2 || Kinds.Any(kind =>
                prepared.Files.Count(file => file.Kind == kind &&
                    file.Name == GithubReleaseUnits.Asset(kind, version)) != 1) ||
            prepared.Files.Any(file => file.Version != version) ||
            !prepared.RequiredChecks.SequenceEqual(GithubReleasePreparation.Required) ||
            !Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !Native(preparation, "name", subject.Unit) || !Native(preparation, "version",
                version) || !Native(preparation, "commit", subject.Revision) ||
            !Native(preparation, "tag", GithubReleaseUnits.Tag(subject, version)) ||
            official && !subject.DryRun && !Native(preparation, "public-release", "true"))
            throw new InvalidDataException("Incomplete or unsupported GitHub Release preparation.");
    }

    internal static void Originals(GithubReleasePreparationResult preparation, string directory)
    {
        string root = OwnedPath.Absolute(directory,
            "Original GitHub Release directory must be absolute.");
        foreach (ReleaseFile file in preparation.Prepared.Files)
        {
            string path = Original(root, file);
            if (!File.Exists(path) ||
                GithubReleasePreparation.Original(path) != (file.Size, file.Sha256))
                throw new InvalidDataException("Transferred original differs from preparation.");
        }
    }

    internal static string[] Missing(PreparedRelease prepared,
        GithubReleaseObservationResult observation)
    {
        ValidateObservation(prepared, observation);
        if (!Qualified(observation.Observation.RequiredChecks, observation.Observation.Checks) ||
            observation.Observation.Files.Any(file => file.State is not
                (ReleaseFileState.Exact or ReleaseFileState.Absent)))
            throw new InvalidDataException("Current GitHub Release state cannot permit creation.");
        return Kinds.Select(kind => prepared.Files.Single(file => file.Kind == kind))
            .Where(file => observation.Observation.Files.Single(value =>
                value.Output == file.Output).State == ReleaseFileState.Absent)
            .Select(file => file.Output).ToArray();
    }

    internal static void ValidateObservation(PreparedRelease prepared,
        GithubReleaseObservationResult result)
    {
        if (!result.Observation.RequiredChecks.SequenceEqual([GithubReleaseDestination.Check]) ||
            result.ReleaseId is null && result.Observation.Files.Any(file =>
                file.State == ReleaseFileState.Exact))
            throw new InvalidDataException("Incomplete GitHub Release observation.");
        if (prepared.Subject.DryRun)
            _ = ReleaseCompletion.Describe(prepared, result.Observation,
                result.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
        else _ = ReleaseCompletion.Evaluate(prepared, result.Observation, []);
    }

    internal static async Task<GithubReleasePublicationResult> PublishAsync(
        GithubReleasePreparationResult preparation, string originals,
        GithubReleaseObservationResult initial,
        Func<PreparedRelease, CancellationToken, Task<GithubReleaseObservationResult>> observe,
        Func<PreparedRelease, CancellationToken, Task<(NativeCommandResult, long?)>> create,
        Func<long, ReleaseFile, string, CancellationToken, Task<NativeCommandResult>> upload,
        CancellationToken token)
    {
        Originals(preparation, originals);
        PreparedRelease prepared = preparation.Prepared;
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot invoke GitHub Release publication.");
        ReleasePolicy.RequireEligible(prepared.Subject);
        string[] intended = Missing(prepared, initial);
        if (intended.Length == 0)
            throw new InvalidDataException("An exact request needs no GitHub Release publisher.");
        GithubReleaseObservationResult before = await observe(prepared, token);
        ValidateObservation(prepared, before);
        var uploads = new List<ReleaseUpload>();
        var errors = new List<string>();
        NativeCommandResult? creation = null;
        string[] missing = [];
        try { missing = Missing(prepared, before); }
        catch (InvalidDataException)
        { errors.Add("Current complete state does not permit further GitHub Release writes."); }
        if (missing.Any(output => !intended.Contains(output, StringComparer.Ordinal)))
        {
            errors.Add("Current state would enlarge the reviewed GitHub Release writes.");
            missing = [];
        }
        long? release = before.ReleaseId;
        if (missing.Length != 0 && release is null)
        {
            token.ThrowIfCancellationRequested();
            (creation, release) = await create(prepared, token);
            if (!creation.Succeeded || creation.Error is not null || release is null)
            {
                errors.Add("GitHub Release creation failed or is ambiguous; no asset was written.");
                missing = [];
            }
        }
        foreach (string output in missing)
        {
            if (uploads.Count != 0 && token.IsCancellationRequested)
            {
                errors.Add("GitHub Release publication interrupted; further writes stopped.");
                break;
            }
            Originals(preparation, originals);
            token.ThrowIfCancellationRequested();
            ReleaseFile file = prepared.Files.Single(value => value.Output == output);
            NativeCommandResult result = await upload(release!.Value, file,
                Original(Path.GetFullPath(originals), file), token);
            uploads.Add(new(output, result));
            if (!result.Succeeded || result.Error is not null)
            { errors.Add("GitHub Release asset upload failed; further writes stopped."); break; }
        }
        return new(prepared.Subject, before, creation, [.. uploads], [.. errors]);
    }

    internal static GithubReleaseCompletionResult Complete(PreparedRelease prepared,
        GithubReleaseObservationResult initial, GithubReleaseObservationResult final,
        GithubReleasePublicationResult? publication)
    {
        ValidateObservation(prepared, initial);
        ValidateObservation(prepared, final);
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot produce GitHub Release completion.");
        string[] initialMissing = Missing(prepared, initial);
        ReleaseUpload[] uploads = publication?.Uploads ?? [];
        if (publication is not null) ValidatePublication(prepared, initialMissing, publication);
        // Completion is the readback of every asset against its original bytes (WD-REL-009).
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(prepared, final.Observation, uploads);
        if (publication is null && initialMissing.Length != 0)
            return new(outcome with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. outcome.Reasons,
                    "Required GitHub Release publication result is missing; " +
                        "effects remain unknown."] },
                null, null);
        return new(outcome, publication is null ? false :
            publication.Creation is not null || uploads.Length != 0, publication);
    }

    private static void ValidatePublication(PreparedRelease prepared, string[] initialMissing,
        GithubReleasePublicationResult publication)
    {
        if (publication.Subject != prepared.Subject || initialMissing.Length == 0 ||
            publication.Uploads.Length > 2)
            throw new InvalidDataException("Unsupported GitHub Release publication result.");
        ValidateObservation(prepared, publication.Before);
        if (publication.Creation is not null && publication.Before.ReleaseId is not null)
            throw new InvalidDataException("Unsupported repeated GitHub Release creation.");
        string[] missing = publication.Uploads.Length == 0 ? [] :
            Missing(prepared, publication.Before);
        for (int i = 0; i < publication.Uploads.Length; i++)
        {
            ReleaseUpload upload = publication.Uploads[i];
            if (i >= missing.Length || upload.Output != missing[i] ||
                !initialMissing.Contains(upload.Output, StringComparer.Ordinal) ||
                i > 0 && !publication.Uploads[i - 1].Command.Succeeded ||
                publication.Before.ReleaseId is null && publication.Creation is not
                    { Succeeded: true } ||
                !double.IsFinite(upload.Command.DurationSeconds) ||
                    upload.Command.DurationSeconds < 0)
                throw new InvalidDataException("Unsupported GitHub Release write sequence.");
        }
    }

    private static string Original(string root, ReleaseFile file)
    {
        string path = Path.GetFullPath(Path.Combine(root, file.Path));
        if (path == root || !OwnedPath.Within(path, root))
            throw new InvalidDataException("Original GitHub Release path leaves its root.");
        return path;
    }

    private static bool Native(GithubReleasePreparationResult preparation, string key,
        string expected) => preparation.Native.TryGetValue(key, out string? value) &&
            value == expected;

    private static bool Qualified(string[] required, ReleaseCheck[] results) =>
        required.All(id => results.Any(check => check.Id == id &&
            check.Status == ReleaseCheckStatus.Passed));
}
