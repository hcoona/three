using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

// Scenarios of the ImageOcclusionEditor GitHub Release: Windows preparation with a
// controlled native toolchain, then observation, publication and readback against a
// controlled GitHub that keeps tags, releases and asset bytes.
[TestClass]
public sealed class GithubReleaseTests(TestContext context)
{
    private const string Revision = "0123456789abcdef0123456789abcdef01234567";
    private const string Version = "2.0.0-beta.5";
    private static readonly byte[] Setup = Encoding.ASCII.GetBytes("MZ controlled setup");
    private static readonly byte[] Portable = Encoding.ASCII.GetBytes("PK controlled zip");

    [TestMethod]
    public async Task WindowsPreparationBuildsOnceAndConsumesBothPackages()
    {
        using var fixture = await PreparationFixture.CreateAsync(context.CancellationToken);
        GithubReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        GithubRelease.Validate(result);
        string version = result.Native["version"];
        Assert.AreEqual("buddy/image-occlusion-editor/v" + version, result.Native["tag"]);
        CollectionAssert.AreEquivalent(new[]
        {
            "ImageOcclusionEditor-" + version + "-win-x64-setup.exe",
            "ImageOcclusionEditor-" + version + "-win-x64-portable.zip",
        }, result.Prepared.Files.Select(file => file.Name).ToArray());
        // Each product script runs once; both packages come from that one build.
        foreach (string script in new[] { "Update-LocalSvgEditor.ps1",
            "Publish-ImageOcclusionEditor.ps1", "Build-InnoInstaller.ps1" })
            Assert.ContainsSingle(result.Commands.Where(command =>
                command.Command.Arguments.Any(value => value.EndsWith(script,
                    StringComparison.Ordinal))));
        ReleaseCommand install = Assert.ContainsSingle(result.Commands.Where(command =>
            command.Check == "installer-consumer"));
        Assert.Contains("/VERYSILENT", install.Command.Arguments);
        Assert.Contains("/SUPPRESSMSGBOXES", install.Command.Arguments);
        foreach (ReleaseCommand command in result.Commands)
        {
            var environment = command.Command.Environment!;
            Assert.AreEqual(result.Prepared.Subject.SelectedRef, environment["GITHUB_REF"]);
            Assert.AreEqual(Revision.Length, environment["GITHUB_SHA"]!.Length);
            Assert.IsNull(environment["GITHUB_TOKEN"]);
            Assert.IsNull(environment["GH_TOKEN"]);
        }
        foreach (ReleaseFile file in result.Prepared.Files)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(fixture.Scratch, file.Path));
            Assert.AreEqual(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        // The portable package carries exactly the published tree.
        using ZipArchive archive = ZipFile.OpenRead(Path.Combine(fixture.Scratch,
            result.Prepared.Files.Single(file => file.Kind == "portable-zip").Path));
        CollectionAssert.AreEquivalent(PreparationFixture.Published.Keys.ToArray(),
            archive.Entries.Select(entry => entry.FullName).ToArray());
    }

    [TestMethod]
    [DataRow("linux", "native-input")]
    [DataRow("missing-editor", "contents")]
    [DataRow("install-fails", "installer-consumer")]
    [DataRow("install-wrong", "installer-consumer")]
    [DataRow("official-unreleased", "native-input")]
    public async Task PreparationStopsAtFirstUnqualifiedCheck(string defect, string failed)
    {
        using var fixture = await PreparationFixture.CreateAsync(context.CancellationToken);
        fixture.Defect = defect;
        if (defect == "official-unreleased")
            fixture.Subject = fixture.Subject with
            {
                Channel = "official", Destination = GithubReleaseUnits.Official,
                SelectedRef = "refs/heads/main",
            };
        GithubReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsNotEmpty(result.Errors);
        ReleaseCheck[] checks = result.Prepared.Checks;
        Assert.AreEqual(ReleaseCheckStatus.Failed, checks.Single(check => check.Id == failed)
            .Status);
        int index = Array.IndexOf(GithubReleasePreparation.Required, failed);
        Assert.IsTrue(checks.Skip(index + 1).All(check =>
            check.Status == ReleaseCheckStatus.Skipped));
        Assert.IsEmpty(result.Prepared.Files);
        Assert.ThrowsExactly<InvalidDataException>(() => GithubRelease.Validate(result));
    }

    [TestMethod]
    [DataRow("github-release", "refs/heads/feature", false)]
    [DataRow("github-release", "refs/heads/main", true)]
    [DataRow("github-release-prerelease", "refs/heads/feature", true)]
    [DataRow("github-release-prerelease", "refs/tags/v1", false)]
    [DataRow("github-packages", "refs/heads/main", false)]
    public void LiveAdmissionFollowsChannelRefPolicy(string destination, string reference,
        bool admitted)
    {
        ReleaseSubject subject = Subject(destination == "github-release") with
        {
            Destination = destination, SelectedRef = reference,
        };
        if (admitted) _ = GithubReleaseUnits.Admit(subject);
        else Assert.ThrowsExactly<InvalidDataException>(() =>
            GithubReleaseUnits.Admit(subject));
        // A dry run has no write capability and needs no live ref eligibility.
        if (destination.StartsWith("github-release", StringComparison.Ordinal))
            _ = GithubReleaseUnits.Admit(subject with { DryRun = true });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AbsentReleaseIsCreatedOnceThenReadBackByteForByte(bool official)
    {
        using var github = new FakeGithub();
        using var originals = new Originals();
        GithubReleasePreparationResult preparation = Preparation(official);
        PreparedRelease prepared = preparation.Prepared;
        GithubReleaseDestination destination = github.Destination();

        GithubReleaseObservationResult initial = await destination.ObserveAsync(prepared,
            context.CancellationToken);
        Assert.IsTrue(initial.Observation.Files.All(file =>
            file.State == ReleaseFileState.Absent));
        Assert.AreEqual("installer,portable", string.Join(",",
            GithubRelease.Missing(prepared, initial)));

        GithubReleasePublicationResult publication = await Publish(preparation, originals,
            initial, destination);
        Assert.IsEmpty(publication.Errors);
        Assert.IsTrue(publication.Creation!.Succeeded);
        Assert.HasCount(2, publication.Uploads);
        Assert.AreEqual(Revision, github.TagCommit);
        Assert.AreEqual((official ? "release/" : "buddy/") + "image-occlusion-editor/v" +
            Version, github.Release!.Tag);
        Assert.AreEqual(!official, github.Release!.Prerelease);
        Assert.AreEqual("image-occlusion-editor v" + Version, github.Release.Name);
        Assert.AreEqual(official ? null : "false", github.Release.MakeLatest);

        GithubReleaseObservationResult final = await destination.ObserveAsync(prepared,
            context.CancellationToken);
        GithubReleaseCompletionResult completion = GithubRelease.Complete(prepared, initial,
            final, publication);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, completion.Outcome.State,
            string.Join("; ", completion.Outcome.Reasons));
        Assert.IsTrue(completion.MutationAttempted);
        Assert.IsTrue(github.Requests.Where(request => request.Method == HttpMethod.Post)
            .All(request => request.Authorization == "Bearer controlled-token"));
    }

    [TestMethod]
    public async Task IdenticalExistingReleaseIsSatisfiedWithoutWrites()
    {
        using var github = new FakeGithub();
        github.Publish(Revision, prerelease: true, ("setup", Setup), ("portable", Portable));
        PreparedRelease prepared = Preparation(false).Prepared;
        GithubReleaseObservationResult observed = await github.Destination().ObserveAsync(
            prepared, context.CancellationToken);

        Assert.IsTrue(observed.Observation.Files.All(file =>
            file.State == ReleaseFileState.Exact));
        Assert.IsEmpty(GithubRelease.Missing(prepared, observed));
        GithubReleaseCompletionResult completion = GithubRelease.Complete(prepared, observed,
            observed, null);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, completion.Outcome.State);
        Assert.IsFalse(completion.MutationAttempted);
        Assert.IsFalse(github.Requests.Any(request => request.Method != HttpMethod.Get));
        // Served asset bytes are downloaded, not trusted from metadata.
        Assert.HasCount(2, github.Requests.Where(request =>
            request.Accept == "application/octet-stream"));
    }

    [TestMethod]
    [DataRow("different-bytes", "Conflict")]
    [DataRow("different-size", "Conflict")]
    [DataRow("other-tag-commit", "Conflict")]
    [DataRow("annotated-tag", "Conflict")]
    [DataRow("channel", "Conflict")]
    [DataRow("other-namespace", "Absent")]
    [DataRow("extra-asset", "Conflict")]
    [DataRow("starter-asset", "Unknown")]
    [DataRow("unavailable", "Unknown")]
    public async Task ExistingDifferentStateIsNeverOverwritten(string defect, string expected)
    {
        using var github = new FakeGithub();
        using var originals = new Originals();
        byte[] setup = defect == "different-bytes" ? Encoding.ASCII.GetBytes("MZ controlled setuq")
            : defect == "different-size" ? Encoding.ASCII.GetBytes("MZ other") : Setup;
        github.Publish(defect == "other-tag-commit" ? new string('b', 40) : Revision,
            prerelease: true, ("setup", setup));
        if (defect == "channel") github.MarkFullRelease();
        if (defect == "annotated-tag") github.AnnotatedTag = true;
        if (defect == "other-namespace") github.MoveToOtherChannel();
        if (defect == "extra-asset") github.AddAsset("notes.txt", "uploaded", [1, 2, 3]);
        if (defect == "starter-asset") github.AddAsset(Name("portable-zip"), "starter", Portable);
        if (defect == "unavailable") github.Unavailable = true;
        GithubReleasePreparationResult preparation = Preparation(false);
        PreparedRelease prepared = preparation.Prepared;
        GithubReleaseDestination destination = github.Destination();
        GithubReleaseObservationResult observed = await destination.ObserveAsync(prepared,
            context.CancellationToken);

        Assert.IsTrue(observed.Observation.Files.Any(file =>
            file.State == Enum.Parse<ReleaseFileState>(expected)));
        if (defect == "other-namespace")
        {
            // A release under the Official namespace is not this Buddy request's coordinate.
            Assert.IsTrue(observed.Observation.Files.All(file =>
                file.State == ReleaseFileState.Absent));
            Assert.IsFalse(github.Requests.Any(request => request.Method != HttpMethod.Get));
            return;
        }
        Assert.ThrowsExactly<InvalidDataException>(() =>
            GithubRelease.Missing(prepared, observed));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Publish(preparation,
            originals, observed, destination));
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(prepared, observed.Observation, []);
        Assert.AreNotEqual(ReleaseBusinessState.Satisfied, outcome.State);
        Assert.IsFalse(github.Requests.Any(request => request.Method != HttpMethod.Get));
    }

    [TestMethod]
    public async Task BuddyAndOfficialOfOneVersionUseDistinctTags()
    {
        using var github = new FakeGithub();
        using var originals = new Originals();
        foreach (bool official in new[] { false, true })
        {
            GithubReleasePreparationResult preparation = Preparation(official);
            GithubReleaseDestination destination = github.Destination();
            GithubReleaseObservationResult initial = await destination.ObserveAsync(
                preparation.Prepared, context.CancellationToken);
            // The earlier Buddy prerelease does not occupy the Official coordinate.
            Assert.IsTrue(initial.Observation.Files.All(file =>
                file.State == ReleaseFileState.Absent));
            GithubReleasePublicationResult publication = await Publish(preparation, originals,
                initial, destination);
            GithubReleaseObservationResult final = await destination.ObserveAsync(
                preparation.Prepared, context.CancellationToken);
            Assert.AreEqual(ReleaseBusinessState.Satisfied, GithubRelease.Complete(
                preparation.Prepared, initial, final, publication).Outcome.State);
        }
        CollectionAssert.AreEquivalent(new[]
        {
            "buddy/image-occlusion-editor/v" + Version,
            "release/image-occlusion-editor/v" + Version,
        }, github.Tags.ToArray());
    }

    [TestMethod]
    public async Task ConflictAppearingBeforeWritesStopsPublication()
    {
        using var github = new FakeGithub();
        using var originals = new Originals();
        GithubReleasePreparationResult preparation = Preparation(false);
        GithubReleaseDestination destination = github.Destination();
        GithubReleaseObservationResult initial = await destination.ObserveAsync(
            preparation.Prepared, context.CancellationToken);
        // Another writer publishes different bytes after the reviewed observation.
        github.Publish(Revision, prerelease: true, ("setup", Encoding.ASCII.GetBytes(
            "MZ other bytes!!!!")));

        GithubReleasePublicationResult publication = await Publish(preparation, originals,
            initial, destination);
        Assert.IsNotEmpty(publication.Errors);
        Assert.IsNull(publication.Creation);
        Assert.IsEmpty(publication.Uploads);
        Assert.IsFalse(github.Requests.Any(request => request.Method != HttpMethod.Get));
    }

    [TestMethod]
    [DataRow("create")]
    [DataRow("upload")]
    public async Task FailedWriteStopsFurtherWritesAndCompletionIsUnsatisfied(string failing)
    {
        using var github = new FakeGithub { Failing = failing };
        using var originals = new Originals();
        GithubReleasePreparationResult preparation = Preparation(false);
        GithubReleaseDestination destination = github.Destination();
        GithubReleaseObservationResult initial = await destination.ObserveAsync(
            preparation.Prepared, context.CancellationToken);

        GithubReleasePublicationResult publication = await Publish(preparation, originals,
            initial, destination);
        Assert.IsNotEmpty(publication.Errors);
        Assert.HasCount(failing == "create" ? 0 : 1, publication.Uploads);
        GithubReleaseObservationResult final = await destination.ObserveAsync(
            preparation.Prepared, context.CancellationToken);
        GithubReleaseCompletionResult completion = GithubRelease.Complete(preparation.Prepared,
            initial, final, publication);
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, completion.Outcome.State);
        // Without the publication result the effects of a required write stay unknown.
        Assert.AreEqual(ReleaseBusinessState.Unknown, GithubRelease.Complete(
            preparation.Prepared, initial, final, null).Outcome.State);
    }

    private static Task<GithubReleasePublicationResult> Publish(
        GithubReleasePreparationResult preparation, Originals originals,
        GithubReleaseObservationResult initial, GithubReleaseDestination destination) =>
        GithubRelease.PublishAsync(preparation, originals.Root, initial,
            destination.ObserveAsync, destination.CreateAsync, destination.UploadAsync,
            CancellationToken.None);

    private static ReleaseSubject Subject(bool official) => new(Revision,
        official ? "refs/heads/main" : "refs/heads/development",
        GithubReleaseUnits.ImageOcclusionEditor, GithubReleaseUnits.Declaration,
        official ? "official" : "buddy",
        official ? GithubReleaseUnits.Official : GithubReleaseUnits.Buddy, false);

    private static string Name(string kind) => GithubReleaseUnits.Asset(kind, Version);

    private static GithubReleasePreparationResult Preparation(bool official)
    {
        ReleaseFile File(string output, string kind, byte[] bytes, string path) =>
            new("windows-app", output, kind, Name(kind), Version, path, bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
        ReleaseSubject subject = Subject(official);
        return new(new(subject, [File("installer", "inno-installer", Setup, "originals/setup.exe"),
                File("portable", "portable-zip", Portable, "originals/portable.zip")],
                GithubReleasePreparation.Required, [.. GithubReleasePreparation.Required
                    .Select(id => new ReleaseCheck(id, ReleaseCheckStatus.Passed))]),
            new()
            {
                ["name"] = subject.Unit, ["version"] = Version, ["commit"] = Revision,
                ["public-release"] = "true",
                ["tag"] = GithubReleaseUnits.Tag(subject, Version),
            },
            [new("build", new("pwsh", "/", [], 1), new(NativeTermination.Exited, 0, "", "", 1,
                null))], []);
    }

    private sealed class Originals : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("github-originals-")
            .FullName;

        internal Originals()
        {
            Directory.CreateDirectory(Path.Combine(Root, "originals"));
            File.WriteAllBytes(Path.Combine(Root, "originals/setup.exe"), Setup);
            File.WriteAllBytes(Path.Combine(Root, "originals/portable.zip"), Portable);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    private sealed record Seen(HttpMethod Method, string Path, string? Authorization,
        string? Accept);

    // A controlled GitHub with one repository: lightweight or annotated tags, at most one
    // release per tag and its assets. Writes follow the native create and upload contract.
    // Release, TagCommit and AddAsset address the most recently published release.
    private sealed class FakeGithub : HttpMessageHandler
    {
        internal sealed record Asset(long Id, string Name, string State, byte[] Bytes);
        internal sealed record Hosted(long Id, string Tag, string Name, bool Prerelease,
            string? MakeLatest, List<Asset> Assets);

        private const string Repo = "/repos/hcoona/three";
        private long next = 100;
        private readonly Dictionary<string, string> tags = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Hosted> releases = new(StringComparer.Ordinal);
        internal string? TagCommit => Release is null ? null : tags[Release.Tag];
        internal bool AnnotatedTag { get; set; }
        internal bool Unavailable { get; set; }
        internal string? Failing { get; init; }
        internal Hosted? Release { get; private set; }
        internal IReadOnlyCollection<string> Tags => tags.Keys;
        internal List<Seen> Requests { get; } = [];

        internal GithubReleaseDestination Destination() =>
            new(new HttpClient(this, disposeHandler: false), "controlled-token");

        // Publishes at the tag of the channel a prerelease flag would normally select.
        internal void Publish(string commit, bool prerelease,
            params (string Kind, byte[] Bytes)[] assets)
        {
            string tag = (prerelease ? "buddy/" : "release/") + "image-occlusion-editor/v" +
                Version;
            tags[tag] = commit;
            Release = releases[tag] = new(next++, tag, "image-occlusion-editor v" + Version,
                prerelease, null, []);
            foreach ((string kind, byte[] bytes) in assets)
                AddAsset(Name(kind == "setup" ? "inno-installer" : "portable-zip"), "uploaded",
                    bytes);
        }

        // Places the current release under the other channel's tag, keeping its flag.
        internal void MoveToOtherChannel()
        {
            string tag = Release!.Tag.StartsWith("buddy/", StringComparison.Ordinal)
                ? "release/" + Release.Tag["buddy/".Length..]
                : "buddy/" + Release.Tag["release/".Length..];
            tags[tag] = tags[Release.Tag];
            tags.Remove(Release.Tag);
            releases.Remove(Release.Tag);
            Release = releases[tag] = Release with { Tag = tag };
        }

        internal void MarkFullRelease() =>
            Release = releases[Release!.Tag] = Release with { Prerelease = false };

        internal void AddAsset(string name, string state, byte[] bytes) =>
            Release!.Assets.Add(new(next++, name, state, bytes));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken token)
        {
            string path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            Requests.Add(new(request.Method, request.RequestUri.Host + path,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.FirstOrDefault()?.MediaType));
            if (Unavailable) return new(HttpStatusCode.BadGateway);
            Hosted? owner = releases.Values.SingleOrDefault(value =>
                path.StartsWith(Repo + "/releases/" + value.Id + "/", StringComparison.Ordinal));
            if (request.Method == HttpMethod.Get &&
                path.StartsWith(Repo + "/git/ref/tags/", StringComparison.Ordinal))
            {
                string tag = path[(Repo + "/git/ref/tags/").Length..];
                return !tags.TryGetValue(tag, out string? commit) ? new(HttpStatusCode.NotFound)
                    : Json(new JsonObject
                    {
                        ["ref"] = "refs/tags/" + tag,
                        ["object"] = new JsonObject
                        {
                            ["type"] = AnnotatedTag ? "tag" : "commit", ["sha"] = commit,
                        },
                    });
            }
            if (request.Method == HttpMethod.Get &&
                path.StartsWith(Repo + "/releases/tags/", StringComparison.Ordinal))
                return !releases.TryGetValue(path[(Repo + "/releases/tags/").Length..],
                    out Hosted? release) ? new(HttpStatusCode.NotFound) : Json(new JsonObject
                    {
                        ["id"] = release.Id, ["tag_name"] = release.Tag, ["draft"] = false,
                        ["prerelease"] = release.Prerelease,
                    });
            if (request.Method == HttpMethod.Get && owner is not null &&
                request.RequestUri.Host == "api.github.com" &&
                path == Repo + "/releases/" + owner.Id + "/assets")
                return Json(new JsonArray([.. owner.Assets.Select(asset => (JsonNode)
                    new JsonObject
                    {
                        ["id"] = asset.Id, ["name"] = asset.Name, ["state"] = asset.State,
                        ["size"] = asset.Bytes.LongLength,
                    })]));
            if (request.Method == HttpMethod.Get && releases.Values.SelectMany(value =>
                    value.Assets).SingleOrDefault(asset =>
                    path == Repo + "/releases/assets/" + asset.Id) is Asset served)
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(served.Bytes) };
            if (request.Method == HttpMethod.Post && path == Repo + "/releases")
            {
                if (Failing == "create") return new(HttpStatusCode.UnprocessableEntity)
                    { Content = new StringContent("{}") };
                JsonObject body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(
                    token))!.AsObject();
                string tag = (string)body["tag_name"]!;
                if (releases.ContainsKey(tag)) return new(HttpStatusCode.UnprocessableEntity)
                    { Content = new StringContent("{}") };
                tags.TryAdd(tag, (string)body["target_commitish"]!);
                Release = releases[tag] = new(next++, tag, (string)body["name"]!,
                    (bool)body["prerelease"]!, (string?)body["make_latest"], []);
                return Json(new JsonObject { ["id"] = Release.Id }, HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Post && owner is not null &&
                request.RequestUri.Host == "uploads.github.com" &&
                path == Repo + "/releases/" + owner.Id + "/assets")
            {
                if (Failing == "upload") return new(HttpStatusCode.InternalServerError)
                    { Content = new StringContent("{}") };
                string name = Uri.UnescapeDataString(request.RequestUri.Query["?name=".Length..]);
                owner.Assets.Add(new(next++, name, "uploaded",
                    await request.Content!.ReadAsByteArrayAsync(token)));
                return Json(new JsonObject { ["id"] = next - 1 }, HttpStatusCode.Created);
            }
            return new(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(JsonNode value,
            HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(value.ToJsonString(), Encoding.UTF8,
                "application/json") };
    }

    // A committed product checkout and a controlled Windows toolchain: the product scripts
    // write their documented outputs and the silent installer installs the published tree.
    private sealed class PreparationFixture(GitFixture repo, string revision) : IDisposable
    {
        internal static readonly Dictionary<string, byte[]> Published = new(StringComparer.Ordinal)
        {
            ["ImageOcclusionEditor.exe"] = Encoding.ASCII.GetBytes("MZ published application"),
            ["ImageOcclusionEditor.dll"] = [1, 2, 3, 4],
            ["svg-edit/index.html"] = Encoding.ASCII.GetBytes("<html></html>"),
        };
        internal string Scratch { get; } = Directory.CreateTempSubdirectory("github-release-")
            .FullName;
        internal ReleaseSubject Subject { get; set; } = Subject(false) with
        {
            Revision = revision,
        };
        internal string? Defect { get; set; }

        internal static async Task<PreparationFixture> CreateAsync(CancellationToken token)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            try
            {
                string product = GithubReleaseUnits.Product + "/";
                await repo.SetAsync("global.json",
                    "{\"sdk\":{\"version\":\"10.0.100\",\"rollForward\":\"disable\"}}");
                foreach (string input in new[] { ".config/dotnet-tools.json",
                    product + "version.json", product + GithubReleaseUnits.Entry,
                    product + "ImageOcclusionEditorWinUI3/Update-LocalSvgEditor.ps1",
                    product + "script/Helpers.ps1",
                    product + "script/Publish-ImageOcclusionEditor.ps1",
                    product + "script/Build-InnoInstaller.ps1", product + "script/Setup.iss" })
                    await repo.SetAsync(input);
                await repo.SetAsync(GithubReleaseUnits.Declaration, File.ReadAllText(
                    Path.Combine(RepositoryToolSelection.SourceRoot,
                        GithubReleaseUnits.Declaration)));
                string revision = await repo.CommitAsync();
                await repo.GitAsync("reset", "--hard", revision);
                return new(repo, revision);
            }
            catch { repo.Dispose(); throw; }
        }

        internal Task<GithubReleasePreparationResult> RunAsync(CancellationToken token) =>
            GithubReleasePreparation.PrepareAsync(new(Subject, repo.Directory, Scratch),
                ExecuteAsync, Defect != "linux", token);

        private string publish = "";

        private Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            string stdout = "";
            string[] args = command.Arguments;
            string Value(string name) => args[Array.IndexOf(args, name) + 1];
            if (command.Executable == "git") stdout = "false\n";
            else if (command.Executable == "dotnet" && args is ["--version"])
                stdout = "10.0.100\n";
            else if (command.Executable == "dotnet" && args.Contains("get-version"))
                stdout = new JsonObject
                {
                    ["SemVer2"] = Version + ".g0123456", ["GitCommitId"] = Subject.Revision,
                    ["PublicRelease"] = Defect != "official-unreleased",
                }.ToJsonString();
            else if (args.Any(arg => arg.EndsWith("Publish-ImageOcclusionEditor.ps1",
                         StringComparison.Ordinal)))
            {
                publish = Path.Combine(Value("-OutputRoot"),
                    "ImageOcclusionEditor/Release/net10.0-windows10.0.22000.0/win-x64");
                Write(publish, Defect == "missing-editor" ? "svg-edit/index.html" : null);
            }
            else if (args.Any(arg => arg.EndsWith("Build-InnoInstaller.ps1",
                         StringComparison.Ordinal)))
            {
                string output = Directory.CreateDirectory(Value("-InstallerOutputPath"))
                    .FullName;
                File.WriteAllBytes(Path.Combine(output, "ImageOcclusionEditorWinUI3_Setup.exe"),
                    Setup);
            }
            else if (command.Executable.EndsWith("-setup.exe", StringComparison.Ordinal))
            {
                if (Defect == "install-fails")
                    return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 5,
                        "", "", 1, null));
                string target = args.Single(arg => arg.StartsWith("/DIR=",
                    StringComparison.Ordinal))["/DIR=".Length..];
                Write(target, Defect == "install-wrong" ? "ImageOcclusionEditor.exe" : null);
            }
            return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0, stdout,
                "", 0.1, null));
        }

        private static void Write(string directory, string? omitted)
        {
            foreach ((string name, byte[] bytes) in Published)
            {
                if (name == omitted) continue;
                string path = Path.Combine(directory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
        }

        public void Dispose()
        {
            Directory.Delete(Scratch, true);
            repo.Dispose();
        }
    }
}
