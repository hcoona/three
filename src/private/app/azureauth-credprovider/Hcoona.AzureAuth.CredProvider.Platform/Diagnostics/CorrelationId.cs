using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class CorrelationId : IEquatable<CorrelationId>
{
    private const string Format = "D";

    private readonly Guid _value;

    private CorrelationId(Guid value)
    {
        _value = value;
    }

    public static CorrelationId New()
    {
        return new CorrelationId(Guid.NewGuid());
    }

    public static CorrelationId FromGuid(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Correlation id must not be empty.", nameof(value));
        }

        return new CorrelationId(value);
    }

    public static CorrelationId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!TryParse(value, out var correlationId))
        {
            throw new FormatException("The value is not a valid correlation id.");
        }

        return correlationId;
    }

    public static bool TryParse(
        string? value,
        [NotNullWhen(true)] out CorrelationId? correlationId)
    {
        if (Guid.TryParseExact(value, Format, out var guid) &&
            guid != Guid.Empty &&
            string.Equals(
                value,
                guid.ToString(Format, CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            correlationId = new CorrelationId(guid);
            return true;
        }

        correlationId = null;
        return false;
    }

    public Guid ToGuid()
    {
        return _value;
    }

    public override string ToString()
    {
        return _value.ToString(Format, CultureInfo.InvariantCulture);
    }

    public bool Equals(CorrelationId? other)
    {
        return other is not null && _value.Equals(other._value);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as CorrelationId);
    }

    public override int GetHashCode()
    {
        return _value.GetHashCode();
    }

    public static bool operator ==(CorrelationId? left, CorrelationId? right)
    {
        return EqualityComparer<CorrelationId>.Default.Equals(left, right);
    }

    public static bool operator !=(CorrelationId? left, CorrelationId? right)
    {
        return !(left == right);
    }
}
