using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record PythonPublicationResult(ReleaseSubject Subject,
    PythonIndexObservationResult Before, PythonPublishResult[] Invocations,
    string[] Errors);
internal sealed record PythonCompletionResult(ReleaseOutcome Outcome,
    bool? MutationAttempted, PythonPublicationResult? Publication);

// Only concrete ordinary job results are durable; the platform owns approval.
internal static class PythonRelease
{
    private static readonly string[] Formats = ["python-wheel", "python-sdist"];

    // The single check of a received preparation in each job; later operations in that job
    // rely on it rather than re-admitting preparation internals.
    internal static void Validate(PythonPreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        ReleaseSubject subject = prepared.Subject;
        bool buddy = subject.Channel == "buddy" && subject.Destination == "testpypi";
        bool official = subject.Channel == "official" && subject.Destination == "pypi";
        PythonReleaseUnit unit = PythonReleaseUnits.Admit(subject);
        if (!(buddy || official) ||
            prepared.Files.Length != 2 || Formats.Any(kind =>
                prepared.Files.Count(file => file.Kind == kind) != 1) ||
            prepared.Files.Any(file => file.Name != unit.Id ||
                file.Version != prepared.Files[0].Version) ||
            !prepared.RequiredChecks.SequenceEqual(PythonPreparation.RequiredFor(unit)) ||
            !Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !preparation.Native.TryGetValue("name", out string? name) || name != unit.Id ||
            !preparation.Native.TryGetValue("version", out string? version) ||
                version != prepared.Files[0].Version ||
            !preparation.Native.TryGetValue("commit", out string? commit) ||
                commit != subject.Revision ||
            // Live Official requires the NBGV public-release projection, except for the
            // static version of the WD-REL-001 exception, which has no projection.
            official && !subject.DryRun && unit.Id != PythonReleaseUnits.Nbgv &&
                (!preparation.Native.TryGetValue("public-release", out string? release) ||
                    release != "true"))
            throw new InvalidDataException("Incomplete or unsupported Python preparation.");
    }

    internal static void Originals(PythonPreparationResult preparation, string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("Original Python directory must be absolute.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        foreach (ReleaseFile file in preparation.Prepared.Files)
        {
            string path = Path.GetFullPath(Path.Combine(root, file.Path));
            if (path == root || !OwnedPath.Within(path, root))
                throw new InvalidDataException("Original Python path leaves its transfer root.");
            PythonSmokePackageOperations.RequireOriginal(new(file.Kind, path, file.Size,
                file.Sha256));
        }
    }

    internal static string[] Missing(PreparedRelease prepared,
        PythonIndexObservationResult observation)
    {
        ValidateObservation(prepared, observation);
        if (!Qualified(observation.Observation.RequiredChecks, observation.Observation.Checks) ||
            observation.Observation.Files.Any(file => file.State is not
                (ReleaseFileState.Exact or ReleaseFileState.Absent)))
            throw new InvalidDataException("Current Python state cannot permit creation.");
        return Formats.Select(kind => prepared.Files.Single(file => file.Kind == kind))
            .Where(file => observation.Observation.Files.Single(value =>
                value.Output == file.Output).State == ReleaseFileState.Absent)
            .Select(file => file.Output).ToArray();
    }

    internal static void ValidateObservation(PreparedRelease prepared,
        PythonIndexObservationResult result)
    {
        if (!result.Observation.RequiredChecks.SequenceEqual(["public-index-files"]))
            throw new InvalidDataException("Incomplete Python index observation.");
        if (prepared.Subject.DryRun)
            _ = ReleaseCompletion.Describe(prepared, result.Observation,
                result.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
        else _ = ReleaseCompletion.Evaluate(prepared, result.Observation, []);
    }

    internal static async Task<PythonPublicationResult> PublishAsync(
        PythonPreparationResult preparation, string originals,
        PythonIndexObservationResult initial,
        Func<PreparedRelease, CancellationToken, Task<PythonIndexObservationResult>> observe,
        Func<PreparedRelease, string, string, CancellationToken, Task<PythonPublishResult>>
            publish,
        CancellationToken token)
    {
        Originals(preparation, originals);
        PreparedRelease prepared = preparation.Prepared;
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot invoke Python publication.");
        string[] intended = Missing(prepared, initial);
        if (intended.Length == 0)
            throw new InvalidDataException("An exact request needs no Python publisher.");
        PythonIndexObservationResult before = await observe(prepared, token);
        ValidateObservation(prepared, before);
        var invocations = new List<PythonPublishResult>();
        var errors = new List<string>();
        string[] missing = [];
        try { missing = Missing(prepared, before); }
        catch (InvalidDataException)
        { errors.Add("Current complete state does not permit further Python writes."); }
        if (missing.Any(output => !intended.Contains(output, StringComparer.Ordinal)))
        { errors.Add("Current state would enlarge the reviewed Python writes."); missing = []; }
        // An acknowledged upload is committed by the index, whose public read has no
        // read-after-write guarantee; the next missing file follows without a readback.
        foreach (string output in missing)
        {
            if (invocations.Count != 0 && token.IsCancellationRequested)
            { errors.Add("Python publication interrupted; further writes stopped."); break; }
            Originals(preparation, originals);
            token.ThrowIfCancellationRequested();
            PythonPublishResult invocation = await publish(prepared, originals, output, token);
            invocations.Add(invocation);
            if (invocation.Upload.Output != output)
                throw new InvalidDataException("Python publisher returned another output.");
            if (!Acknowledged(invocation)) break;
        }
        return new(prepared.Subject, before, [.. invocations], [.. errors]);
    }

    internal static PythonCompletionResult Complete(PreparedRelease prepared,
        PythonIndexObservationResult initial, PythonIndexObservationResult final,
        PythonPublicationResult? publication)
    {
        ValidateObservation(prepared, initial);
        ValidateObservation(prepared, final);
        string[] initialMissing = Missing(prepared, initial);
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot produce Python completion.");
        ReleaseUpload[] uploads = publication?.Invocations.Select(value => value.Upload)
            .ToArray() ?? [];
        if (publication is not null) ValidatePublication(prepared, initialMissing, publication);
        // Completion is the index observation of each exact file and its original bytes.
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(prepared, final.Observation, uploads);
        string[] pending = Pending(prepared, final, publication);
        if (pending.Length != 0)
            outcome = outcome with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. pending.Select(output =>
                    $"Acknowledged Python upload is not yet visible: {output}.")] };
        if (publication is null && initialMissing.Length != 0)
            return new(outcome with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. outcome.Reasons,
                    "Required Python publication result is missing; effects remain unknown."] },
                null, null);
        return new(outcome, uploads.Length != 0, publication);
    }

    // Only an acknowledged file the index does not yet show is pending visibility; any
    // other unsatisfied file or check keeps the request unsatisfied.
    private static string[] Pending(PreparedRelease prepared,
        PythonIndexObservationResult final, PythonPublicationResult? publication)
    {
        string[] acknowledged = publication?.Invocations.Where(Acknowledged)
            .Select(value => value.Upload.Output).ToArray() ?? [];
        ReleaseFileObservation[] files = final.Observation.Files;
        bool visibilityOnly = Qualified(prepared.RequiredChecks, prepared.Checks) &&
            Qualified(final.Observation.RequiredChecks, final.Observation.Checks) &&
            files.All(file => file.State == ReleaseFileState.Exact ||
                file.State == ReleaseFileState.Absent &&
                    acknowledged.Contains(file.Output, StringComparer.Ordinal));
        return visibilityOnly ? files.Where(file => file.State == ReleaseFileState.Absent)
            .Select(file => file.Output).ToArray() : [];
    }

    private static void ValidatePublication(PreparedRelease prepared, string[] initialMissing,
        PythonPublicationResult publication)
    {
        if (publication.Subject != prepared.Subject || initialMissing.Length == 0 ||
            publication.Invocations.Length > 2)
            throw new InvalidDataException("Unsupported Python publication result.");
        ValidateObservation(prepared, publication.Before);
        string[] missing = publication.Invocations.Length == 0 ? [] :
            Missing(prepared, publication.Before);
        for (int i = 0; i < publication.Invocations.Length; i++)
        {
            PythonPublishResult invocation = publication.Invocations[i];
            if (i >= missing.Length || invocation.Upload.Output != missing[i] ||
                !initialMissing.Contains(invocation.Upload.Output, StringComparer.Ordinal) ||
                i > 0 && !Acknowledged(publication.Invocations[i - 1]) ||
                !double.IsFinite(invocation.Upload.Command.DurationSeconds) ||
                    invocation.Upload.Command.DurationSeconds < 0)
                throw new InvalidDataException("Unsupported Python write sequence.");
        }
    }

    private static bool Acknowledged(PythonPublishResult result) =>
        result.Upload.Command.Succeeded && result.Upload.Command.Error is null &&
            !result.MaskForwardingFailed;

    private static bool Qualified(string[] required, ReleaseCheck[] results) =>
        required.All(id => results.Any(check => check.Id == id &&
            check.Status == ReleaseCheckStatus.Passed));
}
