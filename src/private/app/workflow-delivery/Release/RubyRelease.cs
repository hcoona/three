using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record RubyPublicationResult(ReleaseSubject Subject,
    RubyObservationResult Before, RubyPublishResult? Invocation,
    RubyObservationResult? Readback, string[] Errors);
internal sealed record RubyCompletionResult(ReleaseOutcome Outcome,
    bool? MutationAttempted, RubyPublicationResult? Publication);

// These concrete values cross ordinary jobs; native policy owns the write gate.
internal static class RubyRelease
{
    // The single check of a received preparation in each job; later operations in that job
    // rely on it rather than re-admitting preparation internals.
    internal static void Validate(RubyPreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        ReleaseSubject subject = prepared.Subject;
        bool buddy = subject.Channel == "buddy" &&
            subject.Destination == "github-packages-ruby";
        bool official = subject.Channel == "official" && subject.Destination == "rubygems";
        string package = RubyReleaseUnits.Admit(subject).Id;
        if (!(buddy || official) || prepared.Files is not
                [{ Kind: "ruby-gem", Name: var file }] || file != package ||
            !prepared.RequiredChecks.SequenceEqual(RubyPreparation.Required) ||
            !Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !preparation.Native.TryGetValue("name", out string? name) || name != package ||
            !preparation.Native.TryGetValue("version", out string? version) ||
                version != prepared.Files[0].Version ||
            !preparation.Native.TryGetValue("platform", out string? platform) ||
                platform != "ruby" ||
            !preparation.Native.TryGetValue("commit", out string? commit) ||
                commit != subject.Revision ||
            official && !subject.DryRun && (!preparation.Native.TryGetValue("public-release",
                out string? release) || release != "true"))
            throw new InvalidDataException("Incomplete or unsupported Ruby preparation.");
    }

    internal static void Originals(RubyPreparationResult preparation, string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("Original Ruby directory must be absolute.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        ReleaseFile file = preparation.Prepared.Files[0];
        string path = Path.GetFullPath(Path.Combine(root, file.Path));
        if (path == root || !OwnedPath.Within(path, root))
            throw new InvalidDataException("Original Ruby path leaves its transfer root.");
        RubySmokePackageOperations.RequireOriginal(new(path, file.Size, file.Sha256));
    }

    internal static void ValidateObservation(PreparedRelease prepared,
        RubyObservationResult result)
    {
        if (!result.Observation.RequiredChecks.SequenceEqual(["native-source-original"]))
            throw new InvalidDataException("Incomplete native Ruby observation.");
        if (prepared.Subject.DryRun)
            _ = ReleaseCompletion.Describe(prepared, result.Observation,
                result.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
        else _ = ReleaseCompletion.Evaluate(prepared, result.Observation, []);
    }

    internal static bool Missing(PreparedRelease prepared, RubyObservationResult result)
    {
        ValidateObservation(prepared, result);
        if (!Qualified(result.Observation.RequiredChecks, result.Observation.Checks) ||
            result.Observation.Files is not [{ State: ReleaseFileState.Exact or
                ReleaseFileState.Absent }])
            throw new InvalidDataException("Current Ruby state cannot permit creation.");
        return result.Observation.Files[0].State == ReleaseFileState.Absent;
    }

    internal static async Task<RubyPublicationResult> PublishAsync(
        RubyPreparationResult preparation, string originals, RubyObservationResult initial,
        Func<PreparedRelease, CancellationToken, Task<RubyObservationResult>> observe,
        Func<RubyPreparationResult, string, CancellationToken, Task<RubyPublishResult>> publish,
        CancellationToken token)
    {
        Originals(preparation, originals);
        PreparedRelease prepared = preparation.Prepared;
        if (prepared.Subject.DryRun || !Missing(prepared, initial))
            throw new InvalidDataException("This Ruby request requires no publisher.");
        RubyObservationResult before = await observe(prepared, token);
        ValidateObservation(prepared, before);
        bool missing;
        try { missing = Missing(prepared, before); }
        catch (InvalidDataException)
        {
            return new(prepared.Subject, before, null, null,
                ["Refreshed Ruby state cannot permit creation."]);
        }
        if (!missing) return new(prepared.Subject, before, null, null, []);
        token.ThrowIfCancellationRequested();
        Originals(preparation, originals);
        RubyPublishResult invocation = await publish(preparation, originals, token);
        if (invocation.Subject != prepared.Subject ||
            invocation.Upload.Output != prepared.Files[0].Output)
            throw new InvalidDataException("Ruby publisher returned another request or output.");
        RubyObservationResult? readback = null;
        string[] errors = [];
        try
        {
            readback = await observe(prepared, token);
            ValidateObservation(prepared, readback);
        }
        catch (OperationCanceledException)
        { errors = ["Ruby publication readback cancelled; attempted effects retained."]; }
        return new(prepared.Subject, before, invocation, readback, errors);
    }

    internal static RubyCompletionResult Complete(PreparedRelease prepared,
        RubyObservationResult initial, RubyObservationResult final,
        RubyPublicationResult? publication)
    {
        ValidateObservation(prepared, initial);
        ValidateObservation(prepared, final);
        bool initiallyMissing = Missing(prepared, initial);
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot produce Ruby completion.");
        ReleaseFile original = prepared.Files[0];
        if (publication is not null)
        {
            if (publication.Subject != prepared.Subject || !initiallyMissing)
                throw new InvalidDataException("Unsupported Ruby publication result.");
            ValidateObservation(prepared, publication.Before);
            if (publication.Invocation is { } invocation &&
                (!Missing(prepared, publication.Before) || invocation.Subject != prepared.Subject ||
                    invocation.Upload.Output != original.Output ||
                    !double.IsFinite(invocation.Upload.Command.DurationSeconds) ||
                    invocation.Upload.Command.DurationSeconds < 0))
                throw new InvalidDataException("Unsupported Ruby write result.");
            if (publication.Readback is not null)
            {
                if (publication.Invocation is null)
                    throw new InvalidDataException("Ruby readback has no native invocation.");
                ValidateObservation(prepared, publication.Readback);
            }
        }
        ReleaseUpload[] uploads = publication?.Invocation is { } attempt ? [attempt.Upload] : [];
        // Completion is the native source observation of the exact gem and its original bytes.
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(prepared, final.Observation, uploads);
        if (publication is null && initiallyMissing)
            return new(outcome with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. outcome.Reasons,
                    "Required Ruby publication result is missing; effects remain unknown."] },
                null, null);
        return new(outcome, uploads.Length != 0, publication);
    }

    private static bool Qualified(string[] required, ReleaseCheck[] results) =>
        required.All(id => results.Any(check => check.Id == id &&
            check.Status == ReleaseCheckStatus.Passed));
}
