using System.Text;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

[TestClass]
public sealed class ProfileSyntaxTests
{
    private const string ClientId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string Tenant = "11111111-2222-3333-4444-555555555555";
    private const string ProfileJson = """
        {
          "schemaVersion": 1,
          "name": "synthetic-selected-profile",
          "clientId": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
          "cloud": "public",
          "tenantPolicy": { "kind": "multitenant" },
          "integration": "windows-wam",
          "registration": {
            "owner": "Synthetic external owner",
            "externalDependency": true,
            "supportNotice": "Synthetic public configuration; no support commitment."
          }
        }
        """;

    [TestMethod]
    public void ExplicitOrdinaryProfilePreservesPublicClientAndOwnershipMetadata()
    {
        var profile = Parse(ProfileJson);

        Assert.IsNotNull(profile);
        Assert.AreEqual("synthetic-selected-profile", profile.Name);
        Assert.AreEqual(new Guid(ClientId), profile.ClientId);
        Assert.IsNull(profile.FixedTenant);
        Assert.AreEqual("windows-wam", profile.Integration);
        Assert.AreEqual("Synthetic external owner", profile.RegistrationOwner);
        Assert.AreEqual("Synthetic public configuration; no support commitment.", profile.SupportNotice);
    }

    [TestMethod]
    public void LegacyCandidateRetainsItsExactExternalRegistration()
    {
        var profile = Parse(LegacyProfileJson());

        Assert.IsNotNull(profile);
        Assert.AreEqual(new Guid("872cd9fa-d31f-45e0-9eab-6e460a02d1f1"), profile.ClientId);
        Assert.AreEqual("Microsoft", profile.RegistrationOwner);
        Assert.AreEqual("visual-studio-legacy-wam", profile.Integration);
        Assert.IsNull(profile.FixedTenant);
    }

    [TestMethod]
    public void SingleTenantDocumentPreservesItsFixedTenant()
    {
        var profile = Parse(WithSingleTenant(ProfileJson));

        Assert.IsNotNull(profile);
        Assert.AreEqual(new Guid(Tenant), profile.FixedTenant);
        Assert.AreEqual("windows-wam", profile.Integration);
    }

    [TestMethod]
    public void ProfileAtTheByteLimitIsAccepted()
    {
        var document = Encoding.UTF8.GetBytes(ProfileJson);
        var payload = new byte[64 * 1024];
        payload.AsSpan().Fill((byte)' ');
        document.CopyTo(payload, 0);

        Assert.IsNotNull(ProfileSyntax.Parse(payload));
    }

    [TestMethod]
    [DataRow("unsupported-version")]
    [DataRow("unknown-root-field")]
    [DataRow("duplicate-root-field")]
    [DataRow("unknown-tenant-field")]
    [DataRow("duplicate-tenant-field")]
    [DataRow("unknown-registration-field")]
    [DataRow("duplicate-registration-field")]
    [DataRow("unsupported-cloud")]
    [DataRow("unsupported-integration")]
    [DataRow("invalid-client")]
    [DataRow("unacknowledged-external-owner")]
    [DataRow("missing-owner")]
    [DataRow("missing-single-tenant")]
    [DataRow("legacy-wrong-client")]
    [DataRow("legacy-wrong-owner")]
    [DataRow("legacy-fixed-tenant")]
    public void InvalidProfileCannotConfigureAuthentication(string defect)
    {
        var document = defect switch
        {
            "unsupported-version" => ProfileJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2"),
            "unknown-root-field" => ProfileJson.Replace("\"schemaVersion\":", "\"unknown\": true, \"schemaVersion\":"),
            "duplicate-root-field" => ProfileJson.Replace("\"schemaVersion\":", "\"schemaVersion\": 1, \"schemaVersion\":"),
            "unknown-tenant-field" => ProfileJson.Replace("\"kind\":", "\"unknown\": true, \"kind\":"),
            "duplicate-tenant-field" => ProfileJson.Replace("\"kind\":", "\"kind\": \"multitenant\", \"kind\":"),
            "unknown-registration-field" => ProfileJson.Replace("\"owner\":", "\"unknown\": true, \"owner\":"),
            "duplicate-registration-field" => ProfileJson.Replace("\"owner\":", "\"owner\": \"First owner\", \"owner\":"),
            "unsupported-cloud" => ProfileJson.Replace("\"public\"", "\"arbitrary-cloud\""),
            "unsupported-integration" => ProfileJson.Replace("\"windows-wam\"", "\"browser\""),
            "invalid-client" => ProfileJson.Replace(ClientId, "not-a-guid"),
            "unacknowledged-external-owner" => ProfileJson.Replace("\"externalDependency\": true", "\"externalDependency\": false"),
            "missing-owner" => ProfileJson.Replace("\"owner\": \"Synthetic external owner\",", ""),
            "missing-single-tenant" => ProfileJson.Replace("\"multitenant\"", "\"single-tenant\""),
            "legacy-wrong-client" => LegacyProfileJson().Replace("872cd9fa-d31f-45e0-9eab-6e460a02d1f1", ClientId),
            "legacy-wrong-owner" => LegacyProfileJson().Replace("\"Microsoft\"", "\"Synthetic external owner\""),
            "legacy-fixed-tenant" => WithSingleTenant(LegacyProfileJson()),
            _ => throw new InvalidOperationException("Unknown synthetic defect."),
        };

        Assert.IsNull(Parse(document));
    }

    [TestMethod]
    public void OversizedOtherwiseValidProfileIsRejected()
    {
        Assert.IsNull(Parse(ProfileJson + new string(' ', 64 * 1024)));
    }

    // .NET's "D" GUID parser alone accepts 0x and + segment prefixes and would
    // silently rewrite them; only the canonical 8-4-4-4-12 hexadecimal form passes.
    [TestMethod]
    [DataRow("0xaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
    [DataRow("aaaaaaaa-0xbb-cccc-dddd-eeeeeeeeeeee")]
    [DataRow("aaaaaaaa-bbbb-cccc-dddd-0xeeeeeeeeee")]
    [DataRow("+aaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
    [DataRow("aaaaaaaa-bbbb-+ccc-dddd-eeeeeeeeeeee")]
    [DataRow("{aaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee}")]
    [DataRow(" aaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")]
    [DataRow("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeee ")]
    public void NonCanonicalClientIdIsRejected(string clientId)
    {
        Assert.IsNull(Parse(ProfileJson.Replace(ClientId, clientId)));
    }

    [TestMethod]
    [DataRow("0x111111-2222-3333-4444-555555555555")]
    [DataRow("11111111-2222-0x33-4444-555555555555")]
    [DataRow("+1111111-2222-3333-4444-555555555555")]
    [DataRow("11111111-2222-3333-+444-555555555555")]
    [DataRow("{1111111-2222-3333-4444-55555555555}")]
    [DataRow("11111111-2222-3333-4444-55555555555 ")]
    public void NonCanonicalFixedTenantIsRejected(string tenant)
    {
        Assert.IsNull(Parse(WithSingleTenant(ProfileJson).Replace(Tenant, tenant)));
    }

    // Each selector would otherwise parse to the fixed tenant's value.
    [TestMethod]
    [DataRow("0x111111-0222-0333-0444-055555555555")]
    [DataRow("+0111111-0222-0333-0444-055555555555")]
    [DataRow("00111111-0x22-0333-0444-055555555555")]
    [DataRow("00111111-0222-0333-0444-+55555555555")]
    public void NonCanonicalTenantSelectorIsRejected(string selector)
    {
        var fixedTenant = new Guid("00111111-0222-0333-0444-055555555555");
        Assert.IsFalse(ProfileSyntax.TryResolveTenant(SyntheticProfile(fixedTenant), selector, out _));
        Assert.IsFalse(ProfileSyntax.TryResolveTenant(SyntheticProfile(null), selector, out _));
    }

    [TestMethod]
    public void MalformedUtf8CannotBeSilentlyReplaced()
    {
        var document = Encoding.UTF8.GetBytes(ProfileJson);
        document[Array.IndexOf(document, (byte)'S')] = 0xff;

        Assert.IsNull(ProfileSyntax.Parse(document));
    }

    [TestMethod]
    public void OneLeadingUtf8ByteOrderMarkIsSkipped()
    {
        byte[] document = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(ProfileJson)];
        var profile = ProfileSyntax.Parse(document);

        Assert.IsNotNull(profile);
        Assert.AreEqual("synthetic-selected-profile", profile.Name);
        Assert.AreEqual(new Guid(ClientId), profile.ClientId);
    }

    [TestMethod]
    [DataRow("repeated")]
    [DataRow("not-leading")]
    [DataRow("bom-only")]
    [DataRow("utf16")]
    public void OtherByteOrderMarksRemainInvalid(string kind)
    {
        byte[] bom = [0xef, 0xbb, 0xbf];
        var json = Encoding.UTF8.GetBytes(ProfileJson);
        byte[] document = kind switch
        {
            "repeated" => [.. bom, .. bom, .. json],
            "not-leading" => [(byte)' ', .. bom, .. json],
            "bom-only" => bom,
            "utf16" => [0xff, 0xfe, .. Encoding.Unicode.GetBytes(ProfileJson)],
            _ => throw new InvalidOperationException("Unknown synthetic case."),
        };

        Assert.IsNull(ProfileSyntax.Parse(document));
    }

    [TestMethod]
    public void ByteOrderMarkCountsTowardTheByteLimit()
    {
        var payload = new byte[64 * 1024 + 1];
        payload.AsSpan().Fill((byte)' ');
        payload[0] = 0xef;
        payload[1] = 0xbb;
        payload[2] = 0xbf;
        Encoding.UTF8.GetBytes(ProfileJson).CopyTo(payload, 3);

        Assert.IsNull(ProfileSyntax.Parse(payload));
        Assert.IsNotNull(ProfileSyntax.Parse(payload.AsMemory(0, 64 * 1024)));
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("common", null)]
    [DataRow(Tenant, Tenant)]
    public void MultitenantProfilePreservesAnExplicitTenantOtherwiseUsesCommon(string? selector, string? expected)
    {
        var profile = SyntheticProfile(null);

        var valid = ProfileSyntax.TryResolveTenant(profile, selector, out var exactTenant);
        Guid? expectedTenant = expected is null ? null : new Guid(expected);

        Assert.IsTrue(valid);
        Assert.AreEqual(expectedTenant, exactTenant);
    }

    [TestMethod]
    [DataRow((object?)null)]
    [DataRow(Tenant)]
    public void SingleTenantProfileKeepsItsFixedTenant(string? selector)
    {
        var profile = SyntheticProfile(new Guid(Tenant));

        var valid = ProfileSyntax.TryResolveTenant(profile, selector, out var exactTenant);

        Assert.IsTrue(valid);
        Assert.AreEqual(new Guid(Tenant), exactTenant);
    }

    [TestMethod]
    [DataRow("common")]
    [DataRow("99999999-8888-7777-6666-555555555555")]
    public void ConflictingTenantCannotOverrideAFixedProfile(string selector)
    {
        Assert.IsFalse(ProfileSyntax.TryResolveTenant(SyntheticProfile(new Guid(Tenant)), selector, out _));
    }

    [TestMethod]
    [DataRow("1.0", true)]
    [DataRow("10e-1", true)]
    [DataRow("0.01e2", true)]
    [DataRow("1e+0000", true)]
    [DataRow("1.0000000000000000000000000000000000001", false)]
    [DataRow("0.9999999999999999999999999999999999999", false)]
    [DataRow("-1", false)]
    [DataRow("1e2147483647", false)]
    [DataRow("true", false)]
    [DataRow("\"1\"", false)]
    public void SchemaVersionUsesExactJsonNumericEquality(string number, bool accepted)
    {
        var document = ProfileJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": " + number);

        Assert.AreEqual(accepted, Parse(document) is not null);
    }

    [TestMethod]
    [DataRow("", false)]
    [DataRow("null", false)]
    [DataRow("[]", false)]
    [DataRow("{}", false)]
    public void ProfileRequiresOneCompleteConfigurationObject(string document, bool accepted)
    {
        Assert.AreEqual(accepted, Parse(document) is not null);
    }

    [TestMethod]
    public void DecodedDuplicateNamesCannotHideBehindJsonEscapes()
    {
        var document = ProfileJson.Replace("\"name\":", "\"na\\u006de\": \"first\", \"name\":");

        Assert.IsNull(Parse(document));
    }

    [TestMethod]
    public void MalformedEscapedUnicodeIsRejected()
    {
        Assert.IsNull(Parse(ProfileJson.Replace("synthetic-selected-profile", "\\ud800")));
    }

    [TestMethod]
    public void ASecondJsonValueCannotFollowTheProfile()
    {
        Assert.IsNull(Parse(ProfileJson + " {}"));
    }

    [TestMethod]
    [DataRow(128, true)]
    [DataRow(129, false)]
    public void ProfileNameLengthCountsUnicodeCharacters(int length, bool accepted)
    {
        var name = string.Concat(Enumerable.Repeat("\U0001f600", length));

        Assert.AreEqual(accepted, Parse(ProfileJson.Replace("synthetic-selected-profile", name)) is not null);
    }

    [TestMethod]
    [DataRow("name", "\u0007")]
    [DataRow("name", "\u007f")]
    [DataRow("name", "\u0085")]
    [DataRow("name", "\u009b")]
    [DataRow("name", "\u202e")]
    [DataRow("name", "\u2066")]
    [DataRow("name", "\u200b")]
    [DataRow("name", "\u200e")]
    [DataRow("name", "\ufeff")]
    [DataRow("name", "\U000e0041")]
    [DataRow("owner", "\u0085")]
    [DataRow("owner", "\u202e")]
    [DataRow("owner", "\u2067")]
    [DataRow("owner", "\ufeff")]
    public void DisplayedProfileMetadataRejectsControlAndFormatCharacters(string field, string character)
    {
        var document = field == "name"
            ? ProfileJson.Replace("synthetic-selected-profile", "synthetic" + character + "profile")
            : ProfileJson.Replace("Synthetic external owner", "Synthetic" + character + "owner");

        Assert.IsNull(Parse(document));
    }

    [TestMethod]
    public void DisplayedProfileMetadataKeepsOrdinaryUnicodeText()
    {
        var profile = Parse(ProfileJson
            .Replace("synthetic-selected-profile", "Profil \u00e9t\u00e9 \u05e9\u05dc\u05d5\u05dd \U0001f600")
            .Replace("Synthetic external owner", "\u6240\u6709\u8005"));

        Assert.IsNotNull(profile);
        Assert.AreEqual("Profil \u00e9t\u00e9 \u05e9\u05dc\u05d5\u05dd \U0001f600", profile.Name);
        Assert.AreEqual("\u6240\u6709\u8005", profile.RegistrationOwner);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("organizations")]
    [DataRow("COMMON")]
    [DataRow("{11111111-2222-3333-4444-555555555555}")]
    public void InvalidMultitenantSelectorDoesNotBecomeCommon(string selector)
    {
        Assert.IsFalse(ProfileSyntax.TryResolveTenant(SyntheticProfile(null), selector, out _));
    }

    private static ClientProfile? Parse(string document) => ProfileSyntax.Parse(Encoding.UTF8.GetBytes(document));

    private static ClientProfile SyntheticProfile(Guid? tenant) =>
        new("synthetic-selected-profile", new Guid(ClientId), tenant, "windows-wam",
            "Synthetic external owner", "Synthetic public configuration; no support commitment.");

    private static string LegacyProfileJson() => ProfileJson
        .Replace(ClientId, "872cd9fa-d31f-45e0-9eab-6e460a02d1f1")
        .Replace("\"windows-wam\"", "\"visual-studio-legacy-wam\"")
        .Replace("\"Synthetic external owner\"", "\"Microsoft\"");

    private static string WithSingleTenant(string document) =>
        document.Replace("\"kind\": \"multitenant\"", "\"kind\": \"single-tenant\", \"tenantId\": \"" + Tenant + "\"");
}
