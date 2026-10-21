using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Tests.CI;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class BrowserStorePreparationTests(TestContext context)
{
    [TestMethod]
    [DataRow("chrome-web-store", "chrome-zip")]
    [DataRow("firefox-amo", "firefox-sources,firefox-zip")]
    [DataRow("edge-addons", "edge-zip")]
    public async Task OneUnprivilegedBuildQualifiesOnlyTheStoreOriginals(string store,
        string outputs)
    {
        using Fixture fixture = await Fixture.CreateAsync(store, context.CancellationToken);

        BrowserStorePreparationResult result = await fixture.RunAsync(
            context.CancellationToken);

        Assert.IsEmpty(result.Errors);
        Assert.IsTrue(result.Prepared.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
        Assert.AreEqual(outputs, string.Join(",", result.Prepared.Files.Select(file =>
            file.Output).Order(StringComparer.Ordinal)));
        Assert.IsTrue(result.Prepared.Files.All(file => file.Version == "1.2.0.7" &&
            file.Name == BrowserStorePreparation.Unit &&
            File.Exists(Path.Combine(fixture.Scratch, file.Path))));
        // Every store target is built once regardless of the selected store.
        Assert.AreEqual("zip,zip:firefox,zip:edge", string.Join(",", result.Commands
            .Where(command => command.Check == "build").Select(command =>
                command.Command.Arguments[^1])));
        Assert.IsTrue(result.Commands.All(command => !command.Command.InheritEnvironment &&
            command.Command.Environment!["HOME"] == Path.Combine(fixture.Scratch, "home") &&
            !command.Command.Environment.ContainsKey("GITHUB_TOKEN")));
        BrowserStoreRelease.Validate(result);
    }

    [TestMethod]
    [DataRow("version", "consumer")]
    [DataRow("reference", "consumer")]
    [DataRow("manifest-version", "consumer")]
    [DataRow("unsafe-path", "contents")]
    [DataRow("source-output", "contents")]
    [DataRow("source-lockfile", "contents")]
    [DataRow("missing", "build")]
    public async Task DefectiveStoreZipCannotQualify(string defect, string failed)
    {
        using Fixture fixture = await Fixture.CreateAsync("firefox-amo",
            context.CancellationToken);
        fixture.Defect = defect;

        BrowserStorePreparationResult result = await fixture.RunAsync(
            context.CancellationToken);

        Assert.IsEmpty(result.Prepared.Files);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks.Single(check =>
            check.Id == failed).Status);
        Assert.AreEqual(ReleaseCheckStatus.Skipped, result.Prepared.Checks.Single(check =>
            check.Id == "original-bytes").Status);
        Assert.IsNotEmpty(result.Errors);
    }

    [TestMethod]
    [DataRow("buddy", "refs/heads/main", false)]
    [DataRow("official", "refs/heads/feature", false)]
    [DataRow("official", "refs/tags/v1", false)]
    public async Task StoreRequestIsOfficialFromMainOrADryRun(string channel, string selected,
        bool dryRun)
    {
        using Fixture fixture = await Fixture.CreateAsync("chrome-web-store",
            context.CancellationToken);
        fixture.Request = fixture.Request with
        {
            Subject = fixture.Request.Subject with
            { Channel = channel, SelectedRef = selected, DryRun = dryRun }
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RunAsync(
            context.CancellationToken));
    }

    [TestMethod]
    public async Task BuddyDryRunFromADevelopmentBranchPreparesWithoutWrites()
    {
        using Fixture fixture = await Fixture.CreateAsync("edge-addons",
            context.CancellationToken);
        fixture.Request = fixture.Request with
        {
            Subject = fixture.Request.Subject with
            { Channel = "buddy" }
        };

        BrowserStorePreparationResult result = await fixture.RunAsync(
            context.CancellationToken);

        Assert.IsEmpty(result.Errors);
        Assert.ContainsSingle(result.Prepared.Files);
    }

    internal sealed class Fixture : IDisposable
    {
        private readonly GitFixture repository;
        internal BrowserStorePreparationRequest Request { get; set; }
        internal string Scratch => Request.Scratch;
        internal string? Defect { get; set; }

        private Fixture(GitFixture repository, BrowserStorePreparationRequest request)
        {
            this.repository = repository;
            Request = request;
        }

        internal static async Task<Fixture> CreateAsync(string store, CancellationToken token)
        {
            GitFixture repository = await GitFixture.CreateAsync(token);
            try
            {
                string product = BrowserStorePreparation.Product + "/";
                await repository.SetAsync(product + "package.json",
                    """{"name":"steam-account-history-to-csv","version":"0.0.0-placeholder"}""");
                await repository.SetAsync(product + "version.json",
                    """{"version":"1.2.0-dev.{height}"}""");
                await repository.SetAsync(BrowserStorePreparation.Declaration, """
                    schema: workflow-delivery/v3/release-unit
                    release-unit: steam-account-history-to-csv
                    builds:
                      - id: browser-extensions
                        definition: node/browser-extension-v1
                        entry-point: package.json
                        outputs:
                          - id: chrome-zip
                            role: primary-package
                            kind: chrome-extension-zip
                          - id: firefox-zip
                            role: primary-package
                            kind: firefox-extension-zip
                          - id: firefox-sources
                            role: source-package
                            kind: firefox-source-zip
                          - id: edge-zip
                            role: primary-package
                            kind: edge-extension-zip
                    """);
                await repository.SetAsync("mise.toml", RepositoryToolSelection.MiseToml);
                await repository.SetAsync("mise.lock", RepositoryToolSelection.MiseLock);
                foreach (string path in new[] { "pnpm-lock.yaml", "pnpm-workspace.yaml" })
                    await repository.SetAsync(path, "committed native fixture");
                string commit = await repository.CommitAsync();
                await repository.GitAsync("reset", "--hard", commit);
                return new(repository, new(new(commit, "refs/heads/feature",
                        BrowserStorePreparation.Unit, BrowserStorePreparation.Declaration,
                        "official", store, true),
                    new(store, "item", store == "chrome-web-store" ? "publisher" : null),
                    repository.Directory,
                    Directory.CreateTempSubdirectory("browser-store-").FullName));
            }
            catch { repository.Dispose(); throw; }
        }

        internal Task<BrowserStorePreparationResult> RunAsync(CancellationToken token) =>
            BrowserStorePreparation.PrepareAsync(Request, ExecuteAsync,
                (_, _, _, _) => new("1.2.0.7", "1.2.0-dev.7"), token);

        private Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string[] args = command.Arguments;
            string output = "";
            if (command.Executable == "mise")
            {
                string tool = args[^1];
                output = JsonSerializer.Serialize(new[] { new
                {
                    version = Selected(tool),
                    requested_version = RepositoryToolSelection.Requested(File.ReadAllText(
                        Path.Combine(repository.Directory, "mise.toml")), tool),
                    active = true,
                    source = new { type = "mise.toml",
                        path = Path.Combine(repository.Directory, "mise.toml") },
                } });
            }
            else if (args.Contains("--version"))
                output = (command.Executable == "node" ? "v" : "") + Selected(
                    command.Executable);
            else if (args[^2] == "run")
                Build(args[^1]);
            return Task.FromResult(NodeExecutionFixture.Success(output));
        }

        private void Build(string script)
        {
            string directory = Path.Combine(repository.Directory,
                BrowserStorePreparation.Product, ".output");
            Directory.CreateDirectory(directory);
            string browser = script switch
            {
                "zip" => "chrome",
                "zip:firefox" => "firefox",
                _ => "edge"
            };
            if (Defect == "missing" && browser == "edge") return;
            string Name(string suffix) => Path.Combine(directory,
                $"steam-account-history-to-csv-1.2.0-dev.7-{suffix}.zip");
            var manifest = new Dictionary<string, object>
            {
                ["manifest_version"] = Defect == "manifest-version" && browser == "chrome" ? 2
                    : browser == "firefox" ? 2 : 3,
                ["name"] = BrowserStorePreparation.ManifestName,
                ["version"] = Defect == "version" && browser == "edge" ? "1.2.0.8" : "1.2.0.7",
                ["icons"] = new Dictionary<string, string> { ["16"] = "icons/16.png" },
                ["content_scripts"] = new[] { new Dictionary<string, string[]>
                    { ["js"] = [Defect == "reference" && browser == "chrome"
                        ? "content-scripts/missing.js" : "content-scripts/history.js"] } },
            };
            if (browser != "firefox") manifest["version_name"] = "1.2.0-dev.7";
            var entries = new Dictionary<string, string>
            {
                ["manifest.json"] = JsonSerializer.Serialize(manifest),
                ["icons/16.png"] = "png",
                ["content-scripts/history.js"] = "script",
            };
            if (Defect == "unsafe-path" && browser == "firefox") entries["../escape"] = "x";
            Write(Name(browser), entries);
            if (browser == "firefox")
            {
                string product = BrowserStorePreparation.Product;
                var sources = new Dictionary<string, string>
                {
                    ["pnpm-lock.yaml"] = "lock",
                    ["pnpm-workspace.yaml"] = "workspace",
                    [product + "/package.json"] = "{}",
                    [product + "/wxt.config.ts"] = "config",
                };
                if (Defect == "source-output") sources[product + "/.output/old.zip"] = "x";
                if (Defect == "source-lockfile") sources.Remove("pnpm-lock.yaml");
                Write(Name("sources"), sources);
            }
        }

        internal static void Write(string path, Dictionary<string, string> entries)
        {
            using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach ((string name, string text) in entries)
            {
                using Stream stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        private string Selected(string tool) => RepositoryToolSelection.Locked(
            File.ReadAllText(Path.Combine(repository.Directory, "mise.lock")), tool);

        public void Dispose()
        {
            repository.Dispose();
            Directory.Delete(Scratch, recursive: true);
        }
    }
}
