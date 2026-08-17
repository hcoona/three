using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public enum DerivedCredentialCacheReadStatus
{
    Unspecified = 0,
    Hit = 1,
    Miss = 2,
    Unavailable = 3,
    Denied = 4,
    Unsupported = 5,
    VerificationFailed = 6,
}

public readonly record struct DerivedCredentialCacheReadResult(
    DerivedCredentialCacheReadStatus Status,
    IdentityMaterial? Identity = null)
{
    public static DerivedCredentialCacheReadResult Miss =>
        new(DerivedCredentialCacheReadStatus.Miss);

    public static DerivedCredentialCacheReadResult Unavailable =>
        new(DerivedCredentialCacheReadStatus.Unavailable);

    public static DerivedCredentialCacheReadResult Denied =>
        new(DerivedCredentialCacheReadStatus.Denied);

    public static DerivedCredentialCacheReadResult Unsupported =>
        new(DerivedCredentialCacheReadStatus.Unsupported);

    public static DerivedCredentialCacheReadResult VerificationFailed =>
        new(DerivedCredentialCacheReadStatus.VerificationFailed);

    public static DerivedCredentialCacheReadResult Hit(IdentityMaterial identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new(DerivedCredentialCacheReadStatus.Hit, identity);
    }

    public override string ToString()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4} }}",
            nameof(DerivedCredentialCacheReadResult),
            nameof(Status),
            Status,
            nameof(Identity),
            Identity?.ToString() ?? "null"
        );
    }
}
