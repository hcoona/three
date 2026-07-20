using System.Diagnostics;
using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public enum AccessTokenAcquisitionProvenance
{
    Unspecified = 0,
    AzureAuthProcess = 1,
}

public enum AccessTokenClaimValidation
{
    Unspecified = 0,
    AzureDevOpsClaimConsistency = 1,
}

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public sealed record AcquiredAccessToken
{
    public string? AccountId { get; init; }

    public required string TenantId { get; init; }

    public required string DeploymentKey { get; init; }

    public required SecretText Token { get; init; }

    public DateTimeOffset? IssuedAt { get; init; }

    public DateTimeOffset? NotBefore { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public required AccessTokenAcquisitionProvenance Provenance { get; init; }

    public required AccessTokenClaimValidation ClaimValidation { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = {6}, {7} = <redacted>, "
                + "{8} = {9}, {10} = {11}, {12} = {13}, {14} = {15}, {16} = {17} }}",
            nameof(AcquiredAccessToken),
            nameof(AccountId),
            AccountId ?? "<unknown>",
            nameof(TenantId),
            TenantId,
            nameof(DeploymentKey),
            DeploymentKey,
            nameof(Token),
            nameof(IssuedAt),
            IssuedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "<unknown>",
            nameof(NotBefore),
            NotBefore?.ToString("O", CultureInfo.InvariantCulture) ?? "<unknown>",
            nameof(ExpiresAt),
            ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "<unknown>",
            nameof(Provenance),
            Provenance,
            nameof(ClaimValidation),
            ClaimValidation
        );

    private string GetDebuggerDisplay() => ToString();
}
