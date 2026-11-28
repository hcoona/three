using System.Collections.Immutable;
using System.Linq;

namespace WorkflowDelivery.Core;

internal enum FinalProof { Established, Incomplete, Unknown, Conflict, Unqualified }
internal enum PublicationStatus { Satisfied, Unsatisfied, Unknown }
internal enum ConclusionNote
{
    None,
    Unresolved,
    MissingReceipt,
    MissingRead,
    NonStart,
    Unsupported,
    Unconfirmed
}

internal sealed record PublicationRecord(
    PublicationEvidence Before, ImmutableArray<OperationAttempt> Attempts,
    PublicationEvidence? After, bool Interrupted);
internal sealed record PublicationContext(PublicationProfile Profile, PublicationAdmission Route,
    PublicationRecord? Record, PublicationAdmission? Admission);
internal sealed record PublicationVerdict(
    PublicationStatus State, bool? Attempted, ConclusionNote Note,
    bool Definitive = false, ImmutableArray<OperationAttempt> Unconfirmed = default);

internal static class PublicationConclusion
{
    internal static FinalProof ProofOf(PublicationEvidence? evidence)
    {
        if (evidence is null) return FinalProof.Unknown;
        var states = evidence.Operations.Values;
        if (states.Contains(OperationEvidence.Conflict)) return FinalProof.Conflict;
        if (states.Any(state => state is OperationEvidence.Unknown or OperationEvidence.Unobserved))
            return FinalProof.Unknown;
        if (!evidence.Qualified) return FinalProof.Unqualified;
        return states.Contains(OperationEvidence.Missing)
            ? FinalProof.Incomplete
            : FinalProof.Established;
    }

    internal static PublicationStatus StatusOf(FinalProof proof) => proof switch
    {
        FinalProof.Established => PublicationStatus.Satisfied,
        FinalProof.Unknown => PublicationStatus.Unknown,
        _ => PublicationStatus.Unsatisfied,
    };

    internal static PublicationVerdict Evaluate(
        PublicationContext target, PublicationEvidence? evidence, bool knownNonStart)
    {
        var (profile, route, record, _) = target;
        bool required = route.Disposition == WindowDisposition.Ready;
        bool? attempted = record is not null
            ? !record.Attempts.IsEmpty
            : !required || knownNonStart ? false : null;
        FinalProof proof = ProofOf(evidence);
        if (route.Disposition == WindowDisposition.Blocked)
            return new(PublicationStatus.Unsatisfied, attempted, ConclusionNote.Unsupported, true);
        bool conflict = proof == FinalProof.Conflict &&
            profile.Proof == DestinationVerificationMode.ExactBytes;
        if (conflict)
            return new(
                PublicationStatus.Unsatisfied,
                attempted,
                required && record is null && !knownNonStart
                    ? ConclusionNote.MissingReceipt
                    : ConclusionNote.None,
                true);
        if (route.Disposition == WindowDisposition.Unresolved)
            return new(PublicationStatus.Unknown, false, ConclusionNote.Unresolved);
        if (required && record is null)
            return knownNonStart
                ? new(PublicationStatus.Unsatisfied, false, ConclusionNote.NonStart)
                : new(PublicationStatus.Unknown, null, ConclusionNote.MissingReceipt);
        if (evidence is null)
            return new(PublicationStatus.Unknown, attempted, ConclusionNote.MissingRead);
        if (record is not null &&
            (proof == FinalProof.Incomplete ||
                proof == FinalProof.Unknown &&
                profile.Visibility == DestinationVisibilityMode.BoundedSettle))
        {
            ImmutableArray<OperationAttempt> unconfirmed =
            [
                .. record.Attempts.Where(call => evidence.Operations[call.Operation]
                    is OperationEvidence.Missing or OperationEvidence.Unknown)
            ];
            if (!unconfirmed.IsEmpty)
                return new(
                    PublicationStatus.Unknown, attempted, ConclusionNote.Unconfirmed,
                    Unconfirmed: unconfirmed);
        }
        PublicationStatus state = StatusOf(proof);
        return new(state, attempted, ConclusionNote.None, state == PublicationStatus.Unsatisfied);
    }

    internal static bool PermitsAnchor(PublicationContext primary)
    {
        var (profile, route, record, admission) = primary;
        if (route.Disposition == WindowDisposition.Established) return record is null;
        if (route.Disposition != WindowDisposition.Ready || record is null || record.Interrupted ||
            record.Attempts.Any(call => !call.Acknowledged && !call.Ambiguous)) return false;
        if (profile.Read != DestinationVersionRead.BeforeGate)
            return ProofOf(record.After) == FinalProof.Established;
        return admission!.Disposition is WindowDisposition.Established or WindowDisposition.Ready &&
            record.Attempts.Length == admission.Pending.Length &&
            record.Attempts.All(call => call.Acknowledged);
    }

    internal static PublicationVerdict Combine(
        PublicationVerdict primary, PublicationVerdict anchor)
    {
        bool? attempted = primary.Attempted == true || anchor.Attempted == true ? true :
            primary.Attempted is null || anchor.Attempted is null ? null : false;
        bool definitive = primary.Definitive || anchor.Definitive;
        PublicationStatus state = definitive
            ? PublicationStatus.Unsatisfied
            : primary.State == PublicationStatus.Unknown ||
              anchor.State == PublicationStatus.Unknown
                ? PublicationStatus.Unknown
                : primary.State == PublicationStatus.Satisfied &&
                  anchor.State == PublicationStatus.Satisfied
                    ? PublicationStatus.Satisfied
                    : PublicationStatus.Unsatisfied;
        return new(state, attempted, ConclusionNote.None, definitive);
    }
}
