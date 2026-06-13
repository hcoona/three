using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.Configuration;

internal interface IConfigurationPhysicalTargetWriterDispatcher
{
    ValueTask Dispatch(
        ConfigurationPhysicalTargetWriterRequest request,
        CancellationToken cancellationToken
    );
}

internal sealed record ConfigurationPhysicalTargetWriterRequest(
    ConfigurationPlanOperation PlanOperation,
    ConfigurationTargetKind TargetKind,
    ConfigurationChangeOperation ChangeOperation,
    ConfigurationChange Change
);
