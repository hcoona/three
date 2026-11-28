using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace WorkflowDelivery.Core;

internal readonly record struct OperationId(string Kind, string Target);
internal sealed record PublicationOperation(OperationId Id,
    params ImmutableArray<OperationId> Requires);
internal enum OperationEvidence { Unknown, Conflict, Established, Missing, Unobserved }
internal enum WindowDisposition { Ready, Established, Blocked, Enlarged, Unresolved }
internal enum SubmissionClearance { Unresolved, First, OwnerChecked }
internal enum DestinationVerificationMode { ExactBytes, VersionAvailable, StoreAccepted }
internal enum DestinationVisibilityMode { Immediate, EventualRead, BoundedSettle }
internal sealed record PublicationEvidence(bool Qualified,
    ImmutableDictionary<OperationId, OperationEvidence> Operations);
internal readonly record struct OperationAttempt(OperationId Operation, bool Acknowledged,
    bool Ambiguous = false);

internal sealed class PublicationProfile
{
    private readonly bool supportsPartial;

    internal PublicationProfile(IEnumerable<PublicationOperation> operations,
        DestinationVersionRead read, bool supportsPartial = true,
        SubmissionClearance clearance = SubmissionClearance.Unresolved,
        DestinationVerificationMode proof = DestinationVerificationMode.ExactBytes,
        DestinationVisibilityMode visibility = DestinationVisibilityMode.Immediate)
    {
        Operations = [.. operations];
        Read = read;
        Clearance = clearance;
        Proof = proof;
        Visibility = visibility;
        this.supportsPartial = supportsPartial;
        var seen = new HashSet<OperationId>();
        if (Operations.IsEmpty || read == DestinationVersionRead.Unavailable &&
                (Operations.Length != 1 || proof == DestinationVerificationMode.ExactBytes) ||
            proof != DestinationVerificationMode.ExactBytes &&
            Operations.Any(op => !op.Requires.IsEmpty))
            throw new InvalidDataException(
                "Invalid operation profile for the destination proof and reader.");
        foreach (PublicationOperation op in Operations)
            if (string.IsNullOrWhiteSpace(op.Id.Kind) || string.IsNullOrWhiteSpace(op.Id.Target) ||
                !op.Requires.All(seen.Contains) || !seen.Add(op.Id))
                throw new InvalidDataException(
                    "Invalid publication operation order or prerequisite.");
    }

    internal ImmutableArray<PublicationOperation> Operations { get; }
    internal DestinationVersionRead Read { get; }
    internal SubmissionClearance Clearance { get; }
    internal DestinationVerificationMode Proof { get; }
    internal DestinationVisibilityMode Visibility { get; }

    internal PublicationRequest Review(PublicationEvidence initial)
    {
        PublicationAdmission planned = Classify(initial, beforeGate: true);
        return planned.Disposition switch
        {
            WindowDisposition.Ready => new(this, planned.Pending.ToImmutableHashSet()),
            WindowDisposition.Established => throw new InvalidDataException(
                "An exact request needs no publisher."),
            _ => throw new InvalidDataException("Current state cannot permit creation."),
        };
    }

    internal void RequireCoverage(PublicationEvidence evidence)
    {
        if (evidence.Operations.Count != Operations.Length ||
            Operations.Any(op => !evidence.Operations.ContainsKey(op.Id)))
            throw new InvalidDataException("Observation must cover the active operation profile.");
    }

    internal PublicationAdmission Classify(PublicationEvidence evidence, bool beforeGate)
    {
        RequireCoverage(evidence);
        var states = evidence.Operations;
        if (beforeGate && Read == DestinationVersionRead.BeforeGate &&
            PublicationConclusion.ProofOf(evidence) == FinalProof.Unknown)
            return new(WindowDisposition.Unresolved, []);
        if (!evidence.Qualified)
            return new(WindowDisposition.Blocked, []);

        if (beforeGate ? Read != DestinationVersionRead.BeforeGate
                : Read == DestinationVersionRead.Unavailable)
        {
            if (!states.Values.All(state => state == OperationEvidence.Unobserved))
                return new(WindowDisposition.Blocked, []);
            if (!ReleaseDecisions.CanEnterPublisher(Read, Clearance))
                return new(WindowDisposition.Unresolved, []);
            return new(WindowDisposition.Ready, [.. Operations.Select(op => op.Id)]);
        }

        if (Operations.Any(op =>
                states[op.Id] is not (OperationEvidence.Missing or OperationEvidence.Established) ||
                states[op.Id] == OperationEvidence.Established &&
                    op.Requires.Any(pre => states[pre] != OperationEvidence.Established)))
            return new(WindowDisposition.Blocked, []);
        ImmutableArray<OperationId> pending =
            [.. Operations.Where(op => states[op.Id] == OperationEvidence.Missing)
                .Select(op => op.Id)];
        if (!pending.IsEmpty && !supportsPartial && pending.Length != Operations.Length)
            return new(WindowDisposition.Blocked, []);
        return new(pending.IsEmpty ? WindowDisposition.Established : WindowDisposition.Ready,
            pending);
    }
}

internal sealed record PublicationRequest(PublicationProfile Profile,
    ImmutableHashSet<OperationId> Reviewed)
{
    internal PublicationAdmission Refresh(PublicationEvidence fresh)
    {
        PublicationAdmission admission = Profile.Classify(fresh, beforeGate: false);
        return admission.Pending.All(Reviewed.Contains) ? admission
            : new(WindowDisposition.Enlarged, []);
    }
}

internal sealed record PublicationAdmission(WindowDisposition Disposition,
    ImmutableArray<OperationId> Pending)
{
    internal void RequireNext(int index, OperationAttempt attempt)
    {
        if (index >= Pending.Length || attempt.Operation != Pending[index] ||
            attempt.Acknowledged && attempt.Ambiguous)
            throw new InvalidDataException("Unsupported write sequence.");
    }

    internal void ValidateReceipt(IReadOnlyList<OperationAttempt> attempts, bool interrupted)
    {
        for (int i = 0; i < attempts.Count; i++)
        {
            RequireNext(i, attempts[i]);
            if (i != 0 && !attempts[i - 1].Acknowledged)
                throw new InvalidDataException("Unsupported write sequence.");
        }
        bool incomplete = attempts.Count < Pending.Length &&
            (attempts.Count == 0 || attempts[^1].Acknowledged);
        if (interrupted != incomplete || interrupted && attempts.Count == 0)
            throw new InvalidDataException("Incomplete or contradictory publication receipt.");
    }
}
