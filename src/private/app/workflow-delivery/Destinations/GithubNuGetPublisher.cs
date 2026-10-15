using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record NuGetPushResult(bool ReturnedSuccessfully, bool Cancelled,
    string? FailureType, double DurationSeconds);

// Official NuGet owns upload construction and its bounded retries. This component
// constrains the selected original and connects cancellation to the actual transport.
internal static class GithubNuGetPublisher
{
    internal const string Endpoint = "https://nuget.pkg.github.com/hcoona/";
    private const string Package = "Hcoona.ReleaseSmoke.GithubPackages";

    internal static async Task<NuGetPushResult> PushAsync(PreparedRelease prepared,
        string originals, string endpoint, string credential, CancellationToken cancellation,
        HttpMessageHandler? controlledTransport = null)
    {
        if (prepared.Subject.DryRun || prepared.Subject.Channel != "buddy" ||
            prepared.Subject.Destination != "github-packages-nuget" || endpoint != Endpoint ||
            prepared.Files is not [{ Kind: "nuget-package", Size: <= 25 * 1024 * 1024 }] ||
            string.IsNullOrEmpty(credential) || credential.Any(c => c is '\r' or '\n' or '\0') ||
            !Path.IsPathFullyQualified(originals))
            throw new InvalidDataException("Unsupported or unqualified native NuGet upload.");
        ReleaseFile file = prepared.Files[0];
        string path = Path.GetFullPath(Path.Combine(originals, file.Path));
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(originals));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, comparison) ||
            !File.Exists(path) || !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Missing regular original NuGet package.");
        for (FileSystemInfo? item = new FileInfo(path); item is not null; item = item switch
            { FileInfo value => value.Directory, DirectoryInfo value => value.Parent, _ => null })
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked original NuGet package path.");
        var identity = new PackageIdentity(Package, NuGetVersion.Parse(file.Version));
        if (!PackageIdentityComparer.Default.Equals(identity,
                new PackageIdentity(file.Name, NuGetVersion.Parse(file.Version))) ||
            new FileInfo(path).Length != file.Size || DotNetPackageArchive.Digest(path) !=
                file.Sha256 || !PackageIdentityComparer.Default.Equals(identity,
                DotNetPackageArchive.Identity(path)))
            throw new InvalidDataException("Original NuGet package identity or bytes changed.");
        cancellation.ThrowIfCancellationRequested();
        using var standard = new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            UseDefaultCredentials = false, AutomaticDecompression = DecompressionMethods.None,
        };
        using var handler = new PublishHandler(credential, cancellation)
            { InnerHandler = controlledTransport ?? standard };
        var resource = new HttpHandlerResourceV3(standard, handler);
        using var source = new HttpSource(new PackageSource(endpoint),
            () => Task.FromResult<HttpHandlerResource>(resource), NullThrottle.Instance);
        var publisher = new PackageUpdateResource(endpoint, source);
        var elapsed = Stopwatch.StartNew();
        try
        {
            // Await completion even after cancellation; abandoning an await does not stop writes.
            await publisher.PushAsync([path], symbolSource: "", timeoutInSecond: 120,
                disableBuffering: false, getApiKey: _ => credential, getSymbolApiKey: _ => "",
                noServiceEndpoint: true, skipDuplicate: false, allowSnupkg: false,
                allowInsecureConnections: false, log: NullLogger.Instance);
            return new(true, cancellation.IsCancellationRequested, null,
                elapsed.Elapsed.TotalSeconds);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or
            OperationCanceledException or InvalidOperationException or ArgumentException)
        {
            return new(false, cancellation.IsCancellationRequested, error.GetType().Name,
                elapsed.Elapsed.TotalSeconds);
        }
    }

    private sealed class PublishHandler(string credential, CancellationToken parent)
        : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellation)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(parent,
                cancellation);
            linked.Token.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Put || request.RequestUri?.AbsoluteUri != Endpoint)
                throw new InvalidOperationException("Unsupported native NuGet upload endpoint.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + credential)));
            HttpResponseMessage response = await base.SendAsync(request, linked.Token);
            if (response.Content is not null)
                response.Content = new CancelledContent(response.Content, parent);
            return response;
        }
    }

    private sealed class CancelledContent : HttpContent
    {
        private readonly HttpContent original;
        private readonly CancellationToken parent;
        internal CancelledContent(HttpContent original, CancellationToken parent)
        {
            this.original = original;
            this.parent = parent;
            foreach (var header in original.Headers)
                Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override bool TryComputeLength(out long length)
        {
            length = Headers.ContentLength ?? 0;
            return Headers.ContentLength.HasValue;
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream,
            TransportContext? context, CancellationToken cancellation)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(parent,
                cancellation);
            await original.CopyToAsync(stream, context, linked.Token);
        }
        protected override async Task<Stream> CreateContentReadStreamAsync()
            => await CreateContentReadStreamAsync(CancellationToken.None);
        protected override async Task<Stream> CreateContentReadStreamAsync(
            CancellationToken cancellation)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(parent,
                cancellation);
            return new CancelledStream(await original.ReadAsStreamAsync(linked.Token), parent);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) original.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class CancelledStream(Stream original, CancellationToken parent) : Stream
    {
        public override bool CanRead => original.CanRead;
        public override bool CanSeek => original.CanSeek;
        public override bool CanWrite => false;
        public override long Length => original.Length;
        public override long Position
        { get => original.Position; set => original.Position = value; }
        public override void Flush() => original.Flush();
        public override long Seek(long offset, SeekOrigin origin) => original.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            parent.ThrowIfCancellationRequested();
            return original.Read(buffer, offset, count);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellation) => ReadAsync(buffer.AsMemory(offset, count),
                cancellation).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellation = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(parent,
                cancellation);
            return await original.ReadAsync(buffer, linked.Token);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) original.Dispose();
            base.Dispose(disposing);
        }
    }
}
