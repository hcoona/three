using System.Net.Http.Headers;

namespace WorkflowDelivery.Tests.Release;

internal sealed class ScriptedHttpHandler(
    Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond,
    bool captureBody = false) : HttpMessageHandler
{
    internal sealed record Request(Uri Uri, HttpMethod Method, HttpRequestHeaders Headers,
        string? ContentType, string Body);

    internal ScriptedHttpHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond,
        bool captureBody = false)
        : this((request, index, _) => Task.FromResult(respond(request, index)), captureBody) { }

    internal List<Request> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int index = Requests.Count;
        string body = captureBody && request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken) : "";
        Requests.Add(new(request.RequestUri!, request.Method, request.Headers,
            request.Content?.Headers.ContentType?.MediaType, body));
        return await respond(request, index, cancellationToken);
    }
}
