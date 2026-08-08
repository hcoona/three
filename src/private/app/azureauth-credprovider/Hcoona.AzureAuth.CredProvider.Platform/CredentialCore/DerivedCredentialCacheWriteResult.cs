namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public enum DerivedCredentialCacheWriteStatus
{
    Unspecified = 0,
    Written = 1,
    Unavailable = 2,
    Denied = 3,
    Unsupported = 4,
    VerificationFailed = 5,
}

public readonly record struct DerivedCredentialCacheWriteResult(
    DerivedCredentialCacheWriteStatus Status)
{
    public static DerivedCredentialCacheWriteResult Written =>
        new(DerivedCredentialCacheWriteStatus.Written);

    public static DerivedCredentialCacheWriteResult Unavailable =>
        new(DerivedCredentialCacheWriteStatus.Unavailable);

    public static DerivedCredentialCacheWriteResult Denied =>
        new(DerivedCredentialCacheWriteStatus.Denied);

    public static DerivedCredentialCacheWriteResult Unsupported =>
        new(DerivedCredentialCacheWriteStatus.Unsupported);

    public static DerivedCredentialCacheWriteResult VerificationFailed =>
        new(DerivedCredentialCacheWriteStatus.VerificationFailed);
}
