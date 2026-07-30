using System.Diagnostics;
using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public enum AccessTokenAcquisitionProvenance
{
    Unspecified = 0,
    AzureAuthProcess = 1,
}

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public sealed record AcquiredAccessToken
{
    public string? AccountId { get; init; }

    public required string TenantId { get; init; }

    public required SecretText Token { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public required AccessTokenAcquisitionProvenance Provenance { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = <redacted>, {6} = {7}, {8} = {9} }}",
            nameof(AcquiredAccessToken),
            nameof(AccountId),
            AccountId ?? "<unknown>",
            nameof(TenantId),
            TenantId,
            nameof(Token),
            nameof(ExpiresAt),
            ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "<unknown>",
            nameof(Provenance),
            Provenance
        );

    private string GetDebuggerDisplay() => ToString();
}
