using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal enum DestinationVerificationMode
{
    ExactBytes,
    VersionAvailable,
    StoreAccepted,
}

internal enum DestinationVisibilityMode
{
    Immediate,
    EventualIndex,
    BoundedSettle,
}

internal enum DestinationReadbackMode
{
    None,
    Required,
    AllowCancellation,
}

internal sealed record DestinationProfile(
    DestinationVerificationMode Verification,
    DestinationVisibilityMode Visibility = DestinationVisibilityMode.Immediate,
    bool PublisherOnlyRead = false,
    string[]? OrderedKinds = null,
    bool AllowsContainer = false);

internal interface IDestinationAdapter
{
    DestinationProfile Profile(ReleaseSubject subject);
    void ValidatePreparation(ReleasePreparationResult preparation);
    void VerifyOriginals(ReleasePreparationResult preparation, string originalsDirectory) =>
        ReleaseEngine.VerifyOriginals(preparation.Prepared, originalsDirectory);
    void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult observed);
    string[]? CustomDryRunWrites(ReleasePreparationResult preparation) => null;
}

internal sealed record DestinationPublishPlan(
    Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> ObserveAsync,
    Func<ReleaseFile, CancellationToken, Task<ReleaseWriteStep>>? WriteOutputAsync = null,
    Func<PreparedRelease, ReleaseObservationResult, CancellationToken,
        Task<ReleaseWriteStep?>>? EnsureContainerAsync = null,
    Func<List<ReleaseWriteStep>, CancellationToken, Task<ReleaseObservationResult?>>?
        SubmitStoreAsync = null,
    DestinationReadbackMode Readback = DestinationReadbackMode.None);

// Pure port-adapter Release state-transition and completion engine across all destinations.
internal static class ReleaseEngine
{
    internal static readonly TimeSpan DefaultSettleDeadline = TimeSpan.FromMinutes(15);
    internal static readonly TimeSpan DefaultSettleInterval = TimeSpan.FromSeconds(30);
    internal const string StoreNotObservedInDryRun =
        "The store was not observed: a dry run acquires no store credential.";

    internal static void VerifyOriginals(PreparedRelease prepared, string directory)
    {
        foreach (ReleaseFile file in prepared.Files)
        {
            string path = OwnedPath.ResolveWithin(directory, file.Path,
                "Original release directory must be absolute.",
                "Original release path leaves its transfer root.");
            OwnedFile.Verify(path, file.Size, file.Sha256,
                "Transferred original differs from preparation.");
        }
    }

    internal static bool IsSatisfiedState(DestinationProfile profile, ReleaseFileState state) =>
        state == ReleaseFileState.Exact ||
        profile.Verification == DestinationVerificationMode.VersionAvailable &&
            state == ReleaseFileState.Available ||
        profile.Verification == DestinationVerificationMode.StoreAccepted &&
            state == ReleaseFileState.Accepted;

    internal static bool AllSatisfied(DestinationProfile profile, ReleaseObservation observation) =>
        ReleasePolicy.Qualified(observation.RequiredChecks, observation.Checks) &&
        observation.Files.All(file => IsSatisfiedState(profile, file.State));

    internal static bool ObservationProceeds(DestinationProfile profile,
        ReleaseObservation observation) =>
        ReleasePolicy.Qualified(observation.RequiredChecks, observation.Checks) &&
        observation.Files.All(file =>
            file.State == ReleaseFileState.Absent ||
            IsSatisfiedState(profile, file.State) ||
            profile.Verification == DestinationVerificationMode.StoreAccepted &&
                file.State == ReleaseFileState.Unobserved &&
                (observation.Subject.DryRun || profile.PublisherOnlyRead));

    internal static bool WriteNeeded(ReleaseObservation observation) =>
        observation.Files.Any(file =>
            file.State is ReleaseFileState.Absent or ReleaseFileState.Unobserved);

    internal static bool WriteNeeded(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, ReleaseObservationResult observed)
    {
        adapter.ValidateObservation(preparation, observed);
        return WriteNeeded(observed.Observation);
    }

    internal static string[] MissingOutputs(ReleaseObservation observation,
        string[]? orderedKinds = null, PreparedRelease? prepared = null)
    {
        string[] missing = [.. observation.Files
            .Where(file => file.State is ReleaseFileState.Absent or ReleaseFileState.Unobserved)
            .Select(file => file.Output)];
        if (orderedKinds is null || prepared is null) return missing;
        string[] ordered = [.. orderedKinds.Select(kind =>
            prepared.Files.Single(file => file.Kind == kind).Output)];
        return [.. ordered.Where(missing.Contains)];
    }

    internal static string[] PlannedOutputs(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, ReleaseObservationResult observed,
        string blockedMessage)
    {
        DestinationProfile profile = adapter.Profile(preparation.Prepared.Subject);
        adapter.ValidateObservation(preparation, observed);
        if (!ObservationProceeds(profile, observed.Observation))
            throw new InvalidDataException(blockedMessage);
        return MissingOutputs(observed.Observation, profile.OrderedKinds, preparation.Prepared);
    }

    internal static ReleaseDryRun Describe(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, ReleaseObservationResult observed)
    {
        adapter.ValidatePreparation(preparation);
        adapter.ValidateObservation(preparation, observed);
        PreparedRelease prepared = preparation.Prepared;
        if (!prepared.Subject.DryRun)
            throw new InvalidDataException("Hypothetical work requires a dry-run request.");
        DestinationProfile profile = adapter.Profile(prepared.Subject);
        if (profile.Verification == DestinationVerificationMode.StoreAccepted)
        {
            string[] writes = adapter.CustomDryRunWrites(preparation)
                ?? MissingOutputs(observed.Observation, profile.OrderedKinds, prepared);
            return new(prepared.Subject, true, writes,
                [StoreNotObservedInDryRun,
                    "A live request observes the store before any submission."]);
        }
        return ReleaseCompletion.Describe(prepared, observed.Observation,
            ObservationProceeds(profile, observed.Observation)
                ? MissingOutputs(observed.Observation, profile.OrderedKinds, prepared)
                : [], profile);
    }

    internal static async Task<ReleaseObservationResult> SettleAsync(PreparedRelease prepared,
        Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> observe,
        TimeSpan deadline, TimeSpan interval, CancellationToken token, TimeProvider? clock = null)
    {
        if (deadline <= TimeSpan.Zero) return await observe(prepared, token);
        using var timer = new CancellationTokenSource(deadline, clock ?? TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timer.Token);
        while (true)
        {
            ReleaseObservationResult result = await observe(prepared, lifetime.Token);
            if (result.Observation.Files.All(file => file.State != ReleaseFileState.Absent))
                return result;
            try
            {
                await Task.Delay(interval, clock ?? TimeProvider.System, lifetime.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return result with
                {
                    Observation = result.Observation with
                    {
                        Files = [.. result.Observation.Files.Select(file =>
                            file.State == ReleaseFileState.Absent
                                ? file with { State = ReleaseFileState.Unknown } : file)],
                    },
                    Reads = [.. result.Reads, new("settle", null,
                        "Coordinate not observed before the settle deadline.")],
                };
            }
        }
    }

    internal static async Task<ReleasePublicationResult> PublishAsync(
        IDestinationAdapter adapter, ReleasePreparationResult preparation,
        string originalsDirectory, ReleaseObservationResult? initial,
        DestinationPublishPlan plan, CancellationToken token)
    {
        adapter.ValidatePreparation(preparation);
        PreparedRelease prepared = preparation.Prepared;
        ReleaseSubject subject = prepared.Subject;
        if (subject.DryRun)
            throw new InvalidDataException("Dry run cannot invoke publication.");
        ReleasePolicy.RequireEligible(subject);
        adapter.VerifyOriginals(preparation, originalsDirectory);

        DestinationProfile profile = adapter.Profile(subject);
        string[]? intended = null;
        if (initial is not null)
        {
            adapter.ValidateObservation(preparation, initial);
            if (!ObservationProceeds(profile, initial.Observation))
                throw new InvalidDataException("Current state cannot permit creation.");
            intended = MissingOutputs(initial.Observation, profile.OrderedKinds, prepared);
            if (intended.Length == 0)
                throw new InvalidDataException("An exact request needs no publisher.");
        }

        ReleaseObservationResult before = await plan.ObserveAsync(prepared, token);
        adapter.ValidateObservation(preparation, before);

        if (AllSatisfied(profile, before.Observation))
        {
            ReleaseObservationResult? sameAfter =
                profile.Verification == DestinationVerificationMode.StoreAccepted ||
                plan.Readback == DestinationReadbackMode.Required ? before : null;
            return new(subject, before, [], sameAfter, []);
        }

        if (!ObservationProceeds(profile, before.Observation))
        {
            ReleaseObservationResult? blockedAfter =
                plan.Readback == DestinationReadbackMode.Required ? before : null;
            string reason = profile.Verification == DestinationVerificationMode.StoreAccepted
                ? $"Store state does not permit submission: {before.StoreState}."
                : "Current destination state does not permit native creation.";
            return new(subject, before, [], blockedAfter, [reason]);
        }

        string[] missing = MissingOutputs(before.Observation, profile.OrderedKinds, prepared);
        if (intended is not null && initial!.Observation.Files.All(
                file => file.State != ReleaseFileState.Unobserved) &&
            missing.Any(output => !intended.Contains(output, StringComparer.Ordinal)))
            return new(subject, before, [], null,
                ["Current state would enlarge the reviewed writes."]);

        if (plan.SubmitStoreAsync is not null)
        {
            adapter.VerifyOriginals(preparation, originalsDirectory);
            token.ThrowIfCancellationRequested();
            var storeSteps = new List<ReleaseWriteStep>();
            ReleaseObservationResult? storeFinal;
            try
            {
                storeFinal = await plan.SubmitStoreAsync(storeSteps, token);
            }
            catch (Exception failure) when (failure is HttpRequestException or IOException or
                System.Text.Json.JsonException or InvalidDataException or
                OperationCanceledException)
            {
                storeSteps.Add(ReleaseWriteStep.FromStore("submission", false,
                    "Store submission did not complete."));
                storeFinal = null;
            }
            if (storeFinal is not null) adapter.ValidateObservation(preparation, storeFinal);
            string[] storeErrors = [.. storeSteps.Where(step => !step.Succeeded)
                .Select(step => $"{step.Target}: {step.Detail}")];
            return new(subject, before, [.. storeSteps], storeFinal, storeErrors);
        }

        var steps = new List<ReleaseWriteStep>();
        var errors = new List<string>();
        if (missing.Length != 0 && plan.EnsureContainerAsync is not null &&
            await plan.EnsureContainerAsync(prepared, before, token) is { } container)
        {
            steps.Add(container);
            if (!container.Acknowledged)
            {
                errors.Add(
                    "Release container creation failed or is ambiguous; no asset was written.");
                missing = [];
            }
        }

        foreach (string output in missing)
        {
            if (steps.Any(step => step.Kind != "container") && token.IsCancellationRequested)
            {
                errors.Add("Publication interrupted; further writes stopped.");
                break;
            }
            adapter.VerifyOriginals(preparation, originalsDirectory);
            token.ThrowIfCancellationRequested();
            ReleaseFile file = prepared.Files.Single(value => value.Output == output);
            ReleaseWriteStep step = await plan.WriteOutputAsync!(file, token);
            if (step.Target != output)
                throw new InvalidDataException("Publisher returned another output.");
            steps.Add(step);
            if (!step.Acknowledged)
            {
                if (plan.EnsureContainerAsync is not null)
                    errors.Add("Release asset upload failed; further writes stopped.");
                break;
            }
        }

        ReleaseObservationResult? after = null;
        if (plan.Readback != DestinationReadbackMode.None && steps.Count != 0)
        {
            try
            {
                after = await plan.ObserveAsync(prepared, token);
                adapter.ValidateObservation(preparation, after);
            }
            catch (OperationCanceledException)
                when (plan.Readback == DestinationReadbackMode.AllowCancellation)
            {
                errors.Add("Publication readback cancelled; attempted effects retained.");
            }
        }

        return new(subject, before, [.. steps], after, [.. errors]);
    }

    internal static ReleaseCompletionResult Complete(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, ReleaseObservationResult initial,
        ReleaseObservationResult? final, ReleasePublicationResult? publication)
    {
        adapter.ValidatePreparation(preparation);
        PreparedRelease prepared = preparation.Prepared;
        ReleaseSubject subject = prepared.Subject;
        if (subject.DryRun)
            throw new InvalidDataException("Dry run cannot produce a live completion result.");
        DestinationProfile profile = adapter.Profile(subject);
        adapter.ValidateObservation(preparation, initial);

        if (profile.Verification == DestinationVerificationMode.StoreAccepted)
            return CompleteStore(adapter, profile, preparation, initial, final, publication);

        if (final is null)
            throw new InvalidDataException("Missing final destination observation.");
        adapter.ValidateObservation(preparation, final);
        if (!ObservationProceeds(profile, initial.Observation))
            throw new InvalidDataException("Initial observation could not proceed.");

        string[] initialMissing = MissingOutputs(initial.Observation, profile.OrderedKinds,
            prepared);
        bool initiallyMissing = initialMissing.Length != 0;

        if (publication is not null)
            ValidatePublication(adapter, profile, preparation, initialMissing, publication);

        ReleaseUpload[] uploads = publication?.Uploads ?? [];
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(prepared, final.Observation, uploads,
            profile);

        if (publication is null)
        {
            if (!initiallyMissing) return new(outcome, false);
            return new(outcome with
            {
                State = ReleaseBusinessState.Unknown,
                Reasons = [.. outcome.Reasons,
                    "Required publication result is missing; effects remain unknown."],
            }, null);
        }

        bool attempted = publication.Steps.Length != 0;
        bool beforeAllowed = ObservationProceeds(profile, publication.Before.Observation) &&
            (attempted
                ? MissingOutputs(publication.Before.Observation, profile.OrderedKinds,
                    prepared).Length != 0
                : AllSatisfied(profile, publication.Before.Observation));

        if (profile.Visibility == DestinationVisibilityMode.BoundedSettle)
        {
            if (!beforeAllowed || publication.Errors.Length != 0)
                return new(outcome with
                {
                    State = ReleaseBusinessState.Unsatisfied,
                    Reasons = [.. outcome.Reasons,
                        "Publication did not establish a supported path."],
                }, attempted, publication, publication.Invocation);
            if (attempted && final.Observation.Files.Any(file =>
                    file.State is ReleaseFileState.Absent or ReleaseFileState.Unknown))
                return new(outcome with
                {
                    State = ReleaseBusinessState.Unknown,
                    Reasons = [.. outcome.Reasons,
                        "Uploaded coordinate was not observed within the bounded wait; " +
                        "re-dispatch the same revision later."],
                }, true, publication, publication.Invocation);
            return new(outcome, attempted, publication, publication.Invocation);
        }

        if (profile.Visibility == DestinationVisibilityMode.EventualIndex)
        {
            string[] acknowledged = [.. publication.Steps
                .Where(step => step.Kind == "upload" && step.Acknowledged)
                .Select(step => step.Target)];
            bool visibilityOnly = ReleasePolicy.Qualified(prepared.RequiredChecks,
                    prepared.Checks) &&
                ReleasePolicy.Qualified(final.Observation.RequiredChecks,
                    final.Observation.Checks) &&
                final.Observation.Files.All(file => file.State == ReleaseFileState.Exact ||
                    file.State == ReleaseFileState.Absent &&
                        acknowledged.Contains(file.Output, StringComparer.Ordinal));
            string[] pending = visibilityOnly
                ? [.. final.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output)]
                : [];
            if (pending.Length != 0)
                outcome = outcome with
                {
                    State = ReleaseBusinessState.Unknown,
                    Reasons = [.. pending.Select(output =>
                        $"Acknowledged Python upload is not yet visible: {output}.")],
                };
        }

        if (outcome.State != ReleaseBusinessState.Satisfied && publication.Errors.Length != 0)
            outcome = outcome with { Reasons = [.. outcome.Reasons, .. publication.Errors] };

        return new(outcome, attempted, publication);
    }

    private static void ValidatePublication(IDestinationAdapter adapter,
        DestinationProfile profile, ReleasePreparationResult preparation, string[] initialMissing,
        ReleasePublicationResult publication)
    {
        PreparedRelease prepared = preparation.Prepared;
        if (publication.Subject != prepared.Subject || initialMissing.Length == 0)
            throw new InvalidDataException("Mismatched or unnecessary publication result.");
        adapter.ValidateObservation(preparation, publication.Before);
        if (publication.After is not null)
        {
            if (publication.Steps.Length == 0 &&
                !AllSatisfied(profile, publication.Before.Observation) &&
                ObservationProceeds(profile, publication.Before.Observation))
                throw new InvalidDataException("Publication readback has no native invocation.");
            adapter.ValidateObservation(preparation, publication.After);
        }

        ReleaseWriteStep[] containers = [.. publication.Steps.Where(s => s.Kind == "container")];
        ReleaseWriteStep[] writes = [.. publication.Steps.Where(s => s.Kind != "container")];
        if (containers.Length > 1 ||
            containers.Length == 1 && publication.Before.ReleaseId is not null)
            throw new InvalidDataException("Unsupported repeated container creation.");

        string expectedWriteKind = profile.Visibility == DestinationVisibilityMode.BoundedSettle
            ? "push" : "upload";
        foreach (ReleaseWriteStep step in publication.Steps)
        {
            bool validKind = step.Kind == expectedWriteKind &&
                    (expectedWriteKind == "push" ? step.Command is null
                        : step.Command is not null) ||
                profile.AllowsContainer && step.Kind == "container" && step.Command is not null;
            if (!validKind || !double.IsFinite(step.DurationSeconds) || step.DurationSeconds < 0 ||
                step.Kind == "push" && (step.Succeeded && step.Detail is not null ||
                    !step.Succeeded && string.IsNullOrWhiteSpace(step.Detail)))
                throw new InvalidDataException("Unsupported or incomplete write step.");
        }

        if (writes.Length == 0) return;

        if (!ObservationProceeds(profile, publication.Before.Observation))
            throw new InvalidDataException("Unsupported write from non-proceeding state.");
        if (publication.Before.ReleaseId is null && containers is [{ Acknowledged: false }])
            throw new InvalidDataException("Unsupported asset write after failed container.");

        string[] planned = MissingOutputs(publication.Before.Observation, profile.OrderedKinds,
            prepared);
        if (writes.Length > planned.Length)
            throw new InvalidDataException("Unsupported excess write steps.");
        for (int i = 0; i < writes.Length; i++)
        {
            if (writes[i].Target != planned[i] ||
                !initialMissing.Contains(writes[i].Target, StringComparer.Ordinal) ||
                i > 0 && !writes[i - 1].Acknowledged)
                throw new InvalidDataException("Unsupported write sequence.");
        }
    }

    private static ReleaseCompletionResult CompleteStore(IDestinationAdapter adapter,
        DestinationProfile profile, ReleasePreparationResult preparation,
        ReleaseObservationResult initial, ReleaseObservationResult? final,
        ReleasePublicationResult? publication)
    {
        ReleaseSubject subject = preparation.Prepared.Subject;
        if (publication is not null)
        {
            if (publication.Subject != subject ||
                publication.Steps.Any(step => step.Kind != "store" || step.Command is not null ||
                    !double.IsFinite(step.DurationSeconds) || step.DurationSeconds < 0))
                throw new InvalidDataException("Publication is not for this request.");
            adapter.ValidateObservation(preparation, publication.Before);
            if (publication.After is not null)
                adapter.ValidateObservation(preparation, publication.After);
        }
        if (profile.PublisherOnlyRead && final is not null)
            throw new InvalidDataException(
                "Store has no verification observation outside its publisher.");

        ReleaseObservationResult? verified = profile.PublisherOnlyRead ? publication?.After : final;
        if (verified is not null) adapter.ValidateObservation(preparation, verified);

        StoreState state = verified?.StoreState ?? StoreState.Unknown;
        bool initialSatisfied = AllSatisfied(profile, initial.Observation);
        bool? attempted = publication is null ? (initialSatisfied ? false : null)
            : publication.Steps.Length > 0;

        if (!profile.PublisherOnlyRead && verified is null)
            return new(new(subject, ReleaseBusinessState.Unknown, [],
                [.. publication?.Errors ?? [], attempted == false
                    ? "Required Chrome Web Store verification observation is missing."
                    : "Required Chrome Web Store verification observation is missing; " +
                        "effects remain unknown."]), attempted, publication, StoreState: state);

        if (publication is null && !initialSatisfied)
            return new(new(subject, ReleaseBusinessState.Unknown, [],
                [.. verified?.Observation.Reasons ?? [], "Store state: " + state + ".",
                    "Required browser-store publication result is missing; effects remain " +
                    "unknown."]), null, null, StoreState: state);

        ReleaseBusinessState business = verified is not null &&
            AllSatisfied(profile, verified.Observation)
                ? ReleaseBusinessState.Satisfied
                : state is StoreState.Unknown or StoreState.Unobserved
                    ? ReleaseBusinessState.Unknown : ReleaseBusinessState.Unsatisfied;
        string[] reasons = business == ReleaseBusinessState.Satisfied ? []
            : [.. verified?.Observation.Reasons ?? [], .. publication?.Errors ?? [],
                "Store state: " + state + "."];
        return new(new(subject, business, [], reasons), attempted, publication, StoreState: state);
    }
}
