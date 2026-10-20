using System.IO.Compression;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class BrowserStorePreparationNativeTests(TestContext context)
{
    private static readonly string[] Targets = ["chrome", "firefox", "sources", "edge"];

    // One real WXT run under the locked Node and PNPM builds and qualifies every store zip; the
    // AMO request selects two of the four qualified originals.
    [TestMethod]
    [TestCategory("BrowserStoreNativePreparation")]
    [OSCondition(OperatingSystems.Linux)]
    [Timeout(1200000, CooperativeCancellation = true)]
    public async Task NativePreparationBuildsAndQualifiesEveryStoreZipFromTheCommittedProduct()
    {
        string source = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(source, "global.json")))
            source = Path.GetDirectoryName(source) ??
                throw new InvalidDataException("Missing source root.");
        string root = Directory.CreateTempSubdirectory("browser-store-native-").FullName;
        try
        {
            string checkout = Path.Combine(root, "source");
            string revision = (await NativeProcess.RunAsync("git", source, ["rev-parse", "HEAD"],
                context.CancellationToken)).Trim();
            await NativeProcess.RunAsync("git", source,
                ["clone", "--shared", "--no-checkout", "--", source, checkout],
                context.CancellationToken);
            NativeCommandResult materialized = await NativeProcess.ExecuteAsync(new("git",
                checkout, ["checkout", "--detach", revision], 120,
                new Dictionary<string, string?> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }),
                context.CancellationToken);
            Assert.IsTrue(materialized.Succeeded, materialized.Stderr);
            // The caller's prepare job installs the locked product graph without lifecycle
            // scripts before the unprivileged build.
            NativeCommandResult installed = await NativeProcess.ExecuteAsync(new("pnpm",
                checkout, ["install", "--frozen-lockfile", "--ignore-scripts",
                    "--filter", BrowserStorePreparation.Unit + "..."], 900),
                context.CancellationToken);
            Assert.IsTrue(installed.Succeeded, installed.Stdout + installed.Stderr);
            string scratch = Directory.CreateDirectory(Path.Combine(root, "preparation")).FullName;
            // Preparation isolates HOME, so it reaches the installed tools only through an
            // explicit mise data directory, as the caller provides.
            if (Environment.GetEnvironmentVariable("MISE_DATA_DIR") is not { Length: > 0 })
                Environment.SetEnvironmentVariable("MISE_DATA_DIR", Path.Combine(
                    Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data
                        ? data : Path.Combine(Environment.GetFolderPath(
                            Environment.SpecialFolder.UserProfile), ".local", "share"),
                    "mise"));
            var subject = new ReleaseSubject(revision, "refs/heads/preparation-development",
                BrowserStorePreparation.Unit, BrowserStorePreparation.Declaration, "official",
                BrowserStorePreparation.FirefoxAmo, true);
            BrowserStorePreparationResult result = await BrowserStorePreparation.PrepareAsync(
                new(subject, new(BrowserStorePreparation.FirefoxAmo, "item", null), checkout,
                    scratch), context.CancellationToken);
            string retained = Directory.CreateDirectory(Path.Combine(context.TestResultsDirectory!,
                "browser-store-native", Guid.NewGuid().ToString("N"))).FullName;
            string binding = Path.Combine(retained, "preparation.json");
            await File.WriteAllTextAsync(binding, JsonSerializer.Serialize(result),
                context.CancellationToken);
            context.AddResultFile(binding);
            Assert.IsEmpty(result.Errors, JsonSerializer.Serialize(new { result.Errors,
                FailedCommands = result.Commands.Where(command => !command.Result.Succeeded) }));
            BrowserStoreRelease.Validate(result);
            Assert.IsTrue(result.Prepared.Checks.All(check =>
                check.Status == ReleaseCheckStatus.Passed));
            Assert.AreEqual("firefox-zip,firefox-sources", string.Join(",",
                result.Prepared.Files.Select(file => file.Output)));
            Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded &&
                !command.Command.InheritEnvironment));
            string version = result.Prepared.Files[0].Version;
            string[] originals = Directory.GetFiles(Path.Combine(scratch, "original"));
            CollectionAssert.AreEquivalent(Targets,
                originals.Select(path => Path.GetFileNameWithoutExtension(path)
                    .Split('-')[^1]).ToArray());
            foreach (string path in originals)
            {
                string archive = Path.Combine(retained, Path.GetFileName(path));
                File.Copy(path, archive);
                context.AddResultFile(archive);
                using ZipArchive zip = ZipFile.OpenRead(path);
                if (path.EndsWith("-sources.zip", StringComparison.Ordinal))
                {
                    Assert.IsNotNull(zip.GetEntry("package.json"));
                    continue;
                }
                using Stream stream = zip.GetEntry("manifest.json")!.Open();
                using JsonDocument manifest = await JsonDocument.ParseAsync(stream,
                    cancellationToken: context.CancellationToken);
                Assert.AreEqual(version, manifest.RootElement.GetProperty("version")
                    .GetString());
            }
            Assert.AreEqual("", (await NativeProcess.RunAsync("git", checkout,
                ["status", "--porcelain", "--untracked-files=no"],
                context.CancellationToken)).Trim());
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(
                root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
