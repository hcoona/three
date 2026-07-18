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
            LocatorSegment.DocumentRole(AtlasIntakeContracts.CopyPlanRole),
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
            $"{AtlasIntakeContracts.CopyPlanRole}/schema-key-000002/schema-key-000001/"
            + "dynamic-key-000002/dynamic-key-000001/2/@",
            redacted);
    }

    [Fact]
    public void RedactRejectsMissingAliasAndInvalidLiteral()
    {
        LocatorAliasMap aliasMap = new(
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Throws<AtlasSafetyException>(() =>
            LocatorSegmentRedactor.Redact([LocatorSegment.SchemaKey("alpha")], aliasMap));
        Assert.Throws<AtlasSafetyException>(() =>
            LocatorSegmentRedactor.Redact(
                [LocatorSegment.DocumentRole("document-root")],
                LocatorSegmentRedactor.CreateAliasMap(
                    [LocatorSegment.DocumentRole(AtlasIntakeContracts.CopyPlanRole)])));
    }

    [Fact]
    public void RedactRejectsForgedAliasPopulation()
    {
        LocatorAliasMap aliasMap = new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["charlie"] = "dynamic-key-000001",
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["alpha"] = "schema-key-000002",
                ["beta"] = "schema-key-000001",
            });

        Assert.Throws<AtlasSafetyException>(() =>
            LocatorSegmentRedactor.Redact(
                [
                    LocatorSegment.DocumentRole(AtlasIntakeContracts.CopyPlanRole),
                    LocatorSegment.SchemaKey("alpha"),
                    LocatorSegment.SchemaKey("beta"),
                    LocatorSegment.DynamicKey("charlie"),
                ],
                aliasMap));
    }

    [Fact]
    public void LocatorAliasMapCopiesCallerDictionaries()
    {
        Dictionary<string, string> dynamicAliases = new(StringComparer.Ordinal)
        {
            ["charlie"] = "dynamic-key-000001",
        };
        Dictionary<string, string> schemaAliases = new(StringComparer.Ordinal)
        {
            ["alpha"] = "schema-key-000001",
        };
        LocatorAliasMap aliasMap = new(dynamicAliases, schemaAliases);

        dynamicAliases["charlie"] = "dynamic-key-000099";
        schemaAliases["alpha"] = "schema-key-000099";

        Assert.Equal("dynamic-key-000001", aliasMap.DynamicKeyAliases["charlie"]);
        Assert.Equal("schema-key-000001", aliasMap.SchemaKeyAliases["alpha"]);
    }

    [Fact]
    public void LocatorAliasMapRejectsLiteralAliasValues()
    {
        Assert.Throws<AtlasSafetyException>(() =>
            new LocatorAliasMap(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["charlie"] = "charlie",
                },
                new Dictionary<string, string>(StringComparer.Ordinal)));
    }
}
