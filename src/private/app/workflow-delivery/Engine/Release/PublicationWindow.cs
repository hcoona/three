using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WorkflowDelivery.Core;

namespace WorkflowDelivery.Engine.Release;

internal static class PublicationWindow
{
    internal static PublicationContext Bind(PublicationProfile profile, PublicationEvidence initial,
        PublicationRecord? record)
    {
        PublicationAdmission route = profile.Classify(initial, beforeGate: true);
        if (record is null) return new(profile, route, null, null);
        if (route.Disposition != WindowDisposition.Ready)
            throw new InvalidDataException("Mismatched or unnecessary publication result.");
        if (record.After is not null) profile.RequireCoverage(record.After);
        PublicationAdmission admission = profile.Review(initial).Refresh(record.Before);
        if (!record.Attempts.IsEmpty && admission.Disposition == WindowDisposition.Blocked)
            throw new InvalidDataException("Unsupported write from non-proceeding state.");
        admission.ValidateReceipt(record.Attempts, record.Interrupted);
        if (record.After is not null && profile.Proof == DestinationVerificationMode.StoreAccepted)
        {
            if (record.Attempts.IsEmpty && profile.Read != DestinationVersionRead.PublisherOnly &&
                    admission.Disposition != WindowDisposition.Established ||
                !record.Attempts.IsEmpty && profile.Read == DestinationVersionRead.BeforeGate ||
                profile.Read == DestinationVersionRead.Unavailable &&
                    PublicationConclusion.ProofOf(record.After) == FinalProof.Established &&
                    (record.Attempts.Length != admission.Pending.Length ||
                    record.Attempts.Any(call => !call.Acknowledged)))
                throw new InvalidDataException("Unsupported submission completion evidence.");
        }
        // Keep diagnostic publisher readbacks out of a verifier-read port's sealed evidence.
        if (profile.Read == DestinationVersionRead.BeforeGate && record.After is not null)
            record = record with { After = null };
        return new(profile, route, record, admission);
    }

    internal static PublicationVerdict Complete(
        PublicationContext target,
        PublicationEvidence? verifier,
        bool knownNonStart = false)
    {
        bool publisherReads = target.Profile.Read != DestinationVersionRead.BeforeGate;
        if (publisherReads && target.Route.Disposition == WindowDisposition.Blocked)
            throw new InvalidDataException("This request exposes no pre-gate version observation.");
        if (publisherReads && verifier is not null)
            throw new InvalidDataException("No external verifier is admitted for this request.");
        if (verifier is not null) target.Profile.RequireCoverage(verifier);
        return PublicationConclusion.Evaluate(
            target, publisherReads ? target.Record?.After : verifier,
            knownNonStart);
    }

    internal static (
        PublicationVerdict Primary,
        PublicationVerdict? Anchor,
        PublicationVerdict Combined)
        Resolve(PublicationContext primary, PublicationEvidence? verified,
            PublicationContext? anchor, PublicationEvidence? anchorVerified,
            bool primaryKnownNonStart = false)
    {
        PublicationVerdict first = Complete(primary, verified, knownNonStart: primaryKnownNonStart);
        if (anchor is null) return (first, null, first);
        // Only a pre-gate refusal proves non-start here. Verification does not see
        // the gate's delivery.
        bool noPrimaryRoute =
            primary.Route.Disposition is WindowDisposition.Blocked or WindowDisposition.Unresolved;
        if (noPrimaryRoute && anchor.Record?.Attempts.Length > 0)
            throw new InvalidDataException("The secondary publication was not permitted.");
        PublicationVerdict second = Complete(anchor, anchorVerified, knownNonStart: noPrimaryRoute);
        return (first, second, PublicationConclusion.Combine(first, second));
    }

    internal static async Task<(PublicationAdmission Admission, bool Interrupted)> RunAsync(
        PublicationRequest request, Func<CancellationToken, Task<PublicationEvidence>> refresh,
        Func<OperationId, CancellationToken, Task<OperationAttempt>> invoke,
        CancellationToken token)
    {
        if (!ReleaseDecisions.CanEnterPublisher(request.Profile.Read, request.Profile.Clearance))
            throw new InvalidDataException(
                "Unresolved submission clearance cannot enter the publisher.");
        PublicationAdmission admission = request.Refresh(await refresh(token));
        var attempts = new List<OperationAttempt>();
        bool interrupted = false;
        foreach (OperationId operation in admission.Pending)
        {
            if (attempts.Count != 0 && token.IsCancellationRequested)
            {
                interrupted = true;
                break;
            }
            token.ThrowIfCancellationRequested();
            OperationAttempt attempt = await invoke(operation, token);
            admission.RequireNext(attempts.Count, attempt);
            attempts.Add(attempt);
            if (!attempt.Acknowledged) break;
        }
        admission.ValidateReceipt(attempts, interrupted);
        return (admission, interrupted);
    }
}
