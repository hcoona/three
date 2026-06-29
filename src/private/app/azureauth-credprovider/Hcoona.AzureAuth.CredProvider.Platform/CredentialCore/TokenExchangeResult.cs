namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

internal enum TokenExchangeStatus
{
    Unspecified = 0,
    Success = 1,
    Unavailable = 2,
    Failed = 3,
}

internal readonly record struct TokenExchangeResult(
    TokenExchangeStatus Status,
    TokenExchangeMaterial? Material = null)
{
    public static TokenExchangeResult Success(TokenExchangeMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        return new(TokenExchangeStatus.Success, material);
    }

    public static TokenExchangeResult Unavailable => new(TokenExchangeStatus.Unavailable);

    public static TokenExchangeResult Failed => new(TokenExchangeStatus.Failed);
}
