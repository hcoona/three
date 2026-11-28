using WorkflowDelivery.Destinations;

namespace WorkflowDelivery.Core;

internal enum DestinationVersionRead
{
    BeforeGate,
    PublisherOnly,
    Unavailable,
}

internal static class ReleaseDecisions
{
    internal static bool CanReadBeforeGate(DestinationVersionRead? capability, bool dryRun) =>
        !dryRun && capability == DestinationVersionRead.BeforeGate;

    internal static bool MaySubmit(DestinationVersionRead capability, StoreState? freshState,
        bool observationProceeds) =>
        freshState == (capability == DestinationVersionRead.Unavailable
            ? StoreState.Unobserved : StoreState.Absent) &&
        observationProceeds;
}
