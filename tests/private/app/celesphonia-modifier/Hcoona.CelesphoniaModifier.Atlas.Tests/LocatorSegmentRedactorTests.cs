using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class LocatorSegmentRedactorTests
{
    [Fact]
    public void CreateAliasMapUsesStableTwoPassOrdering()
    {
        LocatorSegment[] segments =
        [
            LocatorSegment.DocumentRole("document-root"),
            LocatorSegment.SchemaKey("beta"),
            LocatorSegment.SchemaKey("alpha"),
            LocatorSegment.DynamicKey("delta"),
            LocatorSegment.DynamicKey("charlie"),
            LocatorSegment.ArrayIndex(2),
            LocatorSegment.JsonExMarker("@"),
        ];

        LocatorAliasMap aliasMap = LocatorSegmentRedactor.CreateAliasMap(segments);
        string redacted = LocatorSegmentRedactor.Redact(segments, aliasMap);

        Assert.Equal("schema-key-000001", aliasMap.SchemaKeyAliases["alpha"]);
        Assert.Equal("schema-key-000002", aliasMap.SchemaKeyAliases["beta"]);
        Assert.Equal("dynamic-key-000001", aliasMap.DynamicKeyAliases["charlie"]);
        Assert.Equal("dynamic-key-000002", aliasMap.DynamicKeyAliases["delta"]);
        Assert.Equal(
            "document-root/schema-key-000002/schema-key-000001/"
            + "dynamic-key-000002/dynamic-key-000001/2/@",
            redacted);
    }

    [Fact]
    public void RedactRejectsMissingAliasAndInvalidLiteral()
    {
        LocatorAliasMap aliasMap = new()
        {
            SchemaKeyAliases = new Dictionary<string, string>(StringComparer.Ordinal),
            DynamicKeyAliases = new Dictionary<string, string>(StringComparer.Ordinal),
        };

        Assert.Throws<AtlasSafetyException>(() =>
            LocatorSegmentRedactor.Redact([LocatorSegment.SchemaKey("alpha")], aliasMap));
        Assert.Throws<AtlasSafetyException>(() =>
            LocatorSegmentRedactor.Redact(
                [LocatorSegment.DocumentRole("Not Safe")],
                LocatorSegmentRedactor.CreateAliasMap(
                    [LocatorSegment.DocumentRole("document-root")])));
    }
}
