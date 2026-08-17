using System.Globalization;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

namespace Hcoona.AzureAuth.CredProvider.Platform.TokenMaterialization;

public enum AsyncTokenExchangeStatus
{
    Unspecified = 0,
    Success = 1,
    Disabled = 2,
    Failed = 3,
    Canceled = 4,
    TimedOut = 5,
}

public sealed record AsyncTokenExchangeResult
{
    public required AsyncTokenExchangeStatus Status { get; init; }
    public SecretText? Token { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string Code { get; init; }

    public static AsyncTokenExchangeResult Success(SecretText token, DateTimeOffset expiresAt) =>
        new()
        {
            Status = AsyncTokenExchangeStatus.Success,
            Token = token,
            ExpiresAt = expiresAt,
            Code = "TokenExchangeSucceeded",
        };

    public static AsyncTokenExchangeResult Failure(
        AsyncTokenExchangeStatus status,
        string code) =>
        new()
        {
            Status = status,
            Code = code,
        };

    public override string ToString() =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} {{ {1} = {2}, {3} = <redacted>, {4} = {5}, {6} = {7} }}",
            nameof(AsyncTokenExchangeResult),
            nameof(Status),
            Status,
            nameof(Token),
            nameof(ExpiresAt),
            ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "<unknown>",
            nameof(Code),
            Code);
}

public interface ITokenExchange
{
    ValueTask<AsyncTokenExchangeResult> ExchangeAsync(
        CredentialRequestV2 request,
        AcquiredAccessToken sourceToken,
        CancellationToken cancellationToken = default);
}
