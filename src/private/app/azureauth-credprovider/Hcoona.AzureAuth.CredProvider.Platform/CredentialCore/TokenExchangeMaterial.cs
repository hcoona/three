using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

internal sealed record TokenExchangeMaterial
{
    private const string RedactedValue = "<redacted>";

    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? BearerToken { get; init; }

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = {6} }}",
            nameof(TokenExchangeMaterial),
            nameof(Username),
            Username,
            nameof(Password),
            RedactedValue,
            nameof(BearerToken),
            RedactedValue
        );
}
