using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class GithubNpmConsumerTests
{
    [TestMethod]
    [DataRow("sha512")]
    [DataRow("sha1")]
    public void NativeLockResultUsesDocumentedIntegrityAlgorithm(string algorithm)
    {
        byte[] bytes = [1, 2, 3];
#pragma warning disable CA5350 // Exercise the documented legacy native npm SRI result.
        GithubNpmConsumer.ValidateLock(Lock("1.2.3", "https://npm.pkg.github.com/archive",
            algorithm + "-" + Convert.ToBase64String(algorithm == "sha512"
                ? SHA512.HashData(bytes) : SHA1.HashData(bytes))), File(bytes), bytes);
#pragma warning restore CA5350
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("origin")]
    [DataRow("integrity")]
    [DataRow("duplicate")]
    public void NativeLockResultRejectsWrongInstalledSubject(string defect)
    {
        byte[] bytes = [1, 2, 3];
        string text = Lock(defect == "version" ? "2.0.0" : "1.2.3",
            defect == "origin" ? "https://unrelated.example/archive"
                : "https://npm.pkg.github.com/archive", "sha512-" + Convert.ToBase64String(
                    SHA512.HashData(defect == "integrity" ? [9] : bytes)));
        if (defect == "duplicate") text = text.Insert(1, "\"lockfileVersion\":3,");

        Assert.Throws<InvalidDataException>(() => GithubNpmConsumer.ValidateLock(text, File(bytes),
            bytes));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeScopedInstallAndImportSupplyRequiredConsumerResult(bool fail)
    {
        string root = Directory.CreateTempSubdirectory("npm-consumer-").FullName;
        try
        {
            string originals = Directory.CreateDirectory(Path.Combine(root, "outputs")).FullName;
            string runtime = Directory.CreateDirectory(Path.Combine(root, "runtime")).FullName;
            string tools = Directory.CreateDirectory(Path.Combine(root, "tools")).FullName;
            System.IO.File.WriteAllText(Path.Combine(tools, "selected-tools.json"),
                "{\"node\":\"24.19.0\",\"npm\":\"11.17.0\"}");
            Directory.CreateDirectory(Path.Combine(originals, "original"));
            byte[] bytes = [1, 2, 3];
            System.IO.File.WriteAllBytes(Path.Combine(originals, "original/package.tgz"), bytes);
            var prepared = new PreparedRelease(new(new('a', 40), "refs/heads/development", "unit",
                "product/release.yml", "buddy", "github-packages-npm", false), [File(bytes)],
                ["consumer"], [new("consumer", ReleaseCheckStatus.Passed)]);
            var calls = new List<NativeCommand>();

            ReleaseFile original = prepared.Files[0];
            var observation = new GithubNpmObservationResult(new(prepared.Subject,
                [new(original.Output, original.Name, original.Version, ReleaseFileState.Exact,
                    original.Size, original.Sha256)], ["visibility"],
                [new("visibility", ReleaseCheckStatus.Passed)]), [],
                Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                    "https://npm.pkg.github.com/archive"))));
            GithubNpmConsumerResult result = await GithubNpmConsumer.ConsumeAsync(prepared,
                observation, originals, runtime, tools, "controlled-secret",
                async (command, token) =>
                {
                    calls.Add(command);
                    if (command.Arguments[0] == "--version") return Success(
                        Path.GetFileName(command.Executable) == "node" ? "v24.19.0" : "11.17.0");
                    if (command.Arguments[0] == "install")
                    {
                        if (fail) return new(NativeTermination.Exited, 1, "controlled-secret",
                            "", 0, null);
                        await System.IO.File.WriteAllTextAsync(Path.Combine(runtime,
                            "package-lock.json"), Lock("1.2.3",
                                "https://npm.pkg.github.com/archive",
                                "sha512-" + Convert.ToBase64String(SHA512.HashData(bytes))), token);
                    }
                    return Success("");
                }, CancellationToken.None);

            Assert.AreEqual(prepared.Subject, result.Subject);
            Assert.AreEqual(fail ? ReleaseCheckStatus.Failed : ReleaseCheckStatus.Passed,
                result.Check.Status);
            NativeCommand install = calls.Single(command => command.Arguments[0] == "install");
            Assert.AreEqual(GithubNpmObservation.Package + "@1.2.3", install.Arguments[1]);
            Assert.Contains("--ignore-scripts", install.Arguments);
            Assert.Contains("--fetch-retries=0", install.Arguments);
            Assert.IsFalse(install.InheritEnvironment);
            Assert.AreEqual(fail ? 3 : 4, calls.Count);
            Assert.IsFalse(result.Commands.Any(command =>
                command.Stdout.Contains("controlled-secret", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(root, true); }
    }

    private static ReleaseFile File(byte[] bytes) => new("build", "archive", "npm-tarball",
        GithubNpmObservation.Package, "1.2.3", "original/package.tgz", bytes.Length,
        Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private static NativeCommandResult Success(string output) => new(NativeTermination.Exited,
        0, output, "", 0, null);

    private static string Lock(string version, string resolved, string integrity) =>
        JsonSerializer.Serialize(new
        {
            lockfileVersion = 3,
            packages = new Dictionary<string, object>
            {
                ["node_modules/" + GithubNpmObservation.Package] =
                    new { version, resolved, integrity },
            },
        });
}
