using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

public enum CredentialMaterializationStatus
{
    Unspecified = 0,
    Success = 1,
    Disabled = 2,
    Unsupported = 3,
    InvalidRequest = 4,
    InvalidToken = 5,
    ExchangeFailed = 6,
    Canceled = 7,
    TimedOut = 8,
}

public sealed record CredentialMaterializationResult
{
    public required CredentialMaterializationStatus Status { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? BearerToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string Code { get; init; }
    public required string SafeMessage { get; init; }

    public bool Succeeded => Status == CredentialMaterializationStatus.Success;

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = {4}, {5} = <redacted>, {6} = <redacted>, "
                + "{7} = {8}, {9} = {10}, {11} = {12} }}",
            nameof(CredentialMaterializationResult),
            nameof(Status),
            Status,
            nameof(Username),
            Username,
            nameof(Password),
            nameof(BearerToken),
            nameof(ExpiresAt),
            ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "<unknown>",
            nameof(Code),
            Code,
            nameof(SafeMessage),
            SafeMessage);
}
