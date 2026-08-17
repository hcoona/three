using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class CorrelationIdTests
{
    [Fact]
    public void NewCreatesNonEmptyCorrelationId()
    {
        var correlationId = CorrelationId.New();

        Assert.NotEqual(Guid.Empty, correlationId.ToGuid());
    }

    [Fact]
    public void ToStringUsesCanonicalGuidFormat()
    {
        var guid = Guid.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2");
        var correlationId = CorrelationId.FromGuid(guid);

        Assert.Equal("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2", correlationId.ToString());
    }

    [Fact]
    public void ParseAcceptsCanonicalGuidFormat()
    {
        var guid = Guid.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2");

        var correlationId = CorrelationId.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2");

        Assert.Equal(guid, correlationId.ToGuid());
    }

    [Fact]
    public void TryParseReturnsFalseForNullEmptyAndInvalidValues()
    {
        Assert.False(CorrelationId.TryParse(null, out var nullCorrelationId));
        Assert.Null(nullCorrelationId);
        Assert.False(CorrelationId.TryParse("", out var emptyCorrelationId));
        Assert.Null(emptyCorrelationId);
        Assert.False(CorrelationId.TryParse("not-a-guid", out var invalidCorrelationId));
        Assert.Null(invalidCorrelationId);
    }

    [Theory]
    [InlineData("9f2ea1a145a448d29c7f73a90e6732d2")]
    [InlineData("{9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2}")]
    [InlineData("(9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2)")]
    [InlineData("{0x9f2ea1a1,0x45a4,0x48d2,{0x9c,0x7f,0x73,0xa9,0x0e,0x67,0x32,0xd2}}")]
    [InlineData("9F2EA1A1-45A4-48D2-9C7F-73A90E6732D2")]
    [InlineData(" 9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2")]
    [InlineData("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2 ")]
    [InlineData("\n9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2\n")]
    public void TryParseRejectsNonCanonicalGuidFormats(string value)
    {
        Assert.False(CorrelationId.TryParse(value, out var correlationId));
        Assert.Null(correlationId);
    }

    [Fact]
    public void TryParseRejectsEmptyGuid()
    {
        Assert.False(
            CorrelationId.TryParse("00000000-0000-0000-0000-000000000000", out var correlationId));
        Assert.Null(correlationId);
    }

    [Fact]
    public void ParseRejectsEmptyGuid()
    {
        Assert.Throws<FormatException>(
            () => CorrelationId.Parse("00000000-0000-0000-0000-000000000000"));
    }

    [Fact]
    public void FromGuidRejectsEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => CorrelationId.FromGuid(Guid.Empty));
    }

    [Fact]
    public void EqualValuesHaveValueSemantics()
    {
        var guid = Guid.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2");
        var left = CorrelationId.FromGuid(guid);
        var right = CorrelationId.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2");

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void DifferentValuesAreNotEqual()
    {
        var left = CorrelationId.FromGuid(Guid.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2"));
        var right = CorrelationId.FromGuid(Guid.Parse("71e80ebd-f334-4068-8252-dda160c55e71"));

        Assert.NotEqual(left, right);
        Assert.False(left == right);
        Assert.True(left != right);
    }
}
