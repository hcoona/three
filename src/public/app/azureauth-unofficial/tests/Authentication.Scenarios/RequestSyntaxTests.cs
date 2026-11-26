using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

// Focused contract/core-rule tests for the accepted argument projection.
// Filesystem eligibility, actual process I/O and provider work are separate scenarios.
[TestClass]
public sealed class RequestSyntaxTests
{
    private const string ProfilePath = @"C:\Auth Profiles\selected.json";
    private const string Email = "Personal+Alias@example.test";
    private const string Resource = "499b84ac-1321-427f-aa17-267ca6975798";
    private const string Scope = Resource + "/user_impersonation";
    private const string Tenant = "11111111-2222-3333-4444-555555555555";

    [TestMethod]
    public void ExplicitPersonalRequestPreservesIntentAndAppliesOnlyOptionalDefaults()
    {
        var request = RequestSyntax.Parse(Arguments());

        Assert.IsNotNull(request);
        Assert.AreEqual(ProfilePath, request.ProfilePath);
        Assert.AreEqual(Email, request.AccountEmail);
        CollectionAssert.AreEqual(new[] { Scope }, request.Scopes.ToArray());
        Assert.IsFalse(request.InteractionAllowed);
        Assert.IsNull(request.Tenant);
        Assert.AreEqual(120, request.TimeoutSeconds);
        Assert.IsFalse(request.CancelOnStdinClose);
        Assert.IsFalse(request.TelemetryStderr);
    }

    [TestMethod]
    public void WorkRequestRetainsExactTenantAndExplicitHostControls()
    {
        var arguments = Arguments();
        Set(arguments, "--account-email", "work@example.test");
        Set(arguments, "--interaction", "interactive-if-needed");
        arguments.AddRange([
            "--tenant", Tenant, "--timeout-seconds", "30",
            "--cancel-on-stdin-close", "--telemetry", "stderr",
        ]);

        var request = RequestSyntax.Parse(arguments);

        Assert.IsNotNull(request);
        Assert.AreEqual("work@example.test", request.AccountEmail);
        Assert.AreEqual(Tenant, request.Tenant);
        Assert.IsTrue(request.InteractionAllowed);
        Assert.AreEqual(30, request.TimeoutSeconds);
        Assert.IsTrue(request.CancelOnStdinClose);
        Assert.IsTrue(request.TelemetryStderr);
    }

    [TestMethod]
    [DataRow("non-interactive-only", false, false)]
    [DataRow("interactive-if-needed", true, false)]
    [DataRow("force-interactive", true, true)]
    public void InteractionModesPreserveBothPermissionAndExplicitIntent(string mode, bool allowed, bool forced)
    {
        var arguments = Arguments();
        Set(arguments, "--interaction", mode);
        var request = RequestSyntax.Parse(arguments);
        Assert.IsNotNull(request);
        Assert.AreEqual(allowed, request.InteractionAllowed);
        Assert.AreEqual(forced, request.ForceInteractive);
    }

    [TestMethod]
    [DataRow("499b84ac-1321-427f-aa17-267ca6975798/.default")]
    [DataRow("api://example.test/application/read")]
    [DataRow("https://example.test/resource/read")]
    public void ExplicitResourcesDoNotNeedAResourceCatalog(string scope)
    {
        var arguments = Arguments();
        Set(arguments, "--scope", scope);

        var request = RequestSyntax.Parse(arguments);

        Assert.IsNotNull(request);
        CollectionAssert.AreEqual(new[] { scope }, request.Scopes.ToArray());
    }

    [TestMethod]
    public void MultipleDynamicPermissionsPreserveTheirSpellingAndOrder()
    {
        var arguments = Arguments();
        arguments.AddRange(["--scope", Resource + "/Read"]);

        var request = RequestSyntax.Parse(arguments);

        Assert.IsNotNull(request);
        CollectionAssert.AreEqual(new[] { Scope, Resource + "/Read" }, request.Scopes.ToArray());
    }

    [TestMethod]
    [DataRow("--protocol")]
    [DataRow("--profile")]
    [DataRow("--account-email")]
    [DataRow("--scope")]
    [DataRow("--interaction")]
    public void RequiredIntentHasNoImplicitSelection(string option)
    {
        var arguments = Arguments();
        arguments.RemoveRange(arguments.IndexOf(option), 2);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    [DataRow("unsupported-version")]
    [DataRow("repeated-scalar")]
    [DataRow("repeated-lifetime-flag")]
    [DataRow("unknown-option")]
    [DataRow("equals-syntax")]
    [DataRow("wrong-case")]
    [DataRow("positional-target")]
    [DataRow("response-file")]
    [DataRow("missing-value")]
    public void UnsupportedArgumentFormsCannotBecomeAnAuthenticationRequest(string defect)
    {
        var arguments = Arguments();
        switch (defect)
        {
            case "unsupported-version": Set(arguments, "--protocol", "2"); break;
            case "repeated-scalar": arguments.AddRange(["--protocol", "1"]); break;
            case "repeated-lifetime-flag":
                arguments.AddRange(["--cancel-on-stdin-close", "--cancel-on-stdin-close"]);
                break;
            case "unknown-option": arguments.AddRange(["--cache", "shared"]); break;
            case "equals-syntax":
                arguments.RemoveRange(1, 2);
                arguments.Insert(1, "--protocol=1");
                break;
            case "wrong-case": arguments[1] = "--Protocol"; break;
            case "positional-target": arguments.Add("repository"); break;
            case "response-file": arguments.Add("@request.rsp"); break;
            case "missing-value": arguments.Add("--tenant"); break;
            default: throw new InvalidOperationException("Unknown synthetic defect.");
        }

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    // No PAT path exists: every PAT-like option is an unknown option, so the
    // request fails closed before any Profile read or provider construction.
    [TestMethod]
    [DataRow("--pat", "SYNTHETIC_PAT")]
    [DataRow("--token", "SYNTHETIC_PAT")]
    [DataRow("--personal-access-token", "SYNTHETIC_PAT")]
    [DataRow("--pat-fallback", "true")]
    [DataRow("--allow-pat", "true")]
    [DataRow("--output", "pat")]
    [DataRow("--mode", "pat")]
    public void PersonalAccessTokenOptionsAreRejected(string option, string value)
    {
        var arguments = Arguments();
        arguments.AddRange([option, value]);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    [DataRow("--interaction", "pat")]
    [DataRow("--telemetry", "pat")]
    public void PersonalAccessTokenValuesAreRejected(string option, string value)
    {
        var arguments = Arguments();
        if (arguments.Contains(option)) Set(arguments, option, value);
        else arguments.AddRange([option, value]);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    [DataRow("--account-email", "missing-at")]
    [DataRow("--account-email", "@example.test")]
    [DataRow("--account-email", "a@@example.test")]
    [DataRow("--account-email", "a b@example.test")]
    [DataRow("--account-email", "a\u0000@example.test")]
    [DataRow("--account-email", "\uFEFFa@example.test")]
    [DataRow("--account-email", "a@example.test\uFEFF")]
    [DataRow("--scope", "499b84ac-1321-427f-aa17-267ca6975798/\uFEFFread")]
    [DataRow("--scope", "\uFEFF499b84ac-1321-427f-aa17-267ca6975798/read")]
    [DataRow("--profile", "relative.json")]
    [DataRow("--profile", @"C:relative.json")]
    [DataRow("--profile", @"\\server\share\profile.json")]
    [DataRow("--profile", "/home/profiles/selected.json")]
    [DataRow("--profile", @"\\?\C:\profile.json")]
    [DataRow("--interaction", "allow")]
    [DataRow("--scope", "openid")]
    [DataRow("--scope", "User.Read")]
    [DataRow("--scope", "https://example.test/resource/")]
    [DataRow("--scope", "https://user@example.test/read")]
    [DataRow("--scope", "https://example.test?query/read")]
    [DataRow("--scope", "https://example.test#fragment/read")]
    [DataRow("--scope", "https://example.test/read write")]
    [DataRow("--scope", "urn:resource/read")]
    public void InvalidCoreSelectorsFailClosed(string option, string value)
    {
        var arguments = Arguments();
        Set(arguments, option, value);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("601")]
    [DataRow("-1")]
    [DataRow("1.5")]
    [DataRow("NaN")]
    [DataRow("+1")]
    [DataRow("01")]
    [DataRow("0600")]
    [DataRow(" 1")]
    [DataRow("1 ")]
    [DataRow("1000")]
    [DataRow("\u0661")]
    [DataRow("")]
    public void DeadlineOverrideMustBeABoundedInteger(string value)
    {
        var arguments = Arguments();
        arguments.AddRange(["--timeout-seconds", value]);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    // .NET's "D" GUID parser alone accepts 0x and + segment prefixes and would
    // silently rewrite them; only the canonical 8-4-4-4-12 hexadecimal form passes.
    [TestMethod]
    [DataRow("0x111111-2222-3333-4444-555555555555")]
    [DataRow("0X111111-2222-3333-4444-555555555555")]
    [DataRow("11111111-0x22-3333-4444-555555555555")]
    [DataRow("11111111-2222-3333-4444-0x5555555555")]
    [DataRow("+1111111-2222-3333-4444-555555555555")]
    [DataRow("11111111-+222-3333-4444-555555555555")]
    [DataRow("{1111111-2222-3333-4444-55555555555}")]
    [DataRow("{11111111-2222-3333-4444-555555555555}")]
    [DataRow(" 1111111-2222-3333-4444-555555555555")]
    [DataRow("11111111-2222-3333-4444-55555555555 ")]
    [DataRow("11111111-222 -3333-4444-555555555555")]
    [DataRow("11111111222233334444555555555555")]
    [DataRow("11111111-2222-3333-4444-55555555555\u0661")]
    public void NonCanonicalTenantGuidIsRejected(string tenant)
    {
        var arguments = Arguments();
        arguments.AddRange(["--tenant", tenant]);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    [DataRow("0x9b84ac-1321-427f-aa17-267ca6975798")]
    [DataRow("499b84ac-0x21-427f-aa17-267ca6975798")]
    [DataRow("+99b84ac-1321-427f-aa17-267ca6975798")]
    [DataRow("499b84ac-1321-+27f-aa17-267ca6975798")]
    [DataRow("{99b84ac-1321-427f-aa17-267ca697579}")]
    [DataRow("499b84ac-1321-427f-aa17-0x7ca6975798")]
    public void NonCanonicalResourceGuidPrefixIsRejected(string resource)
    {
        var arguments = Arguments();
        Set(arguments, "--scope", resource + "/user_impersonation");

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    public void UppercaseCanonicalGuidsRemainValidAndUnchanged()
    {
        var arguments = Arguments();
        Set(arguments, "--scope", Resource.ToUpperInvariant() + "/user_impersonation");
        arguments.AddRange(["--tenant", "AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"]);

        var request = RequestSyntax.Parse(arguments);

        Assert.IsNotNull(request);
        Assert.AreEqual("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE", request.Tenant);
        CollectionAssert.AreEqual(new[] { Resource.ToUpperInvariant() + "/user_impersonation" }, request.Scopes.ToArray());
    }

    [TestMethod]
    [DataRow(Scope)]
    [DataRow("api://different.example.test/read")]
    [DataRow(Resource + "/.default")]
    public void DuplicateMixedResourceAndMixedDefaultPermissionsAreRejected(string extraScope)
    {
        var arguments = Arguments();
        arguments.AddRange(["--scope", extraScope]);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    [DataRow("--account-email", 320, true)]
    [DataRow("--account-email", 321, false)]
    [DataRow("--profile", 32767, true)]
    [DataRow("--profile", 32768, false)]
    [DataRow("--scope", 2048, true)]
    [DataRow("--scope", 2049, false)]
    public void SelectorLengthsMatchTheContract(string option, int length, bool accepted)
    {
        var arguments = Arguments();
        var value = option switch
        {
            "--account-email" => new string('a', length - 13) + "@example.test",
            "--profile" => @"C:\" + new string('a', length - 3),
            "--scope" => Resource + "/" + new string('a', length - Resource.Length - 1),
            _ => throw new InvalidOperationException("Unknown synthetic selector."),
        };
        Set(arguments, option, value);

        Assert.AreEqual(accepted, RequestSyntax.Parse(arguments) is not null);
    }

    [TestMethod]
    [DataRow(64, true)]
    [DataRow(65, false)]
    public void ScopeCountHasAnInclusiveLimit(int count, bool accepted)
    {
        var arguments = Arguments();
        for (var index = 1; index < count; index++)
        {
            arguments.AddRange(["--scope", Resource + "/permission" + index]);
        }

        Assert.AreEqual(accepted, RequestSyntax.Parse(arguments) is not null);
    }

    [TestMethod]
    [DataRow("1", 1)]
    [DataRow("600", 600)]
    [DataRow("30", 30)]
    public void ExplicitDeadlineAndTelemetryOffPreserveTheirMeaning(string text, int expected)
    {
        var arguments = Arguments();
        arguments.AddRange(["--timeout-seconds", text, "--telemetry", "off"]);

        var request = RequestSyntax.Parse(arguments);

        Assert.IsNotNull(request);
        Assert.AreEqual(expected, request.TimeoutSeconds);
        Assert.IsFalse(request.TelemetryStderr);
    }

    [TestMethod]
    [DataRow("COMMON")]
    [DataRow("organizations")]
    [DataRow("not-a-guid")]
    [DataRow("{11111111-2222-3333-4444-555555555555}")]
    public void InvalidTenantSelectorCannotEnterTheRequest(string tenant)
    {
        var arguments = Arguments();
        arguments.AddRange(["--tenant", tenant]);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    public void ResourceCaseIsNotNormalizedIntoAnEquivalentResource()
    {
        var arguments = Arguments();
        Set(arguments, "--scope", "https://EXAMPLE.test/read");
        arguments.AddRange(["--scope", "https://example.test/write"]);

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    public void EmptyUserInfoIsStillDisallowed()
    {
        var arguments = Arguments();
        Set(arguments, "--scope", "https://@example.test/read");

        Assert.IsNull(RequestSyntax.Parse(arguments));
    }

    [TestMethod]
    public void EmailLengthCountsUnicodeCharactersWithoutRewritingThem()
    {
        var arguments = Arguments();
        var email = string.Concat(Enumerable.Repeat("\U0001f600", 307)) + "@example.test";
        Set(arguments, "--account-email", email);

        var request = RequestSyntax.Parse(arguments);

        Assert.IsNotNull(request);
        Assert.AreEqual(email, request.AccountEmail);
    }

    private static List<string> Arguments() =>
    [
        "authenticate", "--protocol", "1", "--profile", ProfilePath,
        "--account-email", Email, "--scope", Scope, "--interaction", "non-interactive-only",
    ];

    private static void Set(List<string> arguments, string option, string value) =>
        arguments[arguments.IndexOf(option) + 1] = value;
}
