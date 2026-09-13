using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace WorkflowDeliveryV3NuGetConsumer;

internal sealed class BoundedHttpHandler : DelegatingHandler
{
    private readonly ConsumerRequest _request;
    private readonly byte[][] _secrets;
    private readonly string _basic;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _deadline;
    private bool _stopped;

    internal BoundedHttpHandler(
        ConsumerRequest request,
        string credential,
        HttpMessageHandler inner
    )
        : base(inner)
    {
        request.Validate();
        ConsumerRequest.Require(
            !string.IsNullOrEmpty(credential)
                && !credential.Contains('\r')
                && !credential.Contains('\n'),
            "Invalid read credential."
        );
        _request = request;
        _deadline = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds));
        _basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("hcoona:" + credential));
        _secrets = [Encoding.UTF8.GetBytes(credential), Encoding.UTF8.GetBytes(_basic)];
    }

    internal int Requests { get; private set; }
    internal long ResponseBytes { get; private set; }
    internal bool PackageReturned { get; private set; }

    internal void Check(ReadOnlySpan<byte> content)
    {
        byte[] decoded = Encoding.UTF8.GetBytes(
            Uri.UnescapeDataString(Encoding.UTF8.GetString(content))
        );
        foreach (byte[] secret in _secrets)
        {
            ConsumerRequest.Require(
                content.IndexOf(secret) < 0 && decoded.AsSpan().IndexOf(secret) < 0,
                "Read credential reflected in consumer evidence."
            );
        }
    }

    private void Check(string text) => Check(Encoding.UTF8.GetBytes(text));

    internal void Save(string name, ReadOnlySpan<byte> content)
    {
        Check(content);
        using var stream = new FileStream(
            Path.Combine(_request.EvidencePath, name),
            FileMode.CreateNew
        );
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage message,
        CancellationToken cancellationToken
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _deadline.Token
        );
        cancellationToken = linked.Token;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ConsumerRequest.Require(
                !_stopped
                    && Requests < _request.MaximumRequests
                    && ResponseBytes < _request.MaximumResponseBytes,
                "Consumer HTTP allowance exhausted or stopped."
            );
            cancellationToken.ThrowIfCancellationRequested();
            string url = message.RequestUri?.AbsoluteUri ?? string.Empty;
            Check(url);
            ConsumerRequest.Require(
                message.Method == HttpMethod.Get
                    && message.Content is null
                    && (
                        url == ConsumerRequest.ServiceIndex
                        || url == _request.PackageUrl
                        || url
                            == _request.PackageBaseAddress
                                + ConsumerRequest.NormalizedId
                                + "/index.json"
                    ),
                "Consumer HTTP request is outside the selected resource scope."
            );
            int index = ++Requests;
            Save(
                $"{index:D3}-reserved.json",
                Encoding.UTF8.GetBytes(
                    new JsonObject
                    {
                        ["method"] = "GET",
                        ["url"] = url,
                        ["startedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                        ["maximumRemainingResponseBytes"] =
                            _request.MaximumResponseBytes - ResponseBytes,
                    }.ToJsonString()
                )
            );
            message.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basic);
            message.Headers.AcceptEncoding.Clear();
            using HttpResponseMessage response = await base.SendAsync(message, cancellationToken)
                .ConfigureAwait(false);
            foreach (
                KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(
                    response.Content.Headers
                )
            )
            {
                Check(header.Key);
                foreach (string value in header.Value)
                {
                    Check(value);
                }
            }
            Check(response.ReasonPhrase ?? string.Empty);
            ConsumerRequest.Require(
                response.Content.Headers.ContentEncoding.Count == 0,
                "Encoded consumer response is unsupported by the byte allowance."
            );
            using Stream content = await response
                .Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            while (true)
            {
                int allowance = (int)
                    Math.Min(chunk.Length, _request.MaximumResponseBytes - ResponseBytes);
                int count = await content
                    .ReadAsync(chunk.AsMemory(0, allowance), cancellationToken)
                    .ConfigureAwait(false);
                ResponseBytes += count;
                // EOF must be established within the declared total. A final
                // allowance byte is an overflow sentinel, not permission to
                // read again after exhausting that total.
                ConsumerRequest.Require(
                    ResponseBytes < _request.MaximumResponseBytes,
                    "Consumer response allowance exhausted before complete response."
                );
                if (count == 0)
                {
                    break;
                }
                buffer.Write(chunk, 0, count);
            }
            byte[] body = buffer.ToArray();
            Check(body);
            cancellationToken.ThrowIfCancellationRequested();
            Save($"{index:D3}-body.bin", body);
            var headers = new JsonObject();
            foreach (
                string key in new[]
                {
                    "Date",
                    "ETag",
                    "Content-Type",
                    "Content-Length",
                    "X-GitHub-Request-Id",
                }
            )
            {
                if (
                    response.Headers.TryGetValues(key, out IEnumerable<string>? values)
                    || response.Content.Headers.TryGetValues(key, out values)
                )
                {
                    headers[key.ToLowerInvariant()] = new JsonArray(
                        values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()
                    );
                }
            }
            Save(
                $"{index:D3}-response.json",
                Encoding.UTF8.GetBytes(
                    new JsonObject
                    {
                        ["status"] = (int)response.StatusCode,
                        ["headers"] = headers,
                        ["sha256"] = ConsumerRequest.Sha256(body),
                        ["bytes"] = body.Length,
                        ["completedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    }.ToJsonString()
                )
            );
            ConsumerRequest.Require(
                response.StatusCode == HttpStatusCode.OK,
                "Consumer read did not return a complete successful response."
            );
            if (url == ConsumerRequest.ServiceIndex)
            {
                ConsumerRequest.Require(
                    ConsumerRequest.Sha256(body) == _request.ServiceIndexSha256,
                    "Consumer service index changed."
                );
            }
            if (url == _request.PackageUrl)
            {
                ConsumerRequest.Require(
                    ConsumerRequest.Sha256(body) == _request.PackageSha256,
                    "Consumer did not receive the admitted original package."
                );
                PackageReturned = true;
            }
            var returned = new HttpResponseMessage(response.StatusCode)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = message,
            };
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                returned.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                returned.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return returned;
        }
        catch
        {
            _stopped = true;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _deadline.Dispose();
            _gate.Dispose();
        }
        base.Dispose(disposing);
    }
}
