namespace WorkflowDelivery.Core;

internal enum DestinationVersionRead
{
    BeforeGate,
    PublisherOnly,
    Unavailable,
}

internal static class ReleaseDecisions
{
    internal static bool CanEnterPublisher(DestinationVersionRead capability,
        SubmissionClearance clearance) => capability != DestinationVersionRead.Unavailable ||
        clearance is SubmissionClearance.First or SubmissionClearance.OwnerChecked;

    internal static bool CanReadBeforeGate(DestinationVersionRead? capability, bool dryRun) =>
        !dryRun && capability == DestinationVersionRead.BeforeGate;
}
