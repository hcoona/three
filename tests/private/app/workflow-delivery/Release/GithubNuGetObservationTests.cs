using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class GithubNuGetObservationTests
{
    private const string Credential = "controlled-read-secret";
    private const string Base = "https://nuget.pkg.github.com/hcoona/download/";
    private const string Control = "{\"name\":\"Hcoona.ReleaseSmoke.GithubPackages\","
        + "\"package_type\":\"nuget\",\"visibility\":\"public\","
        + "\"repository\":{\"full_name\":\"hcoona/three\"}}";
    private const string Index = "{\"version\":\"3.0.0\",\"resources\":["
        + "{\"@type\":\"PackageBaseAddress/3.0.0\",\"@id\":\"" + Base + "\"},"
        + "{\"@type\":\"PackagePublish/2.0.0\","
        + "\"@id\":\"https://nuget.pkg.github.com/hcoona/\"}]}";

    [TestMethod]
    [DataRow(false, "Exact")]
    [DataRow(true, "Conflict")]
    public async Task CompleteNativeCoordinateComparesOriginalArchive(bool different,
        string expected)
    {
        byte[] original = Archive();
        using var handler = new Handler((_, step) => Response(step,
            different ? Archive(extra: true) : original));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(original), Credential, CancellationToken.None);

        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected),
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.AreEqual(Prepared(original).Subject, result.Observation.Subject);
        Assert.AreEqual(Base, result.Resources!.PackageBaseAddress);
        Assert.HasCount(5, handler.Requests);
        Assert.AreEqual("Bearer", handler.Requests[1].Scheme);
        Assert.AreEqual("Bearer", handler.Requests[2].Scheme);
        Assert.AreEqual("Basic", handler.Requests[0].Scheme);
        Assert.AreEqual("Basic", handler.Requests[3].Scheme);
        Assert.AreEqual("Basic", handler.Requests[4].Scheme);
        Assert.DoesNotContain(Credential, JsonSerializer.Serialize(result));
    }

    [TestMethod]
    public async Task DifferingMalformedArchiveIsConflictWithObservedBytes()
    {
        byte[] original = Archive();
        byte[] remote = Encoding.UTF8.GetBytes("not a NuGet archive");
        using var handler = new Handler((_, step) => Response(step, remote));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(original), Credential, CancellationToken.None);

        ReleaseFileObservation file = Assert.ContainsSingle(result.Observation.Files);
        Assert.AreEqual(ReleaseFileState.Conflict, file.State);
        Assert.AreEqual(remote.LongLength, file.Size);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(remote)), file.Sha256);
        Assert.HasCount(5, handler.Requests);
    }

    [TestMethod]
    [DataRow("identity", "Exact")]
    [DataRow("Identity", "Exact")]
    [DataRow("iDeNtItY", "Exact")]
    [DataRow("gzip", "Unknown")]
    public async Task ContentEncodingUsesSupportedTokenSemantics(string encoding,
        string expected)
    {
        byte[] original = Archive();
        using var handler = new Handler((_, step) =>
        {
            HttpResponseMessage response = Response(step, original);
            response.Content.Headers.ContentEncoding.Add(encoding);
            return response;
        });
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(original), Credential, CancellationToken.None);

        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected),
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(expected == "Exact" ? 5 : 1, handler.Requests);
    }

    [TestMethod]
    public async Task CompleteNativeAbsenceRequiresEstablishedPackageControl()
    {
        using var handler = new Handler((_, step) => step switch
        {
            0 => Json(Index),
            1 => Json(Control),
            2 => Json("[]"),
            _ => Json("{\"versions\":[]}"),
        });
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Absent, result.Observation.Files[0].State);
        Assert.IsTrue(result.Observation.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
        Assert.HasCount(4, handler.Requests);
    }

    [TestMethod]
    [DataRow("private")]
    [DataRow("unrelated")]
    [DataRow("npm")]
    public async Task ConflictingPackageControlStopsBeforeVersionReads(string change)
    {
        string control = change switch
        {
            "private" => Control.Replace("public", "private", StringComparison.Ordinal),
            "npm" => Control.Replace("nuget", "npm", StringComparison.Ordinal),
            _ => Control.Replace("hcoona/three", "hcoona/unrelated", StringComparison.Ordinal),
        };
        using var handler = new Handler((_, step) => Json(step == 0 ? Index : control));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Conflict, result.Observation.Files[0].State);
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task InventoryDisagreementLeavesStateUnknown()
    {
        using var handler = new Handler((_, step) => step == 3
            ? Json("{\"versions\":[]}") : Response(step, Archive()));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(4, handler.Requests);
    }

    [TestMethod]
    public async Task OneOriginalArchiveRedirectStripsReadCredentials()
    {
        byte[] original = Archive();
        using var handler = new Handler((_, step) => step == 4
            ? Redirect("https://objects.githubusercontent.com/package?signature=private")
            : Response(step, original));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(original), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Exact, result.Observation.Files[0].State);
        Assert.HasCount(6, handler.Requests);
        Assert.IsNull(handler.Requests[^1].Scheme);
        Assert.AreEqual("https://objects.githubusercontent.com", result.Reads[^1].StorageOrigin);
        Assert.IsNotNull(result.Reads[^1].LocationSha256);
        Assert.DoesNotContain("signature", JsonSerializer.Serialize(result));
        Assert.DoesNotContain(Credential, JsonSerializer.Serialize(result));
    }

    [TestMethod]
    [DataRow("raw")]
    [DataRow("encoded")]
    [DataRow("basic")]
    [DataRow("encoded-basic")]
    [DataRow("host")]
    public async Task ReflectedCredentialStopsBeforeStorageSendOrDiagnosticOrigin(string form)
    {
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + Credential));
        string value = form switch
        {
            "encoded" => "%63" + Credential[1..],
            "basic" => basic,
            "encoded-basic" => "%" + ((int)basic[0]).ToString("X2") + basic[1..],
            _ => Credential,
        };
        string location = form == "host" ? "https://" + value + ".example.com/package"
            : "https://objects.githubusercontent.com/package?credential=" + value;
        using var handler = new Handler((_, step) => step == 4 ? Redirect(location)
            : Response(step, Archive()));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(5, handler.Requests);
        Assert.IsTrue(result.Reads.All(read => read.StorageOrigin is null));
        Assert.IsTrue(result.Reads.All(read => read.LocationSha256 is null));
        string diagnostics = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Credential, diagnostics);
        Assert.DoesNotContain(basic, diagnostics);
        Assert.DoesNotContain(location, diagnostics);
    }

    [TestMethod]
    [DataRow("http://objects.githubusercontent.com/package")]
    [DataRow("https://127.0.0.1/package")]
    [DataRow("https://objects.githubusercontent.com:444/package")]
    [DataRow("https://user@objects.githubusercontent.com/package")]
    [DataRow("https://objects.githubusercontent.com/package#fragment")]
    public async Task UnsupportedStorageLocationNeverReceivesCredential(string location)
    {
        using var handler = new Handler((_, step) => step == 4 ? Redirect(location)
            : Response(step, Archive()));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(5, handler.Requests);
    }

    [TestMethod]
    public async Task ASecondRedirectStopsWithoutAnotherStorageSend()
    {
        using var handler = new Handler((_, step) => step >= 4
            ? Redirect("https://objects.githubusercontent.com/next") : Response(step, Archive()));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(6, handler.Requests);
        Assert.IsNull(handler.Requests[^1].Scheme);
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(404)]
    [DataRow(429)]
    [DataRow(503)]
    public async Task UnavailableNativeMetadataCannotAuthorizeCreation(int status)
    {
        using var handler = new Handler((_, _) => new((HttpStatusCode)status)
        { Content = new ByteArrayContent([]) });
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task ParentCancellationIsVisibleWithoutARead()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var handler = new Handler((_, _) => throw new AssertFailedException());
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, cancellation.Token);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.IsEmpty(handler.Requests);
        Assert.AreEqual("Cancelled.", result.Reads[^1].Detail);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("different-owner")]
    [DataRow("multiple-endpoints")]
    [DataRow("missing")]
    public async Task InvalidNativeServiceResourcesStopBeforePackageReads(string shape)
    {
        string index = shape switch
        {
            "duplicate" => Index.Replace("\"version\":\"3.0.0\"",
                "\"version\":\"3.0.0\",\"version\":\"3.0.0\"", StringComparison.Ordinal),
            "different-owner" => Index.Replace("/hcoona/", "/unrelated/",
                StringComparison.Ordinal),
            "multiple-endpoints" => Index.Replace("\"resources\":[",
                "\"resources\":[{\"@type\":\"PackageBaseAddress/3.0.0\","
                + "\"@id\":\"https://nuget.pkg.github.com/hcoona/other/\"},",
                StringComparison.Ordinal),
            _ => "{\"version\":\"3.0.0\",\"resources\":[]}",
        };
        using var handler = new Handler((_, _) => Json(index));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow("duplicate-id")]
    [DataRow("duplicate-version")]
    [DataRow("missing-id")]
    public async Task InvalidSupportingInventoryCannotEstablishAbsence(string shape)
    {
        string versions = shape switch
        {
            "duplicate-id" => "[{\"id\":1,\"name\":\"1.0\"},"
                + "{\"id\":1,\"name\":\"2.0\"}]",
            "duplicate-version" => "[{\"id\":1,\"name\":\"1.0\"},"
                + "{\"id\":2,\"name\":\"1.0.0\"}]",
            _ => "[{\"name\":\"1.0\"}]",
        };
        using var handler = new Handler((_, step) => step == 2 ? Json(versions)
            : Response(step, Archive()));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(3, handler.Requests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IncompleteOrOversizedResponseCannotEstablishNativeState(bool oversized)
    {
        using var handler = new Handler((_, _) =>
        {
            HttpResponseMessage response = Json(Index);
            response.Content.Headers.ContentLength = oversized ? 3 * 1024 * 1024 : 1;
            return response;
        });
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    public async Task FullSupportingPagesStopAtTheTotalReadAllowance()
    {
        using var handler = new Handler((_, step) => step < 2 ? Response(step, Archive())
            : Json("[" + string.Join(',', Enumerable.Range(0, 100).Select(offset =>
                "{\"id\":" + (step * 100 + offset) + ",\"name\":\"1.0."
                + (step * 100 + offset) + "\"}")) + "]"));
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(80, handler.Requests);
        Assert.IsTrue(handler.Requests.All(request => request.Uri.Host is
            "nuget.pkg.github.com" or "api.github.com"));
    }

    [TestMethod]
    public async Task FailedPreparationMakesNoNativeRead()
    {
        PreparedRelease prepared = Prepared(Archive()) with
        { Checks = [new("contents", ReleaseCheckStatus.Failed)] };
        using var handler = new Handler((_, _) => throw new AssertFailedException());
        using var client = new HttpClient(handler);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new GithubNuGetObservation(client).ObserveAsync(prepared, Credential,
                CancellationToken.None));
        Assert.IsEmpty(handler.Requests);
    }

    [TestMethod]
    public async Task BodyConsumptionSharesThePerReadDeadline()
    {
        var clock = new ManualClock();
        using var handler = new Handler((_, _) => new(HttpStatusCode.OK)
        { Content = new StreamContent(new ExpiredBody(clock)) });
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client, clock)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
        Assert.Contains("deadline", result.Reads[^1].Detail);
    }

    [TestMethod]
    public async Task CompleteIndividualReadsStillShareTheObservationDeadline()
    {
        var clock = new ManualClock();
        using var handler = new Handler((_, step) =>
        {
            clock.Advance(TimeSpan.FromSeconds(24));
            return Response(step, Archive());
        });
        using var client = new HttpClient(handler);
        GithubNuGetObservationResult result = await new GithubNuGetObservation(client, clock)
            .ObserveAsync(Prepared(Archive()), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(4, handler.Requests);
        Assert.Contains("deadline", result.Reads[^1].Detail);
    }

    private sealed class ExpiredBody(ManualClock clock) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            clock.Advance(TimeSpan.FromSeconds(31));
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
                if (due is { } value && value <= now) { due = null; callback(state); }
            }
            public void Dispose() => due = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static HttpResponseMessage Response(int step, byte[] archive) => step switch
    {
        0 => Json(Index),
        1 => Json(Control),
        2 => Json("[{\"id\":123,\"name\":\"1.0\"}]"),
        3 => Json("{\"versions\":[\"1.0.0\"]}"),
        _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) },
    };
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found)
        { Content = new ByteArrayContent([]) };
        response.Headers.Location = new(location);
        return response;
    }
    private static PreparedRelease Prepared(byte[] original) => new(new(new('a', 40),
        "refs/heads/development", "hcoona-release-smoke-github-packages",
        "src/public/lib/hcoona-release-smoke-github-packages/workflow-delivery.release-unit.yml",
        "buddy", "github-packages-nuget", false), [new("build", "package", "nuget-package",
        GithubNuGetObservation.Package, "1.0.0", "original/package.nupkg", original.Length,
        Convert.ToHexStringLower(SHA256.HashData(original)))], ["contents"],
        [new("contents", ReleaseCheckStatus.Passed)]);
    private static byte[] Archive(bool extra = false)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("package.nuspec").Open()))
                writer.Write("<package><metadata><id>Hcoona.ReleaseSmoke.GithubPackages</id>"
                    + "<version>1.0.0</version><authors>hcoona</authors>"
                    + "<description>Controlled native archive</description></metadata></package>");
            if (extra) zip.CreateEntry("extra.txt");
        }
        return bytes.ToArray();
    }
    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        internal List<(Uri Uri, string? Scheme)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.Scheme));
            return Task.FromResult(response(request, Requests.Count - 1));
        }
    }
}
