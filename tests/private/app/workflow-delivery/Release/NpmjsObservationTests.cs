using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NpmjsObservationTests(TestContext context)
{
    [TestMethod]
    public async Task ExactNativeCoordinateAndOriginalBytesRequireNoWitnessOrTarReparse()
    {
        byte[] original = Encoding.UTF8.GetBytes("qualified original bytes");
        PreparedRelease prepared = Prepare(original);
        using var handler = new Handler((request, number, _) => Task.FromResult(number == 0
            ? Response(200, Metadata()) : Response(200, original)));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(prepared,
            context.CancellationToken);

        ReleaseFileObservation file = Assert.ContainsSingle(result.Observation.Files);
        Assert.AreEqual(ReleaseFileState.Exact, file.State);
        Assert.AreEqual(prepared.Files[0].Sha256, file.Sha256);
        Assert.AreEqual(prepared.Files[0].Size, file.Size);
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual("https://registry.npmjs.org/@example%2Fpackage/1.2.3",
            handler.Requests[0].Uri);
        Assert.AreEqual("https://registry.npmjs.org/original.tgz", handler.Requests[1].Uri);
        Assert.IsTrue(handler.Requests.All(request => request.Method == "GET" &&
            request.Encoding == "identity" && !request.Authorization && !request.Cookie));
        Assert.AreEqual(ReleaseCheckStatus.Passed,
            Assert.ContainsSingle(result.Observation.Checks).Status);
    }

    [TestMethod]
    [DataRow(404, "Absent")]
    [DataRow(429, "Unknown")]
    [DataRow(503, "Unknown")]
    [DataRow(403, "Unknown")]
    public async Task MetadataStatusDefinesReadOnlyStateWithoutTarballRequest(int status,
        string state)
    {
        using var handler = new Handler((_, _, _) => Task.FromResult(Response(status, "")));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(Enum.Parse<ReleaseFileState>(state),
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow(404)]
    [DataRow(429)]
    [DataRow(502)]
    public async Task MissingTarballCannotEstablishCoordinateAbsence(int status)
    {
        using var handler = new Handler((_, number, _) => Task.FromResult(number == 0
            ? Response(200, Metadata()) : Response(status, "")));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task DifferentOriginalBytesAreConflictEvenWhenMetadataMatches()
    {
        using var handler = new Handler((_, number, _) => Task.FromResult(number == 0
            ? Response(200, Metadata()) : Response(200, new byte[] { 1, 2, 4 })));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Conflict,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.AreEqual(3L, result.Observation.Files[0].Size);
        Assert.AreEqual(ReleaseCheckStatus.Failed,
            Assert.ContainsSingle(result.Observation.Checks).Status);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("name")]
    [DataRow("version")]
    [DataRow("origin")]
    [DataRow("encoding")]
    public async Task UnsupportedMetadataStopsBeforeFurtherRequests(string defect)
    {
        string metadata = defect switch
        {
            "duplicate" => Metadata().Replace("\"version\":", "\"version\":\"1.2.3\",\"version\":",
                StringComparison.Ordinal),
            "name" => Metadata().Replace("@example/package", "other", StringComparison.Ordinal),
            "version" => Metadata().Replace("1.2.3", "9.9.9", StringComparison.Ordinal),
            "origin" => Metadata().Replace("registry.npmjs.org", "example.org",
                StringComparison.Ordinal),
            _ => Metadata(),
        };
        using var handler = new Handler((_, _, _) =>
        {
            HttpResponseMessage response = Response(200, metadata);
            if (defect == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow("https://example.org/file")]
    [DataRow("https://user@registry.npmjs.org/file")]
    [DataRow("https://registry.npmjs.org:444/file")]
    [DataRow("https://registry.npmjs.org/file#fragment")]
    [DataRow("http://registry.npmjs.org/file")]
    public async Task OffOriginRedirectIsRejectedBeforeItCanBeSent(string destination)
    {
        using var handler = new Handler((_, _, _) =>
        {
            HttpResponseMessage response = Response(302, "");
            response.Headers.Location = new(destination);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task RedirectLimitStopsWithoutBlindRetry()
    {
        using var handler = new Handler((_, number, _) =>
        {
            HttpResponseMessage response = Response(302, "");
            response.Headers.Location = new("/redirect-" + number, UriKind.Relative);
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(6, handler.Requests);
    }

    [TestMethod]
    public async Task TruncatedBodyCannotEstablishExactOrAbsentState()
    {
        using var handler = new Handler((_, _, _) =>
        {
            HttpResponseMessage response = Response(404, "short");
            response.Content.Headers.ContentLength = 100;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task CancellationDuringRequestRemainsUnknownAndStopsReads()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        using var handler = new Handler((_, _, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new AssertFailedException("Cancellation did not propagate.");
        });
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), cancellation.Token);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.Contains("Cancelled", result.Reads[^1].Detail);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task RedirectsAndBothBodiesShareOneOperationDeadline()
    {
        var clock = new ManualClock();
        using var handler = new Handler((_, number, token) =>
        {
            clock.Advance(TimeSpan.FromSeconds(16));
            token.ThrowIfCancellationRequested();
            if (number < 2)
            {
                HttpResponseMessage redirect = Response(302, "");
                redirect.Headers.Location = new("/next", UriKind.Relative);
                return Task.FromResult(redirect);
            }
            return Task.FromResult(Response(200, Metadata()));
        });
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client, clock).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(4, handler.Requests);
        Assert.Contains("deadline", result.Reads[^1].Detail);
    }

    [TestMethod]
    public async Task IndividualRequestDeadlineIncludesResponseBody()
    {
        var clock = new ManualClock();
        using var handler = new Handler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new DeadlineStream(clock)) }));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client, clock).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
        Assert.Contains("deadline", result.Reads[^1].Detail);
    }

    private sealed class DeadlineStream(ManualClock clock) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            clock.Advance(TimeSpan.FromSeconds(21));
            cancellationToken.ThrowIfCancellationRequested();
            throw new AssertFailedException("Body read escaped its request deadline.");
        }
    }

    [TestMethod]
    public async Task CancellationDuringResponseBodyStopsBeforeTarballRead()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        using var handler = new Handler((_, _, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StreamContent(new CancelledBody(cancellation)) }));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), cancellation.Token);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
        Assert.Contains("Cancelled", result.Reads[^1].Detail);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OversizedMetadataCannotAuthorizeHypotheticalCreation(bool declared)
    {
        using var handler = new Handler((_, _, _) =>
        {
            HttpResponseMessage response = Response(404, new byte[1_000_001]);
            if (!declared) response.Content.Headers.ContentLength = null;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        PreparedRelease prepared = Prepare([1, 2, 3]);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(prepared,
            context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.IsFalse(ReleaseCompletion.Describe(prepared, result.Observation, []).Ready);
        Assert.HasCount(1, handler.Requests);
    }

    private sealed class CancelledBody(CancellationTokenSource cancellation) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new AssertFailedException("Body cancellation did not propagate.");
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

    private static PreparedRelease Prepare(byte[] bytes) => new(
        new(new('a', 40), "refs/heads/development", "example", "product/release.yml",
            "official", "npmjs", true),
        [new("build", "archive", "npm-tarball", "@example/package", "1.2.3",
            "original/package.tgz", bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)))],
        ["consumer"], [new("consumer", ReleaseCheckStatus.Passed)]);

    private static string Metadata() => """
        {"name":"@example/package","version":"1.2.3",
         "dist":{"tarball":"https://registry.npmjs.org/original.tgz"}}
        """;

    private static HttpResponseMessage Response(int status, string text) =>
        Response(status, Encoding.UTF8.GetBytes(text));

    private static HttpResponseMessage Response(int status, byte[] bytes) =>
        new((HttpStatusCode)status) { Content = new ByteArrayContent(bytes) };

    private sealed record Request(string Uri, string Method, string Encoding,
        bool Authorization, bool Cookie);

    private sealed class Handler(Func<HttpRequestMessage, int, CancellationToken,
        Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        internal List<Request> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int number = Requests.Count;
            Requests.Add(new(request.RequestUri!.AbsoluteUri, request.Method.Method,
                string.Join(",", request.Headers.AcceptEncoding),
                request.Headers.Authorization is not null, request.Headers.Contains("Cookie")));
            return respond(request, number, cancellationToken);
        }
    }
}
