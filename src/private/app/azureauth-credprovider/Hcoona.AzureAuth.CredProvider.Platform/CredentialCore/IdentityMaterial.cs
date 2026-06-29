using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public sealed record IdentityMaterial
{
    private const string RedactedValue = "<redacted>";

    public required string Account { get; init; }
    public required string Tenant { get; init; }
    public string? Secret { get; init; }
    public string? AccessToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = {6}, {7} = {8}, {9} = {10} }}",
            nameof(IdentityMaterial),
            nameof(Account),
            Account,
            nameof(Tenant),
            Tenant,
            nameof(Secret),
            RedactedValue,
            nameof(AccessToken),
            RedactedValue,
            nameof(ExpiresAt),
            ExpiresAt.ToString("O", CultureInfo.InvariantCulture)
        );
}
