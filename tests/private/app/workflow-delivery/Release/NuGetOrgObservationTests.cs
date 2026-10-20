using System.Net;
using System.Text;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetOrgObservationTests(TestContext context)
{
    private const string Unit = "phi-failure-detector";
    private const string Package = "PhiFailureDetector";
    private const string Root = NuGetOrgObservation.PackageBaseAddress + "phifailuredetector/";
    private const string Index = "{\"version\":\"3.0.0\",\"resources\":["
        + "{\"@type\":\"PackageBaseAddress/3.0.0\",\"@id\":\""
        + NuGetOrgObservation.PackageBaseAddress + "\"},"
        + "{\"@type\":\"PackagePublish/2.0.0\","
        + "\"@id\":\"https://www.nuget.org/api/v2/package\"}]}";

    // nuget.org repository-signs every accepted package and its versions are immutable, so
    // WD-REL-009 completes on the exact normalized version and never reads the archive.
    [TestMethod]
    [DataRow("listed", "Available")]
    [DataRow("listed-unnormalized", "Available")]
    [DataRow("not-listed", "Absent")]
    [DataRow("no-versions", "Absent")]
    [DataRow("duplicate", "Unknown")]
    [DataRow("malformed", "Unknown")]
    [DataRow("index-error", "Unknown")]
    [DataRow("other-base", "Unknown")]
    [DataRow("other-publish", "Unknown")]
    public async Task ObservationReadsOnlyTheFlatContainerVersionList(string served,
        string expected)
    {
        var handler = new Handler(request => request.RequestUri!.AbsoluteUri switch
        {
            NuGetOrgObservation.ServiceIndex => Json(served switch
            {
                "other-publish" => Index.Replace("api/v2/package", "api/v3/package"),
                "other-base" => Index.Replace("v3-flatcontainer/", "v3-other/"),
                _ => Index,
            }),
            Root + "index.json" => served switch
            {
                "no-versions" => new(HttpStatusCode.NotFound)
                    { Content = new ByteArrayContent([]) },
                "index-error" => new(HttpStatusCode.InternalServerError)
                    { Content = new ByteArrayContent([]) },
                "not-listed" => Json("{\"versions\":[\"0.9.0\",\"1.0.0-beta.1\"]}"),
                "listed-unnormalized" => Json("{\"versions\":[\"0.9.0\",\"1.0.0.0\"]}"),
                "duplicate" => Json("{\"versions\":[\"1.0.0\",\"1.0.0\"]}"),
                "malformed" => Json("{\"versions\":\"1.0.0\"}"),
                _ => Json("{\"versions\":[\"0.9.0\",\"1.0.0\"]}"),
            },
            var other => throw new AssertFailedException("Unexpected read: " + other),
        });
        using var client = new HttpClient(handler);

        NuGetObservationResult result = await new NuGetOrgObservation(client).ObserveAsync(
            Prepared(), "", context.CancellationToken);

        ReleaseFileObservation file = Assert.ContainsSingle(result.Observation.Files);
        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected), file.State);
        Assert.IsNull(file.Size);
        Assert.IsNull(file.Sha256);
        Assert.IsTrue(handler.Requests.All(request => request.Headers.Authorization is null &&
            !request.RequestUri!.AbsolutePath.EndsWith(".nupkg", StringComparison.Ordinal)));
        if (expected == "Available")
            Assert.AreEqual(NuGetOrgObservation.PackageBaseAddress,
                result.Resources!.PackageBaseAddress);
        Assert.AreEqual(expected is "Available" or "Absent" ? ReleaseCheckStatus.Passed
            : ReleaseCheckStatus.Failed, Assert.ContainsSingle(result.Observation.Checks).Status);
    }

    [TestMethod]
    [DataRow("credential")]
    [DataRow("buddy")]
    [DataRow("package")]
    public async Task ObservationRejectsUnsupportedRequestsBeforeReading(string change)
    {
        PreparedRelease prepared = Prepared();
        if (change == "buddy")
            prepared = prepared with { Subject = prepared.Subject with
                { Channel = "buddy", Destination = "github-packages-nuget" } };
        if (change == "package")
            prepared = prepared with { Files = [prepared.Files[0] with { Name = "Other" }] };
        var handler = new Handler(_ => throw new AssertFailedException("Unexpected read."));
        using var client = new HttpClient(handler);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new NuGetOrgObservation(client).ObserveAsync(prepared,
                change == "credential" ? "secret" : "", context.CancellationToken));
        Assert.IsEmpty(handler.Requests);
    }

    private static PreparedRelease Prepared() => new(
        new(new string('a', 40), "refs/heads/main", Unit,
            "src/public/lib/PhiFailureDetector/workflow-delivery.release-unit.yml", "official",
            "nugetorg", false),
        [new("nuget-package", "nuget-package", "nuget-package", Package, "1.0.0",
            "PhiFailureDetector.1.0.0.nupkg", 1, new string('b', 64))],
        [], []);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> operation)
        : HttpMessageHandler
    {
        internal List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellation)
        {
            Requests.Add(request);
            return Task.FromResult(operation(request));
        }
    }
}
