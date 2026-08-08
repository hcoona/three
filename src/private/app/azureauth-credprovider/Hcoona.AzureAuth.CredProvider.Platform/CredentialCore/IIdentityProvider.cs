using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public interface IIdentityProvider
{
    IdentityMaterial GetIdentity(CredentialRequest request);
}
