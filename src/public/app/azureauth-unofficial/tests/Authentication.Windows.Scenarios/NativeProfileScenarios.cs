using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Authentication.Windows.Scenarios;

// Separate finite entry: these two native launches are never added to MTP discovery.
// Exact artifact/source admission must establish rejection before provider effects.
internal static class NativeProfileScenarios
{
    private const string Duplicate = "native-profile-duplicate";
    private const string Unknown = "native-profile-unknown";
    private const string Marker = "SYNTHETIC_NATIVE_PROFILE_SECRET";

    internal static bool Supports(string scenario) => scenario is Duplicate or Unknown;

    internal static string Profile(string scenario)
    {
        var profile = ProcessChild.Profile.Replace("synthetic-process-profile", Marker, StringComparison.Ordinal);
        return scenario switch
        {
            Duplicate => profile.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1",
                StringComparison.Ordinal),
            Unknown => profile.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"unknown\":\"" + Marker + "\"",
                StringComparison.Ordinal),
            _ => throw new InvalidOperationException("Unallocated native Profile scenario."),
        };
    }

    internal static async Task<int> RunAsync(string executable)
    {
        try
        {
            foreach (var scenario in new[] { Duplicate, Unknown })
            {
                using var child = new ProcessFixture(scenario, executable);
                await child.FinishAsync();
                Assert.IsFalse(child.Forced, "Fixture enforcement cannot establish product completion.");
                Assert.AreEqual(1u, child.ExitCode);
                // Without telemetry, the drained process still waits for its fixed
                // completion indication within the shutdown bound.
                Assert.IsTrue("Authentication request completed.\n"u8.SequenceEqual(child.Error),
                    "Native Profile negatives emit at most the fixed completion indication.");
                var bytes = child.Output;
                Assert.IsTrue(bytes.Length > 1 && bytes[^1] == (byte)'\n');
                Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
                Assert.AreEqual(1, bytes.Count(value => value == (byte)'\n'));
                using var json = JsonDocument.Parse(bytes);
                var result = json.RootElement;
                Assert.AreEqual(1, result.GetProperty("protocol").GetInt32());
                Assert.AreEqual("invalid_request", result.GetProperty("outcome").GetString());
                Assert.AreEqual("invalid_configuration", result.GetProperty("reason").GetString());
                CollectionAssert.AreEquivalent(new[] { "protocol", "outcome", "reason" },
                    result.EnumerateObject().Select(property => property.Name).ToArray());
                Assert.IsFalse(Encoding.UTF8.GetString(bytes).Contains(Marker, StringComparison.Ordinal));
            }
            return 0;
        }
        catch (Exception)
        {
            // The fixture retains each original receipt/capture on assertion failure.
            // Stop the fixed batch; never retry or start its second case after failure.
            Console.Error.WriteLine("Native Profile scenario batch failed.");
            return 1;
        }
    }
}
