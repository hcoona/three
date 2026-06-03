using Xunit;

namespace Hcoona.CfDdnsUpdater.Tests;

[Collection(TestCollectionDefinition.Name)]
public sealed class CloudflareDomainCanonicalizerTests
{
    [Fact]
    public void TryCanonicalizeStripsTrailingDotAndConvertsToAscii()
    {
        bool success = CloudflareDomainCanonicalizer.TryCanonicalize(
            "Bücher.Example.",
            out string canonicalDomain,
            out string? error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("xn--bcher-kva.example", canonicalDomain);
    }

    [Fact]
    public void EnumerateSuffixesWalksFromMostSpecificToLeastSpecific()
    {
        string[] suffixes =
        [
            ..CloudflareDomainCanonicalizer.EnumerateSuffixes("host.example.com"),
        ];

        Assert.Equal(["host.example.com", "example.com", "com"], suffixes);
    }
}
