namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public enum DerivedCredentialCacheAvailabilityStatus
{
    Unspecified = 0,
    Available = 1,
    Unavailable = 2,
    Denied = 3,
    Unsupported = 4,
    VerificationFailed = 5,
}

public readonly record struct DerivedCredentialCacheAvailability(
    DerivedCredentialCacheAvailabilityStatus Status)
{
    public bool IsAvailable => Status == DerivedCredentialCacheAvailabilityStatus.Available;

    public static DerivedCredentialCacheAvailability Available =>
        new(DerivedCredentialCacheAvailabilityStatus.Available);

    public static DerivedCredentialCacheAvailability Unavailable =>
        new(DerivedCredentialCacheAvailabilityStatus.Unavailable);

    public static DerivedCredentialCacheAvailability Denied =>
        new(DerivedCredentialCacheAvailabilityStatus.Denied);

    public static DerivedCredentialCacheAvailability Unsupported =>
        new(DerivedCredentialCacheAvailabilityStatus.Unsupported);

    public static DerivedCredentialCacheAvailability VerificationFailed =>
        new(DerivedCredentialCacheAvailabilityStatus.VerificationFailed);
}
