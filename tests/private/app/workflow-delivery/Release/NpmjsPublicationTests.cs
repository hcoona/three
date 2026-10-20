using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NpmjsPublicationTests
{
    private const string Identity = "controlled-oidc-request-token";

    [TestMethod]
    [DataRow("Exact", 0)]
    [DataRow("Conflict", 0)]
    [DataRow("Unknown", 0)]
    [DataRow("Absent", 1)]
    public async Task CurrentNativeStateControlsSingleTrustedPublication(string state,
        int writes)
    {
        using var files = new Files();
        var commands = new List<NativeCommand>();
        int reads = 0;

        NpmjsPublicationResult result = await NpmjsPublication.PublishAsync(files.Prepared,
            files.Originals, files.Runtime, files.Tools, Platform(),
            (command, _) =>
            {
                commands.Add(command);
                return Task.FromResult(Success(command.Arguments[0] == "--version"
                    ? Version(command) : "published " + Identity));
            },
            (prepared, _) => Task.FromResult(Observe(prepared, reads++ == 0
                ? Enum.Parse<ReleaseFileState>(state) : ReleaseFileState.Exact)),
            CancellationToken.None);

        Assert.HasCount(writes, result.Uploads);
        Assert.AreEqual(writes, commands.Count(command => command.Arguments[0] == "publish"));
        Assert.AreEqual(writes + 1, reads);
        Assert.AreEqual(state is "Exact" or "Absent", result.Errors.Length == 0);
        if (writes == 0) return;
        NativeCommand publish = commands.Single(command => command.Arguments[0] == "publish");
        CollectionAssert.AreEqual(new[] { "publish",
            Path.Combine(files.Originals, "original/package.tgz"), "--registry",
            "https://registry.npmjs.org/", "--access", "public", "--provenance", "--tag", "beta",
            "--ignore-scripts", "--fetch-retries=0" }, publish.Arguments);
        Assert.IsFalse(publish.InheritEnvironment);
        Assert.IsTrue(publish.CloseStandardInput);
        Assert.AreEqual(files.Runtime, publish.Directory);
        IReadOnlyDictionary<string, string?> environment = publish.Environment!;
        Assert.AreEqual("true", environment["GITHUB_ACTIONS"]);
        Assert.AreEqual(Identity, environment["ACTIONS_ID_TOKEN_REQUEST_TOKEN"]);
        Assert.AreEqual("https://token.actions.example/request",
            environment["ACTIONS_ID_TOKEN_REQUEST_URL"]);
        Assert.AreEqual("hcoona/three", environment["GITHUB_REPOSITORY"]);
        // No registry token or ambient configuration reaches the publisher.
        foreach (string name in new[] { "GITHUB_TOKEN", "NODE_AUTH_TOKEN", "NPM_TOKEN",
            "NODE_OPTIONS" })
            Assert.IsFalse(environment.ContainsKey(name), name);
        Assert.AreEqual("", File.ReadAllText(environment["NPM_CONFIG_USERCONFIG"]!));
        Assert.AreEqual("", File.ReadAllText(environment["NPM_CONFIG_GLOBALCONFIG"]!));
        Assert.DoesNotContain(Identity, Assert.ContainsSingle(result.Uploads).Command.Stdout);
    }

    [TestMethod]
    [DataRow("buddy", "github-packages-npm", false, "refs/heads/main")]
    [DataRow("official", "npmjs", true, "refs/heads/main")]
    [DataRow("official", "npmjs", false, "refs/heads/development")]
    [DataRow("official", "npmjs", false, "refs/heads/main", "hcoona-release-smoke-npm")]
    public async Task UnsupportedSubjectStopsBeforeNativeToolsAndObservation(string channel,
        string destination, bool dryRun, string selectedRef,
        string unit = "hexo-renderer-asciidoc")
    {
        using var files = new Files();
        int native = 0;
        PreparedRelease prepared = files.Prepared with { Subject = files.Prepared.Subject with
            { Channel = channel, Destination = destination, DryRun = dryRun,
                SelectedRef = selectedRef, Unit = unit } };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NpmjsPublication.PublishAsync(prepared, files.Originals, files.Runtime, files.Tools,
                Platform(), (_, _) => { native++; return Task.FromResult(Success("")); },
                (current, _) => { native++; return Task.FromResult(Observe(current,
                    ReleaseFileState.Absent)); }, CancellationToken.None));

        Assert.AreEqual(0, native);
    }

    [TestMethod]
    [DataRow("11.5.0", "24.22.0", null)]
    [DataRow("11.19.1", "22.13.1", null)]
    [DataRow("11.19.1", "24.22.0", "ACTIONS_ID_TOKEN_REQUEST_URL")]
    [DataRow("11.19.1", "24.22.0", "GITHUB_ACTIONS")]
    public async Task TrustedPublishingPrerequisitesStopBeforeObservation(string npm,
        string node, string? missing)
    {
        using var files = new Files(npm, node);
        Dictionary<string, string?> platform = Platform();
        if (missing is not null) platform.Remove(missing);
        int reads = 0;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NpmjsPublication.PublishAsync(files.Prepared, files.Originals, files.Runtime,
                files.Tools, platform, (command, _) => Task.FromResult(Success(
                    Path.GetFileName(command.Executable) == "node" ? "v" + node : npm)),
                (prepared, _) => { reads++; return Task.FromResult(Observe(prepared,
                    ReleaseFileState.Absent)); }, CancellationToken.None));

        Assert.AreEqual(0, reads);
    }

    [TestMethod]
    public async Task SubstitutedOriginalStopsBeforeNativeToolsAndObservation()
    {
        using var files = new Files();
        File.WriteAllBytes(Path.Combine(files.Originals, "original/package.tgz"), [9]);
        int native = 0;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            NpmjsPublication.PublishAsync(files.Prepared, files.Originals, files.Runtime,
                files.Tools, Platform(), (_, _) => { native++; return Task.FromResult(
                    Success("")); }, (prepared, _) => { native++; return Task.FromResult(
                    Observe(prepared, ReleaseFileState.Absent)); }, CancellationToken.None));

        Assert.AreEqual(0, native);
    }

    [TestMethod]
    [DataRow("4.0.0", "latest")]
    [DataRow("4.0.0-beta.12", "beta")]
    [DataRow("4.0.0-RC.1", "rc")]
    [DataRow("4.0.0-beta.12+build", "beta")]
    public void DistTagNeverMovesLatestForAPrerelease(string version, string tag) =>
        Assert.AreEqual(tag, NpmjsPublication.DistTag(version));

    [TestMethod]
    [DataRow("4.0.0+build")]
    [DataRow("4.0.0-1.2")]
    [DataRow("4.0.0-latest.1")]
    [DataRow("4.0.0-")]
    public void DistTagRejectsAmbiguousVersions(string version) =>
        Assert.ThrowsExactly<InvalidDataException>(() => NpmjsPublication.DistTag(version));

    private static Dictionary<string, string?> Platform() => new(StringComparer.Ordinal)
    {
        ["GITHUB_ACTIONS"] = "true",
        ["ACTIONS_ID_TOKEN_REQUEST_URL"] = "https://token.actions.example/request",
        ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = Identity,
        ["GITHUB_REPOSITORY"] = "hcoona/three",
        ["GITHUB_TOKEN"] = "ambient-token",
        ["NODE_AUTH_TOKEN"] = "ambient-token",
        ["NODE_OPTIONS"] = "--require ambient",
    };

    private static string Version(NativeCommand command) =>
        Path.GetFileName(command.Executable) == "node" ? "v24.22.0" : "11.19.1";

    private static NativeCommandResult Success(string output) => new(NativeTermination.Exited,
        0, output, "", 0, null);

    private static NpmjsObservationResult Observe(PreparedRelease prepared,
        ReleaseFileState state)
    {
        ReleaseFile file = prepared.Files[0];
        return new(new(prepared.Subject,
            [new(file.Output, file.Name, file.Version, state,
                state == ReleaseFileState.Exact ? file.Size : null,
                state == ReleaseFileState.Exact ? file.Sha256 : null)],
            ["metadata"], [new("metadata", ReleaseCheckStatus.Passed)]), []);
    }

    private sealed class Files : IDisposable
    {
        private string Root { get; } = Directory.CreateTempSubdirectory("npmjs-").FullName;
        internal string Originals { get; }
        internal string Runtime { get; }
        internal string Tools { get; }
        internal PreparedRelease Prepared { get; }
        internal Files(string npm = "11.19.1", string node = "24.22.0")
        {
            Originals = Directory.CreateDirectory(Path.Combine(Root, "outputs")).FullName;
            Runtime = Directory.CreateDirectory(Path.Combine(Root, "runtime")).FullName;
            Tools = Directory.CreateDirectory(Path.Combine(Root, "tools")).FullName;
            File.WriteAllText(Path.Combine(Tools, "selected-tools.json"),
                "{\"node\":\"" + node + "\",\"npm\":\"" + npm + "\"}");
            Directory.CreateDirectory(Path.Combine(Originals, "original"));
            byte[] bytes = [1, 2, 3];
            File.WriteAllBytes(Path.Combine(Originals, "original/package.tgz"), bytes);
            Prepared = new(new(new('a', 40), "refs/heads/main",
                NpmReleaseUnits.Hexo, "hexo/release.yml", "official", "npmjs", false),
                [new("build", "archive", "npm-tarball", "hexo-renderer-asciidoc",
                    "4.0.0-beta.12", "original/package.tgz", bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)))],
                ["consumer"], [new("consumer", ReleaseCheckStatus.Passed)]);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
