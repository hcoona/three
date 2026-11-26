#nullable enable
using System;
using System.Linq;

namespace WindowsResultValidation;

public enum Outcome { Unknown, Success, InvalidRequest, InteractionRequired, AccountAmbiguous,
    IdentityValidationFailed, Cancelled, Denied, MechanismUnavailable, TemporarilyUnavailable, Timeout, InternalFailure }
public enum Route { None, Silent, Interactive }

// A fixed exception prevents malformed result text or source exceptions from escaping.
public sealed class InvalidResultException : Exception
{
    public InvalidResultException() : base("Result validation failed.") { }
    public override string ToString() => nameof(InvalidResultException);
}

// Windows-local expected values. Never serialize, log, hash or retain this object.
public sealed class ResultExpectation
{
    public required string Email { get; init; }
    public required string[] Scopes { get; init; }
    public Guid? ExactResultTenant { get; init; }
    public bool InteractionAllowed { get; init; }
    public Outcome ExpectedOutcome { get; init; }
    public Route ExpectedRoute { get; init; }
    public bool RequirePersistenceUnconfirmed { get; init; }
    public bool DefaultAssociationIndependentlyAccepted { get; init; }
    public override string ToString() => nameof(ResultExpectation);

    internal void Validate()
    {
        Need(Enum.IsDefined(ExpectedOutcome) && ExpectedOutcome != Outcome.Unknown && Enum.IsDefined(ExpectedRoute));
        Need(Email is not null && HasCharacterLength(Email, 3, 320) && Email.IndexOf('@') > 0 &&
            Email.IndexOf('@') == Email.LastIndexOf('@') && !Email.EndsWith('@') &&
            !Email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)));
        Need(Scopes is not null && Scopes.Length is >= 1 and <= 64 &&
            Scopes.All(s => !string.IsNullOrEmpty(s) && s.Length <= 2048 &&
                !s.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) &&
            Scopes.Distinct(StringComparer.Ordinal).Count() == Scopes.Length);
        Need(ExactResultTenant is null || ExactResultTenant != Guid.Empty);
        Need(ExpectedRoute != Route.Interactive || InteractionAllowed);
    }

    internal static bool TryExactGuid(string value, out Guid result)
    {
        result = default;
        // TryParseExact trims whitespace; validate the original D-form first.
        if (value.Length != 36) return false;
        for (int i = 0; i < value.Length; i++)
        {
            if (i is 8 or 13 or 18 or 23)
            { if (value[i] != '-') return false; }
            else if (value[i] is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
                return false;
        }
        return Guid.TryParseExact(value, "D", out result);
    }

    internal static bool HasCharacterLength(string value, int minimum, int maximum)
    {
        int count = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            { if (++i == value.Length || !char.IsLowSurrogate(value[i])) return false; }
            else if (char.IsLowSurrogate(value[i])) return false;
            if (++count > maximum) return false;
        }
        return count >= minimum;
    }
    private static void Need(bool value) { if (!value) throw new InvalidResultException(); }
}
