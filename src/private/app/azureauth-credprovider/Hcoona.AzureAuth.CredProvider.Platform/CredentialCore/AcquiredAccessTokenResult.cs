namespace Hcoona.AzureAuth.CredProvider.Platform.CredentialCore;

public enum AcquiredAccessTokenStatus
{
    Unspecified = 0,
    Success = 1,
    InteractionRequired = 2,
    InteractionBlocked = 3,
    RequestRejected = 4,
    PrerequisiteFailed = 5,
    ProcessFailed = 6,
    OutputRejected = 7,
    Canceled = 8,
    TimedOut = 9,
    Fatal = 10,
}

public sealed record AcquiredAccessTokenResult
{
    public required AcquiredAccessTokenStatus Status { get; init; }

    public AcquiredAccessToken? AccessToken { get; init; }

    public string? Code { get; init; }

    public string? SafeMessage { get; init; }

    public bool Succeeded => Status == AcquiredAccessTokenStatus.Success;

    public static AcquiredAccessTokenResult Success(AcquiredAccessToken accessToken)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        return new AcquiredAccessTokenResult
        {
            Status = AcquiredAccessTokenStatus.Success,
            AccessToken = accessToken,
        };
    }

    public static AcquiredAccessTokenResult Failure(
        AcquiredAccessTokenStatus status,
        string code,
        string safeMessage
    )
    {
        if (status == AcquiredAccessTokenStatus.Success)
        {
            throw new ArgumentException("Failure status must not be Success.", nameof(status));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        return new AcquiredAccessTokenResult
        {
            Status = status,
            Code = code,
            SafeMessage = safeMessage,
        };
    }

    public override string ToString() =>
        Succeeded
            ? $"{nameof(AcquiredAccessTokenResult)} {{ "
                + $"{nameof(Status)} = {Status}, "
                + $"{nameof(AccessToken)} = {AccessToken} }}"
            : $"{nameof(AcquiredAccessTokenResult)} {{ "
                + $"{nameof(Status)} = {Status}, "
                + $"{nameof(Code)} = {Code}, "
                + $"{nameof(SafeMessage)} = {SafeMessage} }}";
}
