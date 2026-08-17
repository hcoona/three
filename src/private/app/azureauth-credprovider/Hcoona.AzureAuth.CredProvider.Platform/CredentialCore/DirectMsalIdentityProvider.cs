using Hcoona.AzureAuth.CredProvider.Contracts;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public sealed class DirectMsalIdentityProvider : IIdentityProvider
{
    private readonly IDirectMsalIdentityProvider _directMsalIdentityProvider;

    public DirectMsalIdentityProvider()
        : this(new NotImplementedDirectMsalIdentityProvider())
    { }

    public DirectMsalIdentityProvider(IDirectMsalIdentityProvider directMsalIdentityProvider)
    {
        ArgumentNullException.ThrowIfNull(directMsalIdentityProvider);
        _directMsalIdentityProvider = directMsalIdentityProvider;
    }

    public IdentityMaterial GetIdentity(CredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DirectMsalIdentityResult result;
        try
        {
            result = _directMsalIdentityProvider.AcquireIdentity(request);
        }
        catch (NotImplementedException)
        {
            throw DirectMsalIdentityProviderUnavailableException.NotImplemented();
        }
        catch (NotSupportedException)
        {
            throw DirectMsalIdentityProviderUnavailableException.Unavailable();
        }

        return result.Status switch
        {
            DirectMsalIdentityStatus.Success when result.Identity is not null => result.Identity,
            DirectMsalIdentityStatus.Unavailable =>
                throw DirectMsalIdentityProviderUnavailableException.Unavailable(),
            DirectMsalIdentityStatus.NotImplemented =>
                throw DirectMsalIdentityProviderUnavailableException.NotImplemented(),
            _ => throw new InvalidOperationException(
                "Direct MSAL identity provider returned an invalid result."),
        };
    }

    private sealed class NotImplementedDirectMsalIdentityProvider : IDirectMsalIdentityProvider
    {
        public DirectMsalIdentityResult AcquireIdentity(CredentialRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            return DirectMsalIdentityResult.NotImplemented;
        }
    }
}

internal sealed class DirectMsalIdentityProviderUnavailableException : Exception
{
    private DirectMsalIdentityProviderUnavailableException(string code, string safeMessage)
        : base(safeMessage)
    {
        Code = code;
        SafeMessage = safeMessage;
    }

    public string Code { get; }

    public string SafeMessage { get; }

    public static DirectMsalIdentityProviderUnavailableException NotImplemented() =>
        new("DirectMsalNotImplemented", "Direct MSAL identity provider is not implemented.");

    public static DirectMsalIdentityProviderUnavailableException Unavailable() =>
        new("DirectMsalUnavailable", "Direct MSAL identity provider is unavailable.");
}
