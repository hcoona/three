using WorkflowDelivery.Core;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Engine.Release;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal enum DestinationReadbackMode
{
    None,
    Required,
    AllowCancellation,
    AfterAttempt,
}

internal sealed record DestinationProfile(
    DestinationVerificationMode Verification,
    DestinationVisibilityMode Visibility = DestinationVisibilityMode.Immediate,
    DestinationVersionRead VersionRead = DestinationVersionRead.BeforeGate,
    string[]? OrderedKinds = null)
{
    internal bool PublisherReportsCompletion => VersionRead != DestinationVersionRead.BeforeGate;
}

internal interface IDestinationAdapter
{
    DestinationProfile Profile(ReleaseSubject subject);
    PublicationBinding BindPublication(ReleasePreparationResult preparation) =>
        PublicationBinding.Files(this, preparation);
    void ValidatePreparation(ReleasePreparationResult preparation);
    void VerifyOriginals(ReleasePreparationResult preparation, string originalsDirectory) =>
        ReleaseEngine.VerifyOriginals(preparation.Prepared, originalsDirectory);
    void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult observed);
    string UnconfirmedOperation(OperationAttempt attempt) =>
        $"Issued {attempt.Operation.Kind} is not yet visible: {attempt.Operation.Target}.";
    string[]? CustomDryRunWrites(ReleasePreparationResult preparation) => null;
}

// CLI transfer validation and projection around the destination-neutral publication window.
internal static class ReleaseEngine
{
    internal const string Interrupted = "Publication interrupted; further writes stopped.";
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
                !ReleaseDecisions.CanReadBeforeGate(profile.VersionRead,
                    observation.Subject.DryRun));

    internal static bool AnchorObservationProceeds(ReleaseSubject subject,
        ReleaseObservationResult anchor) =>
        ObservationProceeds(ReleaseAdapters.GithubAnchor.Profile(subject), anchor.Observation);

    internal static bool MayEnterPublisher(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, ReleaseObservationResult initial)
    {
        adapter.ValidatePreparation(preparation);
        PublicationBinding binding = adapter.BindPublication(preparation);
        return !preparation.Prepared.Subject.DryRun &&
            binding.Profile.Classify(
                binding.Project(initial), beforeGate: true).Disposition == WindowDisposition.Ready;
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
        ReleasePreparationResult preparation, ReleaseObservationResult observed,
        ReleaseObservationResult? anchorObserved = null)
    {
        ReleaseDryRun primary = DescribePrimary(adapter, preparation, observed);
        if (preparation.Prepared.Anchor is null)
        {
            if (anchorObserved is not null)
                throw new InvalidDataException("Unexpected GitHub Release anchor observation.");
            return primary;
        }
        if (anchorObserved is null)
            throw new InvalidDataException(
                "Missing GitHub Release anchor observation for dry run.");
        ReleasePreparationResult anchorPrep = ReleaseAdapters.AnchorPreparation(preparation);
        ReleaseAdapters.GithubAnchor.ValidatePreparation(anchorPrep);
        ReleaseAdapters.GithubAnchor.ValidateObservation(anchorPrep, anchorObserved);
        DestinationProfile anchorProfile = ReleaseAdapters.GithubAnchor.Profile(
            anchorPrep.Prepared.Subject);
        if (!ObservationProceeds(anchorProfile, anchorObserved.Observation))
        {
            ReleaseDryRun blocked = ReleaseCompletion.Describe(anchorPrep.Prepared,
                anchorObserved.Observation, [], anchorProfile);
            return new(primary.Subject, false, [],
                [.. primary.Reasons,
                    .. blocked.Reasons.Select(reason => "GitHub Release anchor: " + reason)],
                HypotheticalAnchorWrites: []);
        }
        if (!primary.Ready)
            return primary with { HypotheticalAnchorWrites = [] };
        ReleaseFile[] missingAssets = [.. anchorPrep.Prepared.Files.Where(file =>
            anchorObserved.Observation.Files.Single(actual => actual.Output == file.Output).State ==
                ReleaseFileState.Absent)];
        if (missingAssets.Length == 0)
            return primary with { HypotheticalAnchorWrites = [] };
        var anchorWrites = new List<string>();
        if (anchorObserved.Container == ContainerPresence.Missing)
            anchorWrites.Add("create release " + EcosystemTag(anchorPrep.Prepared));
        anchorWrites.AddRange(missingAssets.Select(asset => "upload " + asset.Name));
        return primary with { HypotheticalAnchorWrites = [.. anchorWrites] };
    }

    private static string EcosystemTag(PreparedRelease prepared) =>
        WorkflowDelivery.Ecosystems.GithubReleaseUnit.Tag(prepared.Subject,
            prepared.Files[0].Version);

    private static ReleaseDryRun DescribePrimary(IDestinationAdapter adapter,
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
            string live = profile.VersionRead switch
            {
                DestinationVersionRead.PublisherOnly =>
                    "The live publisher must observe the version as absent before submission.",
                DestinationVersionRead.Unavailable =>
                    "Edge has no version read. A first submission can proceed; after an unknown " +
                    "outcome, the owner must confirm in Partner Center that it was not received.",
                _ => "A live request observes the store before any submission.",
            };
            return new(prepared.Subject, true, writes, [StoreNotObservedInDryRun, live]);
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
        string originalsDirectory, ReleaseObservationResult initial,
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
        PublicationBinding binding = adapter.BindPublication(preparation);
        PublicationRequest request = binding.Profile.Review(binding.Project(initial));
        ReleaseObservationResult before = null!;
        ReleaseObservationResult? after = null;
        var steps = new List<ReleaseWriteStep>();
        var errors = new List<string>();
        var (admission, interrupted) = await PublicationWindow.RunAsync(request,
            async cancel =>
            {
                before = await plan.ObserveAsync(prepared, cancel);
                return binding.Project(before);
            },
            async (operation, cancel) =>
            {
                adapter.VerifyOriginals(preparation, originalsDirectory);
                cancel.ThrowIfCancellationRequested();
                DestinationInvocation invocation = await plan.InvokeAsync(operation, cancel);
                ReleaseWriteStep[] stages = invocation.Stages;
                steps.AddRange(stages);
                if (invocation.Error is not null) errors.Add(invocation.Error);
                after = invocation.After;
                return binding.Attempts(stages, invocation.Error == Interrupted).Single();
            }, token);
        if (interrupted) errors.Add(Interrupted);
        if (admission.Disposition == WindowDisposition.Enlarged)
            errors.Add("Current state would enlarge the reviewed writes.");
        if (admission.Disposition == WindowDisposition.Blocked)
            errors.Add(profile.Verification == DestinationVerificationMode.StoreAccepted
                ? $"Store state does not permit submission: {before.StoreState}."
                : "Current destination state does not permit native creation.");

        if (steps.Count == 0 && (profile.VersionRead == DestinationVersionRead.PublisherOnly ||
                plan.Readback == DestinationReadbackMode.Required ||
                profile.Verification == DestinationVerificationMode.StoreAccepted &&
                    admission.Disposition == WindowDisposition.Established))
            after = before;
        if (steps.Count != 0 && plan.Readback != DestinationReadbackMode.None)
        {
            try
            {
                after = await plan.ObserveAsync(prepared, token);
            }
            catch (OperationCanceledException)
                when (plan.Readback == DestinationReadbackMode.AllowCancellation)
            {
                errors.Add("Publication readback cancelled; attempted effects retained.");
            }
        }

        var publication = new ReleasePublicationResult(
            subject, before, [.. steps], after, [.. errors]);
        _ = PublicationWindow.Bind(binding.Profile, binding.Project(initial),
            binding.Record(publication, errors.Contains(Interrupted)));
        return publication;
    }

    internal static bool AnchorAllowed(IDestinationAdapter primaryAdapter,
        ReleasePreparationResult preparation, ReleaseObservationResult initial,
        ReleasePublicationResult? primaryPublication)
    {
        if (preparation.Prepared.Subject.DryRun) return false;
        return PublicationConclusion.PermitsAnchor(Bind(primaryAdapter, preparation, initial,
            primaryPublication).Context);
    }

    internal static void RequireAnchorAllowed(IDestinationAdapter primaryAdapter,
        ReleasePreparationResult preparation, ReleaseObservationResult initial,
        ReleasePublicationResult? primaryPublication)
    {
        if (preparation.Prepared.Anchor is null)
            throw new InvalidDataException("Release preparation carries no GitHub Release anchor.");
        if (!AnchorAllowed(primaryAdapter, preparation, initial, primaryPublication))
            throw new InvalidDataException(
                "Primary destination is not satisfied; GitHub Release anchor cannot proceed.");
    }

    internal static ReleaseCompletionResult Complete(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, ReleaseObservationResult initial,
        ReleaseObservationResult? final, ReleasePublicationResult? publication,
        ReleaseObservationResult? anchorInitial = null,
        ReleaseObservationResult? anchorFinal = null,
        ReleasePublicationResult? anchorPublication = null)
    {
        BoundPort primary = Bind(adapter, preparation, initial, publication);
        BoundPort? anchor = null;
        if (preparation.Prepared.Anchor is not null)
            anchor = Bind(
                ReleaseAdapters.GithubAnchor, ReleaseAdapters.AnchorPreparation(preparation),
                anchorInitial ?? throw new InvalidDataException(
                    "Missing initial GitHub Release anchor observation."),
                anchorPublication);
        else if (anchorInitial is not null || anchorFinal is not null ||
            anchorPublication is not null)
            throw new InvalidDataException("Unexpected GitHub Release anchor input.");

        // The retained initial anchor can prove the Host withheld the primary gate.
        // A missing receipt on a route that could enter the gate proves no such thing.
        bool anchorRefused = anchorInitial is not null &&
            !AnchorObservationProceeds(preparation.Prepared.Subject, anchorInitial);
        var (first, second, combined) = PublicationWindow.Resolve(
            primary.Context, primary.Project(final),
            anchor?.Context, anchor?.Project(anchorFinal), primaryKnownNonStart: anchorRefused);
        ReleaseCompletionResult report = primary.Format(first, final,
            "Publication skipped: the initial GitHub Release anchor observation " +
            "did not permit a write.");
        if (anchor is null) return report;
        ReleaseCompletionResult secondary = anchor.Format(second!, anchorFinal,
            "Publication skipped: primary destination did not permit a write.");
        return report with
        {
            Outcome = report.Outcome with
            {
                State = ReleaseCompletion.Wire(combined.State),
                Reasons = [.. report.Outcome.Reasons, .. secondary.Outcome.Reasons.Select(
                    reason => "GitHub Release anchor: " + reason)],
            },
            MutationAttempted = combined.Attempted,
            AnchorOutcome = secondary.Outcome,
            AnchorPublication = secondary.Publication,
        };
    }

    private static BoundPort Bind(IDestinationAdapter adapter, ReleasePreparationResult preparation,
        ReleaseObservationResult initial, ReleasePublicationResult? publication)
    {
        adapter.ValidatePreparation(preparation);
        ReleaseSubject subject = preparation.Prepared.Subject;
        if (subject.DryRun)
            throw new InvalidDataException("Dry run cannot produce a live completion result.");
        if (publication is not null && publication.Subject != subject)
            throw new InvalidDataException("Mismatched or unnecessary publication result.");
        PublicationBinding binding = adapter.BindPublication(preparation);
        PublicationRecord? record = publication is null ? null : binding.Record(publication,
            publication.Errors.Contains(Interrupted, StringComparer.Ordinal));
        return new(adapter, preparation, publication, binding,
            PublicationWindow.Bind(binding.Profile, binding.Project(initial), record));
    }

    // Keep the original command, native observations and presentation beside their transfer types.
    private sealed record BoundPort(
        IDestinationAdapter Adapter,
        ReleasePreparationResult Preparation,
        ReleasePublicationResult? Publication,
        PublicationBinding Binding, PublicationContext Context)
    {
        internal PublicationEvidence? Project(ReleaseObservationResult? observed) =>
            observed is null ? null : Binding.Project(observed);

        internal ReleaseCompletionResult Format(
            PublicationVerdict verdict, ReleaseObservationResult? final, string nonStartReason)
        {
            PreparedRelease prepared = Preparation.Prepared;
            DestinationProfile profile = Adapter.Profile(prepared.Subject);
            ReleaseObservationResult? observed =
                profile.PublisherReportsCompletion ? Publication?.After : final;
            StoreState? store = profile.Verification == DestinationVerificationMode.StoreAccepted
                ? observed?.StoreState ?? StoreState.Unknown : null;
            var reasons = new List<string>();
            if (verdict.State != PublicationStatus.Satisfied)
            {
                if (verdict.Note == ConclusionNote.Unconfirmed)
                    reasons.AddRange(profile.Visibility == DestinationVisibilityMode.BoundedSettle
                        ? [
                            "Uploaded coordinate was not observed within the bounded wait; " +
                            "re-dispatch the same revision later."
                        ]
                        : verdict.Unconfirmed.Select(Adapter.UnconfirmedOperation));
                else if (observed is not null)
                {
                    if (store is not null) reasons.AddRange(observed.Observation.Reasons ?? []);
                    else
                    {
                        reasons.AddRange(ReleaseCompletion.EvidenceReasons(
                            prepared, observed.Observation, profile));
                        reasons.AddRange(observed.Observation.Files.Where(file =>
                            file.State == ReleaseFileState.Absent)
                            .Select(file =>
                                $"Required destination file is absent: {file.Output}."));
                    }
                }
                if (store is not null) reasons.Add("Store state: " + store + ".");
                string? note = verdict.Note switch
                {
                    ConclusionNote.MissingRead => verdict.Attempted == false
                        ? "Required verification observation is missing."
                        : "Required verification observation is missing; effects remain unknown.",
                    ConclusionNote.MissingReceipt =>
                        "Required publication result is missing; effects remain unknown.",
                    ConclusionNote.NonStart => nonStartReason,
                    ConclusionNote.Unsupported =>
                        "The initial observation blocked publication; no publisher was entered.",
                    ConclusionNote.Unresolved =>
                        "The initial state or submission clearance is unresolved; " +
                        "no publisher was entered.",
                    _ => null,
                };
                if (note is not null) reasons.Add(note);
                reasons.AddRange(Publication?.Errors ?? []);
            }
            return new(new(
                prepared.Subject, ReleaseCompletion.Wire(verdict.State), Publication?.Uploads ?? [],
                [.. reasons]), verdict.Attempted, Publication, Publication?.Invocation, store);
        }
    }
}
