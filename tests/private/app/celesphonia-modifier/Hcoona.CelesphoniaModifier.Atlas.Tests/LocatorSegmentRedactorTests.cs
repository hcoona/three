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
            LocatorSegment.DocumentRole("slot-save"),
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
            "slot-save/schema-key-000002/schema-key-000001/"
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
                    [LocatorSegment.DocumentRole("slot-save")])));
    }

    [Fact]
    public void RedactAllowsSubsetLocatorsAgainstCompleteAliasMap()
    {
        LocatorAliasMap aliasMap = LocatorSegmentRedactor.CreateAliasMap(
            [
                LocatorSegment.SchemaKey("beta"),
                LocatorSegment.SchemaKey("alpha"),
                LocatorSegment.DynamicKey("delta"),
                LocatorSegment.DynamicKey("charlie"),
            ]);

        Assert.Equal(
            "definition-source/schema-key-000001/dynamic-key-000002",
            LocatorSegmentRedactor.Redact(
                [
                    LocatorSegment.DocumentRole("definition-source"),
                    LocatorSegment.SchemaKey("alpha"),
                    LocatorSegment.DynamicKey("delta"),
                ],
                aliasMap));
        Assert.Equal(
            "schema-key-000002/dynamic-key-000001",
            LocatorSegmentRedactor.Redact(
                [
                    LocatorSegment.SchemaKey("beta"),
                    LocatorSegment.DynamicKey("charlie"),
                ],
                aliasMap));
        Assert.Throws<AtlasSafetyException>(() =>
            LocatorSegmentRedactor.Redact([LocatorSegment.DynamicKey("echo")], aliasMap));
    }

    [Fact]
    public void DocumentRolesMatchTheEgressEnvelopeContractExactly()
    {
        string[] allowedRoles =
        [
            "slot-save",
            "global-save",
            "config-save",
            "definition-source",
        ];
        foreach (string role in allowedRoles)
        {
            LocatorSegment segment = LocatorSegment.DocumentRole(role);
            LocatorAliasMap aliasMap = LocatorSegmentRedactor.CreateAliasMap([segment]);
            Assert.Equal(role, LocatorSegmentRedactor.Redact([segment], aliasMap));
        }

        Assert.Throws<AtlasSafetyException>(() =>
            LocatorSegmentRedactor.CreateAliasMap(
                [LocatorSegment.DocumentRole(AtlasIntakeContracts.CopyPlanRole)]));
    }

    [Fact]
    public void RedactionDoesNotEmitDynamicPrivateLiterals()
    {
        const string PrivateLiteral = "synthetic-private-locator";
        LocatorSegment[] segments =
        [
            LocatorSegment.DocumentRole("global-save"),
            LocatorSegment.DynamicKey(PrivateLiteral),
        ];
        LocatorAliasMap aliasMap = LocatorSegmentRedactor.CreateAliasMap(segments);

        string redacted = LocatorSegmentRedactor.Redact(segments, aliasMap);

        Assert.Equal("global-save/dynamic-key-000001", redacted);
        Assert.DoesNotContain(PrivateLiteral, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void LocatorAliasMapRejectsForgedOrdering()
    {
        Assert.Throws<AtlasSafetyException>(() =>
            new LocatorAliasMap(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["charlie"] = "dynamic-key-000001",
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["alpha"] = "schema-key-000002",
                    ["beta"] = "schema-key-000001",
                }));
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
