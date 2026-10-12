using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace WorkflowDelivery.Ecosystems;

// Buffer one bounded response so native readers cannot outlive its body-inclusive deadline.
internal sealed class NuGetConsumerReadHandler(string credential, HttpMessageHandler transport,
    CancellationToken parent) : DelegatingHandler(transport)
{
    private int reads;
    internal int Reads => reads;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(parent, cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        Uri initial = request.RequestUri ?? throw new InvalidDataException("Missing NuGet URI.");
        if (request.Method != HttpMethod.Get || !initial.IsAbsoluteUri ||
            initial.Scheme != "https" || initial.Host != "nuget.pkg.github.com" ||
            initial.Port != 443 || initial.UserInfo.Length != 0 || initial.Query.Length != 0 ||
            initial.Fragment.Length != 0 ||
            !initial.AbsolutePath.StartsWith("/hcoona/", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported remote NuGet read.");
        bool archive = initial.AbsolutePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
        int limit = archive ? 25 * 1024 * 1024 : 2 * 1024 * 1024;
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + credential)));
        request.Headers.AcceptEncoding.Clear();
        request.Headers.AcceptEncoding.ParseAdd("identity");
        HttpResponseMessage response = await SendBoundedAsync(request, timeout.Token);
        try
        {
            int status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
            {
                string[] locations = response.Headers.NonValidated.TryGetValues("Location",
                    out var values) ? values.ToArray() : [];
                if (!archive || status is not (301 or 302) ||
                    locations is not [string location] || string.IsNullOrWhiteSpace(location))
                    throw new InvalidDataException("Unsupported native consumer redirect.");
                Uri storage = Storage(location);
                response.Dispose();
                using var redirected = new HttpRequestMessage(HttpMethod.Get, storage);
                redirected.Headers.AcceptEncoding.ParseAdd("identity");
                response = await SendBoundedAsync(redirected, timeout.Token);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new InvalidDataException("Native consumer storage did not return 200.");
            }
            else if (archive && status != 200)
                throw new InvalidDataException(
                    "Native consumer direct archive did not return 200.");
            if (response.Headers.NonValidated.Contains("Location") ||
                response.Content.Headers.ContentEncoding
                    .Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported native consumer response.");
            long? length = response.Content.Headers.ContentLength;
            if (length > limit) throw new IOException("Native consumer body exceeds its limit.");
            using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            byte[] block = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(block, timeout.Token)) != 0)
            {
                if (buffer.Length + count > limit)
                    throw new IOException("Native consumer body exceeds its limit.");
                await buffer.WriteAsync(block.AsMemory(0, count), timeout.Token);
            }
            if (length is not null && length != buffer.Length)
                throw new IOException("Incomplete native consumer body.");
            timeout.Token.ThrowIfCancellationRequested();
            var content = new ByteArrayContent(buffer.ToArray());
            foreach (var header in response.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content.Dispose();
            response.Content = content;
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendBoundedAsync(HttpRequestMessage request,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref reads) > 80)
            throw new IOException("Native consumer read allowance exhausted.");
        HttpResponseMessage response = await base.SendAsync(request, token);
        if (response.RequestMessage?.RequestUri is { } actual && actual != request.RequestUri)
        {
            response.Dispose();
            throw new InvalidDataException("Unexpected automatic native consumer redirect.");
        }
        return response;
    }

    private Uri Storage(string raw)
    {
        string decoded = Uri.UnescapeDataString(raw);
        string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + credential));
        if (new[] { credential, basic }.Any(secret => raw.Contains(secret,
                StringComparison.Ordinal) || decoded.Contains(secret, StringComparison.Ordinal)) ||
            !raw.StartsWith("https://", StringComparison.Ordinal) ||
            raw.Any(c => c is < '!' or > '~' or '\\' or '#'))
            throw new InvalidDataException("Unsupported native consumer storage location.");
        string authority = raw[8..].Split('/', '?')[0].ToLowerInvariant();
        string[] labels = authority.Split('.');
        if (authority.Length is 0 or > 253 || authority == "api.github.com" ||
            labels.Any(label => label.Length is 0 or > 63 ||
                !char.IsAsciiLetterOrDigit(label[0]) || !char.IsAsciiLetterOrDigit(label[^1]) ||
                label.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) ||
            labels.All(label => label.All(char.IsAsciiDigit) ||
                (label.StartsWith("0x", StringComparison.Ordinal) && label.Length > 2 &&
                    label[2..].All(char.IsAsciiHexDigit))))
            throw new InvalidDataException("Unsupported native consumer storage DNS authority.");
        return new(raw, new UriCreationOptions
            { DangerousDisablePathAndQueryCanonicalization = true });
    }
}
