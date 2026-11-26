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
        using var handler = new ScriptedHttpHandler((_, number) => number == 0
            ? Response(200, Metadata()) : Response(200, original));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(prepared,
            context.CancellationToken);

        ReleaseFileObservation file = Assert.ContainsSingle(result.Observation.Files);
        Assert.AreEqual(ReleaseFileState.Exact, file.State);
        Assert.AreEqual(prepared.Files[0].Sha256, file.Sha256);
        Assert.AreEqual(prepared.Files[0].Size, file.Size);
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual("https://registry.npmjs.org/@example%2Fpackage/1.2.3",
            handler.Requests[0].Uri.AbsoluteUri);
        Assert.AreEqual("https://registry.npmjs.org/original.tgz",
            handler.Requests[1].Uri.AbsoluteUri);
        Assert.IsTrue(handler.Requests.All(request =>
            request.Headers.Authorization is null && !request.Headers.Contains("Cookie")));
        Assert.AreEqual(ReleaseCheckStatus.Passed,
            Assert.ContainsSingle(result.Observation.Checks).Status);
    }

    [TestMethod]
    [DataRow(404, "Absent")]
    [DataRow(503, "Unknown")]
    [DataRow(403, "Unknown")]
    [DataRow(302, "Unknown")]
    public async Task MetadataStatusDefinesReadOnlyStateWithoutTarballRequest(int status,
        string state)
    {
        using var handler = new ScriptedHttpHandler((_, _) => Response(status, ""));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(Enum.Parse<ReleaseFileState>(state),
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
    }

    [TestMethod]
    [DataRow(404)]
    [DataRow(502)]
    public async Task MissingTarballCannotEstablishCoordinateAbsence(int status)
    {
        using var handler = new ScriptedHttpHandler((_, number) => number == 0
            ? Response(200, Metadata()) : Response(status, ""));
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
        using var handler = new ScriptedHttpHandler((_, number) => number == 0
            ? Response(200, Metadata()) : Response(200, new byte[] { 1, 2, 4 }));
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
    [DataRow("@example/package", "other")]
    [DataRow("1.2.3", "9.9.9")]
    [DataRow("registry.npmjs.org", "example.org")]
    public async Task UnsupportedMetadataStopsBeforeFurtherRequests(string original,
        string replacement)
    {
        string metadata = Metadata().Replace(original, replacement, StringComparison.Ordinal);
        using var handler = new ScriptedHttpHandler((_, _) => Response(200, metadata));
        using var client = new HttpClient(handler);

        NpmjsObservationResult result = await new NpmjsObservation(client).ObserveAsync(
            Prepare([1, 2, 3]), context.CancellationToken);

        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.HasCount(1, handler.Requests);
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

}
