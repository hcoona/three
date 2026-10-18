using System.Text.Json;
using Nerdbank.GitVersioning;
using WorkflowDelivery.Platform;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

// Ruby source collection and Release preparation install the manifest-pinned nbgv CLI and read
// `get-version --format json`; these rows run that pinned package's CLI, while .NET planning
// evaluates the pinned NBGV library in process. These rows run both pins on the same commit
// and ref context and require the consumed fields to agree, so an NBGV upgrade that changes
// the CLI's projection, its JSON shape or its cloud-build ref handling fails here.
[TestClass]
public sealed class NbgvCliContractTests(TestContext context)
{
    private const string Project = "product";
    private const string Main = "refs/heads/main";
    private const string Tag = "refs/tags/release/product/v1.2.3";
    // The variables NBGV 3.10.94 reads to detect a cloud-build context, override public
    // release or select its git engine (NBGV_GitEngine, and DEPENDABOT or a GITHUB_ACTOR
    // naming a bot, which disable it). The CI job itself runs under GitHub Actions, so each
    // row starts from none of them and adds only its own. A provider's other variables matter
    // only after its detection variable is set.
    private static readonly string[] AmbientContext = ["GITHUB_ACTIONS", "GITHUB_REF",
        "GITHUB_SHA", "GITHUB_ENV", "GITHUB_EVENT_NAME", "GITHUB_ACTOR", "IGNORE_GITHUB_REF",
        "PublicRelease", "NBGV_GitEngine", "DEPENDABOT", "APPVEYOR", "SYSTEM_TEAMPROJECTID",
        "BUILD_VCS_NUMBER", "bamboo.buildKey", "JENKINS_URL", "GITLAB_CI", "TRAVIS",
        "JB_SPACE_GIT_REVISION", "BITBUCKET_PIPELINE_UUID"];
    [ClassInitialize]
    public static async Task RestoreAsync(TestContext classContext)
    {
        // The locked manifest restore CI already performs; it selects exactly the pin.
        NativeCommandResult restore = await NativeProcess.ExecuteAsync(new("dotnet",
            RepositoryToolSelection.SourceRoot, ["tool", "restore"], 300),
            classContext.CancellationToken);
        Assert.IsTrue(restore.Succeeded, NativeDiagnostic.Describe(restore));
    }

    [TestMethod]
    public async Task InstalledCliReportsTheManifestPin()
    {
        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(
            RepositoryToolSelection.SourceRoot, ".config", "dotnet-tools.json"),
            context.CancellationToken));
        string pin = manifest.RootElement.GetProperty("tools").GetProperty("nbgv")
            .GetProperty("version").GetString()!;
        NativeCommandResult version = await RunAsync(["--version"], []);
        Assert.StartsWith(pin + "+", version.Stdout.Trim());
        context.WriteLine("nbgv " + version.Stdout.Trim());
    }

    // main: the local branch selects a public release. feature: another local branch does
    // not. detached-tag: no branch, a matching tag on HEAD. cloud-branch and cloud-tag: a
    // detached checkout whose GitHub Actions ref supplies the release context, as Release
    // preparation sets it.
    [TestMethod]
    [DataRow("main", true)]
    [DataRow("feature", false)]
    [DataRow("detached-tag", true)]
    [DataRow("cloud-branch", true)]
    [DataRow("cloud-tag", true)]
    [DataRow("cloud-feature", false)]
    public async Task CliProjectionAgreesWithTheLibraryOnTheSameCommit(string row,
        bool publicRelease)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken,
            legacyNbgvCleanup: true);
        await repo.GitAsync("symbolic-ref", "HEAD", Main);
        await repo.SetAsync(Project + "/version.json", """
            {"version":"1.2-beta","pathFilters":["."],
             "publicReleaseRefSpec":["^refs/heads/main$","^refs/tags/release/product/v.+$"]}
            """);
        await repo.SetAsync(Project + "/source.txt", "first");
        string first = await repo.CommitAsync();
        await repo.SetAsync("unrelated/source.txt");
        string unrelated = await repo.CommitAsync(first);
        await repo.SetAsync(Project + "/source.txt", "second");
        string commit = await repo.CommitAsync(unrelated);
        await repo.GitAsync("reset", "--hard", commit);
        string? cloudRef = null;
        switch (row)
        {
            case "feature":
                await repo.GitAsync("branch", "feature", commit);
                await repo.GitAsync("symbolic-ref", "HEAD", "refs/heads/feature");
                break;
            case "detached-tag":
                await repo.GitAsync("tag", Tag["refs/tags/".Length..], commit);
                await repo.GitAsync("checkout", "--quiet", "--detach", commit);
                break;
            case "cloud-branch": cloudRef = Main; break;
            case "cloud-tag": cloudRef = Tag; break;
            case "cloud-feature": cloudRef = "refs/heads/feature"; break;
        }
        if (cloudRef is not null)
            await repo.GitAsync("checkout", "--quiet", "--detach", commit);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (cloudRef is not null)
        {
            environment["GITHUB_ACTIONS"] = "true";
            environment["GITHUB_REF"] = cloudRef;
            environment["GITHUB_SHA"] = commit;
        }

        // Production runs the CLI in the project directory without --project. The fixture
        // sits outside the source checkout, so the tool manifest is copied above it.
        string manifest = Path.Combine(Path.GetDirectoryName(repo.Directory)!, ".config");
        Directory.CreateDirectory(manifest);
        File.Copy(Path.Combine(RepositoryToolSelection.SourceRoot, ".config",
            "dotnet-tools.json"), Path.Combine(manifest, "dotnet-tools.json"));
        NativeCommandResult answer = await RunAsync(["get-version", "--format", "json"],
            environment, Path.Combine(repo.Directory, Project));
        using JsonDocument cli = JsonDocument.Parse(answer.Stdout);
        using (GitContext git = GitContext.Create(repo.Directory,
                   engine: GitContext.Engine.ReadOnly))
        {
            git.RepoRelativeProjectDirectory = Project;
            var library = new VersionOracle(git, cloudRef is null ? null : new Cloud(cloudRef,
                commit));

            Assert.AreEqual(JsonValueKind.String, cli.RootElement.GetProperty("SemVer2")
                .ValueKind);
            Assert.AreEqual(library.SemVer2, cli.RootElement.GetProperty("SemVer2").GetString());
            Assert.AreEqual(library.PublicRelease, cli.RootElement.GetProperty("PublicRelease")
                .GetBoolean());
            Assert.AreEqual(commit, cli.RootElement.GetProperty("GitCommitId").GetString());
            Assert.AreEqual(library.NuGetPackageVersion, cli.RootElement.GetProperty(
                "NuGetPackageVersion").GetString());
            Assert.AreEqual(library.NpmPackageVersion, cli.RootElement.GetProperty(
                "NpmPackageVersion").GetString());
            // The rows must exercise the release decision, not agree on a constant.
            Assert.AreEqual(publicRelease, library.PublicRelease);
            Assert.AreEqual(2, library.VersionHeight);
            // The fixture's own answer: version 1.2-beta at height 2 is 1.2.2-beta on a
            // public release, with the commit suffix otherwise.
            Assert.AreEqual(publicRelease ? "1.2.2-beta" : "1.2.2-beta.g" +
                commit[..10], cli.RootElement.GetProperty("SemVer2").GetString());
            context.WriteLine(row + " SemVer2=" + library.SemVer2);
        }
    }

    private async Task<NativeCommandResult> RunAsync(string[] arguments,
        Dictionary<string, string?> environment, string? directory = null)
    {
        var values = AmbientContext.ToDictionary(name => name, _ => (string?)null,
            StringComparer.Ordinal);
        foreach ((string name, string? value) in environment) values[name] = value;
        NativeCommandResult result = await NativeProcess.ExecuteAsync(new("dotnet",
            directory ?? RepositoryToolSelection.SourceRoot,
            ["tool", "run", "nbgv", "--", .. arguments], 120, values),
            context.CancellationToken);
        // NBGV can exit 0 with no output when it finds no repository, so require an answer.
        Assert.IsTrue(result.Succeeded && result.Stdout.Trim().Length != 0,
            NativeDiagnostic.Describe(result));
        return result;
    }

    // The library's GitHub Actions provider reads the process environment, which the test
    // cannot set per row; this supplies the same ref and commit the CLI row receives.
    private sealed class Cloud(string reference, string commit) : ICloudBuild
    {
        public bool IsApplicable => true;
        public bool IsPullRequest => false;
        public string? BuildingBranch => reference.StartsWith("refs/heads/",
            StringComparison.Ordinal) ? reference : null;
        public string? BuildingTag => reference.StartsWith("refs/tags/",
            StringComparison.Ordinal) ? reference : null;
        public string? GitCommitId => commit;
        public IReadOnlyDictionary<string, string> SetCloudBuildNumber(string buildNumber,
            TextWriter? stdout, TextWriter? stderr) => new Dictionary<string, string>();
        public IReadOnlyDictionary<string, string> SetCloudBuildVariable(string name,
            string value, TextWriter? stdout, TextWriter? stderr) =>
            new Dictionary<string, string>();
    }
}
