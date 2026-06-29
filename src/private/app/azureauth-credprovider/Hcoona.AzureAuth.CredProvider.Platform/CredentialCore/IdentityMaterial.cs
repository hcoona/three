namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public sealed record IdentityMaterial
{
    public required string Account { get; init; }
    public required string Tenant { get; init; }
    public required string Secret { get; init; }
    public required string AccessToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
