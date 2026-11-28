using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
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
        using var handler = new ScriptedHttpHandler((_, index) => index switch
        {
            0 => Json(Control),
            1 => Json(Metadata("https://npm.pkg.github.com/archive?token=transient-location")),
            _ => new(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(
                different ? [4, 5, 6] : Original)
            },
        });
        using var client = new HttpClient(handler);

        ReleaseObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected),
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.AreEqual(Prepared().Subject, result.Observation.Subject);
        Assert.HasCount(3, handler.Requests);
        Assert.IsTrue(handler.Requests.All(request =>
            request.Headers.Authorization?.Parameter == Credential));
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(
            "https://npm.pkg.github.com/archive?token=transient-location"u8)),
            result.Reads.Single(read => read.Stage == "tarball-location").UrlSha256);
        Assert.DoesNotContain("transient-location", JsonSerializer.Serialize(result));
        Assert.DoesNotContain(Credential, JsonSerializer.Serialize(result));
    }

    [TestMethod]
    [DataRow("objects.githubusercontent.com", true)]
    [DataRow("unrelated.example", false)]
    public async Task ArchiveHostControlsWhetherAndHowItCanBeRead(string host, bool allowed)
    {
        using var handler = new ScriptedHttpHandler((_, index) => index switch
        {
            0 => Json(Control),
            1 => Json(Metadata($"https://{host}/archive")),
            _ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Original) },
        });
        using var client = new HttpClient(handler);

        ReleaseObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(allowed ? ReleaseFileState.Exact : ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        if (allowed)
        {
            Assert.AreEqual(host, handler.Requests[^1].Uri.Host);
            Assert.IsNull(handler.Requests[^1].Headers.Authorization);
        }
        else Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task ValidContainerAndCompleteVersionMapEstablishAbsence()
    {
        using var handler = new ScriptedHttpHandler((_, index) => Json(index == 0 ? Control
            : "{\"name\":\"@hcoona/hcoona-release-smoke-npm\",\"versions\":{}}"));
        using var client = new HttpClient(handler);

        ReleaseObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Absent,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.IsTrue(result.Observation.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    [DataRow(404)]
    [DataRow(401)]
    [DataRow(503)]
    public async Task MissingOrUnreadableContainerNeverPermitsVersionCreation(int status)
    {
        using var handler = new ScriptedHttpHandler((_, _) => new((HttpStatusCode)status)
        { Content = new ByteArrayContent([]) });
        using var client = new HttpClient(handler);

        ReleaseObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
        Assert.AreEqual(ReleaseCheckStatus.Missing,
            result.Observation.Checks.Single(check => check.Id == "native-package-control").Status);
    }

    [TestMethod]
    [DataRow("\"hcoona/unrelated\"", "Conflict")]
    [DataRow("null", "Unknown")]
    public async Task NonExactNativeControlStopsBeforeRegistryReads(string repository,
        string expected)
    {
        string control = Control.Replace("\"hcoona/three\"", repository,
            StringComparison.Ordinal);
        using var handler = new ScriptedHttpHandler((_, _) => Json(control));
        using var client = new HttpClient(handler);

        ReleaseObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(expected, Assert.ContainsSingle(result.Observation.Files).State.ToString());
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow(200, """{"name":"@hcoona/hcoona-release-smoke-npm"}""")]
    [DataRow(404, "")]
    public async Task IncompleteRegistryMetadataCannotEstablishAbsence(int status, string body)
    {
        using var handler = new ScriptedHttpHandler((_, index) => index == 0 ? Json(Control)
            : status == 200 ? Json(body)
            : new((HttpStatusCode)status) { Content = new ByteArrayContent([]) });
        using var client = new HttpClient(handler);

        ReleaseObservationResult result = await new GithubNpmObservation(client)
            .ObserveAsync(Prepared(), Credential, CancellationToken.None);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(2, handler.Requests);
    }

    private static PackageDestination Prepared() => new(new(new('a', 40),
        "refs/heads/development", "hcoona-release-smoke-npm", "product/release.yml", "buddy",
        "github-packages-npm", false),
        [new("build", "archive", "npm-tarball", AdoptedNpmPackage.PackageName, "1.2.3",
            "original/package.tgz", Original.Length,
            Convert.ToHexStringLower(SHA256.HashData(Original)))],
        AdoptedNpmPackage.PackageName);

    private static string Metadata(string archive) => JsonSerializer.Serialize(new
    {
        name = AdoptedNpmPackage.PackageName,
        versions = new Dictionary<string, object>
        {
            ["1.2.3"] = new
            {
                name = AdoptedNpmPackage.PackageName,
                version = "1.2.3",
                dist = new { tarball = archive }
            },
        },
    });

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK)
    { Content = new StringContent(value, Encoding.UTF8, "application/json") };

}
