using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class GithubNpmObservationTests
{
    private const string Credential = "controlled-secret";
    private static readonly byte[] Original = [1, 2, 3];
    private const string Control = "{\"name\":\"hcoona-release-smoke-npm\","
        + "\"package_type\":\"npm\",\"visibility\":\"public\","
        + "\"owner\":{\"login\":\"hcoona\"},"
        + "\"repository\":{\"full_name\":\"hcoona/three\"}}";

    [TestMethod]
    [DataRow(false, "Exact")]
    [DataRow(true, "Conflict")]
    public async Task CompleteNativeIdentityComparesOriginalBytes(bool different,
        string expected)
    {
        using var handler = new Handler((request, index) => index switch
        {
            0 => Json(Control),
            1 => Json(Metadata("https://npm.pkg.github.com/archive")),
            _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(
                different ? [4, 5, 6] : Original) },
        });
        using var client = new HttpClient(handler);

        GithubNpmObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected),
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.AreEqual(Prepared().Subject, result.Observation.Subject);
        Assert.HasCount(3, handler.Requests);
        Assert.IsTrue(handler.Requests.All(request => request.Credential == Credential));
        Assert.DoesNotContain(Credential, JsonSerializer.Serialize(result));
    }

    [TestMethod]
    public async Task ValidContainerAndCompleteVersionMapEstablishAbsence()
    {
        using var handler = new Handler((_, index) => Json(index == 0 ? Control
            : "{\"name\":\"@hcoona/hcoona-release-smoke-npm\",\"versions\":{}}"));
        using var client = new HttpClient(handler);

        GithubNpmObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Absent,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.IsTrue(result.Observation.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    [DataRow(302, "Unknown")]
    [DataRow(404, "Unknown")]
    [DataRow(401, "Unknown")]
    [DataRow(429, "Unknown")]
    [DataRow(503, "Unknown")]
    public async Task MissingOrUnreadableContainerNeverPermitsVersionCreation(int status,
        string expected)
    {
        using var handler = new Handler((_, _) => new((HttpStatusCode)status)
            { Content = new ByteArrayContent([]) });
        using var client = new HttpClient(handler);

        GithubNpmObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected),
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(ReleaseCheckStatus.Missing,
            result.Observation.Checks.Single(check => check.Id == "native-package-control").Status);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("missing")]
    public async Task UserPackageRouteEstablishesNullableOwner(string shape)
    {
        string control = shape == "null" ? Control.Replace("{\"login\":\"hcoona\"}", "null",
            StringComparison.Ordinal) : Control.Replace("\"owner\":{\"login\":\"hcoona\"},",
                "", StringComparison.Ordinal);
        using var handler = new Handler((_, index) => index switch
        {
            0 => Json(control),
            1 => Json(Metadata("https://npm.pkg.github.com/archive")),
            _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Original) },
        });
        using var client = new HttpClient(handler);

        GithubNpmObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Exact,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(3, handler.Requests);
    }

    [TestMethod]
    [DataRow("repository")]
    [DataRow("visibility")]
    [DataRow("owner")]
    public async Task DifferentNativeControlStopsBeforeRegistryReads(string field)
    {
        string control = field switch
        {
            "repository" => Control.Replace("hcoona/three", "hcoona/unrelated",
                StringComparison.Ordinal),
            "visibility" => Control.Replace("public", "private", StringComparison.Ordinal),
            _ => Control.Replace("\"login\":\"hcoona\"", "\"login\":\"other\"",
                StringComparison.Ordinal),
        };
        using var handler = new Handler((_, _) => Json(control));
        using var client = new HttpClient(handler);

        GithubNpmObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Conflict,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow("missing-versions")]
    [DataRow("duplicate-version")]
    [DataRow("missing-package")]
    public async Task IncompleteOrAmbiguousMetadataCannotEstablishAbsence(string defect)
    {
        using var handler = new Handler((_, index) => index == 0 ? Json(Control)
            : defect == "missing-package" ? new(HttpStatusCode.NotFound)
                { Content = new ByteArrayContent([]) }
            : Json(defect == "missing-versions"
                ? "{\"name\":\"@hcoona/hcoona-release-smoke-npm\"}"
                : "{\"name\":\"@hcoona/hcoona-release-smoke-npm\","
                    + "\"versions\":{},\"versions\":{}}"));
        using var client = new HttpClient(handler);

        GithubNpmObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task OversizedCompleteResponseAndCancellationRemainUnknown()
    {
        using var handler = new Handler((_, _) => new(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[1_000_001]) });
        using var client = new HttpClient(handler);
        var reader = new GithubNpmObservation(client);

        GithubNpmObservationResult oversized = await reader.ObserveAsync(Prepared(), Credential,
            CancellationToken.None);
        GithubNpmObservationResult cancelled = await reader.ObserveAsync(Prepared(), Credential,
            new CancellationToken(true));

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(oversized.Observation.Files).State);
        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(cancelled.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
        Assert.Contains("Cancelled", Assert.ContainsSingle(cancelled.Reads).Detail);
    }

    [TestMethod]
    public async Task BodyDeadlinePreventsFurtherReads()
    {
        var clock = new ManualClock();
        using var handler = new Handler((_, _) => new(HttpStatusCode.OK)
            { Content = new StreamContent(new ExpiredBody(clock)) });
        using var client = new HttpClient(handler);

        GithubNpmObservationResult result = await new GithubNpmObservation(client, clock)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
        Assert.Contains("deadline", result.Reads[^1].Detail);
    }

    private sealed class ExpiredBody(ManualClock clock) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            clock.Advance(TimeSpan.FromSeconds(21));
            cancellationToken.ThrowIfCancellationRequested();
            throw new AssertFailedException("Body deadline did not propagate.");
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

    private static PreparedRelease Prepared() => new(new(new('a', 40),
        "refs/heads/development", "hcoona-release-smoke-npm", "product/release.yml", "buddy",
        "github-packages-npm", false),
        [new("build", "archive", "npm-tarball", GithubNpmObservation.Package, "1.2.3",
            "original/package.tgz", Original.Length,
            Convert.ToHexStringLower(SHA256.HashData(Original)))],
        ["consumer"], [new("consumer", ReleaseCheckStatus.Passed)]);

    private static string Metadata(string archive) => JsonSerializer.Serialize(new
    {
        name = GithubNpmObservation.Package,
        versions = new Dictionary<string, object>
        {
            ["1.2.3"] = new { name = GithubNpmObservation.Package, version = "1.2.3",
                dist = new { tarball = archive } },
        },
    });

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        internal List<(Uri Uri, string? Credential)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Requests.Count;
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.Parameter));
            return Task.FromResult(respond(request, index));
        }
    }
}
