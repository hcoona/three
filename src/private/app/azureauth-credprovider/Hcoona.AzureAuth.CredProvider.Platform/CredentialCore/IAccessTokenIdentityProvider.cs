using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public interface IAccessTokenIdentityProvider
{
    ValueTask<AcquiredAccessTokenResult> AcquireAccessTokenAsync(
        CredentialRequestV2 request,
        CancellationToken cancellationToken = default
    );
}
