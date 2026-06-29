using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.AdapterHost;

public sealed class AdapterHostExecutionOutcome
{
    internal AdapterHostExecutionOutcome(
        AdapterInvocationContext? invocation,
        AdapterHostResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Invocation = invocation;
        Result = result;
    }

    public AdapterInvocationContext? Invocation { get; }

    public AdapterHostResult Result { get; }
}
