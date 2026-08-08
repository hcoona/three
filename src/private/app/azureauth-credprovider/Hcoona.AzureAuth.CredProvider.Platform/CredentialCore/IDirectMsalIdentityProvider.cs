using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public interface IDirectMsalIdentityProvider
{
    DirectMsalIdentityResult AcquireIdentity(CredentialRequest request);
}
