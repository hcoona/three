namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public enum DirectMsalIdentityStatus
{
    Unspecified = 0,
    Success = 1,
    Unavailable = 2,
    NotImplemented = 3,
}

public readonly record struct DirectMsalIdentityResult(
    DirectMsalIdentityStatus Status,
    IdentityMaterial? Identity = null)
{
    public static DirectMsalIdentityResult Success(IdentityMaterial identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new(DirectMsalIdentityStatus.Success, identity);
    }

    public static DirectMsalIdentityResult Unavailable =>
        new(DirectMsalIdentityStatus.Unavailable);

    public static DirectMsalIdentityResult NotImplemented =>
        new(DirectMsalIdentityStatus.NotImplemented);
}
