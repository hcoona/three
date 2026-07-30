using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Hcoona.CelesphoniaModifier.Atlas;

public enum AtlasGoldCandidateState
{
    Present,
    Missing,
    Ambiguous,
    WrongShape,
    NonInteger,
    OutsideInt64,
}

public sealed class AtlasGoldCandidateResult
{
    private AtlasGoldCandidateResult(AtlasGoldCandidateState state, long? value)
    {
        State = state;
        Value = value;
    }

    public AtlasGoldCandidateState State { get; }

    public long? Value { get; }

    internal static AtlasGoldCandidateResult Present(long value) =>
        new(AtlasGoldCandidateState.Present, value);

    internal static AtlasGoldCandidateResult FromState(AtlasGoldCandidateState state) =>
        state switch
        {
            AtlasGoldCandidateState.Missing => Missing,
            AtlasGoldCandidateState.Ambiguous => Ambiguous,
            AtlasGoldCandidateState.WrongShape => WrongShape,
            AtlasGoldCandidateState.NonInteger => NonInteger,
            AtlasGoldCandidateState.OutsideInt64 => OutsideInt64,
            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                "A non-present Gold candidate state is required."),
        };

    private static AtlasGoldCandidateResult Missing { get; } =
        new(AtlasGoldCandidateState.Missing, null);

    private static AtlasGoldCandidateResult Ambiguous { get; } =
        new(AtlasGoldCandidateState.Ambiguous, null);

    private static AtlasGoldCandidateResult WrongShape { get; } =
        new(AtlasGoldCandidateState.WrongShape, null);

    private static AtlasGoldCandidateResult NonInteger { get; } =
        new(AtlasGoldCandidateState.NonInteger, null);

    private static AtlasGoldCandidateResult OutsideInt64 { get; } =
        new(AtlasGoldCandidateState.OutsideInt64, null);
}

public enum AtlasGoldAggregateState
{
    Consistent,
    Disagree,
    Incomplete,
}

public sealed class AtlasGoldReadModelResult
{
    private AtlasGoldReadModelResult(
        AtlasGoldCandidateResult partyGold,
        AtlasGoldCandidateResult variableGold)
    {
        PartyGold = partyGold;
        VariableGold = variableGold;
        Aggregate = DeriveAggregate(partyGold, variableGold);
    }

    public AtlasGoldCandidateResult PartyGold { get; }

    public AtlasGoldCandidateResult VariableGold { get; }

    public AtlasGoldAggregateState Aggregate { get; }

    internal static AtlasGoldReadModelResult Create(
        AtlasGoldCandidateResult partyGold,
        AtlasGoldCandidateResult variableGold)
    {
        ArgumentNullException.ThrowIfNull(partyGold);
        ArgumentNullException.ThrowIfNull(variableGold);
        return new AtlasGoldReadModelResult(partyGold, variableGold);
    }

    private static AtlasGoldAggregateState DeriveAggregate(
        AtlasGoldCandidateResult partyGold,
        AtlasGoldCandidateResult variableGold)
    {
        if (partyGold.State != AtlasGoldCandidateState.Present
            || variableGold.State != AtlasGoldCandidateState.Present)
        {
            return AtlasGoldAggregateState.Incomplete;
        }

        return partyGold.Value == variableGold.Value
            ? AtlasGoldAggregateState.Consistent
            : AtlasGoldAggregateState.Disagree;
    }
}

internal readonly struct AtlasGoldCandidateInspection
{
    internal AtlasGoldCandidateInspection(
        AtlasGoldCandidateResult result,
        AtlasJsonSourceSpan? sourceSpan)
    {
        Result = result;
        SourceSpan = sourceSpan;
    }

    internal AtlasGoldCandidateResult Result { get; }

    internal AtlasJsonSourceSpan? SourceSpan { get; }
}

internal sealed class AtlasGoldInspectionResult
{
    internal AtlasGoldInspectionResult(
        AtlasGoldCandidateInspection partyGold,
        AtlasGoldCandidateInspection variableGold)
    {
        ReadModel = AtlasGoldReadModelResult.Create(
            partyGold.Result,
            variableGold.Result);
        PartyGoldSpan = partyGold.SourceSpan;
        VariableGoldSpan = variableGold.SourceSpan;
    }

    internal AtlasGoldReadModelResult ReadModel { get; }

    internal AtlasJsonSourceSpan? PartyGoldSpan { get; }

    internal AtlasJsonSourceSpan? VariableGoldSpan { get; }
}

public static class AtlasGoldReadModel
{
    private const int VariableGoldIndex = 215;

    public static AtlasGoldReadModelResult Read(
        AtlasSaveReadResult source,
        CancellationToken cancellationToken = default) =>
        Inspect(source, cancellationToken).ReadModel;

    internal static AtlasGoldInspectionResult Inspect(
        AtlasSaveReadResult source,
        CancellationToken cancellationToken)
    {
        if (source is null)
        {
            throw new ArgumentNullException(
                nameof(source),
                "The Atlas save read result is required.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldCandidateInspection partyGold = ReadPartyGold(
            source.Graph,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldCandidateInspection variableGold = ReadVariableGold(
            source.Graph,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new AtlasGoldInspectionResult(partyGold, variableGold);
    }

    private static AtlasGoldCandidateInspection ReadPartyGold(
        AtlasJsonExNode root,
        CancellationToken cancellationToken)
    {
        if (!TryGetObject(root, cancellationToken, out AtlasJsonExObject? rootObject))
        {
            return Inspection(AtlasGoldCandidateState.WrongShape);
        }

        AtlasGoldCandidateResult? partyLookup = FindMember(
            rootObject,
            "party",
            cancellationToken,
            out AtlasJsonExNode? party);
        if (partyLookup is not null)
        {
            return Inspection(partyLookup);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetObject(party!, cancellationToken, out AtlasJsonExObject? partyObject))
        {
            return Inspection(AtlasGoldCandidateState.WrongShape);
        }

        AtlasGoldCandidateResult? goldLookup = FindMember(
            partyObject,
            "_gold",
            cancellationToken,
            out AtlasJsonExNode? gold);
        if (goldLookup is not null)
        {
            return Inspection(goldLookup);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ReadNumber(gold!, cancellationToken);
    }

    private static AtlasGoldCandidateInspection ReadVariableGold(
        AtlasJsonExNode root,
        CancellationToken cancellationToken)
    {
        if (!TryGetObject(root, cancellationToken, out AtlasJsonExObject? rootObject))
        {
            return Inspection(AtlasGoldCandidateState.WrongShape);
        }

        AtlasGoldCandidateResult? variablesLookup = FindMember(
            rootObject,
            "variables",
            cancellationToken,
            out AtlasJsonExNode? variables);
        if (variablesLookup is not null)
        {
            return Inspection(variablesLookup);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetObject(variables!, cancellationToken, out AtlasJsonExObject? variablesObject))
        {
            return Inspection(AtlasGoldCandidateState.WrongShape);
        }

        AtlasGoldCandidateResult? dataLookup = FindMember(
            variablesObject,
            "_data",
            cancellationToken,
            out AtlasJsonExNode? data);
        if (dataLookup is not null)
        {
            return Inspection(dataLookup);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetArray(data!, cancellationToken, out AtlasJsonExArray? dataArray))
        {
            return Inspection(AtlasGoldCandidateState.WrongShape);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (dataArray.Elements.Count <= VariableGoldIndex)
        {
            return Inspection(AtlasGoldCandidateState.Missing);
        }

        AtlasJsonExNode gold = dataArray.Elements[VariableGoldIndex];
        cancellationToken.ThrowIfCancellationRequested();
        return ReadNumber(gold, cancellationToken);
    }

    private static AtlasGoldCandidateResult? FindMember(
        AtlasJsonExObject source,
        string name,
        CancellationToken cancellationToken,
        out AtlasJsonExNode? value)
    {
        cancellationToken.ThrowIfCancellationRequested();
        value = null;
        int matches = 0;
        foreach (AtlasJsonExMember member in source.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!StringComparer.Ordinal.Equals(member.Name, name))
            {
                continue;
            }

            matches++;
            if (matches > 1)
            {
                value = null;
                return State(AtlasGoldCandidateState.Ambiguous);
            }

            value = member.Value;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return matches == 0
            ? State(AtlasGoldCandidateState.Missing)
            : null;
    }

    private static AtlasGoldCandidateInspection ReadNumber(
        AtlasJsonExNode source,
        CancellationToken cancellationToken)
    {
        AtlasJsonExNode resolved = Dereference(source, cancellationToken);
        if (resolved is not AtlasJsonExScalar scalar
            || scalar.Scalar.Kind != AtlasJsonScalarKind.Number)
        {
            return Inspection(AtlasGoldCandidateState.WrongShape);
        }

        cancellationToken.ThrowIfCancellationRequested();
        string lexeme = scalar.Scalar.RawLexeme;
        if (!IsSignedDecimalInteger(lexeme, cancellationToken))
        {
            return Inspection(AtlasGoldCandidateState.NonInteger);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return long.TryParse(
            lexeme,
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out long value)
            ? new AtlasGoldCandidateInspection(
                AtlasGoldCandidateResult.Present(value),
                scalar.Scalar.Span)
            : Inspection(AtlasGoldCandidateState.OutsideInt64);
    }

    private static bool IsSignedDecimalInteger(
        string lexeme,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int index = lexeme.Length > 0 && lexeme[0] == '-' ? 1 : 0;
        if (index == lexeme.Length)
        {
            return false;
        }

        char firstDigit = lexeme[index];
        if (firstDigit == '0')
        {
            return index + 1 == lexeme.Length;
        }

        if (firstDigit is < '1' or > '9')
        {
            return false;
        }

        for (index++; index < lexeme.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lexeme[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetObject(
        AtlasJsonExNode source,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out AtlasJsonExObject? value)
    {
        value = Dereference(source, cancellationToken) as AtlasJsonExObject;
        return value is not null;
    }

    private static bool TryGetArray(
        AtlasJsonExNode source,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out AtlasJsonExArray? value)
    {
        value = Dereference(source, cancellationToken) as AtlasJsonExArray;
        return value is not null;
    }

    private static AtlasJsonExNode Dereference(
        AtlasJsonExNode source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AtlasJsonExNode resolved = source is AtlasJsonExReference reference
            ? reference.Target
            : source;
        cancellationToken.ThrowIfCancellationRequested();
        return resolved;
    }

    private static AtlasGoldCandidateResult State(AtlasGoldCandidateState state) =>
        AtlasGoldCandidateResult.FromState(state);

    private static AtlasGoldCandidateInspection Inspection(
        AtlasGoldCandidateState state) =>
        Inspection(State(state));

    private static AtlasGoldCandidateInspection Inspection(
        AtlasGoldCandidateResult result) =>
        new(result, null);
}
