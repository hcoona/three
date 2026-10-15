using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record PythonPublicationResult(ReleaseSubject Subject,
    PythonIndexObservationResult Before, PythonPublishResult[] Invocations,
    PythonIndexObservationResult[] Readbacks, string[] Errors);
internal sealed record PythonCompletionResult(ReleaseOutcome Outcome,
    bool? MutationAttempted, PythonPublicationResult? Publication);

// Only concrete ordinary job results are durable; the platform owns approval.
internal static class PythonRelease
{
    private const string Package = "hcoona-release-smoke-python";
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
        if (!(buddy || official) || subject.Unit != Package ||
            prepared.Files.Length != 2 || Formats.Any(kind =>
                prepared.Files.Count(file => file.Kind == kind) != 1) ||
            prepared.Files.Any(file => file.Name != Package ||
                file.Size > ReleasePolicy.ReadbackOriginalLimit ||
                file.Version != prepared.Files[0].Version) ||
            !prepared.RequiredChecks.SequenceEqual(PythonPreparation.Required) ||
            !Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !preparation.Native.TryGetValue("name", out string? name) || name != Package ||
            !preparation.Native.TryGetValue("version", out string? version) ||
                version != prepared.Files[0].Version ||
            !preparation.Native.TryGetValue("commit", out string? commit) ||
                commit != subject.Revision ||
            official && !subject.DryRun && (!preparation.Native.TryGetValue("public-release",
                out string? release) || release != "true"))
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
        var readbacks = new List<PythonIndexObservationResult>();
        var errors = new List<string>();
        PythonIndexObservationResult current = before;
        foreach (string kind in Formats)
        {
            string[] missing;
            try { missing = Missing(prepared, current); }
            catch (InvalidDataException)
            { errors.Add("Current complete state does not permit further Python writes."); break; }
            if (missing.Any(output => !intended.Contains(output, StringComparer.Ordinal)))
            { errors.Add("Current state would enlarge the reviewed Python writes."); break; }
            ReleaseFile file = prepared.Files.Single(value => value.Kind == kind);
            if (!missing.Contains(file.Output, StringComparer.Ordinal)) continue;
            Originals(preparation, originals);
            token.ThrowIfCancellationRequested();
            PythonPublishResult invocation = await publish(prepared, originals, file.Output,
                token);
            invocations.Add(invocation);
            if (invocation.Upload.Output != file.Output)
                throw new InvalidDataException("Python publisher returned another output.");
            try
            {
                current = await observe(prepared, token);
                ValidateObservation(prepared, current);
                readbacks.Add(current);
            }
            catch (OperationCanceledException)
            { errors.Add("Python publication readback cancelled; further writes stopped."); break; }
            if (!Acknowledged(invocation)) break;
            try { _ = Missing(prepared, current); }
            catch (InvalidDataException)
            { errors.Add("Python intermediate state cannot permit further writes."); break; }
            if (current.Observation.Files.Single(value => value.Output == file.Output).State !=
                ReleaseFileState.Exact)
            { errors.Add("Published original lacks exact intermediate readback."); break; }
        }
        return new(prepared.Subject, before, [.. invocations], [.. readbacks], [.. errors]);
    }

    internal static PythonCompletionResult Complete(PreparedRelease prepared,
        PythonIndexObservationResult initial, PythonIndexObservationResult final,
        PythonRemoteConsumerResult[] consumers, PythonPublicationResult? publication)
    {
        ValidateObservation(prepared, initial);
        ValidateObservation(prepared, final);
        string[] initialMissing = Missing(prepared, initial);
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot produce Python completion.");
        var checks = new List<ReleaseCheck>();
        if (consumers.Length > 2 || consumers.Select(value => value.Original.Kind)
                .Distinct(StringComparer.Ordinal).Count() != consumers.Length)
            throw new InvalidDataException("Ambiguous remote Python format results.");
        string index = prepared.Subject.Destination == "testpypi"
            ? "https://test.pypi.org/simple/" : "https://pypi.org/simple/";
        foreach (PythonRemoteConsumerResult consumer in consumers)
        {
            ReleaseFile file = prepared.Files.SingleOrDefault(value =>
                value.Kind == consumer.Original.Kind) ??
                throw new InvalidDataException("Unsupported remote Python format.");
            if (consumer.Subject != prepared.Subject || consumer.Original != file ||
                consumer.Index != index || consumer.Check.Id != ConsumerCheck(file) ||
                !Enum.IsDefined(consumer.Check.Status) ||
                consumer.Check.Status == ReleaseCheckStatus.Passed &&
                    (consumer.Errors.Length != 0 || consumer.Commands.Length == 0 ||
                        consumer.Commands.Any(command => !command.Result.Succeeded ||
                            command.Result.Error is not null) ||
                        !consumer.Commands.Any(command => command.Stage == "install") ||
                        !consumer.Commands.Any(command => command.Stage == "consume")))
                throw new InvalidDataException("Incomplete or mismatched remote Python result.");
            checks.Add(consumer.Check);
        }
        ReleaseObservation complete = final.Observation with
        {
            RequiredChecks = [.. final.Observation.RequiredChecks,
                .. prepared.Files.Select(ConsumerCheck)],
            Checks = [.. final.Observation.Checks, .. checks],
        };
        ReleaseUpload[] uploads = publication?.Invocations.Select(value => value.Upload)
            .ToArray() ?? [];
        if (publication is not null) ValidatePublication(prepared, initialMissing, publication);
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(prepared, complete, uploads);
        if (publication is null && initialMissing.Length != 0)
            return new(outcome with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. outcome.Reasons,
                    "Required Python publication result is missing; effects remain unknown."] },
                null, null);
        return new(outcome, uploads.Length != 0, publication);
    }

    private static void ValidatePublication(PreparedRelease prepared, string[] initialMissing,
        PythonPublicationResult publication)
    {
        if (publication.Subject != prepared.Subject || initialMissing.Length == 0 ||
            publication.Invocations.Length > 2 || publication.Readbacks.Length >
                publication.Invocations.Length)
            throw new InvalidDataException("Unsupported Python publication result.");
        ValidateObservation(prepared, publication.Before);
        PythonIndexObservationResult current = publication.Before;
        for (int i = 0; i < publication.Invocations.Length; i++)
        {
            PythonPublishResult invocation = publication.Invocations[i];
            string[] missing = Missing(prepared, current);
            if (missing.Length == 0 || invocation.Upload.Output != missing[0] ||
                !initialMissing.Contains(invocation.Upload.Output, StringComparer.Ordinal) ||
                i > 0 && (!Acknowledged(publication.Invocations[i - 1]) ||
                    publication.Readbacks.Length < i ||
                    current.Observation.Files.Single(file => file.Output ==
                        publication.Invocations[i - 1].Upload.Output).State !=
                            ReleaseFileState.Exact) ||
                !double.IsFinite(invocation.Upload.Command.DurationSeconds) ||
                    invocation.Upload.Command.DurationSeconds < 0)
                throw new InvalidDataException("Unsupported Python write sequence.");
            if (i < publication.Readbacks.Length)
            {
                current = publication.Readbacks[i];
                ValidateObservation(prepared, current);
            }
        }
    }

    private static bool Acknowledged(PythonPublishResult result) =>
        result.Upload.Command.Succeeded && result.Upload.Command.Error is null &&
            !result.MaskForwardingFailed;

    private static string ConsumerCheck(ReleaseFile file) =>
        "remote-" + file.Output + "-consumer";

    private static bool Qualified(string[] required, ReleaseCheck[] results) =>
        required.All(id => results.Any(check => check.Id == id &&
            check.Status == ReleaseCheckStatus.Passed));
}
