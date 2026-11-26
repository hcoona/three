using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonIndexObservationTests(TestContext context)
{
    private static readonly byte[] Wheel = [1, 2, 3];
    private static readonly byte[] Sdist = [4, 5, 6];

    [TestMethod]
    [DataRow("buddy", "testpypi", "test.pypi.org", "test-files.pythonhosted.org")]
    [DataRow("official", "pypi", "pypi.org", "files.pythonhosted.org")]
    [DataRow("buddy", "testpypi", "test.pypi.org", "test-files.pythonhosted.org",
        PythonReleaseUnits.Nbgv)]
    [DataRow("official", "pypi", "pypi.org", "files.pythonhosted.org",
        PythonReleaseUnits.Nbgv)]
    public async Task CompletePairUsesSelectedPublicIndexAndOriginalBytes(string channel,
        string destination, string origin, string storage,
        string unit = PythonReleaseUnits.Smoke)
    {
        using var fixture = new Fixture(channel, destination, unit);
        using var handler = new ScriptedHttpHandler((_, n) =>
            Response(200, n == 0 ? "{}"u8.ToArray() : n == 1 ? Wheel : Sdist));
        using var client = new HttpClient(handler);
        NativeCommand? command = null;

        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (value, _) =>
            {
                command = value;
                Assert.AreEqual("{}", File.ReadAllText(value.Arguments[4]));
                return Task.FromResult(Native(Facts(fixture.Prepared, storage)));
            }, context.CancellationToken);

        Assert.HasCount(2, result.Observation.Files);
        Assert.IsTrue(result.Observation.Files.All(file => file.State == ReleaseFileState.Exact));
        Assert.HasCount(3, handler.Requests);
        Assert.AreEqual($"https://{origin}/simple/{unit}/",
            handler.Requests[0].Uri.AbsoluteUri);
        Assert.IsTrue(handler.Requests.Skip(1).All(request => request.Uri.Host == storage));
        Assert.IsTrue(handler.Requests.All(request =>
            request.Headers.Authorization is null && !request.Headers.Contains("Cookie")));
        Assert.IsNotNull(command);
        Assert.IsFalse(command.InheritEnvironment);
        Assert.IsNull(command.Environment);
        Assert.AreEqual(30, command.DeadlineSeconds);
        Assert.AreEqual(fixture.Tools.Scratch, command.Directory);
        Assert.AreEqual("-I", command.Arguments[0]);
        Assert.AreEqual("-B", command.Arguments[1]);
        Assert.IsEmpty(Directory.GetFileSystemEntries(fixture.Tools.Scratch));
        Assert.AreEqual(ReleaseCheckStatus.Passed, result.Observation.Checks[0].Status);
        foreach (ReleaseFileObservation file in result.Observation.Files)
            Assert.AreEqual(fixture.Prepared.Files.Single(value => value.Output == file.Output)
                .Sha256, file.Sha256);
    }

    [TestMethod]
    [DataRow(404, "Absent")]
    [DataRow(403, "Unknown")]
    [DataRow(429, "Unknown")]
    [DataRow(503, "Unknown")]
    [DataRow(302, "Unknown")]
    public async Task UnsuccessfulIndexStatusCannotInvokeNativeFacts(int status,
        string expected)
    {
        using var fixture = new Fixture();
        using var handler = new ScriptedHttpHandler((_, _) => Response(status, []));
        using var client = new HttpClient(handler);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) =>
                throw new AssertFailedException("Unavailable index reached native parsing."),
            context.CancellationToken);
        Assert.HasCount(2, result.Observation.Files);
        Assert.IsTrue(result.Observation.Files.All(file => file.State ==
            Enum.Parse<ReleaseFileState>(expected)));
        Assert.HasCount(1, handler.Requests);
        Assert.IsEmpty(Directory.GetFileSystemEntries(fixture.Tools.Scratch));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingOriginalsRetainSupportedExactSubset(bool wheelPresent)
    {
        using var fixture = new Fixture();
        using var handler = new ScriptedHttpHandler((_, n) =>
            Response(200, n == 0 ? "{}"u8.ToArray() : Wheel));
        using var client = new HttpClient(handler);
        string facts = Facts(fixture.Prepared, "test-files.pythonhosted.org",
            wheelPresent ? 1 : 0);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(facts)),
            context.CancellationToken);
        Assert.AreEqual(wheelPresent ? ReleaseFileState.Exact : ReleaseFileState.Absent,
            result.Observation.Files[0].State);
        Assert.AreEqual(ReleaseFileState.Absent, result.Observation.Files[1].State);
        Assert.HasCount(wheelPresent ? 2 : 1, handler.Requests);
        Assert.IsTrue(ReleaseCompletion.Describe(fixture.Prepared, result.Observation,
            wheelPresent ? ["sdist"] : ["wheel", "sdist"]).Ready);
    }

    [TestMethod]
    [DataRow("yanked", "true")]
    [DataRow("sha256", "\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\"")]
    public async Task UnsafeWheelMetadataCannotPermitWrites(string field, string json)
    {
        await AssertMetadataBlocksWritesAsync(facts => facts["files"]![0]![field] =
            JsonNode.Parse(json), expectedReads: 2);
    }

    [TestMethod]
    public async Task WrongProjectCannotEstablishOwnership() =>
        await AssertMetadataBlocksWritesAsync(
            facts => facts["matches_name"] = false, expectedReads: 1);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExtraOrDuplicateFileCannotPermitWrites(bool duplicate) =>
        await AssertMetadataBlocksWritesAsync(facts =>
        {
            JsonArray files = facts["files"]!.AsArray();
            files.Add(duplicate ? files[0]!.DeepClone()
                : new JsonObject { ["filename"] = "undeclared.whl" });
        }, expectedReads: 1);

    private async Task AssertMetadataBlocksWritesAsync(Action<JsonObject> change,
        int expectedReads)
    {
        using var fixture = new Fixture();
        using var handler = new ScriptedHttpHandler((_, n) =>
            Response(200, n == 0 ? "{}"u8.ToArray() : Sdist));
        using var client = new HttpClient(handler);
        JsonObject facts = JsonNode.Parse(Facts(fixture.Prepared,
            "test-files.pythonhosted.org"))!.AsObject();
        change(facts);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared,
                (_, _) => Task.FromResult(Native(facts.ToJsonString())),
                context.CancellationToken);
        Assert.Contains(ReleaseFileState.Conflict,
            result.Observation.Files.Select(file => file.State));
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Observation.Checks[0].Status);
        Assert.IsFalse(ReleaseCompletion.Describe(fixture.Prepared, result.Observation, []).Ready);
        Assert.HasCount(expectedReads, handler.Requests);
    }

    [TestMethod]
    [DataRow("https://files.pythonhosted.org/file.whl")]
    public async Task UnselectedOrUnsafeStorageUrlCannotBeRead(string url)
    {
        using var fixture = new Fixture();
        using var handler = new ScriptedHttpHandler((_, _) => Response(200, [123, 125]));
        using var client = new HttpClient(handler);
        string facts = Facts(fixture.Prepared, "test-files.pythonhosted.org", url: url);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(facts)),
            context.CancellationToken);
        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
        Assert.IsFalse(ReleaseCompletion.Describe(fixture.Prepared, result.Observation, []).Ready);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains(url, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task MatchingMetadataCannotConcealConflictingOriginalBytes()
    {
        using var fixture = new Fixture();
        using var handler = new ScriptedHttpHandler((_, n) =>
            Response(200, n == 0 ? "{}"u8.ToArray() : [7, 8, 9]));
        using var client = new HttpClient(handler);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(
                Facts(fixture.Prepared, "test-files.pythonhosted.org"))),
            context.CancellationToken);
        Assert.IsTrue(result.Observation.Files.All(file =>
            file.State == ReleaseFileState.Conflict));
        Assert.HasCount(3, handler.Requests);
        Assert.IsFalse(ReleaseCompletion.Describe(fixture.Prepared, result.Observation, []).Ready);
    }

    [TestMethod]
    [DataRow("TimedOut")]
    [DataRow("Cancelled")]
    [DataRow("InvalidOutput")]
    [DataRow("Exited")]
    public async Task UnsuccessfulNativeFactsCannotAuthorizeWrites(string termination)
    {
        using var fixture = new Fixture();
        using var handler = new ScriptedHttpHandler((_, _) => Response(200, [123, 125]));
        using var client = new HttpClient(handler);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(
                new NativeCommandResult(Enum.Parse<NativeTermination>(termination), 1,
                    Facts(fixture.Prepared, "test-files.pythonhosted.org"),
                    "unsafe native output", 1, "unsafe native error")),
                context.CancellationToken);
        Assert.HasCount(2, result.Observation.Files);
        Assert.IsTrue(result.Observation.Files.All(file => file.State == ReleaseFileState.Unknown));
        Assert.HasCount(1, handler.Requests);
        Assert.Contains(termination, result.Reads[^1].Detail);
        Assert.DoesNotContain("unsafe native", JsonSerializer.Serialize(result));
        Assert.IsEmpty(Directory.GetFileSystemEntries(fixture.Tools.Scratch));
    }

    [TestMethod]
    [DataRow("not JSON")]
    [DataRow("{}")]
    [DataRow("{\"matches_name\":true,\"files\":null}")]
    public async Task SuccessfulNativeProcessStillRequiresCompleteFacts(string facts)
    {
        using var fixture = new Fixture();
        using var handler = new ScriptedHttpHandler((_, _) => Response(200, [123, 125]));
        using var client = new HttpClient(handler);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(facts)),
            context.CancellationToken);
        Assert.HasCount(2, result.Observation.Files);
        Assert.IsTrue(result.Observation.Files.All(file => file.State == ReleaseFileState.Unknown));
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Observation.Checks[0].Status);
        Assert.HasCount(1, handler.Requests);
        Assert.IsEmpty(Directory.GetFileSystemEntries(fixture.Tools.Scratch));
    }

    [TestMethod]
    [DataRow("other", "python-wheel")]
    [DataRow("testpypi", "other")]
    public async Task UnsupportedRequestCannotReadPublicIndex(string destination, string kind)
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared;
        prepared = prepared with { Subject = prepared.Subject with { Destination = destination } };
        prepared.Files[0] = prepared.Files[0] with { Kind = kind };
        using var handler = new ScriptedHttpHandler((_, _) =>
            throw new AssertFailedException("Unqualified request reached a public destination."));
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new PythonIndexObservation(client, fixture.Tools).ObserveAsync(prepared,
                (_, _) => throw new AssertFailedException(
                    "Unqualified request reached native facts."),
                context.CancellationToken));
        Assert.IsEmpty(handler.Requests);
        Assert.IsEmpty(Directory.GetFileSystemEntries(fixture.Tools.Scratch));
    }

    [TestMethod]
    public async Task ObservationDeadlineIncludesNativeIndexFacts()
    {
        using var fixture = new Fixture();
        var clock = new FakeTimeProvider();
        using var handler = new ScriptedHttpHandler((_, _) => Response(200, [123, 125]));
        using var client = new HttpClient(handler);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools, clock).ObserveAsync(fixture.Prepared, (_, _) =>
            {
                clock.Advance(TimeSpan.FromSeconds(91));
                return Task.FromResult(Native(Facts(fixture.Prepared,
                    "test-files.pythonhosted.org", 0)));
            }, context.CancellationToken);
        Assert.IsTrue(result.Observation.Files.All(file => file.State == ReleaseFileState.Unknown));
        Assert.AreEqual(ReleaseCheckStatus.TimedOut, result.Observation.Checks[0].Status);
        Assert.HasCount(1, handler.Requests);
        Assert.Contains("deadline", result.Reads[^1].Detail);
        Assert.IsFalse(ReleaseCompletion.Describe(fixture.Prepared, result.Observation,
            ["wheel", "sdist"]).Ready);
    }

    [TestMethod]
    public async Task CancellationAfterEarlierExactFileKeepsUnvisitedFileUnknown()
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        using var handler = new ScriptedHttpHandler((_, n) => n < 2
            ? Response(200, n == 0 ? Encoding.UTF8.GetBytes("{}") : Wheel)
            : new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new CancelledBody(cancellation)) });
        using var client = new HttpClient(handler);
        ReleaseObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(
                Facts(fixture.Prepared, "test-files.pythonhosted.org"))), cancellation.Token);
        Assert.AreEqual(ReleaseFileState.Exact, result.Observation.Files[0].State);
        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[1].State);
        Assert.AreEqual(ReleaseCheckStatus.Cancelled, result.Observation.Checks[0].Status);
        Assert.HasCount(3, handler.Requests);
        Assert.IsFalse(ReleaseCompletion.Describe(fixture.Prepared, result.Observation, []).Ready);
        Assert.IsEmpty(Directory.GetFileSystemEntries(fixture.Tools.Scratch));
    }

    private sealed class CancelledBody(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new AssertFailedException("Body escaped cancellation.");
        }
    }

    private static string Facts(PreparedRelease prepared, string host, int count = 2,
        string? url = null)
    {
        var entries = prepared.Files.Take(count).Select((file, i) => new Dictionary<string, object?>
        {
            ["filename"] = Path.GetFileName(file.Path),
            ["url"] = i == 0 && url is not null ? url
                : $"https://{host}/{Path.GetFileName(file.Path)}",
            ["sha256"] = file.Sha256,
            ["yanked"] = false,
            ["size"] = file.Size,
        }).ToArray();
        return JsonSerializer.Serialize(new { matches_name = true, files = entries });
    }

    private static NativeCommandResult Native(string facts) =>
        new(NativeTermination.Exited, 0, facts, "", 0.1, null);
    private static HttpResponseMessage Response(int status, byte[] bytes) =>
        new((HttpStatusCode)status) { Content = new ByteArrayContent(bytes) };

    private sealed class Fixture(string channel = "buddy", string destination = "testpypi",
        string unit = PythonReleaseUnits.Smoke) : IDisposable
    {
        private readonly string scratch =
            Directory.CreateTempSubdirectory("python-index-").FullName;
        internal PythonIndexTools Tools => new(Path.Combine(scratch, "python"),
            Path.Combine(scratch, "packages.py"), scratch);
        internal PreparedRelease Prepared => new(new(new('a', 40), "refs/heads/development",
            unit, "product/release.yml", channel, destination, true),
            [File("wheel", "python-wheel", Stem + "-1.2.3-py3-none-any.whl", Wheel),
             File("sdist", "python-sdist", Stem + "-1.2.3.tar.gz", Sdist)],
            ["contents"], [new("contents", ReleaseCheckStatus.Passed)]);
        private string Stem => unit.Replace('-', '_');
        private ReleaseFile File(string output, string kind, string name, byte[] bytes) =>
            new("build", output, kind, unit, "1.2.3", "original/" + name,
                bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        public void Dispose() => Directory.Delete(scratch, recursive: true);
    }
}
