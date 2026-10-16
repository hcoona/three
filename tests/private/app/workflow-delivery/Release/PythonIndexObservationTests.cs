using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
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
    public async Task CompletePairUsesSelectedPublicIndexAndOriginalBytes(string channel,
        string destination, string origin, string storage)
    {
        using var fixture = new Fixture(channel, destination);
        using var handler = new Handler((_, n, _) => Task.FromResult(Response(200,
            n == 0 ? Encoding.UTF8.GetBytes("{}") : n == 1 ? Wheel : Sdist)));
        using var client = new HttpClient(handler);
        NativeCommand? command = null;

        PythonIndexObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (value, _) =>
            {
                command = value;
                Assert.AreEqual("{}", File.ReadAllText(value.Arguments[4]));
                return Task.FromResult(Native(Facts(fixture.Prepared, storage)));
            }, context.CancellationToken);

        Assert.HasCount(2, result.Observation.Files);
        Assert.IsTrue(result.Observation.Files.All(file => file.State == ReleaseFileState.Exact));
        Assert.HasCount(3, handler.Requests);
        Assert.AreEqual($"https://{origin}/simple/hcoona-release-smoke-python/",
            handler.Requests[0]);
        Assert.IsTrue(handler.Requests.Skip(1).All(uri => new Uri(uri).Host == storage));
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
        using var handler = new Handler((_, _, _) => Task.FromResult(Response(status, [])));
        using var client = new HttpClient(handler);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
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
        using var handler = new Handler((_, n, _) => Task.FromResult(Response(200,
            n == 0 ? Encoding.UTF8.GetBytes("{}") : Wheel)));
        using var client = new HttpClient(handler);
        string facts = Facts(fixture.Prepared, "test-files.pythonhosted.org",
            wheelPresent ? 1 : 0);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
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
    [DataRow("name")]
    [DataRow("extra")]
    [DataRow("duplicate")]
    [DataRow("yanked")]
    [DataRow("digest")]
    public async Task ContradictoryNativeMetadataCannotPermitWrites(string defect)
    {
        using var fixture = new Fixture();
        using var handler = new Handler((_, n, _) => Task.FromResult(Response(200,
            n == 0 ? Encoding.UTF8.GetBytes("{}") : Sdist)));
        using var client = new HttpClient(handler);
        string facts = Facts(fixture.Prepared, "test-files.pythonhosted.org", defect: defect);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(facts)),
            context.CancellationToken);
        Assert.Contains(ReleaseFileState.Conflict,
            result.Observation.Files.Select(file => file.State));
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Observation.Checks[0].Status);
        Assert.IsFalse(ReleaseCompletion.Describe(fixture.Prepared, result.Observation, []).Ready);
        Assert.HasCount(defect is "yanked" or "digest" or "size" ? 2 : 1, handler.Requests);
    }

    [TestMethod]
    [DataRow("http://test-files.pythonhosted.org/file.whl")]
    [DataRow("https://files.pythonhosted.org/file.whl")]
    [DataRow("https://example.invalid/file.whl")]
    [DataRow("https://user@test-files.pythonhosted.org/file.whl")]
    [DataRow("https://test-files.pythonhosted.org/file.whl#fragment")]
    [DataRow("https://test-files.pythonhosted.org:444/file.whl")]
    public async Task UnselectedOrUnsafeStorageUrlCannotBeRead(string url)
    {
        using var fixture = new Fixture();
        using var handler = new Handler((_, _, _) => Task.FromResult(Response(200, [123, 125])));
        using var client = new HttpClient(handler);
        string facts = Facts(fixture.Prepared, "test-files.pythonhosted.org", url: url);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
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
        using var handler = new Handler((_, n, _) => Task.FromResult(Response(200,
            n == 0 ? Encoding.UTF8.GetBytes("{}") : new byte[] { 7, 8, 9 })));
        using var client = new HttpClient(handler);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
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
        using var handler = new Handler((_, _, _) => Task.FromResult(Response(200, [123, 125])));
        using var client = new HttpClient(handler);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
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
        using var handler = new Handler((_, _, _) => Task.FromResult(Response(200, [123, 125])));
        using var client = new HttpClient(handler);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(facts)),
            context.CancellationToken);
        Assert.HasCount(2, result.Observation.Files);
        Assert.IsTrue(result.Observation.Files.All(file => file.State == ReleaseFileState.Unknown));
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Observation.Checks[0].Status);
        Assert.HasCount(1, handler.Requests);
        Assert.IsEmpty(Directory.GetFileSystemEntries(fixture.Tools.Scratch));
    }

    [TestMethod]
    [DataRow("destination")]
    [DataRow("kind")]
    public async Task UnsupportedRequestCannotReadPublicIndex(string defect)
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared;
        if (defect == "destination") prepared = prepared with
        { Subject = prepared.Subject with { Destination = "other" } };
        else prepared.Files[0] = prepared.Files[0] with { Kind = "other" };
        using var handler = new Handler((_, _, _) =>
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
    public async Task IndexBoundIncludesBodiesWithoutDeclaredLength()
    {
        using var fixture = new Fixture();
        using var handler = new Handler((_, _, _) =>
        {
            HttpResponseMessage response = Response(200, new byte[2 * 1024 * 1024 + 1]);
            response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(
                Facts(fixture.Prepared, "test-files.pythonhosted.org"))),
            context.CancellationToken);
        Assert.IsTrue(result.Observation.Files.All(file => file.State == ReleaseFileState.Unknown));
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Observation.Checks[0].Status);
        Assert.HasCount(1, handler.Requests);
        Assert.IsFalse(ReleaseCompletion.Describe(fixture.Prepared, result.Observation, []).Ready);
    }

    // A served body is read only one byte past the qualified original's size, so a longer
    // body is a conflict, never retained.
    [TestMethod]
    public async Task LongerOriginalBodyIsAConflict()
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared;
        using var handler = new Handler((_, n, _) =>
        {
            HttpResponseMessage response = Response(200, n == 0 ? Encoding.UTF8.GetBytes("{}")
                : n == 1 ? [.. Wheel, 0] : Sdist);
            response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(prepared, (_, _) => Task.FromResult(Native(
                Facts(prepared, "test-files.pythonhosted.org"))),
            context.CancellationToken);
        ReleaseFileObservation wheel = result.Observation.Files[0];
        Assert.AreEqual(ReleaseFileState.Conflict, wheel.State);
        Assert.IsNull(wheel.Size);
        Assert.AreEqual(ReleaseFileState.Exact, result.Observation.Files[1].State);
        Assert.HasCount(3, handler.Requests);
        Assert.IsFalse(ReleaseCompletion.Describe(prepared, result.Observation, []).Ready);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadAndPhaseDeadlinesIncludeBodyAndNativeFacts(bool nativePhase)
    {
        using var fixture = new Fixture();
        var clock = new ManualClock();
        using var handler = new Handler((_, _, _) => Task.FromResult(nativePhase
            ? Response(200, [123, 125])
            : new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new DeadlineBody(clock)) }));
        using var client = new HttpClient(handler);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
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
        using var handler = new Handler((_, n, _) => Task.FromResult(n < 2
            ? Response(200, n == 0 ? Encoding.UTF8.GetBytes("{}") : Wheel)
            : new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new CancelledBody(cancellation)) }));
        using var client = new HttpClient(handler);
        PythonIndexObservationResult result = await new PythonIndexObservation(client,
            fixture.Tools).ObserveAsync(fixture.Prepared, (_, _) => Task.FromResult(Native(
                Facts(fixture.Prepared, "test-files.pythonhosted.org"))), cancellation.Token);
        Assert.AreEqual(ReleaseFileState.Exact, result.Observation.Files[0].State);
        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[1].State);
        Assert.AreEqual(ReleaseCheckStatus.Cancelled, result.Observation.Checks[0].Status);
        Assert.HasCount(3, handler.Requests);
        Assert.IsFalse(ReleaseCompletion.Describe(fixture.Prepared, result.Observation, []).Ready);
        Assert.IsEmpty(Directory.GetFileSystemEntries(fixture.Tools.Scratch));
    }

    private sealed class DeadlineBody(ManualClock clock) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            clock.Advance(TimeSpan.FromSeconds(31));
            cancellationToken.ThrowIfCancellationRequested();
            throw new AssertFailedException("Body escaped its inclusive deadline.");
        }
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

    private sealed class ManualClock : TimeProvider
    {
        private TimeSpan elapsed;
        private readonly List<Timer> timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
        internal void Advance(TimeSpan duration)
        {
            elapsed += duration;
            foreach (Timer timer in timers.ToArray()) timer.Fire(elapsed);
        }
        private sealed class Timer(ManualClock clock, TimerCallback callback, object? state)
            : ITimer
        {
            private TimeSpan? due;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.elapsed + dueTime;
                return true;
            }
            internal void Fire(TimeSpan now)
            {
                if (due is { } value && value <= now)
                { due = null; callback(state); }
            }
            public void Dispose() => due = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static string Facts(PreparedRelease prepared, string host, int count = 2,
        string? defect = null, string? url = null)
    {
        var entries = prepared.Files.Take(count).Select((file, i) => new Dictionary<string, object?>
        {
            ["filename"] = Path.GetFileName(file.Path),
            ["url"] = i == 0 && url is not null ? url
                : $"https://{host}/{Path.GetFileName(file.Path)}",
            ["sha256"] = i == 0 && defect == "digest" ? new string('f', 64) : file.Sha256,
            ["yanked"] = i == 0 && defect == "yanked",
            ["size"] = i == 0 && defect == "size" ? file.Size + 1 : file.Size,
        }).ToList();
        if (defect == "duplicate") entries.Add(entries[0]);
        if (defect == "extra") entries.Add(new() { ["filename"] = "undeclared.whl" });
        return JsonSerializer.Serialize(new { matches_name = defect != "name", files = entries });
    }

    private static NativeCommandResult Native(string facts) =>
        new(NativeTermination.Exited, 0, facts, "", 0.1, null);
    private static HttpResponseMessage Response(int status, byte[] bytes) =>
        new((HttpStatusCode)status) { Content = new ByteArrayContent(bytes) };

    private sealed class Handler(Func<HttpRequestMessage, int, CancellationToken,
        Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        internal List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.IsNull(request.Headers.Authorization);
            Assert.IsFalse(request.Headers.Contains("Cookie"));
            Assert.AreEqual("identity",
                Assert.ContainsSingle(request.Headers.AcceptEncoding).Value);
            int index = Requests.Count;
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return response(request, index, cancellationToken);
        }
    }

    private sealed class Fixture(string channel = "buddy", string destination = "testpypi")
        : IDisposable
    {
        private readonly string scratch =
            Directory.CreateTempSubdirectory("python-index-").FullName;
        internal PythonIndexTools Tools => new(Path.Combine(scratch, "python"),
            Path.Combine(scratch, "packages.py"), scratch);
        internal PreparedRelease Prepared => new(new(new('a', 40), "refs/heads/development",
            "hcoona-release-smoke-python", "product/release.yml", channel, destination, true),
            [File("wheel", "python-wheel",
                "hcoona_release_smoke_python-1.2.3-py3-none-any.whl", Wheel),
             File("sdist", "python-sdist", "hcoona_release_smoke_python-1.2.3.tar.gz", Sdist)],
            ["contents"], [new("contents", ReleaseCheckStatus.Passed)]);
        private static ReleaseFile File(string output, string kind, string name, byte[] bytes) =>
            new("build", output, kind, "hcoona-release-smoke-python", "1.2.3", "original/" + name,
                bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        public void Dispose() => Directory.Delete(scratch, recursive: true);
    }
}
