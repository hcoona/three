using System.Diagnostics;
using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public sealed record AcquiredAccessToken
{
    public string? AccountId { get; init; }

    public required string TenantId { get; init; }

    public required string DeploymentKey { get; init; }

    public required SecretText Token { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = {6}, {7} = <redacted>, {8} = {9} }}",
            nameof(AcquiredAccessToken),
            nameof(AccountId),
            AccountId ?? "<unknown>",
            nameof(TenantId),
            TenantId,
            nameof(DeploymentKey),
            DeploymentKey,
            nameof(Token),
            nameof(ExpiresAt),
            ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "<unknown>"
        );

    private string GetDebuggerDisplay() => ToString();
}
