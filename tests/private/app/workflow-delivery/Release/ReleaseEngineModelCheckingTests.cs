using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

/// <summary>
/// Zero-dependency explicit-state BFS model checker over <see cref="ReleaseEngine"/>.
/// Exhaustively explores reachable worlds, contention, partial multi-artifact writes,
/// post-commit transport timeouts, eventual-consistency visibility lag, dropped job artifacts, and
/// cross-run crash recovery, reconstructing the shortest counterexample trace on any violation.
/// </summary>
[TestClass]
public sealed class ReleaseEngineModelCheckingTests(TestContext context)
{
    private enum ArtifactTruth : byte
    {
        Absent,
        CommittedMatching,
        CommittedConflicting,
        Unreadable,
    }

    private enum WriteFault : byte
    {
        AcknowledgedCommit,
        AcknowledgedCommitWithLag,
        FailedBeforeCommit,
        AmbiguousTimeoutAfterCommit,
        AmbiguousTimeoutBeforeCommit,
        CancelAfterStep,
    }

    private enum Contention : byte
    {
        None,
        ExternalMatchingComplete,
        ExternalConflictFirst,
        ExternalRegressionEnlarge,
    }

    private enum VerificationFault : byte
    {
        SettledRead,
        LagPersistsAtFinalRead,
        LostPublicationArtifact,
        MissingFinalObservation,
    }

    private sealed record WorldState(
        ArtifactTruth First,
        ArtifactTruth Second,
        bool ContainerCreated,
        byte LagMask,
        StoreState Store)
    {
        internal ArtifactTruth Get(int index) => index == 0 ? First : Second;

        internal WorldState WithArtifact(int index, ArtifactTruth truth, bool lagged = false)
        {
            byte bit = (byte)(1 << index);
            byte nextLag = lagged ? (byte)(LagMask | bit) : (byte)(LagMask & ~bit);
            return index == 0
                ? this with { First = truth, LagMask = nextLag }
                : this with { Second = truth, LagMask = nextLag };
        }

        internal WorldState Settled() => this with { LagMask = 0 };
    }

    private sealed record TraceNode(string Step, WorldState World, TraceNode? Parent)
    {
        internal string FormatTrace()
        {
            var chain = new List<string>();
            for (TraceNode? cursor = this; cursor is not null; cursor = cursor.Parent)
                chain.Add($"{cursor.Step} => {cursor.World}");
            chain.Reverse();
            return string.Join("\n  -> ", chain);
        }
    }

    private sealed class ModelSpec(
        string name,
        DestinationProfile profile,
        int fileCount,
        bool usesContainer = false,
        DestinationReadbackMode readback = DestinationReadbackMode.None,
        bool blindInitialStore = false) : IDestinationAdapter
    {
        internal string Name => name;
        internal int FileCount => fileCount;
        internal bool UsesContainer => usesContainer;
        internal DestinationReadbackMode Readback => readback;
        internal bool BlindInitialStore => blindInitialStore;

        public DestinationProfile Profile(ReleaseSubject subject) => profile;

        public void ValidatePreparation(ReleasePreparationResult preparation)
        {
            ReleaseCompletion.Validate(preparation.Prepared);
            ReleasePolicy.RequireQualified(preparation.Prepared);
        }

        public void VerifyOriginals(ReleasePreparationResult preparation, string originalsDirectory)
        {
        }

        public void ValidateObservation(ReleasePreparationResult preparation,
            ReleaseObservationResult observed)
        {
            if (profile.Verification == DestinationVerificationMode.StoreAccepted)
            {
                if (observed.StoreState is null ||
                    !preparation.Prepared.Subject.DryRun && !profile.PublisherOnlyRead &&
                        observed.StoreState == StoreState.Unobserved)
                    throw new InvalidDataException("Invalid modeled store observation.");
            }
            ReleaseCompletion.ValidateObservation(preparation.Prepared, observed.Observation,
                profile);
        }

        public string[]? CustomDryRunWrites(ReleasePreparationResult preparation) =>
            profile.Verification == DestinationVerificationMode.StoreAccepted
                ? ["upload", "submit"] : null;
    }

    private static readonly ModelSpec[] Specs =
    [
        new("SingleExactWithReadback",
            new(DestinationVerificationMode.ExactBytes, DestinationVisibilityMode.Immediate),
            fileCount: 1, readback: DestinationReadbackMode.AllowCancellation),
        new("SingleExactBoundedSettle",
            new(DestinationVerificationMode.ExactBytes, DestinationVisibilityMode.BoundedSettle),
            fileCount: 1, readback: DestinationReadbackMode.Required),
        new("SingleVersionBoundedSettle",
            new(DestinationVerificationMode.VersionAvailable,
                DestinationVisibilityMode.BoundedSettle),
            fileCount: 1, readback: DestinationReadbackMode.Required),
        new("TwoFileEventualIndex",
            new(DestinationVerificationMode.ExactBytes, DestinationVisibilityMode.EventualIndex,
                OrderedKinds: ["kind-0", "kind-1"]),
            fileCount: 2),
        new("ContainerAndTwoAssets",
            new(DestinationVerificationMode.ExactBytes, DestinationVisibilityMode.Immediate,
                OrderedKinds: ["kind-0", "kind-1"], AllowsContainer: true),
            fileCount: 2, usesContainer: true),
        new("StoreExternallyReadable",
            new(DestinationVerificationMode.StoreAccepted, PublisherOnlyRead: false),
            fileCount: 1),
        new("StorePublisherOnlyReadable",
            new(DestinationVerificationMode.StoreAccepted, PublisherOnlyRead: true),
            fileCount: 2),
        new("StorePublisherBlindInitial",
            new(DestinationVerificationMode.StoreAccepted, PublisherOnlyRead: true),
            fileCount: 1, blindInitialStore: true),
    ];

    [TestMethod]
    public async Task ExhaustiveBfsVerifiesSafetyAndRecoveryInvariantsAcrossAllProfiles()
    {
        int totalExploredTransitions = 0;
        int totalDistinctPostRunWorlds = 0;

        foreach (ModelSpec spec in Specs)
        {
            (int transitions, int worlds) = await ExploreSpecExhaustivelyAsync(spec);
            totalExploredTransitions += transitions;
            totalDistinctPostRunWorlds += worlds;
            context.WriteLine(
                $"Profile '{spec.Name}': verified {transitions} exhaustive transitions across " +
                $"{worlds} distinct reachable post-run worlds.");
        }

        Assert.IsGreaterThan(800, totalExploredTransitions);
        Assert.IsGreaterThan(40, totalDistinctPostRunWorlds);
    }

    [TestMethod]
    public async Task StoreExceptionRetainsPriorCompletedStepsAndValidationRejectsMismatchedKinds()
    {
        ModelSpec storeSpec = Specs.Single(s => s.Name == "StoreExternallyReadable");
        ReleasePreparationResult storePrep = BuildPreparation(storeSpec, dryRun: false);
        ReleaseObservationResult initialAbsent = Observe(storeSpec, storePrep.Prepared,
            new(ArtifactTruth.Absent, ArtifactTruth.Absent, false, 0, StoreState.Absent), true);

        ReleasePublicationResult interrupted = await ReleaseEngine.PublishAsync(storeSpec,
            storePrep, "/originals", initialAbsent,
            new(
                ObserveAsync: (_, _) => Task.FromResult(initialAbsent),
                SubmitStoreAsync: (steps, _) =>
                {
                    steps.Add(ReleaseWriteStep.FromStore("upload", true, "uploaded"));
                    throw new HttpRequestException("publish dropped");
                }),
            CancellationToken.None);

        Assert.HasCount(2, interrupted.Steps);
        Assert.AreEqual("upload", interrupted.Steps[0].Target);
        Assert.IsTrue(interrupted.Steps[0].Succeeded);
        Assert.AreEqual("submission", interrupted.Steps[1].Target);
        Assert.IsFalse(interrupted.Steps[1].Succeeded);

        // Reject store step injected into ExactBytes completion.
        ModelSpec fileSpec = Specs[0];
        ReleasePreparationResult filePrep = BuildPreparation(fileSpec, dryRun: false);
        ReleaseObservationResult fileInitial = Observe(fileSpec, filePrep.Prepared,
            new(ArtifactTruth.Absent, ArtifactTruth.Absent, false, 0, StoreState.Absent), true);
        ReleaseObservationResult fileFinal = ObserveCleanExact(fileSpec, filePrep.Prepared);
        var mismatchedPub = new ReleasePublicationResult(filePrep.Prepared.Subject, fileInitial,
            [ReleaseWriteStep.FromStore("out-0", true, "wrong-kind")], null, []);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseEngine.Complete(fileSpec, filePrep, fileInitial, fileFinal, mismatchedPub));

        // Reject contradictory StoreState vs file state in BrowserStoreDestinationAdapter.
        var realStoreTarget = new BrowserStoreTarget("chrome-web-store", "item", "pub");
        var realStoreSubject = storePrep.Prepared.Subject with
        {
            Unit = BrowserStorePreparation.Unit,
            Declaration = BrowserStorePreparation.Declaration,
        };
        ReleaseObservationResult validStoreObs = StoreHttp.Observation(realStoreSubject,
            realStoreTarget, "1.0.0", StoreState.Published, [], []);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseAdapters.BrowserStore.ValidateObservation(storePrep with
            {
                Target = realStoreTarget,
                Prepared = storePrep.Prepared with
                {
                    Subject = realStoreSubject,
                    Files = [storePrep.Prepared.Files[0] with
                    {
                        Output = "chrome-zip",
                        Name = BrowserStorePreparation.Unit,
                    }],
                },
            }, validStoreObs with { StoreState = StoreState.Conflict }));
    }

    private static async Task<(int Transitions, int PostWorlds)> ExploreSpecExhaustivelyAsync(
        ModelSpec spec)
    {
        ReleasePreparationResult livePrep = BuildPreparation(spec, dryRun: false);
        ReleasePreparationResult dryPrep = BuildPreparation(spec, dryRun: true);
        DestinationProfile profile = spec.Profile(livePrep.Prepared.Subject);

        var queue = new Queue<TraceNode>();
        var enqueuedWorlds = new HashSet<WorldState>();
        var visitedPostRun1 = new Dictionary<WorldState, TraceNode>();
        int transitions = 0;

        foreach (WorldState init in InitialWorlds(spec))
            if (enqueuedWorlds.Add(init))
                queue.Enqueue(new($"Init({init})", init, null));

        // Phase 1: Exhaustively explore DryRun + all Live Run 1 fault/contention/lag interleavings.
        while (queue.Count > 0)
        {
            TraceNode root = queue.Dequeue();

            // 1A. DryRun safety check from every initial world.
            WorldState beforeDry = root.World;
            ReleaseObservationResult dryObs = Observe(spec, dryPrep.Prepared, beforeDry,
                unprivilegedPhase: true);
            ReleaseDryRun dry = ReleaseEngine.Describe(spec, dryPrep, dryObs);
            AssertInvariant(beforeDry == root.World, "DryRun mutated world state", root);
            if (profile.Verification != DestinationVerificationMode.StoreAccepted)
            {
                bool expectedReady = ReleaseEngine.ObservationProceeds(profile, dryObs.Observation);
                AssertInvariant(dry.Ready == expectedReady,
                    $"DryRun Ready={dry.Ready} disagreed with ObservationProceeds={expectedReady}",
                    root);
            }
            transitions++;

            // 1B. Live Run 1 initial observation.
            ReleaseObservationResult initialObs = Observe(spec, livePrep.Prepared, root.World,
                unprivilegedPhase: true);
            bool initialProceeds = ReleaseEngine.ObservationProceeds(profile,
                initialObs.Observation);
            bool writeNeeded = ReleaseEngine.WriteNeeded(spec, livePrep, initialObs);

            if (!initialProceeds)
            {
                if (profile.Verification != DestinationVerificationMode.StoreAccepted)
                {
                    transitions++;
                    Assert.ThrowsExactly<InvalidDataException>(() =>
                        ReleaseEngine.Complete(spec, livePrep, initialObs, initialObs, null));
                    visitedPostRun1.TryAdd(root.World, new("InitialBlocked", root.World, root));
                    continue;
                }
            }

            if (!initialProceeds || !writeNeeded)
            {
                // Publisher is skipped when initial observation already satisfies or blocks.
                foreach (VerificationFault vFault in VerificationFaultsForSkippedPublisher(spec))
                {
                    transitions++;
                    TraceNode node = new($"SkipPublisher+{vFault}", root.World, root);
                    VerifyCompletionAndEnqueue(spec, profile, livePrep, initialObs,
                        publication: null, currentWorld: root.World, vFault,
                        attemptedAnyWrite: false, node, visitedPostRun1, enqueuedWorlds, queue);
                }
                continue;
            }

            // Publisher is entered: branch over concurrent contention before BeforeObserve.
            foreach (Contention contention in ContentionBranches(spec, root.World))
            {
                WorldState contended = ApplyContention(spec, root.World, contention);
                TraceNode contendedNode = new($"Contention({contention})", contended, root);

                foreach ((string label, ReleasePublicationResult pub, WorldState afterPub,
                    int rawWriteCount) in await RunPublisherBranchesAsync(
                        spec, livePrep, initialObs, contended, contendedNode))
                {
                    TraceNode pubNode = new(label, afterPub, contendedNode);

                    // Theorem 1 & 2: Publisher safety invariants.
                    ReleaseObservationResult before = pub.Before;
                    bool beforeProceeded = ReleaseEngine.ObservationProceeds(profile,
                        before.Observation);
                    bool beforeSatisfied = ReleaseEngine.AllSatisfied(profile, before.Observation);
                    if (!beforeProceeded || beforeSatisfied)
                        AssertInvariant(rawWriteCount == 0 && pub.Steps.Length == 0,
                            "NoUnsafeWrite violated: mutated despite blocked or satisfied Before",
                            pubNode);

                    if (initialObs.Observation.Files.All(
                            f => f.State != ReleaseFileState.Unobserved))
                    {
                        string[] initMissing = ReleaseEngine.MissingOutputs(initialObs.Observation,
                            profile.OrderedKinds, livePrep.Prepared);
                        string[] freshMissing = ReleaseEngine.MissingOutputs(before.Observation,
                            profile.OrderedKinds, livePrep.Prepared);
                        if (beforeProceeded && !beforeSatisfied &&
                            freshMissing.Any(o => !initMissing.Contains(o, StringComparer.Ordinal)))
                        {
                            AssertInvariant(rawWriteCount == 0 && pub.Steps.Length == 0,
                                "NoUnsafeWrite violated: mutated when missing scope enlarged",
                                pubNode);
                        }
                    }

                    VerifyAtMostOnceAndStopOnAmbiguity(pub, rawWriteCount, pubNode);

                    foreach (VerificationFault vFault in VerificationFaultsAfterPublisher(
                        spec, afterPub))
                    {
                        transitions++;
                        TraceNode finalNode = new($"Verify({vFault})", afterPub, pubNode);
                        VerifyCompletionAndEnqueue(spec, profile, livePrep, initialObs,
                            vFault == VerificationFault.LostPublicationArtifact ? null : pub,
                            afterPub, vFault, attemptedAnyWrite: pub.Steps.Length > 0,
                            finalNode, visitedPostRun1, enqueuedWorlds, queue);
                    }
                }
            }
        }

        // Phase 2: From EVERY distinct reachable post-Run-1 world, settle lag and verify Run 2
        // recovery convergence (Theorem 5).
        foreach ((WorldState postWorld, TraceNode originTrace) in visitedPostRun1)
        {
            if (spec.BlindInitialStore) continue; // Edge WD-REL-008: manual Partner Center check.

            WorldState settled = postWorld.Settled();
            bool hasConflictOrCorruption = HasAnyConflictOrUnreadable(spec, settled);
            TraceNode recoveryRoot = new("RecoveryRun2Start(Settled)", settled, originTrace);
            transitions++;

            ReleaseObservationResult recInitial = Observe(spec, livePrep.Prepared, settled,
                unprivilegedPhase: true);
            if (hasConflictOrCorruption)
            {
                // Corrupted or conflicting external state must NEVER falsely satisfy on recovery.
                if (profile.Verification == DestinationVerificationMode.StoreAccepted &&
                    profile.PublisherOnlyRead)
                {
                    (ReleasePublicationResult recPub, WorldState afterRec, int writes) =
                        await ExecuteCleanPublisherAsync(spec, livePrep, recInitial, settled);
                    AssertInvariant(writes == 0,
                        "Recovery mutated conflicting/unreadable publisher-only store",
                        recoveryRoot);
                    ReleaseCompletionResult blocked = ReleaseEngine.Complete(spec, livePrep,
                        recInitial, null, recPub);
                    AssertInvariant(blocked.Outcome.State != ReleaseBusinessState.Satisfied,
                        "Conflicting/unreadable store falsely reported Satisfied on recovery",
                        recoveryRoot);
                }
                else
                {
                    AssertInvariant(!ReleaseEngine.ObservationProceeds(profile,
                            recInitial.Observation),
                        "Conflicting/unreadable state unexpectedly proceeded on recovery",
                        recoveryRoot);
                    ReleaseCompletionResult blocked = ReleaseEngine.Complete(spec, livePrep,
                        ObserveCleanExact(spec, livePrep.Prepared), recInitial, null);
                    AssertInvariant(blocked.Outcome.State != ReleaseBusinessState.Satisfied,
                        "Conflicting/unreadable final state falsely reported Satisfied",
                        recoveryRoot);
                }
                continue;
            }

            // Healthy (Absent / Partial / Complete) post-crash world MUST deterministically
            // converge to Satisfied in one clean recovery run, writing ONLY still-absent outputs!
            int expectedMissingCount = CountAbsentArtifacts(spec, settled);
            bool recWriteNeeded = ReleaseEngine.WriteNeeded(spec, livePrep, recInitial);

            ReleasePublicationResult? cleanPub = null;
            WorldState finalWorld = settled;
            int recoveryWrites = 0;
            if (recWriteNeeded)
            {
                (cleanPub, finalWorld, recoveryWrites) = await ExecuteCleanPublisherAsync(
                    spec, livePrep, recInitial, settled);
            }

            if (profile.Verification != DestinationVerificationMode.StoreAccepted)
            {
                int expectedWrites = expectedMissingCount +
                    (spec.UsesContainer && expectedMissingCount > 0 && !settled.ContainerCreated
                        ? 1 : 0);
                AssertInvariant(recoveryWrites == expectedWrites,
                    $"Recovery idempotency violated: expected {expectedWrites} writes for " +
                    $"remaining missing set, actual={recoveryWrites}", recoveryRoot);
            }
            else if (expectedMissingCount == 0)
            {
                AssertInvariant(recoveryWrites == 0,
                    "Recovery re-submitted an already accepted store version", recoveryRoot);
            }

            ReleaseObservationResult? recFinal = profile.PublisherOnlyRead
                ? null
                : Observe(spec, livePrep.Prepared, finalWorld.Settled(), unprivilegedPhase: false);
            ReleaseCompletionResult converged = ReleaseEngine.Complete(spec, livePrep, recInitial,
                recFinal, cleanPub);
            AssertInvariant(converged.Outcome.State == ReleaseBusinessState.Satisfied,
                $"RecoveryConvergence violated: clean Run 2 ended in {converged.Outcome.State} " +
                $"({string.Join("; ", converged.Outcome.Reasons)}) instead of Satisfied",
                recoveryRoot);
        }

        return (transitions, visitedPostRun1.Count);
    }

    private static void VerifyCompletionAndEnqueue(ModelSpec spec, DestinationProfile profile,
        ReleasePreparationResult prep, ReleaseObservationResult initial,
        ReleasePublicationResult? publication, WorldState currentWorld, VerificationFault vFault,
        bool attemptedAnyWrite, TraceNode node, Dictionary<WorldState, TraceNode> visitedPostRun1,
        HashSet<WorldState> enqueuedWorlds, Queue<TraceNode> queue)
    {
        WorldState effectiveWorld = vFault == VerificationFault.SettledRead
            ? currentWorld.Settled() : currentWorld;

        ReleaseObservationResult? finalObs = null;
        if (!profile.PublisherOnlyRead && vFault != VerificationFault.MissingFinalObservation)
        {
            if (profile.Visibility == DestinationVisibilityMode.BoundedSettle &&
                currentWorld.LagMask != 0 &&
                publication?.Invocation is { ReturnedSuccessfully: true })
            {
                var clock = new FakeTimeProvider();
                int polls = 0;
                finalObs = ReleaseEngine.SettleAsync(prep.Prepared, (_, _) =>
                {
                    polls++;
                    WorldState pollWorld = vFault == VerificationFault.SettledRead && polls >= 2
                        ? currentWorld.Settled() : currentWorld;
                    if (pollWorld.LagMask != 0 && polls >= 2)
                        clock.Advance(TimeSpan.FromMinutes(20));
                    return Task.FromResult(Observe(spec, prep.Prepared, pollWorld,
                        unprivilegedPhase: false));
                }, TimeSpan.FromMinutes(15), TimeSpan.Zero, CancellationToken.None, clock)
                    .GetAwaiter().GetResult();
                if (vFault == VerificationFault.SettledRead &&
                    !HasAnyConflictOrUnreadable(spec, effectiveWorld) &&
                    IsTrulyCompleteInGroundTruth(spec, effectiveWorld))
                    AssertInvariant(polls >= 2 &&
                        ReleaseEngine.AllSatisfied(profile, finalObs.Observation),
                        "SettleAsync did not observe delayed coordinate on second poll", node);
            }
            else
            {
                finalObs = Observe(spec, prep.Prepared, effectiveWorld, unprivilegedPhase: false);
            }
        }

        ReleaseCompletionResult completion = ReleaseEngine.Complete(spec, prep, initial, finalObs,
            publication);

        // Theorem 3: SoundSatisfaction — Satisfied implies every artifact in ground-truth world
        // is genuinely committed & matching (and container exists when required).
        if (completion.Outcome.State == ReleaseBusinessState.Satisfied)
        {
            AssertInvariant(IsTrulyCompleteInGroundTruth(spec, effectiveWorld),
                "SoundSatisfaction violated: reported Satisfied while ground-truth world is " +
                $"incomplete or conflicting ({effectiveWorld})", node);
            bool initialNeededWrite = ReleaseEngine.WriteNeeded(spec, prep, initial);
            if (initialNeededWrite)
                AssertInvariant(publication is not null,
                    "SoundSatisfaction violated: reported Satisfied despite lost publication " +
                    "artifact when initial state required writes", node);
        }

        // Theorem 4: HonestUncertainty — Lost publication artifact when write was needed MUST be
        // Unknown with null MutationAttempted, even if an ambiguous write actually committed!
        if (vFault == VerificationFault.LostPublicationArtifact &&
            ReleaseEngine.WriteNeeded(spec, prep, initial))
        {
            AssertInvariant(
                completion.Outcome.State == ReleaseBusinessState.Unknown &&
                completion.MutationAttempted is null,
                $"HonestUncertainty violated: lost publication artifact yielded " +
                $"State={completion.Outcome.State}, " +
                $"MutationAttempted={completion.MutationAttempted}",
                node);
        }

        // Theorem 4B: Visibility lag on otherwise healthy acknowledged upload MUST be Unknown
        // (never Unsatisfied).
        if (vFault == VerificationFault.LagPersistsAtFinalRead && effectiveWorld.LagMask != 0 &&
            !HasAnyConflictOrUnreadable(spec, effectiveWorld) && publication is not null &&
            publication.Errors.Length == 0 &&
            publication.Steps.All(s => s.Acknowledged) && attemptedAnyWrite &&
            IsTrulyCompleteInGroundTruth(spec, effectiveWorld.Settled()))
        {
            AssertInvariant(completion.Outcome.State == ReleaseBusinessState.Unknown,
                $"VisibilityLag soundness violated: lagged acknowledged upload yielded " +
                $"{completion.Outcome.State} instead of Unknown", node);
        }

        visitedPostRun1.TryAdd(effectiveWorld, node);
        if (!spec.BlindInitialStore)
        {
            if (enqueuedWorlds.Add(effectiveWorld))
                queue.Enqueue(new("NextRunFromPostState", effectiveWorld, node));
            WorldState settledNext = effectiveWorld.Settled();
            if (enqueuedWorlds.Add(settledNext))
                queue.Enqueue(new("NextRunAfterSettle", settledNext, node));
        }
    }

    private static void VerifyAtMostOnceAndStopOnAmbiguity(ReleasePublicationResult pub,
        int rawWriteCount, TraceNode node)
    {
        AssertInvariant(rawWriteCount == pub.Steps.Length,
            $"Recorded steps ({pub.Steps.Length}) != raw write invocations ({rawWriteCount})",
            node);
        var seenTargets = new HashSet<string>(StringComparer.Ordinal);
        bool sawUnacknowledged = false;
        foreach (ReleaseWriteStep step in pub.Steps)
        {
            AssertInvariant(!sawUnacknowledged,
                "StopOnAmbiguity violated: executed write step after an unacknowledged step",
                node);
            AssertInvariant(seenTargets.Add($"{step.Kind}:{step.Target}"),
                $"AtMostOnce violated: duplicate step {step.Kind}:{step.Target}", node);
            if (!step.Acknowledged) sawUnacknowledged = true;
        }
    }

    private static async Task<List<(string Label, ReleasePublicationResult Pub, WorldState World,
        int RawWrites)>> RunPublisherBranchesAsync(ModelSpec spec, ReleasePreparationResult prep,
        ReleaseObservationResult initial, WorldState startWorld, TraceNode parent)
    {
        var results = new List<(string, ReleasePublicationResult, WorldState, int)>();
        DestinationProfile profile = spec.Profile(prep.Prepared.Subject);

        if (profile.Verification == DestinationVerificationMode.StoreAccepted)
        {
            StoreState[] postOutcomes = spec.BlindInitialStore
                ? [StoreState.Received, StoreState.Unknown, StoreState.Conflict]
                : [StoreState.InReview, StoreState.Published, StoreState.Unknown,
                    StoreState.Conflict];
            foreach (StoreState outcome in postOutcomes)
            {
                WorldState world = startWorld;
                int writes = 0;
                ReleasePublicationResult pub = await ReleaseEngine.PublishAsync(spec, prep,
                    "/originals", initial, new(
                        ObserveAsync: (_, _) => Task.FromResult(
                            Observe(spec, prep.Prepared, world, unprivilegedPhase: false)),
                        SubmitStoreAsync: (steps, _) =>
                        {
                            writes++;
                            bool ok = outcome is StoreState.Received or StoreState.InReview or
                                StoreState.Published;
                            world = world with { Store = outcome };
                            steps.Add(ReleaseWriteStep.FromStore("submit", ok,
                                ok ? "ok" : "failed"));
                            ReleaseObservationResult? after = profile.PublisherOnlyRead
                                ? Observe(spec, prep.Prepared, world, unprivilegedPhase: false)
                                : null;
                            return Task.FromResult(after);
                        }), CancellationToken.None);
                results.Add(($"StorePublish({outcome})", pub, world, writes));
            }
            return results;
        }

        // Non-store: enumerate container fault (if needed) x first-step fault x second-step fault.
        bool[] containerModes = spec.UsesContainer && !startWorld.ContainerCreated
            ? [true, false] : [true];
        WriteFault[] step1Faults = StepFaultsFor(spec);
        WriteFault[] step2Faults = spec.FileCount == 2
            ? [WriteFault.AcknowledgedCommit, WriteFault.FailedBeforeCommit,
                WriteFault.AmbiguousTimeoutAfterCommit]
            : [WriteFault.AcknowledgedCommit];

        foreach (bool containerOk in containerModes)
        {
            foreach (WriteFault f1 in step1Faults)
            {
                foreach (WriteFault f2 in step2Faults)
                {
                    if (!containerOk && (f1 != step1Faults[0] || f2 != step2Faults[0])) continue;
                    if (f1 is not (WriteFault.AcknowledgedCommit or
                            WriteFault.AcknowledgedCommitWithLag) && f2 != step2Faults[0])
                        continue;

                    WorldState world = startWorld;
                    int writes = 0;
                    using var cts = new CancellationTokenSource();
                    int outputCallIndex = 0;

                    ReleasePublicationResult pub = await ReleaseEngine.PublishAsync(spec, prep,
                        "/originals", spec.Readback == DestinationReadbackMode.Required
                            ? null : initial,
                        new(
                            ObserveAsync: (_, token) =>
                            {
                                if (f1 == WriteFault.CancelAfterStep && writes > 0 &&
                                    spec.Readback == DestinationReadbackMode.AllowCancellation)
                                    throw new OperationCanceledException(token);
                                return Task.FromResult(Observe(spec, prep.Prepared, world,
                                    unprivilegedPhase: false));
                            },
                            EnsureContainerAsync: spec.UsesContainer
                                ? (_, before, _) =>
                                {
                                    if (before.ReleaseId is not null)
                                        return Task.FromResult<ReleaseWriteStep?>(null);
                                    writes++;
                                    if (containerOk) world = world with { ContainerCreated = true };
                                    var cmd = new NativeCommandResult(NativeTermination.Exited,
                                        containerOk ? 0 : 500, "", "", 0.1,
                                        containerOk ? null : "container error");
                                    return Task.FromResult<ReleaseWriteStep?>(
                                        ReleaseWriteStep.FromContainer(cmd, containerOk));
                                }
                    : null,
                            WriteOutputAsync: (file, _) =>
                            {
                                writes++;
                                int fileIdx = file.Output == "out-0" ? 0 : 1;
                                WriteFault fault = outputCallIndex++ == 0 ? f1 : f2;
                                ReleaseWriteStep step = ApplyFileFault(spec, file.Output, fileIdx,
                                    fault, cts, ref world);
                                return Task.FromResult(step);
                            },
                            Readback: spec.Readback),
                        cts.Token);

                    results.Add(($"Pub(c={containerOk},f1={f1},f2={f2})", pub, world, writes));
                }
            }
        }

        return results;
    }

    private static async Task<(ReleasePublicationResult Pub, WorldState World, int Writes)>
        ExecuteCleanPublisherAsync(ModelSpec spec, ReleasePreparationResult prep,
            ReleaseObservationResult initial, WorldState startWorld)
    {
        WorldState world = startWorld;
        int writes = 0;
        DestinationProfile profile = spec.Profile(prep.Prepared.Subject);

        if (profile.Verification == DestinationVerificationMode.StoreAccepted)
        {
            ReleasePublicationResult storePub = await ReleaseEngine.PublishAsync(spec, prep,
                "/originals", initial, new(
                    ObserveAsync: (_, _) => Task.FromResult(Observe(spec, prep.Prepared, world,
                        unprivilegedPhase: false)),
                    SubmitStoreAsync: (steps, _) =>
                    {
                        writes++;
                        world = world with { Store = StoreState.InReview };
                        steps.Add(ReleaseWriteStep.FromStore("submit", true, "ok"));
                        ReleaseObservationResult? after = profile.PublisherOnlyRead
                            ? Observe(spec, prep.Prepared, world, unprivilegedPhase: false) : null;
                        return Task.FromResult(after);
                    }), CancellationToken.None);
            return (storePub, world, writes);
        }

        ReleasePublicationResult pub = await ReleaseEngine.PublishAsync(spec, prep, "/originals",
            spec.Readback == DestinationReadbackMode.Required ? null : initial,
            new(
                ObserveAsync: (_, _) => Task.FromResult(Observe(spec, prep.Prepared, world,
                    unprivilegedPhase: false)),
                EnsureContainerAsync: spec.UsesContainer
                    ? (_, before, _) =>
                    {
                        if (before.ReleaseId is not null)
                            return Task.FromResult<ReleaseWriteStep?>(null);
                        writes++;
                        world = world with { ContainerCreated = true };
                        var cmd = new NativeCommandResult(NativeTermination.Exited, 0, "", "",
                            0.1, null);
                        return Task.FromResult<ReleaseWriteStep?>(
                            ReleaseWriteStep.FromContainer(cmd, true));
                    }
        : null,
                WriteOutputAsync: (file, _) =>
                {
                    writes++;
                    int idx = file.Output == "out-0" ? 0 : 1;
                    world = world.WithArtifact(idx, ArtifactTruth.CommittedMatching, lagged: false);
                    return Task.FromResult(MakeSuccessStep(spec, file.Output));
                },
                Readback: spec.Readback),
            CancellationToken.None);
        return (pub, world, writes);
    }

    private static ReleaseWriteStep ApplyFileFault(ModelSpec spec, string output, int fileIdx,
        WriteFault fault, CancellationTokenSource cts, ref WorldState world)
    {
        bool pushKind = spec.Profile(default!).Visibility ==
            DestinationVisibilityMode.BoundedSettle;
        switch (fault)
        {
            case WriteFault.AcknowledgedCommit:
                world = world.WithArtifact(fileIdx, ArtifactTruth.CommittedMatching, lagged: false);
                return MakeSuccessStep(spec, output);
            case WriteFault.AcknowledgedCommitWithLag:
                world = world.WithArtifact(fileIdx, ArtifactTruth.CommittedMatching, lagged: true);
                return MakeSuccessStep(spec, output);
            case WriteFault.FailedBeforeCommit:
                return MakeFailedStep(pushKind, output, "FailedBeforeCommit");
            case WriteFault.AmbiguousTimeoutAfterCommit:
                world = world.WithArtifact(fileIdx, ArtifactTruth.CommittedMatching, lagged: false);
                return MakeFailedStep(pushKind, output, "TimeoutException");
            case WriteFault.AmbiguousTimeoutBeforeCommit:
                return MakeFailedStep(pushKind, output, "TimeoutException");
            case WriteFault.CancelAfterStep:
                world = world.WithArtifact(fileIdx, ArtifactTruth.CommittedMatching, lagged: false);
                cts.Cancel();
                return MakeSuccessStep(spec, output);
            default:
                throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private static ReleaseWriteStep MakeSuccessStep(ModelSpec spec, string output) =>
        spec.Profile(default!).Visibility == DestinationVisibilityMode.BoundedSettle
            ? ReleaseWriteStep.FromPush(output, new(true, false, null, 0.1))
            : ReleaseWriteStep.FromUpload(new(output,
                new(NativeTermination.Exited, 0, "", "", 0.1, null)));

    private static ReleaseWriteStep MakeFailedStep(bool pushKind, string output, string reason) =>
        pushKind
            ? ReleaseWriteStep.FromPush(output, new(false, false, reason, 0.1))
            : ReleaseWriteStep.FromUpload(new(output,
                new(NativeTermination.TimedOut, null, "", "", 0.1, reason)));

    private static IEnumerable<WorldState> InitialWorlds(ModelSpec spec)
    {
        if (spec.Profile(default!).Verification == DestinationVerificationMode.StoreAccepted)
        {
            StoreState[] states = spec.BlindInitialStore
                ? [StoreState.Unobserved]
                : [StoreState.Absent, StoreState.InReview, StoreState.Published,
                    StoreState.Conflict, StoreState.Unknown];
            foreach (StoreState state in states)
                yield return new(ArtifactTruth.Absent, ArtifactTruth.Absent, false, 0, state);
            yield break;
        }

        ArtifactTruth[] truths = [ArtifactTruth.Absent, ArtifactTruth.CommittedMatching,
            ArtifactTruth.CommittedConflicting, ArtifactTruth.Unreadable];
        foreach (ArtifactTruth first in truths)
        {
            if (spec.FileCount == 1)
            {
                yield return new(first, ArtifactTruth.Absent,
                    first != ArtifactTruth.Absent, 0, StoreState.Absent);
                continue;
            }
            foreach (ArtifactTruth second in truths)
            {
                bool anyPresent = first != ArtifactTruth.Absent || second != ArtifactTruth.Absent;
                yield return new(first, second, ContainerCreated: anyPresent, 0,
                    StoreState.Absent);
                if (spec.UsesContainer && !anyPresent)
                    yield return new(first, second, ContainerCreated: true, 0, StoreState.Absent);
            }
        }
    }

    private static WriteFault[] StepFaultsFor(ModelSpec spec)
    {
        DestinationVisibilityMode vis = spec.Profile(default!).Visibility;
        var faults = new List<WriteFault>
        {
            WriteFault.AcknowledgedCommit,
            WriteFault.FailedBeforeCommit,
            WriteFault.AmbiguousTimeoutAfterCommit,
            WriteFault.AmbiguousTimeoutBeforeCommit,
        };
        if (vis is DestinationVisibilityMode.EventualIndex or
            DestinationVisibilityMode.BoundedSettle)
            faults.Add(WriteFault.AcknowledgedCommitWithLag);
        if (spec.FileCount > 1 || spec.Readback == DestinationReadbackMode.AllowCancellation)
            faults.Add(WriteFault.CancelAfterStep);
        return [.. faults];
    }

    private static Contention[] ContentionBranches(ModelSpec spec, WorldState world)
    {
        if (spec.BlindInitialStore) return [Contention.None];
        if (spec.Profile(default!).Verification == DestinationVerificationMode.StoreAccepted)
            return [Contention.None, Contention.ExternalMatchingComplete,
                Contention.ExternalConflictFirst];
        if (spec.FileCount == 2 && world.First == ArtifactTruth.CommittedMatching &&
            world.Second == ArtifactTruth.Absent)
            return [Contention.None, Contention.ExternalMatchingComplete,
                Contention.ExternalConflictFirst, Contention.ExternalRegressionEnlarge];
        return [Contention.None, Contention.ExternalMatchingComplete,
            Contention.ExternalConflictFirst];
    }

    private static WorldState ApplyContention(ModelSpec spec, WorldState world, Contention c) =>
        c switch
        {
            Contention.None => world,
            Contention.ExternalMatchingComplete when
                spec.Profile(default!).Verification == DestinationVerificationMode.StoreAccepted =>
                world with { Store = StoreState.Published },
            Contention.ExternalMatchingComplete =>
                new(ArtifactTruth.CommittedMatching,
                    spec.FileCount == 2 ? ArtifactTruth.CommittedMatching : ArtifactTruth.Absent,
                    ContainerCreated: true, LagMask: 0, StoreState.Absent),
            Contention.ExternalConflictFirst when
                spec.Profile(default!).Verification == DestinationVerificationMode.StoreAccepted =>
                world with { Store = StoreState.Conflict },
            Contention.ExternalConflictFirst =>
                world with { First = ArtifactTruth.CommittedConflicting, ContainerCreated = true },
            Contention.ExternalRegressionEnlarge =>
                world with { First = ArtifactTruth.Absent, Second = ArtifactTruth.Absent },
            _ => world,
        };

    private static VerificationFault[] VerificationFaultsForSkippedPublisher(ModelSpec spec) =>
        spec.Profile(default!).Verification == DestinationVerificationMode.StoreAccepted &&
        !spec.Profile(default!).PublisherOnlyRead
            ? [VerificationFault.SettledRead, VerificationFault.MissingFinalObservation]
            : [VerificationFault.SettledRead];

    private static VerificationFault[] VerificationFaultsAfterPublisher(ModelSpec spec,
        WorldState world)
    {
        var list = new List<VerificationFault>
        {
            VerificationFault.SettledRead,
            VerificationFault.LostPublicationArtifact,
        };
        if (world.LagMask != 0)
            list.Add(VerificationFault.LagPersistsAtFinalRead);
        if (spec.Profile(default!).Verification == DestinationVerificationMode.StoreAccepted &&
            !spec.Profile(default!).PublisherOnlyRead)
            list.Add(VerificationFault.MissingFinalObservation);
        return [.. list];
    }

    private static ReleaseObservationResult Observe(ModelSpec spec, PreparedRelease prepared,
        WorldState world, bool unprivilegedPhase)
    {
        DestinationProfile profile = spec.Profile(prepared.Subject);
        if (profile.Verification == DestinationVerificationMode.StoreAccepted)
        {
            StoreState state = unprivilegedPhase &&
                (prepared.Subject.DryRun || profile.PublisherOnlyRead)
                    ? StoreState.Unobserved
                    : world.Store;
            ReleaseFileState fileState = state switch
            {
                StoreState.Absent => ReleaseFileState.Absent,
                StoreState.Received or StoreState.InReview or StoreState.Published =>
                    ReleaseFileState.Accepted,
                StoreState.Unobserved => ReleaseFileState.Unobserved,
                StoreState.Conflict => ReleaseFileState.Conflict,
                _ => ReleaseFileState.Unknown,
            };
            ReleaseCheckStatus checkStatus = state is StoreState.Conflict or StoreState.Unknown
                ? ReleaseCheckStatus.Failed : ReleaseCheckStatus.Passed;
            return new(new(prepared.Subject,
                [.. prepared.Files.Select(f => new ReleaseFileObservation(f.Output, f.Name,
                    f.Version, fileState, null, null))],
                ["store-status"], [new("store-status", checkStatus)],
                state is StoreState.Conflict or StoreState.Unknown ? ["store-blocked"] : null),
                [], StoreState: state);
        }

        var files = new ReleaseFileObservation[prepared.Files.Length];
        bool checkPassed = true;
        for (int i = 0; i < prepared.Files.Length; i++)
        {
            ReleaseFile expected = prepared.Files[i];
            ArtifactTruth truth = world.Get(i);
            bool isLagged = (world.LagMask & (1 << i)) != 0;
            ReleaseFileState state = isLagged ? ReleaseFileState.Absent : truth switch
            {
                ArtifactTruth.Absent => ReleaseFileState.Absent,
                ArtifactTruth.CommittedMatching =>
                    profile.Verification == DestinationVerificationMode.VersionAvailable
                        ? ReleaseFileState.Available : ReleaseFileState.Exact,
                ArtifactTruth.CommittedConflicting => ReleaseFileState.Conflict,
                _ => ReleaseFileState.Unknown,
            };
            if (state is ReleaseFileState.Conflict or ReleaseFileState.Unknown)
                checkPassed = false;
            files[i] = new(expected.Output, expected.Name, expected.Version, state,
                state == ReleaseFileState.Exact ? expected.Size : null,
                state == ReleaseFileState.Exact ? expected.Sha256 : null);
        }

        long? releaseId = spec.UsesContainer && world.ContainerCreated ? 42L : null;
        return new(new(prepared.Subject, files, ["remote-check"],
            [new("remote-check", checkPassed ? ReleaseCheckStatus.Passed
                : ReleaseCheckStatus.Failed)]), [], ReleaseId: releaseId);
    }

    private static ReleaseObservationResult ObserveCleanExact(ModelSpec spec,
        PreparedRelease prepared)
    {
        WorldState clean = new(ArtifactTruth.CommittedMatching, ArtifactTruth.CommittedMatching,
            ContainerCreated: true, LagMask: 0, StoreState.Published);
        return Observe(spec, prepared, clean, unprivilegedPhase: false);
    }

    private static bool IsTrulyCompleteInGroundTruth(ModelSpec spec, WorldState world)
    {
        DestinationProfile profile = spec.Profile(default!);
        if (profile.Verification == DestinationVerificationMode.StoreAccepted)
            return world.Store is StoreState.Received or StoreState.InReview or
                StoreState.Published;
        if (spec.UsesContainer && !world.ContainerCreated) return false;
        for (int i = 0; i < spec.FileCount; i++)
            if (world.Get(i) != ArtifactTruth.CommittedMatching ||
                (world.LagMask & (1 << i)) != 0)
                return false;
        return true;
    }

    private static bool HasAnyConflictOrUnreadable(ModelSpec spec, WorldState world)
    {
        if (spec.Profile(default!).Verification == DestinationVerificationMode.StoreAccepted)
            return world.Store is StoreState.Conflict or StoreState.Unknown;
        for (int i = 0; i < spec.FileCount; i++)
            if (world.Get(i) is ArtifactTruth.CommittedConflicting or ArtifactTruth.Unreadable)
                return true;
        return false;
    }

    private static int CountAbsentArtifacts(ModelSpec spec, WorldState world)
    {
        if (spec.Profile(default!).Verification == DestinationVerificationMode.StoreAccepted)
            return world.Store == StoreState.Absent ? 1 : 0;
        int count = 0;
        for (int i = 0; i < spec.FileCount; i++)
            if (world.Get(i) == ArtifactTruth.Absent) count++;
        return count;
    }

    private static ReleasePreparationResult BuildPreparation(ModelSpec spec, bool dryRun)
    {
        DestinationProfile profile = spec.Profile(default!);
        string destination = profile.Verification switch
        {
            DestinationVerificationMode.VersionAvailable => "nugetorg",
            DestinationVerificationMode.StoreAccepted when !profile.PublisherOnlyRead =>
                "chrome-web-store",
            DestinationVerificationMode.StoreAccepted when spec.BlindInitialStore =>
                "edge-addons",
            DestinationVerificationMode.StoreAccepted => "firefox-amo",
            _ => "model-destination",
        };
        var subject = new ReleaseSubject(new string('a', 40), "refs/heads/main", "model-unit",
            "src/public/lib/model/workflow-delivery.release-unit.yml", "official", destination,
            dryRun);
        var files = Enumerable.Range(0, spec.FileCount).Select(i =>
        {
            byte[] payload = Encoding.UTF8.GetBytes($"payload-{spec.Name}-{i}");
            return new ReleaseFile("build", $"out-{i}", $"kind-{i}", "model-pkg", "1.0.0",
                $"original/file-{i}.bin", payload.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(payload)));
        }).ToArray();
        var prepared = new PreparedRelease(subject, files, ["qualify"],
            [new("qualify", ReleaseCheckStatus.Passed)]);
        return new(prepared, new(StringComparer.Ordinal), [], []);
    }

    private static void AssertInvariant(bool condition, string message, TraceNode trace)
    {
        if (!condition)
            Assert.Fail($"{message}\nShortest counterexample trace:\n  -> {trace.FormatTrace()}");
    }
}
