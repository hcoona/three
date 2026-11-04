using System.Globalization;
using LiteAgensh;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LiteAgensh.Tests;

[TestClass]
public sealed class NativeUsageTests
{
    [TestMethod]
    [DataRow(1d, 0L, "0.000000001")]
    [DataRow(12_500_000_000d, 0L, "12.5")]
    [DataRow(67_250_000_000d, 2L, "67.25")]
    [DataRow(0d, 1L, "0")]
    public void CompleteMeasuredNanoUsageConvertsWithoutClamping(
        double nano,
        long requests,
        string expected
    )
    {
        NativeUsage usage = new("scripted-session", nano, "complete", requests);

        decimal actual = usage.RequireComplete();

        Assert.AreEqual(decimal.Parse(expected, CultureInfo.InvariantCulture), actual);
    }

    [TestMethod]
    [DataRow(null, "complete", 1L)]
    [DataRow(1d, null, 1L)]
    [DataRow(1d, "pending", 1L)]
    [DataRow(1d, "partial", 1L)]
    [DataRow(1d, "Complete", 1L)]
    [DataRow(-1d, "complete", 1L)]
    [DataRow(double.NaN, "complete", 1L)]
    [DataRow(double.PositiveInfinity, "complete", 1L)]
    [DataRow(double.NegativeInfinity, "complete", 1L)]
    [DataRow(double.MaxValue, "complete", 1L)]
    [DataRow(1d, "complete", -1L)]
    [DataRow(0d, "complete", 0L)]
    public void IncompleteInvalidOrEmptyUsageCannotBecomeMeasuredZero(
        double? nano,
        string? status,
        long requests
    )
    {
        NativeUsage usage = new("scripted-session", nano, status, requests);

        OperationException error = Assert.ThrowsExactly<OperationException>(() =>
            usage.RequireComplete()
        );

        Assert.AreEqual("credit_unknown", error.Code);
    }
}
