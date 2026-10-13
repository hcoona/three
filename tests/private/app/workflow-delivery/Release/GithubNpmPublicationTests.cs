using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class GithubNpmPublicationTests
{
    [TestMethod]
    [DataRow("Exact", 0)]
    [DataRow("Conflict", 0)]
    [DataRow("Unknown", 0)]
    [DataRow("Absent", 1)]
    public async Task CurrentNativeStateControlsSinglePublication(string state, int writes)
    {
        using var files = new Files();
        var commands = new List<NativeCommand>();
        int reads = 0;

        GithubNpmPublicationResult result = await GithubNpmPublication.PublishAsync(files.Prepared,
            files.Originals, files.Runtime, files.Tools, Files.Credential,
            (command, _) =>
            {
                commands.Add(command);
                return Task.FromResult(Success(command.Arguments[0] == "--version"
                    ? Path.GetFileName(command.Executable) == "node" ? "v24.19.0" : "11.17.0"
                    : "published"));
            },
            (prepared, credential, _) =>
            {
                Assert.AreEqual(Files.Credential, credential);
                return Task.FromResult(Observe(prepared, reads++ == 0
                    ? Enum.Parse<ReleaseFileState>(state) : ReleaseFileState.Exact));
            }, CancellationToken.None);

        Assert.HasCount(writes, result.Uploads);
        Assert.AreEqual(writes, commands.Count(command => command.Arguments[0] == "publish"));
        Assert.AreEqual(writes + 1, reads);
        if (writes == 1)
        {
            NativeCommand publish = commands.Single(command => command.Arguments[0] == "publish");
            CollectionAssert.AreEqual(new[] { "publish",
                Path.Combine(files.Originals, "original/package.tgz"), "--registry",
                GithubNpmObservation.Registry, "--tag",
                "buddy-sha-" + files.Prepared.Subject.Revision,
                "--ignore-scripts", "--fetch-retries=0" }, publish.Arguments);
            Assert.IsFalse(publish.InheritEnvironment);
            Assert.AreEqual(120, publish.DeadlineSeconds);
            Assert.AreEqual(files.Runtime, publish.Directory);
            Assert.AreEqual(Files.Credential, publish.Environment!["GITHUB_TOKEN"]);
            Assert.IsFalse(publish.Environment.ContainsKey("NODE_OPTIONS"));
        }
    }

    [TestMethod]
    [DataRow("Exited", "Exact", "Satisfied")]
    [DataRow("TimedOut", "Exact", "Satisfied")]
    [DataRow("TimedOut", "Absent", "Unsatisfied")]
    [DataRow("Exited", "Unknown", "Unknown")]
    public async Task AbnormalUploadKeepsNativeFailureAndEvaluatesObservedBusinessState(
        string termination, string after, string business)
    {
        using var files = new Files();
        int reads = 0;
        int writes = 0;

        GithubNpmPublicationResult result = await GithubNpmPublication.PublishAsync(files.Prepared,
            files.Originals, files.Runtime, files.Tools, Files.Credential,
            (command, _) =>
            {
                if (command.Arguments[0] == "--version") return Task.FromResult(Success(
                    Path.GetFileName(command.Executable) == "node" ? "v24.19.0" : "11.17.0"));
                writes++;
                return Task.FromResult(new NativeCommandResult(Enum.Parse<NativeTermination>(
                    termination), 1, "failure " + Files.Credential,
                    "https://pkg-npm.githubusercontent.com/archive?signature=secret", 1,
                    "failed " + Files.Credential));
            },
            (prepared, _, _) => Task.FromResult(Observe(prepared, reads++ == 0
                ? ReleaseFileState.Absent : Enum.Parse<ReleaseFileState>(after))),
            CancellationToken.None);

        ReleaseUpload upload = Assert.ContainsSingle(result.Uploads);
        Assert.IsFalse(upload.Command.Succeeded);
        Assert.AreEqual(Enum.Parse<NativeTermination>(termination), upload.Command.Termination);
        Assert.DoesNotContain(Files.Credential, upload.Command.Stdout);
        Assert.DoesNotContain(Files.Credential, upload.Command.Error!);
        Assert.DoesNotContain("signature", upload.Command.Stderr);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(2, reads);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(business),
            ReleaseCompletion.Evaluate(files.Prepared, result.After.Observation,
                result.Uploads).State);
    }

    [TestMethod]
    public async Task SubstitutedOriginalStopsBeforeNativeToolsAndObservation()
    {
        using var files = new Files();
        File.WriteAllBytes(Path.Combine(files.Originals, "original/package.tgz"), [9]);
        int native = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => GithubNpmPublication.PublishAsync(
            files.Prepared, files.Originals, files.Runtime, files.Tools, Files.Credential,
            (_, _) => { native++; return Task.FromResult(Success("")); },
            (prepared, _, _) => { native++; return Task.FromResult(Observe(prepared,
                ReleaseFileState.Absent)); }, CancellationToken.None));

        Assert.AreEqual(0, native);
    }

    private static NativeCommandResult Success(string output) => new(NativeTermination.Exited,
        0, output, "", 0, null);

    private static GithubNpmObservationResult Observe(PreparedRelease prepared,
        ReleaseFileState state)
    {
        ReleaseFile file = prepared.Files[0];
        return new(new(prepared.Subject,
            [new(file.Output, file.Name, file.Version, state,
                state == ReleaseFileState.Exact ? file.Size : null,
                state == ReleaseFileState.Exact ? file.Sha256 : null)],
            ["visibility"], [new("visibility", ReleaseCheckStatus.Passed)]), []);
    }

    private sealed class Files : IDisposable
    {
        internal const string Credential = "controlled-test-token";
        private string Root { get; } = Directory.CreateTempSubdirectory("github-npm-").FullName;
        internal string Originals { get; }
        internal string Runtime { get; }
        internal string Tools { get; }
        internal PreparedRelease Prepared { get; }
        internal Files()
        {
            Originals = Directory.CreateDirectory(Path.Combine(Root, "outputs")).FullName;
            Runtime = Directory.CreateDirectory(Path.Combine(Root, "runtime")).FullName;
            Tools = Directory.CreateDirectory(Path.Combine(Root, "tools")).FullName;
            File.WriteAllText(Path.Combine(Tools, "selected-tools.json"),
                "{\"node\":\"24.19.0\",\"npm\":\"11.17.0\"}");
            Directory.CreateDirectory(Path.Combine(Originals, "original"));
            byte[] bytes = [1, 2, 3];
            File.WriteAllBytes(Path.Combine(Originals, "original/package.tgz"), bytes);
            Prepared = new(new(new('a', 40), "refs/heads/development",
                "hcoona-release-smoke-npm", "product/release.yml", "buddy", "github-packages-npm",
                false), [new("build", "archive", "npm-tarball", GithubNpmObservation.Package,
                    "1.2.3", "original/package.tgz", bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)))],
                ["consumer"], [new("consumer", ReleaseCheckStatus.Passed)]);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
