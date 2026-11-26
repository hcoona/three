using Authentication.Core;
using Microsoft.Identity.Client.Extensibility;

namespace Authentication.Windows;

internal sealed class RejectingWebUi : ICustomWebUi
{
    private readonly WindowsMechanismTrace? trace;
    internal RejectingWebUi(WindowsMechanismTrace? trace) => this.trace = trace;

    // A lost broker cannot enable browser navigation through MSAL's fallback callback.
    public Task<Uri> AcquireAuthorizationCodeAsync(Uri authorizationUri, Uri redirectUri,
        CancellationToken cancellationToken)
    {
        trace?.Record(WindowsMechanismFailure.RejectedWebUi);
        return Task.FromException<Uri>(new ProviderFailureException(AuthenticationFailure.MechanismUnavailable));
    }
}
