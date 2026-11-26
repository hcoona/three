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
        + "\"@id\":\"https://nuget.pkg.github.com/hcoona\"}]}";

    [TestMethod]
    [DataRow(false, "Exact")]
    [DataRow(true, "Conflict")]
    public async Task CompleteNativeCoordinateComparesOriginalArchive(bool different,
        string expected)
    {
        byte[] original = Archive();
        using var handler = new ScriptedHttpHandler((_, step) => Response(step,
            different ? Archive(extra: true) : original));
        NuGetObservationResult result = await ObserveAsync(handler, original);

        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected),
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.AreEqual(Prepared(original).Subject, result.Observation.Subject);
        Assert.AreEqual(Base, result.Resources!.PackageBaseAddress);
        Assert.HasCount(5, handler.Requests);
        Assert.IsTrue(handler.Requests.All(request => request.Headers.Authorization?.Scheme ==
            (request.Uri.Host == GithubApi.Host ? "Bearer" : "Basic")));
        Assert.DoesNotContain(Credential, JsonSerializer.Serialize(result));
    }

    [TestMethod]
    public async Task DifferingMalformedArchiveIsConflictWithObservedBytes()
    {
        byte[] original = Archive();
        byte[] remote = Encoding.UTF8.GetBytes("not a NuGet archive");
        using var handler = new ScriptedHttpHandler((_, step) => Response(step, remote));
        NuGetObservationResult result = await ObserveAsync(handler, original);

        ReleaseFileObservation file = Assert.ContainsSingle(result.Observation.Files);
        Assert.AreEqual(ReleaseFileState.Conflict, file.State);
        Assert.AreEqual(remote.LongLength, file.Size);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(remote)), file.Sha256);
        Assert.HasCount(5, handler.Requests);
    }

    [TestMethod]
    public async Task CompleteNativeAbsenceRequiresEstablishedPackageControl()
    {
        using var handler = new ScriptedHttpHandler((_, step) => step switch
        {
            0 => Json(Index),
            1 => Json(Control),
            2 => Json("[]"),
            _ => Json("{\"versions\":[]}"),
        });
        NuGetObservationResult result = await ObserveAsync(handler);

        Assert.AreEqual(ReleaseFileState.Absent, result.Observation.Files[0].State);
        Assert.IsTrue(result.Observation.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
        Assert.HasCount(4, handler.Requests);
    }

    [TestMethod]
    [DataRow("\"npm\"", "Conflict")]
    [DataRow("null", "Unknown")]
    public async Task NonExactPackageControlStopsBeforeVersionReads(string replacement,
        string state)
    {
        string control = Control.Replace("\"nuget\"", replacement, StringComparison.Ordinal);
        using var handler = new ScriptedHttpHandler((_, step) => Json(step == 0 ? Index : control));
        NuGetObservationResult result = await ObserveAsync(handler);

        Assert.AreEqual(state, result.Observation.Files[0].State.ToString());
        Assert.HasCount(2, handler.Requests);
    }

    [TestMethod]
    public async Task InventoryDisagreementLeavesStateUnknown()
    {
        using var handler = new ScriptedHttpHandler((_, step) => step == 3
            ? Json("{\"versions\":[]}") : Response(step, Archive()));
        NuGetObservationResult result = await ObserveAsync(handler);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(4, handler.Requests);
    }

    [TestMethod]
    public async Task UnavailableLaterVersionPageCannotEstablishAbsence()
    {
        string firstPage = JsonSerializer.Serialize(Enumerable.Range(2, 100)
            .Select(id => new { id, name = $"{id}.0" }));
        using var handler = new ScriptedHttpHandler((_, step) => step switch
        {
            0 => Json(Index),
            1 => Json(Control),
            2 => Json(firstPage),
            3 => new(HttpStatusCode.ServiceUnavailable),
            _ => throw new AssertFailedException("Unexpected inventory request."),
        });

        NuGetObservationResult result = await ObserveAsync(handler);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(4, handler.Requests);
    }

    [TestMethod]
    [DataRow(302)]
    [DataRow(401)]
    [DataRow(404)]
    [DataRow(503)]
    public async Task UnavailableNativeMetadataCannotAuthorizeCreation(int status)
    {
        using var handler = new ScriptedHttpHandler((_, _) => new((HttpStatusCode)status)
        { Content = new ByteArrayContent([]) });
        NuGetObservationResult result = await ObserveAsync(handler);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow("/hcoona/", "/unrelated/")]
    [DataRow("\"resources\":[", "\"resources\":[{\"@type\":\"PackageBaseAddress/3.0.0\","
        + "\"@id\":\"https://nuget.pkg.github.com/hcoona/other/\"},")]
    [DataRow(Index, """{"version":"3.0.0","resources":[]}""")]
    [DataRow("\"resources\":", "\"version\":\"3.0.0\",\"resources\":")]
    public async Task InvalidNativeServiceResourcesStopBeforePackageReads(string original,
        string replacement)
    {
        string index = Index.Replace(original, replacement, StringComparison.Ordinal);
        using var handler = new ScriptedHttpHandler((_, _) => Json(index));
        NuGetObservationResult result = await ObserveAsync(handler);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow("""[{"id":1,"name":"1.0"},{"id":1,"name":"2.0"}]""")]
    [DataRow("""[{"id":1,"name":"1.0"},{"id":2,"name":"1.0.0"}]""")]
    [DataRow("""[{"id":0,"name":"1.0"}]""")]
    [DataRow("""[{"name":"1.0"}]""")]
    public async Task InvalidSupportingInventoryCannotEstablishAbsence(string versions)
    {
        using var handler = new ScriptedHttpHandler((_, step) => step == 2 ? Json(versions)
            : Response(step, Archive()));
        NuGetObservationResult result = await ObserveAsync(handler);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(3, handler.Requests);
    }

    [TestMethod]
    public async Task FullSupportingPagesStopAtTheTotalReadAllowance()
    {
        using var handler = new ScriptedHttpHandler((_, step) =>
            step < 2 ? Response(step, Archive())
            : Json("[" + string.Join(',', Enumerable.Range(0, 100).Select(offset =>
                "{\"id\":" + (step * 100 + offset) + ",\"name\":\"1.0."
                + (step * 100 + offset) + "\"}")) + "]"));
        NuGetObservationResult result = await ObserveAsync(handler);

        Assert.AreEqual(ReleaseFileState.Unknown, result.Observation.Files[0].State);
        Assert.HasCount(80, handler.Requests);
        Assert.IsTrue(handler.Requests.All(request => request.Uri.Host is
            "nuget.pkg.github.com" or "api.github.com"));
    }

    private static async Task<NuGetObservationResult> ObserveAsync(ScriptedHttpHandler handler,
        byte[]? original = null)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        return await new GithubNuGetObservation(client)
            .ObserveAsync(Prepared(original ?? Archive()), Credential, CancellationToken.None);
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
    private static PreparedRelease Prepared(byte[] original) => new(new(new('a', 40),
        "refs/heads/development", "hcoona-release-smoke-github-packages",
        "tests/private/app/workflow-delivery/" +
            "fixtures/products/hcoona-release-smoke-github-packages/" +
            "workflow-delivery.release-unit.yml",
        "buddy", "github-packages-nuget", false), [new("build", "package", "nuget-package",
        "Hcoona.ReleaseSmoke.GithubPackages", "1.0.0", "original/package.nupkg", original.Length,
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
}
