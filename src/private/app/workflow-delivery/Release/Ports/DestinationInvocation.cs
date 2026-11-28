using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record DestinationInvocation(ReleaseWriteStep Step, string? Error = null,
    ReleaseObservationResult? After = null, ReleaseWriteStep? FirstStage = null)
{
    internal ReleaseWriteStep[] Stages => FirstStage is null ? [Step] : [FirstStage, Step];

    internal static DestinationInvocation Store(string operation, bool succeeded, string detail,
        ReleaseObservationResult? after = null, bool ambiguous = false) =>
        new(ReleaseWriteStep.FromStore(operation, succeeded, detail, ambiguous),
            succeeded ? null : $"{operation}: {detail}", after);
}
