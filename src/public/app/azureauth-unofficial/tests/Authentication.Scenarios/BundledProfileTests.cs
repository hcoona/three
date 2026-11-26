using System.Text.Json;
using System.Text.Json.Nodes;
using Authentication.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Scenarios;

// The Windows package bundles the Visual Studio legacy compatibility Profile as an
// explicit opt-in file. It must stay the schema's concrete candidate, unchanged.
[TestClass]
public sealed class BundledProfileTests
{
    private static string Bundled(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BundledProfile", name));

    [TestMethod]
    public void BundledProfileEqualsTheSchemaCandidate()
    {
        var schema = JsonNode.Parse(Bundled("client-profile.schema.json"))!;
        var candidate = schema["examples"]!.AsArray().Single(example =>
            example!["integration"]!.GetValue<string>() == "visual-studio-legacy-wam");
        Assert.IsTrue(JsonNode.DeepEquals(candidate, JsonNode.Parse(Bundled("visual-studio-legacy-wam.json"))));
    }

    [TestMethod]
    public void BundledProfileIsAcceptedByTheProductReader()
    {
        var profile = ProfileSyntax.Parse(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "BundledProfile", "visual-studio-legacy-wam.json")));
        Assert.IsNotNull(profile);
        Assert.AreEqual("visual-studio-legacy-wam", profile.Integration);
        Assert.AreEqual(new Guid("872cd9fa-d31f-45e0-9eab-6e460a02d1f1"), profile.ClientId);
        Assert.AreEqual("Microsoft", profile.RegistrationOwner);
        Assert.IsNull(profile.FixedTenant);
    }
}
