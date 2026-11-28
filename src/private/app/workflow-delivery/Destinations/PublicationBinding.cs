using System.Collections.Immutable;
using WorkflowDelivery.Core;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

// Projects one bound native coordinate to the operations presented for review.
internal sealed class PublicationBinding
{
    private readonly record struct Binding(PublicationOperation Operation,
        Func<ReleaseObservationResult, OperationEvidence> Evidence);

    private readonly Binding[] bindings;
    private readonly Action<ReleaseObservationResult> validate;
    private readonly string? nativeSubmit;

    private PublicationBinding(DestinationProfile profile, IEnumerable<Binding> operations,
        Action<ReleaseObservationResult> validate, bool supportsPartial = true,
        SubmissionClearance clearance = SubmissionClearance.Unresolved, string? nativeSubmit = null)
    {
        bindings = [.. operations];
        this.validate = validate;
        this.nativeSubmit = nativeSubmit;
        Profile = new(bindings.Select(binding => binding.Operation), profile.VersionRead,
            supportsPartial, clearance, profile.Verification, profile.Visibility);
    }

    internal PublicationProfile Profile { get; }
    internal static readonly OperationId CreateRelease = new("container", "release");
    internal static readonly OperationId SubmitVersion = new("store", "submission");

    internal PublicationEvidence Project(ReleaseObservationResult observation)
    {
        validate(observation);
        return new(ReleasePolicy.Qualified(observation.Observation.RequiredChecks,
                observation.Observation.Checks),
            bindings.ToImmutableDictionary(b => b.Operation.Id, b => b.Evidence(observation)));
    }

    internal PublicationRecord Record(ReleasePublicationResult publication, bool interrupted)
    {
        if (Profile.Proof == DestinationVerificationMode.StoreAccepted &&
            publication.Steps.Length == 0 &&
            (Profile.Read == DestinationVersionRead.PublisherOnly ||
                publication.After is not null) &&
            publication.After?.StoreState != publication.Before.StoreState)
            throw new InvalidDataException(
                "A write-free store receipt must report its fresh version read.");
        return new(Project(publication.Before), Attempts(publication.Steps, interrupted),
            publication.After is null ? null : Project(publication.After),
            interrupted && nativeSubmit is null);
    }

    internal ImmutableArray<OperationAttempt> Attempts(
        IReadOnlyList<ReleaseWriteStep> steps,
        bool interrupted)
    {
        if (nativeSubmit is null) return [.. steps.Select(Project)];
        if (steps.Count == 0)
            return interrupted
                ? throw new InvalidDataException("A native submission never started.")
                : [];
        OperationAttempt upload = Project(steps[0]);
        if (upload.Operation != new OperationId("store", "upload") || steps.Count > 2)
            throw new InvalidDataException("Invalid native submission stages.");
        if (steps.Count == 1)
        {
            if (upload.Acknowledged != interrupted)
                throw new InvalidDataException("Incomplete or contradictory native submission.");
            return [new(SubmitVersion, false, upload.Ambiguous)];
        }
        OperationAttempt submit = Project(steps[1]);
        if (!upload.Acknowledged || interrupted ||
            submit.Operation != new OperationId("store", nativeSubmit))
            throw new InvalidDataException("Invalid native submission stages.");
        return [submit with { Operation = SubmitVersion }];
    }

    internal static PublicationBinding Files(IDestinationAdapter adapter,
        ReleasePreparationResult preparation,
        string writeKind = "upload", bool createRelease = false)
    {
        PreparedRelease prepared = preparation.Prepared;
        DestinationProfile profile = adapter.Profile(prepared.Subject);
        var operations = new List<Binding>();
        if (createRelease)
            operations.Add(new(new(CreateRelease), observed => observed.Container switch
            {
                ContainerPresence.Present => OperationEvidence.Established,
                ContainerPresence.Missing => OperationEvidence.Missing,
                _ => OperationEvidence.Unknown,
            }));
        IEnumerable<ReleaseFile> files = profile.OrderedKinds is { } kinds
            ? kinds.Select(kind => prepared.Files.Single(file => file.Kind == kind))
            : prepared.Files;
        foreach (ReleaseFile file in files)
            operations.Add(new(new(new(writeKind, file.Output),
                    createRelease ? [CreateRelease] : []),
                observed => State(profile,
                    observed.Observation.Files.Single(f => f.Output == file.Output).State)));
        return new(
            profile, operations, observed => adapter.ValidateObservation(preparation, observed));
    }

    internal static PublicationBinding Store(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, string submit)
    {
        // The entire submitted version is the proof coordinate. Upload is not
        // independently resumable.
        return new(adapter.Profile(preparation.Prepared.Subject),
            [new(new(SubmitVersion), Evidence)],
            observed => adapter.ValidateObservation(preparation, observed), supportsPartial: false,
            clearance: preparation.Prepared.Subject.Clearance, nativeSubmit: submit);

        static OperationEvidence Evidence(
            ReleaseObservationResult observed) => observed.StoreState switch
            {
                StoreState.Received or StoreState.InReview or StoreState.Published =>
                    OperationEvidence.Established,
                StoreState.Absent => OperationEvidence.Missing,
                StoreState.Unobserved => OperationEvidence.Unobserved,
                StoreState.Conflict => OperationEvidence.Conflict,
                _ => OperationEvidence.Unknown,
            };
    }

    internal static OperationEvidence State(DestinationProfile profile, ReleaseFileState state) =>
        ReleaseEngine.IsSatisfiedState(profile, state)
            ? OperationEvidence.Established
            : state switch
            {
                ReleaseFileState.Absent => OperationEvidence.Missing,
                ReleaseFileState.Conflict => OperationEvidence.Conflict,
                _ => OperationEvidence.Unknown,
            };

    internal static OperationAttempt Project(ReleaseWriteStep step)
    {
        // Do not trust an acknowledged flag independently of the raw native outcome it summarizes.
        ReleaseWriteStep? expected = step.Kind switch
        {
            "upload" when step.Command is { } command => ReleaseWriteStep.FromUpload(
                new(step.Target, command), step.MaskForwardingFailed),
            "container" when step.Command is { } command =>
                ReleaseWriteStep.FromContainer(command, step.Succeeded),
            "push" when (step.Succeeded ? !step.Cancelled && step.Detail is null
                : !string.IsNullOrWhiteSpace(step.Detail)) => ReleaseWriteStep.FromPush(step.Target,
                    new(step.Succeeded, step.Cancelled, step.Detail, step.DurationSeconds)),
            "store" when !string.IsNullOrWhiteSpace(step.Detail) &&
                (!step.Ambiguous || !step.Succeeded && step.Target == "create-version" &&
                    step.Detail == FirefoxAmo.CreateResponseUnavailable) =>
                ReleaseWriteStep.FromStore(
                    step.Target, step.Succeeded, step.Detail, step.Ambiguous),
            _ => null,
        };
        if (step != expected || !double.IsFinite(step.DurationSeconds) || step.DurationSeconds < 0)
            throw new InvalidDataException("Unsupported or contradictory native write step.");
        bool ambiguous = step.Ambiguous || !step.Acknowledged && step.Command is { } raw &&
            (raw.Termination is NativeTermination.TimedOut or NativeTermination.Cancelled or
                NativeTermination.CleanupFailed or NativeTermination.InvalidOutput ||
                raw.Succeeded);
        return new(new(step.Kind, step.Target), step.Acknowledged, ambiguous);
    }
}

internal sealed record DestinationPublishPlan(
    Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> ObserveAsync,
    Func<OperationId, CancellationToken, Task<DestinationInvocation>> InvokeAsync,
    DestinationReadbackMode Readback = DestinationReadbackMode.None)
{
    internal static DestinationPublishPlan Files(PreparedRelease prepared,
        Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> observe,
        Func<ReleaseFile, CancellationToken, Task<ReleaseWriteStep>> write,
        DestinationReadbackMode readback = DestinationReadbackMode.None) =>
        new(observe, async (id, token) => new(await write(
            prepared.Files.Single(file => file.Output == id.Target), token)), readback);

    internal static DestinationPublishPlan Store(
        Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> observe,
        string submit, StoreWriter write, bool readAfterAttempt, Action? verifyBeforeSubmit = null)
    {
        return new(observe, async (id, token) =>
        {
            if (id != PublicationBinding.SubmitVersion)
                throw new InvalidDataException("Only a complete version submission can be issued.");
            DestinationInvocation upload = await Stage("upload", token);
            if (!upload.Step.Acknowledged) return upload;
            if (!token.IsCancellationRequested) verifyBeforeSubmit?.Invoke();
            if (token.IsCancellationRequested)
                return upload with { Error = ReleaseEngine.Interrupted };
            DestinationInvocation result = await Stage(submit, token);
            return result with { FirstStage = upload.Step };
        }, readAfterAttempt ? DestinationReadbackMode.AfterAttempt : DestinationReadbackMode.None);

        async Task<DestinationInvocation> Stage(string stage, CancellationToken token)
        {
            try
            {
                DestinationInvocation result = await write(stage, token);
                if (result.FirstStage is not null ||
                    PublicationBinding.Project(
                        result.Step).Operation != new OperationId("store", stage))
                    throw new InvalidDataException("Unexpected native submission stage.");
                return result;
            }
            catch (Exception failure) when (failure is HttpRequestException or IOException or
                System.Text.Json.JsonException or
                InvalidDataException or
                OperationCanceledException)
            {
                return DestinationInvocation.Store(stage, false,
                    "Store submission did not complete.");
            }
        }
    }

    internal static DestinationPublishPlan GithubRelease(PreparedRelease prepared,
        GithubReleaseDestination.Publisher publisher) =>
        new((_, token) => publisher.RefreshAsync(token), async (id, token) =>
        {
            if (id == PublicationBinding.CreateRelease)
            {
                ReleaseWriteStep container = await publisher.CreateAsync(token);
                return new(container, container.Acknowledged ? null :
                    "Release container creation failed or is ambiguous; no asset was written.");
            }
            if (id.Kind != "upload")
                throw new InvalidDataException("Unsupported GitHub release operation.");
            ReleaseFile file = prepared.Files.Single(value => value.Output == id.Target);
            ReleaseWriteStep step = await publisher.UploadAsync(file, token);
            return new(step, step.Acknowledged ? null
                : "Release asset upload failed; further writes stopped.");
        });
}
