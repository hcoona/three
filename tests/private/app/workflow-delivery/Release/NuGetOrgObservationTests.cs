using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGet.Versioning;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;
using HashAlgorithmName = NuGet.Common.HashAlgorithmName;

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

    // nuget.org repository-signs every accepted package. Removing only that signature must
    // give back the exact uploaded original, so WD-REL-009 compares the original bytes.
    [TestMethod]
    public async Task RepositorySignatureRemovalRestoresTheExactOriginal()
    {
        byte[] original = Archive("1.0.0");
        byte[] served = await RepositorySignAsync(original);
        CollectionAssert.AreNotEqual(original, served);

        (long size, string digest) = await NuGetOrgObservation.OriginalAsync(served,
            context.CancellationToken);

        Assert.AreEqual(original.LongLength, size);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(original)), digest);
    }

    [TestMethod]
    public async Task UnsignedArchiveIsItsOwnOriginal()
    {
        byte[] original = Archive("1.0.0");
        (long size, string digest) = await NuGetOrgObservation.OriginalAsync(original,
            context.CancellationToken);
        Assert.AreEqual(original.LongLength, size);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(original)), digest);
    }

    [TestMethod]
    [DataRow("signed", "Exact")]
    [DataRow("unsigned", "Exact")]
    [DataRow("different", "Conflict")]
    [DataRow("other-identity", "Conflict")]
    [DataRow("not-listed", "Absent")]
    [DataRow("no-versions", "Absent")]
    [DataRow("other-publish", "Unknown")]
    [DataRow("archive-error", "Unknown")]
    public async Task ObservationReadsTheFlatContainerOriginal(string served, string expected)
    {
        byte[] original = Archive("1.0.0");
        byte[] archive = served switch
        {
            "signed" => await RepositorySignAsync(original),
            "different" => Archive("1.0.0", "different"),
            "other-identity" => Archive("1.0.1"),
            _ => original,
        };
        var handler = new Handler(request => request.RequestUri!.AbsoluteUri switch
        {
            NuGetOrgObservation.ServiceIndex => Json(served == "other-publish"
                ? Index.Replace("api/v2/package", "api/v3/package") : Index),
            Root + "index.json" => served == "no-versions"
                ? new(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) }
                : Json(served == "not-listed" ? "{\"versions\":[\"0.9.0\"]}"
                    : "{\"versions\":[\"0.9.0\",\"1.0.0\"]}"),
            Root + "1.0.0/phifailuredetector.1.0.0.nupkg" => served == "archive-error"
                ? new(HttpStatusCode.InternalServerError) { Content = new ByteArrayContent([]) }
                : new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) },
            var other => throw new AssertFailedException("Unexpected read: " + other),
        });
        using var client = new HttpClient(handler);

        NuGetObservationResult result = await new NuGetOrgObservation(client).ObserveAsync(
            Prepared(original), "", context.CancellationToken);

        ReleaseFileObservation file = Assert.ContainsSingle(result.Observation.Files);
        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected), file.State);
        Assert.IsTrue(handler.Requests.All(request => request.Headers.Authorization is null));
        if (expected == "Exact")
        {
            Assert.AreEqual(original.LongLength, file.Size);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(original)), file.Sha256);
            Assert.AreEqual(NuGetOrgObservation.PackageBaseAddress,
                result.Resources!.PackageBaseAddress);
        }
        ReleaseObservation observation = result.Observation;
        Assert.AreEqual(expected is "Exact" or "Absent" ? ReleaseCheckStatus.Passed
            : ReleaseCheckStatus.Failed, Assert.ContainsSingle(observation.Checks).Status);
    }

    [TestMethod]
    [DataRow("credential")]
    [DataRow("buddy")]
    [DataRow("package")]
    public async Task ObservationRejectsUnsupportedRequestsBeforeReading(string change)
    {
        byte[] original = Archive("1.0.0");
        PreparedRelease prepared = Prepared(original);
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

    private static PreparedRelease Prepared(byte[] original) => new(
        new(new string('a', 40), "refs/heads/main", Unit,
            "src/public/lib/PhiFailureDetector/workflow-delivery.release-unit.yml", "official",
            "nugetorg", false),
        [new("nuget-package", "nuget-package", "nuget-package", Package, "1.0.0",
            "PhiFailureDetector.1.0.0.nupkg", original.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(original)))],
        [], []);

    private static byte[] Archive(string version, string description = "Controlled original")
    {
        var builder = new PackageBuilder
        {
            Id = Package, Version = NuGetVersion.Parse(version), Description = description,
        };
        builder.Authors.Add("hcoona");
        builder.Files.Add(new PhysicalPackageFile(new MemoryStream(
            Encoding.UTF8.GetBytes("controlled")))
            { TargetPath = "lib/netstandard2.0/_._" });
        using var output = new MemoryStream();
        builder.Save(output);
        return output.ToArray();
    }

    private async Task<byte[]> RepositorySignAsync(byte[] original)
    {
        using RSA key = RSA.Create(3072);
        var request = new CertificateRequest("CN=Controlled Repository", key,
            System.Security.Cryptography.HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.3")], false));
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        string scratch = Directory.CreateTempSubdirectory("nuget-repository-sign-").FullName;
        try
        {
            string input = Path.Combine(scratch, "original.nupkg");
            string output = Path.Combine(scratch, "served.nupkg");
            await File.WriteAllBytesAsync(input, original, context.CancellationToken);
            using var signRequest = new RepositorySignPackageRequest(certificate,
                HashAlgorithmName.SHA256, HashAlgorithmName.SHA256,
                new Uri(NuGetOrgObservation.ServiceIndex), null);
            using (SigningOptions options = SigningOptions.CreateFromFilePaths(input, output,
                overwrite: true, new X509SignatureProvider(timestampProvider: null),
                NullLogger.Instance))
                await SigningUtility.SignAsync(options, signRequest, context.CancellationToken);
            return await File.ReadAllBytesAsync(output, context.CancellationToken);
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

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
