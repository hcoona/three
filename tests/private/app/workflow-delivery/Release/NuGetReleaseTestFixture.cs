using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

// Controlled transfer evidence; no SDK execution or external service is needed.
internal sealed class NuGetReleaseTestFixture : IDisposable
{
    internal string Root { get; } = Directory.CreateTempSubdirectory("nuget-caller-tests-")
        .FullName;
    internal string Archive => Path.Combine(Root, "original.nupkg");
    internal ReleasePreparationResult Preparation { get; }
    internal PreparedRelease Prepared => Preparation.Prepared;

    internal NuGetReleaseTestFixture(bool dryRun = false, bool official = false)
    {
        NuGetReleaseUnit unit = ReleaseUnitCatalog.NuGet(official ? "phi-failure-detector"
            : "hcoona-release-smoke-github-packages");
        // Matches the controlled archive used by NuGetPublisherTests.
        using (var zip = new ZipArchive(File.Create(Archive), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("package.nuspec").Open()))
            writer.Write($"<package><metadata><id>{unit.PackageId}</id>"
                + "<version>1.0.0</version><authors>hcoona</authors>"
                + "<description>Controlled native archive</description></metadata></package>");
        var native = DotNetPackageResults.NativeProperties
            .Where(name => name != "MSBuildProjectFullPath")
            .ToDictionary(name => name, _ => "", StringComparer.Ordinal);
        native["TargetFramework"] = official ? "" : "net10.0";
        native["NETCoreSdkVersion"] = "10.0.401";
        native["IsPackable"] = native["GenerateDocumentationFile"] = "true";
        native["DocumentationFile"] = official
            ? "" : "obj/Release/Hcoona.ReleaseSmoke.GithubPackages.xml";
        native["PackageReadmeFile"] = "README.md";
        native["PackageId"] = native["AssemblyName"] = unit.PackageId;
        native["NuGetPackageVersion"] = native["PackageVersion"] = "1.0.0";
        native["AssemblyVersion"] = native["AssemblyFileVersion"] = "1.0.0.0";
        native["AssemblyInformationalVersion"] = "1.0.0";
        var subject = new ReleaseSubject(new string('a', 40),
            official ? "refs/heads/main" : "refs/heads/development", unit.Id, unit.Declaration,
            official ? "official" : "buddy",
            official ? "nugetorg" : "github-packages-nuget", dryRun);
        var prepared = new PreparedRelease(subject,
            [new("package", "original", "nuget-package", unit.PackageId,
                "1.0.0", "original.nupkg", new FileInfo(Archive).Length,
                DotNetPackageArchive.Digest(Archive))], [.. NuGetPreparation.Required],
            NuGetPreparation.Required.Select(id => new ReleaseCheck(id,
                ReleaseCheckStatus.Passed)).ToArray());
        Preparation = new(prepared, native,
            [new("pack", new("dotnet", Root, ["pack"], 60),
                new(NativeTermination.Exited, 0, "controlled native pack", "", 0.1, null))], []);
        // Do not bypass preparation or original archive validation in caller tests.
        _ = NuGetDestinationAdapter.Original(Preparation, Root);
    }

    internal ReleaseObservationResult Observe(ReleaseFileState state,
        ReleaseCheckStatus qualification = ReleaseCheckStatus.Passed)
    {
        ReleaseFile file = Prepared.Files[0];
        string[] required = Prepared.Subject.Destination == "nugetorg"
            ? NuGetOrgObservation.RequiredChecks : GithubNuGetObservation.RequiredChecks;
        return new(new(Prepared.Subject,
            [new(file.Output, file.Name, file.Version, state,
                state == ReleaseFileState.Exact ? file.Size : null,
                state == ReleaseFileState.Exact ? file.Sha256 : null)],
            required, [.. required.Select((id, i) => new ReleaseCheck(id,
                i == 0 ? qualification : ReleaseCheckStatus.Passed))]),
            []);
    }

    internal string Write<T>(string name, T value, JsonTypeInfo<T> type) =>
        WriteText(name, JsonSerializer.Serialize(value, type));

    internal string WriteText(string name, string value)
    {
        string path = Path.Combine(Root, name);
        File.WriteAllText(path, value);
        return path;
    }

    public void Dispose()
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                Directory.Delete(Root, true);
                return;
            }
            catch (IOException exception) when (OperatingSystem.IsWindows() &&
                (exception.HResult == unchecked((int)0x80070020) ||
                    exception.HResult == unchecked((int)0x80070021)) &&
                elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(50);
            }
        }
    }

    internal sealed class UploadTransport(HttpStatusCode status) : HttpMessageHandler
    {
        internal int Sends { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Assert.AreEqual(HttpMethod.Put, request.Method);
            Assert.AreEqual(NuGetPublisher.GithubEndpoint, request.RequestUri!.AbsoluteUri);
            Sends++;
            return Task.FromResult(new HttpResponseMessage(status)
            { Content = new ByteArrayContent([]), RequestMessage = request });
        }
    }
}
