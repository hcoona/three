using System.Diagnostics;

namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

[DebuggerDisplay("<redacted>")]
public sealed record SecretText
{
    public required string Value { get; init; }

    public override string ToString() => "<redacted>";
}
